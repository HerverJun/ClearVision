using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Exceptions;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Services;

namespace Acme.Product.Application.Services;

/// <summary>
/// 检测应用服务
/// </summary>
public class InspectionService : IInspectionService
{
    private readonly IInspectionResultRepository _resultRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IFlowExecutionService _flowExecutionService;
    private readonly IImageAcquisitionService _imageAcquisitionService;

    public InspectionService(
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IFlowExecutionService flowExecutionService,
        IImageAcquisitionService imageAcquisitionService)
    {
        _resultRepository = resultRepository;
        _projectRepository = projectRepository;
        _flowExecutionService = flowExecutionService;
        _imageAcquisitionService = imageAcquisitionService;
    }

    public async Task<InspectionResult> ExecuteSingleAsync(Guid projectId, byte[] imageData)
    {
        // 原有方法：使用数据库加载的流程
        return await ExecuteSingleAsync(projectId, imageData, null);
    }

    public async Task<InspectionResult> ExecuteSingleAsync(Guid projectId, byte[] imageData, OperatorFlow? flow)
    {
        // 【关键修复】如果提供了前端流程数据，则使用它；否则从数据库加载
        OperatorFlow actualFlow;
        if (flow != null)
        {
            actualFlow = flow;
            Console.WriteLine($"[InspectionService] 使用前端提供的流程数据执行检测 (算子数: {flow.Operators?.Count ?? 0})");
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
            // 执行检测流程
            var flowResult = await _flowExecutionService.ExecuteFlowAsync(
                actualFlow,
                new Dictionary<string, object> { { "Image", imageData } });

            var status = flowResult.IsSuccess ? InspectionStatus.OK : InspectionStatus.NG;
            result.SetResult(status, flowResult.ExecutionTimeMs);

            // 提取输出图像用于 UI 显示
            if (flowResult.OutputData?.TryGetValue("Image", out var outputImage) == true
                && outputImage is byte[] imageBytes)
            {
                result.SetOutputImage(imageBytes);
            }

            await _resultRepository.AddAsync(result);

            return result;
        }
        catch (Exception ex)
        {
            result.MarkAsError(ex.Message);
            await _resultRepository.AddAsync(result);
            throw;
        }
    }

    public async Task<InspectionResult> ExecuteSingleAsync(Guid projectId, string cameraId)
    {
        try
        {
            // 1. 从相机采集图像
            var imageDto = await _imageAcquisitionService.AcquireFromCameraAsync(cameraId);

            if (string.IsNullOrEmpty(imageDto.DataBase64))
            {
                throw new Exception($"相机 {cameraId} 采集的图像数据为空");
            }

            // 2. 转换数据
            var imageData = Convert.FromBase64String(imageDto.DataBase64);

            // 3. 执行检测
            return await ExecuteSingleAsync(projectId, imageData);
        }
        catch (Exception ex)
        {
            // 创建一个包含错误信息的失败结果
            var result = new InspectionResult(projectId);
            result.MarkAsError($"相机采集或检测失败: {ex.Message}");
            await _resultRepository.AddAsync(result);
            throw;
        }
    }

    public Task StartRealtimeInspectionAsync(Guid projectId, string cameraId, CancellationToken cancellationToken)
    {
        // TODO: 实现实时检测
        throw new NotImplementedException("实时检测功能待实现");
    }

    public Task StopRealtimeInspectionAsync()
    {
        // 停止实时检测 - 取消正在进行的检测任务
        // 实际实现需要在StartRealtimeInspectionAsync中保存CancellationTokenSource
        // 这里提供基础实现框架
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<InspectionResult>> GetInspectionHistoryAsync(
        Guid projectId, DateTime? startTime, DateTime? endTime, int pageIndex, int pageSize)
    {
        IEnumerable<InspectionResult> results;

        if (startTime.HasValue && endTime.HasValue)
        {
            results = await _resultRepository.GetByTimeRangeAsync(projectId, startTime.Value, endTime.Value);
        }
        else
        {
            results = await _resultRepository.GetByProjectIdAsync(projectId, pageIndex, pageSize);
        }

        return results;
    }

    public async Task<InspectionStatistics> GetStatisticsAsync(Guid projectId, DateTime? startTime, DateTime? endTime)
    {
        return await _resultRepository.GetStatisticsAsync(projectId, startTime, endTime);
    }
}
