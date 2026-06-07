using System.Text.Json;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.AgentRun;

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
        _service.Replay(run.RunId)!.Events.Should().HaveCount(3);
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

    [Theory(DisplayName = "AgentRun required event type is publishable")]
    [InlineData(AgentRunEventTypes.RunStarted)]
    [InlineData(AgentRunEventTypes.AssistantBrief)]
    [InlineData(AgentRunEventTypes.StageStarted)]
    [InlineData(AgentRunEventTypes.StageCompleted)]
    [InlineData(AgentRunEventTypes.ToolCallStarted)]
    [InlineData(AgentRunEventTypes.ToolCallCompleted)]
    [InlineData(AgentRunEventTypes.ToolCallFailed)]
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
        yield return ["long-base64", new { image = new string('A', 120) }, new string('A', 96)];
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
}
