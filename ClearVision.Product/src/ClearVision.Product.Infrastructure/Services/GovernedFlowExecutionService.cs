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
        Operator @operator,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default) =>
        _engine.ExecuteOperatorAsync(@operator, inputs, cancellationToken);

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
