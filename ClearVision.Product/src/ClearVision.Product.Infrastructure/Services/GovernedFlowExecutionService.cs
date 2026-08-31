using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
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
    private readonly IExecutionResourceAuthority? _resourceAuthority;
    private readonly IProjectRepository? _projectRepository;
    private readonly ProjectSaveCoordinator? _projectSaveCoordinator;

    public GovernedFlowExecutionService(
        IFlowExecutionEngine engine,
        IExecutionResourceAuthority? resourceAuthority = null,
        IProjectRepository? projectRepository = null,
        ProjectSaveCoordinator? projectSaveCoordinator = null)
    {
        _engine = engine;
        _resourceAuthority = resourceAuthority;
        _projectRepository = projectRepository;
        _projectSaveCoordinator = projectSaveCoordinator;
    }

    public async Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        await using var projectAccess = await AcquireProjectExecutionAccessAsync(snapshot, cancellationToken);
        var validationFailure = await ValidateBeforeExecutionAsync(snapshot, cancellationToken);
        if (validationFailure != null)
        {
            return validationFailure;
        }

        using var authorityScope = ExecutionAuthorityContext.Enter(snapshot);
        return await _engine.ExecuteWithSnapshotAsync(
            snapshot,
            inputData,
            enableParallel,
            cancellationToken);
    }

    public async Task<FlowExecutionResult> ExecuteWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(projectVariables);
        cancellationToken.ThrowIfCancellationRequested();
        await using var projectAccess = await AcquireProjectExecutionAccessAsync(snapshot, cancellationToken);
        var validationFailure = await ValidateBeforeExecutionAsync(snapshot, cancellationToken);
        if (validationFailure != null)
        {
            return validationFailure;
        }

        using var authorityScope = ExecutionAuthorityContext.Enter(snapshot);
        return await _engine.ExecuteWithSnapshotAsync(
            snapshot,
            inputData,
            projectVariables,
            enableParallel,
            cancellationToken);
    }

    public async Task<FlowDebugExecutionResult> ExecuteDebugWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        ProjectVariableExecutionContext? projectVariables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        await using var projectAccess = await AcquireProjectExecutionAccessAsync(snapshot, cancellationToken);
        var snapshotValidation = await ValidateSnapshotUnderProjectAccessAsync(snapshot, cancellationToken);
        if (!snapshotValidation.IsValid)
        {
            return new FlowDebugExecutionResult
            {
                DebugSessionId = options.DebugSessionId,
                IsSuccess = false,
                ErrorMessage = string.Join("; ", snapshotValidation.Errors)
            };
        }

        using var authorityScope = ExecutionAuthorityContext.Enter(snapshot);
        return await _engine.ExecuteDebugWithSnapshotAsync(
            snapshot,
            options,
            inputData,
            projectVariables,
            cancellationToken);
    }

    public async Task<OperatorExecutionResult> ExecuteOperatorWithSnapshotAsync(
        ExecutionSnapshot snapshot,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        await using var projectAccess = await AcquireProjectExecutionAccessAsync(snapshot, cancellationToken);
        var snapshotValidation = await ValidateSnapshotUnderProjectAccessAsync(snapshot, cancellationToken);
        if (!snapshotValidation.IsValid)
        {
            return InvalidOperatorResult(
                snapshot,
                string.Join("; ", snapshotValidation.Errors));
        }

        using var authorityScope = ExecutionAuthorityContext.Enter(snapshot);
        var executionFlow = snapshot.CreateExecutionFlow();
        if (executionFlow.Operators.Count != 1 || executionFlow.Connections.Count != 0)
        {
            return InvalidOperatorResult(
                snapshot,
                "ADMISSION_SINGLE_OPERATOR_SNAPSHOT_REQUIRED: Single-operator execution requires exactly one operator and no graph connections.");
        }

        var @operator = executionFlow.Operators[0];
        var violations = snapshot.SideEffectPolicy.Validate(@operator);
        if (violations.Count > 0)
        {
            return new OperatorExecutionResult
            {
                OperatorId = @operator.Id,
                OperatorName = @operator.Name,
                IsSuccess = false,
                ErrorMessage = string.Join("; ", violations.Select(item => $"{item.Code}: {item.Message}"))
            };
        }

        return await _engine.ExecuteOperatorAsync(@operator, inputs, cancellationToken);
    }

    public FlowValidationResult ValidateSnapshot(ExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var authority = ExecutionAuthorityMatrix.Validate(snapshot);
        if (!authority.Allowed)
        {
            return new FlowValidationResult
            {
                IsValid = false,
                Errors = [$"{authority.Code}: {authority.Message}"]
            };
        }

        var resourceAuthority = _resourceAuthority?.Validate(snapshot);
        if (resourceAuthority is { Allowed: false })
        {
            return new FlowValidationResult
            {
                IsValid = false,
                Errors = [$"{resourceAuthority.Code}: {resourceAuthority.Message}"]
            };
        }

        using var authorityScope = ExecutionAuthorityContext.Enter(snapshot);
        return ValidateExecutionGraphs(snapshot);
    }

    public async Task<FlowValidationResult> ValidateSnapshotAsync(
        ExecutionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        await using var projectAccess = await AcquireProjectExecutionAccessAsync(snapshot, cancellationToken);
        return await ValidateSnapshotUnderProjectAccessAsync(snapshot, cancellationToken);
    }

    private async Task<FlowValidationResult> ValidateSnapshotUnderProjectAccessAsync(
        ExecutionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSnapshot(snapshot);
        if (!validation.IsValid)
        {
            return validation;
        }

        return await ValidateCurrentProjectBindingAsync(snapshot, cancellationToken)
            ?? validation;
    }

    public FlowValidationResult ValidateFlow(OperatorFlow flow) => _engine.ValidateFlow(flow);

    private async Task<FlowExecutionResult?> ValidateBeforeExecutionAsync(
        ExecutionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateSnapshotUnderProjectAccessAsync(snapshot, cancellationToken);
        if (!validation.IsValid)
        {
            return new FlowExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = string.Join("; ", validation.Errors)
            };
        }

        return null;
    }

    private async ValueTask<ProjectAccessLease?> AcquireProjectExecutionAccessAsync(
        ExecutionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Source == ExecutionSnapshotSource.RuntimePackage || _projectSaveCoordinator == null)
        {
            return null;
        }

        return await _projectSaveCoordinator.AcquireProjectAccessAsync(
            snapshot.ProjectId,
            cancellationToken);
    }

    private async Task<FlowValidationResult?> ValidateCurrentProjectBindingAsync(
        ExecutionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Source == ExecutionSnapshotSource.RuntimePackage || _projectRepository == null)
        {
            return null;
        }

        Project? current;
        try
        {
            current = await _projectRepository.GetByIdFreshAsync(snapshot.ProjectId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new FlowValidationResult
            {
                IsValid = false,
                Errors =
                [
                    "ADMISSION_PROJECT_AUTHORITY_UNAVAILABLE: The current project revision could not be verified."
                ]
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (current is not { IsDeleted: false })
        {
            return new FlowValidationResult
            {
                IsValid = false,
                Errors =
                [
                    "ADMISSION_PROJECT_DELETED: The project bound to this execution snapshot no longer exists."
                ]
            };
        }

        if (current.PersistenceRevision != snapshot.PersistenceRevision)
        {
            return new FlowValidationResult
            {
                IsValid = false,
                Errors =
                [
                    $"ADMISSION_PROJECT_REVISION_STALE: Snapshot revision {snapshot.PersistenceRevision} does not match current project revision {current.PersistenceRevision}."
                ]
            };
        }

        return null;
    }

    private FlowValidationResult ValidateExecutionGraphs(ExecutionSnapshot snapshot)
    {
        try
        {
            foreach (var flow in NestedExecutionFlowCatalog.EnumerateFlows(snapshot.CreateExecutionFlow()))
            {
                var validation = _engine.ValidateFlow(flow);
                if (!validation.IsValid)
                {
                    return new FlowValidationResult
                    {
                        IsValid = false,
                        Errors = validation.Errors
                            .Select(error => $"FLOW_VALIDATION_FAILED: {error}")
                            .ToList()
                    };
                }
            }

            return new FlowValidationResult { IsValid = true };
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("ADMISSION_NESTED_FLOW", StringComparison.Ordinal))
        {
            return new FlowValidationResult
            {
                IsValid = false,
                Errors = [ex.Message]
            };
        }
    }

    private static FlowDebugExecutionResult InvalidDebugResult(
        Guid debugSessionId,
        FlowValidationResult validation) =>
        new()
        {
            DebugSessionId = debugSessionId,
            IsSuccess = false,
            ErrorMessage = $"FLOW_VALIDATION_FAILED: {string.Join("; ", validation.Errors)}"
        };

    private static OperatorExecutionResult InvalidOperatorResult(
        ExecutionSnapshot snapshot,
        string errorMessage)
    {
        var @operator = snapshot.CreateExecutionFlow().Operators.FirstOrDefault();
        return new OperatorExecutionResult
        {
            OperatorId = @operator?.Id ?? Guid.Empty,
            OperatorName = @operator?.Name ?? string.Empty,
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    public FlowExecutionStatus? GetExecutionStatus(Guid flowId) => _engine.GetExecutionStatus(flowId);

    public Task CancelExecutionAsync(Guid flowId) => _engine.CancelExecutionAsync(flowId);

    public Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId) =>
        _engine.GetDebugIntermediateResult(debugSessionId, operatorId);

    public Task ClearDebugCacheAsync(Guid debugSessionId) => _engine.ClearDebugCacheAsync(debugSessionId);
}
