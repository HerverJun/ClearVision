using System.Text.Json;
using System.Threading.Channels;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Continuous;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Diagnostics;
using ClearVision.Product.Infrastructure.Replay;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Continuous;

public sealed class ContinuousInspectionWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CameraStreamRestartDelay = TimeSpan.FromMilliseconds(500);
    private readonly ILogger _logger;

    private static string SanitizeLogValue(object? value)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    public ContinuousInspectionWorker(ILogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(
        Guid projectId,
        Guid sessionId,
        OperatorFlow flow,
        string cameraId,
        ContinuousInspectionConfig config,
        ContinuousInspectionMode mode,
        ICameraFrameStreamCoordinator streamCoordinator,
        IFlowExecutionService flowExecution,
        IInspectionResultChannelWriter resultChannelWriter,
        IInspectionImagePersistenceService imagePersistenceService,
        IInspectionEventBus eventBus,
        CancellationToken cancellationToken,
        Func<ContinuousInspectionMode>? resolveCurrentMode = null,
        IImageCacheRepository? imageCacheRepository = null,
        IProjectVariableSession? projectVariableSession = null,
        ProjectVariableBindingIndex? projectVariableBindingIndex = null,
        ProjectVariableCommitHandler? projectVariableCommitHandler = null,
        long persistenceRevision = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        ArgumentNullException.ThrowIfNull(config);
        config.Normalize();

        var executionSnapshot = new ExecutionSnapshot(
            projectId,
            flow,
            persistenceRevision,
            mode == ContinuousInspectionMode.Shadow
                ? ExecutionSnapshotSource.ShadowCandidate
                : ExecutionSnapshotSource.PersistedProject,
            mode == ContinuousInspectionMode.Shadow
                ? ExecutionRunMode.ShadowCandidate
                : ExecutionRunMode.FormalPrimary,
            shadowRole: mode == ContinuousInspectionMode.Shadow
                ? ShadowExecutionRole.Candidate
                : ShadowExecutionRole.None);

        await using var scheduler = new InferenceScheduler(config.SchedulerQueueLength);
        using var detector = new FrameDifferenceArrivalDetector();
        var tracker = new LightweightTracker(new LightweightTrackerOptions(
            MaxSequenceGap: Math.Max(2, config.PreEventFrames + config.PostEventFrames + 1),
            TrackTimeoutMs: Math.Max(100, config.MaxLatencyMs * 4),
            FreezeAfterSignals: 1));
        var consensus = new TrackConsensusJudge(config.MinConsensusFrames, config.ConsensusThreshold);
        var metrics = new ContinuousMetricsCollector();
        var replay = new FrameReplayRecorder(Path.Combine(AppContext.BaseDirectory, "continuous-replay"));

        scheduler.ResultReady += async scheduled =>
        {
            metrics.RecordInferenceCompleted(scheduled.Latency);
            if (scheduled.Error != null)
            {
                _logger.LogWarning(
                    scheduled.Error,
                    "[ContinuousInspection] Flow inference failed. CameraId={CameraId}, Sequence={Sequence}, TrackId={TrackId}",
                    cameraId,
                    scheduled.Frame.Sequence,
                    scheduled.TrackId);
                var failure = BuildExecutionFailureResult(
                    projectId,
                    sessionId,
                    scheduled,
                    "CONTINUOUS_INFERENCE_EXCEPTION",
                    scheduled.Error.Message,
                    mode);
                failure.SetExecutionTraceability(executionSnapshot, null, sessionId);
                await PublishOrSuppressAsync(
                    failure,
                    mode,
                    imagePersistenceService,
                    imageCacheRepository,
                    resultChannelWriter,
                    eventBus,
                    projectId,
                    sessionId,
                    cancellationToken);
                return;
            }

            if (scheduled.Result == null)
            {
                var failure = BuildExecutionFailureResult(
                    projectId,
                    sessionId,
                    scheduled,
                    "CONTINUOUS_EMPTY_INFERENCE_RESULT",
                    "Continuous inference returned no flow result.",
                    mode);
                failure.SetExecutionTraceability(executionSnapshot, null, sessionId);
                await PublishOrSuppressAsync(
                    failure,
                    mode,
                    imagePersistenceService,
                    imageCacheRepository,
                    resultChannelWriter,
                    eventBus,
                    projectId,
                    sessionId,
                    cancellationToken);
                return;
            }

            var outputData = scheduled.Result.OutputData ?? new Dictionary<string, object>();
            scheduled.Result.OutputData = outputData;
            var frameOutcome = InspectionOutcomeResolver.Resolve(scheduled.Result, flow);
            InspectionOutcomeResolver.SetDiagnostics(outputData, frameOutcome);
            if (frameOutcome.Execution != ExecutionOutcome.Succeeded)
            {
                var failure = BuildFrameResult(projectId, sessionId, scheduled, frameOutcome, mode);
                failure.SetExecutionTraceability(executionSnapshot, null, sessionId);
                await PublishOrSuppressAsync(
                    failure,
                    mode,
                    imagePersistenceService,
                    imageCacheRepository,
                    resultChannelWriter,
                    eventBus,
                    projectId,
                    sessionId,
                    cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(scheduled.TrackId))
            {
                var failure = BuildExecutionFailureResult(
                    projectId,
                    sessionId,
                    scheduled,
                    "CONTINUOUS_TRACK_ID_MISSING",
                    "Continuous inference completed without a track id.",
                    mode);
                failure.SetExecutionTraceability(executionSnapshot, null, sessionId);
                await PublishOrSuppressAsync(
                    failure,
                    mode,
                    imagePersistenceService,
                    imageCacheRepository,
                    resultChannelWriter,
                    eventBus,
                    projectId,
                    sessionId,
                    cancellationToken);
                return;
            }

            var decision = consensus.AddFrame(new TrackFrameJudgment(
                scheduled.TrackId,
                scheduled.Frame.Sequence,
                outputData,
                ResolveConfidence(outputData),
                frameOutcome,
                scheduled.Frame.EffectiveCorrelationId,
                ResolveOutputImage(outputData),
                scheduled.Latency));
            if (decision == null)
            {
                return;
            }

            metrics.RecordDecisionFinalized();
            await PersistReplayIfNeededAsync(streamCoordinator, cameraId, config, replay, decision, cancellationToken);

            var result = BuildInspectionResult(projectId, sessionId, decision, mode);
            result.SetExecutionTraceability(executionSnapshot, null, sessionId);
            AppendRuntimeMetrics(
                result,
                decision.ResultFrame?.OutputData ?? new Dictionary<string, object>(),
                metrics.Snapshot(),
                scheduler.Snapshot(),
                streamCoordinator.SnapshotFrameBufferStats(cameraId));
            await PublishOrSuppressAsync(
                result,
                mode,
                imagePersistenceService,
                imageCacheRepository,
                resultChannelWriter,
                eventBus,
                projectId,
                sessionId,
                cancellationToken);
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var restartRequested = false;
            var lease = await streamCoordinator.AcquireStreamLeaseAsync(cameraId, cancellationToken);

            // 1. 创建基于 Bounded 背压的帧收集 Channel (包含 Frame 和 TrackId 元组)
            var frameChannel = Channel.CreateBounded<(ClearVision.Product.Core.Streaming.FrameEnvelope Frame, string TrackId)>(new BoundedChannelOptions(config.SchedulerQueueLength > 0 ? config.SchedulerQueueLength : 100)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest // 满时丢弃最老，实现优雅背压
            });

            // 2. 启动单一的工控后台处理流水线 Task，专门负责本连接周期内的异步帧收集与推理排程
            var pipelineTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var item in frameChannel.Reader.ReadAllAsync(cancellationToken))
                    {
                        try
                        {
                            var frames = await CollectDecisionFramesAsync(
                                streamCoordinator,
                                lease,
                                cameraId,
                                item.Frame,
                                config,
                                cancellationToken);

                            foreach (var candidate in frames)
                            {
                                var scheduled = scheduler.TrySchedule(new ScheduledInferenceItem(
                                    candidate,
                                    item.TrackId,
                                     (envelope, ct) =>
                                     {
                                         var inputs = new Dictionary<string, object> { ["ProvidedFrameEnvelope"] = envelope };
                                         var executionFlow = executionSnapshot.CreateExecutionFlow();
                                         var policyViolations = executionSnapshot.SideEffectPolicy.Validate(executionFlow);
                                         if (policyViolations.Count > 0)
                                         {
                                             return Task.FromResult(FlowExecutionResult.SideEffectPolicyRejected(policyViolations));
                                         }

                                         if (projectVariableSession == null || projectVariableBindingIndex == null)
                                         {
                                            return flowExecution.ExecuteFlowAsync(executionFlow, inputs, cancellationToken: ct);
                                        }

                                        var isPreview = mode != ContinuousInspectionMode.Primary;
                                        var context = new ProjectVariableExecutionContext(
                                            projectVariableSession,
                                            projectVariableBindingIndex,
                                            Guid.NewGuid(),
                                            isPreview,
                                            isPreview ? null : projectVariableCommitHandler);
                                        return flowExecution.ExecuteFlowAsync(executionFlow, inputs, context, cancellationToken: ct);
                                    }));
                                if (scheduled)
                                {
                                    metrics.RecordInferenceScheduled();
                                }
                                else
                                {
                                    metrics.RecordInferenceDropped();
                                }
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "[ContinuousInspection] Pipeline frame collection and scheduling failed. CameraId={CameraId}, TrackId={TrackId}", SanitizeLogValue(cameraId), SanitizeLogValue(item.TrackId));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, cancellationToken);

            try
            {
                long? lastSequence = null;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (resolveCurrentMode?.Invoke() is { } currentMode &&
                        (currentMode == ContinuousInspectionMode.Disabled || currentMode != mode))
                    {
                        _logger.LogInformation(
                            "[ContinuousInspection] Loop stopped by configuration change. CameraId={CameraId}, PreviousMode={PreviousMode}, CurrentMode={CurrentMode}",
                            SanitizeLogValue(cameraId),
                            mode,
                            currentMode);
                        return;
                    }

                    var frame = await streamCoordinator.WaitForNextFrameEnvelopeAsync(lease, lastSequence, cancellationToken);
                    lastSequence = frame.Sequence;
                    metrics.RecordFrameReceived();

                    if (frame.Sequence % config.DetectEveryNFrames != 0)
                    {
                        continue;
                    }

                    var signal = detector.Update(frame);
                    if (signal == null)
                    {
                        continue;
                    }

                    metrics.RecordArrivalSignal();
                    var track = tracker.Update(signal);
                    if (track.IsNew)
                    {
                        metrics.RecordTrackCreated();
                    }

                    // 极速、同步、非阻塞地将当前帧与 TrackId 写入 Channel 流水线，0% 线程池开销
                    frameChannel.Writer.TryWrite((frame, track.TrackId));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidOperationException ex) when (IsRecoverableCameraStreamFault(ex) && !cancellationToken.IsCancellationRequested)
            {
                restartRequested = true;
                _logger.LogWarning(
                    ex,
                    "[ContinuousInspection] Shared camera stream faulted; releasing lease and restarting. CameraId={CameraId}, Mode={Mode}",
                    SanitizeLogValue(cameraId),
                    mode);
            }
            finally
            {
                // 关闭 Channel 写入端并安全等待流水线结束，彻底终结任何并发生命周期竞争
                frameChannel.Writer.Complete();
                try
                {
                    await pipelineTask;
                }
                catch
                {
                }

                await streamCoordinator.ReleaseStreamLeaseAsync(lease);
                var snapshot = metrics.Snapshot();
                var logMessage = restartRequested
                    ? "[ContinuousInspection] Stream lease released for restart. CameraId={CameraId}, Mode={Mode}, Frames={Frames}, Signals={Signals}, Tracks={Tracks}, Scheduled={Scheduled}, Completed={Completed}, Decisions={Decisions}, AvgLatencyMs={AvgLatencyMs:F1}"
                    : "[ContinuousInspection] Loop stopped. CameraId={CameraId}, Mode={Mode}, Frames={Frames}, Signals={Signals}, Tracks={Tracks}, Scheduled={Scheduled}, Completed={Completed}, Decisions={Decisions}, AvgLatencyMs={AvgLatencyMs:F1}";
                _logger.LogInformation(
                    logMessage,
                    SanitizeLogValue(cameraId),
                    mode,
                    snapshot.FramesReceived,
                    snapshot.ArrivalSignals,
                    snapshot.TracksCreated,
                    snapshot.ScheduledInferences,
                    snapshot.CompletedInferences,
                    snapshot.FinalDecisions,
                    snapshot.AverageInferenceLatencyMs);
            }

            if (!restartRequested)
            {
                break;
            }

            await Task.Delay(CameraStreamRestartDelay, cancellationToken);
        }
    }

    private static bool IsRecoverableCameraStreamFault(InvalidOperationException exception)
    {
        return exception.Message.Contains("stopped producing frames", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<ClearVision.Product.Core.Streaming.FrameEnvelope>> CollectDecisionFramesAsync(
        ICameraFrameStreamCoordinator streamCoordinator,
        CameraStreamLease lease,
        string cameraId,
        ClearVision.Product.Core.Streaming.FrameEnvelope centerFrame,
        ContinuousInspectionConfig config,
        CancellationToken cancellationToken)
    {
        var frames = new List<ClearVision.Product.Core.Streaming.FrameEnvelope>(config.PreEventFrames + config.PostEventFrames + 1);
        var windowFrames = streamCoordinator.GetFrameEnvelopeWindow(cameraId, centerFrame.Sequence, config.PreEventFrames, 0);
        var hasCenterFrame = false;

        foreach (var frame in windowFrames)
        {
            if (frame.Sequence == centerFrame.Sequence)
            {
                hasCenterFrame = true;
            }

            frames.Add(frame);
        }

        if (!hasCenterFrame)
        {
            frames.Add(centerFrame);
        }

        var lastSequence = centerFrame.Sequence;
        for (var index = 0; index < config.PostEventFrames; index++)
        {
            var postFrame = await streamCoordinator.WaitForNextFrameEnvelopeAsync(lease, lastSequence, cancellationToken);
            lastSequence = postFrame.Sequence;
            frames.Add(postFrame);
        }

        return frames;
    }

    private static async Task PersistReplayIfNeededAsync(
        ICameraFrameStreamCoordinator streamCoordinator,
        string cameraId,
        ContinuousInspectionConfig config,
        FrameReplayRecorder replay,
        TrackDecision decision,
        CancellationToken cancellationToken)
    {
        if (config.SaveReplayOnNgOnly && decision.Status != InspectionStatus.NG)
        {
            return;
        }

        if (decision.ResultFrame == null)
        {
            return;
        }

        var frames = streamCoordinator.GetFrameEnvelopeWindow(
            cameraId,
            decision.ResultFrame.Sequence,
            config.PreEventFrames,
            config.PostEventFrames);
        if (frames.Count == 0)
        {
            return;
        }

        await replay.SaveTrackAsync(decision.TrackId, frames, decision, cancellationToken);
    }

    private static InspectionResult BuildInspectionResult(
        Guid projectId,
        Guid sessionId,
        TrackDecision decision,
        ContinuousInspectionMode mode)
    {
        // The consensus decision owns its selected frame. Never fall back to the
        // callback that happened to close the window: its data can support the
        // opposite conclusion.
        var resultFrame = decision.ResultFrame;
        var outputData = resultFrame?.OutputData ?? new Dictionary<string, object>();
        outputData["ContinuousInspection"] = new Dictionary<string, object?>
        {
            ["Mode"] = mode.ToString(),
            ["TrackId"] = decision.TrackId,
            ["Status"] = decision.Status.ToString(),
            ["FrameCount"] = decision.FrameCount,
            ["OkVotes"] = decision.OkVotes,
            ["NgVotes"] = decision.NgVotes,
            ["BestSequence"] = decision.BestSequence,
            ["RepresentativeSequence"] = decision.RepresentativeFrame?.Sequence,
            ["ConsensusScore"] = decision.ConsensusScore,
            ["CorrelationId"] = resultFrame?.CorrelationId,
            ["LatencyMs"] = resultFrame?.Latency.TotalMilliseconds ?? 0,
            ["Confidence"] = resultFrame?.Confidence
        };

        var result = new InspectionResult(projectId);
        var consensusOutcome = decision.Outcome ?? new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            decision.Status == InspectionStatus.OK ? DecisionOutcome.Ok : DecisionOutcome.Ng,
            "ContinuousConsensus",
            "CONTINUOUS_CONSENSUS_REACHED",
            null,
            HasJudgmentSignal: true);
        InspectionOutcomeResolver.SetDiagnostics(outputData, consensusOutcome);
        result.SetOutcome(
            consensusOutcome,
            Math.Max(0, (long)(resultFrame?.Latency.TotalMilliseconds ?? 0)),
            resultFrame?.Confidence);
        result.SetTraceability(null, null, sessionId);
        if (resultFrame?.OutputImage is { Length: > 0 } outputImage)
        {
            result.SetOutputImage(outputImage);
        }
        return result;
    }

    private static InspectionResult BuildFrameResult(
        Guid projectId,
        Guid sessionId,
        ScheduledInferenceResult scheduled,
        InspectionOutcome outcome,
        ContinuousInspectionMode mode)
    {
        var outputData = scheduled.Result?.OutputData ?? new Dictionary<string, object>();
        outputData["ContinuousInspection"] = new Dictionary<string, object?>
        {
            ["Mode"] = mode.ToString(),
            ["TrackId"] = scheduled.TrackId,
            ["BestSequence"] = scheduled.Frame.Sequence,
            ["CorrelationId"] = scheduled.Frame.EffectiveCorrelationId,
            ["LatencyMs"] = scheduled.Latency.TotalMilliseconds
        };
        InspectionOutcomeResolver.SetDiagnostics(outputData, outcome);
        var result = new InspectionResult(projectId);
        result.SetOutcome(outcome, Math.Max(0, (long)scheduled.Latency.TotalMilliseconds));
        result.SetTraceability(null, null, sessionId);
        if (outputData.TryGetValue("Image", out var image) && image is byte[] imageBytes)
        {
            result.SetOutputImage(imageBytes);
        }
        AnalysisPayloadSerialization.TrySetOutputDataJson(result, outputData, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return result;
    }

    private static InspectionResult BuildExecutionFailureResult(
        Guid projectId,
        Guid sessionId,
        ScheduledInferenceResult scheduled,
        string reasonCode,
        string message,
        ContinuousInspectionMode mode) =>
        BuildFrameResult(
            projectId,
            sessionId,
            scheduled,
            new InspectionOutcome(
                ExecutionOutcome.Failed,
                DecisionOutcome.Undetermined,
                "ContinuousPrimary",
                reasonCode,
                message,
                HasJudgmentSignal: false),
            mode);

    private async Task PublishOrSuppressAsync(
        InspectionResult result,
        ContinuousInspectionMode mode,
        IInspectionImagePersistenceService imagePersistenceService,
        IImageCacheRepository? imageCacheRepository,
        IInspectionResultChannelWriter resultChannelWriter,
        IInspectionEventBus eventBus,
        Guid projectId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (mode == ContinuousInspectionMode.Primary)
        {
            await imagePersistenceService.PersistAsync(result, cancellationToken);
            await CacheResultImageAsync(imageCacheRepository, result);
            await resultChannelWriter.WriteAsync(result, cancellationToken);
            await PublishResultEventAsync(eventBus, projectId, sessionId, result, cancellationToken);
            return;
        }

        var outcome = result.GetOutcome();
        _logger.LogInformation(
            "[ContinuousInspection] Shadow outcome suppressed. Execution={Execution}, Decision={Decision}, Reason={ReasonCode}",
            outcome.Execution,
            outcome.Decision,
            outcome.ReasonCode);
    }

    private static void AppendRuntimeMetrics(
        InspectionResult result,
        Dictionary<string, object> outputData,
        ContinuousMetricsSnapshot metrics,
        InferenceSchedulerSnapshot scheduler,
        RingBufferStats buffer)
    {
        if (outputData.TryGetValue("ContinuousInspection", out var value) &&
            value is Dictionary<string, object?> continuous)
        {
            continuous["DroppedFrames"] = metrics.DroppedInferences;
            continuous["AverageInferenceLatencyMs"] = metrics.AverageInferenceLatencyMs;
            continuous["QueueDepth"] = scheduler.QueueDepth;
            continuous["BufferCapacity"] = buffer.Capacity;
            continuous["BufferCount"] = buffer.Count;
            continuous["BufferOverwrittenCount"] = buffer.OverwrittenCount;
        }

        AnalysisPayloadSerialization.TrySetOutputDataJson(result, outputData, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var analysisData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (outputData.TryGetValue("ContinuousInspection", out var continuousInspection))
        {
            analysisData["continuousInspection"] = continuousInspection;
        }

        result.SetAnalysisDataJson(JsonSerializer.Serialize(analysisData, JsonOptions));
    }

    private static async Task PublishResultEventAsync(
        IInspectionEventBus eventBus,
        Guid projectId,
        Guid sessionId,
        InspectionResult result,
        CancellationToken cancellationToken)
    {
        await eventBus.PublishAsync(new InspectionResultEvent
        {
            ProjectId = projectId,
            SessionId = sessionId,
            ResultId = result.Id,
            ImageId = result.ImageId,
            Status = result.Status.ToString(),
            ExecutionOutcome = result.GetOutcome().Execution.ToString(),
            DecisionOutcome = result.GetOutcome().Decision.ToString(),
            DecisionSource = result.GetOutcome().DecisionSource,
            ReasonCode = result.GetOutcome().ReasonCode,
            HasJudgmentSignal = result.GetOutcome().HasJudgmentSignal,
            DefectCount = result.Defects.Count,
            ProcessingTimeMs = result.ProcessingTimeMs,
            ErrorMessage = result.ErrorMessage,
            OutputImageBase64 = BuildInlineOutputImageBase64(result),
            OutputData = AnalysisPayloadSerialization.DeserializeJsonDictionary(result.OutputDataJson),
            AnalysisData = AnalysisPayloadSerialization.DeserializeJsonDictionary(result.AnalysisDataJson)
        }, cancellationToken);
    }

    private static async Task CacheResultImageAsync(IImageCacheRepository? imageCacheRepository, InspectionResult result)
    {
        if (imageCacheRepository == null || result.OutputImage == null || result.OutputImage.Length == 0)
        {
            return;
        }

        var imageId = await imageCacheRepository.AddAsync(result.OutputImage, "png");
        if (imageId != Guid.Empty)
        {
            result.SetImageId(imageId);
        }
    }

    private static string? BuildInlineOutputImageBase64(InspectionResult result)
    {
        if (result.ImageId.HasValue)
        {
            return null;
        }

        return result.OutputImage != null
            ? Convert.ToBase64String(result.OutputImage)
            : null;
    }

    private static double ResolveConfidence(Dictionary<string, object> outputData)
    {
        foreach (var key in new[] { "Confidence", "Score", "confidence", "score" })
        {
            if (outputData.TryGetValue(key, out var value) && double.TryParse(value?.ToString(), out var parsed))
            {
                return Math.Clamp(parsed, 0.0, 1.0);
            }
        }

        return 1.0;
    }

    private static byte[]? ResolveOutputImage(Dictionary<string, object> outputData) =>
        outputData.TryGetValue("Image", out var image) && image is byte[] imageBytes
            ? imageBytes
            : null;
}
