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
        return await ExecuteSingleAsync(
            projectId,
            imageData,
            flow,
            ExecutionRequestAuthority.InternalSystem,
            cancellationToken);
    }

    public async Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        byte[] imageData,
        OperatorFlow? flow,
        ExecutionRequestAuthority authority,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, globalVariables) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            HasExecutableFlow(flow)
                ? ExecutionAdmissionSurface.StudioInspectionRun
                : ExecutionAdmissionSurface.StoredProjectExecution,
            authority,
            cancellationToken);
        return await ExecuteSingleWithCoordinatorAsync(
            snapshot,
            sessionId => ExecuteSingleResolvedCoreAsync(
                projectId,
                imageData,
                snapshot,
                globalVariables,
                sessionId,
                cancellationToken),
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
        return await ExecuteSingleAsync(
            projectId,
            cameraId,
            flow,
            ExecutionRequestAuthority.InternalSystem,
            cancellationToken);
    }

    public async Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        string cameraId,
        OperatorFlow? flow,
        ExecutionRequestAuthority authority,
        CancellationToken cancellationToken = default)
    {
        var cameraBindingId = RequireCameraBinding(cameraId);
        var (snapshot, globalVariables) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            HasExecutableFlow(flow)
                ? ExecutionAdmissionSurface.StudioInspectionRun
                : ExecutionAdmissionSurface.StoredProjectExecution,
            authority,
            cancellationToken,
            cameraBindingId);
        return await ExecuteSingleWithCoordinatorAsync(
            snapshot,
            sessionId => ExecuteSingleFromCameraCoreAsync(
                projectId,
                cameraBindingId,
                snapshot,
                globalVariables,
                sessionId,
                cancellationToken),
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
        await StartRealtimeInspectionAsync(
            projectId,
            cameraId,
            ExecutionRequestAuthority.InternalSystem,
            cancellationToken,
            onResultReady);
    }

    public async Task StartRealtimeInspectionAsync(
        Guid projectId,
        string? cameraId,
        ExecutionRequestAuthority authority,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        var cameraBindingId = string.IsNullOrWhiteSpace(cameraId) ? null : RequireCameraBinding(cameraId);
        var (snapshot, _) = await ResolveExecutionFlowAsync(
            projectId,
            flow: null,
            ExecutionAdmissionSurface.StoredProjectExecution,
            authority,
            cancellationToken,
            cameraBindingId);
        await StartRealtimeInspectionFlowCoreAsync(snapshot, cancellationToken, onResultReady);
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
        await StartRealtimeInspectionFlowAsync(
            projectId,
            flow,
            cameraId,
            ExecutionRequestAuthority.InternalSystem,
            cancellationToken,
            onResultReady);
    }

    public async Task StartRealtimeInspectionFlowAsync(
        Guid projectId,
        OperatorFlow flow,
        string? cameraId,
        ExecutionRequestAuthority authority,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        var cameraBindingId = string.IsNullOrWhiteSpace(cameraId) ? null : RequireCameraBinding(cameraId);
        var (snapshot, _) = await ResolveExecutionFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.StudioInspectionRun,
            authority,
            cancellationToken,
            cameraBindingId);
        await StartRealtimeInspectionFlowCoreAsync(
            snapshot,
            cancellationToken,
            onResultReady);
    }

    private async Task StartRealtimeInspectionFlowCoreAsync(
        ExecutionSnapshot snapshot,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null)
    {
        var projectId = snapshot.ProjectId;
        var sessionId = snapshot.SessionId;
        var effectiveCameraId = ResolveAuthoritativeExternalCameraBinding(snapshot);

        if (effectiveCameraId != null)
        {
            ThrowIfSnapshotValidationRejected(
                await _flowExecutionService.ValidateSnapshotAsync(snapshot, cancellationToken));
        }

        _logger.LogInformation(
            "[InspectionService] 请求启动实时检测: ProjectId={ProjectId}, SessionId={SessionId}, CameraId={CameraId}",
            projectId, sessionId, effectiveCameraId ?? "(流程内)");

        _imagePersistenceService?.EnsureProductionStartAllowed();

        // 步骤 1：注册会话（Coordinator 保证原子性）
        var startResult = await _coordinator.TryStartAsync(snapshot, sessionId, cancellationToken);

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
            var stopRequested = await _coordinator.TryStopAsync(projectId, rollbackCts.Token);
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

        var stopped = await _coordinator.TryStopAsync(projectId, CancellationToken.None);

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
        Func<Guid, Task<InspectionResult>> executeAsync,
        CancellationToken cancellationToken)
    {
        var projectId = snapshot.ProjectId;
        var sessionId = snapshot.SessionId;
        _imagePersistenceService?.EnsureProductionStartAllowed();
        var startResult = await _coordinator.TryStartAsync(snapshot, sessionId, cancellationToken);
        if (startResult != StartResult.Success)
        {
            throw new InvalidOperationException(startResult is StartResult.AlreadyRunning or StartResult.MutationInProgress
                ? "Project is currently running."
                : "Runtime coordinator is shutting down.");
        }

        try
        {
            _coordinator.UpdateSessionStatus(projectId, sessionId, RuntimeStatus.Running);
            return await executeAsync(sessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
            ExecutionRequestAuthority.InternalSystem,
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

            AppendCanonicalDefects(result, flowResult.OutputData);

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

    private static void AppendCanonicalDefects(
        InspectionResult result,
        IReadOnlyDictionary<string, object>? outputData)
    {
        if (!DetectionResultAdapter.TryExtractFromOutput(outputData, out var detections, out var hasDetectionPayload))
        {
            if (hasDetectionPayload)
            {
                throw new InvalidOperationException(
                    "DETECTION_OUTPUT_MALFORMED: The operator emitted an invalid detection payload.");
            }

            return;
        }

        foreach (var detection in detections)
        {
            result.AddDefect(new Defect(
                result.Id,
                DefectType.Other,
                detection.X,
                detection.Y,
                detection.Width,
                detection.Height,
                detection.Confidence,
                string.IsNullOrWhiteSpace(detection.Label) ? "unknown" : detection.Label));
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
            var access = _projectSaveCoordinator == null
                ? null
                : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId, cancellationToken);
            await using (access)
            {
                ThrowIfSnapshotValidationRejected(
                    await _flowExecutionService.ValidateSnapshotAsync(snapshot, cancellationToken));
                var authoritativeCameraBindingId = ResolveAuthoritativeExternalCameraBinding(snapshot);
                if (!string.Equals(authoritativeCameraBindingId, cameraId, StringComparison.OrdinalIgnoreCase))
                {
                    ThrowIfAdmissionRejected(ExecutionAdmissionResult.Reject(
                        "ADMISSION_EXTERNAL_CAMERA_BINDING_MISMATCH",
                        "The requested camera does not match the immutable execution snapshot."));
                }

                imageDto = await _imageAcquisitionService.AcquireFromCameraAsync(
                    authoritativeCameraBindingId!,
                    cancellationToken);
            }

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
        ExecutionRequestAuthority authority,
        CancellationToken cancellationToken = default,
        string? externalCameraBindingId = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId, cancellationToken);
        await using (access)
        {
            if (HasExecutableFlow(flow))
            {
                var draftProject = await _projectRepository.GetByIdFreshAsync(projectId)
                    ?? throw new ProjectNotFoundException(projectId);
                if (draftProject.IsDeleted)
                {
                    throw new ProjectNotFoundException(projectId);
                }
                if (authority.ExpectedProjectRevision is null ||
                    authority.ExpectedProjectRevision.Value != draftProject.PersistenceRevision)
                {
                    ThrowIfAdmissionRejected(ExecutionAdmissionResult.Reject(
                        "ADMISSION_DRAFT_REVISION_REQUIRED",
                        $"Draft expected revision '{authority.ExpectedProjectRevision?.ToString() ?? "missing"}' does not match project revision '{draftProject.PersistenceRevision}'."));
                }

                var admittedFlow = AdmitExecutionFlow(flow!, "inspection.inline");
                var flowHash = ExecutionFlowIdentity.ComputeFlowHash(admittedFlow);
                var authoritativeCameraBindingId = ResolveExternalCameraBindingForFlow(
                    admittedFlow,
                    externalCameraBindingId);
                var externalCapabilities = authoritativeCameraBindingId == null
                    ? ExecutionSideEffect.None
                    : ExecutionSideEffect.DeviceRead;
                var resourceBindings = ExecutionResourceBindingManifest.Build(
                    admittedFlow,
                    "Draft",
                    new Dictionary<string, string>
                    {
                        ["ProjectRevision"] = draftProject.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["FlowHash"] = flowHash
                    },
                    authoritativeCameraBindingId == null
                        ? null
                        : new ExecutionExternalResourceManifest(authoritativeCameraBindingId));
                var snapshot = new ExecutionSnapshot(
                    projectId,
                    admittedFlow,
                    draftProject.PersistenceRevision,
                    ExecutionSnapshotSource.Draft,
                    ExecutionRunMode.FormalPrimary,
                    resourceBindings,
                    globalVariables: draftProject.GlobalVariables,
                    principal: authority.Principal,
                    capabilityManifest: authority.CapabilityManifest,
                    expectedProjectRevision: authority.ExpectedProjectRevision,
                    confirmationId: authority.ConfirmationId,
                    auditId: authority.AuditId,
                    externalCapabilities: externalCapabilities);
                ThrowIfAdmissionRejected(_executionAdmissionService.ValidateSnapshot(snapshot, surface));
                _logger.LogInformation(
                    "[InspectionService] 使用前端提供的流程数据执行检测 (算子数: {OperatorCount})",
                    flow!.Operators.Count);
                return (snapshot, snapshot.CreateGlobalVariables());
            }

            var project = await _projectRepository.GetWithFlowAsync(projectId);
            if (project is not { IsDeleted: false })
            {
                throw new ProjectNotFoundException(projectId);
            }

            // Persisted formal execution has one authority: Project.Flow from the
            // database row captured under the project access lease.  The file is
            // a save artifact and must never become an implicit fallback.
            if (HasExecutableFlow(project.Flow))
            {
                var admittedFlow = AdmitExecutionFlow(project.Flow, "inspection.persisted");
                var authoritativeCameraBindingId = ResolveExternalCameraBindingForFlow(
                    admittedFlow,
                    externalCameraBindingId);
                var externalCapabilities = authoritativeCameraBindingId == null
                    ? ExecutionSideEffect.None
                    : ExecutionSideEffect.DeviceRead;
                var resourceBindings = ExecutionResourceBindingManifest.Build(
                    admittedFlow,
                    "StoredProject",
                    new Dictionary<string, string>
                    {
                        ["ProjectRevision"] = project.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    },
                    authoritativeCameraBindingId == null
                        ? null
                        : new ExecutionExternalResourceManifest(authoritativeCameraBindingId));
                var snapshot = new ExecutionSnapshot(
                    projectId,
                    admittedFlow,
                    project.PersistenceRevision,
                    ExecutionSnapshotSource.PersistedProject,
                    ExecutionRunMode.FormalPrimary,
                    resourceBindings,
                    globalVariables: project.GlobalVariables,
                    principal: authority.Principal,
                    capabilityManifest: new ExecutionCapabilityManifest(
                        ExecutionCapabilityManifest.Derive(admittedFlow).Capabilities | externalCapabilities,
                        isExplicit: false),
                    externalCapabilities: externalCapabilities);
                ThrowIfAdmissionRejected(_executionAdmissionService.ValidateSnapshot(snapshot, surface));
                return (snapshot, snapshot.CreateGlobalVariables());
            }

#if false // File storage is a save/recovery artifact, never a run authority.
            if (HasExecutableFlow(fileFlow))
            {
                ThrowIfAdmissionRejected(await _executionAdmissionService.ValidateFlowAsync(
                    projectId,
                    fileFlow,
                    surface,
                    cancellationToken));
                _logger.LogWarning(
                    "[InspectionService] 项目 {ProjectId} 数据库流程为空，已回退到 ProjectFlows 文件流程 (算子数: {OperatorCount})",
                    projectId,
                    fileFlow!.Operators.Count);
                throw new InvalidOperationException("Project flow storage is not an execution authority.");
            }
#endif

            throw new InvalidOperationException($"Project {projectId} does not contain an executable flow.");
        }
    }

    private string RequireCameraBinding(string cameraBindingId)
    {
        if (string.IsNullOrWhiteSpace(cameraBindingId))
        {
            ThrowIfAdmissionRejected(ExecutionAdmissionResult.Reject(
                "ADMISSION_CAMERA_BINDING_REQUIRED",
                "A configured camera binding id is required."));
        }

        var normalized = cameraBindingId.Trim();
        var config = _configurationService.GetCurrent();
        var binding = config.Cameras.FirstOrDefault(item =>
            item.IsEnabled && string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase));
        if (binding == null || string.IsNullOrWhiteSpace(binding.SerialNumber))
        {
            ThrowIfAdmissionRejected(ExecutionAdmissionResult.Reject(
                "ADMISSION_CAMERA_BINDING_NOT_FOUND",
                $"Camera binding '{normalized}' is missing, disabled, or invalid."));
        }

        return binding.Id;
    }

    private static string? ResolveExternalCameraBindingForFlow(
        OperatorFlow flow,
        string? cameraBindingId) =>
        string.IsNullOrWhiteSpace(cameraBindingId) ||
        ImageAcquisitionFlowAnalyzer.ShouldBypassExternalCameraInput(flow)
            ? null
            : cameraBindingId.Trim();

    private static string? ResolveAuthoritativeExternalCameraBinding(ExecutionSnapshot snapshot)
    {
        if (!snapshot.ExternalCapabilities.HasFlag(ExecutionSideEffect.DeviceRead))
        {
            return null;
        }

        return snapshot.ResourceBindings.TryGetValue("CameraBindingId", out var cameraBindingId) &&
               !string.IsNullOrWhiteSpace(cameraBindingId)
            ? cameraBindingId.Trim()
            : null;
    }

    private static void ThrowIfSnapshotValidationRejected(FlowValidationResult validation)
    {
        if (validation.IsValid)
        {
            return;
        }

        throw new ExecutionAdmissionService.ExecutionAdmissionRejectedException(
            ExecutionAdmissionResult.Reject(
                "ADMISSION_EXECUTION_SNAPSHOT_INVALID",
                validation.Errors.Count == 0
                    ? "Execution snapshot validation failed."
                    : string.Join("; ", validation.Errors)));
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

        var admission = _workflowArtifactAdmissionGate.Inspect(
            flow,
            source,
            context: new WorkflowArtifactAdmissionContext
            {
                AllowHistoricalDisabledOperators = true
            });
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
            var imageId = await _imageCacheRepository.AddResultAsync(
                result.OutputImage,
                format,
                new ResultImageCacheAuthority(result.ProjectId, result.Id));
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

        public Task<Guid> AddResultAsync(
            byte[] imageData,
            string format,
            ResultImageCacheAuthority authority)
        {
            return Task.FromResult(Guid.Empty);
        }

        public Task<byte[]?> GetAsync(Guid id)
        {
            return Task.FromResult<byte[]?>(null);
        }

        public Task<CachedImage?> GetEntryAsync(Guid id)
        {
            return Task.FromResult<CachedImage?>(null);
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
