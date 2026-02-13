using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Exceptions;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Services;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<InspectionService> _logger;

    public InspectionService(
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IFlowExecutionService flowExecutionService,
        IImageAcquisitionService imageAcquisitionService,
        ILogger<InspectionService> logger)
    {
        _resultRepository = resultRepository;
        _projectRepository = projectRepository;
        _flowExecutionService = flowExecutionService;
        _imageAcquisitionService = imageAcquisitionService;
        _logger = logger;
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
            // 执行检测流程
            var flowResult = await _flowExecutionService.ExecuteFlowAsync(
                actualFlow,
                new Dictionary<string, object> { { "Image", imageData } });

            _logger.LogDebug("[InspectionService] 流程执行完成: IsSuccess={IsSuccess}, OutputData keys=[{Keys}]",
                flowResult.IsSuccess, string.Join(", ", flowResult.OutputData?.Keys ?? Enumerable.Empty<string>()));

            // 算子执行失败 → Error；执行成功但有缺陷 → NG；无缺陷 → OK
            InspectionStatus status;
            if (!flowResult.IsSuccess)
            {
                status = InspectionStatus.Error;
                _logger.LogWarning("[InspectionService] 流程执行失败: {ErrorMessage}", flowResult.ErrorMessage);
            }
            else if (flowResult.OutputData?.TryGetValue("DefectCount", out var dc) == true
                     && dc is int defectCount && defectCount > 0)
            {
                status = InspectionStatus.NG;
                _logger.LogInformation("[InspectionService] 检测到 {DefectCount} 个缺陷 → NG", defectCount);
            }
            else
            {
                status = InspectionStatus.OK;
                _logger.LogInformation("[InspectionService] 未检测到缺陷 → OK");
            }

            result.SetResult(status, flowResult.ExecutionTimeMs, null, flowResult.ErrorMessage);

            // 提取输出图像用于 UI 显示
            if (flowResult.OutputData?.TryGetValue("Image", out var outputImage) == true
                && outputImage is byte[] imageBytes)
            {
                result.SetOutputImage(imageBytes);
                _logger.LogDebug("[InspectionService] 输出图像已设置, 大小: {ImageSize} bytes", imageBytes.Length);
            }
            else
            {
                _logger.LogWarning("[InspectionService] 警告: 未找到输出图像数据");
            }

            // 【核心修复】从 flowResult 提取缺陷列表并填充到 InspectionResult
            // 之前此处缺失，导致前端 result.defects 永远为空数组
            if (flowResult.OutputData?.TryGetValue("Defects", out var defectsObj) == true
                && defectsObj is System.Collections.IList defectsList)
            {
                _logger.LogDebug("[InspectionService] 提取到 {DefectCount} 个缺陷数据", defectsList.Count);
                foreach (var item in defectsList)
                {
                    if (item is Dictionary<string, object> defectDict)
                    {
                        var x = Convert.ToDouble(defectDict.GetValueOrDefault("X", 0.0));
                        var y = Convert.ToDouble(defectDict.GetValueOrDefault("Y", 0.0));
                        var width = Convert.ToDouble(defectDict.GetValueOrDefault("Width", 0.0));
                        var height = Convert.ToDouble(defectDict.GetValueOrDefault("Height", 0.0));
                        var confidence = Convert.ToDouble(defectDict.GetValueOrDefault("Confidence", 0.0));
                        var className = defectDict.GetValueOrDefault("ClassName", "unknown")?.ToString() ?? "unknown";

                        var defect = new Defect(
                            result.Id,
                            DefectType.Other,
                            x, y, width, height,
                            confidence,
                            className  // YOLO 类别名存入 Description 字段
                        );
                        result.AddDefect(defect);
                    }
                }
                _logger.LogInformation("[InspectionService] 已添加 {DefectCount} 个缺陷到结果", result.Defects.Count);
            }

            await _resultRepository.AddAsync(result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InspectionService] 检测异常: {ErrorMessage}", ex.Message);
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

    // 实时检测任务管理
    private readonly Dictionary<Guid, CancellationTokenSource> _realtimeCtsMap = new();
    private readonly Dictionary<Guid, Task> _realtimeTasks = new();
    private readonly object _realtimeLock = new();

    public async Task StartRealtimeInspectionAsync(Guid projectId, string cameraId, CancellationToken cancellationToken)
    {
        // 检查是否已有正在运行的实时检测
        lock (_realtimeLock)
        {
            if (_realtimeCtsMap.ContainsKey(projectId))
            {
                _logger.LogWarning("[InspectionService] 工程 {ProjectId} 的实时检测已在运行", projectId);
                throw new InvalidOperationException("实时检测已在运行");
            }
        }

        // 加载工程
        var project = await _projectRepository.GetWithFlowAsync(projectId);
        if (project == null)
            throw new ProjectNotFoundException(projectId);

        // 创建取消令牌源
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        lock (_realtimeLock)
        {
            _realtimeCtsMap[projectId] = cts;
        }

        _logger.LogInformation("[InspectionService] 启动实时检测: ProjectId={ProjectId}, CameraId={CameraId}", projectId, cameraId);

        // 启动后台检测任务
        var task = Task.Run(async () =>
        {
            try
            {
                await RunRealtimeInspectionLoopAsync(projectId, project.Flow, cameraId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[InspectionService] 实时检测被取消: ProjectId={ProjectId}", projectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InspectionService] 实时检测异常: ProjectId={ProjectId}", projectId);
            }
            finally
            {
                lock (_realtimeLock)
                {
                    _realtimeCtsMap.Remove(projectId);
                    _realtimeTasks.Remove(projectId);
                }
            }
        }, cts.Token);

        lock (_realtimeLock)
        {
            _realtimeTasks[projectId] = task;
        }

        // 等待任务启动
        await Task.Delay(100, cancellationToken);
    }

    public Task StopRealtimeInspectionAsync(Guid projectId)
    {
        lock (_realtimeLock)
        {
            if (_realtimeCtsMap.TryGetValue(projectId, out var cts))
            {
                _logger.LogInformation("[InspectionService] 停止实时检测: ProjectId={ProjectId}", projectId);
                cts.Cancel();
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 实时检测循环
    /// </summary>
    private async Task RunRealtimeInspectionLoopAsync(Guid projectId, OperatorFlow flow, string cameraId, CancellationToken cancellationToken)
    {
        // 配置参数
        const int minIntervalMs = 100; // 最小检测间隔 100ms
        const int maxIntervalMs = 5000; // 最大检测间隔 5s
        int currentIntervalMs = 500; // 默认500ms

        while (!cancellationToken.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // 从相机采集图像
                var imageDto = await _imageAcquisitionService.AcquireFromCameraAsync(cameraId);
                
                if (!string.IsNullOrEmpty(imageDto.DataBase64))
                {
                    var imageData = Convert.FromBase64String(imageDto.DataBase64);
                    
                    // 执行检测（不等待结果，异步执行）
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ExecuteSingleAsync(projectId, imageData, flow);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[InspectionService] 实时检测执行失败");
                        }
                    }, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("[InspectionService] 相机 {CameraId} 采集图像为空", cameraId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InspectionService] 实时检测循环异常");
                
                // 增加间隔以避免错误风暴
                currentIntervalMs = Math.Min(currentIntervalMs * 2, maxIntervalMs);
            }

            // 计算下一次执行时间
            var elapsedMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var delayMs = Math.Max(currentIntervalMs - elapsedMs, minIntervalMs);

            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
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
