// IFlowExecutionService.cs
// 开始时间
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// 流程执行服务接口
/// </summary>
public interface IFlowExecutionService
{
    Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        CancellationToken cancellationToken = default);

    Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        CancellationToken cancellationToken = default);

    Task<FlowDebugExecutionResult> ExecuteDebugWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        ProjectVariableExecutionContext? projectVariables = null,
        CancellationToken cancellationToken = default);

    Task<OperatorExecutionResult> ExecuteOperatorAsync(
        GovernedOperatorExecutionContext context,
        Operator @operator,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default);

    FlowValidationResult ValidateSnapshot(ExecutionSnapshot snapshot);

    FlowExecutionStatus? GetExecutionStatus(Guid flowId);

    Task CancelExecutionAsync(Guid flowId);

    Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId);

    Task ClearDebugCacheAsync(Guid debugSessionId);
}

public sealed class GovernedOperatorExecutionContext
{
    private GovernedOperatorExecutionContext(ExecutionRunMode runMode, bool hasIsolatedState)
    {
        RunMode = runMode;
        HasIsolatedState = hasIsolatedState;
        SideEffectPolicy = ExecutionSideEffectPolicy.For(runMode);
    }

    public ExecutionRunMode RunMode { get; }

    public bool HasIsolatedState { get; }

    public ExecutionSideEffectPolicy SideEffectPolicy { get; }

    public static GovernedOperatorExecutionContext Preview(bool hasIsolatedState = false) =>
        new(ExecutionRunMode.Preview, hasIsolatedState);

    public static GovernedOperatorExecutionContext Debug(bool hasIsolatedState = false) =>
        new(ExecutionRunMode.Debug, hasIsolatedState);
}

/// <summary>
/// Raw flow engine contract. Production entrypoints must depend on
/// <see cref="IFlowExecutionService"/> instead.
/// </summary>
public interface IFlowExecutionEngine
{
    /// <summary>
    /// Executes the immutable authority captured for one run.  The default
    /// implementation keeps existing adapters compatible while ensuring the
    /// capability policy is checked before any executor is selected.
    /// </summary>
    Task<FlowExecutionResult> ExecuteFlowAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        System.Threading.CancellationToken cancellationToken = default)
    {
        return this.ExecuteWithSnapshotAsync(snapshot, inputData, enableParallel, cancellationToken);
    }

    /// <summary>Snapshot overload for project-global-variable formal runs.</summary>
    Task<FlowExecutionResult> ExecuteFlowAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        System.Threading.CancellationToken cancellationToken = default)
    {
        return this.ExecuteWithSnapshotAsync(snapshot, inputData, projectVariables, enableParallel, cancellationToken);
    }

    /// <summary>
    /// 执行算子流程
    /// </summary>
    /// <param name="flow">算子流程</param>
    /// <param name="inputData">输入数据</param>
    /// <param name="enableParallel">是否启用并行执行</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<FlowExecutionResult> ExecuteFlowAsync(OperatorFlow flow, Dictionary<string, object>? inputData = null, bool enableParallel = false, System.Threading.CancellationToken cancellationToken = default);

    Task<FlowExecutionResult> ExecuteFlowAsync(
        OperatorFlow flow,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        System.Threading.CancellationToken cancellationToken = default)
    {
        return ExecuteFlowAsync(flow, inputData, enableParallel, cancellationToken);
    }

    /// <summary>
    /// Executes a flow with an explicit production execution mode.
    /// </summary>
    Task<FlowExecutionResult> ExecuteFlowAsync(
        OperatorFlow flow,
        Dictionary<string, object>? inputData,
        FlowExecutionMode executionMode,
        System.Threading.CancellationToken cancellationToken = default)
    {
        return ExecuteFlowAsync(
            flow,
            inputData,
            executionMode == FlowExecutionMode.AutoSafeParallel,
            cancellationToken);
    }

    /// <summary>
    /// 执行单个算子
    /// </summary>
    /// <param name="operator">算子</param>
    /// <param name="inputs">输入数据</param>
    /// <returns>算子执行结果</returns>
    Task<OperatorExecutionResult> ExecuteOperatorAsync(
        Operator @operator,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证流程有效性
    /// </summary>
    /// <param name="flow">算子流程</param>
    /// <returns>验证结果</returns>
    FlowValidationResult ValidateFlow(OperatorFlow flow);

    /// <summary>
    /// 获取流程执行状态
    /// </summary>
    /// <param name="flowId">流程ID</param>
    /// <returns>执行状态</returns>
    FlowExecutionStatus? GetExecutionStatus(Guid flowId);

    /// <summary>
    /// 取消流程执行
    /// </summary>
    /// <param name="flowId">流程ID</param>
    Task CancelExecutionAsync(Guid flowId);

    /// <summary>
    /// 调试执行流程 - 支持断点和单步执行
    /// </summary>
    /// <param name="flow">算子流程</param>
    /// <param name="options">调试选项</param>
    /// <param name="inputData">输入数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调试执行结果</returns>
    Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(
        OperatorFlow flow,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        CancellationToken cancellationToken = default);

    Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(
        OperatorFlow flow,
        DebugOptions options,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        CancellationToken cancellationToken = default)
    {
        return ExecuteFlowDebugAsync(flow, options, inputData, cancellationToken);
    }

    /// <summary>
    /// 获取调试中间结果
    /// </summary>
    /// <param name="debugSessionId">调试会话ID</param>
    /// <param name="operatorId">算子ID</param>
    /// <returns>中间结果数据</returns>
    Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId);

    /// <summary>
    /// 清除调试缓存
    /// </summary>
    /// <param name="debugSessionId">调试会话ID</param>
    Task ClearDebugCacheAsync(Guid debugSessionId);
}

public interface IFlowDefinitionValidator
{
    FlowValidationResult ValidateFlow(OperatorFlow flow);
}

/// <summary>
/// The compatibility boundary between governed execution and the legacy flow
/// engine overloads. Product entrypoints call these methods with an explicit
/// immutable snapshot; only this adapter invokes the legacy engine contract.
/// </summary>
public static class GovernedFlowExecution
{
    public static Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        this IFlowExecutionEngine service,
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(snapshot);
        var flow = snapshot.CreateExecutionFlow();
        var violations = ValidateGovernedExecution(snapshot, flow, projectVariables: null);
        return violations.Count > 0
            ? Task.FromResult(FlowExecutionResult.SideEffectPolicyRejected(violations))
            : service.ExecuteFlowAsync(flow, inputData, enableParallel, cancellationToken);
    }

    public static Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        this IFlowExecutionEngine service,
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(projectVariables);
        var flow = snapshot.CreateExecutionFlow();
        var violations = ValidateGovernedExecution(snapshot, flow, projectVariables);
        return violations.Count > 0
            ? Task.FromResult(FlowExecutionResult.SideEffectPolicyRejected(violations))
            : service.ExecuteFlowAsync(flow, inputData, projectVariables, enableParallel, cancellationToken);
    }

    public static Task<FlowDebugExecutionResult> ExecuteDebugWithSnapshotAsync(
        this IFlowExecutionEngine service,
        ExecutionSnapshot snapshot,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        ProjectVariableExecutionContext? projectVariables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        var flow = snapshot.CreateExecutionFlow();
        var violations = ValidateGovernedExecution(snapshot, flow, projectVariables);
        if (violations.Count > 0)
        {
            return Task.FromResult(new FlowDebugExecutionResult
            {
                DebugSessionId = options.DebugSessionId,
                IsSuccess = false,
                ErrorMessage = $"SIDE_EFFECT_POLICY_BLOCKED: {string.Join("; ", violations.Select(item => item.Message))}",
                OperatorResults = violations.Select(item => new OperatorExecutionResult
                {
                    OperatorId = item.OperatorId,
                    OperatorName = item.OperatorName,
                    IsSuccess = false,
                    ErrorMessage = $"{item.Code}: {item.Message}"
                }).ToList()
            });
        }

        return projectVariables == null
            ? service.ExecuteFlowDebugAsync(flow, options, inputData, cancellationToken)
            : service.ExecuteFlowDebugAsync(flow, options, inputData, projectVariables, cancellationToken);
    }

    private static IReadOnlyList<ExecutionSideEffectViolation> ValidateGovernedExecution(
        ExecutionSnapshot snapshot,
        OperatorFlow flow,
        ProjectVariableExecutionContext? projectVariables)
    {
        var violations = snapshot.SideEffectPolicy.Validate(flow).ToList();
        if (snapshot.RunMode is not (ExecutionRunMode.Preview or ExecutionRunMode.Debug) ||
            !flow.Operators.Any(@operator =>
                @operator.IsEnabled &&
                ExecutionSideEffectCatalog.GetCapabilities(@operator).HasFlag(ExecutionSideEffect.StateWrite)))
        {
            return violations;
        }

        if (projectVariables is { IsPreview: true, CommitHandler: null })
        {
            return violations;
        }

        violations.AddRange(flow.Operators
            .Where(@operator =>
                @operator.IsEnabled &&
                ExecutionSideEffectCatalog.GetCapabilities(@operator).HasFlag(ExecutionSideEffect.StateWrite))
            .Select(@operator => new ExecutionSideEffectViolation(
                @operator.Id,
                @operator.Name,
                @operator.Type,
                ExecutionSideEffect.StateWrite,
                "SIDE_EFFECT_ISOLATED_STATE_REQUIRED",
                $"{@operator.Type} requires an isolated preview variable context in {snapshot.RunMode}.")));
        return violations;
    }
}

public enum FlowExecutionMode
{
    Sequential = 0,
    AutoSafeParallel = 1
}

/// <summary>
/// 流程执行结果
/// </summary>
public class FlowExecutionResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 输出数据
    /// </summary>
    public Dictionary<string, object>? OutputData { get; set; }

    public bool WasShortCircuited { get; set; }

    /// <summary>
    /// 各算子执行结果
    /// </summary>
    public List<OperatorExecutionResult> OperatorResults { get; set; } = new();

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    public static FlowExecutionResult SideEffectPolicyRejected(
        IReadOnlyList<ExecutionSideEffectViolation> violations) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = $"SIDE_EFFECT_POLICY_BLOCKED: {string.Join("; ", violations.Select(violation => violation.Message))}",
            OperatorResults = violations.Select(violation => new OperatorExecutionResult
            {
                OperatorId = violation.OperatorId,
                OperatorName = violation.OperatorName,
                IsSuccess = false,
                ErrorMessage = $"{violation.Code}: {violation.Message}"
            }).ToList()
        };
}

/// <summary>
/// 算子执行结果
/// </summary>
public class OperatorExecutionResult
{
    /// <summary>
    /// 算子ID
    /// </summary>
    public Guid OperatorId { get; set; }

    /// <summary>
    /// 算子名称
    /// </summary>
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 输出数据
    /// </summary>
    public Dictionary<string, object>? OutputData { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    public bool ShortCircuitedFlow { get; set; }
}

/// <summary>
/// 流程验证结果
/// </summary>
public class FlowValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误信息列表
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 警告信息列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 流程执行状态
/// </summary>
public class FlowExecutionStatus
{
    /// <summary>
    /// 流程ID
    /// </summary>
    public Guid FlowId { get; set; }

    /// <summary>
    /// 是否正在执行
    /// </summary>
    public bool IsExecuting { get; set; }

    /// <summary>
    /// 当前执行的算子ID
    /// </summary>
    public Guid? CurrentOperatorId { get; set; }

    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 完成时间（执行结束时写入，用于状态延迟清理）
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}
