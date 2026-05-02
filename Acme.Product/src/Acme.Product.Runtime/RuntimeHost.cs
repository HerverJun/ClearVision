using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Acme.Product.Application.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Entities.Base;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Services;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Runtime;

public sealed class RuntimeHost : IAsyncDisposable
{
    private readonly IFlowExecutionService _flowExecutionService;
    private readonly RuntimePackageLoader _packageLoader;
    private readonly RuntimeResultNormalizer _resultNormalizer;
    private readonly ILogger<RuntimeHost> _logger;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private RuntimeHostState _state = RuntimeHostState.Idle;
    private RuntimePackage? _loadedPackage;
    private CancellationTokenSource? _activeRunCts;
    private Task? _backgroundRunTask;
    private Guid? _currentFlowId;
    private string? _currentRunId;
    private int _sessionOkCount;
    private int _sessionNgCount;
    private int _sessionErrorCount;
    private int _pendingCount;
    private RuntimeResultRecordWriter? _resultWriter;
    private RuntimeImageWriter? _imageWriter;

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
            ResetSessionCounters();
            await RecreateWritersAsync(package.RuntimeProfile);
            _state = RuntimeHostState.Loaded;
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();
        EmitLog($"Loaded package {package.Manifest.PackageName} ({package.Manifest.FlowHash}).");
        return package;
    }

    public async Task<RuntimeNormalizedResult> RunSingleAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePackageLoaded();
            EnsureNotRunning();
            _activeRunCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _state = RuntimeHostState.Running;
            _backgroundRunTask = ExecuteSingleCoreAsync(imagePath, _activeRunCts.Token);
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();

        try
        {
            return await ((Task<RuntimeNormalizedResult>)_backgroundRunTask);
        }
        finally
        {
            await FinalizeRunAsync();
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
                throw new RuntimePackageException("The selected folder does not contain any supported images.");
            }

            _pendingCount = files.Count;
            _activeRunCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _state = RuntimeHostState.Running;
            _backgroundRunTask = Task.Run(
                () => ExecuteFolderReplayAsync(files, _activeRunCts.Token),
                CancellationToken.None);
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();
        EmitLog($"Started folder replay with {_pendingCount} image(s).");
    }

    public async Task<RuntimeStopSummary> StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? activeRunCts;
        Task? backgroundRunTask;
        Guid? currentFlowId;
        RuntimeProfile? profile;
        int pendingCount;
        bool wasRunning;

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            wasRunning = _state is RuntimeHostState.Running or RuntimeHostState.Stopping;
            activeRunCts = _activeRunCts;
            backgroundRunTask = _backgroundRunTask;
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
        EmitLog("Stop requested.");

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
        var completedTask = await Task.WhenAny(backgroundRunTask, Task.Delay(timeout, cancellationToken));
        var completed = completedTask == backgroundRunTask;

        if (completed)
        {
            await FinalizeRunAsync();
        }
        else
        {
            await _stateGate.WaitAsync(cancellationToken);
            try
            {
                _state = RuntimeHostState.Faulted;
            }
            finally
            {
                _stateGate.Release();
            }

            EmitSnapshot();
            EmitLog("Stop timed out before the active run exited.");
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
        try
        {
            await StopAsync();
        }
        catch
        {
        }

        if (_resultWriter != null)
        {
            await _resultWriter.DisposeAsync();
        }

        if (_imageWriter != null)
        {
            await _imageWriter.DisposeAsync();
        }

        _activeRunCts?.Dispose();
        _stateGate.Dispose();
    }

    private async Task ExecuteFolderReplayAsync(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
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
            EmitLog("Folder replay canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Folder replay failed.");
            EmitLog($"Folder replay failed: {ex.Message}");
            await _stateGate.WaitAsync(cancellationToken);
            try
            {
                _state = RuntimeHostState.Faulted;
            }
            finally
            {
                _stateGate.Release();
            }

            EmitSnapshot();
        }
        finally
        {
            await FinalizeRunAsync();
        }
    }

    private async Task<RuntimeNormalizedResult> ExecuteSingleCoreAsync(string imagePath, CancellationToken cancellationToken)
    {
        var package = _loadedPackage ?? throw new RuntimePackageException("No runtime package has been loaded.");
        if (!File.Exists(imagePath))
        {
            throw new RuntimePackageException($"Input image does not exist: {imagePath}");
        }

        var sourceImageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var imageId = BuildImageId(imagePath);
        var startedAt = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid().ToString("N");
        _currentRunId = runId;
        EmitSnapshot();

        try
        {
            var flow = RuntimeFlowAdapter.ToEntity(package.Flow);
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
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Image"] = sourceImageBytes
                },
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
            _imageWriter.TryEnqueue(result);
        }

        if (_resultWriter != null)
        {
            _resultWriter.TryEnqueue(result);
        }

        ResultAvailable?.Invoke(result);
        EmitLog($"{result.ImageId}: {result.Outcome} ({result.ExecutionTimeMs} ms)");
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

    private async Task FinalizeRunAsync()
    {
        await _stateGate.WaitAsync();
        try
        {
            _activeRunCts?.Dispose();
            _activeRunCts = null;
            _backgroundRunTask = null;
            _currentFlowId = null;
            _currentRunId = null;
            _pendingCount = 0;

            if (_state != RuntimeHostState.Faulted)
            {
                _state = _loadedPackage == null ? RuntimeHostState.Idle : RuntimeHostState.Loaded;
            }
        }
        finally
        {
            _stateGate.Release();
        }

        EmitSnapshot();
    }

    private async Task RecreateWritersAsync(RuntimeProfile profile)
    {
        if (_resultWriter != null)
        {
            await _resultWriter.DisposeAsync();
        }

        if (_imageWriter != null)
        {
            await _imageWriter.DisposeAsync();
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

    private static IEnumerable<string> EnumerateReplayFiles(string folderPath, RuntimeProfile profile)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new RuntimePackageException($"Replay folder does not exist: {folderPath}");
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
            throw new RuntimePackageException("No runtime package has been loaded.");
        }
    }

    private void EnsureNotRunning()
    {
        if (_state is RuntimeHostState.Running or RuntimeHostState.Stopping)
        {
            throw new RuntimePackageException("The runtime host is already busy.");
        }
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
        string imagePath,
        byte[] sourceImageBytes,
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
        string imagePath,
        byte[] sourceImageBytes,
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
        string imagePath,
        byte[] sourceImageBytes,
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
        string imagePath,
        byte[] sourceImageBytes,
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
        string imagePath,
        byte[] sourceImageBytes,
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

internal sealed class RuntimeResultRecordWriter : IAsyncDisposable
{
    private readonly Channel<RuntimeNormalizedResult> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _consumerTask;
    private readonly ILogger _logger;

    public RuntimeResultRecordWriter(string dataRoot, int capacity, ILogger logger)
    {
        DataRoot = dataRoot;
        _logger = logger;
        _channel = Channel.CreateBounded<RuntimeNormalizedResult>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_disposeCts.Token));
    }

    public string DataRoot { get; }

    public int DroppedCount { get; private set; }

    public bool TryEnqueue(RuntimeNormalizedResult result)
    {
        if (_channel.Writer.TryWrite(result))
        {
            return true;
        }

        DroppedCount += 1;
        _logger.LogWarning("Dropped runtime result record for run {RunId}", result.RunId);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _disposeCts.CancelAsync();
        await _consumerTask;
        _disposeCts.Dispose();
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var result))
                {
                    var runDate = result.CompletedAtUtc.LocalDateTime.ToString("yyyyMMdd");
                    var targetDirectory = Path.Combine(DataRoot, "runs", runDate);
                    Directory.CreateDirectory(targetDirectory);
                    var targetPath = Path.Combine(targetDirectory, "runtime-results.jsonl");
                    var line = JsonSerializer.Serialize(result, RuntimeJson.SerializerOptions);
                    await File.AppendAllTextAsync(targetPath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
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

    public RuntimeImageWriter(string dataRoot, RuntimeProfile profile, ILogger logger)
    {
        DataRoot = dataRoot;
        _profile = profile;
        _logger = logger;
        _channel = Channel.CreateBounded<RuntimeNormalizedResult>(new BoundedChannelOptions(profile.ImageQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_disposeCts.Token));
    }

    public string DataRoot { get; }

    public int DroppedCount { get; private set; }

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
        var extension = GuessImageExtension(result.OutputImageBytes ?? result.SourceImageBytes);
        return Path.Combine(
            DataRoot,
            "images",
            runDate,
            outcomeFolder,
            $"{result.ImageId}_{result.RunId}{extension}");
    }

    public bool TryEnqueue(RuntimeNormalizedResult result)
    {
        if (_channel.Writer.TryWrite(result))
        {
            return true;
        }

        DroppedCount += 1;
        _logger.LogWarning("Dropped runtime image write for run {RunId}", result.RunId);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _disposeCts.CancelAsync();
        await _consumerTask;
        _disposeCts.Dispose();
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var result))
                {
                    var bytes = result.OutputImageBytes ?? result.SourceImageBytes;
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
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string GuessImageExtension(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 2)
        {
            return ".bin";
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ".jpg";
        }

        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return ".bin";
    }
}
