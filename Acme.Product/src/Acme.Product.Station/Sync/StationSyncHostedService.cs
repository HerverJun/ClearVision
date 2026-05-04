using System.Threading.Channels;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationSyncHostedService : BackgroundService
{
    private readonly RuntimeHost _runtimeHost;
    private readonly StationIdentityResolver _identityResolver;
    private readonly StationSpoolStore _spoolStore;
    private readonly StationHubClient _hubClient;
    private readonly StationSyncOptions _options;
    private readonly ILogger<StationSyncHostedService> _logger;
    private readonly Channel<StationResultSummaryDto> _resultIngressChannel = Channel.CreateUnbounded<StationResultSummaryDto>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Channel<StationSnapshotDto> _snapshotChannel = Channel.CreateBounded<StationSnapshotDto>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly SemaphoreSlim _syncSignal = new(0);
    private readonly object _snapshotGate = new();

    private readonly Action<RuntimeNormalizedResult> _resultHandler;
    private readonly Action<RuntimeHostSnapshot> _snapshotHandler;

    private System.Threading.Timer? _snapshotDebounceTimer;
    private RuntimeHostSnapshot? _debouncedSnapshotSource;
    private StationSnapshotDto? _pendingSnapshot;
    private bool _isRegistered;
    private DateTimeOffset _lastHeartbeatAtUtc = DateTimeOffset.MinValue;

    public StationSyncHostedService(
        RuntimeHost runtimeHost,
        StationIdentityResolver identityResolver,
        StationSpoolStore spoolStore,
        StationHubClient hubClient,
        IOptions<StationSyncOptions> options,
        ILogger<StationSyncHostedService> logger)
    {
        _runtimeHost = runtimeHost;
        _identityResolver = identityResolver;
        _spoolStore = spoolStore;
        _hubClient = hubClient;
        _options = options.Value;
        _logger = logger;
        _resultHandler = HandleResultAvailable;
        _snapshotHandler = HandleSnapshotChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CanRunSync())
        {
            return;
        }

        _snapshotDebounceTimer = new System.Threading.Timer(
            static state => ((StationSyncHostedService)state!).FlushDebouncedSnapshot(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        BindRuntimeEvents();
        HandleSnapshotChanged(_runtimeHost.GetSnapshot());

        var spoolTask = PersistSummariesToSpoolAsync(stoppingToken);
        SignalSync();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DrainSnapshotQueue();

                var didWork = false;
                if (!await _hubClient.EnsureConnectedAsync(stoppingToken))
                {
                    _isRegistered = false;
                }
                else
                {
                    didWork |= await TryRegisterAsync(stoppingToken);
                    didWork |= await TryPushSnapshotAsync(stoppingToken);
                    didWork |= await TryPushPendingResultsAsync(stoppingToken);
                    didWork |= await TryPushHeartbeatAsync(stoppingToken);
                }

                if (!didWork)
                {
                    await WaitForNextSignalAsync(stoppingToken);
                }
            }
        }
        finally
        {
            UnbindRuntimeEvents();
            _snapshotDebounceTimer?.Dispose();
            _resultIngressChannel.Writer.TryComplete();

            try
            {
                await spoolTask;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            await _hubClient.DisposeAsync();
            _syncSignal.Dispose();
        }
    }

    private bool CanRunSync()
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Station sync is disabled.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.StudioBaseUrl))
        {
            _logger.LogWarning("Station sync is enabled but StudioBaseUrl is empty.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.SharedToken))
        {
            _logger.LogWarning("Station sync is enabled but SharedToken is empty.");
            return false;
        }

        return true;
    }

    private void BindRuntimeEvents()
    {
        _runtimeHost.ResultAvailable += _resultHandler;
        _runtimeHost.SnapshotChanged += _snapshotHandler;
    }

    private void UnbindRuntimeEvents()
    {
        _runtimeHost.ResultAvailable -= _resultHandler;
        _runtimeHost.SnapshotChanged -= _snapshotHandler;
    }

    private void HandleResultAvailable(RuntimeNormalizedResult result)
    {
        try
        {
            var identity = _identityResolver.GetOrCreate();
            var summary = new StationResultSummaryDto
            {
                SchemaVersion = StationSyncContractDefaults.SchemaVersion,
                StationId = identity.StationId,
                LineName = identity.LineName,
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

            _resultIngressChannel.Writer.TryWrite(summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue Station result summary.");
        }
    }

    private void HandleSnapshotChanged(RuntimeHostSnapshot snapshot)
    {
        try
        {
            lock (_snapshotGate)
            {
                _debouncedSnapshotSource = snapshot;
                _snapshotDebounceTimer?.Change(
                    Math.Max(100, _options.SnapshotDebounceMilliseconds),
                    Timeout.Infinite);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue Station runtime snapshot.");
        }
    }

    private void FlushDebouncedSnapshot()
    {
        try
        {
            RuntimeHostSnapshot? snapshot;
            lock (_snapshotGate)
            {
                snapshot = _debouncedSnapshotSource;
                _debouncedSnapshotSource = null;
            }

            if (snapshot == null)
            {
                return;
            }

            var identity = _identityResolver.GetOrCreate();
            var payload = BuildSnapshotDto(identity, snapshot);
            _snapshotChannel.Writer.TryWrite(payload);
            SignalSync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush debounced Station snapshot.");
        }
    }

    private async Task PersistSummariesToSpoolAsync(CancellationToken stoppingToken)
    {
        await foreach (var summary in _resultIngressChannel.Reader.ReadAllAsync(stoppingToken))
        {
            _spoolStore.Enqueue(summary);
            SignalSync();
        }
    }

    private async Task<bool> TryRegisterAsync(CancellationToken stoppingToken)
    {
        if (_isRegistered)
        {
            return false;
        }

        var identity = _identityResolver.GetOrCreate();
        var response = await _hubClient.RegisterStationAsync(
            new StationRegistrationDto
            {
                SchemaVersion = StationSyncContractDefaults.SchemaVersion,
                StationId = identity.StationId,
                LineName = identity.LineName,
                MachineName = identity.MachineName,
                ClientVersion = identity.ClientVersion,
                StartedAtUtc = identity.StartedAtUtc
            },
            stoppingToken);

        if (response == null)
        {
            return false;
        }

        _isRegistered = true;
        ApplyAck(response);
        return true;
    }

    private async Task<bool> TryPushPendingResultsAsync(CancellationToken stoppingToken)
    {
        var batch = _spoolStore.GetPendingBatch(Math.Max(1, _options.PendingBatchSize));
        if (batch.Count == 0)
        {
            return false;
        }

        var sentAny = false;
        foreach (var summary in batch)
        {
            var response = await _hubClient.PushResultSummaryAsync(summary, stoppingToken);
            if (response == null)
            {
                _isRegistered = false;
                break;
            }

            ApplyAck(response);
            sentAny = true;
        }

        return sentAny;
    }

    private async Task<bool> TryPushSnapshotAsync(CancellationToken stoppingToken)
    {
        if (_pendingSnapshot == null)
        {
            return false;
        }

        var response = await _hubClient.PushSnapshotAsync(_pendingSnapshot, stoppingToken);
        if (response == null)
        {
            _isRegistered = false;
            return false;
        }

        ApplyAck(response);
        _pendingSnapshot = null;
        return true;
    }

    private async Task<bool> TryPushHeartbeatAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatIntervalSeconds));
        if (DateTimeOffset.UtcNow - _lastHeartbeatAtUtc < interval)
        {
            return false;
        }

        var identity = _identityResolver.GetOrCreate();
        var response = await _hubClient.PushHeartbeatAsync(
            BuildHeartbeatDto(identity, _runtimeHost.GetSnapshot()),
            stoppingToken);
        if (response == null)
        {
            _isRegistered = false;
            return false;
        }

        ApplyAck(response);
        _lastHeartbeatAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    private void DrainSnapshotQueue()
    {
        while (_snapshotChannel.Reader.TryRead(out var snapshot))
        {
            _pendingSnapshot = snapshot;
        }
    }

    private async Task WaitForNextSignalAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatIntervalSeconds));
        var delay = _lastHeartbeatAtUtc == DateTimeOffset.MinValue
            ? TimeSpan.FromSeconds(1)
            : _lastHeartbeatAtUtc.Add(interval) - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        try
        {
            await _syncSignal.WaitAsync(delay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void ApplyAck(StationReplayCursorDto response)
    {
        if (response.AckedSequenceId > 0)
        {
            _spoolStore.Acknowledge(response.AckedSequenceId);
        }
    }

    private void SignalSync()
    {
        try
        {
            _syncSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static StationSnapshotDto BuildSnapshotDto(StationIdentityContext identity, RuntimeHostSnapshot snapshot)
    {
        return new StationSnapshotDto
        {
            SchemaVersion = StationSyncContractDefaults.SchemaVersion,
            StationId = identity.StationId,
            LineName = identity.LineName,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            State = snapshot.State,
            PackageId = snapshot.PackageId,
            PackageName = snapshot.PackageName,
            FlowHash = snapshot.FlowHash,
            CurrentRunId = snapshot.CurrentRunId,
            SessionOkCount = snapshot.SessionOkCount,
            SessionNgCount = snapshot.SessionNgCount,
            SessionErrorCount = snapshot.SessionErrorCount
        };
    }

    private static StationHeartbeatDto BuildHeartbeatDto(StationIdentityContext identity, RuntimeHostSnapshot snapshot)
    {
        return new StationHeartbeatDto
        {
            SchemaVersion = StationSyncContractDefaults.SchemaVersion,
            StationId = identity.StationId,
            LineName = identity.LineName,
            SentAtUtc = DateTimeOffset.UtcNow,
            State = snapshot.State,
            PackageId = snapshot.PackageId,
            PackageName = snapshot.PackageName,
            FlowHash = snapshot.FlowHash,
            CurrentRunId = snapshot.CurrentRunId,
            SessionOkCount = snapshot.SessionOkCount,
            SessionNgCount = snapshot.SessionNgCount,
            SessionErrorCount = snapshot.SessionErrorCount
        };
    }
}
