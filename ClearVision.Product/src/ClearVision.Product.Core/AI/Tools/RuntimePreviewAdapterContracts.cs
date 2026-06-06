using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Core.Entities;

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

    [JsonPropertyName("requestedAdapterName")]
    public string? RequestedAdapterName { get; init; }

    [JsonIgnore]
    public VisionAgentToolContext Context { get; init; } = new();

    [JsonIgnore]
    public RuntimePreviewPilotConfig PilotConfig => Context.RuntimePreviewPilot.CloneNormalized();

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

    [JsonPropertyName("permissionDecision")]
    public RuntimePreviewPermissionDecision PermissionDecision { get; init; } = RuntimePreviewPermissionDecision.AllowOffline();

    [JsonPropertyName("resourceTrace")]
    public RuntimePreviewResourceTrace ResourceTrace { get; init; } = RuntimePreviewResourceTrace.NotEvaluated();

    [JsonPropertyName("fallback")]
    public RuntimePreviewFallbackInfo Fallback { get; init; } = RuntimePreviewFallbackInfo.NotUsed();

    [JsonPropertyName("replaySummary")]
    public object? ReplaySummary { get; init; }

    [JsonPropertyName("issues")]
    public IReadOnlyList<object> Issues { get; init; } = [];

    [JsonPropertyName("pendingActions")]
    public IReadOnlyList<VisionAgentPendingAction> PendingActions { get; init; } = [];

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

public sealed record RuntimePreviewPermissionDecision
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; }

    [JsonPropertyName("pilotEnabled")]
    public bool PilotEnabled { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("requestedAdapterName")]
    public string? RequestedAdapterName { get; init; }

    [JsonPropertyName("effectiveAdapterName")]
    public string EffectiveAdapterName { get; init; } = string.Empty;

    [JsonPropertyName("allowlistCounts")]
    public object? AllowlistCounts { get; init; }

    public static RuntimePreviewPermissionDecision AllowOffline()
    {
        return new RuntimePreviewPermissionDecision
        {
            Allowed = true,
            ReasonCode = "runtime_preview_offline_metadata_only",
            Reason = "Offline RuntimePreview returns metadata only.",
            RuntimePreviewConsent = true,
            PilotEnabled = false,
            EffectiveAdapterName = "offline_runtime_preview"
        };
    }
}

public sealed record RuntimePreviewResourceTrace
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; init; } = string.Empty;

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    [JsonPropertyName("resourceId")]
    public string? ResourceId { get; init; }

    [JsonPropertyName("normalizedKey")]
    public string NormalizedKey { get; init; } = string.Empty;

    [JsonPropertyName("missingResources")]
    public IReadOnlyList<object> MissingResources { get; init; } = [];

    [JsonPropertyName("trace")]
    public IReadOnlyList<object> Trace { get; init; } = [];

    public static RuntimePreviewResourceTrace NotEvaluated()
    {
        return new RuntimePreviewResourceTrace
        {
            Allowed = true,
            ReasonCode = "runtime_preview_resource_trace_not_evaluated",
            ResourceType = "offline_metadata"
        };
    }
}

public sealed record RuntimePreviewFallbackInfo
{
    [JsonPropertyName("used")]
    public bool Used { get; init; }

    [JsonPropertyName("fallbackAdapterName")]
    public string? FallbackAdapterName { get; init; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    public static RuntimePreviewFallbackInfo NotUsed() => new();
}

public static class RuntimePreviewModes
{
    public const string OfflineFixture = "offline_fixture";
    public const string MetadataOnly = RuntimePreviewPilotConfig.ModeMetadataOnly;
}
