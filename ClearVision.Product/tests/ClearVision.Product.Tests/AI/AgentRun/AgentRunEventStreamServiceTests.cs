using System.Text.Json;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.AgentRun;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class AgentRunEventStreamServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"cv-agent-run-tests-{Guid.NewGuid():N}");
    private readonly AgentRunEventStore _store;
    private readonly AgentRunEventRedactor _redactor = new();
    private readonly AgentRunEventStreamService _service;

    public AgentRunEventStreamServiceTests()
    {
        _store = new AgentRunEventStore(_directory, _redactor);
        _service = new AgentRunEventStreamService(_store, _redactor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact(DisplayName = "AgentRun create emits run.started and assistant.brief")]
    public void CreateRun_ShouldEmitStartedAndBrief()
    {
        var result = _service.CreateRun("detect scratches on metal parts", new { mode = "auto" });

        result.RunId.Should().StartWith("ar_");
        result.Events.Should().HaveCount(2);
        result.Events.Select(evt => evt.EventType).Should().Equal(
            AgentRunEventTypes.RunStarted,
            AgentRunEventTypes.AssistantBrief);
        result.Events.Select(evt => evt.Sequence).Should().Equal(1, 2);
        result.Events.Should().OnlyContain(evt => evt.MetadataOnly && evt.RedactionPass);
        result.Brief.Should().Contain("detect scratches");
    }

    [Fact(DisplayName = "AgentRun append assigns monotonic event ordering")]
    public void Append_ShouldAssignMonotonicSequences()
    {
        var run = _service.CreateRun("ordering");

        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));
        _service.Append(run.RunId, Draft(AgentRunEventTypes.ToolCallStarted, "planner"));
        _service.Append(run.RunId, Draft(AgentRunEventTypes.ToolCallCompleted, "planner"));

        _service.Replay(run.RunId)!.Events
            .Select(evt => evt.Sequence)
            .Should()
            .Equal(1, 2, 3, 4, 5);
    }

    [Fact(DisplayName = "AgentRun subscription replays events after requested sequence")]
    public void Subscribe_ShouldReplayEventsAfterSequence()
    {
        var run = _service.CreateRun("replay");
        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));
        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageCompleted, "planner"));

        using var subscription = _service.Subscribe(run.RunId, afterSequence: 2);

        subscription.Should().NotBeNull();
        subscription!.ReplayEvents.Select(evt => evt.Sequence).Should().Equal(3, 4);
    }

    [Fact(DisplayName = "AgentRun subscription receives live appended events")]
    public async Task Subscribe_ShouldReceiveLiveEvent()
    {
        var run = _service.CreateRun("live");
        using var subscription = _service.Subscribe(run.RunId, afterSequence: 2);

        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));

        var received = await subscription!.LiveEvents.ReadAsync(CancellationToken.None);
        received.EventType.Should().Be(AgentRunEventTypes.StageStarted);
        received.Sequence.Should().Be(3);
    }

    [Fact(DisplayName = "AgentRun subscription closes lagging live subscriber instead of buffering without bound")]
    public async Task Subscribe_ShouldCloseLaggingLiveSubscriber()
    {
        var run = _service.CreateRun("lagging subscriber");
        using var subscription = _service.Subscribe(run.RunId, afterSequence: 2);

        for (var i = 0; i < 300; i++)
        {
            _service.Append(run.RunId, Draft(AgentRunEventTypes.ToolCallCompleted, "planner"));
        }

        var receivedSequences = new List<long>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (await subscription!.LiveEvents.WaitToReadAsync(timeout.Token))
        {
            while (subscription.LiveEvents.TryRead(out var evt))
            {
                receivedSequences.Add(evt.Sequence);
            }
        }

        receivedSequences.Should().NotBeEmpty();
        receivedSequences.Count.Should().BeLessThan(300);
        receivedSequences.Should().BeInAscendingOrder();

        var replay = _service.Replay(run.RunId)!;
        replay.Events.Should().HaveCount(302);
        replay.Events.Select(evt => evt.Sequence).Should().Equal(Enumerable.Range(1, 302).Select(i => (long)i));
        replay.Summary.StaleEventCount.Should().Be(1);
        replay.Diagnostics.StaleEventCount.Should().Be(1);
    }

    [Fact(DisplayName = "AgentRun complete persists terminal summary and closes live stream")]
    public async Task Complete_ShouldPersistSummaryAndCloseLiveStream()
    {
        var run = _service.CreateRun("complete");
        using var subscription = _service.Subscribe(run.RunId, afterSequence: 2);

        _service.Complete(run.RunId, "done", new { reportId = "agent-report-test" });

        var terminal = await subscription!.LiveEvents.ReadAsync(CancellationToken.None);
        terminal.EventType.Should().Be(AgentRunEventTypes.RunCompleted);
        (await subscription.LiveEvents.WaitToReadAsync()).Should().BeFalse();

        var replay = _service.Replay(run.RunId)!;
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Completed);
        replay.Summary.Summary.Should().Be("done");
        replay.Summary.EventCount.Should().Be(3);
    }

    [Fact(DisplayName = "AgentRun cancel emits terminal event and signals cancellation token")]
    public void Cancel_ShouldEmitTerminalAndCancelToken()
    {
        var run = _service.CreateRun("cancel");
        var token = _service.GetCancellationToken(run.RunId);

        var cancelled = _service.Cancel(run.RunId);

        cancelled.Should().NotBeNull();
        cancelled!.EventType.Should().Be(AgentRunEventTypes.RunCancelled);
        token.IsCancellationRequested.Should().BeTrue();
        _service.Replay(run.RunId)!.Summary.Status.Should().Be(AgentRunEventStatuses.Cancelled);
    }

    [Fact(DisplayName = "AgentRun terminal reservation grants only one terminal owner")]
    public void TryReserveTerminal_ShouldRejectOtherTerminalOwners()
    {
        var run = _service.CreateRun("terminal reservation");
        var token = _service.GetCancellationToken(run.RunId);

        var cancelReservation = _service.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Cancelled);
        var completeReservation = _service.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Completed);
        var repeatedCancel = _service.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Cancelled);

        cancelReservation.Outcome.Should().Be(AgentRunTerminalReservationOutcome.Acquired);
        cancelReservation.ReservationId.Should().NotBeNullOrWhiteSpace();
        token.IsCancellationRequested.Should().BeTrue();
        completeReservation.Outcome.Should().Be(AgentRunTerminalReservationOutcome.RejectedByOtherTerminalOwner);
        completeReservation.CurrentStatus.Should().Be("cancelling");
        repeatedCancel.Outcome.Should().Be(AgentRunTerminalReservationOutcome.AlreadyReservedBySameStatus);
        repeatedCancel.ReservationId.Should().BeNull();

        _service.Complete(run.RunId, "wrong owner").Should().BeNull();
        var cancelled = _service.Cancel(run.RunId, reservation: cancelReservation);
        cancelled.Should().NotBeNull();
        cancelled!.EventType.Should().Be(AgentRunEventTypes.RunCancelled);
        _service.Replay(run.RunId)!.Events.Should().ContainSingle(evt => evt.EventType == AgentRunEventTypes.RunCancelled);
    }

    [Fact(DisplayName = "AgentRun prepared terminal intent rejects conflicting mutation metadata")]
    public void PrepareTerminalIntent_ShouldRejectConflictingMutationMetadata()
    {
        var run = _service.CreateRun("terminal intent");
        var reservation = _service.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Completed);

        var prepared = _service.PrepareTerminalIntent(
            run.RunId,
            new AgentRunTerminalIntentDraft
            {
                SessionId = "session-intent",
                RunType = "plan",
                TargetStatus = AgentRunEventStatuses.Completed,
                TerminalMutationId = "plan-terminal:run:completed",
                PayloadFingerprint = "sha256:first",
                Identity = "plan:first"
            },
            reservation);
        var conflicting = _service.PrepareTerminalIntent(
            run.RunId,
            new AgentRunTerminalIntentDraft
            {
                SessionId = "session-intent",
                RunType = "plan",
                TargetStatus = AgentRunEventStatuses.Completed,
                TerminalMutationId = "plan-terminal:run:completed",
                PayloadFingerprint = "sha256:second",
                Identity = "plan:second"
            },
            reservation);
        var replay = _service.Replay(run.RunId)!;

        prepared.Should().NotBeNull();
        conflicting.Should().BeNull();
        replay.Summary.TerminalIntent.Should().NotBeNull();
        replay.Summary.TerminalIntent!.PayloadFingerprint.Should().Be("sha256:first");
    }

    [Fact(DisplayName = "AgentRun unreserved terminal append atomically reserves and completes")]
    public void Complete_WithoutReservation_ShouldPreserveCompatibility()
    {
        var run = _service.CreateRun("compat terminal");

        var completed = _service.Complete(run.RunId, "done");
        var cancelReservation = _service.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Cancelled);

        completed.Should().NotBeNull();
        completed!.EventType.Should().Be(AgentRunEventTypes.RunCompleted);
        cancelReservation.Outcome.Should().Be(AgentRunTerminalReservationOutcome.AlreadyTerminal);
        cancelReservation.CurrentStatus.Should().Be(AgentRunEventStatuses.Completed);
        _service.Replay(run.RunId)!.Summary.Status.Should().Be(AgentRunEventStatuses.Completed);
    }

    [Fact(DisplayName = "AgentRun fail captures first fix recommendation in summary")]
    public void Fail_ShouldCaptureFirstFixRecommendation()
    {
        var run = _service.CreateRun("fail");

        _service.Fail(run.RunId, "blocked", "Provide missing threshold metadata.", new { status = "blocked" });

        var replay = _service.Replay(run.RunId)!;
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Failed);
        replay.Summary.FirstFixRecommendation.Should().Be("Provide missing threshold metadata.");
    }

    [Fact(DisplayName = "AgentRun replay restores persisted JSONL after service restart")]
    public void Replay_ShouldRestorePersistedJsonl()
    {
        var run = _service.CreateRun("restore", ownerHash: "usr_owner_restore");
        _service.Append(run.RunId, Draft(AgentRunEventTypes.ReadinessChecked, "readiness"));
        _service.Complete(run.RunId, "restored");

        var reloaded = new AgentRunEventStreamService(new AgentRunEventStore(_directory, _redactor), _redactor);
        var replay = reloaded.Replay(run.RunId);

        replay.Should().NotBeNull();
        replay!.Events.Select(evt => evt.Sequence).Should().Equal(1, 2, 3, 4);
        replay.Summary.Summary.Should().Be("restored");
        replay.Summary.OwnerHash.Should().Be("usr_owner_restore");
        reloaded.IsRunOwner(run.RunId, "usr_owner_restore").Should().BeTrue();
        reloaded.IsRunOwner(run.RunId, "usr_other").Should().BeFalse();
    }

    [Fact(DisplayName = "AgentRun replay after restart does not synthesize terminal failure")]
    public void Replay_ShouldRestoreNonTerminalRunWithoutFailureSideEffect()
    {
        var run = _service.CreateRun("restart recovery", ownerHash: "usr_owner_restart");
        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));

        var reloaded = new AgentRunEventStreamService(new AgentRunEventStore(_directory, _redactor), _redactor);
        var replay = reloaded.Replay(run.RunId);

        replay.Should().NotBeNull();
        replay!.Summary.Status.Should().Be(AgentRunEventStatuses.Running);
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunFailed);
        reloaded.Replay(run.RunId)!.Events.Should().HaveCount(replay.Events.Count);
    }

    [Fact(DisplayName = "AgentRun explicit host-interrupted recovery fails non-terminal runs closed")]
    public void FailHostInterrupted_ShouldFailClosedForNonTerminalRunAfterRestart()
    {
        var run = _service.CreateRun("restart recovery", ownerHash: "usr_owner_restart");
        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));

        var reloaded = new AgentRunEventStreamService(new AgentRunEventStore(_directory, _redactor), _redactor);
        var terminal = reloaded.FailHostInterrupted(run.RunId);
        var appended = reloaded.Append(run.RunId, Draft(AgentRunEventTypes.StageCompleted, "planner"));
        var replay = reloaded.Replay(run.RunId)!;

        terminal.Should().NotBeNull();
        terminal!.EventType.Should().Be(AgentRunEventTypes.RunFailed);
        terminal.Summary.Should().Contain("失败状态");
        terminal.MetadataOnly.Should().BeTrue();
        var payloadJson = JsonSerializer.Serialize(terminal.Payload);
        payloadJson.Should().Contain("host_instance_interrupted");
        payloadJson.Should().Contain("metadataOnly");
        payloadJson.Should().NotContain("hostInstanceId");
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Failed);
        replay.Events.Should().ContainSingle(evt => evt.EventType == AgentRunEventTypes.RunFailed);
        appended.Should().BeNull();
    }

    [Fact(DisplayName = "AgentRun replay returns replay-safe snapshot and diagnostics")]
    public void Replay_ShouldReturnSnapshotAndDiagnostics()
    {
        var now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        _service.UtcNowProvider = () => now;
        var run = _service.CreateRun("snapshot", ownerHash: "usr_owner_snapshot");
        now = now.AddSeconds(1);
        _service.Append(run.RunId, Draft(AgentRunEventTypes.ReadinessChecked, "readiness"));

        var replay = _service.Replay(run.RunId)!;

        replay.Snapshot.StorageVersion.Should().Be(AgentRunEventStore.StorageVersion);
        replay.Snapshot.RunId.Should().Be(run.RunId);
        replay.Snapshot.GeneratedAt.Should().Be(now);
        replay.Snapshot.FirstSequence.Should().Be(1);
        replay.Snapshot.LastSequence.Should().Be(3);
        replay.Snapshot.EventCount.Should().Be(3);
        replay.Snapshot.Events.Select(evt => evt.Sequence).Should().Equal(1, 2, 3);
        replay.Snapshot.MetadataOnly.Should().BeTrue();
        replay.Snapshot.RedactionPass.Should().BeTrue();
        replay.Diagnostics.RunId.Should().Be(run.RunId);
        replay.Diagnostics.EventCount.Should().Be(3);
        replay.Diagnostics.DuplicateEventCount.Should().Be(0);
        replay.Diagnostics.DroppedEventCount.Should().Be(0);
        replay.Diagnostics.StaleEventCount.Should().Be(0);
        replay.Diagnostics.MetadataOnly.Should().BeTrue();
        replay.Diagnostics.RedactionPass.Should().BeTrue();
    }

    [Fact(DisplayName = "AgentRun replay latest returns newest run for owner")]
    public void ReplayLatest_ShouldReturnNewestOwnerRun()
    {
        var now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        _service.UtcNowProvider = () => now;
        var ownerAOld = _service.CreateRun("owner A old", ownerHash: "usr_owner_a");
        _service.Complete(ownerAOld.RunId, "old done");

        now = now.AddMinutes(1);
        var ownerB = _service.CreateRun("owner B", ownerHash: "usr_owner_b");
        _service.Complete(ownerB.RunId, "owner b done");

        now = now.AddMinutes(1);
        var ownerANew = _service.CreateRun("owner A new", ownerHash: "usr_owner_a");
        _service.Append(ownerANew.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));

        _service.ReplayLatest("usr_owner_a")!.Summary.RunId.Should().Be(ownerANew.RunId);
        _service.ReplayLatest("usr_owner_b")!.Summary.RunId.Should().Be(ownerB.RunId);
        _service.ReplayLatest("usr_missing").Should().BeNull();
    }

    [Fact(DisplayName = "AgentRun stream token validates owner run binding expiry and single use")]
    public void StreamToken_ShouldValidateOwnerRunExpiryAndSingleUse()
    {
        var now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        _service.UtcNowProvider = () => now;
        var run = _service.CreateRun("token", ownerHash: "usr_owner_a");

        _service.IssueStreamToken(run.RunId, "usr_owner_b").Should().BeNull();
        var token = _service.IssueStreamToken(run.RunId, "usr_owner_a", TimeSpan.FromSeconds(60));
        token.Should().NotBeNullOrWhiteSpace();

        _service.ValidateStreamToken("ar_other", token, consume: false)
            .FailureReason.Should().Be("run_mismatch");

        var preflight = _service.ValidateStreamToken(run.RunId, token, consume: false);
        preflight.Authorized.Should().BeTrue();
        preflight.OwnerHash.Should().Be("usr_owner_a");

        _service.ValidateStreamToken(run.RunId, token, consume: true).Authorized.Should().BeTrue();
        _service.ValidateStreamToken(run.RunId, token, consume: true)
            .FailureReason.Should().Be("unknown_token");

        var expiring = _service.IssueStreamToken(run.RunId, "usr_owner_a", TimeSpan.FromSeconds(60));
        now = now.AddSeconds(61);
        _service.ValidateStreamToken(run.RunId, expiring, consume: false)
            .FailureReason.Should().Be("expired_token");
    }

    [Fact(DisplayName = "AgentRun stream token clamps unsafe TTL and rejects missing token")]
    public void StreamToken_ShouldClampUnsafeTtlAndRejectMissingToken()
    {
        var now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        _service.UtcNowProvider = () => now;
        var run = _service.CreateRun("ttl", ownerHash: "usr_owner_ttl");

        _service.ValidateStreamToken(run.RunId, null, consume: false)
            .FailureReason.Should().Be("missing_token");

        var longTtlToken = _service.IssueStreamToken(run.RunId, "usr_owner_ttl", TimeSpan.FromMinutes(5));
        now = now.AddSeconds(46);

        _service.ValidateStreamToken(run.RunId, longTtlToken, consume: false)
            .FailureReason.Should().Be("expired_token");

        now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        var negativeTtlToken = _service.IssueStreamToken(run.RunId, "usr_owner_ttl", TimeSpan.FromSeconds(-1));
        now = now.AddSeconds(46);

        _service.ValidateStreamToken(run.RunId, negativeTtlToken, consume: false)
            .FailureReason.Should().Be("expired_token");
    }

    [Fact(DisplayName = "AgentRun append after terminal is ignored")]
    public void AppendAfterTerminal_ShouldBeIgnored()
    {
        var run = _service.CreateRun("terminal");
        _service.Complete(run.RunId, "done");

        var appended = _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner"));

        appended.Should().BeNull();
        var replay = _service.Replay(run.RunId)!;
        replay.Events.Should().HaveCount(3);
        replay.Summary.DroppedEventCount.Should().Be(1);
        replay.Diagnostics.DroppedEventCount.Should().Be(1);
    }

    [Fact(DisplayName = "AgentRun append after terminal persists dropped count across restart")]
    public void AppendAfterTerminal_ShouldPersistDroppedCountAcrossRestart()
    {
        var run = _service.CreateRun("terminal restart");
        _service.Complete(run.RunId, "done");

        _service.Append(run.RunId, Draft(AgentRunEventTypes.StageStarted, "planner")).Should().BeNull();

        var reloaded = new AgentRunEventStreamService(new AgentRunEventStore(_directory, _redactor), _redactor);
        var replay = reloaded.Replay(run.RunId)!;

        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Completed);
        replay.Summary.DroppedEventCount.Should().Be(1);
        replay.Diagnostics.DroppedEventCount.Should().Be(1);
    }

    [Fact(DisplayName = "AgentRun JSONL storage writes metadata-only events and summaries")]
    public void Storage_ShouldWriteMetadataOnlyJsonl()
    {
        var run = _service.CreateRun("jsonl");
        _service.Append(run.RunId, Draft(AgentRunEventTypes.ArtifactCreated, "artifact"));

        File.Exists(_store.EventPath).Should().BeTrue();
        File.Exists(_store.SummaryPath).Should().BeTrue();

        foreach (var line in File.ReadLines(_store.EventPath).Concat(File.ReadLines(_store.SummaryPath)))
        {
            using var document = JsonDocument.Parse(line);
            document.RootElement.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
            document.RootElement.GetProperty("redactionPass").GetBoolean().Should().BeTrue();
            _redactor.IsRedactionSafeText(line).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "AgentRun JSONL storage compacts repeated summaries to latest per run")]
    public void Storage_ShouldCompactRepeatedSummariesToLatestPerRun()
    {
        var store = CreateCompactingStore(maxEventsPerRun: 128);
        var service = new AgentRunEventStreamService(store, _redactor);
        var now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        service.UtcNowProvider = () => now;
        var run = service.CreateRun("compact summaries");

        for (var i = 0; i < 6; i++)
        {
            now = now.AddSeconds(1);
            service.Append(run.RunId, Draft(AgentRunEventTypes.ToolCallCompleted, "planner"));
        }

        File.ReadLines(store.SummaryPath).Should().HaveCount(1);
        var summary = store.LoadSummary(run.RunId);
        summary.Should().NotBeNull();
        summary!.LastSequence.Should().Be(8);
        summary.EventCount.Should().Be(8);
    }

    [Fact(DisplayName = "AgentRun JSONL storage compaction keeps first and recent events for long runs")]
    public void Storage_ShouldCompactLongRunEventsToFirstAndRecentEvents()
    {
        var options = CompactingOptions(maxEventsPerRun: 4);
        var store = new AgentRunEventStore(_directory, _redactor, options);
        var service = new AgentRunEventStreamService(store, _redactor);
        var run = service.CreateRun("compact long run");

        for (var i = 0; i < 6; i++)
        {
            service.Append(run.RunId, Draft(AgentRunEventTypes.ToolCallCompleted, "planner"));
        }

        File.ReadLines(store.EventPath).Should().HaveCount(4);
        var reloaded = new AgentRunEventStreamService(new AgentRunEventStore(_directory, _redactor, options), _redactor);
        var replay = reloaded.Replay(run.RunId)!;

        replay.Events.Select(evt => evt.Sequence).Should().Equal(1, 6, 7, 8);
        replay.Summary.EventCount.Should().Be(8);
        replay.Summary.LastSequence.Should().Be(8);

        var appended = reloaded.Append(run.RunId, Draft(AgentRunEventTypes.StageCompleted, "planner"));
        appended.Should().NotBeNull();
        appended!.Sequence.Should().Be(9);
    }

    [Theory(DisplayName = "AgentRun required event type is publishable")]
    [InlineData(AgentRunEventTypes.RunStarted)]
    [InlineData(AgentRunEventTypes.AssistantBrief)]
    [InlineData(AgentRunEventTypes.PlanCreated)]
    [InlineData(AgentRunEventTypes.PlanStarted)]
    [InlineData(AgentRunEventTypes.PlanContextStarted)]
    [InlineData(AgentRunEventTypes.PlanContextCompleted)]
    [InlineData(AgentRunEventTypes.PlanModelStarted)]
    [InlineData(AgentRunEventTypes.PlanModelCompleted)]
    [InlineData(AgentRunEventTypes.PlanModelTimeout)]
    [InlineData(AgentRunEventTypes.PlanModelFailed)]
    [InlineData(AgentRunEventTypes.PlanContractStarted)]
    [InlineData(AgentRunEventTypes.PlanContractCompleted)]
    [InlineData(AgentRunEventTypes.PlanSafetyCompleted)]
    [InlineData(AgentRunEventTypes.PlanFallbackUsed)]
    [InlineData(AgentRunEventTypes.PlanCompleted)]
    [InlineData(AgentRunEventTypes.PlanFailed)]
    [InlineData(AgentRunEventTypes.PlanCancelled)]
    [InlineData(AgentRunEventTypes.StageStarted)]
    [InlineData(AgentRunEventTypes.StageCompleted)]
    [InlineData(AgentRunEventTypes.ToolCallStarted)]
    [InlineData(AgentRunEventTypes.ToolCallCompleted)]
    [InlineData(AgentRunEventTypes.ToolCallFailed)]
    [InlineData(AgentRunEventTypes.ToolLoopStarted)]
    [InlineData(AgentRunEventTypes.ToolLoopRoundStarted)]
    [InlineData(AgentRunEventTypes.ToolCallRequested)]
    [InlineData(AgentRunEventTypes.ToolCallLoopCompleted)]
    [InlineData(AgentRunEventTypes.ToolCallDenied)]
    [InlineData(AgentRunEventTypes.ToolResultAppended)]
    [InlineData(AgentRunEventTypes.ToolLoopFinalized)]
    [InlineData(AgentRunEventTypes.ToolLoopDraftAccepted)]
    [InlineData(AgentRunEventTypes.ToolLoopDraftRejected)]
    [InlineData(AgentRunEventTypes.ToolLoopFallback)]
    [InlineData(AgentRunEventTypes.ToolLoopFailed)]
    [InlineData(AgentRunEventTypes.WorkflowDraftUpdated)]
    [InlineData(AgentRunEventTypes.ReadinessChecked)]
    [InlineData(AgentRunEventTypes.PackageReadinessChecked)]
    [InlineData(AgentRunEventTypes.ManifestDryRunCompleted)]
    [InlineData(AgentRunEventTypes.StationCompatibilityCompleted)]
    [InlineData(AgentRunEventTypes.OperatorContractCompleted)]
    [InlineData(AgentRunEventTypes.ReleaseReviewCompleted)]
    [InlineData(AgentRunEventTypes.ArtifactCreated)]
    [InlineData(AgentRunEventTypes.RunCompleted)]
    [InlineData(AgentRunEventTypes.RunFailed)]
    [InlineData(AgentRunEventTypes.RunCancelled)]
    public void RequiredEventType_ShouldBePublishable(string eventType)
    {
        var run = _service.CreateRun($"event type {eventType}");

        var evt = eventType switch
        {
            AgentRunEventTypes.RunCompleted => _service.Complete(run.RunId, "complete"),
            AgentRunEventTypes.RunFailed => _service.Fail(run.RunId, "failed", "Fix public metadata."),
            AgentRunEventTypes.RunCancelled => _service.Cancel(run.RunId),
            _ => _service.Append(run.RunId, Draft(eventType, "contract"))
        };

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be(eventType);
        evt.MetadataOnly.Should().BeTrue();
        evt.RedactionPass.Should().BeTrue();
    }

    [Theory(DisplayName = "AgentRun redacts unsafe public event metadata")]
    [MemberData(nameof(UnsafePayloadCases))]
    public void UnsafeMetadata_ShouldBeRedacted(string name, object payload, string forbiddenNeedle)
    {
        var run = _service.CreateRun($"redaction {name}");

        var evt = _service.Append(run.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolCallCompleted,
            Stage = "redaction",
            Title = "Redaction test",
            Summary = "Unsafe payload metadata should not leak.",
            Status = AgentRunEventStatuses.Completed,
            Payload = payload
        });

        evt.Should().NotBeNull();
        evt!.MetadataOnly.Should().BeTrue();
        evt.RedactionPass.Should().BeTrue();
        var json = JsonSerializer.Serialize(evt, AgentRunEventJson.Options);
        _redactor.IsRedactionSafeText(json).Should().BeTrue();
        json.Should().NotContain(forbiddenNeedle);
    }

    [Fact(DisplayName = "AgentRun completed event keeps editable draft flow with pending path parameters")]
    public void Complete_ShouldPublishEditableDraftFlowWithPendingPathParameters()
    {
        var run = _service.CreateRun("draft flow");
        const string stableDraftId = "9b852389-d100-430b-8105-e028c11b89fd";

        var evt = _service.Complete(run.RunId, "done", new
        {
            flow = new
            {
                operators = new object[]
                {
                    new
                    {
                        id = "op_detect",
                        operatorType = "DeepLearning",
                        parameters = new Dictionary<string, string>
                        {
                            ["ParameterId"] = stableDraftId,
                            ["ModelPath"] = "<pending-model-resource>",
                            ["FilePath"] = "",
                            ["TargetClasses"] = "熟透"
                        }
                    }
                },
                connections = Array.Empty<object>()
            },
            applyGate = new
            {
                canvasApplyReady = true,
                deploymentReady = false
            },
            metadataOnly = true
        });

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be(AgentRunEventTypes.RunCompleted);
        evt.Title.Should().Be("Run completed");

        var json = JsonSerializer.Serialize(evt, AgentRunEventJson.Options);
        json.Should().Contain("\"flow\"");
        json.Should().Contain("\"ModelPath\"");
        json.Should().Contain("pending-model-resource");
        json.Should().Contain(stableDraftId);
        json.Should().Contain("\"canvasApplyReady\":true");
        json.Should().NotContain("[redacted:plc-address]");
        json.Should().NotContain("Unsafe metadata was removed");
        _redactor.IsRedactionSafeText(json).Should().BeTrue();
    }

    [Fact(DisplayName = "AgentRun completed event redacts real path values without removing draft flow")]
    public void Complete_ShouldRedactRealPathValuesWithoutRemovingDraftFlow()
    {
        var run = _service.CreateRun("draft flow path redaction");

        var evt = _service.Complete(run.RunId, "done", new
        {
            flow = new
            {
                operators = new object[]
                {
                    new
                    {
                        id = "op_detect",
                        operatorType = "DeepLearning",
                        parameters = new Dictionary<string, string>
                        {
                            ["ModelPath"] = @"C:\factory\models\ripe-strawberry.onnx",
                            ["FilePath"] = @"D:\samples\strawberry.png",
                            ["TargetClasses"] = "熟透"
                        }
                    }
                },
                connections = Array.Empty<object>()
            },
            metadataOnly = true
        });

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be(AgentRunEventTypes.RunCompleted);
        evt.Title.Should().Be("Run completed");

        var json = JsonSerializer.Serialize(evt, AgentRunEventJson.Options);
        json.Should().Contain("\"flow\"");
        json.Should().Contain("[redacted:path]");
        json.Should().NotContain(@"C:\factory");
        json.Should().NotContain(@"D:\samples");
        json.Should().NotContain("ripe-strawberry.onnx");
        json.Should().NotContain("strawberry.png");
        json.Should().NotContain("Unsafe metadata was removed");
        _redactor.IsRedactionSafeText(json).Should().BeTrue();
    }

    public static IEnumerable<object[]> UnsafePayloadCases()
    {
        yield return ["authorization-header", new { Authorization = "Bearer test-secret-value" }, "test-secret-value"];
        yield return ["x-api-key", new { x_api_key = "sk-live-value" }, "sk-live-value"];
        yield return ["bearer-text", new { summary = "Bearer abcdefghijklmnop" }, "abcdefghijklmnop"];
        yield return ["ip-address", new { stationAddress = "192.168.10.45" }, "192.168.10.45"];
        yield return ["windows-path", new { modelPath = @"C:\factory\models\part.onnx" }, @"C:\factory\models\part.onnx"];
        yield return ["unix-path", new { templatePath = "/home/operator/templates/wire.json" }, "/home/operator/templates/wire.json"];
        yield return ["image-data-uri", new { preview = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAA" }, "data:image/png"];
        yield return ["package-path", new { packagePath = @"D:\deploy\run.cvpkg" }, "run.cvpkg"];
        yield return ["plc-url", new { endpoint = "plc://192.168.1.8/D100" }, "plc://192.168.1.8"];
        yield return ["plc-register", new { plcAddress = "D100" }, "D100"];
        yield return ["long-base64", new { image = new string('A', 120) }, new string('A', 96)];
        yield return ["raw-prompt-key", new { rawPrompt = "do not publish" }, "rawPrompt"];
        yield return ["system-prompt-marker", new { summary = "systemPrompt=hidden instruction" }, "systemPrompt"];
        yield return ["chain-of-thought-key", new { chainOfThought = "private reasoning" }, "chainOfThought"];
        yield return ["reasoning-content-marker", new { summary = "reasoning_content: private trace" }, "reasoning_content"];
    }

    private static AgentRunEventDraft Draft(string eventType, string stage)
    {
        return new AgentRunEventDraft
        {
            EventType = eventType,
            Stage = stage,
            Title = $"{stage} title",
            Summary = $"{stage} summary",
            Status = AgentRunEventStatuses.Running,
            Payload = new
            {
                status = "running",
                metadataOnly = true
            }
        };
    }

    private AgentRunEventStore CreateCompactingStore(int maxEventsPerRun)
    {
        return new AgentRunEventStore(_directory, _redactor, CompactingOptions(maxEventsPerRun));
    }

    private static AgentRunEventStoreOptions CompactingOptions(int maxEventsPerRun)
    {
        return new AgentRunEventStoreOptions
        {
            CompactionAppendThreshold = 1,
            CompactionSizeThresholdBytes = 1,
            MaxSummaryRuns = 10,
            MaxEventsPerRun = maxEventsPerRun
        };
    }
}
