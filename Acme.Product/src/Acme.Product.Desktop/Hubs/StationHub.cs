using Acme.Product.Desktop.Station;
using Acme.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Acme.Product.Desktop.Hubs;

public sealed class StationHub : Hub
{
    private readonly StationRegistryService _registryService;
    private readonly StationIngressAuthService _authService;

    public StationHub(
        StationRegistryService registryService,
        StationIngressAuthService authService)
    {
        _registryService = registryService;
        _authService = authService;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _registryService.MarkDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task<StationProbeAckDto> Probe()
    {
        EnsureAuthorized();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new StationProbeAckDto
        {
            Accepted = true,
            Message = "Station ingress probe accepted.",
            ServerTimeUtc = now,
            CreatedAtUtc = now
        });
    }

    public Task<StationReplayCursorDto> RegisterStationAsync(StationRegistrationDto registration)
    {
        EnsureAuthorizedForRegistration(registration);
        return Task.FromResult(_registryService.UpsertRegistration(Context.ConnectionId, registration));
    }

    public Task<StationRegisterAckDto> RegisterStation(StationRegistrationDto registration)
    {
        EnsureAuthorizedForRegistration(registration);
        var cursor = _registryService.UpsertRegistration(Context.ConnectionId, registration);
        return Task.FromResult(new StationRegisterAckDto
        {
            StationId = cursor.StationId,
            Accepted = true,
            LastPersistedSequenceId = cursor.AckedSequenceId,
            Message = "Registered",
            ServerTimeUtc = cursor.ServerTimeUtc,
            CreatedAtUtc = cursor.CreatedAtUtc
        });
    }

    public Task<StationReplayCursorDto> PushHeartbeatAsync(StationHeartbeatDto heartbeat)
    {
        EnsureAuthorizedForStation(heartbeat.StationId);
        return Task.FromResult(_registryService.UpsertHeartbeat(Context.ConnectionId, heartbeat));
    }

    public Task<StationAckDto> Heartbeat(StationHeartbeatDto heartbeat)
    {
        EnsureAuthorizedForStation(heartbeat.StationId);
        var cursor = _registryService.UpsertHeartbeat(Context.ConnectionId, heartbeat);
        return Task.FromResult(new StationAckDto
        {
            StationId = cursor.StationId,
            AcceptedSequenceId = heartbeat.SequenceId,
            LastPersistedSequenceId = cursor.AckedSequenceId,
            ServerTimeUtc = cursor.ServerTimeUtc,
            CreatedAtUtc = cursor.CreatedAtUtc
        });
    }

    public Task<StationReplayCursorDto> PushSnapshotAsync(StationSnapshotDto snapshot)
    {
        EnsureAuthorizedForStation(snapshot.StationId);
        return Task.FromResult(_registryService.UpsertSnapshot(Context.ConnectionId, snapshot));
    }

    public Task<StationAckDto> PushHealth(StationHealthSnapshotDto snapshot)
    {
        EnsureAuthorizedForStation(snapshot.StationId);
        return Task.FromResult(_registryService.UpsertHealthSnapshot(Context.ConnectionId, snapshot));
    }

    public Task<StationReplayCursorDto> PushResultSummaryAsync(StationResultSummaryDto result)
    {
        EnsureAuthorizedForStation(result.StationId);
        return Task.FromResult(_registryService.UpsertResultSummary(Context.ConnectionId, result));
    }

    public Task<StationAckDto> ReportResultGap(StationResultGapDto gap)
    {
        EnsureAuthorizedForStation(gap.StationId);
        return Task.FromResult(_registryService.ReportResultGap(Context.ConnectionId, gap));
    }

    public Task<StationAckDto> PushResult(StationResultSummaryDto result)
    {
        EnsureAuthorizedForStation(result.StationId);
        var cursor = _registryService.UpsertResultSummary(Context.ConnectionId, result);
        return Task.FromResult(new StationAckDto
        {
            StationId = cursor.StationId,
            AcceptedSequenceId = result.SequenceId,
            LastPersistedSequenceId = cursor.AckedSequenceId,
            ServerTimeUtc = cursor.ServerTimeUtc,
            CreatedAtUtc = cursor.CreatedAtUtc
        });
    }

    public Task<StationAckDto> PushLog(StationLogSummaryDto log)
    {
        EnsureAuthorizedForStation(log.StationId);
        return Task.FromResult(_registryService.UpsertLogSummary(Context.ConnectionId, log));
    }

    public Task<StationReplayCursorDto> GetReplayCursor(string stationId)
    {
        stationId = EnsureAuthorizedForStation(stationId);
        return Task.FromResult(_registryService.GetReplayCursor(stationId));
    }

    public Task<StationCommandDto?> PollCommand(string stationId)
    {
        stationId = EnsureAuthorizedForStation(stationId);
        return Task.FromResult(_registryService.PollCommand(stationId));
    }

    public Task ReportCommandResult(StationCommandResultDto result)
    {
        EnsureAuthorizedForStation(result.StationId);
        _registryService.ReportCommandResult(result);
        return Task.CompletedTask;
    }

    private void EnsureAuthorized()
    {
        var httpContext = Context.GetHttpContext();
        if (_authService.TryAuthorize(httpContext, out var failureReason))
        {
            return;
        }

        throw new HubException(failureReason);
    }

    private void EnsureAuthorizedForRegistration(StationRegistrationDto registration)
    {
        EnsureAuthorized();
        var stationId = RequireStationId(registration.StationId);
        if (_registryService.TryGetRegisteredStationId(Context.ConnectionId, out var registeredStationId) &&
            !string.Equals(registeredStationId, stationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException($"Station connection is already registered for station '{registeredStationId}'.");
        }

        registration.StationId = stationId;
    }

    private string EnsureAuthorizedForStation(string? stationId)
    {
        EnsureAuthorized();
        var normalizedStationId = RequireStationId(stationId);
        if (!_registryService.TryGetRegisteredStationId(Context.ConnectionId, out var registeredStationId))
        {
            throw new HubException("Station connection is not registered.");
        }

        if (!string.Equals(registeredStationId, normalizedStationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException($"Station connection is not authorized for station '{normalizedStationId}'.");
        }

        return normalizedStationId;
    }

    private static string RequireStationId(string? stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new HubException("StationId is required.");
        }

        return stationId.Trim();
    }
}
