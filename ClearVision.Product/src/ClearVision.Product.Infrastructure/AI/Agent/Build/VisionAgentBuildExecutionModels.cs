using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record BuildStepResult<T>(
    T Payload,
    string OutputSummary,
    string Status,
    object? PayloadDetails,
    string WarningCode = "",
    string RepairAction = "",
    string ApplyImpact = "",
    string DeploymentImpact = "");

internal sealed record BuildPlanLoad
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string ComputedPlanHash { get; init; } = string.Empty;
    public VisionAgentPlanModeResult? Plan { get; init; }
    public IReadOnlyDictionary<string, string> UserSelections { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> AcceptedDefaults { get; init; } = [];
    public bool AcceptedRecommendedDefaults { get; init; }
    public string CurrentFlowSnapshot { get; init; } = string.Empty;
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
    public string OperatorCatalogVersion { get; init; } = string.Empty;
    public string StationBoundarySummary { get; init; } = string.Empty;
    public string PlcOutputPolicy { get; init; } = string.Empty;
    public string OriginalUserPrompt { get; init; } = string.Empty;
    public string BuildIntentHint { get; init; } = string.Empty;
    public bool HashMismatch { get; init; }
    public bool HasCurrentFlow { get; init; }
}

internal sealed record BuildIntentResolution(string BuildIntent);

internal sealed record TemplateStrategyResolution(
    string Strategy,
    string TemplateId,
    string ScenarioKey,
    object? TemplateSkeleton,
    string GenerationMode,
    string TemplateLockLevel,
    bool RequiredTemplateMissing = false,
    string MissingTemplateResourceKey = "");

internal sealed record OperatorPipelineResolution(
    List<VisionAgentOperatorPipelineStep> Steps,
    List<string> InvalidOperators);

internal sealed record ParameterMappingResolution(
    List<VisionAgentParameterMapping> Mappings,
    List<AiPendingParameterInfo> PendingParameters,
    List<AiMissingResourceInfo> MissingResources);

internal sealed record CanonicalDraft(
    object WorkflowDraft,
    string EntryOperatorTempId,
    List<string> AddedNodeIds,
    int ConnectionCount);

internal sealed record DraftWorkflowResolution(
    AiFlowGenerationResult GenerationResult,
    object WorkflowDraft,
    string EntryOperatorTempId,
    OperatorFlowDto CanvasFlow,
    List<string> AddedNodeIds);

internal sealed record RepairDraftResolution(
    DraftWorkflowResolution Draft,
    VisionAgentBuildRepairRecord Record);

internal sealed record StationCompatibilityResolution(object Report);

internal sealed record OperatorContractResolution(object Report);

internal sealed record ReleaseReviewResolution(object Report);

internal sealed record TemplateCandidate(
    string TemplateId,
    string ScenarioKey,
    double Score);

internal sealed record BuildResultAssemblyInput(
    string? RunId,
    string BuildId,
    AiFlowGenerationRequest Request,
    BuildPlanLoad LoadPlan,
    BuildIntentResolution Intent,
    TemplateStrategyResolution Template,
    OperatorPipelineResolution Pipeline,
    ParameterMappingResolution ParameterMapping,
    DraftWorkflowResolution CurrentDraft,
    VisionAgentToolResult Validation,
    VisionAgentToolResult DryRun,
    VisionAgentToolResult PackageReadiness,
    StationCompatibilityResolution StationCompatibility,
    OperatorContractResolution OperatorContract,
    ReleaseReviewResolution ReleaseReview,
    VisionAgentWorkflowDiff WorkflowDiff,
    VisionAgentApplyGate ApplyGate,
    IReadOnlyList<VisionAgentToolEvidence> Evidence,
    IReadOnlyList<VisionAgentBuildRepairRecord> AutoRepairs,
    IReadOnlyList<string> PublicWarnings);
