namespace ClearVision.Product.Core.DTOs;

public sealed record BuildCommand
{
    public required AiFlowGenerationRequest Request { get; init; }
    public string? RunId { get; init; }
    public string? RequestId { get; init; }
    public string Transport { get; init; } = BuildCommandTransports.Internal;
    public bool PersistResult { get; init; } = true;

    public static BuildCommand FromGenerationRequest(
        AiFlowGenerationRequest request,
        string? runId = null,
        string? requestId = null,
        string transport = BuildCommandTransports.Internal,
        bool persistResult = true)
    {
        return new BuildCommand
        {
            Request = request,
            RunId = runId ?? request.AgentRunId,
            RequestId = requestId,
            Transport = string.IsNullOrWhiteSpace(transport) ? BuildCommandTransports.Internal : transport,
            PersistResult = persistResult
        };
    }
}

public static class BuildCommandTransports
{
    public const string Internal = "internal";
    public const string AgentRun = "agent_run";
    public const string WebMessage = "web_message";
}

public sealed record CanonicalBuildOutcome
{
    public required AiFlowGenerationResult Result { get; init; }
    public string RunId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public string Transport { get; init; } = BuildCommandTransports.Internal;
    public string CompletionStatus { get; init; } = AiFlowGenerationResult.CompletionStatusFailed;
    public string FailureType { get; init; } = string.Empty;
    public string FailureCode { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = string.Empty;
    public string AnswerSetFingerprint { get; init; } = string.Empty;
    public string RequestedMode { get; init; } = AiAgentGenerateFlowModes.Scripted;
    public string EffectiveMode { get; init; } = AiAgentGenerateFlowModes.Scripted;
    public bool ToolLoopEntered { get; init; }
    public string FallbackReason { get; init; } = string.Empty;
    public VisionAgentBuildReadinessSnapshot? BuildReadiness { get; init; }
    public VisionAgentWorkflowDiff? WorkflowDiff { get; init; }
    public VisionAgentApplyGate? ApplyGate { get; init; }
    public bool Persisted { get; init; }
}

public static class VisionAgentBuildFailureCodes
{
    public const string Disabled = "build_from_plan_disabled";
    public const string ContractInvalid = "build_from_plan_contract_invalid";
    public const string PlanIdMismatch = "build_from_plan_plan_id_mismatch";
    public const string PlanHashMissing = "build_from_plan_plan_hash_missing";
    public const string StalePlan = "build_from_plan_stale_plan";
    public const string ReadinessBlocked = "build_from_plan_readiness_blocked";
    public const string BuildOrchestratorNotRegistered = "build_orchestrator_not_registered";
    public const string SystemException = "build_from_plan_system_exception";
    public const string Cancelled = "build_cancelled";
}
