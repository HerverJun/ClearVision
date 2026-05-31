using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Acme.Product.Application.DTOs;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Infrastructure.Operators;
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
    private static readonly HashSet<OperatorType> PlcOperatorTypes =
    [
        OperatorType.ModbusCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.OmronFinsCommunication
    ];

    private readonly RuntimeHost _runtimeHost;
    private readonly StationIdentityResolver _identityResolver;
    private readonly StationSpoolStore _spoolStore;
    private readonly StationCommandResultSpoolStore _commandResultSpoolStore;
    private readonly StationCommandExecutionJournalStore _commandExecutionJournalStore;
    private readonly StationHubClient _hubClient;
    private readonly StationPackageDeploymentService _packageDeploymentService;
    private readonly StationLogRelayService _logRelayService;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly StationSiteProfileStore _siteProfileStore;
    private readonly StationSyncSettingsStore _syncSettingsStore;
    private readonly ICameraManager _cameraManager;
    private readonly StationSyncOptions _options;
    private readonly ILogger<StationSyncHostedService> _logger;
    private readonly Channel<StationResultSummaryDto> _resultIngressChannel;
    private readonly SemaphoreSlim _syncSignal = new(0);
    private readonly object _snapshotGate = new();
    private readonly object _cpuSampleGate = new();

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
    private DateTimeOffset? _lastCpuSampleAtUtc;
    private TimeSpan _lastTotalProcessorTime;
    private double? _lastCpuUsagePercent;
    private string? _lastSyncBlockReason;

    public StationSyncHostedService(
        RuntimeHost runtimeHost,
        StationIdentityResolver identityResolver,
        StationSpoolStore spoolStore,
        StationCommandResultSpoolStore commandResultSpoolStore,
        StationCommandExecutionJournalStore commandExecutionJournalStore,
        StationHubClient hubClient,
        StationPackageDeploymentService packageDeploymentService,
        StationLogRelayService logRelayService,
        StationLocalSettingsStore settingsStore,
        StationSiteProfileStore siteProfileStore,
        StationSyncSettingsStore syncSettingsStore,
        ICameraManager cameraManager,
        IOptions<StationSyncOptions> options,
        ILogger<StationSyncHostedService> logger)
    {
        _runtimeHost = runtimeHost;
        _identityResolver = identityResolver;
        _spoolStore = spoolStore;
        _commandResultSpoolStore = commandResultSpoolStore;
        _commandExecutionJournalStore = commandExecutionJournalStore;
        _hubClient = hubClient;
        _packageDeploymentService = packageDeploymentService;
        _logRelayService = logRelayService;
        _settingsStore = settingsStore;
        _siteProfileStore = siteProfileStore;
        _syncSettingsStore = syncSettingsStore;
        _cameraManager = cameraManager;
        _options = options.Value;
        _logger = logger;
        _resultIngressChannel = Channel.CreateBounded<StationResultSummaryDto>(
            new BoundedChannelOptions(Math.Max(1, _options.OutboundQueueCapacity))
            {
                SingleReader = true,
                SingleWriter = false,
                // The result callback runs on the inspection path. Never wait here; TryWrite failures are
                // reported as dropped Studio-facing telemetry so local inspection stays autonomous.
                FullMode = BoundedChannelFullMode.Wait
            });
        _resultHandler = HandleResultAvailable;
        _snapshotHandler = HandleSnapshotChanged;
        _logHandler = HandleRuntimeLogMessage;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _snapshotDebounceTimer = new System.Threading.Timer(
            static state => ((StationSyncHostedService)state!).FlushDebouncedSnapshot(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _syncSettingsStore.ConnectionSettingsChanged += HandleConnectionSettingsChanged;
        BindRuntimeEvents();
        HandleSnapshotChanged(_runtimeHost.GetSnapshot());

        var spoolTask = PersistSummariesToSpoolAsync(stoppingToken);
        SignalSync();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var didWork = false;
                if (!CanRunSync())
                {
                    _isRegistered = false;
                    await _hubClient.DisconnectAsync(stoppingToken);
                }
                else if (!await _hubClient.EnsureConnectedAsync(stoppingToken))
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
            _syncSettingsStore.ConnectionSettingsChanged -= HandleConnectionSettingsChanged;
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
        string? blockReason = null;
        if (!_options.Enabled)
        {
            blockReason = "Station sync is disabled.";
        }
        else if (string.IsNullOrWhiteSpace(_options.ResolvedStudioHubUrl))
        {
            blockReason = "Station sync is enabled but StudioHubUrl is empty.";
        }
        else if (string.IsNullOrWhiteSpace(_options.SharedToken))
        {
            blockReason = "Station sync is enabled but SharedToken is empty.";
        }

        if (blockReason == null)
        {
            _lastSyncBlockReason = null;
            return true;
        }

        if (!string.Equals(_lastSyncBlockReason, blockReason, StringComparison.Ordinal))
        {
            if (_options.Enabled)
            {
                _logger.LogWarning("{Reason}", blockReason);
            }
            else
            {
                _logger.LogInformation("{Reason}", blockReason);
            }

            _lastSyncBlockReason = blockReason;
        }

        return false;
    }

    private void HandleConnectionSettingsChanged(object? sender, StationSyncConnectionSettings settings)
    {
        _isRegistered = false;
        if (settings.Enabled)
        {
            HandleSnapshotChanged(_runtimeHost.GetSnapshot());
        }

        SignalSync();
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
        if (!_options.Enabled)
        {
            return;
        }

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
                    var dropped = Interlocked.Increment(ref _droppedResultSummaries);
                    if (waits == 1 || waits % 100 == 0)
                    {
                        _logger.LogWarning(
                            "Station result sync queue is full. Dropped Studio-facing result summary to protect local inspection latency. BackpressureWaits={BackpressureWaits}, DroppedResultSummaries={DroppedResultSummaries}, OutboundQueueCapacity={OutboundQueueCapacity}",
                            waits,
                            dropped,
                            Math.Max(1, _options.OutboundQueueCapacity));
                    }

                    return;
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
        if (!_options.Enabled)
        {
            return;
        }

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
        if (!_options.Enabled)
        {
            return;
        }

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

        if (await TryReplayCompletedCommandAsync(command, stoppingToken))
        {
            return true;
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
                case StationCommandType.StartRuntime:
                    await StartRuntimeAsync(command, stoppingToken);
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
                case StationCommandType.ApplySiteProfile:
                    await ApplySiteProfileAsync(command, stoppingToken);
                    break;
                case StationCommandType.CollectLogs:
                    var collectMessage = await CollectLogsAsync(command, stoppingToken);
                    await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, collectMessage, stoppingToken);
                    break;
                default:
                    await ReportCommandAsync(command, StationCommandStatus.Failed, 100, $"{command.CommandType} is not supported by this Station build.", stoppingToken, "NotSupported");
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

    private async Task<bool> TryReplayCompletedCommandAsync(StationCommandDto command, CancellationToken cancellationToken)
    {
        if (!_commandExecutionJournalStore.TryGetTerminalResult(command, out var cachedResult))
        {
            return false;
        }

        _logger.LogInformation(
            "Station command {CommandId} was already completed locally. Replaying cached terminal result {Status}.",
            command.CommandId,
            cachedResult.Status);

        if (cachedResult.Status is StationCommandStatus.Succeeded or StationCommandStatus.Failed)
        {
            await ReportCommandAsync(command, StationCommandStatus.Accepted, 0, "Accepted cached command redelivery.", cancellationToken);
            await ReportCommandAsync(command, StationCommandStatus.Running, 100, "Replaying cached command result.", cancellationToken);
        }

        await ReportCommandAsync(
            command,
            cachedResult.Status,
            Math.Clamp(cachedResult.ProgressPercent <= 0 ? 100 : cachedResult.ProgressPercent, 0, 100),
            BuildCachedCommandReplayMessage(cachedResult),
            cancellationToken,
            cachedResult.ErrorCode,
            cachedResult.ErrorDetail,
            recordTerminal: false);

        return true;
    }

    private async Task StartRuntimeAsync(StationCommandDto command, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<StartRuntimePayload>(
            string.IsNullOrWhiteSpace(command.PayloadJson) ? "{}" : command.PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new StartRuntimePayload();

        if (!string.IsNullOrWhiteSpace(payload.FolderPath))
        {
            await _runtimeHost.StartFolderRunAsync(payload.FolderPath, cancellationToken);
            await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, "Folder runtime started.", cancellationToken);
            return;
        }

        var result = !string.IsNullOrWhiteSpace(payload.ImagePath)
            ? await _runtimeHost.RunSingleAsync(payload.ImagePath, cancellationToken)
            : await _runtimeHost.RunPackageConfiguredSingleAsync(cancellationToken);

        await ReportCommandAsync(
            command,
            result.Outcome is RuntimeRunOutcome.Error or RuntimeRunOutcome.Canceled
                ? StationCommandStatus.Failed
                : StationCommandStatus.Succeeded,
            100,
            $"Runtime completed: {result.Outcome}, runId={result.RunId}, diagnostic={result.DiagnosticCode}.",
            cancellationToken,
            result.Outcome is RuntimeRunOutcome.Error or RuntimeRunOutcome.Canceled ? result.DiagnosticCode : null,
            result.DiagnosticMessage);
    }

    private async Task ApplySiteProfileAsync(StationCommandDto command, CancellationToken cancellationToken)
    {
        var package = _runtimeHost.LoadedPackage;
        if (package == null)
        {
            await ReportCommandAsync(command, StationCommandStatus.Failed, 100, "No active package is loaded.", cancellationToken, "NoActivePackage");
            return;
        }

        var profile = DeserializeSiteProfilePayload(command.PayloadJson);
        var savedProfile = _siteProfileStore.Save(package, profile);
        _runtimeHost.SetActiveSiteProfile(savedProfile);

        await ReportCommandAsync(
            command,
            StationCommandStatus.Succeeded,
            100,
            $"Site profile applied: revision={savedProfile.Revision}, overrides={savedProfile.Overrides.Count}.",
            cancellationToken);
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
            await ReportCommandAsync(command, StationCommandStatus.Failed, 100, "No active package is loaded.", cancellationToken, "NoActivePackage");
            return;
        }

        var package = await _runtimeHost.LoadPackageAsync(packageRoot, cancellationToken);
        var profile = _siteProfileStore.LoadOrCreate(package);
        _runtimeHost.SetActiveSiteProfile(profile);
        await ReportCommandAsync(command, StationCommandStatus.Succeeded, 100, "Runtime package reloaded.", cancellationToken);
    }

    private async Task ReportCommandAsync(
        StationCommandDto command,
        StationCommandStatus status,
        int progress,
        string message,
        CancellationToken cancellationToken,
        string? errorCode = null,
        string? errorDetail = null,
        bool recordTerminal = true)
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

        if (recordTerminal && IsTerminalCommandStatus(status))
        {
            _commandExecutionJournalStore.RecordTerminalResult(command, payload);
        }

        if (!await _hubClient.ReportCommandResultAsync(payload, cancellationToken))
        {
            _isRegistered = false;
            _commandResultSpoolStore.Enqueue(payload);
            SignalSync();
        }
    }

    private static string BuildCachedCommandReplayMessage(StationCommandResultDto cachedResult)
    {
        return string.IsNullOrWhiteSpace(cachedResult.Message)
            ? "Command already completed locally; replaying cached terminal result."
            : $"Command already completed locally; replaying cached terminal result. Original: {cachedResult.Message}";
    }

    private static bool IsTerminalCommandStatus(StationCommandStatus status)
    {
        return status is StationCommandStatus.Succeeded
            or StationCommandStatus.Failed
            or StationCommandStatus.TimedOut
            or StationCommandStatus.Cancelled
            or StationCommandStatus.Rejected;
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

        var gap = spoolStore.GetPendingUnavailableRange();
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
            driveInfo?.AvailableFreeSpace / 1024 / 1024 ?? 0,
            (gap.FromSequenceId, gap.ThroughSequenceId));

        return new StationHealthSnapshotDto
        {
            StationId = identity.StationId,
            SequenceId = _settingsStore.NextHealthSequenceId(),
            MessageId = $"health_{identity.StationId}_{Guid.NewGuid():N}",
            RuntimeState = StationSyncStateMapper.ToStationRuntimeState(snapshot.State),
            ProcessUptimeSeconds = (long)Math.Max(0, (now - identity.StartedAtUtc).TotalSeconds),
            CpuUsagePercent = SampleCpuUsagePercent(process, now),
            WorkingSetMb = process.WorkingSet64 / 1024 / 1024,
            PrivateMemoryMb = process.PrivateMemorySize64 / 1024 / 1024,
            DiskFreeMb = driveInfo?.AvailableFreeSpace / 1024 / 1024 ?? 0,
            DiskTotalMb = driveInfo?.TotalSize / 1024 / 1024 ?? 0,
            SpoolPendingCount = spoolPendingCount,
            SpoolBytes = spoolBytes,
            CameraStatusSummary = BuildCameraStatusSummary(_runtimeHost.LoadedPackage),
            PlcStatusSummary = BuildPlcStatusSummary(_runtimeHost.LoadedPackage, snapshot),
            CurrentPackageId = snapshot.PackageId,
            CurrentPackageHealth = snapshot.PackageId == null ? "NoPackage" : "Loaded",
            LastErrorCode = resultSyncDiagnostic.Code,
            LastErrorMessage = resultSyncDiagnostic.Message,
            CreatedAtUtc = now
        };
    }

    private double? SampleCpuUsagePercent(Process process, DateTimeOffset now)
    {
        var totalProcessorTime = process.TotalProcessorTime;

        lock (_cpuSampleGate)
        {
            if (_lastCpuSampleAtUtc is not { } lastSampleAt)
            {
                _lastCpuSampleAtUtc = now;
                _lastTotalProcessorTime = totalProcessorTime;
                return _lastCpuUsagePercent;
            }

            var elapsedMilliseconds = (now - lastSampleAt).TotalMilliseconds;
            var processorMilliseconds = Math.Max(0d, (totalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds);
            _lastCpuSampleAtUtc = now;
            _lastTotalProcessorTime = totalProcessorTime;

            if (elapsedMilliseconds <= 0)
            {
                return _lastCpuUsagePercent;
            }

            var processorCount = Math.Max(1, Environment.ProcessorCount);
            var cpuUsage = processorMilliseconds / elapsedMilliseconds / processorCount * 100d;
            _lastCpuUsagePercent = Math.Round(Math.Clamp(cpuUsage, 0d, 100d), 2);
            return _lastCpuUsagePercent;
        }
    }

    private string BuildCameraStatusSummary(RuntimePackage? package)
    {
        if (package?.Flow == null)
        {
            return "NotConfigured: no runtime package is loaded.";
        }

        var acquisitionOperators = package.Flow.Operators
            .Where(op => op.IsEnabled && OperatorTypeAliasResolver.Resolve(op.Type) == OperatorType.ImageAcquisition)
            .ToList();
        if (acquisitionOperators.Count == 0)
        {
            return "NotConfigured: current flow does not contain an enabled ImageAcquisition operator.";
        }

        var probes = acquisitionOperators.Select(BuildCameraStatusProbe).ToList();
        var cameraProbes = probes.Where(probe => probe.IsCameraMode).ToList();
        if (cameraProbes.Count == 0)
        {
            return acquisitionOperators.Count == 1
                ? "FileMode: current flow uses image file input."
                : $"FileMode: current flow uses {acquisitionOperators.Count} image file inputs.";
        }

        var disconnected = cameraProbes.Where(probe => !probe.IsConnected).ToList();
        if (disconnected.Count > 0)
        {
            return disconnected.Count == 1
                ? disconnected[0].Summary
                : $"Disconnected: {disconnected.Count} of {cameraProbes.Count} camera bindings unavailable. " +
                  string.Join("; ", disconnected.Take(2).Select(probe => probe.Detail));
        }

        return cameraProbes.Count == 1
            ? cameraProbes[0].Summary
            : $"Connected: {cameraProbes.Count} camera bindings are connected.";
    }

    private CameraStatusProbe BuildCameraStatusProbe(OperatorDto acquisitionOperator)
    {
        var bindingId = GetParameterString(acquisitionOperator, "CameraId", string.Empty);
        var sourceType = NormalizeOptionValue(GetParameterString(acquisitionOperator, "SourceType", string.Empty), string.Empty);
        var isCameraMode = sourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(sourceType) && !string.IsNullOrWhiteSpace(bindingId));
        if (!isCameraMode)
        {
            return new CameraStatusProbe(false, true, "FileMode: current flow uses image file input.", acquisitionOperator.Name);
        }

        if (string.IsNullOrWhiteSpace(bindingId))
        {
            return new CameraStatusProbe(
                true,
                false,
                "Disconnected: camera mode is selected but CameraId is not configured.",
                $"{acquisitionOperator.Name}: CameraId is empty");
        }

        var binding = _cameraManager.FindBinding(bindingId);
        if (binding == null)
        {
            return new CameraStatusProbe(
                true,
                false,
                $"Disconnected: camera binding '{bindingId}' is not configured.",
                $"{acquisitionOperator.Name}: binding '{bindingId}' is not configured");
        }

        var displayName = DescribeCameraBinding(binding);
        if (!binding.IsEnabled)
        {
            return new CameraStatusProbe(
                true,
                false,
                $"Disconnected: camera binding {displayName} is disabled.",
                $"{acquisitionOperator.Name}: {displayName} disabled");
        }

        var camera = !string.IsNullOrWhiteSpace(binding.SerialNumber)
            ? _cameraManager.GetCamera(binding.SerialNumber)
            : null;
        camera ??= _cameraManager.GetCamera(binding.Id);

        if (camera?.IsConnected == true)
        {
            return new CameraStatusProbe(
                true,
                true,
                $"Connected: {displayName}.",
                $"{acquisitionOperator.Name}: {displayName}");
        }

        return new CameraStatusProbe(
            true,
            false,
            $"Disconnected: {displayName}.",
            $"{acquisitionOperator.Name}: {displayName}");
    }

    private static string DescribeCameraBinding(CameraBindingConfig binding)
    {
        var displayName = string.IsNullOrWhiteSpace(binding.DisplayName)
            ? binding.Id
            : binding.DisplayName.Trim();
        var id = string.IsNullOrWhiteSpace(binding.Id) ? "unbound" : binding.Id.Trim();
        var serial = binding.SerialNumber?.Trim();

        return string.IsNullOrWhiteSpace(serial)
            ? $"{displayName} [{id}]"
            : $"{displayName} [{id}/{serial}]";
    }

    private static string GetParameterString(OperatorDto op, string parameterName, string fallback)
    {
        var parameter = op.Parameters.FirstOrDefault(item => string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        var value = parameter?.Value ?? parameter?.DefaultValue;
        var text = value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string NormalizeOptionValue(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var separatorIndex = normalized.IndexOf('|', StringComparison.Ordinal);
        return separatorIndex >= 0
            ? normalized[..separatorIndex].Trim()
            : normalized;
    }

    private (string? Code, string? Message) BuildResultSyncDiagnostic(
        long queued,
        long backpressureWaits,
        long dropped,
        int spoolPending,
        long spoolBytes,
        long diskFreeMb,
        (long From, long Through) spoolTrimmingRange)
    {
        if (dropped > 0 || spoolTrimmingRange.From > 0)
        {
            return (
                ResultSpoolPersistFailedDiagnosticCode,
                BuildResultSyncDiagnosticMessage(queued, backpressureWaits, dropped, spoolPending, spoolBytes, diskFreeMb, spoolTrimmingRange));
        }

        if (IsResultBackpressured(queued, spoolPending))
        {
            return (
                ResultBackpressureDiagnosticCode,
                BuildResultSyncDiagnosticMessage(queued, backpressureWaits, dropped, spoolPending, spoolBytes, diskFreeMb, spoolTrimmingRange));
        }

        return (null, null);
    }

    private sealed record CameraStatusProbe(bool IsCameraMode, bool IsConnected, string Summary, string Detail);

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
        long diskFreeMb,
        (long From, long Through) spoolTrimmingRange)
    {
        var trimmingText = spoolTrimmingRange.From > 0
            ? $"spoolTrimmingRange={spoolTrimmingRange.From}-{spoolTrimmingRange.Through}; "
            : "";
        return "Station 结果同步出现背压。请检查：Studio 连接、工站到 Studio 的网络、防火墙规则、spool 磁盘空间/权限、StationSync 队列容量。 " +
            $"queued={queued}; outboundQueueCapacity={Math.Max(1, _options.OutboundQueueCapacity)}; backpressureWaits={backpressureWaits}; " +
            $"spoolPending={spoolPending}; spoolBytes={spoolBytes}; diskFreeMb={diskFreeMb}; {trimmingText}failedResultSpoolWrites={dropped}";
    }

    private static string BuildPlcStatusSummary(RuntimePackage? package, RuntimeHostSnapshot snapshot)
    {
        return BuildPlcStatusSummaryCore(
            package,
            snapshot,
            PlcCommunicationOperatorBase.GetConnectionStateSnapshot(),
            ModbusCommunicationOperator.GetConnectionStateSnapshot());
    }

    private static string BuildPlcStatusSummaryCore(
        RuntimePackage? package,
        RuntimeHostSnapshot snapshot,
        IReadOnlyDictionary<string, bool> industrialConnectionStates,
        IReadOnlyDictionary<string, bool> modbusConnectionStates)
    {
        if (package?.Flow == null || snapshot.PackageId == null)
        {
            return "NotConfigured: no runtime package is loaded.";
        }

        if (snapshot.State == RuntimeHostState.Faulted)
        {
            return "Error: runtime host is faulted; PLC operator state may be unavailable.";
        }

        if (snapshot.State is not (RuntimeHostState.Loaded or RuntimeHostState.Running or RuntimeHostState.Idle))
        {
            return $"Disconnected: runtime host state is {snapshot.State}.";
        }

        var plcOperators = package.Flow.Operators
            .Where(op => op.IsEnabled && PlcOperatorTypes.Contains(OperatorTypeAliasResolver.Resolve(op.Type)))
            .ToList();
        if (plcOperators.Count == 0)
        {
            return "NotConfigured: current flow does not contain an enabled PLC communication operator.";
        }

        var probes = plcOperators
            .Select(op => BuildPlcStatusProbe(op, industrialConnectionStates, modbusConnectionStates))
            .ToList();
        var connectedCount = probes.Count(probe => probe.HasRuntimeState && probe.IsConnected);
        var disconnected = probes.Where(probe => probe.HasRuntimeState && !probe.IsConnected).ToList();
        var pending = probes.Where(probe => !probe.HasRuntimeState).ToList();

        if (disconnected.Count > 0)
        {
            return $"Disconnected: PLC online {connectedCount} / disconnected {disconnected.Count} / pending {pending.Count}. " +
                string.Join("; ", disconnected.Take(2).Select(probe => probe.Detail));
        }

        if (connectedCount == 0)
        {
            return $"Pending: {plcOperators.Count} PLC operator(s) configured; no runtime connection has been opened.";
        }

        if (pending.Count > 0)
        {
            return $"Ready: PLC online {connectedCount} / pending {pending.Count}. " +
                string.Join("; ", pending.Take(2).Select(probe => probe.Detail));
        }

        return $"Connected: PLC online {connectedCount} / total {probes.Count}.";
    }

    private static PlcStatusProbe BuildPlcStatusProbe(
        OperatorDto plcOperator,
        IReadOnlyDictionary<string, bool> industrialConnectionStates,
        IReadOnlyDictionary<string, bool> modbusConnectionStates)
    {
        var type = OperatorTypeAliasResolver.Resolve(plcOperator.Type);
        var operatorName = string.IsNullOrWhiteSpace(plcOperator.Name)
            ? type.ToString()
            : plcOperator.Name.Trim();
        var connectionKey = BuildPlcConnectionKey(plcOperator, type);
        if (string.IsNullOrWhiteSpace(connectionKey))
        {
            return new PlcStatusProbe(
                false,
                false,
                $"{operatorName}: PLC connection parameters are incomplete.");
        }

        var states = type == OperatorType.ModbusCommunication
            ? modbusConnectionStates
            : industrialConnectionStates;
        if (!states.TryGetValue(connectionKey, out var isConnected))
        {
            return new PlcStatusProbe(
                false,
                false,
                $"{operatorName}: {GetPlcProtocolLabel(type)} {connectionKey} not opened");
        }

        return new PlcStatusProbe(
            true,
            isConnected,
            $"{operatorName}: {GetPlcProtocolLabel(type)} {connectionKey}");
    }

    private static string? BuildPlcConnectionKey(OperatorDto plcOperator, OperatorType type)
    {
        var ipAddress = GetParameterString(plcOperator, "IpAddress", string.Empty);
        var port = GetParameterInt(plcOperator, "Port", GetDefaultPlcPort(type));
        if (string.IsNullOrWhiteSpace(ipAddress) || port <= 0)
        {
            return null;
        }

        return type switch
        {
            OperatorType.SiemensS7Communication => BuildS7ConnectionKey(plcOperator, ipAddress, port),
            OperatorType.MitsubishiMcCommunication => $"MC:{ipAddress}:{port}",
            OperatorType.OmronFinsCommunication => $"FINS:{ipAddress}:{port}",
            OperatorType.ModbusCommunication => $"{ipAddress}:{port}",
            _ => null
        };
    }

    private static string BuildS7ConnectionKey(OperatorDto plcOperator, string ipAddress, int port)
    {
        var cpuType = NormalizeS7CpuType(GetParameterString(plcOperator, "CpuType", "S71200"));
        var rack = GetParameterInt(plcOperator, "Rack", 0);
        var slot = GetParameterInt(plcOperator, "Slot", 1);
        return $"S7:{ipAddress}:{port}:{cpuType}:{rack}:{slot}";
    }

    private static string NormalizeS7CpuType(string? value)
    {
        var normalized = NormalizeOptionValue(value, "S71200").ToUpperInvariant();
        return normalized switch
        {
            "S7200" => "S7200",
            "S7200SMART" => "S7200Smart",
            "S7300" => "S7300",
            "S7400" => "S7400",
            "S71500" => "S71500",
            _ => "S71200"
        };
    }

    private static int GetDefaultPlcPort(OperatorType type)
    {
        return type switch
        {
            OperatorType.SiemensS7Communication => 102,
            OperatorType.MitsubishiMcCommunication => 5002,
            OperatorType.OmronFinsCommunication => 9600,
            OperatorType.ModbusCommunication => 502,
            _ => 0
        };
    }

    private static string GetPlcProtocolLabel(OperatorType type)
    {
        return type switch
        {
            OperatorType.SiemensS7Communication => "S7",
            OperatorType.MitsubishiMcCommunication => "MC",
            OperatorType.OmronFinsCommunication => "FINS",
            OperatorType.ModbusCommunication => "Modbus",
            _ => type.ToString()
        };
    }

    private static int GetParameterInt(OperatorDto op, string parameterName, int fallback)
    {
        var parameter = op.Parameters.FirstOrDefault(item => string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        var value = parameter?.Value ?? parameter?.DefaultValue;
        if (value == null)
        {
            return fallback;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonNumber))
            {
                return jsonNumber;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), out var jsonTextNumber))
            {
                return jsonTextNumber;
            }
        }

        return int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : fallback;
    }

    private sealed record PlcStatusProbe(bool HasRuntimeState, bool IsConnected, string Detail);

    private static RuntimeSiteProfile DeserializeSiteProfilePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidOperationException("ApplySiteProfile payload is empty.");
        }

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        foreach (var propertyName in new[] { "profile", "siteProfile" })
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out var profileElement))
            {
                return profileElement.Deserialize<RuntimeSiteProfile>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("ApplySiteProfile profile is invalid.");
            }
        }

        return root.Deserialize<RuntimeSiteProfile>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("ApplySiteProfile payload is invalid.");
    }

    private sealed class StartRuntimePayload
    {
        public string? ImagePath { get; set; }

        public string? FolderPath { get; set; }
    }

    private sealed class CollectLogsPayload
    {
        public DateTimeOffset? SinceUtc { get; set; }

        public DateTimeOffset? UntilUtc { get; set; }

        public long? MaxBytes { get; set; }

        public int? MaxHours { get; set; }
    }
}
