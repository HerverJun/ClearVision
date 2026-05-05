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

    public Task<StationReplayCursorDto> RegisterStationAsync(StationRegistrationDto registration)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertRegistration(Context.ConnectionId, registration));
    }

    public Task<StationRegisterAckDto> RegisterStation(StationRegistrationDto registration)
    {
        EnsureAuthorized();
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
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertHeartbeat(Context.ConnectionId, heartbeat));
    }

    public Task<StationAckDto> Heartbeat(StationHeartbeatDto heartbeat)
    {
        EnsureAuthorized();
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
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertSnapshot(Context.ConnectionId, snapshot));
    }

    public Task<StationAckDto> PushHealth(StationHealthSnapshotDto snapshot)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertHealthSnapshot(Context.ConnectionId, snapshot));
    }

    public Task<StationReplayCursorDto> PushResultSummaryAsync(StationResultSummaryDto result)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertResultSummary(Context.ConnectionId, result));
    }

    public Task<StationAckDto> PushResult(StationResultSummaryDto result)
    {
        EnsureAuthorized();
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
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertLogSummary(Context.ConnectionId, log));
    }

    public Task<StationReplayCursorDto> GetReplayCursor(string stationId)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.GetReplayCursor(stationId));
    }

    public Task<StationCommandDto?> PollCommand(string stationId)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.PollCommand(stationId));
    }

    public Task ReportCommandResult(StationCommandResultDto result)
    {
        EnsureAuthorized();
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
}
