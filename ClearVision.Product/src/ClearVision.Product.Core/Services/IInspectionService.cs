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

    Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        byte[] imageData,
        OperatorFlow? flow,
        ExecutionRequestAuthority authority,
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

    Task<InspectionResult> ExecuteSingleAsync(
        Guid projectId,
        string cameraBindingId,
        OperatorFlow? flow,
        ExecutionRequestAuthority authority,
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

    Task StartRealtimeInspectionAsync(
        Guid projectId,
        string? cameraBindingId,
        ExecutionRequestAuthority authority,
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

    Task StartRealtimeInspectionFlowAsync(
        Guid projectId,
        OperatorFlow flow,
        string? cameraBindingId,
        ExecutionRequestAuthority authority,
        CancellationToken cancellationToken,
        Action<InspectionResult>? onResultReady = null);

    /// <summary>
    /// 停止实时检测
    /// </summary>
    /// <param name="projectId">工程ID</param>
    Task StopRealtimeInspectionAsync(Guid projectId);

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
