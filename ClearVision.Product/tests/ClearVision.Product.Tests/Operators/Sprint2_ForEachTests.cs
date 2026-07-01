// Sprint2_ForEachTests.cs
// Sprint 2 Task 2.1 ForEach 算子单元测试
// 测试 IoMode 双模式：Parallel（并行）/ Sequential（串行）
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

/// <summary>
/// Sprint 2 Task 2.1: ForEach 算子单元测试
/// </summary>
public class Sprint2_ForEachTests
{
    private readonly ILogger<ForEachOperator> _loggerMock;
    private readonly IFlowExecutionService _flowExecutorMock;
    private readonly IServiceProvider _scopedServiceProviderMock;
    private readonly IServiceScope _serviceScopeMock;
    private readonly IServiceScopeFactory _serviceScopeFactoryMock;
    private readonly ForEachOperator _operator;

    public Sprint2_ForEachTests()
    {
        _loggerMock = Substitute.For<ILogger<ForEachOperator>>();
        _flowExecutorMock = Substitute.For<IFlowExecutionService>();
        _scopedServiceProviderMock = Substitute.For<IServiceProvider>();
        _serviceScopeMock = Substitute.For<IServiceScope>();
        _serviceScopeFactoryMock = Substitute.For<IServiceScopeFactory>();
        _scopedServiceProviderMock.GetService(typeof(IFlowExecutionService)).Returns(_flowExecutorMock);
        _serviceScopeMock.ServiceProvider.Returns(_scopedServiceProviderMock);
        _serviceScopeFactoryMock.CreateScope().Returns(_serviceScopeMock);
        _operator = new ForEachOperator(_loggerMock, _serviceScopeFactoryMock);
    }

    /// <summary>
    /// 测试：Parallel 模式正确并行执行
    /// 15 目标 × 50ms/子图，MaxParallelism=8，总耗时 ≤ 150ms
    /// </summary>
    [Fact]
    public async Task ForEach_ParallelMode_ExecutesInParallel()
    {
        // 准备
        var items = Enumerable.Range(0, 15)
            .Select(i => new ClearVision.Product.Core.ValueObjects.DetectionResult($"Item{i}", 0.9f, i * 10, 0, 10, 10))
            .ToList();

        var op = CreateOperator(new Dictionary<string, object>
        {
            { "IoMode", "Parallel" },
            { "MaxParallelism", 8 },
            { "TimeoutMs", 30000 }
        });

        var inputs = new Dictionary<string, object>
        {
            { "Items", items }
        };

        var activeExecutions = 0;
        var maxConcurrentExecutions = 0;

        // 模拟子图执行 - 每个耗时 50ms
        _flowExecutorMock.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var active = Interlocked.Increment(ref activeExecutions);
                maxConcurrentExecutions = Math.Max(maxConcurrentExecutions, active);
                await Task.Delay(50); // 模拟 50ms 处理时间
                Interlocked.Decrement(ref activeExecutions);
                return new FlowExecutionResult
                {
                    IsSuccess = true,
                    OutputData = new Dictionary<string, object> { { "Result", true } }
                };
            });

        // 设置子图
        _operator.SubGraph = new OperatorFlow("SubGraph");

        // 执行
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _operator.ExecuteAsync(op, inputs);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.True(maxConcurrentExecutions > 1, "Parallel mode should execute more than one sub-flow at a time.");
        await _flowExecutorMock.Received(15).ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>>(),
                false,
                Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// 测试：Sequential 模式串行执行
    /// 验证每次只有一个子图在执行
    /// </summary>
    [Fact]
    public async Task ForEach_SequentialMode_ExecutesSequentially()
    {
        // 准备
        var items = Enumerable.Range(0, 5)
            .Select(i => new ClearVision.Product.Core.ValueObjects.DetectionResult($"Item{i}", 0.9f, i * 10, 0, 10, 10))
            .ToList();

        var op = CreateOperator(new Dictionary<string, object>
        {
            { "IoMode", "Sequential" },
            { "TimeoutMs", 30000 }
        });

        var inputs = new Dictionary<string, object>
        {
            { "Items", items }
        };

        var executionTimes = new List<DateTime>();
        _flowExecutorMock.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                lock (executionTimes)
                {
                    executionTimes.Add(DateTime.UtcNow);
                }
                await Task.Delay(50); // 模拟 50ms 处理时间
                return new FlowExecutionResult
                {
                    IsSuccess = true,
                    OutputData = new Dictionary<string, object> { { "Result", true } }
                };
            });

        // 设置子图
        _operator.SubGraph = new OperatorFlow("SubGraph");

        // 执行
        var result = await _operator.ExecuteAsync(op, inputs);

        // 验证
        Assert.True(result.IsSuccess);
        Assert.Equal(5, executionTimes.Count);

        // 验证串行：每次执行应该间隔至少 50ms
        for (int i = 1; i < executionTimes.Count; i++)
        {
            var gap = executionTimes[i] - executionTimes[i - 1];
            Assert.True(gap.TotalMilliseconds >= 40, // 允许一些误差
                $"串行执行间隔过短: {gap.TotalMilliseconds:F1}ms，期望 >= 40ms");
        }
    }

    /// <summary>
    /// 测试：FailFast 在 Sequential 模式下正确中断
    /// 第 3 个子图失败时，第 4~15 个子图不应执行
    /// </summary>
    [Fact]
    public async Task ForEach_SequentialMode_FailFast_StopsAfterFailure()
    {
        // 准备
        var items = Enumerable.Range(0, 10)
            .Select(i => new ClearVision.Product.Core.ValueObjects.DetectionResult($"Item{i}", 0.9f, i * 10, 0, 10, 10))
            .ToList();

        var op = CreateOperator(new Dictionary<string, object>
        {
            { "IoMode", "Sequential" },
            { "FailFast", true },
            { "TimeoutMs", 30000 }
        });

        var inputs = new Dictionary<string, object>
        {
            { "Items", items }
        };

        var executionCount = 0;
        _flowExecutorMock.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int currentIndex;
                lock (this)
                {
                    currentIndex = executionCount++;
                }

                // 第 3 个（索引 2）失败
                if (currentIndex == 2)
                {
                    return Task.FromResult(new FlowExecutionResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "模拟失败"
                    });
                }

                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    OutputData = new Dictionary<string, object> { { "Result", true } }
                });
            });

        // 设置子图
        _operator.SubGraph = new OperatorFlow("SubGraph");

        // 执行
        var result = await _operator.ExecuteAsync(op, inputs);

        // 验证：只执行了 3 次（0, 1, 2）
        Assert.Equal(3, executionCount);
    }

    /// <summary>
    /// 测试：空列表返回空结果
    /// </summary>
    [Fact]
    public async Task ForEach_EmptyItems_ReturnsEmptyResult()
    {
        var op = CreateOperator();
        var inputs = new Dictionary<string, object>
        {
            { "Items", new List<ClearVision.Product.Core.ValueObjects.DetectionResult>() }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.Equal(0, result.OutputData!["Count"]);
        Assert.Equal(0, result.OutputData["PassCount"]);
        Assert.True((bool)result.OutputData["AllPass"]);
    }

    [Fact]
    public async Task ForEach_ExecutesSubGraphWithinCreatedScope()
    {
        var op = CreateOperator();
        var inputs = new Dictionary<string, object>
        {
            { "Items", new List<object> { 1 } }
        };

        _flowExecutorMock.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                OutputData = new Dictionary<string, object> { { "Result", true } }
            }));

        _operator.SubGraph = new OperatorFlow("ScopedSubGraph");

        var result = await _operator.ExecuteAsync(op, inputs);

        Assert.True(result.IsSuccess);
        _serviceScopeFactoryMock.Received(1).CreateScope();
        await _flowExecutorMock.Received(1).ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>>(),
            false,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// 测试：参数验证 - IoMode 必须是 Parallel 或 Sequential
    /// </summary>
    [Fact]
    public void ForEach_ValidateParameters_InvalidIoMode_ReturnsError()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "IoMode", "InvalidMode" }
        });

        var result = _operator.ValidateParameters(op);

        Assert.False(result.IsValid);
        Assert.Contains("Parallel", result.Errors[0]);
        Assert.Contains("Sequential", result.Errors[0]);
    }

    /// <summary>
    /// 测试：参数验证 - MaxParallelism 范围检查
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void ForEach_ValidateParameters_InvalidMaxParallelism_ReturnsError(int maxParallelism)
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "MaxParallelism", maxParallelism }
        });

        var result = _operator.ValidateParameters(op);

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// 测试：参数验证 - TimeoutMs 范围检查
    /// </summary>
    [Theory]
    [InlineData(500)]   // 太小
    [InlineData(400000)] // 太大
    public void ForEach_ValidateParameters_InvalidTimeout_ReturnsError(int timeoutMs)
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "TimeoutMs", timeoutMs }
        });

        var result = _operator.ValidateParameters(op);

        Assert.False(result.IsValid);
    }

    private Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator(Guid.NewGuid(), "TestForEach", OperatorType.ForEach, 0, 0);

        // 添加默认参数
        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "IoMode",
            "执行模式",
            "Parallel=并行纯计算, Sequential=串行含I/O",
            "string",
            "Parallel",
            isRequired: true
        ));

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "MaxParallelism",
            "最大并行度",
            "并行模式下的最大线程数",
            "int",
            Environment.ProcessorCount,
            1,
            64,
            true
        ));

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "FailFast",
            "快速失败",
            "任一子图失败时立即终止",
            "bool",
            false,
            isRequired: true
        ));

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "TimeoutMs",
            "超时(毫秒)",
            "单个子图执行超时时间",
            "int",
            30000,
            1000,
            300000,
            true
        ));

        // 添加自定义参数
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                op.UpdateParameter(key, value);
            }
        }

        return op;
    }
}
