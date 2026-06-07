using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public static class AgentRunEventTypes
{
    public const string RunStarted = "run.started";
    public const string AssistantBrief = "assistant.brief";
    public const string StageStarted = "stage.started";
    public const string StageCompleted = "stage.completed";
    public const string ToolCallStarted = "tool.call.started";
    public const string ToolCallCompleted = "tool.call.completed";
    public const string ToolCallFailed = "tool.call.failed";
    public const string WorkflowDraftUpdated = "workflow.draft.updated";
    public const string ReadinessChecked = "readiness.checked";
    public const string PackageReadinessChecked = "package.readiness.checked";
    public const string ManifestDryRunCompleted = "manifest.dryrun.completed";
    public const string StationCompatibilityCompleted = "station.compatibility.completed";
    public const string OperatorContractCompleted = "operator.contract.completed";
    public const string ReleaseReviewCompleted = "release.review.completed";
    public const string ArtifactCreated = "artifact.created";
    public const string RunCompleted = "run.completed";
    public const string RunFailed = "run.failed";
    public const string RunCancelled = "run.cancelled";
}

public static class AgentRunEventStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Blocked = "blocked";
}

public sealed record AgentRunEvent
{
    public string RunId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Status { get; init; } = AgentRunEventStatuses.Running;
    public object? Payload { get; init; }
    public bool MetadataOnly { get; init; } = true;
    public bool RedactionPass { get; init; } = true;
}

public sealed record AgentRunEventDraft
{
    public string EventType { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Status { get; init; } = AgentRunEventStatuses.Running;
    public object? Payload { get; init; }
    public bool MetadataOnly { get; init; } = true;
}

public sealed record AgentRunSummary
{
    public string RunId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string Status { get; init; } = AgentRunEventStatuses.Running;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string FirstFixRecommendation { get; init; } = string.Empty;
    public long LastSequence { get; init; }
    public int EventCount { get; init; }
    public bool MetadataOnly { get; init; } = true;
    public bool RedactionPass { get; init; } = true;
    public object? Payload { get; init; }
}

public sealed record AgentRunCreateResult(
    string RunId,
    string Brief,
    IReadOnlyList<AgentRunEvent> Events);

public sealed record AgentRunReplayResult(
    AgentRunSummary Summary,
    IReadOnlyList<AgentRunEvent> Events);

public static class AgentRunEventJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
