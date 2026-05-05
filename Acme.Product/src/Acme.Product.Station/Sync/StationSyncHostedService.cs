using System.Threading.Channels;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
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
    private readonly StationPackageDeploymentService _packageDeploymentService;
    private readonly StationLogRelayService _logRelayService;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly StationSyncOptions _options;
    private readonly ILogger<StationSyncHostedService> _logger;
    private readonly Channel<StationResultSummaryDto> _resultIngressChannel;
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
    private readonly Action<string> _logHandler;

    private System.Threading.Timer? _snapshotDebounceTimer;
    private RuntimeHostSnapshot? _debouncedSnapshotSource;
    private StationSnapshotDto? _pendingSnapshot;
    private bool _isRegistered;
    private DateTimeOffset _lastHeartbeatAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHealthAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastResultAtUtc;
    private long _nextControlSequenceId;
    private long _droppedResultSummaries;

    public StationSyncHostedService(
        RuntimeHost runtimeHost,
        StationIdentityResolver identityResolver,
        StationSpoolStore spoolStore,
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
        _hubClient = hubClient;
        _packageDeploymentService = packageDeploymentService;
        _logRelayService = logRelayService;
        _settingsStore = settingsStore;
        _options = options.Value;
        _logger = logger;
        _resultIngressChannel = Channel.CreateBounded<StationResultSummaryDto>(
            new BoundedChannelOptions(Math.Max(1, _options.OutboundQueueCapacity))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
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

            if (_resultIngressChannel.Writer.TryWrite(summary))
            {
                SignalSync();
                return;
            }

            var dropped = Interlocked.Increment(ref _droppedResultSummaries);
            if (dropped == 1 || dropped % 100 == 0)
            {
                _logger.LogWarning(
                    "Dropped Station Studio result summary because the outbound queue is full. DroppedResultSummaries={DroppedResultSummaries}",
                    dropped);
            }
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
        try
        {
            await foreach (var summary in _resultIngressChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _spoolStore.Enqueue(summary);
                SignalSync();
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
                _spoolStore.Enqueue(summary);
                drainedAny = true;
            }

            if (drainedAny)
            {
                SignalSync();
            }
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

    private Task ReportCommandAsync(
        StationCommandDto command,
        StationCommandStatus status,
        int progress,
        string message,
        CancellationToken cancellationToken,
        string? errorCode = null,
        string? errorDetail = null)
    {
        var now = DateTimeOffset.UtcNow;
        return _hubClient.ReportCommandResultAsync(new StationCommandResultDto
        {
            CommandId = command.CommandId,
            StationId = command.StationId,
            Status = status,
            ProgressPercent = progress,
            Message = message,
            ErrorCode = errorCode,
            ErrorDetail = errorDetail,
            StartedAtUtc = status is StationCommandStatus.Running or StationCommandStatus.Succeeded or StationCommandStatus.Failed ? now : null,
            CompletedAtUtc = status is StationCommandStatus.Succeeded or StationCommandStatus.Failed or StationCommandStatus.Rejected ? now : null,
            ReportedAtUtc = now,
            CreatedAtUtc = now
        }, cancellationToken);
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
            message.Contains("寮傚父", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("閿欒", StringComparison.OrdinalIgnoreCase))
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
            SpoolPendingCount = spoolStore.PendingCount,
            SpoolBytes = spoolStore.SpoolBytes,
            CameraStatusSummary = "Unknown",
            PlcStatusSummary = "Unknown",
            CurrentPackageId = snapshot.PackageId,
            CurrentPackageHealth = snapshot.PackageId == null ? "NoPackage" : "Loaded",
            CreatedAtUtc = now
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
