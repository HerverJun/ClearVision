using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Core.Enums;

namespace Acme.Product.Runtime.Abstractions;

public enum RuntimeHostState
{
    Idle = 0,
    Loaded = 1,
    Running = 2,
    Stopping = 3,
    Faulted = 4
}

public enum RuntimeRunOutcome
{
    Ok = 0,
    Ng = 1,
    Error = 2,
    Canceled = 3
}

public enum RuntimeIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed class RuntimeFieldExtensions
{
    public string? StationProfile { get; set; }

    public string? TriggerProfile { get; set; }

    public string? ResultMappingProfile { get; set; }

    public string? ModelAssets { get; set; }

    public string? RuntimeParameters { get; set; }

    public string? DefaultSiteProfile { get; set; }
}

public enum RuntimeParameterValueType
{
    Number = 0
}

public enum RuntimeParameterUiKind
{
    NumericInput = 0
}

public enum RuntimeParameterApplyMode
{
    NextRun = 0
}

public sealed class RuntimeParameterSchema
{
    public string SchemaVersion { get; set; } = "1.0";

    public string PackageId { get; set; } = string.Empty;

    public string FlowHash { get; set; } = string.Empty;

    public List<RuntimeParameterDefinition> Parameters { get; set; } = [];
}

public sealed class RuntimeParameterDefinition
{
    public string Id { get; set; } = string.Empty;

    public Guid OperatorId { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string OperatorType { get; set; } = string.Empty;

    public string ParameterName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string GroupName { get; set; } = "现场参数";

    public RuntimeParameterValueType ValueType { get; set; } = RuntimeParameterValueType.Number;

    public RuntimeParameterUiKind UiKind { get; set; } = RuntimeParameterUiKind.NumericInput;

    public JsonElement DefaultValue { get; set; } = JsonSerializer.SerializeToElement(0d);

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Step { get; set; }

    public bool SiteTunable { get; set; } = true;

    public bool RequiresEngineerMode { get; set; } = true;

    public RuntimeParameterApplyMode ApplyMode { get; set; } = RuntimeParameterApplyMode.NextRun;

    public int Order { get; set; }
}

public sealed class RuntimeSiteProfile
{
    public string ProfileVersion { get; set; } = "1.0";

    public string ProfileId { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string FlowHash { get; set; } = string.Empty;

    public int Revision { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string UpdatedBy { get; set; } = "local-engineer";

    public List<RuntimeParameterOverride> Overrides { get; set; } = [];
}

public sealed class RuntimeParameterOverride
{
    public string ParameterId { get; set; } = string.Empty;

    public JsonElement Value { get; set; } = JsonSerializer.SerializeToElement(0d);
}

public sealed class RuntimePackageManifest
{
    public string PackageId { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public string RuntimeApiVersion { get; set; } = "1.0";

    public string MinStationVersion { get; set; } = "0.1.0";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string CreatedBy { get; set; } = "ClearVision Studio";

    public Guid SourceProjectId { get; set; }

    public string EntryFlow { get; set; } = "flow.json";

    public string FlowHash { get; set; } = string.Empty;

    public string OperatorCatalogVersion { get; set; } = string.Empty;

    public bool ExportAllowed { get; set; } = true;

    public List<string> PendingParameters { get; set; } = [];

    public List<string> MissingResources { get; set; } = [];

    public RuntimeFieldExtensions FieldExtensions { get; set; } = new();
}

public sealed class RuntimeProfile
{
    public string RuntimeApiVersion { get; set; } = "1.0";

    public int SingleRunTimeoutMs { get; set; } = 30_000;

    public int StopTimeoutMs { get; set; } = 5_000;

    public int DirectoryReplayMaxFileCount { get; set; } = 1_000;

    public int ResultRecordQueueCapacity { get; set; } = 256;

    public int ImageQueueCapacity { get; set; } = 64;

    public int RecentResultsLimit { get; set; } = 50;

    public int LogRetainedLineCount { get; set; } = 200;

    public bool EnableResultJsonl { get; set; } = true;

    public bool SaveOkImages { get; set; }

    public bool SaveNgImages { get; set; } = true;

    public bool SaveErrorImages { get; set; } = true;

    public List<string> SupportedInputExtensions { get; set; } =
    [
        ".bmp",
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff"
    ];
}

public sealed class RuntimeValidationReport
{
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsValid { get; set; }

    public string? FlowHash { get; set; }

    public List<string> Errors { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<string> Notes { get; set; } = [];
}

public sealed class RuntimePackageValidationIssue
{
    public RuntimeIssueSeverity Severity { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? RelativePath { get; set; }
}

public sealed class RuntimePackageValidationResult
{
    public List<RuntimePackageValidationIssue> Issues { get; set; } = [];

    [JsonIgnore]
    public bool IsValid => Issues.All(issue => issue.Severity != RuntimeIssueSeverity.Error);

    public string ToUserMessage()
    {
        if (Issues.Count == 0)
        {
            return "Package validation passed.";
        }

        return string.Join(
            Environment.NewLine,
            Issues.Select(issue =>
            {
                var pathSuffix = string.IsNullOrWhiteSpace(issue.RelativePath)
                    ? string.Empty
                    : $" ({issue.RelativePath})";
                return $"[{issue.Severity}] {issue.Code}: {issue.Message}{pathSuffix}";
            }));
    }
}

public sealed class RuntimeNormalizedResult
{
    public string RunId { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public string FlowHash { get; set; } = string.Empty;

    public string ImageId { get; set; } = string.Empty;

    public string? SourceImagePath { get; set; }

    public RuntimeRunOutcome Outcome { get; set; }

    public InspectionStatus? InspectionStatus { get; set; }

    public long ExecutionTimeMs { get; set; }

    public string DiagnosticCode { get; set; } = string.Empty;

    public string? DiagnosticMessage { get; set; }

    public bool HasJudgmentSignal { get; set; }

    public string? SavedImagePath { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public Dictionary<string, object?> PrimaryOutputs { get; set; } = [];

    [JsonIgnore]
    public byte[]? OutputImageBytes { get; set; }

    [JsonIgnore]
    public byte[]? SourceImageBytes { get; set; }
}

public sealed class RuntimeHostSnapshot
{
    public RuntimeHostState State { get; set; }

    public string? PackageId { get; set; }

    public string? PackageName { get; set; }

    public string? FlowHash { get; set; }

    public string? CurrentRunId { get; set; }

    public int SessionOkCount { get; set; }

    public int SessionNgCount { get; set; }

    public int SessionErrorCount { get; set; }
}

public sealed class RuntimeStopSummary
{
    public bool WasRunning { get; set; }

    public bool Completed { get; set; }

    public bool TimedOut { get; set; }

    public int PendingCount { get; set; }

    public int DroppedCount { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class StationLocalSettings
{
    public string? StationId { get; set; }

    public string? StationName { get; set; }

    public string? LineName { get; set; }

    public string? AreaName { get; set; }

    public string? WorkcellName { get; set; }

    public string? InspectionNodeName { get; set; }

    public string? CameraAlias { get; set; }

    public string? StationRole { get; set; }

    public string? Owner { get; set; }

    public string? LastGoodPackagePath { get; set; }

    public string? LastRunId { get; set; }

    public string? CurrentPackageVersion { get; set; }

    public long LastHealthSequenceId { get; set; }

    public long LastLogSequenceId { get; set; }

    public DateTimeOffset? LastUnexpectedExitAtUtc { get; set; }
}
