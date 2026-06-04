namespace ClearVision.Product.Tests.AI.AgentEvaluation;

public enum AgentEvaluationToolPermission
{
    ReadOnly,
    Simulation,
    RuntimePreview,
    ConfigDraft,
    ConfigWrite,
    DeploymentPrepare
}

public sealed record AgentEvaluationPendingAction
{
    public string ActionType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public bool RequiresUserConfirmation { get; init; } = true;
}

public sealed record AgentEvaluationToolResult
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresUserConfirmation { get; init; }
    public List<AgentEvaluationPendingAction> PendingActions { get; init; } = new();

    public static AgentEvaluationToolResult Fail(
        string errorCode,
        string errorMessage,
        object? data = null)
    {
        return new AgentEvaluationToolResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Data = data
        };
    }
}

public sealed record AgentEngineeringEvaluationCase
{
    public string CaseId { get; init; } = string.Empty;
    public string UserRequest { get; init; } = string.Empty;
    public bool AllowRuntimePreview { get; init; }
    public EvaluationFlow Flow { get; init; } = new();
    public IReadOnlyList<MockToolResponse> MockToolResponses { get; init; } = [];
    public IReadOnlyList<EvaluationToolCall> ToolCalls { get; init; } = [];
    public IReadOnlyList<string> ExpectedToolCalls { get; init; } = [];
    public EvaluationFlowStructure ExpectedFlowStructure { get; init; } = new();
    public IReadOnlyList<string> ExpectedPendingActions { get; init; } = [];
    public AgentEvaluationValidationPreview ExpectedValidationPreview { get; init; } = new();
    public AgentEvaluationPermissionDecision ExpectedPermissionBehavior { get; init; } = new();
    public IReadOnlyList<string> ExpectedBlockingIssues { get; init; } = [];
    public bool ExpectedPassed { get; init; } = true;
    public string ExpectedPassFailReason { get; init; } = string.Empty;

    public override string ToString() => CaseId;
}

public sealed record MockToolResponse
{
    public string ToolName { get; init; } = string.Empty;
    public AgentEvaluationToolPermission Permission { get; init; } = AgentEvaluationToolPermission.ReadOnly;
    public bool Success { get; init; } = true;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresUserConfirmation { get; init; }
    public IReadOnlyList<AgentEvaluationPendingAction> PendingActions { get; init; } = [];

    public static MockToolResponse Ok(
        string toolName,
        object? data,
        AgentEvaluationToolPermission permission = AgentEvaluationToolPermission.ReadOnly,
        bool requiresUserConfirmation = false,
        IReadOnlyList<AgentEvaluationPendingAction>? pendingActions = null)
    {
        return new MockToolResponse
        {
            ToolName = toolName,
            Permission = permission,
            Success = true,
            Data = data,
            RequiresUserConfirmation = requiresUserConfirmation,
            PendingActions = pendingActions ?? []
        };
    }

    public static MockToolResponse Fail(
        string toolName,
        string errorCode,
        string errorMessage,
        object? data,
        AgentEvaluationToolPermission permission,
        IReadOnlyList<AgentEvaluationPendingAction>? pendingActions = null)
    {
        return new MockToolResponse
        {
            ToolName = toolName,
            Permission = permission,
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Data = data,
            PendingActions = pendingActions ?? []
        };
    }
}

public sealed record EvaluationToolCall
{
    public string ToolName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    public bool IncludeFlow { get; init; }
    public bool UseCapturedFrame { get; init; }
    public bool IncludeDryRunSummary { get; init; }
    public bool IncludeReplaySummary { get; init; }

    public static EvaluationToolCall Create(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        return new EvaluationToolCall
        {
            ToolName = toolName,
            Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public static EvaluationToolCall WithFlow(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        bool includeDryRunSummary = false,
        bool includeReplaySummary = false)
    {
        return new EvaluationToolCall
        {
            ToolName = toolName,
            Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            IncludeFlow = true,
            IncludeDryRunSummary = includeDryRunSummary,
            IncludeReplaySummary = includeReplaySummary
        };
    }

    public static EvaluationToolCall Replay(
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        return new EvaluationToolCall
        {
            ToolName = "replay_flow_with_frame",
            Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            IncludeFlow = true,
            UseCapturedFrame = true
        };
    }
}

public sealed record EvaluationFlow
{
    public string Kind { get; init; } = "final_flow";
    public IReadOnlyList<EvaluationOperator> Operators { get; init; } = [];
    public IReadOnlyList<EvaluationConnection> Connections { get; init; } = [];
    public IReadOnlyList<EvaluationMissingResource> MissingResources { get; init; } = [];

    public EvaluationFlowStructure ToStructure()
    {
        return new EvaluationFlowStructure
        {
            OperatorTypes = Operators.Select(item => item.OperatorType).ToList(),
            ConnectionCount = Connections.Count,
            ImageAcquisitionCount = Operators.Count(item =>
                string.Equals(item.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)),
            MissingResourceKeys = MissingResources
                .Select(item => $"{item.ResourceType}:{item.ResourceKey}")
                .ToList()
        };
    }
}

public sealed record EvaluationOperator
{
    public string TempId { get; init; } = string.Empty;
    public string OperatorType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record EvaluationConnection
{
    public string SourceTempId { get; init; } = string.Empty;
    public string SourcePortName { get; init; } = string.Empty;
    public string TargetTempId { get; init; } = string.Empty;
    public string TargetPortName { get; init; } = string.Empty;
}

public sealed record EvaluationMissingResource
{
    public string ResourceType { get; init; } = string.Empty;
    public string ResourceKey { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record EvaluationFlowStructure
{
    public IReadOnlyList<string> OperatorTypes { get; init; } = [];
    public int ConnectionCount { get; init; }
    public int ImageAcquisitionCount { get; init; }
    public IReadOnlyList<string> MissingResourceKeys { get; init; } = [];
}

public sealed record AgentEvaluationResult
{
    public string CaseId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public IReadOnlyList<AgentEvaluationToolCallResult> ActualToolCalls { get; init; } = [];
    public EvaluationFlowStructure ActualFlowStructure { get; init; } = new();
    public IReadOnlyList<string> ActualPendingActions { get; init; } = [];
    public AgentEvaluationValidationPreview ActualValidationPreview { get; init; } = new();
    public AgentEvaluationPermissionDecision ActualPermissionDecision { get; init; } = new();
    public IReadOnlyList<string> ActualBlockingIssues { get; init; } = [];
    public string? FailReason { get; init; }
    public string PassReason { get; init; } = string.Empty;
}

public sealed record AgentEvaluationToolCallResult
{
    public string ToolName { get; init; } = string.Empty;
    public string Permission { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ExecutedByMock { get; init; }
    public string? MockSource { get; init; }
}

public sealed record AgentEvaluationValidationPreview
{
    public string StructuralDryRunStatus { get; init; } = "not_run";
    public string FrameReplayStatus { get; init; } = "not_run";
    public string RuntimePackagePrecheckStatus { get; init; } = "not_run";
    public IReadOnlyList<string> ToolDryRunTrace { get; init; } = [];
}

public sealed record AgentEvaluationPermissionDecision
{
    public bool RuntimePreviewAllowed { get; init; }
    public IReadOnlyList<string> DeniedToolNames { get; init; } = [];
    public IReadOnlyList<string> RuntimePreviewExecutedToolNames { get; init; } = [];
    public IReadOnlyList<string> DeploymentPrepareExecutedToolNames { get; init; } = [];
}
