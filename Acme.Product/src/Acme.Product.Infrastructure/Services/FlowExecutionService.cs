using System.Collections.Concurrent;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Services;

/// <summary>
/// 流程执行服务实现
/// </summary>
public class FlowExecutionService : IFlowExecutionService
{
    private readonly ConcurrentDictionary<Guid, FlowExecutionStatus> _executionStatuses = new();
    private readonly Dictionary<OperatorType, IOperatorExecutor> _executors;
    private readonly ILogger<FlowExecutionService> _logger;

    public FlowExecutionService(IEnumerable<IOperatorExecutor> executors, ILogger<FlowExecutionService> logger)
    {
        _executors = executors.ToDictionary(e => e.OperatorType);
        _logger = logger;
    }

    public async Task<FlowExecutionResult> ExecuteFlowAsync(OperatorFlow flow, Dictionary<string, object>? inputData = null, bool enableParallel = false)
    {
        var result = new FlowExecutionResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 获取执行顺序（拓扑排序）
            var executionOrder = flow.GetExecutionOrder().ToList();

            // 初始化执行状态
            var status = new FlowExecutionStatus
            {
                FlowId = flow.Id,
                IsExecuting = true,
                StartTime = DateTime.UtcNow,
                ProgressPercentage = 0
            };
            _executionStatuses[flow.Id] = status;

            // 存储每个算子的输出 - 使用 ConcurrentDictionary 支持并行执行
            var operatorOutputs = new ConcurrentDictionary<Guid, Dictionary<string, object>>();

            // 设置初始输入数据
            if (inputData != null)
            {
                operatorOutputs[Guid.Empty] = inputData;
            }

            if (enableParallel && executionOrder.Count > 1)
            {
                // 并行执行模式
                await ExecuteFlowParallelAsync(flow, executionOrder, operatorOutputs, result, status);
            }
            else
            {
                // 顺序执行模式
                await ExecuteFlowSequentialAsync(flow, executionOrder, operatorOutputs, result, status);
            }

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.IsSuccess = result.OperatorResults.All(r => r.IsSuccess);

            // 记录流程执行完成日志
            _logger.LogFlowExecution(flow.Id, executionOrder.Count, stopwatch.ElapsedMilliseconds, result.IsSuccess);

            // 获取最后一个算子的输出作为流程输出
            if (executionOrder.Any() && operatorOutputs.ContainsKey(executionOrder.Last().Id))
            {
                result.OutputData = operatorOutputs[executionOrder.Last().Id];
            }

            status.IsExecuting = false;
            status.ProgressPercentage = 100;

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = $"流程执行异常: {ex.Message}";
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            _logger.LogError(ex, "流程执行异常: {FlowId}", flow.Id);
            return result;
        }
    }

    /// <summary>
    /// 顺序执行流程
    /// </summary>
    private async Task ExecuteFlowSequentialAsync(
        OperatorFlow flow,
        List<Operator> executionOrder,
        ConcurrentDictionary<Guid, Dictionary<string, object>> operatorOutputs,
        FlowExecutionResult result,
        FlowExecutionStatus status)
    {
        int completedCount = 0;
        foreach (var op in executionOrder)
        {
            if (!_executors.TryGetValue(op.Type, out var executor))
            {
                result.OperatorResults.Add(new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ErrorMessage = $"未找到类型为 {op.Type} 的算子执行器"
                });
                continue;
            }

            // 更新当前执行状态
            status.CurrentOperatorId = op.Id;
            status.ProgressPercentage = (double)completedCount / executionOrder.Count * 100;

            // 准备输入数据
            var inputs = PrepareOperatorInputs(flow, op, operatorOutputs);

            // 执行算子
            var opResult = await ExecuteOperatorInternalAsync(op, executor, inputs);
            result.OperatorResults.Add(opResult);

            if (!opResult.IsSuccess)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"算子 '{op.Name}' 执行失败: {opResult.ErrorMessage}";
                break;
            }

            operatorOutputs[op.Id] = opResult.OutputData ?? new Dictionary<string, object>();
            completedCount++;
        }
    }

    /// <summary>
    /// 并行执行流程 - 按层级并行执行无依赖的算子
    /// </summary>
    private async Task ExecuteFlowParallelAsync(
        OperatorFlow flow,
        List<Operator> executionOrder,
        ConcurrentDictionary<Guid, Dictionary<string, object>> operatorOutputs,
        FlowExecutionResult result,
        FlowExecutionStatus status)
    {
        // 构建执行层级（哪些算子可以并行执行）
        var executionLayers = BuildExecutionLayers(flow, executionOrder);
        var completedOperators = new HashSet<Guid>();
        var failed = false;

        foreach (var layer in executionLayers)
        {
            if (failed) break;

            // 更新状态
            status.CurrentOperatorId = layer.First().Id;
            status.ProgressPercentage = (double)completedOperators.Count / executionOrder.Count * 100;

            // 并行执行当前层的所有算子
            var layerTasks = layer.Select(async op =>
            {
                if (!_executors.TryGetValue(op.Type, out var executor))
                {
                    return new OperatorExecutionResult
                    {
                        OperatorId = op.Id,
                        OperatorName = op.Name,
                        IsSuccess = false,
                        ErrorMessage = $"未找到类型为 {op.Type} 的算子执行器"
                    };
                }

                // 准备输入数据
                var inputs = PrepareOperatorInputs(flow, op, operatorOutputs);

                // 执行算子
                var opResult = await ExecuteOperatorInternalAsync(op, executor, inputs);
                
                if (opResult.IsSuccess)
                {
                    operatorOutputs[op.Id] = opResult.OutputData ?? new Dictionary<string, object>();
                }

                return opResult;
            }).ToList();

            // 等待当前层所有算子执行完成
            var layerResults = await Task.WhenAll(layerTasks);
            result.OperatorResults.AddRange(layerResults);

            // 检查是否有失败的算子
            if (layerResults.Any(r => !r.IsSuccess))
            {
                failed = true;
                var failedOp = layerResults.First(r => !r.IsSuccess);
                result.IsSuccess = false;
                result.ErrorMessage = $"算子 '{failedOp.OperatorName}' 执行失败: {failedOp.ErrorMessage}";
            }

            foreach (var op in layer)
            {
                completedOperators.Add(op.Id);
            }
        }
    }

    /// <summary>
    /// 构建执行层级 - 将算子分组，同一层的算子可以并行执行
    /// </summary>
    private List<List<Operator>> BuildExecutionLayers(OperatorFlow flow, List<Operator> executionOrder)
    {
        var layers = new List<List<Operator>>();
        var executed = new HashSet<Guid>();
        var remaining = new HashSet<Operator>(executionOrder);

        while (remaining.Any())
        {
            // 找出当前可以执行的算子（所有依赖都已执行）
            var currentLayer = remaining.Where(op =>
            {
                // 获取该算子的所有依赖（输入连接）
                var dependencies = flow.Connections
                    .Where(c => c.TargetOperatorId == op.Id)
                    .Select(c => c.SourceOperatorId);

                // 检查所有依赖是否已执行
                return dependencies.All(depId => executed.Contains(depId));
            }).ToList();

            if (!currentLayer.Any())
            {
                // 如果没有可以执行的算子，说明有循环依赖或其他问题
                // 将剩余的算子作为一个层级执行
                currentLayer = remaining.ToList();
            }

            layers.Add(currentLayer);
            
            foreach (var op in currentLayer)
            {
                executed.Add(op.Id);
                remaining.Remove(op);
            }
        }

        return layers;
    }

    /// <summary>
    /// 内部执行单个算子
    /// </summary>
    private async Task<OperatorExecutionResult> ExecuteOperatorInternalAsync(
        Operator op,
        IOperatorExecutor executor,
        Dictionary<string, object> inputs)
    {
        op.MarkExecutionStarted();
        var opStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var opResult = await executor.ExecuteAsync(op, inputs);
            opStopwatch.Stop();

            if (opResult.IsSuccess)
            {
                op.MarkExecutionCompleted(opStopwatch.ElapsedMilliseconds);
                _logger.LogOperatorExecution(op.Id, op.Name, opStopwatch.ElapsedMilliseconds, true);

                return new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = true,
                    ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                    OutputData = opResult.OutputData
                };
            }
            else
            {
                op.MarkExecutionFailed(opResult.ErrorMessage ?? "未知错误");
                _logger.LogOperatorExecution(op.Id, op.Name, opStopwatch.ElapsedMilliseconds, false);
                _logger.LogError("算子执行失败: {OperatorName} ({OperatorId}), 错误: {ErrorMessage}",
                    op.Name, op.Id, opResult.ErrorMessage);

                return new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                    ErrorMessage = opResult.ErrorMessage
                };
            }
        }
        catch (Exception ex)
        {
            opStopwatch.Stop();
            op.MarkExecutionFailed(ex.Message);
            _logger.LogError(ex, "算子执行异常: {OperatorName} ({OperatorId})", op.Name, op.Id);

            return new OperatorExecutionResult
            {
                OperatorId = op.Id,
                OperatorName = op.Name,
                IsSuccess = false,
                ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task<OperatorExecutionResult> ExecuteOperatorAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        if (!_executors.TryGetValue(@operator.Type, out var executor))
        {
            return Task.FromResult(new OperatorExecutionResult
            {
                OperatorId = @operator.Id,
                OperatorName = @operator.Name,
                IsSuccess = false,
                ErrorMessage = $"未找到类型为 {@operator.Type} 的算子执行器"
            });
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        return executor.ExecuteAsync(@operator, inputs).ContinueWith(t =>
        {
            stopwatch.Stop();
            var result = t.Result;

            return new OperatorExecutionResult
            {
                OperatorId = @operator.Id,
                OperatorName = @operator.Name,
                IsSuccess = result.IsSuccess,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                OutputData = result.OutputData,
                ErrorMessage = result.ErrorMessage
            };
        });
    }

    public FlowValidationResult ValidateFlow(OperatorFlow flow)
    {
        var result = new FlowValidationResult();

        // 检查是否有算子
        if (!flow.Operators.Any())
        {
            result.Errors.Add("流程中没有任何算子");
            return result;
        }

        // 检查是否有图像采集算子作为输入
        var hasInputOperator = flow.Operators.Any(o => o.Type == OperatorType.ImageAcquisition);
        if (!hasInputOperator)
        {
            result.Warnings.Add("流程缺少图像采集算子作为输入");
        }

        // 检查是否有结果输出算子
        var hasOutputOperator = flow.Operators.Any(o => o.Type == OperatorType.ResultOutput);
        if (!hasOutputOperator)
        {
            result.Warnings.Add("流程缺少结果输出算子");
        }

        // 验证每个算子的参数
        foreach (var op in flow.Operators)
        {
            if (_executors.TryGetValue(op.Type, out var executor))
            {
                var validation = executor.ValidateParameters(op);
                if (!validation.IsValid)
                {
                    foreach (var error in validation.Errors)
                    {
                        result.Errors.Add($"算子 '{op.Name}': {error}");
                    }
                }
            }
        }

        result.IsValid = !result.Errors.Any();
        return result;
    }

    public FlowExecutionStatus? GetExecutionStatus(Guid flowId)
    {
        return _executionStatuses.TryGetValue(flowId, out var status) ? status : null;
    }

    public Task CancelExecutionAsync(Guid flowId)
    {
        // TODO: 实现取消逻辑
        if (_executionStatuses.TryGetValue(flowId, out var status))
        {
            status.IsExecuting = false;
        }
        return Task.CompletedTask;
    }

    private Dictionary<string, object> PrepareOperatorInputs(OperatorFlow flow, Operator op, IDictionary<Guid, Dictionary<string, object>> operatorOutputs)
    {
        var inputs = new Dictionary<string, object>();

        // 查找连接到该算子的所有连接
        var incomingConnections = flow.Connections
            .Where(c => c.TargetOperatorId == op.Id)
            .ToList();

        // 如果没有输入连接,尝试从初始输入数据获取(Guid.Empty)
        if (!incomingConnections.Any())
        {
            if (operatorOutputs.TryGetValue(Guid.Empty, out var initialInputs))
            {
                foreach (var kvp in initialInputs)
                {
                    inputs[kvp.Key] = kvp.Value;
                }
            }
        }
        else
        {
            foreach (var connection in incomingConnections)
            {
                if (operatorOutputs.TryGetValue(connection.SourceOperatorId, out var sourceOutputs))
                {
                    // 将源算子的输出合并到当前算子的输入
                    foreach (var kvp in sourceOutputs)
                    {
                        inputs[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        return inputs;
    }
}
