// IInspectionService.cs
// 获取统计信息
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// 检测执行服务接口
/// </summary>
public interface IInspectionService
{
    /// <summary>
    /// Validates the currently persisted Project flow for a Studio Workspace run.
    /// This is a preflight projection only; it neither starts Runtime nor reserves it.
    /// </summary>
    Task<StudioInspectionRunAdmission> AdmitPersistedStudioRunAsync(
        Guid projectId,
        long expectedPersistenceRevision,
        Guid clientSnapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the persisted Project snapshot only after re-checking the admission identity.
    /// </summary>
    Task<InspectionResult> ExecutePersistedStudioRunAsync(
        Guid projectId,
        long expectedPersistenceRevision,
        Guid clientSnapshotId,
        string expectedCanonicalFlowHash,
        string expectedDecisionConfigurationHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation through the existing runtime coordinator and
    /// returns the authoritative state observed for the supplied identity.
    /// </summary>
    Task<StudioInspectionRunReconciliation> StopPersistedStudioRunAsync(
        StudioInspectionRunIdentity identity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles a response-less formal run against the runtime coordinator
    /// and the persisted inspection result repository.
    /// </summary>
    Task<StudioInspectionRunReconciliation> ReconcilePersistedStudioRunAsync(
        StudioInspectionRunIdentity identity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行单次检测
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="imageData">图像数据</param>
    /// <returns>检测结果</returns>
    Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        byte[] imageData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行单次检测（使用前端提供的流程数据）
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="imageData">图像数据</param>
    /// <param name="flow">流程数据（含前端编辑过的参数）</param>
    /// <returns>检测结果</returns>
    Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        byte[] imageData,
        OperatorFlow? flow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行单次检测（使用相机采集）
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="cameraId">相机ID</param>
    /// <returns>检测结果</returns>
    Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        string cameraId,
        CancellationToken cancellationToken = default);

    Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        string cameraId,
        OperatorFlow? flow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 开始实时检测（相机驱动模式 - 兼容旧模式）
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="cameraId">相机ID（可选，为空则使用流程内图像采集算子）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartRealtimeInspectionAsync(
        Guid projectId,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null);

    /// <summary>
    /// Starts continuous inspection from the verified persisted canonical
    /// Project snapshot. Browser draft FlowData is never accepted.
    /// </summary>
    Task<StudioInspectionRunAdmission> StartPersistedRealtimeInspectionAsync(
        StudioInspectionRunIdentity identity,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null);

    /// <summary>
    /// 开始实时检测（流程驱动模式）
    /// 流程将循环执行，直到调用 StopRealtimeInspectionAsync
    /// 适用于PLC触发等工业场景
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="flow">流程数据</param>
    /// <param name="cameraId">相机ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartRealtimeInspectionFlowAsync(
        Guid projectId,
        OperatorFlow flow,
        string? cameraId,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null);

    /// <summary>
    /// 停止实时检测
    /// </summary>
    /// <param name="projectId">工程ID</param>
    Task StopRealtimeInspectionAsync(Guid projectId);

    Task StopPersistedRealtimeInspectionAsync(StudioInspectionRunIdentity identity);

    /// <summary>
    /// 获取检测历史
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="pageIndex">页码</param>
    /// <param name="pageSize">每页大小</param>
    Task<InspectionHistoryPage> GetInspectionHistoryAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null,
        int pageIndex = 0,
        int pageSize = 20,
        string? flowVersionHash = null);

    /// <summary>
    /// 获取检测历史详情。
    /// </summary>
    Task<InspectionHistoryDetail?> GetInspectionHistoryDetailAsync(Guid projectId, Guid resultId);

    /// <summary>
    /// 比较两条正式检测历史详情。
    /// </summary>
    Task<InspectionHistoryComparison?> CompareInspectionHistoryAsync(Guid projectId, Guid leftId, Guid rightId);

    /// <summary>
    /// 查找失败结果之前最近的 OK 正式检测参考。
    /// </summary>
    Task<InspectionPreviousSuccessReference?> FindPreviousSuccessfulInspectionAsync(
        Guid projectId,
        Guid resultId,
        int limit = 50);

    /// <summary>
    /// 获取统计信息
    /// </summary>
    /// <param name="projectId">工程ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    Task<InspectionStatistics> GetStatisticsAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null);
}

/// <summary>
/// Immutable projection returned by the admission-only Workspace endpoint.
/// It deliberately does not reserve a runtime session or grant an execute token.
/// </summary>
public sealed record StudioInspectionRunAdmission(
    Guid ProjectId,
    Guid ClientSnapshotId,
    long PersistenceRevision,
    string CanonicalFlowHash,
    string DecisionConfigurationHash,
    Guid? RuntimeSessionId = null,
    RuntimeSessionType? RuntimeSessionType = null);

public sealed record StudioInspectionRunIdentity(
    Guid ProjectId,
    Guid ClientSnapshotId,
    long PersistenceRevision,
    string CanonicalFlowHash,
    string DecisionConfigurationHash);

public enum StudioInspectionRunReconciliationStatus
{
    StillRunning,
    CancelRequested,
    Cancelled,
    Succeeded,
    Failed,
    ResultNotFound,
    IdentityMismatch
}

public sealed record StudioInspectionRunReconciliation(
    Guid ProjectId,
    Guid ClientSnapshotId,
    long PersistenceRevision,
    string CanonicalFlowHash,
    string DecisionConfigurationHash,
    StudioInspectionRunReconciliationStatus Status,
    string? Code,
    string Message,
    InspectionResult? Result);

/// <summary>
/// A stored Project changed between admission and execute, so the caller must admit again.
/// </summary>
public sealed class StudioInspectionRunIdentityException : InvalidOperationException
{
    public StudioInspectionRunIdentityException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
