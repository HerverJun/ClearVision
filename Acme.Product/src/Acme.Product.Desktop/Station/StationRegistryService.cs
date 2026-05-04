using Acme.Product.Runtime.Abstractions;
using Acme.Product.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Desktop.Station;

public sealed class StationRegistryService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, StationRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StoredStationRegistryEvent> _eventBuffer = [];
    private readonly List<Action<StoredStationRegistryEvent>> _subscribers = [];
    private readonly ILogger<StationRegistryService> _logger;
    private readonly StationIngressOptions _options;
    private long _nextEventSequenceId;

    public StationRegistryService(
        IOptions<StationIngressOptions> options,
        ILogger<StationRegistryService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public StationReplayCursorDto UpsertRegistration(
        string connectionId,
        StationRegistrationDto registration)
    {
        List<StoredStationRegistryEvent> events;
        StationReplayCursorDto cursor;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(registration.StationId);
            entry.StationId = registration.StationId;
            entry.LineName = registration.LineName;
            entry.MachineName = registration.MachineName;
            entry.ClientVersion = registration.ClientVersion;
            entry.StartedAtUtc = registration.StartedAtUtc == default ? now : registration.StartedAtUtc;
            TouchConnectionLocked(entry, connectionId, now);

            cursor = BuildCursor(entry);
            events =
            [
                CreateEventLocked("stationUpserted", ToStatusViewModelLocked(entry, now)),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return cursor;
    }

    public StationReplayCursorDto UpsertHeartbeat(
        string connectionId,
        StationHeartbeatDto heartbeat)
    {
        List<StoredStationRegistryEvent> events;
        StationReplayCursorDto cursor;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(heartbeat.StationId);
            ApplySnapshotLocked(
                entry,
                heartbeat.LineName,
                heartbeat.State,
                heartbeat.PackageId,
                heartbeat.PackageName,
                heartbeat.FlowHash,
                heartbeat.CurrentRunId,
                heartbeat.SessionOkCount,
                heartbeat.SessionNgCount,
                heartbeat.SessionErrorCount,
                connectionId,
                now);

            cursor = BuildCursor(entry);
            events =
            [
                CreateEventLocked("stationUpserted", ToStatusViewModelLocked(entry, now)),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return cursor;
    }

    public StationReplayCursorDto UpsertSnapshot(
        string connectionId,
        StationSnapshotDto snapshot)
    {
        List<StoredStationRegistryEvent> events;
        StationReplayCursorDto cursor;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(snapshot.StationId);
            ApplySnapshotLocked(
                entry,
                snapshot.LineName,
                snapshot.State,
                snapshot.PackageId,
                snapshot.PackageName,
                snapshot.FlowHash,
                snapshot.CurrentRunId,
                snapshot.SessionOkCount,
                snapshot.SessionNgCount,
                snapshot.SessionErrorCount,
                connectionId,
                now);

            cursor = BuildCursor(entry);
            events =
            [
                CreateEventLocked("stationUpserted", ToStatusViewModelLocked(entry, now)),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return cursor;
    }

    public StationReplayCursorDto UpsertResultSummary(
        string connectionId,
        StationResultSummaryDto result)
    {
        List<StoredStationRegistryEvent> events;
        StationReplayCursorDto cursor;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(result.StationId);
            TouchConnectionLocked(entry, connectionId, now);
            if (!string.IsNullOrWhiteSpace(result.LineName))
            {
                entry.LineName = result.LineName;
            }

            if (result.SequenceId <= entry.LastAcceptedSequenceId)
            {
                return BuildCursor(entry);
            }

            entry.LastAcceptedSequenceId = result.SequenceId;
            entry.LastOutcome = result.Outcome;
            entry.LastInspectionStatus = result.InspectionStatus;
            entry.LastDiagnosticCode = result.DiagnosticCode;
            entry.LastDiagnosticMessage = result.DiagnosticMessage;
            entry.LastResultAtUtc = result.CompletedAtUtc;

            entry.RecentResults.Insert(0, CloneResult(result));
            while (entry.RecentResults.Count > Math.Max(10, _options.ResultBufferPerStation))
            {
                entry.RecentResults.RemoveAt(entry.RecentResults.Count - 1);
            }

            cursor = BuildCursor(entry);
            var stationViewModel = ToStatusViewModelLocked(entry, now);
            events =
            [
                CreateEventLocked(
                    "stationResultAdded",
                    new StationResultEventViewModel
                    {
                        StationId = entry.StationId,
                        Result = CloneResult(result),
                        Station = stationViewModel
                    }),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return cursor;
    }

    public void MarkDisconnected(string connectionId)
    {
        List<StoredStationRegistryEvent> events = [];
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            foreach (var entry in _entries.Values.Where(entry => string.Equals(entry.ConnectionId, connectionId, StringComparison.Ordinal)).ToList())
            {
                entry.ConnectionId = null;
                events.Add(CreateEventLocked("stationUpserted", ToStatusViewModelLocked(entry, now)));
            }

            if (events.Count > 0)
            {
                events.Add(CreateEventLocked("summaryUpdated", BuildSummaryLocked(now)));
            }
        }

        PublishEvents(events);
    }

    public IReadOnlyList<StationStatusViewModel> GetStations()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            return _entries.Values
                .OrderBy(entry => entry.StationId, StringComparer.OrdinalIgnoreCase)
                .Select(entry => ToStatusViewModelLocked(entry, now))
                .ToList();
        }
    }

    public StationSummaryViewModel GetSummary()
    {
        lock (_syncRoot)
        {
            return BuildSummaryLocked(DateTimeOffset.UtcNow);
        }
    }

    public StationDetailViewModel? GetStation(string stationId)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var status = ToStatusViewModelLocked(entry, now);
            return new StationDetailViewModel
            {
                StationId = status.StationId,
                LineName = status.LineName,
                MachineName = status.MachineName,
                ClientVersion = status.ClientVersion,
                State = status.State,
                IsOnline = status.IsOnline,
                StartedAtUtc = status.StartedAtUtc,
                LastSeenAtUtc = status.LastSeenAtUtc,
                PackageId = status.PackageId,
                PackageName = status.PackageName,
                FlowHash = status.FlowHash,
                CurrentRunId = status.CurrentRunId,
                SessionOkCount = status.SessionOkCount,
                SessionNgCount = status.SessionNgCount,
                SessionErrorCount = status.SessionErrorCount,
                LastOutcome = status.LastOutcome,
                LastInspectionStatus = status.LastInspectionStatus,
                LastDiagnosticCode = status.LastDiagnosticCode,
                LastDiagnosticMessage = status.LastDiagnosticMessage,
                LastResultAtUtc = status.LastResultAtUtc,
                LastSequenceId = status.LastSequenceId,
                AverageExecutionTimeMs = status.AverageExecutionTimeMs,
                RecentResultCount = status.RecentResultCount,
                RecentResults = entry.RecentResults.Select(CloneResult).ToList()
            };
        }
    }

    public IReadOnlyList<StationResultSummaryDto> GetRecentResults(string stationId, int take)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                return Array.Empty<StationResultSummaryDto>();
            }

            return entry.RecentResults
                .Take(Math.Max(1, take))
                .Select(CloneResult)
                .ToList();
        }
    }

    public StationSseSnapshotViewModel GetSseSnapshot()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var stationViewModels = _entries.Values
                .OrderBy(entry => entry.StationId, StringComparer.OrdinalIgnoreCase)
                .Select(entry => ToStatusViewModelLocked(entry, now))
                .ToList();
            var stationLookup = stationViewModels.ToDictionary(viewModel => viewModel.StationId, StringComparer.OrdinalIgnoreCase);
            var recentResults = _entries.Values
                .SelectMany(entry => entry.RecentResults.Select(result => new { entry.StationId, Result = result }))
                .OrderByDescending(item => item.Result.CompletedAtUtc)
                .Take(20)
                .Select(item => new StationResultEventViewModel
                {
                    StationId = item.StationId,
                    Result = CloneResult(item.Result),
                    Station = stationLookup.TryGetValue(item.StationId, out var station)
                        ? station
                        : new StationStatusViewModel { StationId = item.StationId }
                })
                .ToList();

            return new StationSseSnapshotViewModel
            {
                Summary = BuildSummaryLocked(now),
                Stations = stationViewModels,
                RecentResults = recentResults
            };
        }
    }

    public IReadOnlyList<StoredStationRegistryEvent> GetEventsAfter(long sequenceId)
    {
        lock (_syncRoot)
        {
            return _eventBuffer
                .Where(evt => evt.SequenceId > sequenceId)
                .OrderBy(evt => evt.SequenceId)
                .ToList();
        }
    }

    public IDisposable Subscribe(Action<StoredStationRegistryEvent> handler)
    {
        lock (_syncRoot)
        {
            _subscribers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_syncRoot)
            {
                _subscribers.Remove(handler);
            }
        });
    }

    private void ApplySnapshotLocked(
        StationRegistryEntry entry,
        string? lineName,
        RuntimeHostState state,
        string? packageId,
        string? packageName,
        string? flowHash,
        string? currentRunId,
        int sessionOkCount,
        int sessionNgCount,
        int sessionErrorCount,
        string connectionId,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(lineName))
        {
            entry.LineName = lineName;
        }

        entry.State = state;
        entry.PackageId = packageId;
        entry.PackageName = packageName;
        entry.FlowHash = flowHash;
        entry.CurrentRunId = currentRunId;
        entry.SessionOkCount = sessionOkCount;
        entry.SessionNgCount = sessionNgCount;
        entry.SessionErrorCount = sessionErrorCount;
        TouchConnectionLocked(entry, connectionId, now);
    }

    private void TouchConnectionLocked(StationRegistryEntry entry, string connectionId, DateTimeOffset now)
    {
        entry.ConnectionId = connectionId;
        entry.LastSeenAtUtc = now;
    }

    private StationStatusViewModel ToStatusViewModelLocked(StationRegistryEntry entry, DateTimeOffset now)
    {
        return new StationStatusViewModel
        {
            StationId = entry.StationId,
            LineName = entry.LineName,
            MachineName = entry.MachineName,
            ClientVersion = entry.ClientVersion,
            State = entry.State,
            IsOnline = IsOnlineLocked(entry, now),
            StartedAtUtc = entry.StartedAtUtc,
            LastSeenAtUtc = entry.LastSeenAtUtc,
            PackageId = entry.PackageId,
            PackageName = entry.PackageName,
            FlowHash = entry.FlowHash,
            CurrentRunId = entry.CurrentRunId,
            SessionOkCount = entry.SessionOkCount,
            SessionNgCount = entry.SessionNgCount,
            SessionErrorCount = entry.SessionErrorCount,
            LastOutcome = entry.LastOutcome,
            LastInspectionStatus = entry.LastInspectionStatus,
            LastDiagnosticCode = entry.LastDiagnosticCode,
            LastDiagnosticMessage = entry.LastDiagnosticMessage,
            LastResultAtUtc = entry.LastResultAtUtc,
            LastSequenceId = entry.LastAcceptedSequenceId,
            AverageExecutionTimeMs = entry.RecentResults.Count == 0
                ? 0
                : entry.RecentResults.Average(result => result.ExecutionTimeMs),
            RecentResultCount = entry.RecentResults.Count
        };
    }

    private StationSummaryViewModel BuildSummaryLocked(DateTimeOffset now)
    {
        var stations = _entries.Values.ToList();
        var onlineStations = stations.Count(entry => IsOnlineLocked(entry, now));
        var faultedStations = stations.Count(entry => entry.State == RuntimeHostState.Faulted);
        var runningStations = stations.Count(entry => entry.State == RuntimeHostState.Running && IsOnlineLocked(entry, now));
        var recentResults = stations.SelectMany(entry => entry.RecentResults).ToList();

        return new StationSummaryViewModel
        {
            TotalStations = stations.Count,
            OnlineStations = onlineStations,
            OfflineStations = stations.Count - onlineStations,
            RunningStations = runningStations,
            FaultedStations = faultedStations,
            AlertCount = stations.Count(entry => !IsOnlineLocked(entry, now) || entry.State == RuntimeHostState.Faulted),
            TotalOkCount = stations.Sum(entry => entry.SessionOkCount),
            TotalNgCount = stations.Sum(entry => entry.SessionNgCount),
            TotalErrorCount = stations.Sum(entry => entry.SessionErrorCount),
            AverageExecutionTimeMs = recentResults.Count == 0 ? 0 : recentResults.Average(result => result.ExecutionTimeMs),
            OfflineThresholdSeconds = Math.Max(1, _options.OfflineThresholdSeconds),
            UpdatedAtUtc = now
        };
    }

    private bool IsOnlineLocked(StationRegistryEntry entry, DateTimeOffset now)
    {
        return !string.IsNullOrWhiteSpace(entry.ConnectionId) &&
               now - entry.LastSeenAtUtc <= TimeSpan.FromSeconds(Math.Max(1, _options.OfflineThresholdSeconds));
    }

    private StationRegistryEntry GetOrCreateEntryLocked(string stationId)
    {
        if (_entries.TryGetValue(stationId, out var entry))
        {
            return entry;
        }

        entry = new StationRegistryEntry
        {
            StationId = stationId
        };
        _entries[stationId] = entry;
        return entry;
    }

    private StationReplayCursorDto BuildCursor(StationRegistryEntry entry)
    {
        return new StationReplayCursorDto
        {
            SchemaVersion = StationSyncContractDefaults.SchemaVersion,
            StationId = entry.StationId,
            AckedSequenceId = entry.LastAcceptedSequenceId,
            ServerTimeUtc = DateTimeOffset.UtcNow
        };
    }

    private StoredStationRegistryEvent CreateEventLocked(string eventType, object data)
    {
        var stored = new StoredStationRegistryEvent(
            Interlocked.Increment(ref _nextEventSequenceId),
            eventType,
            data,
            DateTimeOffset.UtcNow);
        _eventBuffer.Add(stored);

        while (_eventBuffer.Count > Math.Max(100, _options.EventBufferSize))
        {
            _eventBuffer.RemoveAt(0);
        }

        return stored;
    }

    private void PublishEvents(IReadOnlyList<StoredStationRegistryEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        Action<StoredStationRegistryEvent>[] subscribers;
        lock (_syncRoot)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var evt in events)
        {
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber(evt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Station registry subscriber failed for {EventType}", evt.EventType);
                }
            }
        }
    }

    private static StationResultSummaryDto CloneResult(StationResultSummaryDto result)
    {
        return new StationResultSummaryDto
        {
            SchemaVersion = result.SchemaVersion,
            StationId = result.StationId,
            LineName = result.LineName,
            SequenceId = result.SequenceId,
            RunId = result.RunId,
            PackageId = result.PackageId,
            PackageName = result.PackageName,
            FlowHash = result.FlowHash,
            ImageId = result.ImageId,
            Outcome = result.Outcome,
            InspectionStatus = result.InspectionStatus,
            ExecutionTimeMs = result.ExecutionTimeMs,
            DiagnosticCode = result.DiagnosticCode,
            DiagnosticMessage = result.DiagnosticMessage,
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc
        };
    }

    private sealed class StationRegistryEntry
    {
        public string StationId { get; set; } = string.Empty;

        public string? LineName { get; set; }

        public string MachineName { get; set; } = string.Empty;

        public string ClientVersion { get; set; } = string.Empty;

        public string? ConnectionId { get; set; }

        public RuntimeHostState State { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

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

        public long LastAcceptedSequenceId { get; set; }

        public List<StationResultSummaryDto> RecentResults { get; } = [];
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private int _disposed;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _unsubscribe();
            }
        }
    }
}
