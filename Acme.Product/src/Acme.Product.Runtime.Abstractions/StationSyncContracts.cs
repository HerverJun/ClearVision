using Acme.Product.Core.Enums;

namespace Acme.Product.Runtime.Abstractions;

/// <summary>
/// Shared defaults for Station-to-Studio sync contracts.
/// </summary>
public static class StationSyncContractDefaults
{
    /// <summary>
    /// Current schema version for Station sync payloads.
    /// </summary>
    public const int SchemaVersion = 1;
}

/// <summary>
/// Registers a Station with the central Studio ingress hub.
/// </summary>
public sealed class StationRegistrationDto
{
    /// <summary>
    /// Payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>
    /// Stable station identifier.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Optional production line name.
    /// </summary>
    public string? LineName { get; set; }

    /// <summary>
    /// Local machine name reported by the Station host.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Station client version string.
    /// </summary>
    public string ClientVersion { get; set; } = string.Empty;

    /// <summary>
    /// Station process start time in UTC.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Reports the latest runtime snapshot as a best-effort heartbeat payload.
/// </summary>
public sealed class StationHeartbeatDto
{
    /// <summary>
    /// Payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>
    /// Stable station identifier.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Optional production line name.
    /// </summary>
    public string? LineName { get; set; }

    /// <summary>
    /// Timestamp when the heartbeat was emitted in UTC.
    /// </summary>
    public DateTimeOffset SentAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current runtime state.
    /// </summary>
    public RuntimeHostState State { get; set; }

    /// <summary>
    /// Currently loaded runtime package identifier.
    /// </summary>
    public string? PackageId { get; set; }

    /// <summary>
    /// Currently loaded runtime package name.
    /// </summary>
    public string? PackageName { get; set; }

    /// <summary>
    /// Flow hash for the loaded runtime package.
    /// </summary>
    public string? FlowHash { get; set; }

    /// <summary>
    /// Current run identifier when the Station is actively processing.
    /// </summary>
    public string? CurrentRunId { get; set; }

    /// <summary>
    /// Session OK count reported by the runtime host.
    /// </summary>
    public int SessionOkCount { get; set; }

    /// <summary>
    /// Session NG count reported by the runtime host.
    /// </summary>
    public int SessionNgCount { get; set; }

    /// <summary>
    /// Session error count reported by the runtime host.
    /// </summary>
    public int SessionErrorCount { get; set; }
}

/// <summary>
/// Reports a debounced runtime snapshot change.
/// </summary>
public sealed class StationSnapshotDto
{
    /// <summary>
    /// Payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>
    /// Stable station identifier.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Optional production line name.
    /// </summary>
    public string? LineName { get; set; }

    /// <summary>
    /// Timestamp when the snapshot was emitted in UTC.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current runtime state.
    /// </summary>
    public RuntimeHostState State { get; set; }

    /// <summary>
    /// Currently loaded runtime package identifier.
    /// </summary>
    public string? PackageId { get; set; }

    /// <summary>
    /// Currently loaded runtime package name.
    /// </summary>
    public string? PackageName { get; set; }

    /// <summary>
    /// Flow hash for the loaded runtime package.
    /// </summary>
    public string? FlowHash { get; set; }

    /// <summary>
    /// Current run identifier when the Station is actively processing.
    /// </summary>
    public string? CurrentRunId { get; set; }

    /// <summary>
    /// Session OK count reported by the runtime host.
    /// </summary>
    public int SessionOkCount { get; set; }

    /// <summary>
    /// Session NG count reported by the runtime host.
    /// </summary>
    public int SessionNgCount { get; set; }

    /// <summary>
    /// Session error count reported by the runtime host.
    /// </summary>
    public int SessionErrorCount { get; set; }
}

/// <summary>
/// Stable cross-node summary for a completed Station inspection result.
/// </summary>
public sealed class StationResultSummaryDto
{
    /// <summary>
    /// Payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>
    /// Stable station identifier.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Optional production line name.
    /// </summary>
    public string? LineName { get; set; }

    /// <summary>
    /// Monotonic per-station sequence number used for replay and deduplication.
    /// </summary>
    public long SequenceId { get; set; }

    /// <summary>
    /// Runtime run identifier.
    /// </summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>
    /// Runtime package identifier.
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Runtime package display name.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Exported flow hash for the runtime package.
    /// </summary>
    public string FlowHash { get; set; } = string.Empty;

    /// <summary>
    /// Image identifier for the processed frame.
    /// </summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>
    /// Runtime outcome for the inspection execution.
    /// </summary>
    public RuntimeRunOutcome Outcome { get; set; }

    /// <summary>
    /// Normalized inspection status when available.
    /// </summary>
    public InspectionStatus? InspectionStatus { get; set; }

    /// <summary>
    /// End-to-end execution time in milliseconds.
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Stable diagnostic code for monitoring and filtering.
    /// </summary>
    public string DiagnosticCode { get; set; } = string.Empty;

    /// <summary>
    /// Optional diagnostic message for operators and monitoring UI.
    /// </summary>
    public string? DiagnosticMessage { get; set; }

    /// <summary>
    /// Inspection start time in UTC.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Inspection completion time in UTC.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; set; }
}

/// <summary>
/// Acknowledges the highest accepted Station result sequence.
/// </summary>
public sealed class StationReplayCursorDto
{
    /// <summary>
    /// Payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = StationSyncContractDefaults.SchemaVersion;

    /// <summary>
    /// Stable station identifier.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Highest accepted result sequence for the Station.
    /// </summary>
    public long AckedSequenceId { get; set; }

    /// <summary>
    /// Server acknowledgement timestamp in UTC.
    /// </summary>
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;
}
