using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildPlanContextLoader
{
    private readonly IAgentRunEventSink? _eventSink;
    private readonly VisionAgentPlanAnswerValidator _answerValidator;
    private readonly VisionAgentPlanRequirementOverlay _requirementOverlay;

    public BuildPlanContextLoader(
        IAgentRunEventSink? eventSink = null,
        VisionAgentPlanAnswerValidator? answerValidator = null,
        VisionAgentPlanRequirementOverlay? requirementOverlay = null)
    {
        _eventSink = eventSink;
        _answerValidator = answerValidator ?? new VisionAgentPlanAnswerValidator();
        _requirementOverlay = requirementOverlay ?? new VisionAgentPlanRequirementOverlay();
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
                "计划哈希不一致",
                "构建会继续使用公开计划快照；应用前请复核计划来源。",
                new
                {
                    warningCode = "plan_hash_mismatch",
                    planId = plan?.PlanId ?? build?.PlanId ?? string.Empty,
                    providedPlanHash = provided,
                    computedPlanHash = computed,
                    publicDiagnosticsOnly = true,
                    metadataOnly = true,
                    redactionPass = true
                });
        }

        var currentFlowSnapshot = VisionAgentBuildSupport.FirstNonEmpty(build?.CurrentFlowSnapshot, request.ExistingFlowJson);
        var templateSelection = build?.TemplateSelection ?? request.TemplateSelection ?? plan?.TemplateSelection;
        var requirementMode = NormalizeRequirementMode(request.RequirementMode);
        var maturityRequest = new VisionAgentRequirementMaturityRequest
        {
            Description = build?.OriginalUserPrompt ?? request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = build?.BuildIntent ?? request.Mode.ToWireValue(),
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                             !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot),
            HasPendingPlan = plan != null,
            TemplateSelection = templateSelection,
            RequirementMode = requirementMode
        };
        var validatedAnswers = _answerValidator.Validate(
            plan,
            build?.ConfirmedAnswers,
            build?.UserSelections,
            build?.AcceptedRecommendedDefaults == true);
        var effectiveRequirement = _requirementOverlay.Build(plan, validatedAnswers, maturityRequest);
        validatedAnswers.RequirementAnswers.TryGetValue(
            VisionAgentPlanAnswerFields.TaskType,
            out var answerTaskType);
        var taskType = VisionTaskRouteContractRegistry.NormalizeTaskType(
            VisionAgentBuildSupport.FirstNonEmpty(
                effectiveRequirement.Maturity.TaskType,
                answerTaskType,
                plan?.SemanticExtraction?.TaskType));
        var payload = new BuildPlanLoad
        {
            PlanId = VisionAgentBuildSupport.Clean(plan?.PlanId) is { Length: > 0 } planId
                ? planId
                : VisionAgentBuildSupport.Clean(build?.PlanId),
            PlanHash = string.IsNullOrWhiteSpace(computed)
                ? VisionAgentBuildSupport.Clean(plan?.PlanHash)
                : computed,
            ComputedPlanHash = computed,
            Plan = plan,
            UserSelections = build?.UserSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ConfirmedAnswers = build?.ConfirmedAnswers ?? [],
            ValidatedPlanAnswers = validatedAnswers,
            EffectiveRequirement = effectiveRequirement,
            RequirementAnswers = validatedAnswers.RequirementAnswers,
            BuildDecisions = validatedAnswers.BuildDecisions,
            ParameterSelections = validatedAnswers.ParameterSelections,
            ParameterValues = build?.ParameterValues ??
                new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase),
            ResolvedFields = effectiveRequirement.ResolvedFields
                .Concat(validatedAnswers.ResolvedFields)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RemainingFields = effectiveRequirement.RemainingFields,
            AnswerSetFingerprint = validatedAnswers.AnswerSetFingerprint,
            RequirementMode = requirementMode,
            AcceptedDefaults = build?.AcceptedDefaults ?? [],
            AcceptedRecommendedDefaults = build?.AcceptedRecommendedDefaults ?? false,
            ResourceDecisions = build?.ResourceDecisions ?? [],
            CurrentFlowSnapshot = currentFlowSnapshot,
            TemplateSelection = templateSelection,
            AttachmentSummary = build?.AttachmentSummary ?? new VisionAgentAttachmentSummary(),
            OperatorCatalogVersion = VisionAgentBuildSupport.FirstNonEmpty(build?.OperatorCatalogVersion, plan?.OperatorCatalogVersion),
            StationBoundarySummary = VisionAgentBuildSupport.FirstNonEmpty(build?.StationBoundarySummary, plan?.StationBoundarySummary),
            PlcOutputPolicy = VisionAgentBuildSupport.FirstNonEmpty(build?.PlcOutputPolicy, plan?.PlcOutputPolicy),
            OriginalUserPrompt = VisionAgentBuildSupport.FirstNonEmpty(build?.OriginalUserPrompt, plan?.OriginalUserPrompt, request.Description),
            BuildIntentHint = build?.BuildIntent ?? request.Mode.ToWireValue(),
            TaskType = taskType,
            HashMismatch = hashMismatch,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(currentFlowSnapshot)
        };

        return VisionAgentBuildSupport.StepResult(
            payload,
            hashMismatch
                ? "计划哈希不一致，已拒绝继续构建。"
                : "计划快照和结构化 BuildFromPlan 上下文已加载。",
            hashMismatch ? AgentRunEventStatuses.Failed : AgentRunEventStatuses.Completed,
            new
            {
                planId = payload.PlanId,
                planHash = payload.PlanHash,
                hashMismatch,
                userSelectionCount = payload.UserSelections.Count,
                confirmedAnswerCount = payload.ConfirmedAnswers.Count,
                acceptedAnswerCount = payload.ValidatedPlanAnswers.AcceptedAnswers.Count,
                invalidQuestionIds = payload.ValidatedPlanAnswers.InvalidQuestionIds,
                invalidValues = payload.ValidatedPlanAnswers.InvalidValues,
                conflictedFields = payload.ValidatedPlanAnswers.ConflictedFields,
                resolvedFields = payload.ResolvedFields,
                remainingFields = payload.RemainingFields,
                answerSetFingerprint = payload.AnswerSetFingerprint,
                requirementMode = payload.RequirementMode,
                acceptedDefaultCount = payload.AcceptedDefaults.Count,
                hasCurrentFlow = payload.HasCurrentFlow,
                templateSelectionMode = payload.TemplateSelection?.Mode ?? string.Empty,
                templateId = payload.TemplateSelection?.TemplateId ?? string.Empty,
                metadataOnly = true
            },
            warningCode: hashMismatch ? "plan_hash_mismatch" : string.Empty,
            applyImpact: hashMismatch ? "blocked" : "editable_draft_allowed",
            deploymentImpact: hashMismatch ? "blocked" : "no_deployment_blocker");
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

    private static string NormalizeRequirementMode(string? value)
    {
        return string.Equals(value, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
            ? AiRequirementModes.Draft
            : AiRequirementModes.Strict;
    }
}
