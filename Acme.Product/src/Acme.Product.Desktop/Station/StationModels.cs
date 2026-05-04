using Acme.Product.Core.Enums;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Desktop.Station;

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

    public int OfflineThresholdSeconds { get; set; } = 15;

    public int ResultBufferPerStation { get; set; } = 200;

    public int EventBufferSize { get; set; } = 1000;
}

public sealed class StationSummaryViewModel
{
    public int TotalStations { get; set; }

    public int OnlineStations { get; set; }

    public int OfflineStations { get; set; }

    public int RunningStations { get; set; }

    public int FaultedStations { get; set; }

    public int AlertCount { get; set; }

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

    public RuntimeHostState State { get; set; }

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
}

public sealed class StationDetailViewModel : StationStatusViewModel
{
    public IReadOnlyList<StationResultSummaryDto> RecentResults { get; set; } = Array.Empty<StationResultSummaryDto>();
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

public sealed record StoredStationRegistryEvent(
    long SequenceId,
    string EventType,
    object Data,
    DateTimeOffset StoredAtUtc);
