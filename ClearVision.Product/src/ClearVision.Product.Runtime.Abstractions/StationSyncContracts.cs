using System.Text.Json.Serialization;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Runtime.Abstractions;

/// <summary>
/// Shared defaults for Station-to-Studio sync contracts.
/// </summary>
public static class StationSyncContractDefaults
{
    /// <summary>
    /// Current schema version for Station sync payloads.
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>
    /// SignalR hub route used by Station clients.
    /// </summary>
    public const string HubPath = "/hubs/station-ingest";

    /// <summary>
    /// Header used for shared-token Station ingress authentication.
    /// </summary>
    public const string StationTokenHeaderName = "X-ClearVision-Station-Token";
}

/// <summary>
/// SignalR hub method names used by Station clients and Studio.
/// </summary>
public static class StationHubMethods
{
    public const string Probe = nameof(Probe);
    public const string RegisterStationAsync = nameof(RegisterStationAsync);
    public const string RegisterStation = nameof(RegisterStation);
    public const string PushHeartbeatAsync = nameof(PushHeartbeatAsync);
    public const string Heartbeat = nameof(Heartbeat);
    public const string PushSnapshotAsync = nameof(PushSnapshotAsync);
    public const string PushHealth = nameof(PushHealth);
    public const string PushResultSummaryAsync = nameof(PushResultSummaryAsync);
    public const string ReportResultGap = nameof(ReportResultGap);
    public const string PushResult = nameof(PushResult);
    public const string PushLog = nameof(PushLog);
    public const string GetReplayCursor = nameof(GetReplayCursor);
    public const string PollCommand = nameof(PollCommand);
    public const string ReportCommandResult = nameof(ReportCommandResult);
}

/// <summary>
/// Studio-side online state for a Station.
/// </summary>
public enum StationOnlineState
{
    Unknown = 0,
    Online = 1,
    Warning = 2,
    Degraded = 3,
    Critical = 4,
    Offline = 5
}

/// <summary>
/// Cross-node runtime state used by Station telemetry.
/// </summary>
public enum StationRuntimeState
{
    Unknown = 0,
    Idle = 1,
    Running = 2,
    Paused = 3,
    LoadingPackage = 4,
    Faulted = 5,
    Stopping = 6
}

/// <summary>
/// Cross-node result outcome for monitoring and statistics.
/// </summary>
public enum StationResultOutcome
{
    Unknown = 0,
    Ok = 1,
    Ng = 2,
    Error = 3
}

/// <summary>
/// Remote command type supported by the first Station command state machine.
/// </summary>
public enum StationCommandType
{
    Ping = 0,
    StartRuntime = 1,
    StopRuntime = 2,
    ReloadPackage = 3,
    DeployPackage = 4,
    ApplySiteProfile = 5,
    CollectLogs = 6
}

/// <summary>
/// Remote command lifecycle status.
/// </summary>
public enum StationCommandStatus
{
    Created = 0,
    Delivered = 1,
    Accepted = 2,
    Rejected = 3,
    Running = 4,
    Succeeded = 5,
    Failed = 6,
    TimedOut = 7,
    Cancelled = 8
}

/// <summary>
/// Runtime package deployment state used by Studio and Station.
/// </summary>
public enum StationPackageState
{
    Unknown = 0,
    Available = 1,
    Downloading = 2,
    Staged = 3,
    Active = 4,
    Failed = 5,
    RolledBack = 6
}

/// <summary>
/// Studio-side package purpose for Station deployment.
/// </summary>
public enum StationPackageKind
{
    Production = 0,
    Test = 1
}

/// <summary>
/// Registers a Station with the central Studio ingress hub.
/// </summary>
public sealed class StationRegistrationDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Business display name configured for the Station.</summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>Optional production line name.</summary>
    public string? LineName { get; set; }

    /// <summary>Optional production area name.</summary>
    public string? AreaName { get; set; }

    /// <summary>Optional workcell name.</summary>
    public string? WorkcellName { get; set; }

    /// <summary>Optional inspection node name.</summary>
    public string? InspectionNodeName { get; set; }

    /// <summary>Optional camera alias used by operators.</summary>
    public string? CameraAlias { get; set; }

    /// <summary>Business role for the Station.</summary>
    public string StationRole { get; set; } = string.Empty;

    /// <summary>Owner or maintainer hint.</summary>
    public string? Owner { get; set; }

    /// <summary>Local machine name reported by the Station host.</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Station process identifier.</summary>
    public int ProcessId { get; set; }

    /// <summary>Station application version string.</summary>
    public string StationVersion { get; set; } = string.Empty;

    /// <summary>Runtime assembly or API version string.</summary>
    public string RuntimeVersion { get; set; } = string.Empty;

    /// <summary>Best-effort local IP hint.</summary>
    public string? IpAddressHint { get; set; }

    /// <summary>Privacy-preserving MAC address hash.</summary>
    public string? MacAddressHash { get; set; }

    /// <summary>Currently loaded package identifier.</summary>
    public string? CurrentPackageId { get; set; }

    /// <summary>Currently loaded package name.</summary>
    public string? CurrentPackageName { get; set; }

    /// <summary>Currently loaded package version.</summary>
    public string? CurrentPackageVersion { get; set; }

    /// <summary>When the Station process started.</summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the Station registration was emitted.</summary>
    public DateTimeOffset RegisteredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Compatibility alias used by early MVP clients.</summary>
    public string ClientVersion
    {
        get => string.IsNullOrWhiteSpace(StationVersion) ? RuntimeVersion : StationVersion;
        set => StationVersion = value ?? string.Empty;
    }
}

/// <summary>
/// Acknowledges Station registration and returns the current replay cursor.
/// </summary>
public sealed class StationRegisterAckDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Whether Studio accepted the registration.</summary>
    public bool Accepted { get; set; }

    /// <summary>Highest persisted result sequence known to Studio.</summary>
    public long LastPersistedSequenceId { get; set; }

    /// <summary>Optional operator-facing message.</summary>
    public string? Message { get; set; }

    /// <summary>Studio server timestamp.</summary>
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Acknowledges a lightweight Station ingress connectivity probe.
/// </summary>
public sealed class StationProbeAckDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Whether Studio accepted the probe.</summary>
    public bool Accepted { get; set; }

    /// <summary>Optional operator-facing message.</summary>
    public string? Message { get; set; }

    /// <summary>Studio server timestamp.</summary>
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Reports a lightweight heartbeat from Station to Studio.
/// </summary>
public sealed class StationHeartbeatDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Monotonic per-station message sequence.</summary>
    public long SequenceId { get; set; }

    /// <summary>Unique message identifier for idempotent diagnostics.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Optional production line name.</summary>
    public string? LineName { get; set; }

    /// <summary>Current Station runtime state.</summary>
    public StationRuntimeState RuntimeState { get; set; } = StationRuntimeState.Unknown;

    /// <summary>Human-readable connection state hint.</summary>
    public string ConnectionState { get; set; } = string.Empty;

    /// <summary>Currently loaded runtime package identifier.</summary>
    public string? CurrentPackageId { get; set; }

    /// <summary>Currently loaded runtime package name.</summary>
    public string? CurrentPackageName { get; set; }

    /// <summary>Currently loaded runtime package version.</summary>
    public string? CurrentPackageVersion { get; set; }

    /// <summary>Package-semantic flow hash from the manifest.</summary>
    public string? PackageFlowHash { get; set; }

    /// <summary>Actual execution-definition hash for the current snapshot.</summary>
    public string? ExecutionFlowHash { get; set; }

    /// <summary>Compatibility alias for ExecutionFlowHash.</summary>
    public string? FlowHash { get; set; }

    public Guid? ExecutionSnapshotId { get; set; }

    public long? ProjectRevision { get; set; }

    public string? DecisionConfigurationHash { get; set; }

    public string? ExecutionRunMode { get; set; }

    /// <summary>Current run identifier when the Station is actively processing.</summary>
    public string? CurrentRunId { get; set; }

    /// <summary>Session OK count reported by the runtime host.</summary>
    public int SessionOkCount { get; set; }

    /// <summary>Session NG count reported by the runtime host.</summary>
    public int SessionNgCount { get; set; }

    /// <summary>Session error count reported by the runtime host.</summary>
    public int SessionErrorCount { get; set; }

    /// <summary>
    /// Canonical session outcome statistics. Null means this is a legacy Station payload
    /// and the three compatibility counters must be projected in Studio.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InspectionOutcomeStatistics? SessionOutcomeStatistics { get; set; }

    /// <summary>Number of locally queued result summaries waiting for ACK.</summary>
    public int SpoolPendingCount { get; set; }

    /// <summary>Number of locally queued command-result reports waiting for ACK.</summary>
    public int CommandResultSpoolPendingCount { get; set; }

    /// <summary>Last completed result timestamp.</summary>
    public DateTimeOffset? LastResultAtUtc { get; set; }

    /// <summary>Station local offset from UTC in minutes.</summary>
    public int StationLocalOffsetMinutes { get; set; }

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Compatibility timestamp used by early MVP clients.</summary>
    public DateTimeOffset SentAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    /// <summary>Compatibility runtime state used by early MVP clients.</summary>
    public RuntimeHostState State
    {
        get => StationSyncStateMapper.ToRuntimeHostState(RuntimeState);
        set => RuntimeState = StationSyncStateMapper.ToStationRuntimeState(value);
    }

    /// <summary>Compatibility package identifier used by early MVP clients.</summary>
    public string? PackageId
    {
        get => CurrentPackageId;
        set => CurrentPackageId = value;
    }

    /// <summary>Compatibility package name used by early MVP clients.</summary>
    public string? PackageName
    {
        get => CurrentPackageName;
        set => CurrentPackageName = value;
    }
}

/// <summary>
/// Reports a debounced runtime snapshot change.
/// </summary>
public sealed class StationSnapshotDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Monotonic per-station message sequence.</summary>
    public long SequenceId { get; set; }

    /// <summary>Unique message identifier for idempotent diagnostics.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Optional production line name.</summary>
    public string? LineName { get; set; }

    /// <summary>Current Station runtime state.</summary>
    public StationRuntimeState RuntimeState { get; set; } = StationRuntimeState.Unknown;

    /// <summary>Currently loaded runtime package identifier.</summary>
    public string? CurrentPackageId { get; set; }

    /// <summary>Currently loaded runtime package name.</summary>
    public string? CurrentPackageName { get; set; }

    /// <summary>Currently loaded runtime package version.</summary>
    public string? CurrentPackageVersion { get; set; }

    /// <summary>Package-semantic flow hash from the manifest.</summary>
    public string? PackageFlowHash { get; set; }

    /// <summary>Actual execution-definition hash for the current snapshot.</summary>
    public string? ExecutionFlowHash { get; set; }

    /// <summary>Compatibility alias for ExecutionFlowHash.</summary>
    public string? FlowHash { get; set; }

    public Guid? ExecutionSnapshotId { get; set; }

    public long? ProjectRevision { get; set; }

    public string? DecisionConfigurationHash { get; set; }

    public string? ExecutionRunMode { get; set; }

    /// <summary>Current run identifier when the Station is actively processing.</summary>
    public string? CurrentRunId { get; set; }

    /// <summary>Session OK count reported by the runtime host.</summary>
    public int SessionOkCount { get; set; }

    /// <summary>Session NG count reported by the runtime host.</summary>
    public int SessionNgCount { get; set; }

    /// <summary>Session error count reported by the runtime host.</summary>
    public int SessionErrorCount { get; set; }

    /// <summary>
    /// Canonical session outcome statistics. Null is preserved for legacy Station payloads.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InspectionOutcomeStatistics? SessionOutcomeStatistics { get; set; }

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Compatibility timestamp used by early MVP clients.</summary>
    public DateTimeOffset CapturedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    /// <summary>Compatibility runtime state used by early MVP clients.</summary>
    public RuntimeHostState State
    {
        get => StationSyncStateMapper.ToRuntimeHostState(RuntimeState);
        set => RuntimeState = StationSyncStateMapper.ToStationRuntimeState(value);
    }

    /// <summary>Compatibility package identifier used by early MVP clients.</summary>
    public string? PackageId
    {
        get => CurrentPackageId;
        set => CurrentPackageId = value;
    }

    /// <summary>Compatibility package name used by early MVP clients.</summary>
    public string? PackageName
    {
        get => CurrentPackageName;
        set => CurrentPackageName = value;
    }
}

/// <summary>
/// Reports low-frequency Station health metrics.
/// </summary>
public sealed class StationHealthSnapshotDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Monotonic per-station message sequence.</summary>
    public long SequenceId { get; set; }

    /// <summary>Unique message identifier for idempotent diagnostics.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Current Station runtime state.</summary>
    public StationRuntimeState RuntimeState { get; set; } = StationRuntimeState.Unknown;

    /// <summary>Station process uptime in seconds.</summary>
    public long ProcessUptimeSeconds { get; set; }

    /// <summary>Best-effort CPU usage percentage.</summary>
    public double? CpuUsagePercent { get; set; }

    /// <summary>Process working set in megabytes.</summary>
    public long WorkingSetMb { get; set; }

    /// <summary>Process private memory in megabytes.</summary>
    public long PrivateMemoryMb { get; set; }

    /// <summary>Free disk space in megabytes for the Station data drive.</summary>
    public long DiskFreeMb { get; set; }

    /// <summary>Total disk space in megabytes for the Station data drive.</summary>
    public long DiskTotalMb { get; set; }

    /// <summary>Pending spool result count.</summary>
    public int SpoolPendingCount { get; set; }

    /// <summary>Total spool size in bytes.</summary>
    public long SpoolBytes { get; set; }

    /// <summary>Pending command-result report count.</summary>
    public int CommandResultSpoolPendingCount { get; set; }

    /// <summary>Canonical pending command-result payload bytes.</summary>
    public long CommandResultSpoolBytes { get; set; }

    /// <summary>Oldest retained command-result report, when one exists.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CommandResultSpoolOldestAtUtc { get; set; }

    /// <summary>Count of command-result records removed by retention.</summary>
    public long CommandResultSpoolTrimmedCount { get; set; }

    /// <summary>Whether command-result retention created a replay gap.</summary>
    public bool CommandResultSpoolGapDetected { get; set; }

    /// <summary>Whether command-result spool cleanup is currently degraded.</summary>
    public bool CommandResultSpoolDegraded { get; set; }

    /// <summary>Best-effort camera status summary.</summary>
    public string? CameraStatusSummary { get; set; }

    /// <summary>Best-effort PLC status summary.</summary>
    public string? PlcStatusSummary { get; set; }

    /// <summary>Currently loaded package identifier.</summary>
    public string? CurrentPackageId { get; set; }

    /// <summary>Current package health summary.</summary>
    public string? CurrentPackageHealth { get; set; }

    /// <summary>Last known error code.</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>Last known error message.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Stable cross-node summary for a completed Station inspection result.
/// </summary>
public sealed class StationResultSummaryDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Optional production line name.</summary>
    public string? LineName { get; set; }

    /// <summary>Monotonic per-station sequence number used for replay and deduplication.</summary>
    public long SequenceId { get; set; }

    /// <summary>Unique result message identifier.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Runtime run identifier.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Runtime package identifier.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Runtime package display name.</summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Runtime package semantic version when available.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>Package-semantic flow hash from the manifest.</summary>
    public string PackageFlowHash { get; set; } = string.Empty;

    /// <summary>Actual execution-definition hash used by this result.</summary>
    public string ExecutionFlowHash { get; set; } = string.Empty;

    /// <summary>Compatibility alias for ExecutionFlowHash.</summary>
    public string FlowHash { get; set; } = string.Empty;

    /// <summary>Project revision captured in the runtime execution snapshot.</summary>
    public long ProjectRevision { get; set; }

    /// <summary>Hash of the final-decision configuration used by this run.</summary>
    public string? DecisionConfigurationHash { get; set; }

    /// <summary>Immutable run-definition identity created by Station Runtime.</summary>
    public Guid? ExecutionSnapshotId { get; set; }

    /// <summary>Runtime side-effect mode used by the executor.</summary>
    public string? ExecutionRunMode { get; set; }

    /// <summary>Image identifier for the processed frame.</summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>Runtime outcome for the inspection execution.</summary>
    public RuntimeRunOutcome Outcome { get; set; }

    /// <summary>Normalized inspection status when available.</summary>
    public InspectionStatus? InspectionStatus { get; set; }

    /// <summary>
    /// Canonical execution outcome. Null means the sender uses the legacy Outcome projection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecutionOutcome? ExecutionOutcome { get; set; }

    /// <summary>
    /// Canonical decision outcome. Null means the sender uses the legacy Outcome projection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DecisionOutcome? DecisionOutcome { get; set; }

    /// <summary>Whether the runtime observed a usable judgment signal.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasJudgmentSignal { get; set; }

    /// <summary>Canonical final-decision source when available.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DecisionSource { get; set; }

    /// <summary>Stable canonical reason code distinct from transport diagnostics.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; set; }

    /// <summary>End-to-end execution time in milliseconds.</summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>Stable diagnostic code for monitoring and filtering.</summary>
    public string DiagnosticCode { get; set; } = string.Empty;

    /// <summary>Optional diagnostic message for operators and monitoring UI.</summary>
    public string? DiagnosticMessage { get; set; }

    /// <summary>String-only preview of primary scalar outputs. No images or binary data are permitted.</summary>
    public Dictionary<string, string?> PrimaryOutputsPreview { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inspection start time in UTC.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>Inspection completion time in UTC.</summary>
    public DateTimeOffset CompletedAtUtc { get; set; }

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Reports result sequences that are no longer available for replay from the Station spool.
/// </summary>
public sealed class StationResultGapDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>First unavailable result sequence in the reported range.</summary>
    public long DroppedFromSequenceId { get; set; }

    /// <summary>Last unavailable result sequence in the reported range.</summary>
    public long DroppedThroughSequenceId { get; set; }

    /// <summary>Best-effort reason, for example capacity, age, or byte limit.</summary>
    public string Reason { get; set; } = "spool-trim";

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Acknowledges a Station telemetry message after Studio processing.
/// </summary>
public sealed class StationAckDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Accepted sequence for the current message.</summary>
    public long AcceptedSequenceId { get; set; }

    /// <summary>Highest result sequence persisted by Studio.</summary>
    public long LastPersistedSequenceId { get; set; }

    /// <summary>Whether the incoming message was a duplicate.</summary>
    public bool Duplicate { get; set; }

    /// <summary>Optional operator-facing message.</summary>
    public string? Message { get; set; }

    /// <summary>Studio server timestamp.</summary>
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Reports the current replay cursor for a Station.
/// </summary>
public sealed class StationReplayCursorDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Highest accepted result sequence for the Station.</summary>
    public long AckedSequenceId { get; set; }

    /// <summary>Highest persisted result sequence for the Station.</summary>
    public long LastPersistedSequenceId
    {
        get => AckedSequenceId;
        set => AckedSequenceId = value;
    }

    /// <summary>Server acknowledgement timestamp in UTC.</summary>
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Remote command pulled by a Station from Studio.
/// </summary>
public sealed class StationCommandDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Unique command identifier.</summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>Target Station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Remote command type.</summary>
    public StationCommandType CommandType { get; set; }

    /// <summary>JSON payload for command-specific data.</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>When Studio created the command.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the command should expire.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);

    /// <summary>User or system that issued the command.</summary>
    public string IssuedBy { get; set; } = "Studio";

    /// <summary>Correlation identifier for diagnostics.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Current command lifecycle status.</summary>
    public StationCommandStatus Status { get; set; } = StationCommandStatus.Created;

    /// <summary>Latest reported progress percentage.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>When Studio delivered this command to Station.</summary>
    public DateTimeOffset? DeliveredAtUtc { get; set; }

    /// <summary>When Station accepted this command.</summary>
    public DateTimeOffset? AcceptedAtUtc { get; set; }

    /// <summary>When command execution started.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>When command execution completed.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Latest command result message.</summary>
    public string? ResultMessage { get; set; }

    /// <summary>Machine-readable command error code.</summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Station-reported command execution status.
/// </summary>
public sealed class StationCommandResultDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Unique command identifier.</summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>Target Station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Current command status.</summary>
    public StationCommandStatus Status { get; set; }

    /// <summary>Progress percentage when available.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Operator-facing status message.</summary>
    public string? Message { get; set; }

    /// <summary>Machine-readable error code.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Detailed error message.</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>When execution started.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>When execution completed.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>When Station reported this status.</summary>
    public DateTimeOffset ReportedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// WARN/ERROR/FATAL log summary sent from Station to Studio.
/// </summary>
public sealed class StationLogSummaryDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Monotonic per-station message sequence.</summary>
    public long SequenceId { get; set; }

    /// <summary>Unique message identifier for idempotent diagnostics.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Log event timestamp in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Log level, for example WARN, ERROR, or FATAL.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Log source or subsystem name.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Optional event identifier.</summary>
    public string? EventId { get; set; }

    /// <summary>Message template when available.</summary>
    public string? MessageTemplate { get; set; }

    /// <summary>Rendered log message, truncated by Station.</summary>
    public string RenderedMessage { get; set; } = string.Empty;

    /// <summary>Exception type when available.</summary>
    public string? ExceptionType { get; set; }

    /// <summary>Exception message when available.</summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>Correlation identifier.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Runtime run identifier.</summary>
    public string? RunId { get; set; }

    /// <summary>Runtime package identifier.</summary>
    public string? PackageId { get; set; }

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Studio-side Station alarm event.
/// </summary>
public sealed class StationAlarmDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Stable station identifier.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Unique alarm identifier.</summary>
    public string AlarmId { get; set; } = string.Empty;

    /// <summary>Alarm severity state.</summary>
    public StationOnlineState Severity { get; set; } = StationOnlineState.Warning;

    /// <summary>Stable alarm code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Alarm message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Whether the alarm is currently active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Manifest for Studio-stored runtime package artifacts.
/// </summary>
public sealed class StationPackageManifestDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Runtime package identifier.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Runtime package display name.</summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Runtime package version.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>Package purpose.</summary>
    public StationPackageKind PackageKind { get; set; } = StationPackageKind.Production;

    /// <summary>Flow hash for the package.</summary>
    public string FlowHash { get; set; } = string.Empty;

    /// <summary>User or system that created the package.</summary>
    public string CreatedBy { get; set; } = "Studio";

    /// <summary>Minimum Station version required by the package.</summary>
    public string MinStationVersion { get; set; } = "0.1.0";

    /// <summary>Required operator names or versions.</summary>
    public List<string> RequiredOperators { get; set; } = [];

    /// <summary>Package size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 hash formatted as hex or sha256:hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Audit event for high-risk Station operations.
/// </summary>
public sealed class StationAuditDto
{
    /// <summary>Payload schema version.</summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>Unique audit identifier.</summary>
    public string AuditId { get; set; } = string.Empty;

    /// <summary>User identifier.</summary>
    public string? UserId { get; set; }

    /// <summary>User display name.</summary>
    public string? UserName { get; set; }

    /// <summary>Audited action.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Target Station identifier.</summary>
    public string? TargetStationId { get; set; }

    /// <summary>Related command identifier.</summary>
    public string? CommandId { get; set; }

    /// <summary>Redacted payload summary.</summary>
    public string? PayloadSummary { get; set; }

    /// <summary>Operation result summary.</summary>
    public string? Result { get; set; }

    /// <summary>Client IP address.</summary>
    public string? ClientIp { get; set; }

    /// <summary>When this payload was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Runtime/Station state mapping helpers used by compatibility properties.
/// </summary>
public static class StationSyncStateMapper
{
    /// <summary>
    /// Maps runtime host state to Station runtime state.
    /// </summary>
    public static StationRuntimeState ToStationRuntimeState(RuntimeHostState state)
    {
        return state switch
        {
            RuntimeHostState.Idle => StationRuntimeState.Idle,
            RuntimeHostState.Loaded => StationRuntimeState.Idle,
            RuntimeHostState.Running => StationRuntimeState.Running,
            RuntimeHostState.Stopping => StationRuntimeState.Stopping,
            RuntimeHostState.Faulted => StationRuntimeState.Faulted,
            _ => StationRuntimeState.Unknown
        };
    }

    /// <summary>
    /// Maps Station runtime state to runtime host state for MVP compatibility.
    /// </summary>
    public static RuntimeHostState ToRuntimeHostState(StationRuntimeState state)
    {
        return state switch
        {
            StationRuntimeState.Idle => RuntimeHostState.Idle,
            StationRuntimeState.Running => RuntimeHostState.Running,
            StationRuntimeState.Stopping => RuntimeHostState.Stopping,
            StationRuntimeState.Faulted => RuntimeHostState.Faulted,
            StationRuntimeState.LoadingPackage => RuntimeHostState.Loaded,
            _ => RuntimeHostState.Idle
        };
    }
}
