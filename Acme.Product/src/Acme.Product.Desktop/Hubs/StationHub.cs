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

    public Task<StationReplayCursorDto> PushHeartbeatAsync(StationHeartbeatDto heartbeat)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertHeartbeat(Context.ConnectionId, heartbeat));
    }

    public Task<StationReplayCursorDto> PushSnapshotAsync(StationSnapshotDto snapshot)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertSnapshot(Context.ConnectionId, snapshot));
    }

    public Task<StationReplayCursorDto> PushResultSummaryAsync(StationResultSummaryDto result)
    {
        EnsureAuthorized();
        return Task.FromResult(_registryService.UpsertResultSummary(Context.ConnectionId, result));
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
