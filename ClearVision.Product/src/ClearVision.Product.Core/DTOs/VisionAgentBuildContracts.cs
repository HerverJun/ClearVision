namespace ClearVision.Product.Core.DTOs;

public sealed record VisionAgentBuildResult
{
    public string BuildId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = string.Empty;
    public string BuildIntent { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public string AnswerSetFingerprint { get; init; } = string.Empty;
    public string RequestedMode { get; init; } = AiAgentGenerateFlowModes.Scripted;
    public string EffectiveMode { get; init; } = AiAgentGenerateFlowModes.Scripted;
    public bool ToolLoopEntered { get; init; }
    public string FallbackReason { get; init; } = string.Empty;
    public List<string> ResolvedFields { get; init; } = [];
    public List<string> RemainingFields { get; init; } = [];
    public string SelectionSource { get; init; } = string.Empty;
    public string EffectiveRouteId { get; init; } = string.Empty;
    public List<string> EffectiveOperators { get; init; } = [];
    public bool StrategyConfirmed { get; init; }
    public string StrategyConfirmationSource { get; init; } = string.Empty;
    public List<string> UnresolvedStrategyBlockers { get; init; } = [];
    public string ParameterStrategy { get; init; } = string.Empty;
    public string ArtifactFingerprint { get; init; } = string.Empty;
    public string CompiledFingerprint { get; init; } = string.Empty;
    public string ValidationFingerprint { get; init; } = string.Empty;
    public string DryRunFingerprint { get; init; } = string.Empty;
    public string PrecheckFingerprint { get; init; } = string.Empty;
    public string ReturnedFlowSemanticFingerprint { get; init; } = string.Empty;
    public string CatalogVersion { get; init; } = string.Empty;
    public bool PlanSucceeded { get; init; }
    public bool CompilationSucceeded { get; init; }
    public bool RouteSemanticsSatisfied { get; init; }
    public string ArtifactDisposition { get; init; } = "blocked";
    public object? Flow { get; init; }
    public object? WorkflowDraft { get; init; }
    public List<VisionAgentOperatorPipelineStep> OperatorPipeline { get; init; } = [];
    public List<VisionAgentParameterMapping> ParameterMapping { get; init; } = [];
    public List<AiPendingParameterInfo> PendingParameters { get; init; } = [];
    public List<AiMissingResourceInfo> MissingResources { get; init; } = [];
    public List<VisionAgentGlobalVariableDraft> GlobalVariableDrafts { get; init; } = [];
    public List<VisionAgentGlobalVariableSourceBindingDraft> GlobalVariableSourceBindingDrafts { get; init; } = [];
    public List<VisionAgentGlobalVariableTargetBindingDraft> GlobalVariableTargetBindingDrafts { get; init; } = [];
    public List<VisionAgentGlobalVariableDiagnostic> GlobalVariableDiagnostics { get; init; } = [];
    public object? ValidationPreview { get; init; }
    public object? DryRunResult { get; init; }
    public object? ReadinessReport { get; init; }
    public object? StationCompatibilityReport { get; init; }
    public object? OperatorContractReport { get; init; }
    public object? ReleaseReview { get; init; }
    public VisionAgentWorkflowDiff WorkflowDiff { get; init; } = new();
    public VisionAgentApplyGate ApplyGate { get; init; } = new();
    public List<VisionAgentToolEvidence> ToolEvidenceTimeline { get; init; } = [];
    public List<VisionAgentBuildRepairRecord> AutoRepairs { get; init; } = [];
    public string FirstFixRecommendation { get; init; } = string.Empty;
    public List<string> PublicWarnings { get; init; } = [];
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentToolEvidence
{
    public string Stage { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string Source { get; init; } = "fixed_build_orchestrator";
    public string InputSummary { get; init; } = string.Empty;
    public string OutputSummary { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long DurationMs { get; init; }
    public string EvidenceId { get; init; } = string.Empty;
    public string RepairAction { get; init; } = string.Empty;
    public string WarningCode { get; init; } = string.Empty;
    public string ApplyImpact { get; init; } = string.Empty;
    public string DeploymentImpact { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
    public bool RedactionPass { get; init; } = true;
}

public sealed record VisionAgentOperatorPipelineStep
{
    public string TempId { get; init; } = string.Empty;
    public string OperatorType { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RepairNote { get; init; } = string.Empty;
}

public sealed record VisionAgentParameterMapping
{
    public string CanonicalKey { get; init; } = string.Empty;
    public string TempId { get; init; } = string.Empty;
    public string OperatorType { get; init; } = string.Empty;
    public string OperatorDisplayName { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string ParameterDisplayName { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string DataType { get; init; } = "string";
    public bool IsRequired { get; init; }
    public object? Value { get; init; }
    public bool HasExplicitValue { get; init; }
    public string ValueSummary { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool Pending { get; init; }
    public string Impact { get; init; } = string.Empty;
    public string SuggestedReason { get; init; } = string.Empty;
    public object? DefaultValue { get; init; }
    public object? MinValue { get; init; }
    public object? MaxValue { get; init; }
    public List<VisionAgentParameterOption> Options { get; init; } = [];
    public string RequiredPolicy { get; init; } = string.Empty;
    public string AtLeastOneGroup { get; init; } = string.Empty;
    public string MutuallyExclusiveGroup { get; init; } = string.Empty;
    public VisionAgentParameterConditionSet? RequiredWhen { get; init; }
    public VisionAgentParameterConditionSet? EnabledWhen { get; init; }
    public VisionAgentParameterConditionSet? DisabledWhen { get; init; }
    public string ResourceKind { get; init; } = string.Empty;
    public string ResourceCanonicalId { get; init; } = string.Empty;
    public bool ResourceDependent { get; init; }
}

public sealed record VisionAgentParameterOption
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed record VisionAgentParameterCondition
{
    public string Parameter { get; init; } = string.Empty;
    public string Comparison { get; init; } = string.Empty;
    public object? Value { get; init; }
}

public sealed record VisionAgentParameterConditionSet
{
    public List<VisionAgentParameterCondition> AllConditions { get; init; } = [];
    public List<VisionAgentParameterCondition> AnyConditions { get; init; } = [];
}

public sealed record VisionAgentGlobalVariableDraft
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ValueType { get; init; } = "String";
    public string InitialValueSummary { get; init; } = string.Empty;
    public bool ManualWriteAllowed { get; init; } = true;
    public bool IncludeInResultMetadata { get; init; }
    public string Source { get; init; } = "agent_suggestion";
    public string Rationale { get; init; } = string.Empty;
    public bool RequiresHumanConfirmation { get; init; } = true;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentGlobalVariableSourceBindingDraft
{
    public string VariableName { get; init; } = string.Empty;
    public string OperatorHint { get; init; } = string.Empty;
    public string OutputPortHint { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public bool RequiresHumanConfirmation { get; init; } = true;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentGlobalVariableTargetBindingDraft
{
    public string VariableName { get; init; } = string.Empty;
    public string OperatorHint { get; init; } = string.Empty;
    public string ParameterHint { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public bool RequiresHumanConfirmation { get; init; } = true;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentGlobalVariableDiagnostic
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = "info";
    public string VariableName { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentWorkflowDiff
{
    public List<string> AddedNodes { get; init; } = [];
    public List<string> ModifiedNodes { get; init; } = [];
    public List<string> PreservedNodes { get; init; } = [];
    public List<string> RemovedNodes { get; init; } = [];
    public List<string> AddedOrChangedParameters { get; init; } = [];
    public List<string> PendingParameters { get; init; } = [];
    public List<string> MissingResources { get; init; } = [];
    public List<string> ValidationFailures { get; init; } = [];
    public List<string> AutoRepairs { get; init; } = [];
    public List<string> DeploymentBlockers { get; init; } = [];
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentApplyGate
{
    public bool CanvasApplyReady { get; init; }
    public bool RuntimeDraftReady { get; init; }
    public bool DeploymentReady { get; init; }
    public bool Blocked { get; init; }
    public string Status { get; init; } = "blocked";
    public List<string> ApplyBlockers { get; init; } = [];
    public List<string> DeploymentBlockers { get; init; } = [];
    public string FirstFixRecommendation { get; init; } = string.Empty;
    public string ArtifactFingerprint { get; init; } = string.Empty;
    public string CompiledFingerprint { get; init; } = string.Empty;
    public string ValidationFingerprint { get; init; } = string.Empty;
    public string DryRunFingerprint { get; init; } = string.Empty;
    public string PrecheckFingerprint { get; init; } = string.Empty;
    public string ReturnedFlowSemanticFingerprint { get; init; } = string.Empty;
    public bool ArtifactFingerprintConsistent { get; init; }
    public bool RouteSemanticsSatisfied { get; init; }
    public string ArtifactDisposition { get; init; } = "blocked";
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentPortFingerprint
{
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool Required { get; init; }
}

public sealed record VisionAgentBuildRepairRecord
{
    public string Stage { get; init; } = string.Empty;
    public string RepairReason { get; init; } = string.Empty;
    public string DiffSummary { get; init; } = string.Empty;
    public string ResultStatus { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentBuildCheckV1
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Status { get; init; } = "pending";
    public string Summary { get; init; } = string.Empty;
    public int BlockerCount { get; init; }
    public int WarningCount { get; init; }
}

public sealed record VisionAgentBuildValidationV1
{
    public VisionAgentBuildCheckV1 Structural { get; init; } = new();
    public VisionAgentBuildCheckV1 DryRun { get; init; } = new();
    public VisionAgentBuildCheckV1 Manifest { get; init; } = new();
    public VisionAgentApplyGate ApplyGate { get; init; } = new();
    public bool HandoffEligible { get; init; }
    public string ReadinessStatus { get; init; } = "blocked";
    public string FirstFixRecommendation { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentPublicBuildResultV1
{
    public int SchemaVersion { get; init; } = 1;
    public string RunId { get; init; } = string.Empty;
    public string BuildId { get; init; } = string.Empty;
    public Guid? ClientOperationId { get; init; }
    public string BuildIdentity { get; init; } = string.Empty;
    public string SubmittedBuildFingerprint { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string AnswerSetFingerprint { get; init; } = string.Empty;
    public int AnswerRevision { get; init; }
    public int ResourceRevision { get; init; }
    public AiProjectBaselineIdentity? ProjectBaseline { get; init; }
    public string CandidateFlowFingerprint { get; init; } = string.Empty;
    public int OperatorCount { get; init; }
    public int ConnectionCount { get; init; }
    public List<VisionAgentOperatorPipelineStep> OperatorPipeline { get; init; } = [];
    public List<VisionAgentParameterMapping> ParameterMapping { get; init; } = [];
    public List<AiMissingResourceInfo> MissingResources { get; init; } = [];
    public VisionAgentWorkflowDiff WorkflowDiff { get; init; } = new();
    public VisionAgentBuildValidationV1 Validation { get; init; } = new();
    public List<VisionAgentToolEvidence> PublicTimeline { get; init; } = [];
    public List<string> PublicWarnings { get; init; } = [];
    public bool MetadataOnly { get; init; } = true;
    public bool RedactionPass { get; init; } = true;
}

public sealed record VisionAgentBuildRevalidationRequest
{
    public string CandidateFlowJson { get; init; } = string.Empty;
    public VisionAgentPublicBuildResultV1 Build { get; init; } = new();
    public Dictionary<string, System.Text.Json.JsonElement> ParameterValues { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<VisionAgentResourceDecision> ResourceDecisions { get; init; } = [];
    public int AnswerRevision { get; init; }
    public int ResourceRevision { get; init; }
}
