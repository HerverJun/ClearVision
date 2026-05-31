using System.Threading.Channels;
using System.Text.Json;
using Acme.Product.Application.Analysis;
using Acme.Product.Application.Services;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Continuous;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Events;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.Diagnostics;
using Acme.Product.Infrastructure.Replay;
using Acme.Product.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Continuous;

public sealed class ContinuousInspectionWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CameraStreamRestartDelay = TimeSpan.FromMilliseconds(500);
    private readonly ILogger _logger;

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
        Func<ContinuousInspectionMode>? resolveCurrentMode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        ArgumentNullException.ThrowIfNull(config);
        config.Normalize();

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
                return;
            }

            if (scheduled.Result == null || string.IsNullOrWhiteSpace(scheduled.TrackId))
            {
                return;
            }

            var outputData = scheduled.Result.OutputData ?? new Dictionary<string, object>();
            var evaluation = InspectionJudgmentResolver.DetermineStatusFromFlowOutput(outputData);
            var decision = consensus.AddFrame(new TrackFrameJudgment(
                scheduled.TrackId,
                scheduled.Frame.Sequence,
                outputData,
                ResolveConfidence(outputData)));
            if (decision == null)
            {
                return;
            }

            metrics.RecordDecisionFinalized();
            await PersistReplayIfNeededAsync(streamCoordinator, cameraId, config, replay, decision, cancellationToken);

            var result = BuildInspectionResult(projectId, sessionId, scheduled, decision, evaluation, mode);
            AppendRuntimeMetrics(
                result,
                outputData,
                metrics.Snapshot(),
                scheduler.Snapshot(),
                streamCoordinator.SnapshotFrameBufferStats(cameraId));
            if (mode == ContinuousInspectionMode.Primary)
            {
                await imagePersistenceService.PersistAsync(result, cancellationToken);
                await resultChannelWriter.WriteAsync(result, cancellationToken);
                await PublishResultEventAsync(eventBus, projectId, sessionId, result, cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "[ContinuousInspection] Shadow decision suppressed. CameraId={CameraId}, TrackId={TrackId}, Status={Status}, BestSequence={BestSequence}",
                    cameraId,
                    decision.TrackId,
                    decision.Status,
                    decision.BestSequence);
            }
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var restartRequested = false;
            var lease = await streamCoordinator.AcquireStreamLeaseAsync(cameraId, cancellationToken);

            // 1. 创建基于 Bounded 背压的帧收集 Channel (包含 Frame 和 TrackId 元组)
            var frameChannel = Channel.CreateBounded<(Acme.Product.Core.Streaming.FrameEnvelope Frame, string TrackId)>(new BoundedChannelOptions(config.SchedulerQueueLength > 0 ? config.SchedulerQueueLength : 100)
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
                                    (envelope, ct) => flowExecution.ExecuteFlowAsync(
                                        flow,
                                        new Dictionary<string, object> { ["ProvidedFrameEnvelope"] = envelope },
                                        cancellationToken: ct)));
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
                            _logger.LogError(ex, "[ContinuousInspection] Pipeline frame collection and scheduling failed. CameraId={CameraId}, TrackId={TrackId}", cameraId, item.TrackId);
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
                            cameraId,
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
                    cameraId,
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
                    cameraId,
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

    private static async Task<IReadOnlyList<Acme.Product.Core.Streaming.FrameEnvelope>> CollectDecisionFramesAsync(
        ICameraFrameStreamCoordinator streamCoordinator,
        CameraStreamLease lease,
        string cameraId,
        Acme.Product.Core.Streaming.FrameEnvelope centerFrame,
        ContinuousInspectionConfig config,
        CancellationToken cancellationToken)
    {
        var frames = new List<Acme.Product.Core.Streaming.FrameEnvelope>(config.PreEventFrames + config.PostEventFrames + 1);
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

        var frames = streamCoordinator.GetFrameEnvelopeWindow(
            cameraId,
            decision.BestSequence,
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
        ScheduledInferenceResult scheduled,
        TrackDecision decision,
        InspectionJudgmentEvaluation evaluation,
        ContinuousInspectionMode mode)
    {
        var outputData = scheduled.Result?.OutputData ?? new Dictionary<string, object>();
        outputData["ContinuousInspection"] = new Dictionary<string, object?>
        {
            ["Mode"] = mode.ToString(),
            ["TrackId"] = decision.TrackId,
            ["Status"] = decision.Status.ToString(),
            ["FrameCount"] = decision.FrameCount,
            ["OkVotes"] = decision.OkVotes,
            ["NgVotes"] = decision.NgVotes,
            ["BestSequence"] = decision.BestSequence,
            ["ConsensusScore"] = decision.ConsensusScore,
            ["CorrelationId"] = scheduled.Frame.EffectiveCorrelationId,
            ["LatencyMs"] = scheduled.Latency.TotalMilliseconds
        };

        var result = new InspectionResult(projectId);
        result.SetResult(
            decision.Status,
            Math.Max(0, (long)scheduled.Latency.TotalMilliseconds),
            decision.ConsensusScore,
            evaluation.Status == InspectionStatus.Error ? evaluation.StatusReason : null);
        result.SetTraceability(null, null, sessionId);
        if (outputData.TryGetValue("Image", out var image) && image is byte[] imageBytes)
        {
            result.SetOutputImage(imageBytes);
        }
        return result;
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
            continuous["LatencyMs"] = metrics.AverageInferenceLatencyMs;
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
            DefectCount = result.Defects.Count,
            ProcessingTimeMs = result.ProcessingTimeMs,
            ErrorMessage = result.ErrorMessage,
            OutputImageBase64 = result.OutputImage != null ? Convert.ToBase64String(result.OutputImage) : null,
            OutputData = AnalysisPayloadSerialization.DeserializeJsonDictionary(result.OutputDataJson),
            AnalysisData = AnalysisPayloadSerialization.DeserializeJsonDictionary(result.AnalysisDataJson)
        }, cancellationToken);
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
}
