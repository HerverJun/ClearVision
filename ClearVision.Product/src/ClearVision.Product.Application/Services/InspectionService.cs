// InspectionService.cs
// 检测应用服务
// 职责：API 门面，协调单次检测和实时检测
// 生命周期：Scoped（无状态，不保存运行时状态）
// 作者：蘅芜君 + 架构修复方案 v2

using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

// 【架构修复 v2】IInspectionWorker 从 Infrastructure 移到 Core

namespace ClearVision.Product.Application.Services;

/// <summary>
/// 检测应用服务
/// 【架构修复 v2】移除实例字段，改为纯门面模式
/// 实时检测状态由 IInspectionRuntimeCoordinator（Singleton）管理
/// 实时检测执行由 IInspectionWorker（HostedService）执行
/// </summary>
public class InspectionService : IInspectionService
{
    private const string TraceabilityFieldName = "Traceability";
    private static readonly TimeSpan RealtimeStartRollbackTimeout = TimeSpan.FromSeconds(5);

    private readonly IInspectionResultRepository _resultRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IFlowExecutionService _flowExecutionService;
    private readonly IImageAcquisitionService _imageAcquisitionService;
    private readonly IConfigurationService _configurationService;
    private readonly IInspectionRuntimeCoordinator _coordinator;
    private readonly IInspectionWorker _worker;
    private readonly IImageCacheRepository _imageCacheRepository;
    private readonly IAnalysisDataBuilder _analysisDataBuilder;
    private readonly IProjectFlowStorage _flowStorage;
    private readonly IInspectionImagePersistenceService? _imagePersistenceService;
    private readonly IInspectionEvidenceManifestService? _evidenceManifestService;
    private readonly ProjectVariableSessionRegistry? _projectVariableSessions;
    private readonly ProjectSaveCoordinator? _projectSaveCoordinator;
    private readonly IExecutionAdmissionService _executionAdmissionService;
    private readonly IWorkflowArtifactAdmissionGate? _workflowArtifactAdmissionGate;
    private readonly ILogger<InspectionService> _logger;
    private static readonly JsonSerializerOptions FlowJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public InspectionService(
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IFlowExecutionService flowExecutionService,
        IImageAcquisitionService imageAcquisitionService,
        IConfigurationService configurationService,
        IInspectionRuntimeCoordinator coordinator,
        IInspectionWorker worker,
        IImageCacheRepository imageCacheRepository,
        IAnalysisDataBuilder analysisDataBuilder,
        IProjectFlowStorage flowStorage,
        ILogger<InspectionService> logger,
        IInspectionImagePersistenceService? imagePersistenceService = null,
        ProjectVariableSessionRegistry? projectVariableSessions = null,
        ProjectSaveCoordinator? projectSaveCoordinator = null,
        IInspectionEvidenceManifestService? evidenceManifestService = null,
        IExecutionAdmissionService? executionAdmissionService = null,
        IWorkflowArtifactAdmissionGate? workflowArtifactAdmissionGate = null)
    {
        _resultRepository = resultRepository;
        _projectRepository = projectRepository;
        _flowExecutionService = flowExecutionService;


        _imageAcquisitionService = imageAcquisitionService;
        _configurationService = configurationService;
        _coordinator = coordinator;
        _worker = worker;
        _imageCacheRepository = imageCacheRepository;
        _analysisDataBuilder = analysisDataBuilder;
        _flowStorage = flowStorage;
        _imagePersistenceService = imagePersistenceService;
        _evidenceManifestService = evidenceManifestService;
        _projectVariableSessions = projectVariableSessions;
        _projectSaveCoordinator = projectSaveCoordinator;
        _executionAdmissionService = executionAdmissionService ?? new ExecutionAdmissionService(
            projectRepository,
            coordinator,
            flowExecutionService as IFlowDefinitionValidator);
        _workflowArtifactAdmissionGate = workflowArtifactAdmissionGate;
        _logger = logger;
    }

    public InspectionService(
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IFlowExecutionService flowExecutionService,
        IImageAcquisitionService imageAcquisitionService,
        IConfigurationService configurationService,
        IInspectionRuntimeCoordinator coordinator,
        IInspectionWorker worker,
        IImageCacheRepository imageCacheRepository,
        ILogger<InspectionService> logger)
        : this(
            resultRepository,
            projectRepository,
            flowExecutionService,
            imageAcquisitionService,
            configurationService,
            coordinator,
            worker,
            imageCacheRepository,
            new AnalysisDataBuilder(),
            new NoOpProjectFlowStorage(),
            logger)
    {
    }

    public InspectionService(
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IFlowExecutionService flowExecutionService,
        IImageAcquisitionService imageAcquisitionService,
        IConfigurationService configurationService,
        IInspectionRuntimeCoordinator coordinator,
        IInspectionWorker worker,
        IAnalysisDataBuilder analysisDataBuilder,
        ILogger<InspectionService> logger)
        : this(
            resultRepository,
            projectRepository,
            flowExecutionService,
            imageAcquisitionService,
            configurationService,
            coordinator,
            worker,
            new NoOpImageCacheRepository(),
            analysisDataBuilder,
            new NoOpProjectFlowStorage(),
            logger)
    {
    }

    public InspectionService(
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IFlowExecutionService flowExecutionService,
        IImageAcquisitionService imageAcquisitionService,
        IConfigurationService configurationService,
        IInspectionRuntimeCoordinator coordinator,
        IInspectionWorker worker,
        ILogger<InspectionService> logger)
        : this(
            resultRepository,
            projectRepository,
            flowExecutionService,
            imageAcquisitionService,
            configurationService,
            coordinator,
            worker,
            new NoOpImageCacheRepository(),
            new AnalysisDataBuilder(),
            new NoOpProjectFlowStorage(),
            logger)
    {
    }

    #region 单次检测

    public async Task<StudioInspectionRunAdmission> AdmitPersistedStudioRunAsync(
        Guid projectId,
        long expectedPersistenceRevision,
        Guid clientSnapshotId,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, _) = await ResolveExecutionFlowAsync(
            projectId,
            flow: null,
            ExecutionAdmissionSurface.StudioInspectionRun,
            cancellationToken,
            clientSnapshotId);
        EnsurePersistedStudioRunIdentity(snapshot, expectedPersistenceRevision, null, null);
        return new StudioInspectionRunAdmission(
            snapshot.ProjectId,
            snapshot.SnapshotId,
            snapshot.PersistenceRevision,
            snapshot.FlowHash,
            snapshot.DecisionConfigurationHash);
    }

    public async Task<InspectionResult> ExecutePersistedStudioRunAsync(
        Guid projectId,
        long expectedPersistenceRevision,
        Guid clientSnapshotId,
        string expectedCanonicalFlowHash,
        string expectedDecisionConfigurationHash,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, globalVariables) = await ResolveExecutionFlowAsync(
            projectId,
            flow: null,
            ExecutionAdmissionSurface.StudioInspectionRun,
            CancellationToken.None,
            clientSnapshotId);
        EnsurePersistedStudioRunIdentity(
            snapshot,
            expectedPersistenceRevision,
            expectedCanonicalFlowHash,
            expectedDecisionConfigurationHash);

        try
        {
            // A disconnected HTTP client must not cancel the authoritative
            // formal execution. Stop uses the coordinator-owned token instead.
            return await ExecuteSingleWithCoordinatorAsync(
                snapshot,
                (sessionId, executionCancellationToken) => ExecuteSingleResolvedCoreAsync(
                    projectId,
                    imageData: null,
                    snapshot,
                    globalVariables,
                    sessionId,
                    executionCancellationToken),
                CancellationToken.None,
                useRuntimeCancellationToken: true,
                sessionType: RuntimeSessionType.WorkspaceFormalRun);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an authoritative formal terminal outcome, not
            // an HTTP transport failure. Persist it so reconcile can remain
            // result-repository backed even after coordinator cleanup.
            return await PersistCancelledStudioRunAsync(snapshot);
        }
    }

    public async Task<StudioInspectionRunReconciliation> StopPersistedStudioRunAsync(
        StudioInspectionRunIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeStudioRunIdentity(identity, out var normalized, out var invalid))
        {
            return invalid!;
        }

        var state = _coordinator.GetState(normalized.ProjectId);
        if (state != null &&
            (state.SessionType != RuntimeSessionType.WorkspaceFormalRun || !MatchesRuntimeIdentity(state, normalized)))
        {
            return IdentityMismatch(normalized, "RUN_IDENTITY_MISMATCH", "The active runtime session does not match the requested formal run.");
        }

        if (state?.Status is RuntimeStatus.Starting or RuntimeStatus.Running)
        {
            await _coordinator.TryStopAsync(
                normalized.ProjectId,
                state.SessionId,
                RuntimeSessionType.WorkspaceFormalRun,
                CancellationToken.None);
        }

        return await ReconcilePersistedStudioRunAsync(normalized, cancellationToken);
    }

    public async Task<StudioInspectionRunReconciliation> ReconcilePersistedStudioRunAsync(
        StudioInspectionRunIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeStudioRunIdentity(identity, out var normalized, out var invalid))
        {
            return invalid!;
        }

        var storedResult = await _resultRepository.FindByExecutionSnapshotIdAsync(
            normalized.ProjectId,
            normalized.ClientSnapshotId);
        if (storedResult != null)
        {
            return MatchesResultIdentity(storedResult, normalized)
                ? ReconcileStoredResult(normalized, storedResult)
                : IdentityMismatch(normalized, "RUN_RESULT_IDENTITY_MISMATCH", "The persisted result does not match the requested formal run identity.");
        }

        var runtimeState = _coordinator.GetState(normalized.ProjectId);
        if (runtimeState != null)
        {
            if (runtimeState.SessionType != RuntimeSessionType.WorkspaceFormalRun ||
                !MatchesRuntimeIdentity(runtimeState, normalized))
            {
                return IdentityMismatch(normalized, "RUN_IDENTITY_MISMATCH", "The active runtime session does not match the requested formal run.");
            }

            return runtimeState.Status switch
            {
                RuntimeStatus.Starting or RuntimeStatus.Running =>
                    RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.StillRunning,
                        "RUN_STILL_RUNNING", "The authoritative formal run is still running."),
                RuntimeStatus.Stopping =>
                    RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.CancelRequested,
                        "RUN_CANCEL_REQUESTED", "Cancellation was requested; the authoritative terminal outcome is not stored yet."),
                RuntimeStatus.Faulted =>
                    RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.Failed,
                        "RUN_RUNTIME_FAULT", runtimeState.ErrorMessage ?? "The authoritative runtime session failed."),
                _ => RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.ResultNotFound,
                    "RUN_RESULT_NOT_FOUND", "The runtime session ended without a matching persisted formal result.")
            };
        }

        try
        {
            var current = await AdmitPersistedStudioRunAsync(
                normalized.ProjectId,
                normalized.PersistenceRevision,
                normalized.ClientSnapshotId,
                cancellationToken);
            if (!string.Equals(current.CanonicalFlowHash, normalized.CanonicalFlowHash, StringComparison.Ordinal) ||
                !string.Equals(current.DecisionConfigurationHash, normalized.DecisionConfigurationHash, StringComparison.Ordinal))
            {
                return IdentityMismatch(normalized, "RUN_IDENTITY_MISMATCH", "The current persisted Project identity differs from the formal run identity.");
            }
        }
        catch (StudioInspectionRunIdentityException ex)
        {
            return IdentityMismatch(normalized, ex.Code, ex.Message);
        }
        catch (ProjectNotFoundException)
        {
            return RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.ResultNotFound,
                "RUN_PROJECT_NOT_FOUND", "The Project or its formal result was not found.");
        }
        catch (ExecutionAdmissionService.ExecutionAdmissionRejectedException ex)
        {
            return RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.ResultNotFound,
                ex.Admission.Code, "No matching persisted formal result is available.");
        }

        return RuntimeReconciliation(normalized, StudioInspectionRunReconciliationStatus.ResultNotFound,
            "RUN_RESULT_NOT_FOUND", "No formal result matches the supplied snapshot identity.");
    }

    public async Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        byte[] imageData,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(projectId, imageData, null, cancellationToken);
    }

    public async Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        byte[] imageData,
        OperatorFlow? flow,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, globalVariables) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            HasExecutableFlow(flow)
                ? ExecutionAdmissionSurface.StudioInspectionRun
                : ExecutionAdmissionSurface.StoredProjectExecution,
            cancellationToken);
        return await ExecuteSingleWithCoordinatorAsync(
            snapshot,
            (sessionId, executionCancellationToken) => ExecuteSingleResolvedCoreAsync(
                projectId,
                imageData,
                snapshot,
                globalVariables,
                sessionId,
                executionCancellationToken),
            cancellationToken);
#if false
        var actualFlow = await ResolveExecutionFlowAsync(projectId, flow);
        if (flow != null)
        {
            actualFlow = flow;
            _logger.LogInformation("[InspectionService] 使用前端提供的流程数据执行检测 (算子数: {OperatorCount})", flow.Operators?.Count ?? 0);
        }
        else
        {
            var project = await _projectRepository.GetWithFlowAsync(projectId);
            if (project == null)
                throw new ProjectNotFoundException(projectId);
            actualFlow = project.Flow;
        }

        var result = new InspectionResult(projectId);

        try
        {
            var flowResult = await _flowExecutionService.ExecuteFlowAsync(
                actualFlow,
                new Dictionary<string, object> { { "Image", imageData } });

            InspectionStatus status;
            if (!flowResult.IsSuccess)
            {
                status = InspectionStatus.Error;
                _logger.LogWarning("[InspectionService] 流程执行失败: {ErrorMessage}", flowResult.ErrorMessage);
            }
            else
            {
                status = DetermineStatusFromFlowOutput(flowResult.OutputData);
                _logger.LogInformation("[InspectionService] 判定结果: {Status}", status);
            }

            result.SetResult(status, flowResult.ExecutionTimeMs, null, flowResult.ErrorMessage);

            var inspectionImage = ResolveInspectionImage(flowResult);
            if (inspectionImage != null)
            {
                result.SetOutputImage(inspectionImage);
            }

            // 提取缺陷列表
            if (flowResult.OutputData?.TryGetValue("Defects", out var defectsObj) == true
                && defectsObj is IList defectsList)
            {
                foreach (var item in defectsList)
                {
                    if (item is Dictionary<string, object> defectDict)
                    {
                        var defect = new Defect(
                            result.Id,
                            DefectType.Other,
                            Convert.ToDouble(defectDict.GetValueOrDefault("X", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Y", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Width", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Height", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Confidence", 0.0)),
                            defectDict.GetValueOrDefault("ClassName", "unknown")?.ToString() ?? "unknown"
                        );
                        result.AddDefect(defect);
                    }
                }
            }

            var analysisData = _analysisDataBuilder.Build(actualFlow, flowResult, status);
            AnalysisPayloadSerialization.TrySetOutputDataJson(result, flowResult.OutputData, _logger);
            AnalysisPayloadSerialization.TrySetAnalysisDataJson(result, analysisData, _logger);
            await PersistResultImageAsync(result, cancellationToken);
            await CacheResultImageAsync(result);
            await _resultRepository.AddAsync(InspectionResultPersistenceSnapshot.WithoutOutputImage(result));
            await CaptureEvidenceManifestAsync(result, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InspectionService] 检测异常: {ErrorMessage}", ex.Message);
            result.MarkAsError(ex.Message);
            await _resultRepository.AddAsync(result);
            return result;
        }
#endif
    }

    public async Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        string cameraId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(projectId, cameraId, null, cancellationToken);
#if false
        try
        {
            var imageDto = await _imageAcquisitionService.AcquireFromCameraAsync(cameraId);

            if (string.IsNullOrEmpty(imageDto.DataBase64))
            {
                throw new Exception($"相机 {cameraId} 采集的图像数据为空");
            }

            var imageData = Convert.FromBase64String(imageDto.DataBase64);
            return await ExecuteSingleAsync(projectId, imageData);
        }
        catch (Exception ex)
        {
            var result = new InspectionResult(projectId);
            result.MarkAsError($"相机采集或检测失败: {ex.Message}");
            await _resultRepository.AddAsync(result);
            throw;
        }
#endif
    }

    public async Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        string cameraId,
        OperatorFlow? flow,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, globalVariables) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            HasExecutableFlow(flow)
                ? ExecutionAdmissionSurface.StudioInspectionRun
                : ExecutionAdmissionSurface.StoredProjectExecution,
            cancellationToken);
        return await ExecuteSingleWithCoordinatorAsync(
            snapshot,
            (sessionId, executionCancellationToken) => ExecuteSingleFromCameraCoreAsync(
                projectId,
                cameraId,
                snapshot,
                globalVariables,
                sessionId,
                executionCancellationToken),
            cancellationToken);
    }

    #endregion

    #region 实时检测（门面模式）

    /// <summary>
    /// 【架构修复 v2】改为门面模式：委托给 Coordinator 和 Worker
    /// </summary>
    public async Task StartRealtimeInspectionAsync(
        Guid projectId,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        var (snapshot, _) = await ResolveExecutionFlowAsync(
            projectId,
            flow: null,
            ExecutionAdmissionSurface.StoredProjectExecution,
            cancellationToken);
        await StartRealtimeInspectionFlowCoreAsync(snapshot, cameraId, cancellationToken, onResultReady);
    }

    public async Task<StudioInspectionRunAdmission> StartPersistedRealtimeInspectionAsync(
        StudioInspectionRunIdentity identity,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var (snapshot, _) = await ResolveExecutionFlowAsync(
            identity.ProjectId,
            flow: null,
            ExecutionAdmissionSurface.StudioInspectionRun,
            cancellationToken,
            identity.ClientSnapshotId);
        EnsurePersistedStudioRunIdentity(
            snapshot,
            identity.PersistenceRevision,
            identity.CanonicalFlowHash,
            identity.DecisionConfigurationHash);
        await StartRealtimeInspectionFlowCoreAsync(snapshot, cameraId, cancellationToken, onResultReady);
        var runtimeState = _coordinator.GetState(snapshot.ProjectId);
        if (runtimeState == null || runtimeState.ExecutionSnapshotId != snapshot.SnapshotId ||
            runtimeState.SessionType != RuntimeSessionType.ContinuousInspection)
        {
            throw new StudioInspectionRunIdentityException(
                "RUN_IDENTITY_MISMATCH",
                "Continuous inspection started without a matching authoritative runtime identity.");
        }
        return new StudioInspectionRunAdmission(
            snapshot.ProjectId,
            snapshot.SnapshotId,
            snapshot.PersistenceRevision,
            snapshot.FlowHash,
            snapshot.DecisionConfigurationHash,
            runtimeState.SessionId,
            runtimeState.SessionType);
    }

    /// <summary>
    /// 【架构修复 v2】实时检测启动入口
    /// 1. 调用 Coordinator 注册会话
    /// 2. 调用 Worker 启动后台任务
    /// </summary>
    public async Task StartRealtimeInspectionFlowAsync(
        Guid projectId,
        OperatorFlow flow,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        // 检测页“运行流程”（连续/实时）属于正式运行：允许流程声明的真实 I/O，
        // 仅保留项目存在/激活等非 I/O 安全校验（运行中防并发由 Coordinator 负责）。
        var (snapshot, _) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.StudioInspectionRun,
            cancellationToken);
        await StartRealtimeInspectionFlowCoreAsync(
            snapshot,
            cameraId,
            cancellationToken,
            onResultReady);
    }

    private async Task StartRealtimeInspectionFlowCoreAsync(
        ExecutionSnapshot snapshot,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        var projectId = snapshot.ProjectId;
        var flow = snapshot.CreateExecutionFlow();
        var sessionId = Guid.NewGuid();
        var effectiveCameraId = ImageAcquisitionFlowAnalyzer.ShouldBypassExternalCameraInput(flow)
            ? null
            : cameraId;

        _logger.LogInformation(
            "[InspectionService] 请求启动实时检测: ProjectId={ProjectId}, SessionId={SessionId}, CameraId={CameraId}",
            projectId, sessionId, effectiveCameraId ?? "(流程内)");

        // 步骤 1：注册会话（Coordinator 保证原子性）
        var startResult = await _coordinator.TryStartAsync(
            snapshot,
            sessionId,
            RuntimeSessionType.ContinuousInspection,
            cancellationToken);

        switch (startResult)
        {
            case StartResult.AlreadyRunning:
                _logger.LogWarning("[InspectionService] 实时检测已在运行: {ProjectId}", projectId);
                throw new InvalidOperationException("实时检测已在运行");

            case StartResult.MutationInProgress:
                _logger.LogWarning("[InspectionService] 项目正在配置变更，无法启动: {ProjectId}", projectId);
                throw new InvalidOperationException("项目正在配置变更，请稍后重试");

            case StartResult.ShutdownInProgress:
                _logger.LogWarning("[InspectionService] 系统正在关机，无法启动: {ProjectId}", projectId);
                throw new InvalidOperationException("系统正在关机，请稍后重试");
        }

        // 步骤 2：启动 Worker（不等待完成，Fire-and-forget）
        var workerStarted = await _worker.TryStartRunAsync(sessionId, snapshot, effectiveCameraId);

        if (!workerStarted)
        {
            // Worker 启动失败，回滚 Coordinator 状态
            _logger.LogError("[InspectionService] Worker 启动失败，回滚状态: {ProjectId}", projectId);
            await RollbackRealtimeStartAsync(projectId, sessionId);
            throw new InvalidOperationException("实时检测启动失败，请重试");
        }

        _logger.LogInformation(
            "[InspectionService] 实时检测已启动: ProjectId={ProjectId}, SessionId={SessionId}",
            projectId, sessionId);

        // 注意：不再使用 Task.Run + onResultReady 回调
        // 结果通过事件总线推送（Phase 2 实现）
    }

    private async Task RollbackRealtimeStartAsync(Guid projectId, Guid sessionId)
    {
        try
        {
            using var rollbackCts = new CancellationTokenSource(RealtimeStartRollbackTimeout);
            var stopRequested = await _coordinator.TryStopAsync(
                projectId,
                sessionId,
                RuntimeSessionType.ContinuousInspection,
                rollbackCts.Token);
            if (stopRequested)
            {
                _coordinator.MarkAsStopped(projectId, sessionId);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(
                ex,
                "[InspectionService] Worker 启动失败后的状态回滚超时: {ProjectId}, SessionId={SessionId}",
                projectId,
                sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[InspectionService] Worker 启动失败后的状态回滚异常: {ProjectId}, SessionId={SessionId}",
                projectId,
                sessionId);
        }
    }

    /// <summary>
    /// 【架构修复 v2】实时检测停止入口
    /// 委托给 Coordinator 处理
    /// </summary>
    public async Task StopRealtimeInspectionAsync(Guid projectId)
    {
        _logger.LogInformation("[InspectionService] 请求停止实时检测: {ProjectId}", projectId);

        var runtimeState = _coordinator.GetState(projectId);
        var stopped = runtimeState != null && await _coordinator.TryStopAsync(
            projectId,
            runtimeState.SessionId,
            RuntimeSessionType.ContinuousInspection,
            CancellationToken.None);

        if (stopped)
        {
            var workerExited = await _worker.WaitForRunExitAsync(
                projectId,
                TimeSpan.FromSeconds(3),
                CancellationToken.None);

            if (!workerExited)
            {
                _logger.LogError("[InspectionService] Stop timeout, worker is still running: {ProjectId}", projectId);
                throw new InvalidOperationException("实时检测停止超时，后台任务仍未退出。");
            }

            var stateReleased = await WaitForStateReleaseAsync(projectId, TimeSpan.FromSeconds(3));
            if (!stateReleased)
            {
                _logger.LogError("[InspectionService] Stop completed but runtime state was not released: {ProjectId}", projectId);
                throw new InvalidOperationException("实时检测停止后状态仍未释放。");
            }
            _logger.LogInformation("[InspectionService] 实时检测停止请求已发送: {ProjectId}", projectId);
        }
        else
        {
            _logger.LogWarning("[InspectionService] 未找到运行中的实时检测: {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// 获取实时检测状态
    /// </summary>
    public RuntimeState? GetRealtimeState(Guid projectId)
    {
        return _coordinator.GetState(projectId);
    }

    private async Task<bool> WaitForStateReleaseAsync(Guid projectId, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt <= timeout)
        {
            if (_coordinator.GetState(projectId) == null)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return _coordinator.GetState(projectId) == null;
    }

    #endregion

    #region 查询方法

    public async Task<InspectionHistoryPage> GetInspectionHistoryAsync(
        Guid projectId,
        DateTime? startTime,
        DateTime? endTime,
        string? status,
        string? defectType,
        int pageIndex,
        int pageSize,
        string? flowVersionHash = null)
    {
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId);
        await using (access)
        {
            if (!await IsActiveProjectAsync(projectId))
            {
                return new InspectionHistoryPage
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize
                };
            }

            return await _resultRepository.GetHistoryPageAsync(
                projectId,
                startTime,
                endTime,
                status,
                defectType,
                pageIndex,
                pageSize,
                flowVersionHash);
        }
    }

    public async Task<InspectionHistoryDetail?> GetInspectionHistoryDetailAsync(Guid projectId, Guid resultId)
    {
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId);
        await using (access)
        {
            if (!await IsActiveProjectAsync(projectId))
            {
                return null;
            }

            return await _resultRepository.GetHistoryDetailAsync(projectId, resultId);
        }
    }

    public async Task<InspectionHistoryComparison?> CompareInspectionHistoryAsync(Guid projectId, Guid leftId, Guid rightId)
    {
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId);
        await using (access)
        {
            if (!await IsActiveProjectAsync(projectId))
            {
                return null;
            }

            var left = await _resultRepository.GetHistoryDetailAsync(projectId, leftId);
            var right = await _resultRepository.GetHistoryDetailAsync(projectId, rightId);
            return left == null || right == null
                ? null
                : InspectionHistoryComparisonBuilder.Build(left, right);
        }
    }

    public async Task<InspectionPreviousSuccessReference?> FindPreviousSuccessfulInspectionAsync(
        Guid projectId,
        Guid resultId,
        int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId);
        await using (access)
        {
            if (!await IsActiveProjectAsync(projectId))
            {
                return null;
            }

            var current = await _resultRepository.GetHistoryDetailAsync(projectId, resultId);
            if (current == null)
            {
                return null;
            }

            var currentOutcome = current.ExecutionOutcome.HasValue && current.DecisionOutcome.HasValue
                ? new InspectionOutcome(
                    current.ExecutionOutcome.Value,
                    current.DecisionOutcome.Value,
                    current.DecisionSource,
                    current.ReasonCode,
                    current.ErrorMessage,
                    current.HasJudgmentSignal ?? false)
                : LegacyInspectionStatusProjection.FromLegacy(current.Status);
            if (InspectionOutcomeClassifier.Classify(currentOutcome) == CanonicalInspectionOutcomeKind.Ok)
            {
                return InspectionHistoryComparisonBuilder.BuildPreviousSuccessReference(
                    current,
                    reference: null,
                    limit,
                    isFlowVersionFallback: false);
            }

            InspectionHistoryDetail? reference = null;
            if (!string.IsNullOrWhiteSpace(current.FlowVersionHash))
            {
                reference = await _resultRepository.FindPreviousSuccessfulInspectionAsync(
                    projectId,
                    current.InspectionTime,
                    current.FlowVersionHash,
                    limit);
            }

            if (reference != null)
            {
                return InspectionHistoryComparisonBuilder.BuildPreviousSuccessReference(
                    current,
                    reference,
                    limit,
                    isFlowVersionFallback: false);
            }

            reference = await _resultRepository.FindPreviousSuccessfulInspectionAsync(
                projectId,
                current.InspectionTime,
                flowVersionHash: null,
                limit);

            var isFallback = reference != null &&
                !string.Equals(reference.FlowVersionHash, current.FlowVersionHash, StringComparison.Ordinal);
            return InspectionHistoryComparisonBuilder.BuildPreviousSuccessReference(
                current,
                reference,
                limit,
                isFallback);
        }
    }

    public async Task<InspectionStatistics> GetStatisticsAsync(
        Guid projectId,
        DateTime? startTime,
        DateTime? endTime,
        string? status,
        string? defectType)
    {
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId);
        await using (access)
        {
            return await IsActiveProjectAsync(projectId)
                ? await _resultRepository.GetStatisticsAsync(projectId, startTime, endTime, status, defectType)
                : new InspectionStatistics();
        }
    }

    #endregion

    #region 辅助方法

    private async Task<InspectionResult> ExecuteSingleWithCoordinatorAsync(
        ExecutionSnapshot snapshot,
        Func<Guid, CancellationToken, Task<InspectionResult>> executeAsync,
        CancellationToken cancellationToken,
        bool useRuntimeCancellationToken = false,
        RuntimeSessionType sessionType = RuntimeSessionType.LegacyRealtime)
    {
        var projectId = snapshot.ProjectId;
        var sessionId = Guid.NewGuid();
        var startResult = await _coordinator.TryStartAsync(snapshot, sessionId, sessionType, cancellationToken);
        if (startResult != StartResult.Success)
        {
            throw new InvalidOperationException(startResult is StartResult.AlreadyRunning or StartResult.MutationInProgress
                ? "Project is currently running."
                : "Runtime coordinator is shutting down.");
        }

        try
        {
            _coordinator.UpdateSessionStatus(projectId, sessionId, RuntimeStatus.Running);
            var executionCancellationToken = useRuntimeCancellationToken
                ? _coordinator.GetCancellationToken(projectId)
                : cancellationToken;
            return await executeAsync(sessionId, executionCancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            useRuntimeCancellationToken && _coordinator.GetCancellationToken(projectId).IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _coordinator.MarkAsFaulted(projectId, sessionId, ex.Message);
            throw;
        }
        finally
        {
            _coordinator.MarkAsStopped(projectId, sessionId);
        }
    }

    public async Task StopPersistedRealtimeInspectionAsync(StudioInspectionRunIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var state = _coordinator.GetState(identity.ProjectId);
        if (state == null || state.SessionType != RuntimeSessionType.ContinuousInspection ||
            !MatchesRuntimeIdentity(state, identity))
        {
            throw new StudioInspectionRunIdentityException(
                "RUN_IDENTITY_MISMATCH",
                "The active runtime does not match the continuous inspection identity.");
        }

        var stopped = await _coordinator.TryStopAsync(
            identity.ProjectId,
            state.SessionId,
            RuntimeSessionType.ContinuousInspection,
            CancellationToken.None);
        if (!stopped)
        {
            throw new StudioInspectionRunIdentityException(
                "RUN_IDENTITY_MISMATCH",
                "The active runtime no longer matches the continuous inspection identity.");
        }

        var workerExited = await _worker.WaitForRunExitAsync(
            identity.ProjectId,
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        if (!workerExited || !await WaitForStateReleaseAsync(identity.ProjectId, TimeSpan.FromSeconds(3)))
        {
            throw new InvalidOperationException("Continuous inspection stop was not authoritatively confirmed.");
        }
    }

    private async Task<InspectionResult> PersistCancelledStudioRunAsync(ExecutionSnapshot snapshot)
    {
        var runtimeState = _coordinator.GetState(snapshot.ProjectId);
        var result = new InspectionResult(snapshot.ProjectId);
        result.SetOutcome(new InspectionOutcome(
            ExecutionOutcome.Cancelled,
            DecisionOutcome.NotApplicable,
            "StudioFormalRun",
            "Cancelled",
            "The formal run was cancelled by the operator."),
            processingTimeMs: 0);
        result.SetExecutionTraceability(snapshot, null, runtimeState?.SessionId);
        await _resultRepository.AddAsync(InspectionResultPersistenceSnapshot.WithoutOutputImage(result));
        return result;
    }

    private static bool TryNormalizeStudioRunIdentity(
        StudioInspectionRunIdentity identity,
        out StudioInspectionRunIdentity normalized,
        out StudioInspectionRunReconciliation? invalid)
    {
        normalized = identity with
        {
            CanonicalFlowHash = identity.CanonicalFlowHash?.Trim() ?? string.Empty,
            DecisionConfigurationHash = identity.DecisionConfigurationHash?.Trim() ?? string.Empty
        };
        invalid = normalized.ProjectId == Guid.Empty ||
                  normalized.ClientSnapshotId == Guid.Empty ||
                  normalized.PersistenceRevision < 0 ||
                  string.IsNullOrWhiteSpace(normalized.CanonicalFlowHash) ||
                  string.IsNullOrWhiteSpace(normalized.DecisionConfigurationHash)
            ? IdentityMismatch(normalized, "RUN_IDENTITY_INVALID", "A complete persisted formal run identity is required.")
            : null;
        return invalid == null;
    }

    private static bool MatchesRuntimeIdentity(RuntimeState state, StudioInspectionRunIdentity identity) =>
        state.ExecutionSnapshotId == identity.ClientSnapshotId &&
        state.ProjectId == identity.ProjectId &&
        state.ProjectRevision == identity.PersistenceRevision &&
        string.Equals(state.FlowHash, identity.CanonicalFlowHash, StringComparison.Ordinal) &&
        string.Equals(state.DecisionConfigurationHash, identity.DecisionConfigurationHash, StringComparison.Ordinal);

    private static bool MatchesResultIdentity(InspectionResult result, StudioInspectionRunIdentity identity) =>
        result.ProjectId == identity.ProjectId &&
        result.ExecutionSnapshotId == identity.ClientSnapshotId &&
        result.ProjectPersistenceRevision == identity.PersistenceRevision &&
        string.Equals(result.FlowVersionHash, identity.CanonicalFlowHash, StringComparison.Ordinal) &&
        string.Equals(result.DecisionConfigurationHash, identity.DecisionConfigurationHash, StringComparison.Ordinal);

    private static StudioInspectionRunReconciliation ReconcileStoredResult(
        StudioInspectionRunIdentity identity,
        InspectionResult result)
    {
        var outcome = result.GetOutcome();
        var status = outcome.Execution switch
        {
            ExecutionOutcome.Cancelled => StudioInspectionRunReconciliationStatus.Cancelled,
            ExecutionOutcome.Succeeded => StudioInspectionRunReconciliationStatus.Succeeded,
            _ => StudioInspectionRunReconciliationStatus.Failed
        };
        var code = status switch
        {
            StudioInspectionRunReconciliationStatus.Cancelled => "RUN_CANCELLED",
            StudioInspectionRunReconciliationStatus.Succeeded => null,
            _ => "RUN_FAILED"
        };
        var message = status switch
        {
            StudioInspectionRunReconciliationStatus.Cancelled => "The formal run was authoritatively cancelled.",
            StudioInspectionRunReconciliationStatus.Succeeded => "The formal run completed and its persisted result was recovered.",
            _ => result.ErrorMessage ?? "The formal run completed with a failed execution outcome."
        };
        return RuntimeReconciliation(identity, status, code, message, result);
    }

    private static StudioInspectionRunReconciliation IdentityMismatch(
        StudioInspectionRunIdentity identity,
        string code,
        string message) =>
        RuntimeReconciliation(identity, StudioInspectionRunReconciliationStatus.IdentityMismatch, code, message);

    private static StudioInspectionRunReconciliation RuntimeReconciliation(
        StudioInspectionRunIdentity identity,
        StudioInspectionRunReconciliationStatus status,
        string? code,
        string message,
        InspectionResult? result = null) =>
        new(
            identity.ProjectId,
            identity.ClientSnapshotId,
            identity.PersistenceRevision,
            identity.CanonicalFlowHash,
            identity.DecisionConfigurationHash,
            status,
            code,
            message,
            result);

    private async Task<InspectionResult> ExecuteSingleCoreAsync(
        Guid projectId,
        byte[]? imageData,
        OperatorFlow? flow,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var (snapshot, globalVariables) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            HasExecutableFlow(flow)
                ? ExecutionAdmissionSurface.StudioInspectionRun
                : ExecutionAdmissionSurface.StoredProjectExecution,
            cancellationToken);

        return await ExecuteSingleResolvedCoreAsync(
            projectId,
            imageData,
            snapshot,
            globalVariables,
            sessionId,
            cancellationToken);
    }

    private async Task<InspectionResult> ExecuteSingleResolvedCoreAsync(
        Guid projectId,
        byte[]? imageData,
        ExecutionSnapshot snapshot,
        ProjectGlobalVariableSchema? globalVariables,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = new InspectionResult(projectId);
        var actualFlow = snapshot.CreateExecutionFlow();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executionInputs = new Dictionary<string, object>();
            if (imageData != null && imageData.Length > 0)
            {
                executionInputs["Image"] = imageData;
            }

            var projectVariables = CreateProjectVariableContext(projectId, actualFlow, globalVariables);
            var flowResult = projectVariables == null
                ? await _flowExecutionService.ExecuteWithSnapshotAsync(
                    snapshot,
                    executionInputs,
                    enableParallel: false,
                    cancellationToken)
                : await _flowExecutionService.ExecuteWithSnapshotAsync(
                    snapshot,
                    executionInputs,
                    projectVariables,
                    enableParallel: false,
                    cancellationToken);

            var outputData = flowResult.OutputData ?? new Dictionary<string, object>();
            flowResult.OutputData = outputData;

            var outcome = InspectionOutcomeResolver.Resolve(flowResult, actualFlow);
            if (outcome.Execution == ExecutionOutcome.Skipped)
            {
                outputData["NoMaterialFrame"] = true;
            }

            InspectionOutcomeResolver.SetDiagnostics(outputData, outcome);
            var status = LegacyInspectionStatusProjection.Project(outcome);

            if (!flowResult.IsSuccess)
            {
                _logger.LogWarning("[InspectionService] 流程执行失败: {ErrorMessage}", flowResult.ErrorMessage);
            }
            else
            {
                _logger.LogInformation("[InspectionService] 判定结果: {Status}", status);
            }

            result.SetOutcome(outcome, flowResult.ExecutionTimeMs);
            result.SetExecutionTraceability(
                snapshot,
                TryResolveCalibrationBundleId(flowResult.OutputData),
                sessionId);

            var inspectionImage = ResolveInspectionImage(flowResult);
            if (inspectionImage != null)
            {
                result.SetOutputImage(inspectionImage);
            }

            if (flowResult.OutputData?.TryGetValue("Defects", out var defectsObj) == true
                && defectsObj is IList defectsList)
            {
                foreach (var item in defectsList)
                {
                    if (item is Dictionary<string, object> defectDict)
                    {
                        var defect = new Defect(
                            result.Id,
                            DefectType.Other,
                            Convert.ToDouble(defectDict.GetValueOrDefault("X", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Y", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Width", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Height", 0.0)),
                            Convert.ToDouble(defectDict.GetValueOrDefault("Confidence", 0.0)),
                            defectDict.GetValueOrDefault("ClassName", "unknown")?.ToString() ?? "unknown");
                        result.AddDefect(defect);
                    }
                }
            }

            var analysisData = _analysisDataBuilder.Build(actualFlow, flowResult, status);
            var outputPayload = EnsureTraceabilityPayload(flowResult.OutputData, result);
            AnalysisPayloadSerialization.TrySetOutputDataJson(result, outputPayload, _logger);
            AnalysisPayloadSerialization.TrySetAnalysisDataJson(result, analysisData, _logger);
            await PersistResultImageAsync(result, cancellationToken);
            await CacheResultImageAsync(result);
            await _resultRepository.AddAsync(InspectionResultPersistenceSnapshot.WithoutOutputImage(result));
            await CaptureEvidenceManifestAsync(result, cancellationToken);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InspectionService] 检测异常: {ErrorMessage}", ex.Message);
            result.MarkAsError(ex.Message);
            result.SetExecutionTraceability(snapshot, null, sessionId);
            await _resultRepository.AddAsync(InspectionResultPersistenceSnapshot.WithoutOutputImage(result));
            await CaptureEvidenceManifestAsync(result, cancellationToken);
            return result;
        }
    }

    private async Task<InspectionResult> ExecuteSingleFromCameraCoreAsync(
        Guid projectId,
        string cameraId,
        ExecutionSnapshot snapshot,
        ProjectGlobalVariableSchema? globalVariables,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        ImageDto? imageDto = null;
        try
        {
            var actualFlow = snapshot.CreateExecutionFlow();
            if (ImageAcquisitionFlowAnalyzer.ShouldBypassExternalCameraInput(actualFlow))
            {
                _logger.LogInformation(
                    "[InspectionService] 图像采集算子使用本地文件输入，跳过相机预采集: ProjectId={ProjectId}, CameraId={CameraId}",
                    projectId,
                    cameraId);
                return await ExecuteSingleResolvedCoreAsync(
                    projectId,
                    null,
                    snapshot,
                    globalVariables,
                    sessionId,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            imageDto = await _imageAcquisitionService.AcquireFromCameraAsync(cameraId, cancellationToken);

            if (string.IsNullOrEmpty(imageDto.DataBase64))
            {
                throw new Exception($"相机 {cameraId} 采集的图像数据为空");
            }

            var imageData = Convert.FromBase64String(imageDto.DataBase64);
            return await ExecuteSingleResolvedCoreAsync(
                projectId,
                imageData,
                snapshot,
                globalVariables,
                sessionId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ADMISSION", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = new InspectionResult(projectId);
            result.MarkAsError($"相机采集或检测失败: {ex.Message}");
            result.SetExecutionTraceability(snapshot, null, sessionId);
            await _resultRepository.AddAsync(InspectionResultPersistenceSnapshot.WithoutOutputImage(result));
            await CaptureEvidenceManifestAsync(result, cancellationToken);
            return result;
        }
        finally
        {
            if (imageDto?.Id is { } imageId && imageId != Guid.Empty)
            {
                try
                {
                    await _imageAcquisitionService.ReleaseImageAsync(imageId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[InspectionService] 释放相机采集缓存失败: {ImageId}", imageId);
                }
            }
        }
    }

    private async Task<(ExecutionSnapshot Snapshot, ProjectGlobalVariableSchema? GlobalVariables)> ResolveExecutionFlowAsync(
        Guid projectId,
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface,
        CancellationToken cancellationToken = default,
        Guid? snapshotId = null)
    {
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId, cancellationToken);
        await using (access)
        {
            if (HasExecutableFlow(flow))
            {
                var draftProject = await _projectRepository.GetByIdFreshAsync(projectId)
                    ?? throw new ProjectNotFoundException(projectId);
                var admittedFlow = AdmitExecutionFlow(flow!, "inspection.inline");
                var snapshot = new ExecutionSnapshot(
                    projectId,
                    admittedFlow,
                    draftProject.PersistenceRevision,
                    ExecutionSnapshotSource.Draft,
                    ExecutionRunMode.FormalPrimary,
                    new Dictionary<string, string> { ["ProjectRevision"] = draftProject.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    snapshotId: snapshotId,
                    globalVariables: draftProject.GlobalVariables);
                ThrowIfAdmissionRejected(_executionAdmissionService.ValidateSnapshot(snapshot, surface));
                _logger.LogInformation(
                    "[InspectionService] 使用前端提供的流程数据执行检测 (算子数: {OperatorCount})",
                    flow!.Operators.Count);
                return (snapshot, snapshot.CreateGlobalVariables());
            }

            var project = await _projectRepository.GetWithFlowAsync(projectId);
            if (project == null)
            {
                throw new ProjectNotFoundException(projectId);
            }

            // Workspace Run consumes the revisioned artifact committed by
            // ProjectSaveCoordinator under the same project access lease. It
            // never accepts a browser flow or falls back to the unsynchronized
            // legacy Project.Flow table split.
            if (surface == ExecutionAdmissionSurface.StudioInspectionRun)
            {
                var persistedFlow = await LoadVerifiedPersistedStudioFlowAsync(project, cancellationToken);
                var snapshot = new ExecutionSnapshot(
                    projectId,
                    persistedFlow,
                    project.PersistenceRevision,
                    ExecutionSnapshotSource.PersistedProject,
                    ExecutionRunMode.FormalPrimary,
                    new Dictionary<string, string> { ["ProjectRevision"] = project.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    snapshotId: snapshotId,
                    globalVariables: project.GlobalVariables);
                ThrowIfAdmissionRejected(_executionAdmissionService.ValidateSnapshot(snapshot, surface));
                return (snapshot, snapshot.CreateGlobalVariables());
            }

            // Other established execution surfaces retain their existing
            // repository-backed behavior; Workspace Run is intentionally not a
            // fallback consumer of this legacy representation.
            if (HasExecutableFlow(project.Flow))
            {
                var admittedFlow = AdmitExecutionFlow(project.Flow, "inspection.persisted");
                var snapshot = new ExecutionSnapshot(
                    projectId,
                    admittedFlow,
                    project.PersistenceRevision,
                    ExecutionSnapshotSource.PersistedProject,
                    ExecutionRunMode.FormalPrimary,
                    new Dictionary<string, string> { ["ProjectRevision"] = project.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    snapshotId: snapshotId,
                    globalVariables: project.GlobalVariables);
                ThrowIfAdmissionRejected(_executionAdmissionService.ValidateSnapshot(snapshot, surface));
                return (snapshot, snapshot.CreateGlobalVariables());
            }

            throw new InvalidOperationException($"Project {projectId} does not contain an executable flow.");
        }
    }

    private async Task<OperatorFlow> LoadVerifiedPersistedStudioFlowAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var metadata = await _flowStorage.LoadMetadataAsync(project.Id);
        if (metadata == null || metadata.SchemaVersion != 1 || metadata.ProjectId != project.Id)
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_CANONICAL_FLOW_METADATA_INVALID",
                "The persisted canonical Flow metadata is missing or does not belong to this Project.");
        }

        if (metadata.PersistenceRevision != project.PersistenceRevision)
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_PERSISTENCE_REVISION_MISMATCH",
                "The persisted canonical Flow revision does not match the Project. Save or reload, then request admission again.");
        }

        string? flowJson;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            flowJson = await _flowStorage.LoadFlowJsonAsync(project.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[InspectionService] Failed to load canonical persisted Flow for Studio Run: {ProjectId}", project.Id);
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_CANONICAL_FLOW_UNAVAILABLE",
                "The persisted canonical Flow could not be loaded. Save or reload, then request admission again.");
        }

        if (string.IsNullOrWhiteSpace(flowJson) ||
            !string.Equals(metadata.FlowHash, ComputeStoredFlowArtifactHash(flowJson), StringComparison.Ordinal))
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_CANONICAL_FLOW_HASH_MISMATCH",
                "The persisted canonical Flow failed integrity verification. Save or reload, then request admission again.");
        }

        try
        {
            var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, FlowJsonOptions);
            if (flowDto?.Operators?.Count > 0)
            {
                return flowDto.ToEntity();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[InspectionService] Failed to deserialize canonical persisted Flow for Studio Run: {ProjectId}", project.Id);
        }

        throw new StudioInspectionRunIdentityException(
            "ADMISSION_CANONICAL_FLOW_UNAVAILABLE",
            "The persisted canonical Flow is not executable. Save or reload, then request admission again.");
    }

    private static string ComputeStoredFlowArtifactHash(string flowJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(flowJson));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private OperatorFlow AdmitExecutionFlow(OperatorFlow flow, string source)
    {
        if (_workflowArtifactAdmissionGate == null)
        {
            if (WorkflowArtifactAdmissionClassifier.IsAiArtifact(flow))
            {
                throw WorkflowArtifactAdmissionFailures.GateUnavailable(source);
            }

            // Hand-authored flows retain the established runtime test seam. An
            // AI-marked flow never uses this compatibility path.
            return flow;
        }

        var admission = _workflowArtifactAdmissionGate.Inspect(flow, source);
        if (!admission.AllowedToRun || admission.Entity == null)
        {
            throw new WorkflowArtifactAdmissionException(admission.Report);
        }

        return admission.Entity;
    }

    private ProjectVariableExecutionContext? CreateProjectVariableContext(
        Guid projectId,
        OperatorFlow flow,
        ProjectGlobalVariableSchema? schema)
    {
        var hasSchema = schema != null &&
            (schema.Variables.Count > 0 || schema.SourceBindings.Count > 0 || schema.TargetBindings.Count > 0);
        var hasFlowSemantics = ProjectGlobalVariableFlowValidator.HasProjectVariableSemantics(
            flow,
            schema ?? new ProjectGlobalVariableSchema());
        if (!hasSchema && !hasFlowSemantics)
        {
            return null;
        }

        if (_projectVariableSessions == null)
        {
            throw new InvalidOperationException(
                "GV040: project global variable formal execution requires ProjectVariableSessionRegistry.");
        }

        schema ??= new ProjectGlobalVariableSchema();
        ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(schema, flow);
        var registry = _projectVariableSessions;
        var session = registry.GetOrCreate(projectId, schema);
        return new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: (workingSession, expectedVersions) =>
                registry.TryCommitAndPersist(projectId, workingSession, expectedVersions, out _, out var error)
                    ? ProjectVariableCommitResult.Success()
                    : ProjectVariableCommitResult.Failure(error));
    }

    private async Task<OperatorFlow?> LoadFlowFromStorageAsync(Guid projectId)
    {
        try
        {
            var flowJson = await _flowStorage.LoadFlowJsonAsync(projectId);
            if (string.IsNullOrWhiteSpace(flowJson))
            {
                return null;
            }

            var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, FlowJsonOptions);
            if (flowDto?.Operators?.Count > 0)
            {
                return flowDto.ToEntity();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[InspectionService] 加载项目流程文件失败: {ProjectId}", projectId);
        }

        return null;
    }

    private static bool HasExecutableFlow(OperatorFlow? flow)
    {
        return flow?.Operators?.Count > 0;
    }

    private async Task<bool> IsActiveProjectAsync(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            return false;
        }

        return await _projectRepository.GetByIdFreshAsync(projectId) != null;
    }

    private static void ThrowIfAdmissionRejected(ExecutionAdmissionResult admission)
    {
        if (!admission.IsAllowed)
        {
            throw new ExecutionAdmissionService.ExecutionAdmissionRejectedException(admission);
        }
    }

    private static void EnsurePersistedStudioRunIdentity(
        ExecutionSnapshot snapshot,
        long expectedPersistenceRevision,
        string? expectedCanonicalFlowHash,
        string? expectedDecisionConfigurationHash)
    {
        if (snapshot.Source != ExecutionSnapshotSource.PersistedProject)
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_PERSISTED_SNAPSHOT_REQUIRED",
                "Studio Workspace Run requires a persisted Project snapshot.");
        }

        if (snapshot.PersistenceRevision != expectedPersistenceRevision)
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_PERSISTENCE_REVISION_MISMATCH",
                "The Project persistence revision changed. Save or reload, then request admission again.");
        }

        if (expectedCanonicalFlowHash != null &&
            !string.Equals(snapshot.FlowHash, expectedCanonicalFlowHash, StringComparison.Ordinal))
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_SNAPSHOT_MISMATCH",
                "The persisted Flow changed after admission. Request admission again.");
        }

        if (expectedDecisionConfigurationHash != null &&
            !string.Equals(snapshot.DecisionConfigurationHash, expectedDecisionConfigurationHash, StringComparison.Ordinal))
        {
            throw new StudioInspectionRunIdentityException(
                "ADMISSION_DECISION_IDENTITY_MISMATCH",
                "The final decision binding changed after admission. Request admission again.");
        }
    }

    private Dictionary<string, object>? EnsureTraceabilityPayload(
        Dictionary<string, object>? payload,
        InspectionResult result)
    {
        var traceability = BuildTraceabilityPayload(result);
        if (traceability.Count == 0)
        {
            return payload;
        }

        var merged = payload != null
            ? new Dictionary<string, object>(payload, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        merged[TraceabilityFieldName] = traceability;
        return merged;
    }

    private static Dictionary<string, object> BuildTraceabilityPayload(InspectionResult result)
    {
        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(result.FlowVersionHash))
        {
            payload["FlowVersionHash"] = result.FlowVersionHash;
        }

        if (!string.IsNullOrWhiteSpace(result.CalibrationBundleId))
        {
            payload["CalibrationBundleId"] = result.CalibrationBundleId;
        }

        if (result.SessionId.HasValue)
        {
            payload["SessionId"] = result.SessionId.Value.ToString("D");
        }

        if (result.ExecutionSnapshotId.HasValue)
        {
            payload["ExecutionSnapshotId"] = result.ExecutionSnapshotId.Value.ToString("D");
        }

        if (result.ProjectPersistenceRevision.HasValue)
        {
            payload["ProjectPersistenceRevision"] = result.ProjectPersistenceRevision.Value;
        }

        if (!string.IsNullOrWhiteSpace(result.DecisionConfigurationHash))
        {
            payload["DecisionConfigurationHash"] = result.DecisionConfigurationHash;
        }

        if (!string.IsNullOrWhiteSpace(result.RuntimePackageId))
        {
            payload["RuntimePackageId"] = result.RuntimePackageId;
        }

        if (!string.IsNullOrWhiteSpace(result.ExecutionSource))
        {
            payload["ExecutionSource"] = result.ExecutionSource;
        }

        if (!string.IsNullOrWhiteSpace(result.ExecutionRunMode))
        {
            payload["ExecutionRunMode"] = result.ExecutionRunMode;
        }

        if (!string.IsNullOrWhiteSpace(result.ShadowRole))
        {
            payload["ShadowRole"] = result.ShadowRole;
        }

        return payload;
    }

    private string? ComputeFlowVersionHash(OperatorFlow flow)
    {
        try
        {
            return ExecutionFlowIdentity.ComputeFlowHash(flow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[InspectionService] Failed to compute flow version hash.");
            return null;
        }
    }

    private static string? TryResolveCalibrationBundleId(Dictionary<string, object>? outputData)
    {
        if (outputData == null)
        {
            return null;
        }

        if (TryResolveBundleIdFromDictionary(outputData, out var directId))
        {
            return directId;
        }

        foreach (var containerKey in new[] { TraceabilityFieldName, "Calibration", "CalibrationBundle", "CalibrationInfo", "TransformResult" })
        {
            if (!outputData.TryGetValue(containerKey, out var nested) || nested == null)
            {
                continue;
            }

            if (TryResolveBundleIdFromObject(nested, out var nestedId))
            {
                return nestedId;
            }
        }

        return null;
    }

    private static bool TryResolveBundleIdFromDictionary(
        IReadOnlyDictionary<string, object> data,
        out string? bundleId)
    {
        bundleId = null;
        if (TryReadStringValue(data, "CalibrationBundleId", out var calibrationBundleId))
        {
            bundleId = calibrationBundleId;
            return true;
        }

        if (TryReadStringValue(data, "BundleId", out var legacyBundleId))
        {
            bundleId = legacyBundleId;
            return true;
        }

        return false;
    }

    private static bool TryReadStringValue(
        IReadOnlyDictionary<string, object> data,
        string key,
        out string? value)
    {
        value = null;
        if (!data.TryGetValue(key, out var raw))
        {
            return false;
        }

        return TryResolveBundleIdFromObject(raw, out value);
    }

    private static bool TryResolveBundleIdFromObject(object value, out string? bundleId)
    {
        bundleId = null;
        switch (value)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                bundleId = text.Trim();
                return true;
            case Guid guid when guid != Guid.Empty:
                bundleId = guid.ToString("D");
                return true;
            case Dictionary<string, object> dictionary:
                return TryResolveBundleIdFromDictionary(dictionary, out bundleId);
            case IReadOnlyDictionary<string, object> dictionary:
                return TryResolveBundleIdFromDictionary(dictionary, out bundleId);
            case JsonElement element:
                return TryResolveBundleIdFromJsonElement(element, out bundleId);
            default:
                return false;
        }
    }

    private static bool TryResolveBundleIdFromJsonElement(JsonElement element, out string? bundleId)
    {
        bundleId = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    bundleId = raw.Trim();
                    return true;
                }
            }

            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals("CalibrationBundleId", StringComparison.OrdinalIgnoreCase)
                && !property.Name.Equals("BundleId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            bundleId = raw.Trim();
            return true;
        }

        return false;
    }

    private async Task PersistResultImageAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        if (_imagePersistenceService != null)
        {
            await _imagePersistenceService.PersistAsync(result, cancellationToken);
            return;
        }

        if (result.OutputImage == null || result.OutputImage.Length == 0)
        {
            return;
        }

        var config = _configurationService.GetCurrent();
        var storage = config.Storage ?? new StorageConfig();
        if (!InspectionImagePersistencePolicy.ShouldPersistImage(storage.SavePolicy, result.Status))
        {
            return;
        }

        try
        {
            var capturedAt = DateTime.Now;
            var dateFolder = capturedAt.ToString("yyyyMMdd");
            var statusFolder = result.Status switch
            {
                InspectionStatus.OK => "OK",
                InspectionStatus.NG => "NG",
                _ => "ERROR"
            };

            var extension = InspectionImageFormatDetector.GuessExtension(result.OutputImage);
            var fileName = $"{result.ProjectId:N}_{result.Id:N}_{capturedAt:HHmmssfff}{extension}";

            foreach (var rootPath in InspectionImagePersistencePaths.ResolveImageSaveRoots(storage.ImageSavePath))
            {
                var targetDir = Path.Combine(rootPath, dateFolder, statusFolder);
                var targetPath = Path.Combine(targetDir, fileName);
                try
                {
                    Directory.CreateDirectory(targetDir);
                    await File.WriteAllBytesAsync(targetPath, result.OutputImage, cancellationToken);
                    _logger.LogDebug("[InspectionService] 检测图像已落盘: {Path}", targetPath);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[InspectionService] 检测图像落盘失败，尝试下一个保存目录: {Path}", targetPath);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "[InspectionService] 检测图像落盘失败");
        }
    }

    private static byte[]? ResolveInspectionImage(FlowExecutionResult flowResult)
    {
        if (flowResult.OutputData?.TryGetValue("Image", out var outputImage) == true &&
            outputImage is byte[] imageBytes &&
            imageBytes.Length > 0)
        {
            return imageBytes;
        }

        return flowResult.InputImage is { Length: > 0 }
            ? flowResult.InputImage
            : null;
    }

    private async Task CaptureEvidenceManifestAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        if (_evidenceManifestService == null)
        {
            return;
        }

        try
        {
            await _evidenceManifestService.CaptureAsync(result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[InspectionService] Evidence manifest capture failed without affecting InspectionResult summary. ResultId={ResultId}",
                result.Id);
        }
    }

    private async Task CacheResultImageAsync(InspectionResult result)
    {
        if (result.OutputImage == null || result.OutputImage.Length == 0)
        {
            return;
        }

        try
        {
            var format = InspectionImageFormatDetector.GuessFormat(result.OutputImage);
            var imageId = await _imageCacheRepository.AddAsync(result.OutputImage, format);
            if (imageId != Guid.Empty)
            {
                result.SetImageId(imageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[InspectionService] 结果图像缓存失败");
        }
    }

    private sealed class NoOpImageCacheRepository : IImageCacheRepository
    {
        public Task<Guid> AddAsync(byte[] imageData, string format)
        {
            return Task.FromResult(Guid.Empty);
        }

        public Task<byte[]?> GetAsync(Guid id)
        {
            return Task.FromResult<byte[]?>(null);
        }

        public Task DeleteAsync(Guid id)
        {
            return Task.CompletedTask;
        }

        public Task CleanExpiredAsync(TimeSpan expiration)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpProjectFlowStorage : IProjectFlowStorage
    {
        public Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            return Task.CompletedTask;
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId)
        {
            return Task.FromResult<string?>(null);
        }
    }

    #endregion
}
