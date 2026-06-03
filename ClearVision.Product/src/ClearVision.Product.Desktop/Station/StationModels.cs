using ClearVision.Product.Core.Enums;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Desktop.Station;

public enum StationIngressListenMode
{
    Loopback = 0,
    Lan = 1
}

public sealed class StationIngressOptions
{
    public const string SectionName = "StationIngress";

    public bool Enabled { get; set; }

    public StationIngressListenMode ListenMode { get; set; } = StationIngressListenMode.Loopback;

    public int Port { get; set; } = 5000;

    public string SharedToken { get; set; } = string.Empty;

    public bool AllowInsecureDevelopment { get; set; }

    public bool AllowMessagePack { get; set; } = true;

    public int OfflineThresholdSeconds { get; set; } = 15;

    public int ResultBufferPerStation { get; set; } = 200;

    public int EventBufferSize { get; set; } = 1000;

    public int HealthBufferPerStation { get; set; } = 100;

    public int LogBufferPerStation { get; set; } = 100;

    public int CommandBufferPerStation { get; set; } = 100;
}

public sealed class StationSummaryViewModel
{
    public int TotalStations { get; set; }

    public int OnlineStations { get; set; }

    public int OfflineStations { get; set; }

    public int RunningStations { get; set; }

    public int FaultedStations { get; set; }

    public int AlertCount { get; set; }

    public int WarningStations { get; set; }

    public int CriticalStations { get; set; }

    public int TotalOkCount { get; set; }

    public int TotalNgCount { get; set; }

    public int TotalErrorCount { get; set; }

    public double AverageExecutionTimeMs { get; set; }

    public int OfflineThresholdSeconds { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class StationStatusViewModel
{
    public string StationId { get; set; } = string.Empty;

    public string? LineName { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public string ClientVersion { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public string? AreaName { get; set; }

    public string? WorkcellName { get; set; }

    public string? InspectionNodeName { get; set; }

    public string? CameraAlias { get; set; }

    public string StationRole { get; set; } = string.Empty;

    public string? Owner { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string? Remark { get; set; }

    public StationOnlineState OnlineState { get; set; }

    public RuntimeHostState State { get; set; }

    public StationRuntimeState RuntimeState { get; set; }

    public bool IsOnline { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public string? PackageId { get; set; }

    public string? PackageName { get; set; }

    public string? FlowHash { get; set; }

    public string? CurrentRunId { get; set; }

    public int SessionOkCount { get; set; }

    public int SessionNgCount { get; set; }

    public int SessionErrorCount { get; set; }

    public RuntimeRunOutcome? LastOutcome { get; set; }

    public InspectionStatus? LastInspectionStatus { get; set; }

    public string? LastDiagnosticCode { get; set; }

    public string? LastDiagnosticMessage { get; set; }

    public DateTimeOffset? LastResultAtUtc { get; set; }

    public long LastSequenceId { get; set; }

    public double AverageExecutionTimeMs { get; set; }

    public int RecentResultCount { get; set; }

    public int SpoolPendingCount { get; set; }

    public long SpoolBytes { get; set; }

    public double? CpuUsagePercent { get; set; }

    public long WorkingSetMb { get; set; }

    public long DiskFreeMb { get; set; }

    public long DiskTotalMb { get; set; }

    public string? CameraStatusSummary { get; set; }

    public string? PlcStatusSummary { get; set; }

    public string? CurrentPackageHealth { get; set; }
}

public sealed class StationDetailViewModel : StationStatusViewModel
{
    public IReadOnlyList<StationResultSummaryDto> RecentResults { get; set; } = Array.Empty<StationResultSummaryDto>();

    public IReadOnlyList<StationHealthSnapshotDto> RecentHealth { get; set; } = Array.Empty<StationHealthSnapshotDto>();

    public IReadOnlyList<StationLogSummaryDto> RecentLogs { get; set; } = Array.Empty<StationLogSummaryDto>();

    public IReadOnlyList<StationCommandDto> RecentCommands { get; set; } = Array.Empty<StationCommandDto>();
}

public sealed class StationResultEventViewModel
{
    public string StationId { get; set; } = string.Empty;

    public StationResultSummaryDto Result { get; set; } = new();

    public StationStatusViewModel Station { get; set; } = new();
}

public sealed class StationSseSnapshotViewModel
{
    public StationSummaryViewModel Summary { get; set; } = new();

    public IReadOnlyList<StationStatusViewModel> Stations { get; set; } = Array.Empty<StationStatusViewModel>();

    public IReadOnlyList<StationResultEventViewModel> RecentResults { get; set; } = Array.Empty<StationResultEventViewModel>();
}

public sealed class StationResultsPageViewModel
{
    public IReadOnlyList<StationResultSummaryDto> Items { get; set; } = Array.Empty<StationResultSummaryDto>();

    public int TotalCount { get; set; }

    public int PageIndex { get; set; }

    public int PageSize { get; set; }
}

public sealed record StoredStationRegistryEvent(
    long SequenceId,
    string EventType,
    object Data,
    DateTimeOffset StoredAtUtc);

public sealed class StationIdentityUpdateRequest
{
    public string? StationName { get; set; }

    public string? LineName { get; set; }

    public string? AreaName { get; set; }

    public string? WorkcellName { get; set; }

    public string? InspectionNodeName { get; set; }

    public string? CameraAlias { get; set; }

    public string? StationRole { get; set; }

    public string? Owner { get; set; }

    public bool? IsEnabled { get; set; }

    public string? Remark { get; set; }

    public string? UpdatedBy { get; set; }
}

public sealed class StationAuditViewModel
{
    public string AuditId { get; set; } = string.Empty;

    public string? UserName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? TargetStationId { get; set; }

    public string? CommandId { get; set; }

    public string? PayloadSummary { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? Result { get; set; }

    public string? ClientIp { get; set; }
}
