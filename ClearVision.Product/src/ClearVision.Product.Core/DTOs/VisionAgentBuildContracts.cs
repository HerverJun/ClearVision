namespace ClearVision.Product.Core.DTOs;

public sealed record VisionAgentBuildResult
{
    public string BuildId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string BuildIntent { get; init; } = string.Empty;
    public object? WorkflowDraft { get; init; }
    public List<VisionAgentOperatorPipelineStep> OperatorPipeline { get; init; } = [];
    public List<VisionAgentParameterMapping> ParameterMapping { get; init; } = [];
    public List<AiPendingParameterInfo> PendingParameters { get; init; } = [];
    public List<AiMissingResourceInfo> MissingResources { get; init; } = [];
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
    public string TempId { get; init; } = string.Empty;
    public string OperatorType { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string ValueSummary { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool Pending { get; init; }
    public string Impact { get; init; } = string.Empty;
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
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentBuildRepairRecord
{
    public string Stage { get; init; } = string.Empty;
    public string RepairReason { get; init; } = string.Empty;
    public string DiffSummary { get; init; } = string.Empty;
    public string ResultStatus { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}
