using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Entities.Base;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Runtime;

public sealed class RuntimeHost : IAsyncDisposable
{
    private readonly IFlowExecutionService _flowExecutionService;
    private readonly RuntimePackageLoader _packageLoader;
    private readonly RuntimeResultNormalizer _resultNormalizer;
    private readonly ILogger<RuntimeHost> _logger;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly object _profileGate = new();
    private RuntimeHostState _state = RuntimeHostState.Idle;
    private RuntimePackage? _loadedPackage;
    private RuntimeSiteProfile? _activeSiteProfile;
    private CancellationTokenSource? _activeRunCts;
    private Task? _backgroundRunTask;
    private long _runGenerationCounter;
    private long _activeRunGeneration;
    private bool _stopTimeoutPending;
    private Guid? _currentFlowId;
    private string? _currentRunId;
    private int _sessionOkCount;
    private int _sessionNgCount;
    private int _sessionErrorCount;
    private int _pendingCount;
    private RuntimeResultRecordWriter? _resultWriter;
    private RuntimeImageWriter? _imageWriter;
    private int _disposeStarted;

    public RuntimeHost(
        IFlowExecutionService flowExecutionService,
        RuntimePackageLoader packageLoader,
        RuntimeResultNormalizer resultNormalizer,
        ILogger<RuntimeHost> logger)
    {
        _flowExecutionService = flowExecutionService;
        _packageLoader = packageLoader;
        _resultNormalizer = resultNormalizer;
        _logger = logger;
    }

    public event Action<RuntimeHostSnapshot>? SnapshotChanged;

    public event Action<RuntimeNormalizedResult>? ResultAvailable;

    public event Action<string>? LogMessage;

    public RuntimePackage? LoadedPackage => _loadedPackage;

    public RuntimeSiteProfile? ActiveSiteProfile
    {
        get
        {
            lock (_profileGate)
            {
                return _activeSiteProfile == null
                    ? null
                    : RuntimeParameterOverrideApplier.CloneProfile(_activeSiteProfile);
            }
        }
    }

    public RuntimeHostSnapshot GetSnapshot()
    {
        return new RuntimeHostSnapshot
        {
            State = _state,
            PackageId = _loadedPackage?.Manifest.PackageId,
            PackageName = _loadedPackage?.Manifest.PackageName,
            FlowHash = _loadedPackage?.Manifest.FlowHash,
            CurrentRunId = _currentRunId,
            SessionOkCount = _sessionOkCount,
            SessionNgCount = _sessionNgCount,
            SessionErrorCount = _sessionErrorCount
        };
    }

    public async Task<RuntimePackage> LoadPackageAsync(string packageRoot, CancellationToken cancellationToken = default)
    {
        var package = await _packageLoader.LoadAsync(packageRoot, cancellationToken);

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            EnsureNotRunning();
            _loadedPackage = package;
            lock (_profileGate)
            {
                _activeSiteProfile = RuntimeParameterOverrideApplier.CloneProfile(package.DefaultSiteProfile);
            }

            ResetSessionCounters();
            await RecreateWritersAsync(package.RuntimeProfile);
            _state = RuntimeHostState.Loaded;
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();
        EmitLog($"已加载运行包：{package.Manifest.PackageName}（{package.Manifest.FlowHash}）");
        return package;
    }

    public void SetActiveSiteProfile(RuntimeSiteProfile? profile)
    {
        var package = _loadedPackage;
        if (package == null)
        {
            if (profile != null)
            {
                throw new RuntimePackageException("当前尚未加载运行包。");
            }

            lock (_profileGate)
            {
                _activeSiteProfile = null;
            }

            return;
        }

        var nextProfile = profile ?? package.DefaultSiteProfile;
        RuntimeParameterValidator.ThrowIfInvalid(package.ParameterSchema, nextProfile);

        lock (_profileGate)
        {
            _activeSiteProfile = RuntimeParameterOverrideApplier.CloneProfile(nextProfile);
        }

        EmitLog($"现场参数 Profile 已激活：{nextProfile.ProfileId}，Revision {nextProfile.Revision}。");
    }

    public async Task<RuntimeNormalizedResult> RunSingleAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new RuntimePackageException("Input image path is required.");
        }

        return await RunSingleCoreWithStateAsync(imagePath, cancellationToken);
    }

    public async Task<RuntimeNormalizedResult> RunPackageConfiguredSingleAsync(
        CancellationToken cancellationToken = default)
    {
        return await RunSingleCoreWithStateAsync(null, cancellationToken);
    }

    private async Task<RuntimeNormalizedResult> RunSingleCoreWithStateAsync(
        string? imagePath,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource runCts;
        Task<RuntimeNormalizedResult> backgroundRunTask;
        long runGeneration;

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePackageLoaded();
            EnsureNotRunning();
            runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runGeneration = Interlocked.Increment(ref _runGenerationCounter);
            _activeRunGeneration = runGeneration;
            _stopTimeoutPending = false;
            _activeRunCts = runCts;
            _state = RuntimeHostState.Running;
            backgroundRunTask = ExecuteSingleCoreAsync(imagePath, runCts.Token);
            _backgroundRunTask = backgroundRunTask;
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();

        try
        {
            return await backgroundRunTask;
        }
        finally
        {
            await FinalizeRunAsync(runGeneration, runCts);
        }
    }

    public async Task StartFolderRunAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePackageLoaded();
            EnsureNotRunning();

            var files = EnumerateReplayFiles(folderPath, _loadedPackage!.RuntimeProfile).ToList();
            if (files.Count == 0)
            {
                throw new RuntimePackageException("所选文件夹中没有可运行的图片文件。");
            }

            _pendingCount = files.Count;
            var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var runGeneration = Interlocked.Increment(ref _runGenerationCounter);
            _activeRunGeneration = runGeneration;
            _stopTimeoutPending = false;
            _activeRunCts = runCts;
            _state = RuntimeHostState.Running;
            _backgroundRunTask = Task.Run(
                () => ExecuteFolderReplayAsync(files, runGeneration, runCts),
                CancellationToken.None);
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();
        EmitLog($"已开始批量运行，共 {_pendingCount} 张图片。");
    }

    public async Task<RuntimeStopSummary> StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? activeRunCts;
        Task? backgroundRunTask;
        Guid? currentFlowId;
        RuntimeProfile? profile;
        int pendingCount;
        bool wasRunning;
        long activeRunGeneration;

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            wasRunning = _state is RuntimeHostState.Running or RuntimeHostState.Stopping || HasActiveRun();
            activeRunCts = _activeRunCts;
            backgroundRunTask = _backgroundRunTask;
            activeRunGeneration = _activeRunGeneration;
            currentFlowId = _currentFlowId;
            profile = _loadedPackage?.RuntimeProfile;
            pendingCount = _pendingCount;

            if (!wasRunning || backgroundRunTask == null || activeRunCts == null)
            {
                return new RuntimeStopSummary
                {
                    WasRunning = false,
                    Completed = true,
                    PendingCount = _pendingCount,
                    DroppedCount = GetDroppedCount()
                };
            }

            _state = RuntimeHostState.Stopping;
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();
        EmitLog("已收到停止请求。");

        try
        {
            await activeRunCts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }

        if (currentFlowId.HasValue)
        {
            await _flowExecutionService.CancelExecutionAsync(currentFlowId.Value);
        }

        var timeout = TimeSpan.FromMilliseconds(profile?.StopTimeoutMs ?? 5_000);
        var completedTask = await Task.WhenAny(backgroundRunTask, Task.Delay(timeout));
        var completed = completedTask == backgroundRunTask || backgroundRunTask.IsCompleted;

        if (completed)
        {
            await FinalizeRunAsync(activeRunGeneration, activeRunCts);
        }
        else
        {
            var markTimedOut = false;
            var completedAfterTimeout = false;
            await _stateGate.WaitAsync(CancellationToken.None);
            try
            {
                if (_activeRunGeneration == activeRunGeneration &&
                    ReferenceEquals(_backgroundRunTask, backgroundRunTask))
                {
                    if (backgroundRunTask.IsCompleted)
                    {
                        completedAfterTimeout = true;
                    }
                    else
                    {
                        _state = RuntimeHostState.Faulted;
                        _stopTimeoutPending = true;
                        markTimedOut = true;
                    }
                }
            }
            finally
            {
                _stateGate.Release();
            }

            if (completedAfterTimeout)
            {
                completed = true;
                await FinalizeRunAsync(activeRunGeneration, activeRunCts);
            }
            else if (markTimedOut)
            {
                EmitSnapshot();
                EmitLog("停止等待超时，当前任务未在超时前退出。");
            }
        }

        return new RuntimeStopSummary
        {
            WasRunning = wasRunning,
            Completed = completed,
            TimedOut = !completed,
            PendingCount = pendingCount,
            DroppedCount = GetDroppedCount(),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync();
        }
        catch
        {
        }

        var resultWriter = _resultWriter;
        _resultWriter = null;
        if (resultWriter != null)
        {
            await resultWriter.DisposeAsync();
        }

        var imageWriter = _imageWriter;
        _imageWriter = null;
        if (imageWriter != null)
        {
            await imageWriter.DisposeAsync();
        }

        var activeRunCts = _activeRunCts;
        _activeRunCts = null;
        activeRunCts?.Dispose();
        _stateGate.Dispose();
    }

    private async Task ExecuteFolderReplayAsync(
        IReadOnlyList<string> files,
        long runGeneration,
        CancellationTokenSource runCts)
    {
        var cancellationToken = runCts.Token;

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteSingleCoreAsync(file, cancellationToken);
                Interlocked.Decrement(ref _pendingCount);
                EmitSnapshot();
            }
        }
        catch (OperationCanceledException)
        {
            EmitLog("批量运行已取消。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Folder replay failed.");
            EmitLog($"批量运行失败：{ex.Message}");
            try
            {
                await _stateGate.WaitAsync(CancellationToken.None);
                try
                {
                    if (_activeRunGeneration == runGeneration)
                    {
                        _state = RuntimeHostState.Faulted;
                        _stopTimeoutPending = false;
                    }
                }
                finally
                {
                    _stateGate.Release();
                }

                EmitSnapshot();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        finally
        {
            await FinalizeRunAsync(runGeneration, runCts);
        }
    }

    private async Task<RuntimeNormalizedResult> ExecuteSingleCoreAsync(string? imagePath, CancellationToken cancellationToken)
    {
        var package = _loadedPackage ?? throw new RuntimePackageException("当前尚未加载运行包。");
        byte[]? sourceImageBytes = null;
        if (!string.IsNullOrWhiteSpace(imagePath) && !File.Exists(imagePath))
        {
            throw new RuntimePackageException($"输入图片不存在：{imagePath}");
        }

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            sourceImageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        }

        var imageId = string.IsNullOrWhiteSpace(imagePath)
            ? BuildGeneratedImageId()
            : BuildImageId(imagePath);
        var startedAt = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid().ToString("N");
        _currentRunId = runId;
        EmitSnapshot();

        try
        {
            var profile = GetActiveSiteProfileSnapshot(package);
            var applyResult = RuntimeParameterOverrideApplier.CloneAndApply(package, profile);
            var flow = RuntimeFlowAdapter.ToEntity(applyResult.Flow);
            _currentFlowId = flow.Id;

            var validation = _flowExecutionService.ValidateFlow(flow);
            if (!validation.IsValid)
            {
                var invalidResult = _resultNormalizer.CreateValidationFailure(
                    package,
                    runId,
                    imageId,
                    imagePath,
                    sourceImageBytes,
                    validation,
                    startedAt,
                    DateTimeOffset.UtcNow);
                await PersistResultAsync(invalidResult, cancellationToken);
                return invalidResult;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(package.RuntimeProfile.SingleRunTimeoutMs));

            var flowResult = await _flowExecutionService.ExecuteFlowAsync(
                flow,
                BuildFlowInputData(sourceImageBytes),
                cancellationToken: timeoutCts.Token);

            var normalizedResult = _resultNormalizer.Normalize(
                package,
                runId,
                imageId,
                imagePath,
                sourceImageBytes,
                flowResult,
                startedAt,
                DateTimeOffset.UtcNow,
                timeoutCts.IsCancellationRequested);

            await PersistResultAsync(normalizedResult, cancellationToken);
            return normalizedResult;
        }
        catch (OperationCanceledException)
        {
            var canceled = _resultNormalizer.CreateCanceledResult(
                package,
                runId,
                imageId,
                imagePath,
                sourceImageBytes,
                startedAt,
                DateTimeOffset.UtcNow);
            await PersistResultAsync(canceled, CancellationToken.None);
            return canceled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Single run failed for image {ImagePath}", imagePath);
            var failure = _resultNormalizer.CreateUnhandledFailure(
                package,
                runId,
                imageId,
                imagePath,
                sourceImageBytes,
                ex,
                startedAt,
                DateTimeOffset.UtcNow);
            await PersistResultAsync(failure, CancellationToken.None);
            return failure;
        }
        finally
        {
            _currentFlowId = null;
            _currentRunId = null;
            EmitSnapshot();
        }
    }

    private async Task PersistResultAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken)
    {
        UpdateSessionCounters(result);

        if (_imageWriter != null && _imageWriter.ShouldPersist(result))
        {
            result.SavedImagePath = _imageWriter.PlanPath(result);
            if (!await _imageWriter.EnqueueAsync(result, cancellationToken))
            {
                result.SavedImagePath = null;
            }
        }

        if (_resultWriter != null)
        {
            await _resultWriter.EnqueueAsync(result, cancellationToken);
        }

        ResultAvailable?.Invoke(result);
        EmitLog($"{result.ImageId}: {FormatOutcome(result.Outcome)}（{result.ExecutionTimeMs} ms）");
        EmitSnapshot();
        await Task.CompletedTask;
    }

    private void ResetSessionCounters()
    {
        _sessionOkCount = 0;
        _sessionNgCount = 0;
        _sessionErrorCount = 0;
        _pendingCount = 0;
    }

    private void UpdateSessionCounters(RuntimeNormalizedResult result)
    {
        switch (result.Outcome)
        {
            case RuntimeRunOutcome.Ok:
                _sessionOkCount += 1;
                break;
            case RuntimeRunOutcome.Ng:
                _sessionNgCount += 1;
                break;
            default:
                _sessionErrorCount += 1;
                break;
        }
    }

    private int GetDroppedCount()
    {
        return (_resultWriter?.DroppedCount ?? 0) + (_imageWriter?.DroppedCount ?? 0);
    }

    private async Task FinalizeRunAsync(long expectedGeneration, CancellationTokenSource? runCts)
    {
        var emitSnapshot = false;

        if (Interlocked.CompareExchange(ref _disposeStarted, 0, 0) != 0)
        {
            runCts?.Dispose();
            return;
        }

        try
        {
            await _stateGate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            runCts?.Dispose();
            return;
        }

        try
        {
            if (_activeRunGeneration == expectedGeneration)
            {
                _activeRunCts = null;
                _backgroundRunTask = null;
                _activeRunGeneration = 0;
                _currentFlowId = null;
                _currentRunId = null;
                _pendingCount = 0;

                if (_state != RuntimeHostState.Faulted || _stopTimeoutPending)
                {
                    _state = _loadedPackage == null ? RuntimeHostState.Idle : RuntimeHostState.Loaded;
                }

                _stopTimeoutPending = false;
                emitSnapshot = true;
            }
        }
        finally
        {
            _stateGate.Release();
            runCts?.Dispose();
        }

        if (emitSnapshot)
        {
            EmitSnapshot();
        }
    }

    private async Task RecreateWritersAsync(RuntimeProfile profile)
    {
        var resultWriter = _resultWriter;
        _resultWriter = null;
        if (resultWriter != null)
        {
            await resultWriter.DisposeAsync();
        }

        var imageWriter = _imageWriter;
        _imageWriter = null;
        if (imageWriter != null)
        {
            await imageWriter.DisposeAsync();
        }

        var dataRoot = RuntimePathGuard.GetDefaultStationDataRoot();
        Directory.CreateDirectory(dataRoot);
        _resultWriter = new RuntimeResultRecordWriter(dataRoot, profile.ResultRecordQueueCapacity, _logger);
        _imageWriter = new RuntimeImageWriter(dataRoot, profile, _logger);
    }

    private static string BuildImageId(string imagePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(imagePath);
        return RuntimePathGuard.SanitizeFileName(fileName, Guid.NewGuid().ToString("N"));
    }

    private static string BuildGeneratedImageId()
    {
        return $"package-configured-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    }

    private static Dictionary<string, object>? BuildFlowInputData(byte[]? sourceImageBytes)
    {
        if (sourceImageBytes is not { Length: > 0 })
        {
            return null;
        }

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Image"] = sourceImageBytes
        };
    }

    private static IEnumerable<string> EnumerateReplayFiles(string folderPath, RuntimeProfile profile)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new RuntimePackageException($"批量运行文件夹不存在：{folderPath}");
        }

        var allowedExtensions = new HashSet<string>(profile.SupportedInputExtensions, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folderPath)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(profile.DirectoryReplayMaxFileCount);
    }

    private void EnsurePackageLoaded()
    {
        if (_loadedPackage == null)
        {
            throw new RuntimePackageException("当前尚未加载运行包。");
        }
    }

    private void EnsureNotRunning()
    {
        if (_state is RuntimeHostState.Running or RuntimeHostState.Stopping || HasActiveRun())
        {
            throw new RuntimePackageException("运行引擎当前正忙，请先等待当前任务结束。");
        }
    }

    private bool HasActiveRun()
    {
        return _backgroundRunTask is { IsCompleted: false };
    }

    private void EmitSnapshot()
    {
        SnapshotChanged?.Invoke(GetSnapshot());
    }

    private void EmitLog(string message)
    {
        LogMessage?.Invoke(message);
        _logger.LogInformation("{Message}", message);
    }

    private RuntimeSiteProfile GetActiveSiteProfileSnapshot(RuntimePackage package)
    {
        lock (_profileGate)
        {
            return RuntimeParameterOverrideApplier.CloneProfile(_activeSiteProfile ?? package.DefaultSiteProfile);
        }
    }

    private static string FormatOutcome(RuntimeRunOutcome outcome)
    {
        return outcome switch
        {
            RuntimeRunOutcome.Ok => "OK",
            RuntimeRunOutcome.Ng => "NG",
            RuntimeRunOutcome.Error => "异常",
            RuntimeRunOutcome.Canceled => "已取消",
            _ => outcome.ToString()
        };
    }
}

internal static class RuntimeFlowAdapter
{
    public static OperatorFlow ToEntity(OperatorFlowDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var flow = dto.ToEntity();
        if (dto.Id != Guid.Empty)
        {
            typeof(Entity)
                .GetProperty(nameof(Entity.Id))?
                .SetValue(flow, dto.Id);
        }

        return flow;
    }
}

public sealed class RuntimeResultNormalizer
{
    public RuntimeNormalizedResult Normalize(
        RuntimePackage package,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        FlowExecutionResult flowResult,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        bool cancellationRequested)
    {
        var outputData = flowResult.OutputData ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var evaluation = flowResult.IsSuccess
            ? InspectionJudgmentResolver.DetermineStatusFromFlowOutput(outputData)
            : new InspectionJudgmentEvaluation(
                InspectionStatus.Error,
                "FlowExecution",
                string.IsNullOrWhiteSpace(flowResult.ErrorMessage)
                    ? "ExecutionFailed"
                    : flowResult.ErrorMessage,
                false);

        var outcome = ResolveOutcome(flowResult, evaluation, cancellationRequested);
        var inspectionStatus = outcome == RuntimeRunOutcome.Canceled
            ? null
            : (InspectionStatus?)evaluation.Status;
        var diagnosticCode = ResolveDiagnosticCode(outcome, evaluation, flowResult);
        var diagnosticMessage = ResolveDiagnosticMessage(outcome, evaluation, flowResult);

        return new RuntimeNormalizedResult
        {
            RunId = runId,
            PackageId = package.Manifest.PackageId,
            PackageName = package.Manifest.PackageName,
            FlowHash = package.Manifest.FlowHash,
            ImageId = imageId,
            SourceImagePath = imagePath,
            Outcome = outcome,
            InspectionStatus = inspectionStatus,
            ExecutionTimeMs = flowResult.ExecutionTimeMs,
            DiagnosticCode = diagnosticCode,
            DiagnosticMessage = diagnosticMessage,
            HasJudgmentSignal = !evaluation.MissingJudgmentSignal,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PrimaryOutputs = BuildPrimaryOutputs(outputData),
            OutputImageBytes = TryExtractOutputImage(outputData) ?? sourceImageBytes,
            SourceImageBytes = sourceImageBytes
        };
    }

    public RuntimeNormalizedResult CreateValidationFailure(
        RuntimePackage package,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        FlowValidationResult validation,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return CreateFixedResult(
            package,
            runId,
            imageId,
            imagePath,
            sourceImageBytes,
            RuntimeRunOutcome.Error,
            InspectionStatus.Error,
            "FlowInvalid",
            string.Join("; ", validation.Errors),
            startedAtUtc,
            completedAtUtc);
    }

    public RuntimeNormalizedResult CreateCanceledResult(
        RuntimePackage package,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return CreateFixedResult(
            package,
            runId,
            imageId,
            imagePath,
            sourceImageBytes,
            RuntimeRunOutcome.Canceled,
            null,
            "Canceled",
            "Run canceled.",
            startedAtUtc,
            completedAtUtc);
    }

    public RuntimeNormalizedResult CreateUnhandledFailure(
        RuntimePackage package,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        Exception exception,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return CreateFixedResult(
            package,
            runId,
            imageId,
            imagePath,
            sourceImageBytes,
            RuntimeRunOutcome.Error,
            InspectionStatus.Error,
            "ExecutionFailed",
            exception.Message,
            startedAtUtc,
            completedAtUtc);
    }

    private static RuntimeNormalizedResult CreateFixedResult(
        RuntimePackage package,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        RuntimeRunOutcome outcome,
        InspectionStatus? inspectionStatus,
        string diagnosticCode,
        string diagnosticMessage,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return new RuntimeNormalizedResult
        {
            RunId = runId,
            PackageId = package.Manifest.PackageId,
            PackageName = package.Manifest.PackageName,
            FlowHash = package.Manifest.FlowHash,
            ImageId = imageId,
            SourceImagePath = imagePath,
            Outcome = outcome,
            InspectionStatus = inspectionStatus,
            ExecutionTimeMs = (long)Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds),
            DiagnosticCode = diagnosticCode,
            DiagnosticMessage = diagnosticMessage,
            HasJudgmentSignal = false,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PrimaryOutputs = new Dictionary<string, object?>
            {
                ["DiagnosticMessage"] = diagnosticMessage
            },
            OutputImageBytes = sourceImageBytes,
            SourceImageBytes = sourceImageBytes
        };
    }

    private static RuntimeRunOutcome ResolveOutcome(
        FlowExecutionResult flowResult,
        InspectionJudgmentEvaluation evaluation,
        bool cancellationRequested)
    {
        if (cancellationRequested ||
            string.Equals(flowResult.ErrorMessage, "Flow was canceled.", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeRunOutcome.Canceled;
        }

        if (!flowResult.IsSuccess)
        {
            return RuntimeRunOutcome.Error;
        }

        return evaluation.Status switch
        {
            InspectionStatus.OK => RuntimeRunOutcome.Ok,
            InspectionStatus.NG => RuntimeRunOutcome.Ng,
            _ => RuntimeRunOutcome.Error
        };
    }

    private static string ResolveDiagnosticCode(
        RuntimeRunOutcome outcome,
        InspectionJudgmentEvaluation evaluation,
        FlowExecutionResult flowResult)
    {
        if (outcome == RuntimeRunOutcome.Canceled)
        {
            return "Canceled";
        }

        if (!flowResult.IsSuccess)
        {
            return "ExecutionFailed";
        }

        return evaluation.MissingJudgmentSignal ? "FlowInvalid" : evaluation.StatusReason;
    }

    private static string? ResolveDiagnosticMessage(
        RuntimeRunOutcome outcome,
        InspectionJudgmentEvaluation evaluation,
        FlowExecutionResult flowResult)
    {
        if (outcome == RuntimeRunOutcome.Canceled)
        {
            return "Run canceled.";
        }

        if (!string.IsNullOrWhiteSpace(flowResult.ErrorMessage))
        {
            return flowResult.ErrorMessage;
        }

        return evaluation.Status == InspectionStatus.Error ? evaluation.StatusReason : null;
    }

    private static Dictionary<string, object?> BuildPrimaryOutputs(Dictionary<string, object> outputData)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in outputData.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (key.Equals("Image", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryNormalizeOutputValue(value, out var normalized))
            {
                result[key] = normalized;
            }
        }

        return result;
    }

    private static bool TryNormalizeOutputValue(object? value, out object? normalized)
    {
        switch (value)
        {
            case null:
                normalized = null;
                return true;
            case byte[]:
                normalized = null;
                return false;
            case string or bool or int or long or double or float or decimal:
                normalized = value;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                normalized = element.ToString();
                return true;
            case IDictionary<string, object> typedDictionary:
                normalized = typedDictionary
                    .Where(pair => TryNormalizeOutputValue(pair.Value, out _))
                    .ToDictionary(pair => pair.Key, pair =>
                    {
                        TryNormalizeOutputValue(pair.Value, out var nested);
                        return nested;
                    }, StringComparer.OrdinalIgnoreCase);
                return true;
            case IEnumerable<object?> sequence:
                var list = new List<object?>();
                foreach (var item in sequence)
                {
                    if (TryNormalizeOutputValue(item, out var nested))
                    {
                        list.Add(nested);
                    }
                }

                normalized = list;
                return true;
            default:
                normalized = value.ToString();
                return true;
        }
    }

    private static byte[]? TryExtractOutputImage(Dictionary<string, object> outputData)
    {
        if (!outputData.TryGetValue("Image", out var outputImage) || outputImage == null)
        {
            return null;
        }

        return outputImage as byte[];
    }
}

internal static class RuntimeResultSnapshot
{
    public static RuntimeNormalizedResult Create(RuntimeNormalizedResult source, bool includeImageBytes)
    {
        return new RuntimeNormalizedResult
        {
            RunId = source.RunId,
            PackageId = source.PackageId,
            PackageName = source.PackageName,
            FlowHash = source.FlowHash,
            ImageId = source.ImageId,
            SourceImagePath = source.SourceImagePath,
            Outcome = source.Outcome,
            InspectionStatus = source.InspectionStatus,
            ExecutionTimeMs = source.ExecutionTimeMs,
            DiagnosticCode = source.DiagnosticCode,
            DiagnosticMessage = source.DiagnosticMessage,
            HasJudgmentSignal = source.HasJudgmentSignal,
            SavedImagePath = source.SavedImagePath,
            StartedAtUtc = source.StartedAtUtc,
            CompletedAtUtc = source.CompletedAtUtc,
            PrimaryOutputs = ClonePrimaryOutputs(source.PrimaryOutputs),
            OutputImageBytes = includeImageBytes ? source.OutputImageBytes?.ToArray() : null,
            SourceImageBytes = includeImageBytes ? source.SourceImageBytes?.ToArray() : null
        };
    }

    private static Dictionary<string, object?> ClonePrimaryOutputs(Dictionary<string, object?> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => CloneOutputValue(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static object? CloneOutputValue(object? value)
    {
        return value switch
        {
            null => null,
            byte[] bytes => bytes.ToArray(),
            JsonElement element => element.Clone(),
            string or bool or int or long or double or float or decimal or DateTime or DateTimeOffset or Guid => value,
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => CloneOutputValue(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            IEnumerable<object?> sequence => sequence.Select(CloneOutputValue).ToList(),
            _ => value
        };
    }
}

internal sealed class RuntimeResultRecordWriter : IAsyncDisposable
{
    private readonly Channel<RuntimeNormalizedResult> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _consumerTask;
    private readonly ILogger _logger;
    private readonly TimeSpan _disposeDrainTimeout;
    private int _backpressureWaitCount;
    private int _droppedCount;
    private int _persistenceFailureCount;
    private int _disposeStarted;

    public RuntimeResultRecordWriter(
        string dataRoot,
        int capacity,
        ILogger logger)
        : this(dataRoot, capacity, logger, default, null)
    {
    }

    public RuntimeResultRecordWriter(
        string dataRoot,
        int capacity,
        ILogger logger,
        TimeSpan disposeDrainTimeout = default,
        Task? consumerTaskOverride = null)
    {
        DataRoot = dataRoot;
        _logger = logger;
        _disposeDrainTimeout = disposeDrainTimeout > TimeSpan.Zero ? disposeDrainTimeout : TimeSpan.FromSeconds(10);
        _channel = Channel.CreateBounded<RuntimeNormalizedResult>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = consumerTaskOverride ?? Task.Run(() => ConsumeAsync(_disposeCts.Token));
    }

    public string DataRoot { get; }

    public int DroppedCount => Volatile.Read(ref _droppedCount);

    public int BackpressureWaitCount => Volatile.Read(ref _backpressureWaitCount);

    public int PersistenceFailureCount => Volatile.Read(ref _persistenceFailureCount);

    public async ValueTask<bool> EnqueueAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _disposeStarted, 0, 0) != 0)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Dropped runtime result record for run {RunId} because the writer is disposed.", result.RunId);
            return false;
        }

        var snapshot = RuntimeResultSnapshot.Create(result, includeImageBytes: false);
        if (_channel.Writer.TryWrite(snapshot))
        {
            return true;
        }

        var waitCount = Interlocked.Increment(ref _backpressureWaitCount);
        if (waitCount == 1 || waitCount % 100 == 0)
        {
            _logger.LogWarning(
                "Runtime result record writer is applying backpressure instead of dropping records. BackpressureWaitCount={BackpressureWaitCount}",
                waitCount);
        }

        try
        {
            await _channel.Writer.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Dropped runtime result record for run {RunId} because the writer is closed.", result.RunId);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Dropped runtime result record for run {RunId} because enqueue was canceled.", result.RunId);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            var completedTask = await Task.WhenAny(_consumerTask, Task.Delay(_disposeDrainTimeout)).ConfigureAwait(false);
            if (completedTask == _consumerTask)
            {
                await _consumerTask.ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Runtime result record writer dispose timed out after {Timeout}. Pending records may be dropped.",
                    _disposeDrainTimeout);
                _ = _consumerTask.ContinueWith(
                    task => _logger.LogDebug(task.Exception, "Runtime result record writer consumer faulted after timed out disposal."),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        finally
        {
            await _disposeCts.CancelAsync();
            _disposeCts.Dispose();
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var result))
                {
                    try
                    {
                        var runDate = result.CompletedAtUtc.LocalDateTime.ToString("yyyyMMdd");
                        var targetDirectory = Path.Combine(DataRoot, "runs", runDate);
                        Directory.CreateDirectory(targetDirectory);
                        var targetPath = Path.Combine(targetDirectory, "runtime-results.jsonl");
                        var line = JsonSerializer.Serialize(result, RuntimeJson.StableSerializerOptions);
                        await File.AppendAllTextAsync(targetPath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var failureCount = Interlocked.Increment(ref _persistenceFailureCount);
                        _logger.LogError(
                            ex,
                            "Failed to persist runtime result record for run {RunId}. PersistenceFailureCount={PersistenceFailureCount}",
                            result.RunId,
                            failureCount);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal sealed class RuntimeImageWriter : IAsyncDisposable
{
    private readonly Channel<RuntimeNormalizedResult> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _consumerTask;
    private readonly RuntimeProfile _profile;
    private readonly ILogger _logger;
    private readonly TimeSpan _disposeDrainTimeout;
    private int _backpressureWaitCount;
    private int _droppedCount;
    private int _persistenceFailureCount;
    private int _disposeStarted;

    public RuntimeImageWriter(
        string dataRoot,
        RuntimeProfile profile,
        ILogger logger)
        : this(dataRoot, profile, logger, default, null)
    {
    }

    public RuntimeImageWriter(
        string dataRoot,
        RuntimeProfile profile,
        ILogger logger,
        TimeSpan disposeDrainTimeout = default,
        Task? consumerTaskOverride = null)
    {
        DataRoot = dataRoot;
        _profile = profile;
        _logger = logger;
        _disposeDrainTimeout = disposeDrainTimeout > TimeSpan.Zero ? disposeDrainTimeout : TimeSpan.FromSeconds(10);
        _channel = Channel.CreateBounded<RuntimeNormalizedResult>(new BoundedChannelOptions(Math.Max(1, profile.ImageQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = consumerTaskOverride ?? Task.Run(() => ConsumeAsync(_disposeCts.Token));
    }

    public string DataRoot { get; }

    public int DroppedCount => Volatile.Read(ref _droppedCount);

    public int BackpressureWaitCount => Volatile.Read(ref _backpressureWaitCount);

    public int PersistenceFailureCount => Volatile.Read(ref _persistenceFailureCount);

    public bool ShouldPersist(RuntimeNormalizedResult result)
    {
        return result.Outcome switch
        {
            RuntimeRunOutcome.Ok => _profile.SaveOkImages,
            RuntimeRunOutcome.Ng => _profile.SaveNgImages,
            RuntimeRunOutcome.Error => _profile.SaveErrorImages,
            _ => false
        };
    }

    public string PlanPath(RuntimeNormalizedResult result)
    {
        var runDate = result.CompletedAtUtc.LocalDateTime.ToString("yyyyMMdd");
        var outcomeFolder = result.Outcome switch
        {
            RuntimeRunOutcome.Ok => "OK",
            RuntimeRunOutcome.Ng => "NG",
            _ => "ERROR"
        };
        var extension = InspectionImageFormatDetector.GuessExtension(SelectImageBytes(result));
        return Path.Combine(
            DataRoot,
            "images",
            runDate,
            outcomeFolder,
            $"{result.ImageId}_{result.RunId}{extension}");
    }

    public async ValueTask<bool> EnqueueAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken)
    {
        if (SelectImageBytes(result) == null || string.IsNullOrWhiteSpace(result.SavedImagePath))
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _disposeStarted, 0, 0) != 0)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Dropped runtime image write for run {RunId} because the writer is disposed.", result.RunId);
            return false;
        }

        var snapshot = RuntimeResultSnapshot.Create(result, includeImageBytes: true);
        if (_channel.Writer.TryWrite(snapshot))
        {
            return true;
        }

        var waitCount = Interlocked.Increment(ref _backpressureWaitCount);
        if (waitCount == 1 || waitCount % 100 == 0)
        {
            _logger.LogWarning(
                "Runtime image writer is applying backpressure instead of dropping configured image saves. BackpressureWaitCount={BackpressureWaitCount}",
                waitCount);
        }

        try
        {
            await _channel.Writer.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Dropped runtime image write for run {RunId} because the writer is closed.", result.RunId);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Dropped runtime image write for run {RunId} because enqueue was canceled.", result.RunId);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            var completedTask = await Task.WhenAny(_consumerTask, Task.Delay(_disposeDrainTimeout)).ConfigureAwait(false);
            if (completedTask == _consumerTask)
            {
                await _consumerTask.ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Runtime image writer dispose timed out after {Timeout}. Pending images may be dropped.",
                    _disposeDrainTimeout);
                _ = _consumerTask.ContinueWith(
                    task => _logger.LogDebug(task.Exception, "Runtime image writer consumer faulted after timed out disposal."),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        finally
        {
            await _disposeCts.CancelAsync();
            _disposeCts.Dispose();
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var result))
                {
                    try
                    {
                        var bytes = SelectImageBytes(result);
                        if (bytes == null || bytes.Length == 0 || string.IsNullOrWhiteSpace(result.SavedImagePath))
                        {
                            continue;
                        }

                        var directory = Path.GetDirectoryName(result.SavedImagePath);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await File.WriteAllBytesAsync(result.SavedImagePath, bytes, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var failureCount = Interlocked.Increment(ref _persistenceFailureCount);
                        _logger.LogError(
                            ex,
                            "Failed to persist runtime image for run {RunId}. PersistenceFailureCount={PersistenceFailureCount}",
                            result.RunId,
                            failureCount);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static byte[]? SelectImageBytes(RuntimeNormalizedResult result)
    {
        if (result.OutputImageBytes is { Length: > 0 } outputImageBytes)
        {
            return outputImageBytes;
        }

        if (result.SourceImageBytes is { Length: > 0 } sourceImageBytes)
        {
            return sourceImageBytes;
        }

        return null;
    }

}
