using System.Text.Json.Serialization;

namespace ClearVision.Product.Core.DTOs;

public sealed record BuildCommand
{
    public required AiFlowGenerationRequest Request { get; init; }
    public string? RunId { get; init; }
    public string? RequestId { get; init; }
    public string Transport { get; init; } = BuildCommandTransports.Internal;
    public bool PersistResult { get; init; } = true;
    public bool BuildAssociationPrepared { get; init; }

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

public sealed record VisionAgentBuildReadinessPreviewRequest
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public VisionAgentPlanModeResult? PlanSnapshot { get; init; }
    public string RequirementMode { get; init; } = AiRequirementModes.Strict;
    public List<VisionAgentPlanAnswer> ConfirmedAnswers { get; init; } = [];
    public Dictionary<string, string> UserSelections { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<string> AcceptedDefaults { get; init; } = [];
    public bool AcceptedRecommendedDefaults { get; init; }
    public int AnswerRevision { get; init; }
    public int ResourceRevision { get; init; }
    public List<VisionAgentResourceDecision> ResourceDecisions { get; init; } = [];
    public string? AdditionalContext { get; init; }
    public string? CurrentFlowSnapshot { get; init; }
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
    public string OperatorCatalogVersion { get; init; } = string.Empty;
    public string StationBoundarySummary { get; init; } = string.Empty;
    public string PlcOutputPolicy { get; init; } = string.Empty;
    public string BuildIntent { get; init; } = "new";
    public string OriginalUserPrompt { get; init; } = string.Empty;
    public AiRequirementMaturityResult? RequirementMaturity { get; init; }
    public AiDecisionTrace? DecisionTrace { get; init; }
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentBuildReadinessPreviewResult
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string RequirementMode { get; init; } = AiRequirementModes.Strict;
    public int AnswerRevision { get; init; }
    public int ResourceRevision { get; init; }
    public List<VisionAgentPlanAnswer> AcceptedAnswers { get; init; } = [];
    public string AnswerSetFingerprint { get; init; } = string.Empty;
    public VisionAgentBuildReadinessSnapshot BuildReadiness { get; init; } = new();
    public List<string> DeferredQuestionIds { get; init; } = [];
    public int PendingConfirmationCount { get; init; }
    public int ResourcePendingCount { get; init; }
    public int HardBlockerCount { get; init; }
    [JsonPropertyName("buildBlockingConfirmationCount")]
    public int BuildBlockingConfirmationCount { get; init; }
    [JsonPropertyName("buildRequiredResourceCount")]
    public int BuildRequiredResourceCount { get; init; }
    [JsonPropertyName("deferredFieldCount")]
    public int DeferredFieldCount { get; init; }
    [JsonPropertyName("draftAllowedResourceCount")]
    public int DraftAllowedResourceCount { get; init; }
    [JsonPropertyName("mustConfirmBeforeBuildCount")]
    public int MustConfirmBeforeBuildCount { get; init; }
    [JsonPropertyName("fillLaterCount")]
    public int FillLaterCount { get; init; }
    [JsonPropertyName("totalIncompleteCount")]
    public int TotalIncompleteCount { get; init; }
    public bool ContractValid { get; init; } = true;
    public string FailureCode { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
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
