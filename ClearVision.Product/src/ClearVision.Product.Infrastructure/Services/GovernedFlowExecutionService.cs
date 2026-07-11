using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// The only product-facing flow execution adapter. It requires an immutable
/// execution snapshot and delegates raw graph execution to the engine boundary.
/// </summary>
public sealed class GovernedFlowExecutionService : IFlowExecutionService, IFlowDefinitionValidator
{
    private readonly IFlowExecutionEngine _engine;

    public GovernedFlowExecutionService(IFlowExecutionEngine engine)
    {
        _engine = engine;
    }

    public Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        CancellationToken cancellationToken = default) =>
        _engine.ExecuteWithSnapshotAsync(snapshot, inputData, enableParallel, cancellationToken);

    public Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        CancellationToken cancellationToken = default) =>
        _engine.ExecuteWithSnapshotAsync(
            snapshot,
            inputData,
            projectVariables,
            enableParallel,
            cancellationToken);

    public Task<FlowDebugExecutionResult> ExecuteDebugWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        ProjectVariableExecutionContext? projectVariables = null,
        CancellationToken cancellationToken = default) =>
        _engine.ExecuteDebugWithSnapshotAsync(
            snapshot,
            options,
            inputData,
            projectVariables,
            cancellationToken);

    public Task<OperatorExecutionResult> ExecuteOperatorAsync(
        GovernedOperatorExecutionContext context,
        Operator @operator,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(@operator);
        var violations = context.SideEffectPolicy.Validate(@operator).ToList();
        if (context.RunMode is ExecutionRunMode.Preview or ExecutionRunMode.Debug &&
            !context.HasIsolatedState &&
            ExecutionSideEffectCatalog.GetCapabilities(@operator).HasFlag(ExecutionSideEffect.StateWrite))
        {
            violations.Add(new ExecutionSideEffectViolation(
                @operator.Id,
                @operator.Name,
                @operator.Type,
                ExecutionSideEffect.StateWrite,
                "SIDE_EFFECT_ISOLATED_STATE_REQUIRED",
                $"{@operator.Type} requires isolated state in {context.RunMode}."));
        }

        if (violations.Count > 0)
        {
            return Task.FromResult(new OperatorExecutionResult
            {
                OperatorId = @operator.Id,
                OperatorName = @operator.Name,
                IsSuccess = false,
                ErrorMessage = string.Join("; ", violations.Select(item => $"{item.Code}: {item.Message}"))
            });
        }

        return _engine.ExecuteOperatorAsync(@operator, inputs, cancellationToken);
    }

    public FlowValidationResult ValidateSnapshot(ExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _engine.ValidateFlow(snapshot.CreateExecutionFlow());
    }

    public FlowValidationResult ValidateFlow(OperatorFlow flow) => _engine.ValidateFlow(flow);

    public FlowExecutionStatus? GetExecutionStatus(Guid flowId) => _engine.GetExecutionStatus(flowId);

    public Task CancelExecutionAsync(Guid flowId) => _engine.CancelExecutionAsync(flowId);

    public Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId) =>
        _engine.GetDebugIntermediateResult(debugSessionId, operatorId);

    public Task ClearDebugCacheAsync(Guid debugSessionId) => _engine.ClearDebugCacheAsync(debugSessionId);
}
