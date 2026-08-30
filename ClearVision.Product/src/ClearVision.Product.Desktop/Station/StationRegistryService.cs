using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Station;

public sealed class StationRegistryService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, StationRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StoredStationRegistryEvent> _eventBuffer = [];
    private readonly List<Action<StoredStationRegistryEvent>> _subscribers = [];
    private readonly ILogger<StationRegistryService> _logger;
    private readonly StationIngressOptions _options;
    private readonly StationCentralStore? _centralStore;
    private long _nextEventSequenceId;

    public StationRegistryService(
        IOptions<StationIngressOptions> options,
        ILogger<StationRegistryService> logger,
        StationCentralStore? centralStore = null)
    {
        _options = options.Value;
        _logger = logger;
        _centralStore = centralStore;
        RestorePersistedStations();
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
            entry.StationName = registration.StationName;
            entry.AreaName = registration.AreaName;
            entry.WorkcellName = registration.WorkcellName;
            entry.InspectionNodeName = registration.InspectionNodeName;
            entry.CameraAlias = registration.CameraAlias;
            entry.StationRole = registration.StationRole;
            entry.Owner = registration.Owner;
            entry.MachineName = registration.MachineName;
            entry.ClientVersion = registration.ClientVersion;
            entry.StartedAtUtc = registration.StartedAtUtc == default ? now : registration.StartedAtUtc;
            entry.PackageId = registration.CurrentPackageId;
            entry.PackageName = registration.CurrentPackageName;
            entry.PackageVersion = registration.CurrentPackageVersion;
            entry.LastAcceptedSequenceId = Math.Max(entry.LastAcceptedSequenceId, _centralStore?.UpsertRegistration(registration) ?? 0);
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
            entry.LastAcceptedSequenceId = Math.Max(entry.LastAcceptedSequenceId, _centralStore?.UpsertHeartbeat(heartbeat) ?? 0);
            ApplySnapshotLocked(
                entry,
                heartbeat.LineName,
                heartbeat.State,
                heartbeat.PackageId,
                heartbeat.PackageName,
                heartbeat.CurrentPackageVersion,
                heartbeat.PackageFlowHash,
                heartbeat.ExecutionFlowHash,
                heartbeat.FlowHash,
                heartbeat.ExecutionSnapshotId,
                heartbeat.ProjectRevision,
                heartbeat.DecisionConfigurationHash,
                heartbeat.ExecutionRunMode,
                heartbeat.CurrentRunId,
                heartbeat.SessionOkCount,
                heartbeat.SessionNgCount,
                heartbeat.SessionErrorCount,
                heartbeat.SessionOutcomeStatistics,
                heartbeat.SpoolPendingCount,
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
            entry.LastAcceptedSequenceId = Math.Max(entry.LastAcceptedSequenceId, _centralStore?.UpsertSnapshot(snapshot) ?? 0);
            ApplySnapshotLocked(
                entry,
                snapshot.LineName,
                snapshot.State,
                snapshot.PackageId,
                snapshot.PackageName,
                snapshot.CurrentPackageVersion,
                snapshot.PackageFlowHash,
                snapshot.ExecutionFlowHash,
                snapshot.FlowHash,
                snapshot.ExecutionSnapshotId,
                snapshot.ProjectRevision,
                snapshot.DecisionConfigurationHash,
                snapshot.ExecutionRunMode,
                snapshot.CurrentRunId,
                snapshot.SessionOkCount,
                snapshot.SessionNgCount,
                snapshot.SessionErrorCount,
                snapshot.SessionOutcomeStatistics,
                entry.SpoolPendingCount,
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

            var ack = _centralStore?.UpsertResultSummary(result);
            var duplicate = ack?.Duplicate == true ||
                (_centralStore == null &&
                 (result.SequenceId <= entry.LastAcceptedSequenceId || !entry.AcceptedResultSequences.Add(result.SequenceId)));
            if (_centralStore == null && !duplicate)
            {
                AdvanceMemoryCursor(entry, result.SequenceId);
            }
            else if (_centralStore != null)
            {
                entry.LastAcceptedSequenceId = Math.Max(entry.LastAcceptedSequenceId, ack?.LastPersistedSequenceId ?? 0);
            }

            if (duplicate)
            {
                return BuildCursor(entry);
            }

            var canonical = StationCanonicalOutcomeProjection.Resolve(result);
            entry.LastOutcome = StationCanonicalOutcomeProjection.ProjectRuntimeOutcome(canonical);
            entry.LastInspectionStatus = LegacyInspectionStatusProjection.Project(canonical);
            entry.LastExecutionOutcome = canonical.Execution;
            entry.LastDecisionOutcome = canonical.Decision;
            entry.LastHasJudgmentSignal = canonical.HasJudgmentSignal;
            entry.LastDecisionSource = canonical.DecisionSource;
            entry.LastReasonCode = canonical.ReasonCode;
            entry.LastDiagnosticCode = result.DiagnosticCode;
            entry.LastDiagnosticMessage = result.DiagnosticMessage;
            entry.LastResultAtUtc = result.CompletedAtUtc;
            entry.PackageId = result.PackageId;
            entry.PackageName = result.PackageName;
            entry.PackageVersion = result.PackageVersion;
            entry.PackageFlowHash = NullIfWhiteSpace(result.PackageFlowHash);
            entry.ExecutionFlowHash = NullIfWhiteSpace(result.ExecutionFlowHash) ?? NullIfWhiteSpace(result.FlowHash);
            entry.FlowHash = entry.ExecutionFlowHash;
            entry.ExecutionSnapshotId = result.ExecutionSnapshotId;
            entry.ProjectRevision = result.ProjectRevision == 0 ? null : result.ProjectRevision;
            entry.DecisionConfigurationHash = NullIfWhiteSpace(result.DecisionConfigurationHash);
            entry.ExecutionRunMode = NullIfWhiteSpace(result.ExecutionRunMode);
            entry.CurrentRunId = NullIfWhiteSpace(result.RunId);

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

    public StationAckDto ReportResultGap(string connectionId, StationResultGapDto gap)
    {
        List<StoredStationRegistryEvent> events;
        StationAckDto ack;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(gap.StationId);
            TouchConnectionLocked(entry, connectionId, now);

            ack = _centralStore?.ReportResultGap(gap)
                ?? new StationAckDto
                {
                    StationId = gap.StationId,
                    AcceptedSequenceId = gap.DroppedThroughSequenceId,
                    LastPersistedSequenceId = Math.Max(entry.LastAcceptedSequenceId, gap.DroppedThroughSequenceId),
                    Duplicate = gap.DroppedThroughSequenceId <= entry.LastAcceptedSequenceId,
                    Message = "Result gap acknowledged.",
                    ServerTimeUtc = now,
                    CreatedAtUtc = now
                };

            entry.LastAcceptedSequenceId = Math.Max(entry.LastAcceptedSequenceId, ack.LastPersistedSequenceId);
            entry.AcceptedResultSequences.RemoveWhere(sequenceId => sequenceId <= entry.LastAcceptedSequenceId);
            events =
            [
                CreateEventLocked("stationUpserted", ToStatusViewModelLocked(entry, now)),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return ack;
    }

    public StationAckDto UpsertHealthSnapshot(string connectionId, StationHealthSnapshotDto snapshot)
    {
        List<StoredStationRegistryEvent> events;
        StationAckDto ack;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(snapshot.StationId);
            TouchConnectionLocked(entry, connectionId, now);
            entry.RuntimeState = snapshot.RuntimeState;
            entry.State = StationSyncStateMapper.ToRuntimeHostState(snapshot.RuntimeState);
            entry.SpoolPendingCount = snapshot.SpoolPendingCount;
            entry.SpoolBytes = snapshot.SpoolBytes;
            entry.CpuUsagePercent = snapshot.CpuUsagePercent;
            entry.WorkingSetMb = snapshot.WorkingSetMb;
            entry.DiskFreeMb = snapshot.DiskFreeMb;
            entry.DiskTotalMb = snapshot.DiskTotalMb;
            entry.CameraStatusSummary = snapshot.CameraStatusSummary;
            entry.PlcStatusSummary = snapshot.PlcStatusSummary;
            entry.PackageId = snapshot.CurrentPackageId ?? entry.PackageId;
            entry.CurrentPackageHealth = snapshot.CurrentPackageHealth;
            entry.LastDiagnosticCode = snapshot.LastErrorCode ?? entry.LastDiagnosticCode;
            entry.LastDiagnosticMessage = snapshot.LastErrorMessage ?? entry.LastDiagnosticMessage;
            entry.OnlineState = EvaluateOnlineState(snapshot, now, entry);

            if (snapshot.SequenceId > entry.LastHealthSequenceId)
            {
                entry.LastHealthSequenceId = snapshot.SequenceId;
                entry.RecentHealth.Insert(0, CloneHealth(snapshot));
                while (entry.RecentHealth.Count > Math.Max(10, _options.HealthBufferPerStation))
                {
                    entry.RecentHealth.RemoveAt(entry.RecentHealth.Count - 1);
                }
            }

            ack = _centralStore?.UpsertHealthSnapshot(snapshot)
                ?? new StationAckDto
                {
                    StationId = snapshot.StationId,
                    AcceptedSequenceId = snapshot.SequenceId,
                    LastPersistedSequenceId = entry.LastAcceptedSequenceId
                };
            var stationViewModel = ToStatusViewModelLocked(entry, now);
            events =
            [
                CreateEventLocked(
                    "stationHealthUpdated",
                    new StationHealthEventViewModel
                    {
                        StationId = entry.StationId,
                        Health = CloneHealth(snapshot),
                        Station = stationViewModel
                    }),
                CreateEventLocked("stationUpserted", stationViewModel),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return ack;
    }

    public StationAckDto UpsertLogSummary(string connectionId, StationLogSummaryDto log)
    {
        List<StoredStationRegistryEvent> events;
        StationAckDto ack;
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(log.StationId);
            TouchConnectionLocked(entry, connectionId, now);

            if (log.SequenceId > entry.LastLogSequenceId)
            {
                entry.LastLogSequenceId = log.SequenceId;
                entry.RecentLogs.Insert(0, CloneLog(log));
                while (entry.RecentLogs.Count > Math.Max(10, _options.LogBufferPerStation))
                {
                    entry.RecentLogs.RemoveAt(entry.RecentLogs.Count - 1);
                }
            }

            ack = _centralStore?.UpsertLogSummary(log)
                ?? new StationAckDto
                {
                    StationId = log.StationId,
                    AcceptedSequenceId = log.SequenceId,
                    LastPersistedSequenceId = entry.LastAcceptedSequenceId
                };
            events =
            [
                CreateEventLocked("stationLogAdded", new { StationId = entry.StationId, Log = CloneLog(log), Station = ToStatusViewModelLocked(entry, now) })
            ];
        }

        PublishEvents(events);
        return ack;
    }

    public StationReplayCursorDto GetReplayCursor(string stationId)
    {
        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(stationId);
            entry.LastAcceptedSequenceId = Math.Max(
                entry.LastAcceptedSequenceId,
                _centralStore?.GetLastPersistedSequenceId(stationId) ?? 0);
            return BuildCursor(entry);
        }
    }

    public StationCommandDto? PollCommand(string stationId)
    {
        var command = _centralStore?.PollCommand(stationId);
        if (command == null)
        {
            return null;
        }

        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(stationId);
            UpsertCommandLocked(entry, command);
        }

        PublishEvents([new StoredStationRegistryEvent(Interlocked.Increment(ref _nextEventSequenceId), "stationCommandUpdated", command, DateTimeOffset.UtcNow)]);
        return command;
    }

    public bool ReportCommandResult(
        string authenticatedStationId,
        StationCommandResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var normalizedStationId = authenticatedStationId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStationId) ||
            !string.Equals(result.StationId?.Trim(), normalizedStationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var command = _centralStore?.ReportCommandResult(normalizedStationId, result);
        if (command is null ||
            !string.Equals(command.StationId, normalizedStationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        StoredStationRegistryEvent storedEvent;
        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(normalizedStationId);
            UpsertCommandLocked(entry, command);
            storedEvent = CreateEventLocked("stationCommandUpdated", command);
        }

        PublishEvents([storedEvent]);
        return true;
    }

    public bool TryGetRegisteredStationId(string connectionId, out string? stationId)
    {
        lock (_syncRoot)
        {
            var entry = _entries.Values.FirstOrDefault(item =>
                string.Equals(item.ConnectionId, connectionId, StringComparison.Ordinal));
            stationId = entry?.StationId;
            return !string.IsNullOrWhiteSpace(stationId);
        }
    }

    public StationStatusViewModel UpdateIdentity(
        string stationId,
        StationIdentityUpdateRequest request,
        string userName,
        string? clientIp)
    {
        _centralStore?.UpdateStationIdentity(stationId, request, userName, clientIp);

        StationStatusViewModel viewModel;
        List<StoredStationRegistryEvent> events;
        var now = DateTimeOffset.UtcNow;
        lock (_syncRoot)
        {
            var entry = GetOrCreateEntryLocked(stationId);
            ApplyIdentityLocked(entry, request);
            viewModel = ToStatusViewModelLocked(entry, now);
            events =
            [
                CreateEventLocked("stationUpserted", viewModel),
                CreateEventLocked("summaryUpdated", BuildSummaryLocked(now))
            ];
        }

        PublishEvents(events);
        return viewModel;
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
                StationName = status.StationName,
                AreaName = status.AreaName,
                WorkcellName = status.WorkcellName,
                InspectionNodeName = status.InspectionNodeName,
                CameraAlias = status.CameraAlias,
                StationRole = status.StationRole,
                Owner = status.Owner,
                IsEnabled = status.IsEnabled,
                Remark = status.Remark,
                OnlineState = status.OnlineState,
                State = status.State,
                RuntimeState = status.RuntimeState,
                IsOnline = status.IsOnline,
                StartedAtUtc = status.StartedAtUtc,
                LastSeenAtUtc = status.LastSeenAtUtc,
                PackageId = status.PackageId,
                PackageName = status.PackageName,
                PackageFlowHash = status.PackageFlowHash,
                ExecutionFlowHash = status.ExecutionFlowHash,
                FlowHash = status.FlowHash,
                ExecutionSnapshotId = status.ExecutionSnapshotId,
                ProjectRevision = status.ProjectRevision,
                DecisionConfigurationHash = status.DecisionConfigurationHash,
                ExecutionRunMode = status.ExecutionRunMode,
                CurrentRunId = status.CurrentRunId,
                SessionOkCount = status.SessionOkCount,
                SessionNgCount = status.SessionNgCount,
                SessionErrorCount = status.SessionErrorCount,
                SessionOutcomeStatistics = status.SessionOutcomeStatistics,
                SessionOutcomeStatisticsIsLegacyProjection = status.SessionOutcomeStatisticsIsLegacyProjection,
                LastOutcome = status.LastOutcome,
                LastInspectionStatus = status.LastInspectionStatus,
                LastExecutionOutcome = status.LastExecutionOutcome,
                LastDecisionOutcome = status.LastDecisionOutcome,
                LastHasJudgmentSignal = status.LastHasJudgmentSignal,
                LastDecisionSource = status.LastDecisionSource,
                LastReasonCode = status.LastReasonCode,
                LastDiagnosticCode = status.LastDiagnosticCode,
                LastDiagnosticMessage = status.LastDiagnosticMessage,
                LastResultAtUtc = status.LastResultAtUtc,
                LastSequenceId = status.LastSequenceId,
                AverageExecutionTimeMs = status.AverageExecutionTimeMs,
                RecentResultCount = status.RecentResultCount,
                SpoolPendingCount = status.SpoolPendingCount,
                SpoolBytes = status.SpoolBytes,
                CpuUsagePercent = status.CpuUsagePercent,
                WorkingSetMb = status.WorkingSetMb,
                DiskFreeMb = status.DiskFreeMb,
                DiskTotalMb = status.DiskTotalMb,
                CameraStatusSummary = status.CameraStatusSummary,
                PlcStatusSummary = status.PlcStatusSummary,
                CurrentPackageHealth = status.CurrentPackageHealth,
                RecentResults = entry.RecentResults.Select(CloneResult).ToList(),
                RecentHealth = entry.RecentHealth.Select(CloneHealth).ToList(),
                RecentLogs = entry.RecentLogs.Select(CloneLog).ToList(),
                RecentCommands = entry.RecentCommands.ToList()
            };
        }
    }

    public IReadOnlyList<StationResultSummaryDto> GetRecentResults(string stationId, int take)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                return _centralStore?.GetRecentResults(stationId, take) ?? Array.Empty<StationResultSummaryDto>();
            }

            var memoryResults = entry.RecentResults
                .Take(Math.Max(1, take))
                .Select(CloneResult)
                .ToList();
            return memoryResults.Count > 0
                ? memoryResults
                : (_centralStore?.GetRecentResults(stationId, take) ?? memoryResults);
        }
    }

    public StationResultsPageViewModel GetResultsPage(
        string? stationId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? status,
        string? diagnosticCode,
        int pageIndex,
        int pageSize)
    {
        if (_centralStore != null)
        {
            return _centralStore.GetResultsPage(
                stationId,
                fromUtc,
                toUtc,
                status,
                diagnosticCode,
                pageIndex,
                pageSize);
        }

        lock (_syncRoot)
        {
            var normalizedPageIndex = Math.Max(0, pageIndex);
            var normalizedPageSize = Math.Clamp(pageSize, 1, 500);
            var filtered = _entries.Values
                .Where(entry => string.IsNullOrWhiteSpace(stationId) ||
                                string.Equals(entry.StationId, stationId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.RecentResults.Select(CloneResult))
                .Where(result => !fromUtc.HasValue || result.CompletedAtUtc >= fromUtc.Value)
                .Where(result => !toUtc.HasValue || result.CompletedAtUtc <= toUtc.Value)
                .Where(result => MatchesStatus(result, status))
                .Where(result => MatchesText(result.DiagnosticCode, diagnosticCode))
                .OrderByDescending(result => result.CompletedAtUtc)
                .ThenByDescending(result => result.SequenceId)
                .ToList();

            return new StationResultsPageViewModel
            {
                Items = filtered
                    .Skip(normalizedPageIndex * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .ToList(),
                TotalCount = filtered.Count,
                PageIndex = normalizedPageIndex,
                PageSize = normalizedPageSize
            };
        }
    }

    public StationResultStatisticsViewModel GetStatistics(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? stationId,
        string? status,
        string? diagnosticCode)
    {
        if (_centralStore != null)
        {
            return _centralStore.GetStatistics(fromUtc, toUtc, stationId, status, diagnosticCode);
        }

        lock (_syncRoot)
        {
            var results = _entries.Values
                .Where(entry => string.IsNullOrWhiteSpace(stationId) ||
                                string.Equals(entry.StationId, stationId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.RecentResults.Select(CloneResult))
                .Where(result => !fromUtc.HasValue || result.CompletedAtUtc >= fromUtc.Value)
                .Where(result => !toUtc.HasValue || result.CompletedAtUtc <= toUtc.Value)
                .Where(result => MatchesStatus(result, status))
                .Where(result => MatchesText(result.DiagnosticCode, diagnosticCode))
                .ToList();

            return StationOutcomeStatisticsBuilder.Build(results, fromUtc, toUtc);
        }
    }

    public IReadOnlyList<StationHealthSnapshotDto> GetRecentHealth(string stationId, int take)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                return _centralStore?.GetRecentHealth(stationId, take) ?? Array.Empty<StationHealthSnapshotDto>();
            }

            var memory = entry.RecentHealth
                .Take(Math.Max(1, take))
                .Select(CloneHealth)
                .ToList();
            return memory.Count > 0 ? memory : (_centralStore?.GetRecentHealth(stationId, take) ?? memory);
        }
    }

    public IReadOnlyList<StationLogSummaryDto> GetRecentLogs(string stationId, int take)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                return _centralStore?.GetRecentLogs(stationId, take) ?? Array.Empty<StationLogSummaryDto>();
            }

            var memory = entry.RecentLogs
                .Take(Math.Max(1, take))
                .Select(CloneLog)
                .ToList();
            return memory.Count > 0 ? memory : (_centralStore?.GetRecentLogs(stationId, take) ?? memory);
        }
    }

    public IReadOnlyList<StationCommandDto> GetRecentCommands(string stationId, int take)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                return _centralStore?.GetCommands(stationId, take) ?? Array.Empty<StationCommandDto>();
            }

            var memory = entry.RecentCommands
                .Take(Math.Max(1, take))
                .ToList();
            return memory.Count > 0 ? memory : (_centralStore?.GetCommands(stationId, take) ?? memory);
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
        string? packageVersion,
        string? packageFlowHash,
        string? executionFlowHash,
        string? flowHash,
        Guid? executionSnapshotId,
        long? projectRevision,
        string? decisionConfigurationHash,
        string? executionRunMode,
        string? currentRunId,
        int sessionOkCount,
        int sessionNgCount,
        int sessionErrorCount,
        InspectionOutcomeStatistics? sessionOutcomeStatistics,
        int spoolPendingCount,
        string connectionId,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(lineName))
        {
            entry.LineName = lineName;
        }

        entry.State = state;
        entry.RuntimeState = StationSyncStateMapper.ToStationRuntimeState(state);
        entry.PackageId = packageId;
        entry.PackageName = packageName;
        entry.PackageVersion = packageVersion;
        entry.PackageFlowHash = NullIfWhiteSpace(packageFlowHash);
        entry.ExecutionFlowHash = NullIfWhiteSpace(executionFlowHash) ?? NullIfWhiteSpace(flowHash);
        entry.FlowHash = entry.ExecutionFlowHash;
        entry.ExecutionSnapshotId = executionSnapshotId;
        entry.ProjectRevision = projectRevision;
        entry.DecisionConfigurationHash = NullIfWhiteSpace(decisionConfigurationHash);
        entry.ExecutionRunMode = NullIfWhiteSpace(executionRunMode);
        entry.CurrentRunId = currentRunId;
        entry.SessionOkCount = sessionOkCount;
        entry.SessionNgCount = sessionNgCount;
        entry.SessionErrorCount = sessionErrorCount;
        entry.SessionOutcomeStatistics = sessionOutcomeStatistics;
        entry.SpoolPendingCount = spoolPendingCount;
        TouchConnectionLocked(entry, connectionId, now);
    }

    private static InspectionOutcomeStatistics ResolveSessionOutcomeStatistics(StationRegistryEntry entry)
    {
        return entry.SessionOutcomeStatistics ?? StationOutcomeStatisticsBuilder.ProjectLegacySession(
            entry.SessionOkCount,
            entry.SessionNgCount,
            entry.SessionErrorCount);
    }

    private static void ApplyIdentityLocked(StationRegistryEntry entry, StationIdentityUpdateRequest request)
    {
        entry.StationName = Choose(request.StationName, entry.StationName);
        entry.LineName = ChooseNullable(request.LineName, entry.LineName);
        entry.AreaName = ChooseNullable(request.AreaName, entry.AreaName);
        entry.WorkcellName = ChooseNullable(request.WorkcellName, entry.WorkcellName);
        entry.InspectionNodeName = ChooseNullable(request.InspectionNodeName, entry.InspectionNodeName);
        entry.CameraAlias = ChooseNullable(request.CameraAlias, entry.CameraAlias);
        entry.StationRole = Choose(request.StationRole, entry.StationRole);
        entry.Owner = ChooseNullable(request.Owner, entry.Owner);
        entry.Remark = ChooseNullable(request.Remark, entry.Remark);
        if (request.IsEnabled.HasValue)
        {
            entry.IsEnabled = request.IsEnabled.Value;
        }
    }

    private static string Choose(string? candidate, string existing)
    {
        return string.IsNullOrWhiteSpace(candidate) ? existing : candidate.Trim();
    }

    private static string? ChooseNullable(string? candidate, string? existing)
    {
        return string.IsNullOrWhiteSpace(candidate) ? existing : candidate.Trim();
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void RestorePersistedStations()
    {
        if (_centralStore == null)
        {
            return;
        }

        foreach (var status in _centralStore.GetStationStatuses())
        {
            var entry = GetOrCreateEntryLocked(status.StationId);
            entry.StationName = status.StationName;
            entry.LineName = status.LineName;
            entry.AreaName = status.AreaName;
            entry.WorkcellName = status.WorkcellName;
            entry.InspectionNodeName = status.InspectionNodeName;
            entry.CameraAlias = status.CameraAlias;
            entry.StationRole = status.StationRole;
            entry.Owner = status.Owner;
            entry.MachineName = status.MachineName;
            entry.IsEnabled = status.IsEnabled;
            entry.Remark = status.Remark;
            entry.OnlineState = status.OnlineState;
            entry.State = status.State;
            entry.RuntimeState = status.RuntimeState;
            entry.StartedAtUtc = status.StartedAtUtc;
            entry.LastSeenAtUtc = status.LastSeenAtUtc;
            entry.PackageId = status.PackageId;
            entry.PackageName = status.PackageName;
            entry.PackageFlowHash = status.PackageFlowHash;
            entry.ExecutionFlowHash = status.ExecutionFlowHash;
            entry.FlowHash = status.ExecutionFlowHash ?? status.FlowHash;
            entry.ExecutionSnapshotId = status.ExecutionSnapshotId;
            entry.ProjectRevision = status.ProjectRevision;
            entry.DecisionConfigurationHash = status.DecisionConfigurationHash;
            entry.ExecutionRunMode = status.ExecutionRunMode;
            entry.CurrentRunId = status.CurrentRunId;
        }
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
            StationName = entry.StationName,
            AreaName = entry.AreaName,
            WorkcellName = entry.WorkcellName,
            InspectionNodeName = entry.InspectionNodeName,
            CameraAlias = entry.CameraAlias,
            StationRole = entry.StationRole,
            Owner = entry.Owner,
            IsEnabled = entry.IsEnabled,
            Remark = entry.Remark,
            OnlineState = entry.OnlineState,
            State = entry.State,
            RuntimeState = entry.RuntimeState,
            IsOnline = IsOnlineLocked(entry, now),
            StartedAtUtc = entry.StartedAtUtc,
            LastSeenAtUtc = entry.LastSeenAtUtc,
            PackageId = entry.PackageId,
            PackageName = entry.PackageName,
            PackageFlowHash = entry.PackageFlowHash,
            ExecutionFlowHash = entry.ExecutionFlowHash,
            FlowHash = entry.FlowHash,
            ExecutionSnapshotId = entry.ExecutionSnapshotId,
            ProjectRevision = entry.ProjectRevision,
            DecisionConfigurationHash = entry.DecisionConfigurationHash,
            ExecutionRunMode = entry.ExecutionRunMode,
            CurrentRunId = entry.CurrentRunId,
            SessionOkCount = entry.SessionOkCount,
            SessionNgCount = entry.SessionNgCount,
            SessionErrorCount = entry.SessionErrorCount,
            SessionOutcomeStatistics = ResolveSessionOutcomeStatistics(entry),
            SessionOutcomeStatisticsIsLegacyProjection = entry.SessionOutcomeStatistics == null,
            LastOutcome = entry.LastOutcome,
            LastInspectionStatus = entry.LastInspectionStatus,
            LastExecutionOutcome = entry.LastExecutionOutcome,
            LastDecisionOutcome = entry.LastDecisionOutcome,
            LastHasJudgmentSignal = entry.LastHasJudgmentSignal,
            LastDecisionSource = entry.LastDecisionSource,
            LastReasonCode = entry.LastReasonCode,
            LastDiagnosticCode = entry.LastDiagnosticCode,
            LastDiagnosticMessage = entry.LastDiagnosticMessage,
            LastResultAtUtc = entry.LastResultAtUtc,
            LastSequenceId = entry.LastAcceptedSequenceId,
            AverageExecutionTimeMs = entry.RecentResults.Count == 0
                ? 0
                : entry.RecentResults.Average(result => result.ExecutionTimeMs),
            RecentResultCount = entry.RecentResults.Count,
            SpoolPendingCount = entry.SpoolPendingCount,
            SpoolBytes = entry.SpoolBytes,
            CpuUsagePercent = entry.CpuUsagePercent,
            WorkingSetMb = entry.WorkingSetMb,
            DiskFreeMb = entry.DiskFreeMb,
            DiskTotalMb = entry.DiskTotalMb,
            CameraStatusSummary = entry.CameraStatusSummary,
            PlcStatusSummary = entry.PlcStatusSummary,
            CurrentPackageHealth = entry.CurrentPackageHealth
        };
    }

    private StationSummaryViewModel BuildSummaryLocked(DateTimeOffset now)
    {
        var stations = _entries.Values.ToList();
        var onlineStations = stations.Count(entry => IsOnlineLocked(entry, now));
        var faultedStations = stations.Count(entry => entry.State == RuntimeHostState.Faulted);
        var runningStations = stations.Count(entry => entry.State == RuntimeHostState.Running && IsOnlineLocked(entry, now));
        var recentResults = stations.SelectMany(entry => entry.RecentResults).ToList();
        var warningStations = stations.Count(entry => entry.OnlineState == StationOnlineState.Warning || entry.OnlineState == StationOnlineState.Degraded);
        var criticalStations = stations.Count(entry => entry.OnlineState == StationOnlineState.Critical);

        var outcomeStatistics = StationOutcomeStatisticsBuilder.Combine(stations.Select(ResolveSessionOutcomeStatistics));
        return new StationSummaryViewModel
        {
            TotalStations = stations.Count,
            OnlineStations = onlineStations,
            OfflineStations = stations.Count - onlineStations,
            RunningStations = runningStations,
            FaultedStations = faultedStations,
            AlertCount = stations.Count(entry => !IsOnlineLocked(entry, now) || entry.State == RuntimeHostState.Faulted || entry.OnlineState is StationOnlineState.Warning or StationOnlineState.Degraded or StationOnlineState.Critical),
            WarningStations = warningStations,
            CriticalStations = criticalStations,
            TotalOkCount = outcomeStatistics.OkCount,
            TotalNgCount = outcomeStatistics.NgCount,
            TotalErrorCount = outcomeStatistics.ExecutionFailureCount,
            OutcomeStatistics = outcomeStatistics,
            AverageExecutionTimeMs = recentResults.Count == 0 ? 0 : recentResults.Average(result => result.ExecutionTimeMs),
            OfflineThresholdSeconds = Math.Max(1, _options.OfflineThresholdSeconds),
            UpdatedAtUtc = now
        };
    }

    private bool IsOnlineLocked(StationRegistryEntry entry, DateTimeOffset now)
    {
        return entry.IsEnabled &&
               !string.IsNullOrWhiteSpace(entry.ConnectionId) &&
               now - entry.LastSeenAtUtc <= TimeSpan.FromSeconds(Math.Max(1, _options.OfflineThresholdSeconds));
    }

    private static bool MatchesStatus(StationResultSummaryDto result, string? requestedStatus) =>
        StationOutcomeStatisticsBuilder.MatchesStatus(result, requestedStatus);

    private static bool MatchesText(string? value, string? requestedValue)
    {
        return string.IsNullOrWhiteSpace(requestedValue) ||
               string.Equals(requestedValue, "all", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, requestedValue, StringComparison.OrdinalIgnoreCase);
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
        var lastPersisted = Math.Max(
            entry.LastAcceptedSequenceId,
            _centralStore?.GetLastPersistedSequenceId(entry.StationId) ?? 0);
        entry.LastAcceptedSequenceId = lastPersisted;
        return new StationReplayCursorDto
        {
            SchemaVersion = StationSyncContractDefaults.SchemaVersion,
            StationId = entry.StationId,
            AckedSequenceId = lastPersisted,
            ServerTimeUtc = DateTimeOffset.UtcNow
        };
    }

    private static void AdvanceMemoryCursor(StationRegistryEntry entry, long sequenceId)
    {
        if (entry.LastAcceptedSequenceId == 0 && entry.AcceptedResultSequences.Count == 1)
        {
            entry.LastAcceptedSequenceId = sequenceId;
            return;
        }

        while (entry.AcceptedResultSequences.Contains(entry.LastAcceptedSequenceId + 1))
        {
            entry.LastAcceptedSequenceId++;
        }
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

    private void UpsertCommandLocked(StationRegistryEntry entry, StationCommandDto command)
    {
        var existingIndex = entry.RecentCommands.FindIndex(item => string.Equals(item.CommandId, command.CommandId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            entry.RecentCommands[existingIndex] = command;
        }
        else
        {
            entry.RecentCommands.Insert(0, command);
        }

        while (entry.RecentCommands.Count > Math.Max(10, _options.CommandBufferPerStation))
        {
            entry.RecentCommands.RemoveAt(entry.RecentCommands.Count - 1);
        }
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
            MessageId = result.MessageId,
            RunId = result.RunId,
            PackageId = result.PackageId,
            PackageName = result.PackageName,
            PackageVersion = result.PackageVersion,
            PackageFlowHash = result.PackageFlowHash,
            ExecutionFlowHash = result.ExecutionFlowHash,
            FlowHash = result.FlowHash,
            ProjectRevision = result.ProjectRevision,
            DecisionConfigurationHash = result.DecisionConfigurationHash,
            ExecutionSnapshotId = result.ExecutionSnapshotId,
            ExecutionRunMode = result.ExecutionRunMode,
            ImageId = result.ImageId,
            Outcome = result.Outcome,
            InspectionStatus = result.InspectionStatus,
            ExecutionOutcome = result.ExecutionOutcome,
            DecisionOutcome = result.DecisionOutcome,
            HasJudgmentSignal = result.HasJudgmentSignal,
            DecisionSource = result.DecisionSource,
            ReasonCode = result.ReasonCode,
            ExecutionTimeMs = result.ExecutionTimeMs,
            DiagnosticCode = result.DiagnosticCode,
            DiagnosticMessage = result.DiagnosticMessage,
            PrimaryOutputsPreview = new Dictionary<string, string?>(result.PrimaryOutputsPreview, StringComparer.OrdinalIgnoreCase),
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc,
            CreatedAtUtc = result.CreatedAtUtc
        };
    }

    private static StationHealthSnapshotDto CloneHealth(StationHealthSnapshotDto snapshot)
    {
        return new StationHealthSnapshotDto
        {
            SchemaVersion = snapshot.SchemaVersion,
            StationId = snapshot.StationId,
            SequenceId = snapshot.SequenceId,
            MessageId = snapshot.MessageId,
            RuntimeState = snapshot.RuntimeState,
            ProcessUptimeSeconds = snapshot.ProcessUptimeSeconds,
            CpuUsagePercent = snapshot.CpuUsagePercent,
            WorkingSetMb = snapshot.WorkingSetMb,
            PrivateMemoryMb = snapshot.PrivateMemoryMb,
            DiskFreeMb = snapshot.DiskFreeMb,
            DiskTotalMb = snapshot.DiskTotalMb,
            SpoolPendingCount = snapshot.SpoolPendingCount,
            SpoolBytes = snapshot.SpoolBytes,
            CameraStatusSummary = snapshot.CameraStatusSummary,
            PlcStatusSummary = snapshot.PlcStatusSummary,
            CurrentPackageId = snapshot.CurrentPackageId,
            CurrentPackageHealth = snapshot.CurrentPackageHealth,
            LastErrorCode = snapshot.LastErrorCode,
            LastErrorMessage = snapshot.LastErrorMessage,
            CreatedAtUtc = snapshot.CreatedAtUtc
        };
    }

    private static StationLogSummaryDto CloneLog(StationLogSummaryDto log)
    {
        return new StationLogSummaryDto
        {
            SchemaVersion = log.SchemaVersion,
            StationId = log.StationId,
            SequenceId = log.SequenceId,
            MessageId = log.MessageId,
            TimestampUtc = log.TimestampUtc,
            Level = log.Level,
            Source = log.Source,
            EventId = log.EventId,
            MessageTemplate = log.MessageTemplate,
            RenderedMessage = log.RenderedMessage,
            ExceptionType = log.ExceptionType,
            ExceptionMessage = log.ExceptionMessage,
            CorrelationId = log.CorrelationId,
            RunId = log.RunId,
            PackageId = log.PackageId,
            CreatedAtUtc = log.CreatedAtUtc
        };
    }

    private static StationOnlineState EvaluateOnlineState(
        StationHealthSnapshotDto snapshot,
        DateTimeOffset now,
        StationRegistryEntry entry)
    {
        if (now - entry.LastSeenAtUtc > TimeSpan.FromSeconds(1))
        {
            return entry.OnlineState;
        }

        if (snapshot.RuntimeState == StationRuntimeState.Faulted)
        {
            return StationOnlineState.Critical;
        }

        if (snapshot.DiskTotalMb > 0)
        {
            var freeRatio = (double)snapshot.DiskFreeMb / snapshot.DiskTotalMb;
            if (freeRatio < 0.05d)
            {
                return StationOnlineState.Critical;
            }

            if (freeRatio < 0.10d)
            {
                return StationOnlineState.Warning;
            }
        }

        if (snapshot.SpoolPendingCount > 10_000 ||
            (snapshot.CameraStatusSummary?.Contains("Disconnected", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return StationOnlineState.Critical;
        }

        if (snapshot.SpoolPendingCount > 1_000)
        {
            return StationOnlineState.Warning;
        }

        return StationOnlineState.Online;
    }

    private sealed class StationRegistryEntry
    {
        public string StationId { get; set; } = string.Empty;

        public string StationName { get; set; } = string.Empty;

        public string? LineName { get; set; }

        public string? AreaName { get; set; }

        public string? WorkcellName { get; set; }

        public string? InspectionNodeName { get; set; }

        public string? CameraAlias { get; set; }

        public string StationRole { get; set; } = string.Empty;

        public string? Owner { get; set; }

        public bool IsEnabled { get; set; } = true;

        public string? Remark { get; set; }

        public string MachineName { get; set; } = string.Empty;

        public string ClientVersion { get; set; } = string.Empty;

        public string? ConnectionId { get; set; }

        public StationOnlineState OnlineState { get; set; } = StationOnlineState.Unknown;

        public RuntimeHostState State { get; set; }

        public StationRuntimeState RuntimeState { get; set; } = StationRuntimeState.Unknown;

        public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public string? PackageId { get; set; }

        public string? PackageName { get; set; }

        public string? PackageVersion { get; set; }

        public string? PackageFlowHash { get; set; }

        public string? ExecutionFlowHash { get; set; }

        public string? FlowHash { get; set; }

        public Guid? ExecutionSnapshotId { get; set; }

        public long? ProjectRevision { get; set; }

        public string? DecisionConfigurationHash { get; set; }

        public string? ExecutionRunMode { get; set; }

        public string? CurrentRunId { get; set; }

        public int SessionOkCount { get; set; }

        public int SessionNgCount { get; set; }

        public int SessionErrorCount { get; set; }

        public InspectionOutcomeStatistics? SessionOutcomeStatistics { get; set; }

        public RuntimeRunOutcome? LastOutcome { get; set; }

        public InspectionStatus? LastInspectionStatus { get; set; }

        public ExecutionOutcome? LastExecutionOutcome { get; set; }

        public DecisionOutcome? LastDecisionOutcome { get; set; }

        public bool? LastHasJudgmentSignal { get; set; }

        public string? LastDecisionSource { get; set; }

        public string? LastReasonCode { get; set; }

        public string? LastDiagnosticCode { get; set; }

        public string? LastDiagnosticMessage { get; set; }

        public DateTimeOffset? LastResultAtUtc { get; set; }

        public long LastAcceptedSequenceId { get; set; }

        public SortedSet<long> AcceptedResultSequences { get; } = [];

        public long LastHealthSequenceId { get; set; }

        public long LastLogSequenceId { get; set; }

        public int SpoolPendingCount { get; set; }

        public long SpoolBytes { get; set; }

        public double? CpuUsagePercent { get; set; }

        public long WorkingSetMb { get; set; }

        public long DiskFreeMb { get; set; }

        public long DiskTotalMb { get; set; }

        public string? CameraStatusSummary { get; set; }

        public string? PlcStatusSummary { get; set; }

        public string? CurrentPackageHealth { get; set; }

        public List<StationResultSummaryDto> RecentResults { get; } = [];

        public List<StationHealthSnapshotDto> RecentHealth { get; } = [];

        public List<StationLogSummaryDto> RecentLogs { get; } = [];

        public List<StationCommandDto> RecentCommands { get; } = [];
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
