// OperatorExecutionResultModel.cs
// 算子执行结果模型
// 提供 IOperatorExecutionResult 的默认实现与传输结构
// 作者：蘅芜君
using System.Collections.Generic;
using ClearVision.OperatorLibrary.Abstractions.Contracts;

namespace ClearVision.OperatorLibrary.Abstractions.Models;

/// <summary>
/// Default implementation for host-agnostic execution results.
/// </summary>
public sealed class OperatorExecutionResultModel : IOperatorExecutionResult
{
    public bool IsSuccess { get; init; }

    public IDictionary<string, object?>? OutputData { get; init; }

    public long ExecutionTimeMs { get; init; }

    public string? ErrorMessage { get; init; }

    public bool ShouldShortCircuitFlow { get; init; }

    public static OperatorExecutionResultModel Success(
        IDictionary<string, object?>? outputData = null,
        long executionTimeMs = 0,
        bool shouldShortCircuitFlow = false)
    {
        return new OperatorExecutionResultModel
        {
            IsSuccess = true,
            OutputData = outputData,
            ExecutionTimeMs = executionTimeMs,
            ShouldShortCircuitFlow = shouldShortCircuitFlow
        };
    }

    public static OperatorExecutionResultModel Failure(string errorMessage)
    {
        return new OperatorExecutionResultModel
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
