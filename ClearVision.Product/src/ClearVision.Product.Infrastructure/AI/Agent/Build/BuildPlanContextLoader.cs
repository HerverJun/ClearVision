using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildPlanContextLoader
{
    private readonly IAgentRunEventSink? _eventSink;

    public BuildPlanContextLoader(IAgentRunEventSink? eventSink = null)
    {
        _eventSink = eventSink;
    }

    internal BuildStepResult<BuildPlanLoad> Load(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        VisionAgentPlanModeResult? plan,
        List<string> publicWarnings)
    {
        var computed = VisionAgentOrchestrator.ComputePlanHash(plan);
        var provided = VisionAgentBuildSupport.Clean(build?.PlanHash);
        var hashMismatch = plan != null &&
                           !string.IsNullOrWhiteSpace(provided) &&
                           !string.IsNullOrWhiteSpace(computed) &&
                           !string.Equals(provided, computed, StringComparison.OrdinalIgnoreCase);
        if (hashMismatch)
        {
            publicWarnings.Add("plan_hash_mismatch");
            _eventSink?.StageCompleted(
                request.AgentRunId,
                "plan_hash_validation",
                "Plan hash mismatch detected",
                "Build is continuing with the public plan snapshot; review plan provenance before applying.",
                new
                {
                    warningCode = "plan_hash_mismatch",
                    planId = build?.PlanId ?? plan?.PlanId ?? string.Empty,
                    providedPlanHash = provided,
                    computedPlanHash = computed,
                    publicDiagnosticsOnly = true,
                    metadataOnly = true,
                    redactionPass = true
                });
        }

        var currentFlowSnapshot = VisionAgentBuildSupport.FirstNonEmpty(build?.CurrentFlowSnapshot, request.ExistingFlowJson);
        var templateSelection = build?.TemplateSelection ?? request.TemplateSelection ?? plan?.TemplateSelection;
        var payload = new BuildPlanLoad
        {
            PlanId = VisionAgentBuildSupport.Clean(build?.PlanId) is { Length: > 0 } planId
                ? planId
                : VisionAgentBuildSupport.Clean(plan?.PlanId),
            PlanHash = string.IsNullOrWhiteSpace(provided)
                ? VisionAgentBuildSupport.Clean(plan?.PlanHash)
                : provided,
            ComputedPlanHash = computed,
            Plan = plan,
            UserSelections = build?.UserSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AcceptedDefaults = build?.AcceptedDefaults ?? [],
            AcceptedRecommendedDefaults = build?.AcceptedRecommendedDefaults ?? false,
            CurrentFlowSnapshot = currentFlowSnapshot,
            TemplateSelection = templateSelection,
            AttachmentSummary = build?.AttachmentSummary ?? new VisionAgentAttachmentSummary(),
            OperatorCatalogVersion = VisionAgentBuildSupport.FirstNonEmpty(build?.OperatorCatalogVersion, plan?.OperatorCatalogVersion),
            StationBoundarySummary = VisionAgentBuildSupport.FirstNonEmpty(build?.StationBoundarySummary, plan?.StationBoundarySummary),
            PlcOutputPolicy = VisionAgentBuildSupport.FirstNonEmpty(build?.PlcOutputPolicy, plan?.PlcOutputPolicy),
            OriginalUserPrompt = VisionAgentBuildSupport.FirstNonEmpty(build?.OriginalUserPrompt, plan?.OriginalUserPrompt, request.Description),
            BuildIntentHint = build?.BuildIntent ?? request.Mode.ToWireValue(),
            HashMismatch = hashMismatch,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(currentFlowSnapshot)
        };

        return VisionAgentBuildSupport.StepResult(
            payload,
            hashMismatch
                ? "Plan loaded with plan_hash_mismatch warning."
                : "Plan snapshot and structured BuildFromPlan context loaded.",
            AgentRunEventStatuses.Completed,
            new
            {
                planId = payload.PlanId,
                planHash = payload.PlanHash,
                hashMismatch,
                userSelectionCount = payload.UserSelections.Count,
                acceptedDefaultCount = payload.AcceptedDefaults.Count,
                hasCurrentFlow = payload.HasCurrentFlow,
                templateSelectionMode = payload.TemplateSelection?.Mode ?? string.Empty,
                templateId = payload.TemplateSelection?.TemplateId ?? string.Empty,
                metadataOnly = true
            },
            warningCode: hashMismatch ? "plan_hash_mismatch" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: hashMismatch ? "requires_plan_provenance_review" : "no_deployment_blocker");
    }

    internal VisionAgentToolContext BuildToolContext(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        string? currentFlowSnapshot)
    {
        return new VisionAgentToolContext
        {
            UserDescription = VisionAgentBuildSupport.FirstNonEmpty(build?.OriginalUserPrompt, request.Description),
            AdditionalContext = request.AdditionalContext,
            SessionId = request.SessionId,
            AgentRunId = request.AgentRunId,
            ExistingFlowJson = currentFlowSnapshot,
            DebugTrace = false,
            RuntimePreviewConsent = false,
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };
    }
}
