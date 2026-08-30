using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Desktop.Station;

/// <summary>
/// Non-sensitive Station status exposed to authenticated monitoring users.
/// This DTO intentionally has no machine, package, flow, spool, hardware, or diagnostic-detail fields.
/// </summary>
public class StationSafeStatusViewModel
{
    public string StationId { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public string? LineName { get; set; }

    public StationOnlineState OnlineState { get; set; }

    public RuntimeHostState State { get; set; }

    public StationRuntimeState RuntimeState { get; set; }

    public bool IsOnline { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public InspectionOutcomeStatistics SessionOutcomeStatistics { get; set; } = new();

    public RuntimeRunOutcome? LastOutcome { get; set; }

    public InspectionStatus? LastInspectionStatus { get; set; }

    public ExecutionOutcome? LastExecutionOutcome { get; set; }

    public DecisionOutcome? LastDecisionOutcome { get; set; }

    public bool? LastHasJudgmentSignal { get; set; }

    public DateTimeOffset? LastResultAtUtc { get; set; }

    public double AverageExecutionTimeMs { get; set; }

    public int RecentResultCount { get; set; }
}

/// <summary>
/// Non-sensitive completed-result projection. Transport, package, flow, image, and output-preview
/// identities are intentionally absent.
/// </summary>
public sealed class StationSafeResultViewModel
{
    public string StationId { get; set; } = string.Empty;

    public string? LineName { get; set; }

    public long SequenceId { get; set; }

    public RuntimeRunOutcome Outcome { get; set; }

    public InspectionStatus? InspectionStatus { get; set; }

    public ExecutionOutcome? ExecutionOutcome { get; set; }

    public DecisionOutcome? DecisionOutcome { get; set; }

    public bool? HasJudgmentSignal { get; set; }

    public string? ReasonCode { get; set; }

    public long ExecutionTimeMs { get; set; }

    public string DiagnosticCode { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Coarse Station health projection. Resource usage, spooling, package, camera/PLC and exception
/// details are intentionally absent.
/// </summary>
public sealed class StationSafeHealthViewModel
{
    public string StationId { get; set; } = string.Empty;

    public long SequenceId { get; set; }

    public StationRuntimeState RuntimeState { get; set; }

    public StationOnlineState HealthState { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class StationSafeDetailViewModel : StationSafeStatusViewModel
{
    public IReadOnlyList<StationSafeResultViewModel> RecentResults { get; set; } = Array.Empty<StationSafeResultViewModel>();

    public IReadOnlyList<StationSafeHealthViewModel> RecentHealth { get; set; } = Array.Empty<StationSafeHealthViewModel>();
}

public sealed class StationSafeResultsPageViewModel
{
    public IReadOnlyList<StationSafeResultViewModel> Items { get; set; } = Array.Empty<StationSafeResultViewModel>();

    public int TotalCount { get; set; }

    public int PageIndex { get; set; }

    public int PageSize { get; set; }
}

public sealed class StationSafeResultEventViewModel
{
    public string StationId { get; set; } = string.Empty;

    public StationSafeResultViewModel Result { get; set; } = new();

    public StationSafeStatusViewModel Station { get; set; } = new();
}

public sealed class StationHealthEventViewModel
{
    public string StationId { get; set; } = string.Empty;

    public StationHealthSnapshotDto Health { get; set; } = new();

    public StationStatusViewModel Station { get; set; } = new();
}

public sealed class StationSafeHealthEventViewModel
{
    public string StationId { get; set; } = string.Empty;

    public StationSafeHealthViewModel Health { get; set; } = new();

    public StationSafeStatusViewModel Station { get; set; } = new();
}

public sealed class StationSafeSseSnapshotViewModel
{
    public StationSummaryViewModel Summary { get; set; } = new();

    public IReadOnlyList<StationSafeStatusViewModel> Stations { get; set; } = Array.Empty<StationSafeStatusViewModel>();

    public IReadOnlyList<StationSafeResultEventViewModel> RecentResults { get; set; } = Array.Empty<StationSafeResultEventViewModel>();
}

/// <summary>
/// The single safe-monitoring projection used by REST initial reads, SSE initial state, replay,
/// and live events. Non-Admin events fail closed unless explicitly listed here.
/// </summary>
public static class StationMonitoringProjection
{
    public static StationSafeStatusViewModel ToSafeStatus(StationStatusViewModel station)
    {
        ArgumentNullException.ThrowIfNull(station);

        return new StationSafeStatusViewModel
        {
            StationId = station.StationId,
            StationName = station.StationName,
            LineName = station.LineName,
            OnlineState = station.OnlineState,
            State = station.State,
            RuntimeState = station.RuntimeState,
            IsOnline = station.IsOnline,
            LastSeenAtUtc = station.LastSeenAtUtc,
            SessionOutcomeStatistics = station.SessionOutcomeStatistics,
            LastOutcome = station.LastOutcome,
            LastInspectionStatus = station.LastInspectionStatus,
            LastExecutionOutcome = station.LastExecutionOutcome,
            LastDecisionOutcome = station.LastDecisionOutcome,
            LastHasJudgmentSignal = station.LastHasJudgmentSignal,
            LastResultAtUtc = station.LastResultAtUtc,
            AverageExecutionTimeMs = station.AverageExecutionTimeMs,
            RecentResultCount = station.RecentResultCount
        };
    }

    public static StationSafeResultViewModel ToSafeResult(StationResultSummaryDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StationSafeResultViewModel
        {
            StationId = result.StationId,
            LineName = result.LineName,
            SequenceId = result.SequenceId,
            Outcome = result.Outcome,
            InspectionStatus = result.InspectionStatus,
            ExecutionOutcome = result.ExecutionOutcome,
            DecisionOutcome = result.DecisionOutcome,
            HasJudgmentSignal = result.HasJudgmentSignal,
            ReasonCode = result.ReasonCode,
            ExecutionTimeMs = result.ExecutionTimeMs,
            DiagnosticCode = result.DiagnosticCode,
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc,
            CreatedAtUtc = result.CreatedAtUtc
        };
    }

    public static StationSafeHealthViewModel ToSafeHealth(
        StationHealthSnapshotDto health,
        StationOnlineState? currentHealthState = null)
    {
        ArgumentNullException.ThrowIfNull(health);

        return new StationSafeHealthViewModel
        {
            StationId = health.StationId,
            SequenceId = health.SequenceId,
            RuntimeState = health.RuntimeState,
            HealthState = currentHealthState ?? ResolveHealthState(health),
            CreatedAtUtc = health.CreatedAtUtc
        };
    }

    public static StationSafeDetailViewModel ToSafeDetail(StationDetailViewModel detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var status = ToSafeStatus(detail);
        return new StationSafeDetailViewModel
        {
            StationId = status.StationId,
            StationName = status.StationName,
            LineName = status.LineName,
            OnlineState = status.OnlineState,
            State = status.State,
            RuntimeState = status.RuntimeState,
            IsOnline = status.IsOnline,
            LastSeenAtUtc = status.LastSeenAtUtc,
            SessionOutcomeStatistics = status.SessionOutcomeStatistics,
            LastOutcome = status.LastOutcome,
            LastInspectionStatus = status.LastInspectionStatus,
            LastExecutionOutcome = status.LastExecutionOutcome,
            LastDecisionOutcome = status.LastDecisionOutcome,
            LastHasJudgmentSignal = status.LastHasJudgmentSignal,
            LastResultAtUtc = status.LastResultAtUtc,
            AverageExecutionTimeMs = status.AverageExecutionTimeMs,
            RecentResultCount = status.RecentResultCount,
            RecentResults = detail.RecentResults.Select(ToSafeResult).ToList(),
            RecentHealth = detail.RecentHealth
                .Select(health => ToSafeHealth(health, status.OnlineState))
                .ToList()
        };
    }

    public static StationSafeResultsPageViewModel ToSafeResultsPage(StationResultsPageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new StationSafeResultsPageViewModel
        {
            Items = page.Items.Select(ToSafeResult).ToList(),
            TotalCount = page.TotalCount,
            PageIndex = page.PageIndex,
            PageSize = page.PageSize
        };
    }

    public static StationSafeSseSnapshotViewModel ToSafeSnapshot(StationSseSnapshotViewModel snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new StationSafeSseSnapshotViewModel
        {
            Summary = snapshot.Summary,
            Stations = snapshot.Stations.Select(ToSafeStatus).ToList(),
            RecentResults = snapshot.RecentResults.Select(ToSafeResultEvent).ToList()
        };
    }

    public static bool TryProjectEvent(
        StoredStationRegistryEvent storedEvent,
        bool includeSensitive,
        out StoredStationRegistryEvent projectedEvent)
    {
        ArgumentNullException.ThrowIfNull(storedEvent);

        if (includeSensitive)
        {
            projectedEvent = storedEvent;
            return true;
        }

        object? data = storedEvent.EventType switch
        {
            "stationUpserted" when storedEvent.Data is StationStatusViewModel station => ToSafeStatus(station),
            "summaryUpdated" when storedEvent.Data is StationSummaryViewModel summary => summary,
            "stationResultAdded" when storedEvent.Data is StationResultEventViewModel result => ToSafeResultEvent(result),
            "stationHealthUpdated" when storedEvent.Data is StationHealthEventViewModel health => ToSafeHealthEvent(health),
            "heartbeat" => new { timestamp = storedEvent.StoredAtUtc },
            _ => null
        };

        if (data is null)
        {
            projectedEvent = storedEvent;
            return false;
        }

        projectedEvent = storedEvent with { Data = data };
        return true;
    }

    private static StationSafeResultEventViewModel ToSafeResultEvent(StationResultEventViewModel evt) =>
        new()
        {
            StationId = evt.StationId,
            Result = ToSafeResult(evt.Result),
            Station = ToSafeStatus(evt.Station)
        };

    private static StationSafeHealthEventViewModel ToSafeHealthEvent(StationHealthEventViewModel evt) =>
        new()
        {
            StationId = evt.StationId,
            Health = ToSafeHealth(evt.Health, evt.Station.OnlineState),
            Station = ToSafeStatus(evt.Station)
        };

    private static StationOnlineState ResolveHealthState(StationHealthSnapshotDto health)
    {
        if (health.RuntimeState == StationRuntimeState.Faulted ||
            !string.IsNullOrWhiteSpace(health.LastErrorCode))
        {
            return StationOnlineState.Critical;
        }

        return health.RuntimeState == StationRuntimeState.Unknown
            ? StationOnlineState.Unknown
            : StationOnlineState.Online;
    }
}
