using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClearVision.Product.Core.AI.Tools;

public interface IRuntimePreviewAdapter
{
    string Name { get; }
    IReadOnlySet<string> SupportedToolNames { get; }

    Task<RuntimePreviewResult> ExecuteAsync(
        RuntimePreviewRequest request,
        CancellationToken cancellationToken);
}

public sealed record RuntimePreviewRequest
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("adapterName")]
    public string? AdapterName { get; init; }

    [JsonPropertyName("previewMode")]
    public string PreviewMode { get; init; } = RuntimePreviewModes.OfflineFixture;

    [JsonIgnore]
    public VisionAgentToolContext Context { get; init; } = new();

    [JsonIgnore]
    public JsonElement Arguments { get; init; }
}

public sealed record RuntimePreviewResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("adapterName")]
    public string AdapterName { get; init; } = string.Empty;

    [JsonPropertyName("previewMode")]
    public string PreviewMode { get; init; } = RuntimePreviewModes.OfflineFixture;

    [JsonPropertyName("previewReady")]
    public bool PreviewReady { get; init; }

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("frameSource")]
    public string FrameSource { get; init; } = "offline_fixture_metadata";

    [JsonPropertyName("frameId")]
    public string? FrameId { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "runtime_preview_offline_adapter";

    [JsonPropertyName("replaySummary")]
    public object? ReplaySummary { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<object> Warnings { get; init; } = [];

    [JsonPropertyName("blockingIssues")]
    public IReadOnlyList<object> BlockingIssues { get; init; } = [];

    [JsonPropertyName("missingResources")]
    public IReadOnlyList<object> MissingResources { get; init; } = [];

    [JsonPropertyName("artifacts")]
    public IReadOnlyList<RuntimePreviewArtifactSummary> Artifacts { get; init; } = [];

    [JsonPropertyName("binaryIncluded")]
    public bool BinaryIncluded { get; init; }

    [JsonPropertyName("capturedRealFrame")]
    public bool CapturedRealFrame { get; init; }

    [JsonPropertyName("loadedModelFiles")]
    public bool LoadedModelFiles { get; init; }

    [JsonPropertyName("accessedHardware")]
    public bool AccessedHardware { get; init; }

    [JsonPropertyName("stationTouched")]
    public bool StationTouched { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    public static RuntimePreviewResult Fail(
        string adapterName,
        string errorCode,
        string errorMessage,
        IReadOnlyList<object>? blockingIssues = null)
    {
        return new RuntimePreviewResult
        {
            Success = false,
            AdapterName = adapterName,
            PreviewReady = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            BlockingIssues = blockingIssues ?? new object[]
            {
                new
                {
                    code = errorCode,
                    message = errorMessage
                }
            }
        };
    }
}

public static class RuntimePreviewModes
{
    public const string OfflineFixture = "offline_fixture";
}
