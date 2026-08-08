using System.Text.Json;
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
using ClearVision.Product.Core.Streaming;
using ClearVision.Product.Infrastructure.Continuous;
using ClearVision.Product.Infrastructure.Diagnostics;
using ClearVision.Product.Infrastructure.Replay;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Runtime;

[TestClassification(TestDomain.Runtime, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "runtime")]
[Collection(RuntimeConcurrencyCollection.Name)]
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
    public void TrackConsensusJudge_ShouldChooseHighestConfidenceVoteThatSupportsMajorityOutcome()
    {
        var judge = new TrackConsensusJudge(minConsensusFrames: 3, consensusThreshold: 0.66);

        judge.AddFrame(CreateJudgment("track-1", 1, "OK", confidence: 0.99)).Should().BeNull();
        judge.AddFrame(CreateJudgment("track-1", 2, "NG", confidence: 0.80)).Should().BeNull();
        var decision = judge.AddFrame(CreateJudgment("track-1", 3, "NG", confidence: 0.70));

        decision!.Status.Should().Be(InspectionStatus.NG);
        decision.BestSequence.Should().Be(2);
        decision.RepresentativeFrame.Should().NotBeNull();
        decision.RepresentativeFrame!.Sequence.Should().Be(2);
        decision.RepresentativeFrame.Outcome.Decision.Should().Be(DecisionOutcome.Ng);
    }

    [Fact]
    public void TrackConsensusJudge_ConflictingWindow_ShouldKeepStableEvidenceWithoutChangingUndetermined()
    {
        var judge = new TrackConsensusJudge(
            minConsensusFrames: 3,
            consensusThreshold: 0.75,
            maxFramesPerTrack: 4);
        TrackFrameJudgment Frame(long sequence, DecisionOutcome decision, double confidence) => new(
            "track-conflict",
            sequence,
            new Dictionary<string, object> { ["Marker"] = $"frame-{sequence}" },
            confidence,
            new InspectionOutcome(ExecutionOutcome.Succeeded, decision, "Test", null, null, true),
            CorrelationId: $"corr-{sequence}",
            OutputImage: [(byte)sequence]);

        judge.AddFrame(Frame(1, DecisionOutcome.Ok, 0.80)).Should().BeNull();
        judge.AddFrame(Frame(2, DecisionOutcome.Ng, 0.95)).Should().BeNull();
        judge.AddFrame(Frame(3, DecisionOutcome.Ok, 0.95)).Should().BeNull();
        var decision = judge.AddFrame(Frame(4, DecisionOutcome.Ng, 0.70));

        decision.Should().NotBeNull();
        decision!.Outcome!.Value.Decision.Should().Be(DecisionOutcome.Undetermined);
        decision.Outcome.Value.ReasonCode.Should().Be("CONTINUOUS_CONSENSUS_CONFLICT");
        decision.OkVotes.Should().Be(2);
        decision.NgVotes.Should().Be(2);
        decision.BestSequence.Should().Be(2);
        decision.ResultFrame.Should().NotBeNull();
        decision.ResultFrame!.CorrelationId.Should().Be("corr-2");
        decision.ResultFrame.OutputImage.Should().Equal((byte)2);
        decision.ConsensusScore.Should().Be(0.5);
    }

    [Fact]
    public void TrackConsensusJudge_ShouldNotLetSingleInvalidVotePoisonSufficientComparableVotes()
    {
        var judge = new TrackConsensusJudge(minConsensusFrames: 2, consensusThreshold: 1);

        judge.AddFrame(CreateJudgment("track-1", 1, DecisionOutcome.Invalid)).Should().BeNull();
        judge.AddFrame(CreateJudgment("track-1", 2, "NG", confidence: 0.50)).Should().BeNull();
        var decision = judge.AddFrame(CreateJudgment("track-1", 3, "NG", confidence: 0.75));

        decision!.Status.Should().Be(InspectionStatus.NG);
        decision.NgVotes.Should().Be(2);
        decision.Outcome!.Value.Decision.Should().Be(DecisionOutcome.Ng);
        decision.RepresentativeFrame!.Sequence.Should().Be(3);
    }

    [Theory]
    [InlineData(DecisionOutcome.Invalid, InspectionStatus.Error)]
    [InlineData(DecisionOutcome.Undetermined, InspectionStatus.NotInspected)]
    [InlineData(DecisionOutcome.NotApplicable, InspectionStatus.NotInspected)]
    public void TrackConsensusJudge_ShouldFinalizeAllNonComparableWindowsWithControlledOutcome(
        DecisionOutcome outcome,
        InspectionStatus expectedStatus)
    {
        var judge = new TrackConsensusJudge(minConsensusFrames: 2, consensusThreshold: 1);

        judge.AddFrame(CreateJudgment("track-1", 1, outcome)).Should().BeNull();
        var decision = judge.AddFrame(CreateJudgment("track-1", 2, outcome));

        decision.Should().NotBeNull();
        decision!.Status.Should().Be(expectedStatus);
        decision.Outcome!.Value.Decision.Should().Be(outcome);
        decision.RepresentativeFrame.Should().BeNull();
        decision.TerminalFrame!.Outcome.Decision.Should().Be(outcome);
    }

    [Fact]
    public void TrackConsensusJudge_ShouldBoundPendingTracksAndFrameHistory()
    {
        var judge = new TrackConsensusJudge(
            minConsensusFrames: 2,
            consensusThreshold: 1,
            maxPendingTracks: 3,
            maxFramesPerTrack: 4,
            maxFinalizedTracks: 2);

        for (var index = 0; index < 10; index++)
        {
            judge.AddFrame(CreateJudgment($"track-{index}", index, "OK")).Should().BeNull();
        }

        var pendingSnapshot = judge.Snapshot();
        pendingSnapshot.PendingTrackCount.Should().Be(3);
        pendingSnapshot.PendingFrameCount.Should().Be(3);
        pendingSnapshot.MaxPendingTracks.Should().Be(3);

        for (var index = 0; index < 20; index++)
        {
            var judgment = index % 2 == 0 ? "OK" : "NG";
            judge.AddFrame(CreateJudgment("long-running-track", 100 + index, judgment));
        }

        var frameSnapshot = judge.Snapshot();
        frameSnapshot.PendingTrackCount.Should().BeLessThanOrEqualTo(3);
        frameSnapshot.PendingFrameCount.Should().BeLessThanOrEqualTo(frameSnapshot.PendingTrackCount * frameSnapshot.MaxFramesPerTrack);
        frameSnapshot.MaxFramesPerTrack.Should().Be(4);
    }

    [Fact]
    public void TrackConsensusJudge_ShouldBoundFinalizedTrackDedupe()
    {
        var judge = new TrackConsensusJudge(
            minConsensusFrames: 1,
            consensusThreshold: 1,
            maxPendingTracks: 8,
            maxFramesPerTrack: 4,
            maxFinalizedTracks: 2);

        judge.AddFrame(CreateJudgment("track-1", 1, "OK")).Should().NotBeNull();
        judge.AddFrame(CreateJudgment("track-2", 2, "OK")).Should().NotBeNull();
        judge.AddFrame(CreateJudgment("track-3", 3, "OK")).Should().NotBeNull();

        var snapshot = judge.Snapshot();
        snapshot.FinalizedTrackCount.Should().Be(2);
        snapshot.MaxFinalizedTracks.Should().Be(2);
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
            CreateExecutionSnapshot("continuous-test"),
            Guid.NewGuid(),
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
            NullInspectionImagePersistenceService.Instance,
            eventBus,
            cts.Token);

        writer.Results.Should().ContainSingle();
        writer.Results[0].Status.Should().Be(InspectionStatus.OK);
        eventBus.Results.Should().ContainSingle();
        eventBus.Results[0].OutputData.Should().ContainKey("ContinuousInspection");
        eventBus.Results[0].AnalysisData.Should().ContainKey("continuousInspection");
        writer.Results[0].AnalysisDataJson.Should().NotBeNullOrWhiteSpace();
        using var analysisDoc = JsonDocument.Parse(writer.Results[0].AnalysisDataJson!);
        analysisDoc.RootElement.GetProperty("continuousInspection").GetProperty("Mode").GetString().Should().Be("Primary");
        flow.InputFrames.Should().ContainSingle();
        flow.InputFrames[0].Sequence.Should().Be(2);
    }

    [Fact]
    public async Task ContinuousInspectionWorker_Primary_FlowFailure_ShouldPublishCanonicalFailureWithoutConsensusVote()
    {
        var frames = new[]
        {
            CreateFrame(1, new Scalar(0, 0, 0), 32),
            CreateFrame(2, new Scalar(255, 255, 255), 32)
        };
        var stream = new FakeStreamCoordinator(frames);
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "inference failed"
            }));
        var writer = new CapturingResultWriter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        writer.Written += () => cts.Cancel();

        await new ContinuousInspectionWorker(NullLogger.Instance).RunAsync(
            CreateExecutionSnapshot("continuous-failure"),
            Guid.NewGuid(),
            "cam-1",
            new ContinuousInspectionConfig
            {
                Mode = ContinuousInspectionMode.Primary,
                DetectEveryNFrames = 1,
                MinConsensusFrames = 1,
                ConsensusThreshold = 1,
                PreEventFrames = 0,
                PostEventFrames = 0
            },
            ContinuousInspectionMode.Primary,
            stream,
            flowExecution,
            writer,
            NullInspectionImagePersistenceService.Instance,
            new CapturingEventBus(),
            cts.Token);

        writer.Results.Should().ContainSingle();
        writer.Results[0].GetOutcome().Execution.Should().Be(ExecutionOutcome.Failed);
        writer.Results[0].GetOutcome().Decision.Should().Be(DecisionOutcome.Undetermined);
        writer.Results[0].GetOutcome().DecisionSource.Should().Be("FlowExecution");
    }

    [Fact]
    public async Task ContinuousInspectionWorker_Primary_WithNgOutputImage_ShouldPersistResultImage()
    {
        var outputImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 9, 8, 7, 6 };
        var imageId = Guid.NewGuid();
        var frames = new[]
        {
            CreateFrame(1, new Scalar(0, 0, 0), 32),
            CreateFrame(2, new Scalar(255, 255, 255), 32)
        };
        var stream = new FakeStreamCoordinator(frames);
        var flow = new FakeFlowExecutionService("NG", outputImage);
        var writer = new CapturingResultWriter();
        var imagePersistence = Substitute.For<IInspectionImagePersistenceService>();
        var imageCache = Substitute.For<IImageCacheRepository>();
        imageCache.AddAsync(
                Arg.Is<byte[]>(bytes => bytes.SequenceEqual(outputImage)),
                Arg.Any<string>())
            .Returns(Task.FromResult(imageId));
        var eventBus = new CapturingEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        writer.Written += () => cts.Cancel();

        var worker = new ContinuousInspectionWorker(NullLogger.Instance);
        await worker.RunAsync(
            CreateExecutionSnapshot("continuous-ng-test"),
            Guid.NewGuid(),
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
            imagePersistence,
            eventBus,
            cts.Token,
            imageCacheRepository: imageCache);

        writer.Results.Should().ContainSingle();
        writer.Results[0].Status.Should().Be(InspectionStatus.NG);
        eventBus.Results.Should().ContainSingle();
        eventBus.Results[0].ImageId.Should().Be(imageId);
        eventBus.Results[0].OutputImageBase64.Should().BeNull();
        eventBus.Results[0].OutputData.Should().NotContainKey("Image");
        eventBus.Results[0].OutputData.Should().ContainKey("JudgmentResult");
        eventBus.Results[0].OutputData.Should().ContainKey("ContinuousInspection");
        await imagePersistence.Received(1).PersistAsync(
            Arg.Is<InspectionResult>(item =>
                item.Status == InspectionStatus.NG &&
                item.OutputImage != null &&
                item.OutputImage.SequenceEqual(outputImage)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinuousInspectionWorker_Primary_ShouldPersistAndPublishTheConsensusRepresentativeFrame()
    {
        var representativeImage = new byte[] { 2, 4, 6, 8 };
        var callbackImage = new byte[] { 3, 6, 9, 12 };
        var frames = new[]
        {
            CreateFrame(1, new Scalar(0, 0, 0), 32),
            CreateFrame(2, new Scalar(0, 0, 0), 32),
            CreateFrame(3, new Scalar(255, 255, 255), 32)
        };
        var stream = new FakeStreamCoordinator(frames);
        var flow = new FakeFlowExecutionService(envelope => envelope!.Sequence switch
        {
            1 => CreateFlowResult("OK", "ok-high-confidence", 0.99, new byte[] { 1, 1, 1, 1 }),
            2 => CreateFlowResult("NG", "ng-representative", 0.80, representativeImage),
            _ => CreateFlowResult("NG", "ng-callback", 0.70, callbackImage)
        });
        var writer = new CapturingResultWriter();
        var eventBus = new CapturingEventBus();
        var imagePersistence = Substitute.For<IInspectionImagePersistenceService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        writer.Written += () => cts.Cancel();

        await new ContinuousInspectionWorker(NullLogger.Instance).RunAsync(
            CreateExecutionSnapshot("continuous-representative"),
            Guid.NewGuid(),
            "cam-1",
            new ContinuousInspectionConfig
            {
                Mode = ContinuousInspectionMode.Primary,
                DetectEveryNFrames = 1,
                MinConsensusFrames = 3,
                ConsensusThreshold = 0.66,
                PreEventFrames = 2,
                PostEventFrames = 0,
                SaveReplayOnNgOnly = true
            },
            ContinuousInspectionMode.Primary,
            stream,
            flow,
            writer,
            imagePersistence,
            eventBus,
            cts.Token);

        writer.Results.Should().ContainSingle();
        var result = writer.Results[0];
        result.Status.Should().Be(InspectionStatus.NG);
        result.ConfidenceScore.Should().Be(0.80);
        result.OutputImage.Should().Equal(representativeImage);
        using var outputDocument = JsonDocument.Parse(result.OutputDataJson!);
        outputDocument.RootElement.GetProperty("FrameMarker").GetString().Should().Be("ng-representative");
        var continuous = outputDocument.RootElement.GetProperty("ContinuousInspection");
        continuous.GetProperty("BestSequence").GetInt64().Should().Be(2);
        continuous.GetProperty("RepresentativeSequence").GetInt64().Should().Be(2);
        continuous.GetProperty("CorrelationId").GetString().Should().Be("corr-2");
        continuous.GetProperty("Confidence").GetDouble().Should().Be(0.80);
        eventBus.Results.Should().ContainSingle();
        using var eventOutputDocument = JsonDocument.Parse(JsonSerializer.Serialize(eventBus.Results[0].OutputData));
        eventOutputDocument.RootElement.GetProperty("FrameMarker").GetString().Should().Be("ng-representative");
        await imagePersistence.Received(1).PersistAsync(
            Arg.Is<InspectionResult>(item => item.OutputImage!.SequenceEqual(representativeImage)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinuousInspectionWorker_Primary_WhenStreamFaults_ShouldRestartLeaseAndPublishDecision()
    {
        var frames = new[]
        {
            CreateFrame(1, new Scalar(0, 0, 0), 32),
            CreateFrame(2, new Scalar(255, 255, 255), 32)
        };
        var stream = new FakeStreamCoordinator(frames, failFirstWait: true);
        var flow = new FakeFlowExecutionService("OK");
        var writer = new CapturingResultWriter();
        var eventBus = new CapturingEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        writer.Written += () => cts.Cancel();

        var worker = new ContinuousInspectionWorker(NullLogger.Instance);
        await worker.RunAsync(
            CreateExecutionSnapshot("continuous-restart-test"),
            Guid.NewGuid(),
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
            NullInspectionImagePersistenceService.Instance,
            eventBus,
            cts.Token);

        stream.LeaseCount.Should().BeGreaterThanOrEqualTo(2);
        stream.ReleaseCount.Should().BeGreaterThanOrEqualTo(2);
        writer.Results.Should().ContainSingle();
        writer.Results[0].Status.Should().Be(InspectionStatus.OK);
    }

    [Fact]
    public async Task ContinuousInspectionWorker_ShadowWithoutCandidateAuthority_ShouldRejectBeforeExecution()
    {
        var frames = new[]
        {
            CreateFrame(1, new Scalar(0, 0, 0), 32),
            CreateFrame(2, new Scalar(255, 255, 255), 32)
        };
        var stream = new FakeStreamCoordinator(frames);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var flow = new FakeFlowExecutionService(
            "OK",
            onProjectVariables: _ => cts.Cancel());
        var writer = new CapturingResultWriter();
        var eventBus = new CapturingEventBus();
        var variableId = Guid.NewGuid();
        var schema = CreateProjectVariableSchema(variableId);
        using var session = new ProjectVariableSession(schema);
        var commitCalls = 0;
        ProjectVariableCommitHandler commitHandler = (_, _) =>
        {
            commitCalls++;
            return ProjectVariableCommitResult.Success();
        };

        var worker = new ContinuousInspectionWorker(NullLogger.Instance);
        var act = async () => await worker.RunAsync(
            CreateExecutionSnapshot("continuous-shadow-project-variables"),
            Guid.NewGuid(),
            "cam-1",
            new ContinuousInspectionConfig
            {
                Mode = ContinuousInspectionMode.Shadow,
                DetectEveryNFrames = 1,
                MinConsensusFrames = 1,
                ConsensusThreshold = 1,
                PreEventFrames = 0,
                PostEventFrames = 0,
                SaveReplayOnNgOnly = true
            },
            ContinuousInspectionMode.Shadow,
            stream,
            flow,
            writer,
            NullInspectionImagePersistenceService.Instance,
            eventBus,
            cts.Token,
            projectVariableSession: session,
            projectVariableBindingIndex: ProjectVariableBindingIndex.Build(schema),
            projectVariableCommitHandler: commitHandler);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*explicit candidate snapshot*");
        flow.ProjectVariableContexts.Should().BeEmpty();
        commitCalls.Should().Be(0);
        writer.Results.Should().BeEmpty();
        eventBus.Results.Should().BeEmpty();
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

    private static OperatorFlow CreateDecisionFlow(string name)
    {
        var flow = new OperatorFlow(name);
        var op = new Operator(Guid.NewGuid(), "Decision", OperatorType.ResultOutput, 0, 0);
        flow.AddOperator(op);
        return flow.BindStringDecision(op);
    }

    private static ExecutionSnapshot CreateExecutionSnapshot(string name) =>
        new(
            Guid.NewGuid(),
            CreateDecisionFlow(name),
            persistenceRevision: 1,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary);

    private static ProjectGlobalVariableSchema CreateProjectVariableSchema(Guid variableId)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(1L),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static ArrivalSignal CreateSignal(long sequence, DateTimeOffset eventTimeUtc) =>
        new("cam-1", sequence, eventTimeUtc, "frame_change", 1.0, new OpenCvSharp.Rect(0, 0, 8, 8), $"corr-{sequence}", 64);

    private static TrackFrameJudgment CreateJudgment(
        string trackId,
        long sequence,
        string judgment,
        double confidence = 1.0) =>
        CreateJudgment(
            trackId,
            sequence,
            judgment.Equals("NG", StringComparison.OrdinalIgnoreCase)
                ? DecisionOutcome.Ng
                : DecisionOutcome.Ok,
            confidence);

    private static TrackFrameJudgment CreateJudgment(
        string trackId,
        long sequence,
        DecisionOutcome decision,
        double confidence = 1.0) =>
        new(
            trackId,
            sequence,
            new Dictionary<string, object> { ["JudgmentResult"] = "legacy-data-must-not-be-read" },
            confidence,
            new InspectionOutcome(
                ExecutionOutcome.Succeeded,
                decision,
                "Test",
                $"TEST_{decision}",
                null,
                decision is DecisionOutcome.Ok or DecisionOutcome.Ng));

    private static FlowExecutionResult CreateFlowResult(
        string judgment,
        string marker,
        double confidence,
        byte[] image) =>
        new()
        {
            IsSuccess = true,
            ExecutionTimeMs = 1,
            OutputData = new Dictionary<string, object>
            {
                ["JudgmentResult"] = judgment,
                ["FrameMarker"] = marker,
                ["Confidence"] = confidence,
                ["Image"] = image
            }
        };

    private sealed class FakeStreamCoordinator : ICameraFrameStreamCoordinator
    {
        private readonly List<FrameEnvelope> _frames;
        private readonly bool _failFirstWait;
        private int _index;
        private bool _hasFailedFirstWait;

        public FakeStreamCoordinator(IEnumerable<FrameEnvelope> frames, bool failFirstWait = false)
        {
            _frames = frames.ToList();
            _failFirstWait = failFirstWait;
        }

        public int LeaseCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task<CameraStreamFrame> AcquireFrameAsync(string cameraId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FrameEnvelope> AcquireFrameEnvelopeAsync(string cameraId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_frames[Math.Min(_index, _frames.Count - 1)]);

        public Task<CameraStreamLease> AcquireStreamLeaseAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            LeaseCount++;
            return Task.FromResult(new CameraStreamLease($"lease-{LeaseCount}", cameraId, CameraTriggerMode.Continuous, 25));
        }

        public Task<CameraStreamFrame> WaitForNextFrameAsync(CameraStreamLease lease, long? afterSequence = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FrameEnvelope> WaitForNextFrameEnvelopeAsync(CameraStreamLease lease, long? afterSequence = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_failFirstWait && !_hasFailedFirstWait)
            {
                _hasFailedFirstWait = true;
                throw new InvalidOperationException("Camera stream 'cam-1' stopped producing frames because the camera acquisition loop is no longer running.");
            }

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

        public Task ReleaseStreamLeaseAsync(CameraStreamLease lease)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
        public Task ReleaseIdleStreamAsync(string cameraId) => Task.CompletedTask;
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
        private readonly byte[]? _outputImage;
        private readonly Action<ProjectVariableExecutionContext>? _onProjectVariables;
        private readonly Func<FrameEnvelope?, FlowExecutionResult>? _resultFactory;
        public List<FrameEnvelope> InputFrames { get; } = new();
        public List<ProjectVariableExecutionContext> ProjectVariableContexts { get; } = new();

        public FakeFlowExecutionService(
            string judgment,
            byte[]? outputImage = null,
            Action<ProjectVariableExecutionContext>? onProjectVariables = null)
        {
            _judgment = judgment;
            _outputImage = outputImage;
            _onProjectVariables = onProjectVariables;
        }

        public FakeFlowExecutionService(Func<FrameEnvelope?, FlowExecutionResult> resultFactory)
        {
            _judgment = string.Empty;
            _resultFactory = resultFactory;
        }

        public Task<FlowExecutionResult> ExecuteWithSnapshotAsync(ExecutionSnapshot snapshot, Dictionary<string, object>? inputData = null, bool enableParallel = false, CancellationToken cancellationToken = default)
        {
            return ExecuteCoreAsync(inputData);
        }

        private Task<FlowExecutionResult> ExecuteCoreAsync(Dictionary<string, object>? inputData)
        {
            FrameEnvelope? providedFrame = null;
            if (inputData?.TryGetValue("ProvidedFrameEnvelope", out var frame) == true && frame is FrameEnvelope envelope)
            {
                providedFrame = envelope;
                InputFrames.Add(envelope);
            }

            if (_resultFactory != null)
            {
                return Task.FromResult(_resultFactory(providedFrame));
            }

            return Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 1,
                OutputData = _outputImage == null
                    ? new Dictionary<string, object> { ["JudgmentResult"] = _judgment }
                    : new Dictionary<string, object>
                    {
                        ["JudgmentResult"] = _judgment,
                        ["Image"] = _outputImage
                    }
            });
        }

        public Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
            ExecutionSnapshot snapshot,
            Dictionary<string, object>? inputData,
            ProjectVariableExecutionContext projectVariables,
            bool enableParallel = false,
            CancellationToken cancellationToken = default)
        {
            ProjectVariableContexts.Add(projectVariables);
            _onProjectVariables?.Invoke(projectVariables);
            return ExecuteCoreAsync(inputData);
        }

        public Task<OperatorExecutionResult> ExecuteOperatorAsync(
            GovernedOperatorExecutionContext context,
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public FlowValidationResult ValidateSnapshot(ExecutionSnapshot snapshot) => new() { IsValid = true };
        public FlowExecutionStatus? GetExecutionStatus(Guid flowId) => null;
        public Task CancelExecutionAsync(Guid flowId) => Task.CompletedTask;
        public Task<FlowDebugExecutionResult> ExecuteDebugWithSnapshotAsync(ExecutionSnapshot snapshot, DebugOptions options, Dictionary<string, object>? inputData = null, ProjectVariableExecutionContext? projectVariables = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
