using Acme.Product.Core.Cameras;
using Acme.Product.Core.Continuous;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Events;
using Acme.Product.Core.Services;
using Acme.Product.Core.Streaming;
using Acme.Product.Infrastructure.Continuous;
using Acme.Product.Infrastructure.Diagnostics;
using Acme.Product.Infrastructure.Replay;
using Acme.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace Acme.Product.Tests.Runtime;

public class ContinuousRuntimeTests
{
    [Fact]
    public void ArrivalDetector_ShouldEmitSignalWhenRoiChanges()
    {
        using var detector = new FrameDifferenceArrivalDetector(new ArrivalDetectorOptions(
            PixelThreshold: 10,
            MinChangeRatio: 0.01,
            MinChangePixels: 1,
            CooldownMs: 0));

        detector.Update(CreateFrame(1, new Scalar(0, 0, 0))).Should().BeNull();
        var signal = detector.Update(CreateFrame(2, new Scalar(255, 255, 255)));

        signal.Should().NotBeNull();
        signal!.CameraId.Should().Be("cam-1");
        signal.Sequence.Should().Be(2);
        signal.Score.Should().BeGreaterThan(0.9);
        signal.ChangedPixels.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LightweightTracker_ShouldMergeNearbySignalsAndExpireOldTrack()
    {
        var tracker = new LightweightTracker(new LightweightTrackerOptions(
            MaxSequenceGap: 3,
            TrackTimeoutMs: 100,
            FreezeAfterSignals: 3));
        var t0 = DateTimeOffset.UtcNow;

        var first = tracker.Update(CreateSignal(10, t0));
        var second = tracker.Update(CreateSignal(12, t0.AddMilliseconds(20)));

        first.IsNew.Should().BeTrue();
        second.IsNew.Should().BeFalse();
        second.TrackId.Should().Be(first.TrackId);
        second.SignalCount.Should().Be(2);

        tracker.SnapshotActiveTracks(t0.AddMilliseconds(250)).Should().BeEmpty();
        var third = tracker.Update(CreateSignal(20, t0.AddMilliseconds(300)));
        third.IsNew.Should().BeTrue();
        third.TrackId.Should().NotBe(first.TrackId);
    }

    [Fact]
    public async Task InferenceScheduler_ShouldExecuteScheduledWorkAndPublishResult()
    {
        await using var scheduler = new InferenceScheduler(queueLength: 2);
        var ready = new TaskCompletionSource<ScheduledInferenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.ResultReady += result =>
        {
            ready.TrySetResult(result);
            return Task.CompletedTask;
        };

        var accepted = scheduler.TrySchedule(new ScheduledInferenceItem(
            CreateFrame(1, new Scalar(1, 2, 3)),
            "track-1",
            (_, _) => Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
            })));

        accepted.Should().BeTrue();
        var completed = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().Be(ready.Task);

        var result = await ready.Task;
        result.TrackId.Should().Be("track-1");
        result.Error.Should().BeNull();
        result.Result!.IsSuccess.Should().BeTrue();
        scheduler.Snapshot().CompletedCount.Should().Be(1);
    }

    [Fact]
    public void TrackConsensusJudge_ShouldFinalizeAfterThreshold()
    {
        var judge = new TrackConsensusJudge(minConsensusFrames: 3, consensusThreshold: 0.66);

        judge.AddFrame(CreateJudgment("track-1", 1, "OK", confidence: 0.5)).Should().BeNull();
        judge.AddFrame(CreateJudgment("track-1", 2, "NG", confidence: 0.6)).Should().BeNull();
        var decision = judge.AddFrame(CreateJudgment("track-1", 3, "NG", confidence: 0.9));

        decision.Should().NotBeNull();
        decision!.Status.Should().Be(InspectionStatus.NG);
        decision.FrameCount.Should().Be(3);
        decision.NgVotes.Should().Be(2);
        decision.BestSequence.Should().Be(3);
        judge.AddFrame(CreateJudgment("track-1", 4, "OK")).Should().BeNull();
    }

    [Fact]
    public void ContinuousMetricsCollector_ShouldReportCountersAndAverageLatency()
    {
        var collector = new ContinuousMetricsCollector();

        collector.RecordFrameReceived();
        collector.RecordArrivalSignal();
        collector.RecordTrackCreated();
        collector.RecordInferenceScheduled();
        collector.RecordInferenceCompleted(TimeSpan.FromMilliseconds(10));
        collector.RecordInferenceCompleted(TimeSpan.FromMilliseconds(30));
        collector.RecordDecisionFinalized();

        var snapshot = collector.Snapshot();
        snapshot.FramesReceived.Should().Be(1);
        snapshot.ArrivalSignals.Should().Be(1);
        snapshot.TracksCreated.Should().Be(1);
        snapshot.CompletedInferences.Should().Be(2);
        snapshot.FinalDecisions.Should().Be(1);
        snapshot.AverageInferenceLatencyMs.Should().Be(20);
    }

    [Fact]
    public async Task FrameReplayRecorder_ShouldPersistFramesAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cv-replay-{Guid.NewGuid():N}");
        try
        {
            var recorder = new FrameReplayRecorder(root);
            var directory = await recorder.SaveTrackAsync(
                "cam-1:42",
                new[] { CreateFrame(42, new Scalar(10, 20, 30)) },
                new TrackDecision("cam-1:42", InspectionStatus.OK, 1, 1, 0, 42, 1.0, true));

            Directory.Exists(directory).Should().BeTrue();
            File.Exists(Path.Combine(directory, "00000042.png")).Should().BeTrue();
            File.Exists(Path.Combine(directory, "metadata.json")).Should().BeTrue();
            var metadata = await File.ReadAllTextAsync(Path.Combine(directory, "metadata.json"));
            metadata.Should().Contain("cam-1:42");
            metadata.Should().Contain("00000042.png");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ContinuousInspectionWorker_Primary_ShouldPublishFinalDecisionFromStream()
    {
        var frames = new[]
        {
            CreateFrame(1, new Scalar(0, 0, 0), 32),
            CreateFrame(2, new Scalar(255, 255, 255), 32)
        };
        var stream = new FakeStreamCoordinator(frames);
        var flow = new FakeFlowExecutionService("OK");
        var writer = new CapturingResultWriter();
        var eventBus = new CapturingEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        writer.Written += () => cts.Cancel();

        var worker = new ContinuousInspectionWorker(NullLogger.Instance);
        await worker.RunAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new OperatorFlow("continuous-test"),
            "cam-1",
            new ContinuousInspectionConfig
            {
                Mode = ContinuousInspectionMode.Primary,
                DetectEveryNFrames = 1,
                MinConsensusFrames = 1,
                ConsensusThreshold = 1,
                PreEventFrames = 0,
                PostEventFrames = 0,
                SaveReplayOnNgOnly = true
            },
            ContinuousInspectionMode.Primary,
            stream,
            flow,
            writer,
            eventBus,
            cts.Token);

        writer.Results.Should().ContainSingle();
        writer.Results[0].Status.Should().Be(InspectionStatus.OK);
        eventBus.Results.Should().ContainSingle();
        flow.InputFrames.Should().ContainSingle();
        flow.InputFrames[0].Sequence.Should().Be(2);
    }

    private static FrameEnvelope CreateFrame(long sequence, Scalar color, int size = 8)
    {
        using var mat = new Mat(size, size, MatType.CV_8UC3, color);
        return new FrameEnvelope(
            "cam-1",
            sequence,
            DateTimeOffset.UtcNow.AddMilliseconds(sequence),
            mat.Width,
            mat.Height,
            "image/png",
            FramePayloadKind.EncodedImage,
            mat.ToBytes(".png"),
            TimestampSource: FrameTimestampSource.HostFallback,
            CorrelationId: $"corr-{sequence}");
    }

    private static ArrivalSignal CreateSignal(long sequence, DateTimeOffset eventTimeUtc) =>
        new("cam-1", sequence, eventTimeUtc, "frame_change", 1.0, new OpenCvSharp.Rect(0, 0, 8, 8), $"corr-{sequence}", 64);

    private static TrackFrameJudgment CreateJudgment(
        string trackId,
        long sequence,
        string judgment,
        double confidence = 1.0) =>
        new(trackId, sequence, new Dictionary<string, object> { ["JudgmentResult"] = judgment }, confidence);

    private sealed class FakeStreamCoordinator : ICameraFrameStreamCoordinator
    {
        private readonly List<FrameEnvelope> _frames;
        private int _index;

        public FakeStreamCoordinator(IEnumerable<FrameEnvelope> frames)
        {
            _frames = frames.ToList();
        }

        public Task<CameraStreamFrame> AcquireFrameAsync(string cameraId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FrameEnvelope> AcquireFrameEnvelopeAsync(string cameraId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_frames[Math.Min(_index, _frames.Count - 1)]);

        public Task<CameraStreamLease> AcquireStreamLeaseAsync(string cameraId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CameraStreamLease("lease-1", cameraId, CameraTriggerMode.Continuous, 25));

        public Task<CameraStreamFrame> WaitForNextFrameAsync(CameraStreamLease lease, long? afterSequence = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FrameEnvelope> WaitForNextFrameEnvelopeAsync(CameraStreamLease lease, long? afterSequence = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (_index < _frames.Count && afterSequence.HasValue && _frames[_index].Sequence <= afterSequence.Value)
            {
                _index++;
            }

            if (_index >= _frames.Count)
            {
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ContinueWith(_ => _frames[^1], cancellationToken);
            }

            return Task.FromResult(_frames[_index++]);
        }

        public Task ReleaseStreamLeaseAsync(CameraStreamLease lease) => Task.CompletedTask;
        public Task<CameraPreviewSession> StartPreviewSessionAsync(string cameraId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CameraStreamFrame> WaitForPreviewFrameAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopPreviewSessionAsync(string sessionId) => Task.CompletedTask;
        public bool TryGetLatestFrameEnvelope(string cameraId, out FrameEnvelope? frame)
        {
            frame = _frames.LastOrDefault();
            return frame != null;
        }

        public IReadOnlyList<FrameEnvelope> GetFrameEnvelopeWindow(string cameraId, long centerSequence, int before, int after) =>
            _frames.Where(frame => frame.Sequence >= centerSequence - before && frame.Sequence <= centerSequence + after).ToList();

        public RingBufferStats SnapshotFrameBufferStats(string cameraId) => new(_frames.Count, _frames.Count, 0, _frames.First().Sequence, _frames.Last().Sequence);
        public CameraStreamUsageSnapshot SnapshotStreamUsage(string cameraId) =>
            new(cameraId, true, 0, 0, 0, CameraTriggerMode.Continuous, 25);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFlowExecutionService : IFlowExecutionService
    {
        private readonly string _judgment;
        public List<FrameEnvelope> InputFrames { get; } = new();

        public FakeFlowExecutionService(string judgment)
        {
            _judgment = judgment;
        }

        public Task<FlowExecutionResult> ExecuteFlowAsync(OperatorFlow flow, Dictionary<string, object>? inputData = null, bool enableParallel = false, CancellationToken cancellationToken = default)
        {
            if (inputData?.TryGetValue("ProvidedFrameEnvelope", out var frame) == true && frame is FrameEnvelope envelope)
            {
                InputFrames.Add(envelope);
            }

            return Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 1,
                OutputData = new Dictionary<string, object> { ["JudgmentResult"] = _judgment }
            });
        }

        public Task<OperatorExecutionResult> ExecuteOperatorAsync(Operator @operator, Dictionary<string, object>? inputs = null) => throw new NotSupportedException();
        public FlowValidationResult ValidateFlow(OperatorFlow flow) => new() { IsValid = true };
        public FlowExecutionStatus? GetExecutionStatus(Guid flowId) => null;
        public Task CancelExecutionAsync(Guid flowId) => Task.CompletedTask;
        public Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(OperatorFlow flow, DebugOptions options, Dictionary<string, object>? inputData = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId) => null;
        public Task ClearDebugCacheAsync(Guid debugSessionId) => Task.CompletedTask;
    }

    private sealed class CapturingResultWriter : IInspectionResultChannelWriter
    {
        public List<InspectionResult> Results { get; } = new();
        public event Action? Written;

        public bool TryWrite(InspectionResult result)
        {
            Results.Add(result);
            Written?.Invoke();
            return true;
        }
    }

    private sealed class CapturingEventBus : IInspectionEventBus
    {
        public List<InspectionResultEvent> Results { get; } = new();

        public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default) where TEvent : IInspectionEvent
        {
            if (eventData is InspectionResultEvent result)
            {
                Results.Add(result);
            }

            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IInspectionEvent => new NoopDisposable();
        public IDisposable SubscribeInterface<TInterface>(Func<TInterface, CancellationToken, Task> handler) where TInterface : class, IInspectionEvent => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
