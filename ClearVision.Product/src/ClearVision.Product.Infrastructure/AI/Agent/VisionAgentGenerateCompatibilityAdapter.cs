using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI.Agent;

/// <summary>
/// Production compatibility entry for the historical GenerateFlow request.
/// It owns no workflow materialization; all artifacts are produced by the
/// official Plan -> BuildRun -> BuildApplication chain.
/// </summary>
public interface IVisionAgentGenerateCompatibilityAdapter
{
    Task<AiFlowGenerationResult> GenerateAsync(
        AiFlowGenerationRequest request,
        Action<string>? onProgress,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentGenerateCompatibilityAdapter : IVisionAgentGenerateCompatibilityAdapter
{
    private readonly IVisionAgentOrchestrator _orchestrator;
    private readonly IVisionAgentBuildApplicationService _buildApplicationService;
    private readonly IVisionAgentBuildRunService _buildRunService;
    private readonly IAgentRunEventStreamService _agentRunStreamService;
    private readonly IConversationalFlowService _conversationService;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateCompatibilityAdapter> _logger;

    public VisionAgentGenerateCompatibilityAdapter(
        IVisionAgentOrchestrator orchestrator,
        IVisionAgentBuildApplicationService buildApplicationService,
        IVisionAgentBuildRunService buildRunService,
        IAgentRunEventStreamService agentRunStreamService,
        IConversationalFlowService conversationService,
        Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateCompatibilityAdapter> logger)
    {
        _orchestrator = orchestrator;
        _buildApplicationService = buildApplicationService;
        _buildRunService = buildRunService;
        _agentRunStreamService = agentRunStreamService;
        _conversationService = conversationService;
        _logger = logger;
    }

    public async Task<AiFlowGenerationResult> GenerateAsync(
        AiFlowGenerationRequest request,
        Action<string>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var ownerHash = ConversationOwnerAuthority.Require(request.OwnerHash);
        var session = _conversationService.GetOrCreateSession(ownerHash, request.SessionId);
        var planRequest = BuildPlanRequest(request, session);
        var initialPersistence = _conversationService.TryInitializeWorkspaceSnapshot(
            ownerHash,
            session.SessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = session.WorkspaceSnapshot?.Revision,
                RequireExpectedRevisionWhenWorkspaceExists = session.WorkspaceSnapshot != null,
                ClientMutationId = $"generate-plan:{Guid.NewGuid():N}:start",
                LifecycleState = "planning",
                RequirementMode = request.RequirementMode,
                ConfirmedPlanAnswers = planRequest.ConfirmedPlanAnswers,
                UserTurnId = $"generate-plan:{Guid.NewGuid():N}:user",
                UserMessage = request.Description
            });
        if (!initialPersistence.Success)
        {
            return PersistenceFailure(
                request,
                session.SessionId,
                initialPersistence,
                "generate_plan_persistence_failed",
                "普通 Generate 已停止：正式 Plan 尚未成功保存。");
        }

        onProgress?.Invoke("正在创建正式 Plan，并验证构建就绪状态...");

        VisionAgentPlanModeResult plan;
        try
        {
            plan = await _orchestrator.CreatePlanAsync(planRequest, cancellationToken);
        }
        catch (VisionAgentPlanningDeadlineExceededException ex)
        {
            return Failure(
                request,
                session.SessionId,
                VisionAgentBuildFailureCodes.SystemException,
                $"正式 Plan 规划超时：{ex.Message}",
                AiFlowGenerationResult.FailureTypeTimeout,
                AiFlowGenerationResult.CompletionStatusTimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request,
                session.SessionId,
                VisionAgentBuildFailureCodes.Cancelled,
                "普通 Generate 已取消。",
                AiFlowGenerationResult.FailureTypeUserCancelled,
                AiFlowGenerationResult.CompletionStatusCancelled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Production GenerateFlow Plan creation failed.");
            return Failure(
                request,
                session.SessionId,
                "generate_plan_failed",
                "正式 Plan 创建失败，未进入任何旧 GenerateFlow 物化路径。",
                AiFlowGenerationResult.FailureTypeSystemError,
                AiFlowGenerationResult.CompletionStatusFailed);
        }

        var canonicalPlan = NormalizePlanHash(plan);
        var planPersistence = _conversationService.TryUpdateWorkspaceSnapshot(
            ownerHash,
            session.SessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = initialPersistence.Snapshot?.Revision,
                RequireExpectedRevisionWhenWorkspaceExists = true,
                ClientMutationId = $"generate-plan:{canonicalPlan.PlanId}:completed:{canonicalPlan.PlanHash}",
                LifecycleState = canonicalPlan.CanBuild ? "plan_ready" : "plan_blocked",
                PendingPlanSnapshot = canonicalPlan,
                PlanRunStatus = AgentRunEventStatuses.Completed,
                RequirementMode = request.RequirementMode,
                ConfirmedPlanAnswers = canonicalPlan.ConfirmedPlanAnswers.Count > 0
                    ? canonicalPlan.ConfirmedPlanAnswers
                    : planRequest.ConfirmedPlanAnswers
            });
        if (!planPersistence.Success)
        {
            return AttachPlan(
                PersistenceFailure(
                request,
                session.SessionId,
                planPersistence,
                "generate_plan_terminal_persistence_failed",
                "正式 Plan 已生成但未能保存，已阻止 BuildRun。"),
                canonicalPlan);
        }

        var readiness = await _buildApplicationService.PreviewBuildReadinessAsync(
            BuildReadinessRequest(request, canonicalPlan),
            cancellationToken);
        var baseResult = PlanResult(request, session.SessionId, canonicalPlan, readiness);
        var canBuild = request.Mode != GenerateFlowMode.Explain &&
                       readiness.ContractValid &&
                       canonicalPlan.CanBuild &&
                       readiness.BuildReadiness.CanBuild;

        // Readiness is part of the persisted Plan -> Build contract even when the
        // same user action continues directly into BuildRun.  The association must
        // use the revision created by this write, never the earlier Plan revision.
        var readinessPersistence = _conversationService.TryUpdateWorkspaceSnapshot(
            ownerHash,
            session.SessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = planPersistence.Snapshot?.Revision,
                RequireExpectedRevisionWhenWorkspaceExists = true,
                ClientMutationId = $"generate-plan:{canonicalPlan.PlanId}:readiness:{readiness.AnswerSetFingerprint}",
                LifecycleState = canBuild ? "plan_ready" : "plan_blocked",
                PendingPlanSnapshot = canonicalPlan,
                ReadinessPreview = readiness,
                MissingResources = readiness.BuildReadiness.MissingResources,
                RequirementMode = request.RequirementMode,
                ConfirmedPlanAnswers = readiness.AcceptedAnswers
            });
        if (!readinessPersistence.Success)
        {
            baseResult.Success = false;
            baseResult.ErrorMessage = "正式 Plan 已生成，但 readiness 保存失败，已阻止 BuildRun。";
            baseResult.FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_generate_compatibility",
                Code = "generate_readiness_persistence_failed",
                Message = "正式 Plan 已生成，但 readiness 保存失败，已阻止 BuildRun。",
                RepairTarget = "请刷新 Plan 工作台并重试。"
            };
            return baseResult;
        }

        if (!canBuild)
        {
            return baseResult;
        }

        var build = BuildRequest(request, canonicalPlan, readiness, readinessPersistence.Snapshot?.Revision);
        var createResult = _agentRunStreamService.CreateRun(
            request.Description,
            new
            {
                runKind = VisionAgentRunKindResolver.Build,
                source = "generate_compatibility_adapter",
                transport = BuildCommandTransports.Internal,
                sessionId = session.SessionId,
                planId = canonicalPlan.PlanId,
                planHash = canonicalPlan.PlanHash,
                metadataOnly = true
            },
            ownerHash);
        var runRequest = request with
        {
            AgentRunId = createResult.RunId,
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted,
            BuildFromPlan = build
        };
        var command = BuildCommand.FromGenerationRequest(
            runRequest,
            createResult.RunId,
            transport: BuildCommandTransports.Internal,
            persistResult: false);
        var association = _buildRunService.PrepareBuildAssociation(command);
        if (!association.Success)
        {
            var result = PersistenceFailure(
                runRequest,
                session.SessionId,
                association,
                association.ErrorCode,
                association.PublicMessage);
            _agentRunStreamService.Fail(
                createResult.RunId,
                result.ErrorMessage ?? "Build association failed.",
                result.FailureSummary?.RepairTarget ?? "请确认最新 Plan 状态后重试。",
                new
                {
                    source = "generate_compatibility_adapter",
                    planId = canonicalPlan.PlanId,
                    planHash = canonicalPlan.PlanHash,
                    metadataOnly = true
                });
            result.PlanSnapshot = canonicalPlan;
            result.AgentRunId = createResult.RunId;
            return result;
        }

        onProgress?.Invoke("正式 Plan 已通过 readiness，正在进入 AgentRun Build...");
        var runResult = await _buildRunService.RunAsync(
            command with { BuildAssociationPrepared = true },
            cancellationToken);
        var finalResult = runResult.Outcome.Result;
        finalResult.PlanSnapshot = canonicalPlan;
        finalResult.AgentRunId = createResult.RunId;
        finalResult.SessionId ??= session.SessionId;
        return finalResult;
    }

    private static VisionAgentPlanModeRequest BuildPlanRequest(
        AiFlowGenerationRequest request,
        ConversationSession session)
    {
        var workspace = session.WorkspaceSnapshot;
        return new VisionAgentPlanModeRequest
        {
            Description = request.Description,
            OriginalUserPrompt = request.Description,
            AdditionalContext = request.AdditionalContext,
            SessionId = session.SessionId,
            Mode = request.Mode.ToWireValue(),
            CurrentFlowSnapshot = request.ExistingFlowJson,
            TemplateSelection = request.TemplateSelection,
            AttachmentSummary = BuildAttachmentSummary(request.Attachments),
            HistorySummary = BuildHistorySummary(session),
            RequirementMode = request.RequirementMode,
            ConfirmedPlanAnswers = workspace?.ConfirmedPlanAnswers?.ToList() ?? [],
            ResolvedPlanFields = [],
            RemainingPlanFields = []
        };
    }

    private static VisionAgentBuildReadinessPreviewRequest BuildReadinessRequest(
        AiFlowGenerationRequest request,
        VisionAgentPlanModeResult plan)
    {
        return new VisionAgentBuildReadinessPreviewRequest
        {
            PlanId = plan.PlanId,
            PlanHash = plan.PlanHash,
            PlanSnapshot = plan,
            RequirementMode = request.RequirementMode,
            ConfirmedAnswers = plan.ConfirmedPlanAnswers.ToList(),
            CurrentFlowSnapshot = request.ExistingFlowJson,
            TemplateSelection = plan.TemplateSelection ?? request.TemplateSelection,
            AttachmentSummary = BuildAttachmentSummary(request.Attachments),
            BuildIntent = request.Mode.ToWireValue(),
            OriginalUserPrompt = request.Description,
            RequirementMaturity = plan.RequirementMaturity,
            DecisionTrace = plan.DecisionTrace,
            MetadataOnly = true
        };
    }

    private static VisionAgentBuildFromPlanRequest BuildRequest(
        AiFlowGenerationRequest request,
        VisionAgentPlanModeResult plan,
        VisionAgentBuildReadinessPreviewResult readiness,
        long? workspaceRevision)
    {
        return new VisionAgentBuildFromPlanRequest
        {
            PlanId = plan.PlanId,
            PlanHash = plan.PlanHash,
            WorkspaceExpectedRevision = workspaceRevision,
            PlanSnapshot = plan,
            ConfirmedAnswers = readiness.AcceptedAnswers.ToList(),
            TemplateSelection = plan.TemplateSelection ?? request.TemplateSelection,
            AttachmentSummary = BuildAttachmentSummary(request.Attachments),
            BuildIntent = request.Mode.ToWireValue(),
            OriginalUserPrompt = request.Description,
            RequirementMaturity = plan.RequirementMaturity,
            DecisionTrace = plan.DecisionTrace,
            MetadataOnly = true
        };
    }

    private static AiFlowGenerationResult PlanResult(
        AiFlowGenerationRequest request,
        string sessionId,
        VisionAgentPlanModeResult plan,
        VisionAgentBuildReadinessPreviewResult readiness)
    {
        var canBuild = request.Mode != GenerateFlowMode.Explain &&
                       plan.CanBuild &&
                       readiness.ContractValid &&
                       readiness.BuildReadiness.CanBuild;
        var message = canBuild
            ? "正式 Plan 已通过 readiness。"
            : readiness.FailureMessage.Length > 0
                ? readiness.FailureMessage
                : plan.BuildReadiness.PrimaryMessage;
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
            ErrorMessage = message,
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_plan",
                Code = canBuild ? "generate_plan_ready" : "generate_plan_readiness_blocked",
                Message = message,
                RepairTarget = canBuild
                    ? "正式 BuildRun 将继续。"
                    : "请在 Plan 工作台补齐阻断项后重试。"
            },
            ClarificationRequired = !canBuild,
            SessionId = sessionId,
            PlanSnapshot = plan,
            PlanId = plan.PlanId,
            PlanHash = plan.PlanHash,
            ContractVersion = plan.PlanContractVersion,
            BuildReadiness = readiness.BuildReadiness,
            RequirementMaturity = plan.RequirementMaturity,
            DecisionTrace = plan.DecisionTrace,
            BlockingClarificationFields = plan.RemainingPlanFields.ToList(),
            NonBlockingMissingFields = readiness.BuildReadiness.MissingResources
                .Where(resource => resource.DraftPolicy == VisionAgentResourceDraftPolicies.DraftAllowed)
                .Select(resource => resource.CanonicalId)
                .ToList(),
            InteractionState = canBuild ? AiInteractionStates.Generating : AiInteractionStates.Clarifying,
            TurnIntent = ResolveTurnIntent(request.Mode),
            RouterConfidence = string.IsNullOrWhiteSpace(plan.Confidence)
                ? AiRouterConfidence.Medium
                : plan.Confidence,
            GenerationMode = "official_plan_build",
            EffectiveMode = AiAgentGenerateFlowModes.Scripted,
            RequestedMode = AiAgentGenerateFlowModes.Scripted
        };
    }

    private static AiFlowGenerationResult PersistenceFailure(
        AiFlowGenerationRequest request,
        string sessionId,
        VisionAgentWorkspaceSnapshotMutationResult persistence,
        string code,
        string message)
    {
        var result = Failure(
            request,
            sessionId,
            string.IsNullOrWhiteSpace(code) ? "generate_persistence_failed" : code,
            string.IsNullOrWhiteSpace(message) ? "正式 Plan 状态保存失败。" : message,
            AiFlowGenerationResult.FailureTypeSystemError,
            AiFlowGenerationResult.CompletionStatusFailed);
        result.PersistenceWarning = new AiPersistenceWarning
        {
            Code = string.IsNullOrWhiteSpace(code) ? "generate_persistence_failed" : code,
            Message = string.IsNullOrWhiteSpace(message) ? "正式 Plan 状态保存失败。" : message,
            PersistenceStatus = persistence.PersistenceStatus
        };
        return result;
    }

    private static AiFlowGenerationResult Failure(
        AiFlowGenerationRequest request,
        string sessionId,
        string code,
        string message,
        string failureType,
        string status)
    {
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = status,
            FailureType = failureType,
            ErrorMessage = message,
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_generate_compatibility",
                Code = code,
                Message = message,
                RepairTarget = "请返回 Plan 工作台检查会话状态后重试。"
            },
            SessionId = sessionId,
            InteractionState = status == AiFlowGenerationResult.CompletionStatusCancelled
                ? AiInteractionStates.Idle
                : AiInteractionStates.Failed,
            TurnIntent = ResolveTurnIntent(request.Mode),
            RouterConfidence = AiRouterConfidence.High,
            GenerationMode = "official_plan_build",
            RequestedMode = AiAgentGenerateFlowModes.Scripted,
            EffectiveMode = AiAgentGenerateFlowModes.Scripted
        };
    }

    private static VisionAgentPlanModeResult NormalizePlanHash(VisionAgentPlanModeResult plan)
    {
        var computed = VisionAgentOrchestrator.ComputePlanHash(plan);
        return string.Equals(plan.PlanHash, computed, StringComparison.OrdinalIgnoreCase)
            ? plan
            : plan with { PlanHash = computed };
    }

    private static AiFlowGenerationResult AttachPlan(
        AiFlowGenerationResult result,
        VisionAgentPlanModeResult plan)
    {
        result.PlanSnapshot = plan;
        result.PlanId = plan.PlanId;
        result.PlanHash = plan.PlanHash;
        result.ContractVersion = plan.PlanContractVersion;
        return result;
    }

    private static VisionAgentAttachmentSummary BuildAttachmentSummary(IReadOnlyList<string>? attachments)
    {
        var count = attachments?.Count ?? 0;
        return new VisionAgentAttachmentSummary
        {
            Count = count,
            ResourceKinds = count == 0 ? [] : ["image"],
            PathsRedacted = true
        };
    }

    private static string BuildHistorySummary(ConversationSession session)
    {
        return string.Join(
            " | ",
            session.History
                .TakeLast(5)
                .Select(turn => $"{turn.Role}:{turn.Message}")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Length > 240 ? value[..240] : value));
    }

    private static string ResolveTurnIntent(GenerateFlowMode mode) => mode switch
    {
        GenerateFlowMode.Modify => AiTurnIntents.ModifyFlow,
        GenerateFlowMode.Explain => AiTurnIntents.ExplainFlow,
        GenerateFlowMode.ReviewPendingParameters => AiTurnIntents.ReviewPendingParameters,
        _ => AiTurnIntents.NewFlow
    };
}
