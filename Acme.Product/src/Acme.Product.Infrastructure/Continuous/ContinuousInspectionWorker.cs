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
            AppendRuntimeMetrics(result, outputData, metrics.Snapshot(), scheduler.Snapshot(), streamCoordinator.SnapshotFrameBufferStats(cameraId));
            if (mode == ContinuousInspectionMode.Primary)
            {
                resultChannelWriter.TryWrite(result);
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

        var lease = await streamCoordinator.AcquireStreamLeaseAsync(cameraId, cancellationToken);
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
                    break;
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

                var frames = await CollectDecisionFramesAsync(
                    streamCoordinator,
                    lease,
                    cameraId,
                    frame,
                    config,
                    cancellationToken);
                foreach (var candidate in frames)
                {
                    var scheduled = scheduler.TrySchedule(new ScheduledInferenceItem(
                        candidate,
                        track.TrackId,
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await streamCoordinator.ReleaseStreamLeaseAsync(lease);
            var snapshot = metrics.Snapshot();
            _logger.LogInformation(
                "[ContinuousInspection] Loop stopped. CameraId={CameraId}, Mode={Mode}, Frames={Frames}, Signals={Signals}, Tracks={Tracks}, Scheduled={Scheduled}, Completed={Completed}, Decisions={Decisions}, AvgLatencyMs={AvgLatencyMs:F1}",
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
    }

    private static async Task<IReadOnlyList<Acme.Product.Core.Streaming.FrameEnvelope>> CollectDecisionFramesAsync(
        ICameraFrameStreamCoordinator streamCoordinator,
        CameraStreamLease lease,
        string cameraId,
        Acme.Product.Core.Streaming.FrameEnvelope centerFrame,
        ContinuousInspectionConfig config,
        CancellationToken cancellationToken)
    {
        var frames = streamCoordinator
            .GetFrameEnvelopeWindow(cameraId, centerFrame.Sequence, config.PreEventFrames, 0)
            .ToDictionary(frame => frame.Sequence);
        frames[centerFrame.Sequence] = centerFrame;

        var lastSequence = centerFrame.Sequence;
        for (var index = 0; index < config.PostEventFrames; index++)
        {
            var postFrame = await streamCoordinator.WaitForNextFrameEnvelopeAsync(lease, lastSequence, cancellationToken);
            lastSequence = postFrame.Sequence;
            frames[postFrame.Sequence] = postFrame;
        }

        return frames.Values
            .OrderBy(frame => frame.Sequence)
            .ToList();
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

        AnalysisPayloadSerialization.TrySetOutputDataJson(result, outputData, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        result.SetAnalysisDataJson(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["continuousInspection"] = outputData["ContinuousInspection"]
        }, JsonOptions));
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
        result.SetAnalysisDataJson(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["continuousInspection"] = outputData.GetValueOrDefault("ContinuousInspection")
        }, JsonOptions));
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
