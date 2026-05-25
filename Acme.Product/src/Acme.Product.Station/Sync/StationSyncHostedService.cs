using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationSyncHostedService : BackgroundService
{
    private const string ResultBackpressureDiagnosticCode = "StationResultBackpressure";
    private const string ResultSpoolPersistFailedDiagnosticCode = "StationResultSpoolPersistFailed";

    private readonly RuntimeHost _runtimeHost;
    private readonly StationIdentityResolver _identityResolver;
    private readonly StationSpoolStore _spoolStore;
    private readonly StationCommandResultSpoolStore _commandResultSpoolStore;
    private readonly StationHubClient _hubClient;
    private readonly StationPackageDeploymentService _packageDeploymentService;
    private readonly StationLogRelayService _logRelayService;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly StationSyncOptions _options;
    private readonly ILogger<StationSyncHostedService> _logger;
    private readonly Channel<StationResultSummaryDto> _resultIngressChannel;
    private readonly SemaphoreSlim _syncSignal = new(0);
    private readonly object _snapshotGate = new();

    private readonly Action<RuntimeNormalizedResult> _resultHandler;
    private readonly Action<RuntimeHostSnapshot> _snapshotHandler;
    private readonly Action<string> _logHandler;

    private System.Threading.Timer? _snapshotDebounceTimer;
    private RuntimeHostSnapshot? _debouncedSnapshotSource;
    private StationSnapshotDto? _pendingSnapshot;
    private bool _isRegistered;
    private DateTimeOffset _lastHeartbeatAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHealthAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastResultAtUtc;
    private long _nextControlSequenceId;
    private long _queuedResultSummaries;
    private long _resultBackpressureWaits;
    private long _droppedResultSummaries;
    private long _overwrittenSnapshots;

    public StationSyncHostedService(
        RuntimeHost runtimeHost,
        StationIdentityResolver identityResolver,
        StationSpoolStore spoolStore,
        StationCommandResultSpoolStore commandResultSpoolStore,
        StationHubClient hubClient,
        StationPackageDeploymentService packageDeploymentService,
        StationLogRelayService logRelayService,
        StationLocalSettingsStore settingsStore,
        IOptions<StationSyncOptions> options,
        ILogger<StationSyncHostedService> logger)
    {
        _runtimeHost = runtimeHost;
        _identityResolver = identityResolver;
        _spoolStore = spoolStore;
        _commandResultSpoolStore = commandResultSpoolStore;
        _hubClient = hubClient;
        _packageDeploymentService = packageDeploymentService;
        _logRelayService = logRelayService;
        _settingsStore = settingsStore;
        _options = options.Value;
        _logger = logger;
        _resultIngressChannel = Channel.CreateBounded<StationResultSummaryDto>(
            new BoundedChannelOptions(Math.Max(1, _options.OutboundQueueCapacity))
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        _resultHandler = HandleResultAvailable;
        _snapshotHandler = HandleSnapshotChanged;
        _logHandler = HandleRuntimeLogMessage;
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
                var didWork = false;
                if (!await _hubClient.EnsureConnectedAsync(stoppingToken))
                {
                    _isRegistered = false;
                }
                else
                {
                    didWork |= await TryRegisterAsync(stoppingToken);
                    didWork |= await TryPushSnapshotAsync(stoppingToken);
                    didWork |= await TryReportResultGapAsync(stoppingToken);
                    didWork |= await TryPushPendingCommandResultsAsync(stoppingToken);
                    didWork |= await TryPushPendingResultsAsync(stoppingToken);
                    didWork |= await TryPushHealthAsync(stoppingToken);
                    didWork |= await TryPushPendingLogsAsync(stoppingToken);
                    didWork |= await TryPushHeartbeatAsync(stoppingToken);
                    didWork |= await TryPollAndExecuteCommandAsync(stoppingToken);
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

        if (string.IsNullOrWhiteSpace(_options.ResolvedStudioHubUrl))
        {
            _logger.LogWarning("Station sync is enabled but StudioHubUrl is empty.");
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
        _runtimeHost.LogMessage += _logHandler;
    }

    private void UnbindRuntimeEvents()
    {
        _runtimeHost.ResultAvailable -= _resultHandler;
        _runtimeHost.SnapshotChanged -= _snapshotHandler;
        _runtimeHost.LogMessage -= _logHandler;
    }

    private void HandleResultAvailable(RuntimeNormalizedResult result)
    {
        try
        {
            var identity = _identityResolver.GetOrCreate();
            var summary = StationResultMapper.ToSummary(result, identity);
            _lastResultAtUtc = summary.CompletedAtUtc;

            Interlocked.Increment(ref _queuedResultSummaries);
            var accepted = false;
            try
            {
                if (!_resultIngressChannel.Writer.TryWrite(summary))
                {
                    var waits = Interlocked.Increment(ref _resultBackpressureWaits);
                    if (waits == 1 || waits % 100 == 0)
                    {
                        _logger.LogWarning(
                            "Station result sync is applying backpressure instead of dropping result summaries. BackpressureWaits={BackpressureWaits}, OutboundQueueCapacity={OutboundQueueCapacity}",
                            waits,
                            Math.Max(1, _options.OutboundQueueCapacity));
                    }

                    _resultIngressChannel.Writer.WriteAsync(summary).AsTask().GetAwaiter().GetResult();
                }

                accepted = true;
            }
            finally
            {
                if (!accepted)
                {
                    Interlocked.Decrement(ref _queuedResultSummaries);
                }
            }
        }
        catch (Exception ex)
        {
            var dropped = Interlocked.Increment(ref _droppedResultSummaries);
            _logger.LogError(
                ex,
                "Failed to queue Station result summary for local spool persistence. DroppedResultSummaries={DroppedResultSummaries}",
                dropped);
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

    private void HandleRuntimeLogMessage(string message)
    {
        try
        {
            if (_logRelayService.TryEnqueue(DetectLogLevel(message), "RuntimeHost", message))
            {
                SignalSync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue Station log summary.");
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
            lock (_snapshotGate)
            {
                if (_pendingSnapshot != null)
                {
                    var overwritten = Interlocked.Increment(ref _overwrittenSnapshots);
                    if (overwritten == 1 || overwritten % 100 == 0)
                    {
                        _logger.LogInformation(
                            "Coalesced Station runtime snapshot update. OverwrittenSnapshots={OverwrittenSnapshots}",
                            overwritten);
                    }
                }

                _pendingSnapshot = payload;
            }

            SignalSync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush debounced Station snapshot.");
        }
    }

    private async Task PersistSummariesToSpoolAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var summary in _resultIngressChannel.Reader.ReadAllAsync(stoppingToken))
            {
                PersistSummaryToSpool(summary);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            var drainedAny = false;
            while (_resultIngressChannel.Reader.TryRead(out var summary))
            {
                PersistSummaryToSpool(summary);
                drainedAny = true;
            }

            if (drainedAny)
            {
                SignalSync();
            }
        }
    }

    private void PersistSummaryToSpool(StationResultSummaryDto summary)
    {
        try
        {
            _spoolStore.Enqueue(summary);
            SignalSync();
        }
        catch (Exception ex)
        {
            var dropped = Interlocked.Increment(ref _droppedResultSummaries);
            _logger.LogError(
                ex,
                "Failed to persist Station result summary to local spool. RunId={RunId}, DroppedResultSummaries={DroppedResultSummaries}",
                summary.RunId,
                dropped);
        }
        finally
        {
            Interlocked.Decrement(ref _queuedResultSummaries);
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
                StationName = identity.StationName ?? string.Empty,
                LineName = identity.LineName,
                AreaName = identity.AreaName,
                WorkcellName = identity.WorkcellName,
                InspectionNodeName = identity.InspectionNodeName,
                CameraAlias = identity.CameraAlias,
                StationRole = identity.StationRole ?? string.Empty,
                Owner = identity.Owner,
                MachineName = identity.MachineName,
                ProcessId = Environment.ProcessId,
                StationVersion = identity.ClientVersion,
                RuntimeVersion = typeof(RuntimeHost).Assembly.GetName().Version?.ToString() ?? identity.ClientVersion,
                ClientVersion = identity.ClientVersion,
                StartedAtUtc = identity.StartedAtUtc,
                RegisteredAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow
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

    private async Task<bool> TryReportResultGapAsync(CancellationToken stoppingToken)
    {
        var gap = _spoolStore.GetPendingUnavailableRange();
        if (gap.ThroughSequenceId <= 0)
        {
            return false;
        }

        var identity = _identityResolver.GetOrCreate();
        var response = await _hubClient.ReportResultGapAsync(
            new StationResultGapDto
            {
                SchemaVersion = StationSyncContractDefaults.SchemaVersion,
                StationId = identity.StationId,
                DroppedFromSequenceId = gap.FromSequenceId,
                DroppedThroughSequenceId = gap.ThroughSequenceId,
                Reason = "station-spool-trim",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            stoppingToken);
        if (response == null)
        {
            _isRegistered = false;
            return false;
        }

        if (response.LastPersistedSequenceId >= gap.ThroughSequenceId)
        {
            _spoolStore.AcknowledgeUnavailableThrough(gap.ThroughSequenceId);
        }

        ApplyAck(response);
        return true;
    }

    private async Task<bool> TryPushPendingCommandResultsAsync(CancellationToken stoppingToken)
    {
        var batch = _commandResultSpoolStore.GetPendingBatch(Math.Max(1, _options.PendingBatchSize));
        if (batch.Count == 0)
        {
            return false;
        }

        var sentAny = false;
        foreach (var result in batch)
        {
            if (!await _hubClient.ReportCommandResultAsync(result, stoppingToken))
            {
                _isRegistered = false;
                return sentAny;
            }

            _commandResultSpoolStore.Acknowledge(result.CommandId, result.Status);
            sentAny = true;
        }

        return sentAny;
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
        StationSnapshotDto? snapshot;
        lock (_snapshotGate)
        {
            snapshot = _pendingSnapshot;
        }

        if (snapshot == null)
        {
            return false;
        }

        var response = await _hubClient.PushSnapshotAsync(snapshot, stoppingToken);
        if (response == null)
        {
            _isRegistered = false;
            return false;
        }

        ApplyAck(response);
        lock (_snapshotGate)
        {
            if (ReferenceEquals(_pendingSnapshot, snapshot))
            {
                _pendingSnapshot = null;
            }
        }

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
        var heartbeat = BuildHeartbeatDto(identity, _runtimeHost.GetSnapshot());
        heartbeat.SequenceId = Interlocked.Increment(ref _nextControlSequenceId);
        heartbeat.SpoolPendingCount = _spoolStore.PendingCount;
        heartbeat.LastResultAtUtc = _lastResultAtUtc;
        var response = await _hubClient.PushHeartbeatAsync(
            heartbeat,
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

    private async Task<bool> TryPushHealthAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.HealthIntervalSeconds));
        if (DateTimeOffset.UtcNow - _lastHealthAtUtc < interval)
        {
            return false;
        }

        var identity = _identityResolver.GetOrCreate();
        var response = await _hubClient.PushHealthAsync(
            BuildHealthDto(identity, _runtimeHost.GetSnapshot(), _spoolStore),
            stoppingToken);
        if (response == null)
        {
            _isRegistered = false;
            return false;
        }

        ApplyAck(response);
        _lastHealthAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    private async Task<bool> TryPushPendingLogsAsync(CancellationToken stoppingToken)
    {
        var sentAny = false;
        var remaining = Math.Max(1, _options.PendingBatchSize);
        while (remaining-- > 0 && _logRelayService.TryRead(out var log))
        {
            var response = await _hubClient.PushLogAsync(log, stoppingToken);
            if (response == null)
            {
                _isRegistered = false;
                return sentAny;
            }

            sentAny = true;
        }

        return sentAny;
    }

    private async Task<bool> TryPollAndExecuteCommandAsync(CancellationToken stoppingToken)
    {
        var identity = _identityResolver.GetOrCreate();
        var command = await _hubClient.PollCommandAsync(identity.StationId, stoppingToken);
        if (command == null)
        {
            return false;
        }

        await ReportCommandAsync(command, StationCommandStatus.Accepted, 0, "Accepted", stoppingToken);
        await ReportCommandAsync(command, StationCommandStatus.Running, 10, "Running", stoppingToken);

        try
        {
            switch (command.CommandType)
            {
                case StationCommandType.Ping:
                    await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, "Pong", stoppingToken);
                    break;
                case StationCommandType.StopRuntime:
                    await _runtimeHost.StopAsync(stoppingToken);
                    await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, "Runtime stop requested.", stoppingToken);
                    break;
                case StationCommandType.ReloadPackage:
                    await ReloadCurrentPackageAsync(command, stoppingToken);
                    break;
                case StationCommandType.DeployPackage:
                    var message = await _packageDeploymentService.DeployAsync(command.PayloadJson, stoppingToken);
                    await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, message, stoppingToken);
                    break;
                case StationCommandType.CollectLogs:
                    var collectMessage = await CollectLogsAsync(command, stoppingToken);
                    await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, collectMessage, stoppingToken);
                    break;
                default:
                    await ReportCommandAsync(command, StationCommandStatus.Rejected, 0, $"{command.CommandType} is not supported by this Station build.", stoppingToken, "NotSupported");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Station command failed: {CommandId}", command.CommandId);
            _logRelayService.TryEnqueue("ERROR", "StationCommand", $"Command {command.CommandId} failed: {ex.Message}", ex);
            await ReportCommandAsync(command, StationCommandStatus.Failed, 100, ex.Message, stoppingToken, "CommandFailed", ex.ToString());
        }

        return true;
    }

    private async Task<string> CollectLogsAsync(StationCommandDto command, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CollectLogsPayload>(
            string.IsNullOrWhiteSpace(command.PayloadJson) ? "{}" : command.PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CollectLogsPayload();

        var now = DateTimeOffset.UtcNow;
        var maxHours = Math.Clamp(payload.MaxHours ?? _options.MaxCollectLogsHours, 1, Math.Max(1, _options.MaxCollectLogsHours));
        var sinceUtc = payload.SinceUtc ?? now.AddHours(-maxHours);
        var untilUtc = payload.UntilUtc ?? now;
        if (untilUtc < sinceUtc)
        {
            throw new InvalidOperationException("CollectLogs untilUtc must be greater than or equal to sinceUtc.");
        }

        if (untilUtc - sinceUtc > TimeSpan.FromHours(maxHours))
        {
            sinceUtc = untilUtc.AddHours(-maxHours);
        }

        var maxBytes = Math.Clamp(
            payload.MaxBytes ?? _options.MaxCollectLogsMb * 1024L * 1024L,
            1L,
            Math.Max(1, _options.MaxCollectLogsMb) * 1024L * 1024L);
        var logRoot = _options.ResolvedLogDirectory;
        if (string.IsNullOrWhiteSpace(logRoot) || !Directory.Exists(logRoot))
        {
            return "No local log directory is available for collection.";
        }

        var diagnosticsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVisionStation",
            "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);
        var bundlePath = Path.Combine(diagnosticsRoot, $"collectlogs-{command.CommandId}.zip");
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        var includedFiles = 0;
        long includedBytes = 0;
        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(logRoot, "*", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                var writtenUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                if (writtenUtc < sinceUtc || writtenUtc > untilUtc)
                {
                    continue;
                }

                if (includedBytes + info.Length > maxBytes)
                {
                    break;
                }

                archive.CreateEntryFromFile(file, Path.GetRelativePath(logRoot, file), CompressionLevel.Fastest);
                includedFiles++;
                includedBytes += info.Length;
            }
        }

        await Task.CompletedTask;
        return $"Collected {includedFiles} log file(s), {includedBytes} byte(s), bundle={bundlePath}.";
    }

    private async Task ReloadCurrentPackageAsync(StationCommandDto command, CancellationToken cancellationToken)
    {
        var packageRoot = _runtimeHost.LoadedPackage?.RootPath;
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            await ReportCommandAsync(command, StationCommandStatus.Rejected, 0, "No active package is loaded.", cancellationToken, "NoActivePackage");
            return;
        }

        await _runtimeHost.LoadPackageAsync(packageRoot, cancellationToken);
        await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, "Runtime package reloaded.", cancellationToken);
    }

    private async Task ReportCommandAsync(
        StationCommandDto command,
        StationCommandStatus status,
        int progress,
        string message,
        CancellationToken cancellationToken,
        string? errorCode = null,
        string? errorDetail = null)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new StationCommandResultDto
        {
            CommandId = command.CommandId,
            StationId = command.StationId,
            Status = status,
            ProgressPercent = progress,
            Message = message,
            ErrorCode = errorCode,
            ErrorDetail = errorDetail,
            StartedAtUtc = status == StationCommandStatus.Running ? now : null,
            CompletedAtUtc = status is StationCommandStatus.Succeeded or StationCommandStatus.Failed or StationCommandStatus.Rejected ? now : null,
            ReportedAtUtc = now,
            CreatedAtUtc = now
        };

        if (!await _hubClient.ReportCommandResultAsync(payload, cancellationToken))
        {
            _isRegistered = false;
            _commandResultSpoolStore.Enqueue(payload);
            SignalSync();
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

    private void ApplyAck(StationAckDto response)
    {
        if (response.LastPersistedSequenceId > 0)
        {
            _spoolStore.Acknowledge(response.LastPersistedSequenceId);
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

    private static string DetectLogLevel(string message)
    {
        if (message.Contains("fatal", StringComparison.OrdinalIgnoreCase))
        {
            return "FATAL";
        }

        if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("异常", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("错误", StringComparison.OrdinalIgnoreCase))
        {
            return "ERROR";
        }

        if (message.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            return "WARN";
        }

        return "INFO";
    }

    private static StationSnapshotDto BuildSnapshotDto(StationIdentityContext identity, RuntimeHostSnapshot snapshot)
    {
        return new StationSnapshotDto
        {
            SchemaVersion = StationSyncContractDefaults.SchemaVersion,
            StationId = identity.StationId,
            SequenceId = 0,
            MessageId = $"snapshot_{identity.StationId}_{Guid.NewGuid():N}",
            LineName = identity.LineName,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            RuntimeState = StationSyncStateMapper.ToStationRuntimeState(snapshot.State),
            CurrentPackageId = snapshot.PackageId,
            CurrentPackageName = snapshot.PackageName,
            CurrentPackageVersion = identity.CurrentPackageVersion,
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
            SequenceId = 0,
            MessageId = $"heartbeat_{identity.StationId}_{Guid.NewGuid():N}",
            LineName = identity.LineName,
            SentAtUtc = DateTimeOffset.UtcNow,
            RuntimeState = StationSyncStateMapper.ToStationRuntimeState(snapshot.State),
            ConnectionState = "Connected",
            CurrentPackageId = snapshot.PackageId,
            CurrentPackageName = snapshot.PackageName,
            CurrentPackageVersion = identity.CurrentPackageVersion,
            FlowHash = snapshot.FlowHash,
            CurrentRunId = snapshot.CurrentRunId,
            SessionOkCount = snapshot.SessionOkCount,
            SessionNgCount = snapshot.SessionNgCount,
            SessionErrorCount = snapshot.SessionErrorCount,
            StationLocalOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes
        };
    }

    private StationHealthSnapshotDto BuildHealthDto(
        StationIdentityContext identity,
        RuntimeHostSnapshot snapshot,
        StationSpoolStore spoolStore)
    {
        var process = Process.GetCurrentProcess();
        var now = DateTimeOffset.UtcNow;
        var dataDirectory = string.IsNullOrWhiteSpace(_options.ResolvedSpoolDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : _options.ResolvedSpoolDirectory;
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(dataDirectory));
        var driveInfo = string.IsNullOrWhiteSpace(driveRoot) ? null : new DriveInfo(driveRoot);

        var queuedResultSummaries = Volatile.Read(ref _queuedResultSummaries);
        var resultBackpressureWaits = Volatile.Read(ref _resultBackpressureWaits);
        var droppedResultSummaries = Volatile.Read(ref _droppedResultSummaries);
        var spoolPendingCount = spoolStore.PendingCount;
        var spoolBytes = spoolStore.SpoolBytes;
        var resultSyncDiagnostic = BuildResultSyncDiagnostic(
            queuedResultSummaries,
            resultBackpressureWaits,
            droppedResultSummaries,
            spoolPendingCount,
            spoolBytes,
            driveInfo?.AvailableFreeSpace / 1024 / 1024 ?? 0);

        return new StationHealthSnapshotDto
        {
            StationId = identity.StationId,
            SequenceId = _settingsStore.NextHealthSequenceId(),
            MessageId = $"health_{identity.StationId}_{Guid.NewGuid():N}",
            RuntimeState = StationSyncStateMapper.ToStationRuntimeState(snapshot.State),
            ProcessUptimeSeconds = (long)Math.Max(0, (now - identity.StartedAtUtc).TotalSeconds),
            CpuUsagePercent = null,
            WorkingSetMb = process.WorkingSet64 / 1024 / 1024,
            PrivateMemoryMb = process.PrivateMemorySize64 / 1024 / 1024,
            DiskFreeMb = driveInfo?.AvailableFreeSpace / 1024 / 1024 ?? 0,
            DiskTotalMb = driveInfo?.TotalSize / 1024 / 1024 ?? 0,
            SpoolPendingCount = spoolPendingCount,
            SpoolBytes = spoolBytes,
            CameraStatusSummary = "Unknown",
            PlcStatusSummary = BuildPlcStatusSummary(snapshot),
            CurrentPackageId = snapshot.PackageId,
            CurrentPackageHealth = snapshot.PackageId == null ? "NoPackage" : "Loaded",
            LastErrorCode = resultSyncDiagnostic.Code,
            LastErrorMessage = resultSyncDiagnostic.Message,
            CreatedAtUtc = now
        };
    }

    private (string? Code, string? Message) BuildResultSyncDiagnostic(
        long queued,
        long backpressureWaits,
        long dropped,
        int spoolPending,
        long spoolBytes,
        long diskFreeMb)
    {
        if (dropped > 0)
        {
            return (
                ResultSpoolPersistFailedDiagnosticCode,
                BuildResultSyncDiagnosticMessage(queued, backpressureWaits, dropped, spoolPending, spoolBytes, diskFreeMb));
        }

        if (IsResultBackpressured(queued, spoolPending))
        {
            return (
                ResultBackpressureDiagnosticCode,
                BuildResultSyncDiagnosticMessage(queued, backpressureWaits, dropped, spoolPending, spoolBytes, diskFreeMb));
        }

        return (null, null);
    }

    private bool IsResultBackpressured(long queued, int spoolPending)
    {
        if (queued > 0)
        {
            return true;
        }

        var pendingThreshold = Math.Max(
            Math.Max(1, _options.OutboundQueueCapacity),
            Math.Max(1, _options.PendingBatchSize) * 10);
        return spoolPending >= pendingThreshold;
    }

    private string BuildResultSyncDiagnosticMessage(
        long queued,
        long backpressureWaits,
        long dropped,
        int spoolPending,
        long spoolBytes,
        long diskFreeMb)
    {
        return "Station 结果同步出现背压。请检查：Studio 连接、工站到 Studio 的网络、防火墙规则、spool 磁盘空间/权限、StationSync 队列容量。 " +
            $"queued={queued}; outboundQueueCapacity={Math.Max(1, _options.OutboundQueueCapacity)}; backpressureWaits={backpressureWaits}; " +
            $"spoolPending={spoolPending}; spoolBytes={spoolBytes}; diskFreeMb={diskFreeMb}; failedResultSpoolWrites={dropped}";
    }

    private static string BuildPlcStatusSummary(RuntimeHostSnapshot snapshot)
    {
        if (snapshot.PackageId == null)
        {
            return "NotConfigured: no runtime package is loaded.";
        }

        return snapshot.State switch
        {
            RuntimeHostState.Faulted => "Error: runtime host is faulted; PLC operator state may be unavailable.",
            RuntimeHostState.Loaded => "Ready: runtime package is loaded; PLC status is reported by PLC operators when configured.",
            RuntimeHostState.Running or RuntimeHostState.Idle => "Ready: PLC status is reported by PLC operators when configured.",
            _ => $"Disconnected: runtime host state is {snapshot.State}."
        };
    }

    private sealed class CollectLogsPayload
    {
        public DateTimeOffset? SinceUtc { get; set; }

        public DateTimeOffset? UntilUtc { get; set; }

        public long? MaxBytes { get; set; }

        public int? MaxHours { get; set; }
    }
}
