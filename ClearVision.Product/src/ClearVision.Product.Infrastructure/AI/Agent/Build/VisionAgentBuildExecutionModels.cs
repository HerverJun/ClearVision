using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using System.Text.Json;

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
    public IReadOnlyList<VisionAgentPlanAnswer> ConfirmedAnswers { get; init; } = [];
    public VisionAgentPlanAnswerValidationResult ValidatedPlanAnswers { get; init; } =
        new([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [], [], [], [], string.Empty, []);
    public VisionAgentEffectiveRequirement EffectiveRequirement { get; init; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new AiRequirementMaturityResult(),
            [], []);
    public IReadOnlyDictionary<string, string> RequirementAnswers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> BuildDecisions { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> ParameterSelections { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, JsonElement> ParameterValues { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ResolvedFields { get; init; } = [];
    public IReadOnlyList<string> RemainingFields { get; init; } = [];
    public string AnswerSetFingerprint { get; init; } = string.Empty;
    public string RequirementMode { get; init; } = AiRequirementModes.Strict;
    public IReadOnlyList<string> AcceptedDefaults { get; init; } = [];
    public bool AcceptedRecommendedDefaults { get; init; }
    public IReadOnlyList<VisionAgentResourceDecision> ResourceDecisions { get; init; } = [];
    public string CurrentFlowSnapshot { get; init; } = string.Empty;
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
    public string OperatorCatalogVersion { get; init; } = string.Empty;
    public string StationBoundarySummary { get; init; } = string.Empty;
    public string PlcOutputPolicy { get; init; } = string.Empty;
    public string OriginalUserPrompt { get; init; } = string.Empty;
    public string BuildIntentHint { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
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

internal sealed record PlanSelectionResolution(
    VisionAgentRecommendedRoute EffectiveRoute,
    string SelectionSource,
    string Strategy,
    bool StrategyConfirmed,
    string StrategyConfirmationSource,
    List<string> UnresolvedStrategyBlockers,
    string ParameterStrategy,
    List<string> BlockingReasons,
    List<string> Evidence);

internal sealed record OperatorPipelineResolution(
    List<VisionAgentOperatorPipelineStep> Steps,
    List<string> InvalidOperators);

internal sealed record ParameterMappingResolution(
    List<VisionAgentParameterMapping> Mappings,
    List<AiPendingParameterInfo> PendingParameters,
    List<AiMissingResourceInfo> MissingResources,
    string ParameterStrategy);

internal sealed record CanonicalDraft(
    object WorkflowDraft,
    string EntryOperatorTempId,
    List<string> AddedNodeIds,
    int ConnectionCount);

internal sealed record CanonicalWorkflowGraph(
    IReadOnlyList<CanonicalWorkflowNode> Nodes,
    IReadOnlyList<CanonicalWorkflowConnection> Connections,
    string EntryOperatorTempId);

internal sealed record CanonicalWorkflowNode(
    string TempId,
    string OperatorType,
    string DisplayName,
    IReadOnlyDictionary<string, string?> Parameters,
    IReadOnlyList<VisionAgentPortFingerprint> InputPorts,
    IReadOnlyList<VisionAgentPortFingerprint> OutputPorts);

internal sealed record CanonicalWorkflowConnection(
    string SourceTempId,
    string SourcePortName,
    string TargetTempId,
    string TargetPortName);

internal sealed record CompiledWorkflowArtifact(
    CanonicalWorkflowGraph Graph,
    object WorkflowDraft,
    OperatorFlowDto CanvasProjection,
    string ArtifactFingerprint,
    string CatalogVersion,
    string ReturnedFlowSemanticFingerprint);

internal sealed record DraftWorkflowResolution(
    AiFlowGenerationResult GenerationResult,
    object WorkflowDraft,
    string EntryOperatorTempId,
    OperatorFlowDto CanvasFlow,
    List<string> AddedNodeIds,
    CompiledWorkflowArtifact Artifact);

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
    PlanSelectionResolution Selection,
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
    VisionTaskRouteAssessment RouteAssessment,
    IReadOnlyList<VisionAgentToolEvidence> Evidence,
    IReadOnlyList<VisionAgentBuildRepairRecord> AutoRepairs,
    IReadOnlyList<string> PublicWarnings);
