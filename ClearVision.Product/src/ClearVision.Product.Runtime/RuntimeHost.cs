using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Entities.Base;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.RuntimeAssets;
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
    private ExecutionSnapshot? _currentExecutionSnapshot;
    private int _sessionOkCount;
    private int _sessionNgCount;
    private int _sessionErrorCount;
    private int _sessionUndeterminedCount;
    private int _sessionNotApplicableCount;
    private int _sessionInvalidCount;
    private int _sessionFailedCount;
    private int _sessionCancelledCount;
    private int _sessionTimedOutCount;
    private int _sessionSkippedCount;
    private int _pendingCount;
    private IRuntimeResultRecordWriter? _resultWriter;
    private IRuntimeImageWriter? _imageWriter;
    private IProjectVariableSession? _projectVariableSession;
    private readonly IProjectVariableStateStore? _projectVariableStateStore;
    private string? _projectVariableStateScopeId;
    private readonly Func<RuntimeProfile, ValueTask<RuntimePreparedWriters>> _writerFactory;
    private readonly Func<string, RuntimeProfile, IRuntimeResultRecordWriter> _resultWriterFactory;
    private readonly Func<string, RuntimeProfile, IRuntimeImageWriter> _imageWriterFactory;
    private int _disposeStarted;

    public RuntimeHost(
        IFlowExecutionService flowExecutionService,
        RuntimePackageLoader packageLoader,
        RuntimeResultNormalizer resultNormalizer,
        ILogger<RuntimeHost> logger,
        IProjectVariableStateStore? projectVariableStateStore = null)
        : this(flowExecutionService, packageLoader, resultNormalizer, logger, null, projectVariableStateStore)
    {
    }

    internal RuntimeHost(
        IFlowExecutionService flowExecutionService,
        RuntimePackageLoader packageLoader,
        RuntimeResultNormalizer resultNormalizer,
        ILogger<RuntimeHost> logger,
        Func<RuntimeProfile, ValueTask<RuntimePreparedWriters>>? writerFactory,
        IProjectVariableStateStore? projectVariableStateStore = null,
        Func<string, RuntimeProfile, IRuntimeResultRecordWriter>? resultWriterFactory = null,
        Func<string, RuntimeProfile, IRuntimeImageWriter>? imageWriterFactory = null)
    {
        _flowExecutionService = flowExecutionService;
        _packageLoader = packageLoader;
        _resultNormalizer = resultNormalizer;
        _logger = logger;
        _resultWriterFactory = resultWriterFactory ?? ((dataRoot, profile) =>
            new RuntimeResultRecordWriter(dataRoot, profile.ResultRecordQueueCapacity, _logger));
        _imageWriterFactory = imageWriterFactory ?? ((dataRoot, profile) =>
            new RuntimeImageWriter(dataRoot, profile, _logger));
        _writerFactory = writerFactory ?? CreateWritersAsync;
        _projectVariableStateStore = projectVariableStateStore;
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
            PackageFlowHash = _loadedPackage?.Manifest.FlowHash,
            ExecutionFlowHash = _currentExecutionSnapshot?.FlowHash,
            FlowHash = _currentExecutionSnapshot?.FlowHash,
            ExecutionSnapshotId = _currentExecutionSnapshot?.SnapshotId,
            ProjectRevision = _currentExecutionSnapshot?.PersistenceRevision,
            DecisionConfigurationHash = _currentExecutionSnapshot?.DecisionConfigurationHash,
            ExecutionRunMode = _currentExecutionSnapshot?.RunMode.ToString(),
            CurrentRunId = _currentRunId,
            SessionOkCount = _sessionOkCount,
            SessionNgCount = _sessionNgCount,
            SessionErrorCount = _sessionErrorCount,
            SessionOutcomeStatistics = BuildSessionOutcomeStatistics()
        };
    }

    public IReadOnlyList<ProjectVariableValueSnapshot> GetProjectVariableSnapshots()
    {
        return _projectVariableSession?.GetSnapshots() ?? [];
    }

    public async Task<ProjectVariableValueSnapshot> SetProjectVariableValueAsync(
        Guid variableId,
        object? value,
        CancellationToken cancellationToken = default)
    {
        return await SetProjectVariableValueAsync(variableId, value, expectedVersion: null, cancellationToken);
    }

    public async Task<ProjectVariableValueSnapshot> SetProjectVariableValueAsync(
        Guid variableId,
        object? value,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePackageLoaded();
            EnsureNotRunning();
            var session = _projectVariableSession ?? throw new RuntimePackageException("Project variable session is not initialized.");
            if (!session.TryGetDefinition(variableId, out var definition))
            {
                throw new RuntimePackageException($"Project global variable '{variableId}' does not exist.");
            }

            if (!definition.ManualWriteAllowed)
            {
                throw new RuntimePackageException($"Project global variable '{definition.Name}' does not allow manual Station writes.");
            }

            var expectedVersions = CaptureProjectVariableVersions(session, variableId, expectedVersion);
            using var candidate = session.CreateSnapshotClone();
            var snapshot = candidate.SetValue(variableId, value, ProjectVariableUpdatedBy.StationManual);
            var commitResult = CommitLoadedProjectVariableSessionNoLock(candidate, expectedVersions);
            if (!commitResult.Succeeded)
            {
                throw new RuntimePackageException(commitResult.Error ?? "Project global variable state could not be persisted.");
            }

            EmitLog($"Station updated project global variable '{definition.Name}'.");
            return snapshot;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<ProjectVariableValueSnapshot> ResetProjectVariableAsync(
        Guid variableId,
        CancellationToken cancellationToken = default)
    {
        return await ResetProjectVariableAsync(variableId, expectedVersion: null, cancellationToken);
    }

    public async Task<ProjectVariableValueSnapshot> ResetProjectVariableAsync(
        Guid variableId,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePackageLoaded();
            EnsureNotRunning();
            var session = _projectVariableSession ?? throw new RuntimePackageException("Project variable session is not initialized.");
            if (!session.TryGetDefinition(variableId, out var definition))
            {
                throw new RuntimePackageException($"Project global variable '{variableId}' does not exist.");
            }

            if (!definition.ManualWriteAllowed)
            {
                throw new RuntimePackageException($"Project global variable '{definition.Name}' does not allow manual Station resets.");
            }

            var expectedVersions = CaptureProjectVariableVersions(session, variableId, expectedVersion);
            using var candidate = session.CreateSnapshotClone();
            var snapshot = candidate.Reset(variableId, ProjectVariableUpdatedBy.Reset);
            var commitResult = CommitLoadedProjectVariableSessionNoLock(candidate, expectedVersions);
            if (!commitResult.Succeeded)
            {
                throw new RuntimePackageException(commitResult.Error ?? "Project global variable state could not be persisted.");
            }

            EmitLog($"Station reset project global variable '{definition.Name}' to its initial value.");
            return snapshot;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<RuntimePackage> LoadPackageAsync(string packageRoot, CancellationToken cancellationToken = default)
    {
        var package = await _packageLoader.LoadAsync(packageRoot, cancellationToken);
        var stateScopeId = BuildRuntimeProjectVariableStateScopeId(package);
        var persistedSnapshots = _projectVariableStateStore?.Load(stateScopeId, package.GlobalVariables) ?? [];
        var projectVariableSession = new ProjectVariableSession(package.GlobalVariables, persistedSnapshots);
        var nextSiteProfile = RuntimeParameterOverrideApplier.CloneProfile(package.DefaultSiteProfile);
        RuntimePreparedWriters? preparedWriters = null;
        IProjectVariableSession? oldProjectVariableSession = null;
        IRuntimeResultRecordWriter? oldResultWriter = null;
        IRuntimeImageWriter? oldImageWriter = null;

        try
        {
            preparedWriters = await _writerFactory(package.RuntimeProfile);

            await _stateGate.WaitAsync(cancellationToken);
            try
            {
                EnsureNotRunning();

                oldProjectVariableSession = _projectVariableSession;
                oldResultWriter = _resultWriter;
                oldImageWriter = _imageWriter;

                _loadedPackage = package;
                _currentExecutionSnapshot = package.ExecutionSnapshot;
                _projectVariableSession = projectVariableSession;
                _projectVariableStateScopeId = stateScopeId;
                projectVariableSession = null;
                _resultWriter = preparedWriters.ResultWriter;
                _imageWriter = preparedWriters.ImageWriter;
                preparedWriters = null;
                lock (_profileGate)
                {
                    _activeSiteProfile = nextSiteProfile;
                }

                ResetSessionCounters();
                _state = RuntimeHostState.Loaded;
            }
            finally
            {
                _stateGate.Release();
            }
        }
        catch
        {
            projectVariableSession?.Dispose();
            if (preparedWriters != null)
            {
                await DisposePreparedWritersAfterPrepareFailureAsync(preparedWriters);
            }

            throw;
        }

        DisposeOldProjectVariableSession(oldProjectVariableSession);
        await DisposeOldWriterAfterCommitAsync(oldResultWriter, "runtime result record writer");
        await DisposeOldWriterAfterCommitAsync(oldImageWriter, "runtime image writer");

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
            ValidateRuntimeAdmission(_loadedPackage!);
            runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runGeneration = Interlocked.Increment(ref _runGenerationCounter);
            _activeRunGeneration = runGeneration;
            _stopTimeoutPending = false;
            _activeRunCts = runCts;
            _state = RuntimeHostState.Running;
            backgroundRunTask = Task.Run(
                () => ExecuteSingleCoreAsync(imagePath, runCts.Token),
                CancellationToken.None);
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
            ValidateRuntimeAdmission(_loadedPackage!);

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

        _projectVariableSession?.Dispose();
        _projectVariableSession = null;

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
        var runtimeSnapshot = package.ExecutionSnapshot
            ?? throw new RuntimePackageException("The loaded package does not contain a runtime execution snapshot.");
        try
        {
            var profile = GetActiveSiteProfileSnapshot(package);
            var applyResult = RuntimeParameterOverrideApplier.CloneAndApply(package, profile);
            var appliedFlow = RuntimeFlowAdapter.ToEntity(applyResult.Flow);
            var appliedIdentityFlow = RuntimeFlowAdapter.ToEntity(applyResult.IdentityFlow);
            runtimeSnapshot = CreateRuntimeExecutionSnapshot(package, appliedFlow, appliedIdentityFlow, runtimeSnapshot);
            _currentExecutionSnapshot = runtimeSnapshot;
            _currentRunId = runId;
            EmitSnapshot();
            var flow = runtimeSnapshot.CreateExecutionFlow();
            _currentFlowId = flow.Id;
            var variableSession = _projectVariableSession ?? new ProjectVariableSession(package.GlobalVariables);
            var variableContext = new ProjectVariableExecutionContext(
                variableSession,
                ProjectVariableBindingIndex.Build(package.GlobalVariables),
                Guid.TryParse(runId, out var parsedRunId) ? parsedRunId : Guid.NewGuid(),
                commitHandler: CommitLoadedProjectVariableSession);

            var validation = _flowExecutionService.ValidateSnapshot(runtimeSnapshot);
            if (!validation.IsValid)
            {
                var invalidResult = _resultNormalizer.CreateValidationFailure(
                    package,
                    runtimeSnapshot,
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

            var flowResult = await _flowExecutionService.ExecuteWithSnapshotAsync(
                runtimeSnapshot,
                BuildFlowInputData(sourceImageBytes, package.AssetContext),
                variableContext,
                cancellationToken: timeoutCts.Token);
            var resultVariableSession = _projectVariableSession ?? variableSession;

            var normalizedResult = _resultNormalizer.Normalize(
                package,
                runtimeSnapshot,
                runId,
                imageId,
                imagePath,
                sourceImageBytes,
                flowResult,
                flow,
                startedAt,
                DateTimeOffset.UtcNow,
                timeoutCts.IsCancellationRequested);
            AttachPublicGlobalVariables(normalizedResult, package.GlobalVariables, resultVariableSession);

            await PersistResultAsync(normalizedResult, cancellationToken);
            return normalizedResult;
        }
        catch (OperationCanceledException)
        {
            var canceled = _resultNormalizer.CreateCanceledResult(
                package,
                runtimeSnapshot,
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
                runtimeSnapshot,
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

    private static ExecutionSnapshot CreateRuntimeExecutionSnapshot(
        RuntimePackage package,
        OperatorFlow appliedFlow,
        OperatorFlow appliedIdentityFlow,
        ExecutionSnapshot loadedSnapshot)
    {
        var appliedFlowHash = ExecutionFlowIdentity.ComputeFlowHash(appliedIdentityFlow);
        var appliedDecisionHash = ExecutionFlowIdentity.ComputeDecisionConfigurationHash(
            appliedIdentityFlow.DecisionConfiguration);
        if (string.Equals(appliedFlowHash, loadedSnapshot.FlowHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(appliedDecisionHash, loadedSnapshot.DecisionConfigurationHash, StringComparison.OrdinalIgnoreCase))
        {
            return loadedSnapshot;
        }

        return new ExecutionSnapshot(
            loadedSnapshot.ProjectId,
            appliedFlow,
            package.Manifest.SourceProjectRevision,
            ExecutionSnapshotSource.RuntimePackage,
            ExecutionRunMode.StationRuntime,
            loadedSnapshot.ResourceBindings,
            runtimePackageId: package.Manifest.PackageId,
            globalVariables: package.GlobalVariables,
            executionIdentityFlow: appliedIdentityFlow);
    }

    private static void AttachPublicGlobalVariables(
        RuntimeNormalizedResult result,
        ProjectGlobalVariableSchema schema,
        IProjectVariableSession session)
    {
        result.GlobalVariableSchemaHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema);

        var publicVariables = schema.Variables
            .Where(variable => variable.IncludeInResultMetadata)
            .ToDictionary(variable => variable.Id);
        if (publicVariables.Count == 0)
        {
            return;
        }

        foreach (var snapshot in session.GetSnapshots())
        {
            if (!publicVariables.TryGetValue(snapshot.VariableId, out var definition))
            {
                continue;
            }

            result.PublicGlobalVariables[definition.Name] = ProjectVariableValueConverter.ToObject(snapshot.Value);
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

        InvokeRuntimeEventHandlers(ResultAvailable, result, nameof(ResultAvailable));
        EmitLog($"{result.ImageId}: {FormatOutcome(result.Outcome)}（{result.ExecutionTimeMs} ms）");
        EmitSnapshot();
        await Task.CompletedTask;
    }

    private void ResetSessionCounters()
    {
        _sessionOkCount = 0;
        _sessionNgCount = 0;
        _sessionErrorCount = 0;
        _sessionUndeterminedCount = 0;
        _sessionNotApplicableCount = 0;
        _sessionInvalidCount = 0;
        _sessionFailedCount = 0;
        _sessionCancelledCount = 0;
        _sessionTimedOutCount = 0;
        _sessionSkippedCount = 0;
        _pendingCount = 0;
    }

    private void UpdateSessionCounters(RuntimeNormalizedResult result)
    {
        var canonical = new InspectionOutcome(
            result.ExecutionOutcome,
            result.DecisionOutcome,
            result.DecisionSource,
            result.ReasonCode,
            result.DiagnosticMessage,
            result.HasJudgmentSignal);
        switch (InspectionOutcomeClassifier.Classify(canonical))
        {
            case CanonicalInspectionOutcomeKind.Ok:
                _sessionOkCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.Ng:
                _sessionNgCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.Undetermined:
                _sessionUndeterminedCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.NotApplicable:
                _sessionNotApplicableCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.Invalid:
                _sessionInvalidCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.Failed:
                _sessionFailedCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.Cancelled:
                _sessionCancelledCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.TimedOut:
                _sessionTimedOutCount += 1;
                break;
            case CanonicalInspectionOutcomeKind.Skipped:
                _sessionSkippedCount += 1;
                break;
        }

        // Compatibility counter: historic Station UIs classified Invalid together with
        // execution errors. Canonical consumers use SessionOutcomeStatistics instead.
        switch (result.Outcome)
        {
            case RuntimeRunOutcome.Error:
                _sessionErrorCount += 1;
                break;
        }
    }

    private InspectionOutcomeStatistics BuildSessionOutcomeStatistics()
    {
        var executionSucceeded = _sessionOkCount +
                                 _sessionNgCount +
                                 _sessionUndeterminedCount +
                                 _sessionNotApplicableCount +
                                 _sessionInvalidCount;
        return new InspectionOutcomeStatistics
        {
            TotalAttemptCount = executionSucceeded +
                                _sessionFailedCount +
                                _sessionCancelledCount +
                                _sessionTimedOutCount +
                                _sessionSkippedCount,
            ExecutionSucceededCount = executionSucceeded,
            ValidDecisionCount = _sessionOkCount + _sessionNgCount,
            OkCount = _sessionOkCount,
            NgCount = _sessionNgCount,
            UndeterminedCount = _sessionUndeterminedCount,
            NotApplicableCount = _sessionNotApplicableCount,
            InvalidCount = _sessionInvalidCount,
            FailedCount = _sessionFailedCount,
            CancelledCount = _sessionCancelledCount,
            TimedOutCount = _sessionTimedOutCount,
            SkippedCount = _sessionSkippedCount
        };
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

    private async ValueTask<RuntimePreparedWriters> CreateWritersAsync(RuntimeProfile profile)
    {
        var dataRoot = RuntimePathGuard.GetDefaultStationDataRoot();
        Directory.CreateDirectory(dataRoot);
        IRuntimeResultRecordWriter? resultWriter = null;

        try
        {
            resultWriter = _resultWriterFactory(dataRoot, profile);
            var imageWriter = _imageWriterFactory(dataRoot, profile);
            return new RuntimePreparedWriters(resultWriter, imageWriter);
        }
        catch
        {
            if (resultWriter != null)
            {
                await DisposePreparedWriterAfterCreateFailureAsync(resultWriter, "runtime result record writer");
            }

            throw;
        }
    }

    private async ValueTask DisposePreparedWriterAfterCreateFailureAsync(IAsyncDisposable writer, string resourceName)
    {
        try
        {
            await writer.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose prepared {ResourceName} after writer creation failed.", resourceName);
        }
    }

    private async ValueTask DisposePreparedWritersAfterPrepareFailureAsync(RuntimePreparedWriters preparedWriters)
    {
        try
        {
            await preparedWriters.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose prepared runtime writers after package load prepare failed.");
        }
    }

    private void DisposeOldProjectVariableSession(IProjectVariableSession? session)
    {
        if (session == null)
        {
            return;
        }

        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose old project variable session after runtime package commit.");
        }
    }

    private ProjectVariableCommitResult CommitLoadedProjectVariableSession(
        IProjectVariableSession workingSession,
        IReadOnlyDictionary<Guid, long> expectedVersions)
    {
        _stateGate.Wait();
        try
        {
            return CommitLoadedProjectVariableSessionNoLock(workingSession, expectedVersions);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private ProjectVariableCommitResult CommitLoadedProjectVariableSessionNoLock(
        IProjectVariableSession workingSession,
        IReadOnlyDictionary<Guid, long> expectedVersions)
    {
        var authoritative = _projectVariableSession;
        if (authoritative == null)
        {
            return ProjectVariableCommitResult.Failure("GV025: project global variable session is not initialized.");
        }

        var authoritativeHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(authoritative.Schema);
        var workingHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(workingSession.Schema);
        if (!string.Equals(authoritativeHash, workingHash, StringComparison.Ordinal))
        {
            return ProjectVariableCommitResult.Failure("GV025: project global variable schema changed before this run could commit.");
        }

        using var candidate = authoritative.CreateSnapshotClone();
        if (!candidate.TryCommitFrom(workingSession, expectedVersions, out var commitError))
        {
            return ProjectVariableCommitResult.Failure(commitError);
        }

        try
        {
            PersistLoadedProjectVariableSession(candidate);
            _projectVariableSession = candidate.CreateSnapshotClone();
            return ProjectVariableCommitResult.Success();
        }
        catch (Exception ex)
        {
            return ProjectVariableCommitResult.Failure($"GV030: project global variable state could not be persisted: {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<Guid, long> CaptureProjectVariableVersions(
        IProjectVariableSession session,
        Guid? callerVariableId = null,
        long? callerExpectedVersion = null)
    {
        var versions = session.GetSnapshots().ToDictionary(snapshot => snapshot.VariableId, snapshot => snapshot.Version);
        if (callerVariableId.HasValue && callerExpectedVersion.HasValue)
        {
            versions[callerVariableId.Value] = callerExpectedVersion.Value;
        }

        return versions;
    }

    private void PersistLoadedProjectVariableSession(IProjectVariableSession session)
    {
        if (_projectVariableStateStore == null || string.IsNullOrWhiteSpace(_projectVariableStateScopeId))
        {
            return;
        }

        _projectVariableStateStore.Save(_projectVariableStateScopeId, session.Schema, session.GetSnapshots());
    }

    private static string BuildRuntimeProjectVariableStateScopeId(RuntimePackage package)
    {
        return $"runtime:{package.Manifest.PackageId}";
    }

    private async ValueTask DisposeOldWriterAfterCommitAsync(IAsyncDisposable? writer, string resourceName)
    {
        if (writer == null)
        {
            return;
        }

        try
        {
            await writer.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose old {ResourceName} after runtime package commit.", resourceName);
        }
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

    private static Dictionary<string, object>? BuildFlowInputData(
        byte[]? sourceImageBytes,
        IRuntimeAssetContext assetContext)
    {
        var inputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (sourceImageBytes is { Length: > 0 })
        {
            inputs["Image"] = sourceImageBytes;
        }

        if (!assetContext.IsEmpty)
        {
            inputs[RuntimeAssetInputKeys.RuntimeAssetContext] = assetContext;
        }

        return inputs.Count == 0 ? null : inputs;
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
        InvokeRuntimeEventHandlers(SnapshotChanged, GetSnapshot(), nameof(SnapshotChanged));
    }

    private void EmitLog(string message)
    {
        InvokeRuntimeEventHandlers(LogMessage, message, nameof(LogMessage));
        _logger.LogInformation("{Message}", message);
    }

    private void InvokeRuntimeEventHandlers<T>(Action<T>? handlers, T value, string eventName)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<T>)handler).Invoke(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "RuntimeHost {EventName} subscriber failed; continuing with remaining subscribers.",
                    eventName);
            }
        }
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
            RuntimeRunOutcome.Undetermined => "未判定",
            _ => outcome.ToString()
        };
    }

    private void ValidateRuntimeAdmission(RuntimePackage package)
    {
        var flow = RuntimeFlowAdapter.ToEntity(package.Flow);
        var admission = ExecutionAdmissionService.ValidateStandaloneFlow(
            flow,
            ExecutionAdmissionSurface.StationRuntimeExecution);
        if (!admission.IsAllowed)
        {
            throw new RuntimePackageException($"{admission.Code}: {admission.Message}");
        }

        var snapshot = package.ExecutionSnapshot
            ?? throw new RuntimePackageException("The loaded package does not contain an execution snapshot.");
        var validation = _flowExecutionService.ValidateSnapshot(snapshot);
        if (!validation.IsValid)
        {
            throw new RuntimePackageException($"ADMISSION_FLOW_INVALID: {string.Join("; ", validation.Errors)}");
        }
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
        ExecutionSnapshot snapshot,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        FlowExecutionResult flowResult,
        OperatorFlow flow,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        bool cancellationRequested)
    {
        var outputData = flowResult.OutputData ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var canonicalOutcome = cancellationRequested
            ? new InspectionOutcome(
                ExecutionOutcome.Cancelled,
                DecisionOutcome.Undetermined,
                "RuntimeHost",
                "Canceled",
                "Run canceled.")
            : InspectionOutcomeResolver.Resolve(flowResult, flow);

        var outcome = ResolveOutcome(canonicalOutcome);
        InspectionStatus? inspectionStatus = canonicalOutcome.Execution == ExecutionOutcome.Cancelled
            ? null
            : LegacyInspectionStatusProjection.Project(canonicalOutcome);
        var diagnosticCode = canonicalOutcome.ReasonCode ?? string.Empty;
        var diagnosticMessage = canonicalOutcome.Message;

        return new RuntimeNormalizedResult
        {
            RunId = runId,
            PackageId = package.Manifest.PackageId,
            PackageName = package.Manifest.PackageName,
            PackageFlowHash = package.Manifest.FlowHash,
            ExecutionFlowHash = snapshot.FlowHash,
            FlowHash = snapshot.FlowHash,
            ProjectRevision = snapshot.PersistenceRevision,
            DecisionConfigurationHash = snapshot.DecisionConfigurationHash,
            ExecutionSnapshotId = snapshot.SnapshotId,
            ExecutionRunMode = snapshot.RunMode.ToString(),
            ImageId = imageId,
            SourceImagePath = imagePath,
            Outcome = outcome,
            InspectionStatus = inspectionStatus,
            ExecutionOutcome = canonicalOutcome.Execution,
            DecisionOutcome = canonicalOutcome.Decision,
            DecisionSource = canonicalOutcome.DecisionSource,
            ReasonCode = canonicalOutcome.ReasonCode,
            ExecutionTimeMs = flowResult.ExecutionTimeMs,
            DiagnosticCode = diagnosticCode,
            DiagnosticMessage = diagnosticMessage,
            HasJudgmentSignal = canonicalOutcome.HasJudgmentSignal,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PrimaryOutputs = BuildPrimaryOutputs(outputData),
            OutputImageBytes = TryExtractOutputImage(outputData) ?? sourceImageBytes,
            SourceImageBytes = sourceImageBytes
        };
    }

    public RuntimeNormalizedResult CreateValidationFailure(
        RuntimePackage package,
        ExecutionSnapshot snapshot,
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
            snapshot,
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
        ExecutionSnapshot snapshot,
        string runId,
        string imageId,
        string? imagePath,
        byte[]? sourceImageBytes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return CreateFixedResult(
            package,
            snapshot,
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
        ExecutionSnapshot snapshot,
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
            snapshot,
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
        ExecutionSnapshot snapshot,
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
            PackageFlowHash = package.Manifest.FlowHash,
            ExecutionFlowHash = snapshot.FlowHash,
            FlowHash = snapshot.FlowHash,
            ProjectRevision = snapshot.PersistenceRevision,
            DecisionConfigurationHash = snapshot.DecisionConfigurationHash,
            ExecutionSnapshotId = snapshot.SnapshotId,
            ExecutionRunMode = snapshot.RunMode.ToString(),
            ImageId = imageId,
            SourceImagePath = imagePath,
            Outcome = outcome,
            InspectionStatus = inspectionStatus,
            ExecutionOutcome = outcome switch
            {
                RuntimeRunOutcome.Canceled => ExecutionOutcome.Cancelled,
                RuntimeRunOutcome.Error => ExecutionOutcome.Failed,
                _ => ExecutionOutcome.Succeeded
            },
            DecisionOutcome = inspectionStatus switch
            {
                InspectionStatus.OK => DecisionOutcome.Ok,
                InspectionStatus.NG => DecisionOutcome.Ng,
                _ => DecisionOutcome.Undetermined
            },
            DecisionSource = "RuntimeHost",
            ReasonCode = diagnosticCode,
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

    private static RuntimeRunOutcome ResolveOutcome(InspectionOutcome outcome)
    {
        if (outcome.Execution == ExecutionOutcome.Cancelled)
        {
            return RuntimeRunOutcome.Canceled;
        }

        if (outcome.Execution is ExecutionOutcome.Failed or ExecutionOutcome.TimedOut ||
            outcome.Decision == DecisionOutcome.Invalid)
        {
            return RuntimeRunOutcome.Error;
        }

        return outcome.Decision switch
        {
            DecisionOutcome.Ok => RuntimeRunOutcome.Ok,
            DecisionOutcome.Ng => RuntimeRunOutcome.Ng,
            _ => RuntimeRunOutcome.Undetermined
        };
    }

    private static Dictionary<string, object?> BuildPrimaryOutputs(Dictionary<string, object> outputData)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in outputData.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (ShouldSkipPrimaryOutput(key))
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

    private static bool ShouldSkipPrimaryOutput(string key)
    {
        return key.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("Scene", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("VisualScene", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("OutputScene", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("ArtifactPayload", StringComparison.OrdinalIgnoreCase) ||
               key.Equals(RuntimeAssetInputKeys.RuntimeAssetContext, StringComparison.OrdinalIgnoreCase);
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

internal interface IRuntimeResultRecordWriter : IAsyncDisposable
{
    int DroppedCount { get; }

    ValueTask<bool> EnqueueAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken);
}

internal interface IRuntimeImageWriter : IAsyncDisposable
{
    int DroppedCount { get; }

    bool ShouldPersist(RuntimeNormalizedResult result);

    string PlanPath(RuntimeNormalizedResult result);

    ValueTask<bool> EnqueueAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken);
}

internal sealed class RuntimePreparedWriters : IAsyncDisposable
{
    public RuntimePreparedWriters(IRuntimeResultRecordWriter resultWriter, IRuntimeImageWriter imageWriter)
    {
        ResultWriter = resultWriter;
        ImageWriter = imageWriter;
    }

    public IRuntimeResultRecordWriter ResultWriter { get; }

    public IRuntimeImageWriter ImageWriter { get; }

    public async ValueTask DisposeAsync()
    {
        Exception? firstException = null;

        try
        {
            await ResultWriter.DisposeAsync();
        }
        catch (Exception ex)
        {
            firstException = ex;
        }

        try
        {
            await ImageWriter.DisposeAsync();
        }
        catch (Exception ex) when (firstException == null)
        {
            firstException = ex;
        }

        if (firstException != null)
        {
            throw firstException;
        }
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
            PackageFlowHash = source.PackageFlowHash,
            ExecutionFlowHash = source.ExecutionFlowHash,
            FlowHash = source.FlowHash,
            ProjectRevision = source.ProjectRevision,
            DecisionConfigurationHash = source.DecisionConfigurationHash,
            ExecutionSnapshotId = source.ExecutionSnapshotId,
            ExecutionRunMode = source.ExecutionRunMode,
            ImageId = source.ImageId,
            SourceImagePath = source.SourceImagePath,
            Outcome = source.Outcome,
            InspectionStatus = source.InspectionStatus,
            ExecutionOutcome = source.ExecutionOutcome,
            DecisionOutcome = source.DecisionOutcome,
            DecisionSource = source.DecisionSource,
            ReasonCode = source.ReasonCode,
            ExecutionTimeMs = source.ExecutionTimeMs,
            DiagnosticCode = source.DiagnosticCode,
            DiagnosticMessage = source.DiagnosticMessage,
            HasJudgmentSignal = source.HasJudgmentSignal,
            SavedImagePath = source.SavedImagePath,
            StartedAtUtc = source.StartedAtUtc,
            CompletedAtUtc = source.CompletedAtUtc,
            PrimaryOutputs = ClonePrimaryOutputs(source.PrimaryOutputs),
            GlobalVariableSchemaHash = source.GlobalVariableSchemaHash,
            PublicGlobalVariables = ClonePrimaryOutputs(source.PublicGlobalVariables),
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

internal sealed class RuntimeResultRecordWriter : IRuntimeResultRecordWriter
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
            _disposeCts.Cancel();
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

internal sealed class RuntimeImageWriter : IRuntimeImageWriter
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
            _disposeCts.Cancel();
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
