using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Station.Sync;

public sealed class StationHubClient : IAsyncDisposable
{
    private readonly StationSyncOptions _options;
    private readonly ILogger<StationHubClient> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private HubConnection? _connection;
    private ConnectionSignature? _connectionSignature;
    private long _connectionEpoch;
    private bool _disposed;

    public StationHubClient(
        IOptions<StationSyncOptions> options,
        ILogger<StationHubClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public string CurrentHubUrl => _connectionSignature?.HubUrl ?? _options.ResolvedStudioHubUrl;

    public string LastErrorMessage { get; private set; } = string.Empty;

    public DateTimeOffset? LastConnectedAtUtc { get; private set; }

    public string ConnectionState => _connection?.State.ToString() ?? "Disconnected";

    public long ConnectionEpoch => Interlocked.Read(ref _connectionEpoch);

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            await _connectionGate.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            if (_disposed)
            {
                return false;
            }

            var desiredSignature = BuildConnectionSignature();
            if (desiredSignature == null)
            {
                await DisposeConnectionCoreAsync();
                return false;
            }

            if (_connection != null && !desiredSignature.Equals(_connectionSignature))
            {
                _logger.LogInformation(
                    "Station sync connection settings changed. Reconnecting Studio Station hub from {OldHubUrl} to {NewHubUrl}.",
                    _connectionSignature?.HubUrl ?? "none",
                    desiredSignature.HubUrl);
                await DisposeConnectionCoreAsync();
            }

            _connection ??= BuildConnection(desiredSignature);
            _connectionSignature ??= desiredSignature;
            if (_connection.State == HubConnectionState.Connected ||
                _connection.State == HubConnectionState.Connecting ||
                _connection.State == HubConnectionState.Reconnecting)
            {
                return _connection.State == HubConnectionState.Connected;
            }

            await _connection.StartAsync(cancellationToken);
            BumpConnectionEpoch();
            LastConnectedAtUtc = DateTimeOffset.UtcNow;
            LastErrorMessage = string.Empty;
            _logger.LogInformation("Connected to Studio Station hub at {HubUrl}", desiredSignature.HubUrl);
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Failed to connect to Studio Station hub.");
            return false;
        }
        finally
        {
            try
            {
                _connectionGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public Task<StationReplayCursorDto?> RegisterStationAsync(
        StationRegistrationDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>(StationHubMethods.RegisterStationAsync, payload, cancellationToken);
    }

    public Task<StationReplayCursorDto?> PushHeartbeatAsync(
        StationHeartbeatDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>(StationHubMethods.PushHeartbeatAsync, payload, cancellationToken);
    }

    public Task<StationReplayCursorDto?> PushSnapshotAsync(
        StationSnapshotDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>(StationHubMethods.PushSnapshotAsync, payload, cancellationToken);
    }

    public Task<StationReplayCursorDto?> PushResultSummaryAsync(
        StationResultSummaryDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationReplayCursorDto>(StationHubMethods.PushResultSummaryAsync, payload, cancellationToken);
    }

    public Task<StationAckDto?> ReportResultGapAsync(
        StationResultGapDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationAckDto>(StationHubMethods.ReportResultGap, payload, cancellationToken);
    }

    public Task<StationAckDto?> PushHealthAsync(
        StationHealthSnapshotDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationAckDto>(StationHubMethods.PushHealth, payload, cancellationToken);
    }

    public Task<StationAckDto?> PushLogAsync(
        StationLogSummaryDto payload,
        CancellationToken cancellationToken)
    {
        return InvokeAsync<StationAckDto>(StationHubMethods.PushLog, payload, cancellationToken);
    }

    public Task<StationCommandDto?> PollCommandAsync(string stationId, CancellationToken cancellationToken)
    {
        return InvokeAsync<StationCommandDto?>(StationHubMethods.PollCommand, stationId, cancellationToken);
    }

    public async Task<bool> ReportCommandResultAsync(StationCommandResultDto payload, CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            await _connection!.InvokeAsync(StationHubMethods.ReportCommandResult, payload, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            await HandleInvocationFailureAsync(StationHubMethods.ReportCommandResult, ex);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true; // 1. 先置为已销毁，防止任何新的 EnsureConnectedAsync / DisconnectAsync 进入

        try
        {
            // 2. 等待并独占锁。一旦 Wait 成功，代表此前所有并发线程已全部安全退出。
            await _connectionGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            // 3. 安全销毁物理连接
            await DisposeConnectionCoreAsync();
        }
        finally
        {
            // 4. 安全 Dispose 信号量。由于此时没有任何其他线程在等待或并发占有该锁，这里绝对安全！
            _connectionGate.Dispose();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _connectionGate.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }
            await DisposeConnectionCoreAsync();
        }
        finally
        {
            try
            {
                _connectionGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task DisposeConnectionCoreAsync()
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

        _connection = null;
        _connectionSignature = null;
    }

    private HubConnection BuildConnection(ConnectionSignature signature)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                signature.HubUrl,
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    options.AccessTokenProvider = () => Task.FromResult<string?>(signature.SharedToken);
                    options.Headers[StationSyncContractDefaults.StationTokenHeaderName] = signature.SharedToken;
                    options.Headers["X-Station-Token"] = signature.SharedToken;
                })
            .WithAutomaticReconnect()
            .Build();

        connection.Closed += error =>
        {
            if (error != null)
            {
                LastErrorMessage = error.Message;
                _logger.LogWarning(error, "Studio Station hub connection closed.");
            }

            BumpConnectionEpoch();
            return Task.CompletedTask;
        };

        connection.Reconnecting += error =>
        {
            if (error != null)
            {
                LastErrorMessage = error.Message;
                _logger.LogWarning(error, "Reconnecting to Studio Station hub.");
            }

            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            BumpConnectionEpoch();
            LastConnectedAtUtc = DateTimeOffset.UtcNow;
            LastErrorMessage = string.Empty;
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
            await HandleInvocationFailureAsync(methodName, ex);
            return default;
        }
    }

    private async Task HandleInvocationFailureAsync(
        string methodName,
        Exception ex)
    {
        LastErrorMessage = ex.Message;
        _logger.LogWarning(ex, "Studio Station hub invocation failed: {MethodName}", methodName);

        if (IsRegistrationOrAuthorizationFailure(ex))
        {
            await DisconnectAsync(CancellationToken.None);
        }
    }

    private static bool IsRegistrationOrAuthorizationFailure(Exception ex)
    {
        return ex.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("not authorized", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("ingress", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase);
    }

    private void BumpConnectionEpoch()
    {
        Interlocked.Increment(ref _connectionEpoch);
    }

    private ConnectionSignature? BuildConnectionSignature()
    {
        if (!_options.Enabled)
        {
            LastErrorMessage = "Station sync is disabled.";
            return null;
        }

        var hubUrl = _options.ResolvedStudioHubUrl;
        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            LastErrorMessage = "Station sync is enabled but StudioHubUrl is empty.";
            return null;
        }

        var sharedToken = _options.SharedToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sharedToken))
        {
            LastErrorMessage = "Station sync is enabled but SharedToken is empty.";
            return null;
        }

        return new ConnectionSignature(hubUrl.Trim(), sharedToken.Trim());
    }

    private sealed record ConnectionSignature(string HubUrl, string SharedToken);
}
