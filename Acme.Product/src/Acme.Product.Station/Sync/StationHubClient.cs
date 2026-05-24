using Acme.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationHubClient : IAsyncDisposable
{
    private readonly StationSyncOptions _options;
    private readonly ILogger<StationHubClient> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private HubConnection? _connection;

    public StationHubClient(
        IOptions<StationSyncOptions> options,
        ILogger<StationHubClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ResolvedStudioHubUrl))
        {
            return false;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            _connection ??= BuildConnection();
            if (_connection.State == HubConnectionState.Connected ||
                _connection.State == HubConnectionState.Connecting ||
                _connection.State == HubConnectionState.Reconnecting)
            {
                return _connection.State == HubConnectionState.Connected;
            }

            await _connection.StartAsync(cancellationToken);
            _logger.LogInformation("Connected to Studio Station hub at {HubUrl}", _connection?.State == HubConnectionState.Connected ? BuildHubUrl() : "unknown");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Studio Station hub.");
            return false;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public Task<StationReplayCursorDto?> RegisterStationAsync(
        StationRegistrationDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>("RegisterStationAsync", payload, cancellationToken);
    }

    public Task<StationReplayCursorDto?> PushHeartbeatAsync(
        StationHeartbeatDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>("PushHeartbeatAsync", payload, cancellationToken);
    }

    public Task<StationReplayCursorDto?> PushSnapshotAsync(
        StationSnapshotDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>("PushSnapshotAsync", payload, cancellationToken);
    }

    public Task<StationReplayCursorDto?> PushResultSummaryAsync(
        StationResultSummaryDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>("PushResultSummaryAsync", payload, cancellationToken);
    }

    public Task<StationAckDto?> ReportResultGapAsync(
        StationResultGapDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationAckDto>("ReportResultGap", payload, cancellationToken);
    }

    public Task<StationAckDto?> PushHealthAsync(
        StationHealthSnapshotDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationAckDto>("PushHealth", payload, cancellationToken);
    }

    public Task<StationAckDto?> PushLogAsync(
        StationLogSummaryDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationAckDto>("PushLog", payload, cancellationToken);
    }

    public Task<StationCommandDto?> PollCommandAsync(string stationId, CancellationToken cancellationToken)
    {
        return InvokeAsync<StationCommandDto?>("PollCommand", stationId, cancellationToken);
    }

    public async Task<bool> ReportCommandResultAsync(StationCommandResultDto payload, CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            await _connection!.InvokeAsync("ReportCommandResult", payload, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Studio Station hub invocation failed: ReportCommandResult");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch
            {
            }
        }

        _connectionGate.Dispose();
    }

    private HubConnection BuildConnection()
    {
        var hubUrl = BuildHubUrl();
        var connection = new HubConnectionBuilder()
            .WithUrl(
                hubUrl,
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_options.SharedToken);
                    options.Headers[StationSyncContractDefaults.StationTokenHeaderName] = _options.SharedToken;
                })
            .WithAutomaticReconnect()
            .Build();

        connection.Closed += error =>
        {
            if (error != null)
            {
                _logger.LogWarning(error, "Studio Station hub connection closed.");
            }

            return Task.CompletedTask;
        };

        connection.Reconnecting += error =>
        {
            if (error != null)
            {
                _logger.LogWarning(error, "Reconnecting to Studio Station hub.");
            }

            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Reconnected to Studio Station hub. ConnectionId={ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        return connection;
    }

    private async Task<T?> InvokeAsync<T>(string methodName, object payload, CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken))
        {
            return default;
        }

        try
        {
            return await _connection!.InvokeAsync<T>(methodName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Studio Station hub invocation failed: {MethodName}", methodName);
            return default;
        }
    }

    private string BuildHubUrl()
    {
        return _options.ResolvedStudioHubUrl;
    }
}
