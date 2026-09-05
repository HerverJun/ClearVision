using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentBuildRunResult(
    CanonicalBuildOutcome Outcome,
    AgentRunEvent? TerminalEvent);

public interface IVisionAgentBuildRunService
{
    VisionAgentWorkspaceSnapshotMutationResult PrepareBuildAssociation(BuildCommand command);

    Task<VisionAgentBuildRunResult> RunAsync(
        BuildCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class VisionAgentBuildRunService : IVisionAgentBuildRunService
{
    private readonly IVisionAgentBuildApplicationService _applicationService;
    private readonly IAgentRunEventStreamService _streamService;
    private readonly IConversationalFlowService _conversationService;
    private readonly IVisionAgentBuildTerminalProjector _terminalProjector;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildRunService> _logger;

    public VisionAgentBuildRunService(
        IVisionAgentBuildApplicationService applicationService,
        IAgentRunEventStreamService streamService,
        IConversationalFlowService conversationService,
        IVisionAgentBuildTerminalProjector terminalProjector,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildRunService> logger)
    {
        _applicationService = applicationService;
        _streamService = streamService;
        _conversationService = conversationService;
        _terminalProjector = terminalProjector;
        _logger = logger;
    }

    public VisionAgentWorkspaceSnapshotMutationResult PrepareBuildAssociation(BuildCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);
        var request = command.Request;
        var build = request.BuildFromPlan;
        var runId = FirstNonBlank(command.RunId, request.AgentRunId);
        var sessionId = request.SessionId?.Trim() ?? string.Empty;
        if (build == null || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return new VisionAgentWorkspaceSnapshotMutationResult { Success = true };
        }

        var ownerHash = ResolveOwnerHash(request);
        if (!_streamService.IsRunOwner(runId, ownerHash))
        {
            return new VisionAgentWorkspaceSnapshotMutationResult
            {
                Success = false,
                ErrorCode = "session_not_found",
                PublicMessage = "Conversation session was not found."
            };
        }

        return _conversationService.TryUpdateWorkspaceSnapshot(ownerHash, sessionId, new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = build.WorkspaceExpectedRevision,
            RequireExpectedRevisionWhenWorkspaceExists = true,
            RequireNoRunningAgentRun = true,
            ClientMutationId = $"build-association:{runId}",
            LifecycleState = "building",
            BuildRunId = runId,
            BuildRunStatus = AgentRunEventStatuses.Running,
            PlanRunStatus = AgentRunEventStatuses.Completed,
            PendingPlanSnapshot = build.PlanSnapshot,
            PlanQuestionSelections = build.UserSelections,
            ConfirmedPlanAnswers = build.ConfirmedAnswers,
            RequirementMode = request.RequirementMode,
            PlanAcceptedRecommendedDefaults = build.AcceptedRecommendedDefaults,
            SubmittedBuildFingerprint = ComputeSubmittedBuildFingerprint(request)
        });
    }

    public async Task<VisionAgentBuildRunResult> RunAsync(
        BuildCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var runId = FirstNonBlank(command.RunId, command.Request.AgentRunId);
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("AgentRun-backed BuildFromPlan requires a runId.");
        }

        var request = string.Equals(command.Request.AgentRunId, runId, StringComparison.OrdinalIgnoreCase)
            ? command.Request
            : command.Request with { AgentRunId = runId };
        var runCommand = command with
        {
            Request = request,
            RunId = runId,
            PersistResult = false
        };
        var projectionBasis = ResolveBuildAssociationProjectionBasis(runCommand, request);
        if (!runCommand.BuildAssociationPrepared)
        {
            var association = PrepareBuildAssociation(runCommand);
            if (!association.Success)
            {
                var result = BuildAssociationFailureResult(request, association);
                var terminal = TerminalOrReplay(
                    _streamService.Fail(
                        runId,
                        result.ErrorMessage ?? "Build 创建失败，会话状态未保存。",
                        result.FailureSummary?.RepairTarget ?? "请确认 Plan 状态后重新构建。",
                        BuildFailurePayload(result, request, projectionBasis, runId)),
                    runId);
                return new VisionAgentBuildRunResult(
                    BuildOutcome(runCommand, request, result, projected: false),
                    terminal);
            }

            projectionBasis = BuildAssociationProjectionBasis.FromAssociation(
                association,
                ComputeSubmittedBuildFingerprint(request));
        }
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _streamService.GetCancellationToken(runId));

        try
        {
            var outcome = await _applicationService.BuildAsync(runCommand, linkedCancellation.Token);
            return CompleteRun(
                runCommand,
                request,
                outcome,
                linkedCancellation.IsCancellationRequested,
                projectionBasis);
        }
        catch (OperationCanceledException)
        {
            var result = BuildCancelledProjectionResult(request, null);
            var terminal = TerminalOrReplay(
                _streamService.Cancel(
                    runId,
                    "Vision Agent BuildFromPlan was cancelled.",
                    BuildFailurePayload(result, request, projectionBasis, runId)),
                runId);
            var projected = ProjectTerminal(runCommand, request, result, terminal);
            return new VisionAgentBuildRunResult(
                BuildOutcome(runCommand, request, result, projected),
                terminal);
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                ex,
                "AgentRun-backed BuildFromPlan failed before canonical outcome. RunId={RunId}",
                runId);
            var result = BuildSystemErrorProjectionResult(request, null);
            var terminal = TerminalOrReplay(
                _streamService.Fail(
                    runId,
                    "Vision Agent BuildFromPlan failed before completion.",
                    "Review backend logs and retry after fixing the BuildFromPlan failure.",
                    BuildFailurePayload(result, request, projectionBasis, runId)),
                runId);
            var projected = ProjectTerminal(runCommand, request, result, terminal);
            return new VisionAgentBuildRunResult(
                BuildOutcome(runCommand, request, result, projected),
                terminal);
        }
    }

    private VisionAgentBuildRunResult CompleteRun(
        BuildCommand command,
        AiFlowGenerationRequest request,
        CanonicalBuildOutcome outcome,
        bool cancellationRequested,
        BuildAssociationProjectionBasis projectionBasis)
    {
        var result = outcome.Result;
        var runId = FirstNonBlank(command.RunId, request.AgentRunId);
        var terminal = AppendTerminal(runId, request, result, cancellationRequested, projectionBasis);
        var projectionResult = AlignResultWithTerminal(request, result, terminal);
        var projected = ProjectTerminal(command, request, projectionResult, terminal);
        return new VisionAgentBuildRunResult(
            BuildOutcome(command, request, projectionResult, projected),
            terminal);
    }

    private AgentRunEvent? AppendTerminal(
        string runId,
        AiFlowGenerationRequest request,
        AiFlowGenerationResult result,
        bool cancellationRequested,
        BuildAssociationProjectionBasis projectionBasis)
    {
        if (cancellationRequested ||
            string.Equals(result.CompletionStatus, AiFlowGenerationResult.CompletionStatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalOrReplay(
                _streamService.Cancel(
                    runId,
                    "Vision Agent BuildFromPlan was cancelled.",
                    BuildFailurePayload(result, request, projectionBasis, runId)),
                runId);
        }

        if (result.Success)
        {
            return TerminalOrReplay(
                _streamService.Complete(
                    runId,
                    "Vision Agent completed the metadata-only workflow draft build.",
                    BuildSuccessPayload(result, request, runId, projectionBasis)),
                runId);
        }

        return TerminalOrReplay(
            _streamService.Fail(
                runId,
                result.ErrorMessage ?? result.FailureSummary?.Message ?? "Vision Agent BuildFromPlan failed.",
                result.FailureSummary?.RepairTarget ??
                "Review public diagnostics, fill missing metadata, or resolve blocking intent before retrying.",
                BuildFailurePayload(result, request, projectionBasis, runId)),
            runId);
    }

    private AgentRunEvent? TerminalOrReplay(AgentRunEvent? appended, string runId)
    {
        if (appended != null)
        {
            return appended;
        }

        return _streamService.Replay(runId)?.Events
            .LastOrDefault(evt =>
                evt.EventType is AgentRunEventTypes.RunCompleted or
                    AgentRunEventTypes.RunFailed or
                    AgentRunEventTypes.RunCancelled);
    }

    private bool ProjectTerminal(
        BuildCommand command,
        AiFlowGenerationRequest request,
        AiFlowGenerationResult result,
        AgentRunEvent? terminal)
    {
        if (terminal == null)
        {
            return false;
        }

        return _terminalProjector.Project(new VisionAgentBuildTerminalProjection(
            FirstNonBlank(command.RunId, request.AgentRunId, terminal.RunId),
            command.Transport,
            request,
            result,
            terminal));
    }

    private static AiFlowGenerationResult AlignResultWithTerminal(
        AiFlowGenerationRequest request,
        AiFlowGenerationResult result,
        AgentRunEvent? terminal)
    {
        if (terminal == null)
        {
            return result;
        }

        if (string.Equals(terminal.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(result.CompletionStatus, AiFlowGenerationResult.CompletionStatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            return BuildCancelledProjectionResult(request, terminal);
        }

        return result;
    }

    private static object BuildSuccessPayload(
        AiFlowGenerationResult result,
        AiFlowGenerationRequest request,
        string runId,
        BuildAssociationProjectionBasis projectionBasis)
    {
        var terminalBasis = BuildTerminalBasis(result, request, projectionBasis);
        return new
        {
            runKind = VisionAgentRunKindResolver.Build,
            projectionDisposition = terminalBasis.ProjectionDisposition,
            associationCommitted = terminalBasis.AssociationCommitted,
            associationWorkspaceRevision = terminalBasis.AssociationWorkspaceRevision,
            submittedBuildFingerprint = terminalBasis.SubmittedBuildFingerprint,
            planId = terminalBasis.PlanId,
            planHash = terminalBasis.PlanHash,
            answerSetFingerprint = terminalBasis.AnswerSetFingerprint,
            buildIdentity = terminalBasis.BuildIdentity,
            status = result.CompletionStatus,
            sessionId = FirstNonBlank(result.SessionId, request.SessionId),
            generationMode = result.GenerationMode,
            templateLockLevel = result.TemplateLockLevel,
            recommendedTemplate = result.RecommendedTemplate,
            flow = result.Flow,
            aiExplanation = result.AiExplanation,
            parametersNeedingReview = result.ParametersNeedingReview,
            pendingParameters = result.PendingParameters,
            missingResources = result.MissingResources,
            globalVariableDrafts = result.GlobalVariableDrafts,
            globalVariableSourceBindingDrafts = result.GlobalVariableSourceBindingDrafts,
            globalVariableTargetBindingDrafts = result.GlobalVariableTargetBindingDrafts,
            globalVariableDiagnostics = result.GlobalVariableDiagnostics,
            pendingActions = result.PendingActions,
            validationPreview = result.ValidationPreview,
            dryRunResult = result.DryRunResult,
            toolTrace = result.ToolTrace,
            buildResult = result.BuildResult,
            buildReadiness = result.BuildReadiness,
            contractVersion = result.ContractVersion,
            requestedMode = result.RequestedMode,
            effectiveMode = result.EffectiveMode,
            toolLoopEntered = result.ToolLoopEntered,
            fallbackReason = result.FallbackReason,
            toolEvidenceTimeline = result.BuildResult?.ToolEvidenceTimeline,
            workflowDiff = result.BuildResult?.WorkflowDiff,
            applyGate = result.BuildResult?.ApplyGate,
            readinessReport = result.BuildResult?.ReadinessReport,
            stationCompatibilityReport = result.BuildResult?.StationCompatibilityReport,
            operatorContractReport = result.BuildResult?.OperatorContractReport,
            releaseReview = result.BuildResult?.ReleaseReview,
            firstFixRecommendation = result.BuildResult?.FirstFixRecommendation,
            stageTimeline = result.StageTimeline,
            persistenceWarning = result.PersistenceWarning,
            turnIntent = result.TurnIntent,
            interactionState = result.InteractionState,
            routerConfidence = result.RouterConfidence,
            requirementMaturity = result.RequirementMaturity,
            decisionTrace = result.DecisionTrace,
            planSnapshot = request.BuildFromPlan?.PlanSnapshot,
            buildFromPlan = BuildReplayPayload(request.BuildFromPlan),
            buildInputSummary = BuildInputSummary(request),
            toolTraceCount = result.ToolTrace.Count,
            pendingParameterCount = result.PendingParameters.Count,
            missingResourceCount = result.MissingResources.Count,
            globalVariableDraftCount = result.GlobalVariableDrafts.Count,
            globalVariableDiagnosticCount = result.GlobalVariableDiagnostics.Count,
            reportId = $"agent-report-{runId}",
            metadataOnly = true
        };
    }

    private static object BuildFailurePayload(
        AiFlowGenerationResult result,
        AiFlowGenerationRequest request,
        BuildAssociationProjectionBasis projectionBasis,
        string runId)
    {
        var terminalBasis = BuildTerminalBasis(result, request, projectionBasis);
        return new
        {
            runKind = VisionAgentRunKindResolver.Build,
            projectionDisposition = terminalBasis.ProjectionDisposition,
            associationCommitted = terminalBasis.AssociationCommitted,
            associationWorkspaceRevision = terminalBasis.AssociationWorkspaceRevision,
            submittedBuildFingerprint = terminalBasis.SubmittedBuildFingerprint,
            planId = terminalBasis.PlanId,
            planHash = terminalBasis.PlanHash,
            answerSetFingerprint = terminalBasis.AnswerSetFingerprint,
            buildIdentity = terminalBasis.BuildIdentity,
            status = result.CompletionStatus,
            sessionId = FirstNonBlank(result.SessionId, request.SessionId),
            failureType = result.FailureType,
            failureCode = result.FailureSummary?.Code ?? string.Empty,
            failureSummary = result.FailureSummary,
            diagnostics = result.LastAttemptDiagnostics,
            buildReadiness = result.BuildReadiness,
            contractVersion = result.ContractVersion,
            requestedMode = result.RequestedMode,
            effectiveMode = result.EffectiveMode,
            toolLoopEntered = result.ToolLoopEntered,
            fallbackReason = result.FallbackReason,
            planSnapshot = request.BuildFromPlan?.PlanSnapshot,
            buildFromPlan = BuildReplayPayload(request.BuildFromPlan),
            blockingClarificationFields = result.BlockingClarificationFields,
            nonBlockingMissingFields = result.NonBlockingMissingFields,
            requirementMaturity = result.RequirementMaturity,
            decisionTrace = result.DecisionTrace,
            persistenceWarning = result.PersistenceWarning,
            metadataOnly = true
        };
    }

    private static object? BuildReplayPayload(VisionAgentBuildFromPlanRequest? buildFromPlan)
    {
        if (buildFromPlan == null)
        {
            return null;
        }

        return new
        {
            planId = buildFromPlan.PlanId,
            planHash = buildFromPlan.PlanHash,
            planSnapshot = buildFromPlan.PlanSnapshot,
            userSelections = buildFromPlan.UserSelections,
            acceptedDefaults = buildFromPlan.AcceptedDefaults,
            currentFlowSnapshotIncluded = !string.IsNullOrWhiteSpace(buildFromPlan.CurrentFlowSnapshot),
            templateSelection = buildFromPlan.TemplateSelection,
            templateSelectionMode = buildFromPlan.TemplateSelection?.Mode ?? string.Empty,
            templateId = buildFromPlan.TemplateSelection?.TemplateId ?? string.Empty,
            attachmentSummary = buildFromPlan.AttachmentSummary,
            operatorCatalogVersion = buildFromPlan.OperatorCatalogVersion,
            stationBoundarySummary = buildFromPlan.StationBoundarySummary,
            plcOutputPolicy = buildFromPlan.PlcOutputPolicy,
            buildIntent = buildFromPlan.BuildIntent,
            originalUserPrompt = buildFromPlan.OriginalUserPrompt,
            acceptedRecommendedDefaults = buildFromPlan.AcceptedRecommendedDefaults,
            requirementMaturity = buildFromPlan.RequirementMaturity,
            decisionTrace = buildFromPlan.DecisionTrace,
            metadataOnly = true
        };
    }

    private static object BuildInputSummary(AiFlowGenerationRequest request)
    {
        var build = request.BuildFromPlan;
        return new
        {
            planId = build?.PlanId ?? string.Empty,
            planHash = build?.PlanHash ?? build?.PlanSnapshot?.PlanHash ?? string.Empty,
            buildIntent = build?.BuildIntent ?? request.Mode.ToWireValue(),
            currentFlowSnapshotIncluded = !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot) ||
                                          !string.IsNullOrWhiteSpace(request.ExistingFlowJson),
            templateSelectionMode = build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode ?? string.Empty,
            templateId = build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId ?? string.Empty,
            attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
            operatorCatalogVersion = build?.OperatorCatalogVersion ?? string.Empty,
            stationBoundarySummary = build?.StationBoundarySummary ?? string.Empty,
            plcOutputPolicy = build?.PlcOutputPolicy ?? string.Empty,
            metadataOnly = true
        };
    }

    private BuildAssociationProjectionBasis ResolveBuildAssociationProjectionBasis(
        BuildCommand command,
        AiFlowGenerationRequest request)
    {
        var runId = FirstNonBlank(command.RunId, request.AgentRunId);
        var sessionId = request.SessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return BuildAssociationProjectionBasis.Empty;
        }

        var workspace = _conversationService.GetSession(
            ResolveOwnerHash(request),
            sessionId)?.WorkspaceSnapshot;
        if (workspace == null ||
            !string.Equals(workspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase))
        {
            return BuildAssociationProjectionBasis.Empty;
        }

        return new BuildAssociationProjectionBasis(
            workspace.Revision,
            FirstNonBlank(workspace.SubmittedBuildFingerprint, ComputeSubmittedBuildFingerprint(request)),
            AssociationCommitted: true);
    }

    private static BuildTerminalProjectionBasis BuildTerminalBasis(
        AiFlowGenerationResult result,
        AiFlowGenerationRequest request,
        BuildAssociationProjectionBasis projectionBasis)
    {
        var planId = ResolveResultPlanId(result, request.BuildFromPlan);
        var planHash = ResolveResultPlanHash(result, request.BuildFromPlan);
        var answerSetFingerprint = FirstNonBlank(
            result.AnswerSetFingerprint,
            result.BuildResult?.AnswerSetFingerprint,
            request.BuildFromPlan?.PlanHash,
            request.BuildFromPlan?.PlanSnapshot?.PlanHash);
        var submittedBuildFingerprint = FirstNonBlank(
            projectionBasis.SubmittedBuildFingerprint,
            ComputeSubmittedBuildFingerprint(request),
            answerSetFingerprint,
            planHash);
        var buildIdentity = BuildBuildIdentity(
            planId,
            planHash,
            answerSetFingerprint,
            submittedBuildFingerprint);

        return new BuildTerminalProjectionBasis(
            projectionBasis.AssociationCommitted
                ? VisionAgentBuildProjectionDispositionResolver.Project
                : VisionAgentBuildProjectionDispositionResolver.Skip,
            projectionBasis.AssociationCommitted,
            projectionBasis.AssociationWorkspaceRevision,
            submittedBuildFingerprint,
            planId,
            planHash,
            answerSetFingerprint,
            buildIdentity);
    }

    private static string BuildBuildIdentity(
        string planId,
        string planHash,
        string answerSetFingerprint,
        string submittedBuildFingerprint)
    {
        return string.Join(
            ":",
            new[]
            {
                SanitizeIdentityToken(planId),
                SanitizeIdentityToken(planHash),
                SanitizeIdentityToken(answerSetFingerprint),
                SanitizeIdentityToken(submittedBuildFingerprint)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string SanitizeIdentityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            string.Empty,
            value.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is ':' or '_' or '-' or '.'));
    }

    private static CanonicalBuildOutcome BuildOutcome(
        BuildCommand command,
        AiFlowGenerationRequest request,
        AiFlowGenerationResult result,
        bool projected)
    {
        return new CanonicalBuildOutcome
        {
            Result = result,
            RunId = FirstNonBlank(command.RunId, request.AgentRunId),
            RequestId = command.RequestId ?? string.Empty,
            Transport = command.Transport,
            CompletionStatus = result.CompletionStatus,
            FailureType = result.FailureType ?? string.Empty,
            FailureCode = result.FailureSummary?.Code ?? string.Empty,
            PlanId = result.PlanId,
            PlanHash = result.PlanHash,
            ContractVersion = result.ContractVersion,
            AnswerSetFingerprint = result.AnswerSetFingerprint,
            RequestedMode = result.RequestedMode,
            EffectiveMode = result.EffectiveMode,
            ToolLoopEntered = result.ToolLoopEntered,
            FallbackReason = result.FallbackReason,
            BuildReadiness = result.BuildReadiness,
            WorkflowDiff = result.BuildResult?.WorkflowDiff,
            ApplyGate = result.BuildResult?.ApplyGate,
            Persisted = projected
        };
    }

    private static AiFlowGenerationResult BuildCancelledProjectionResult(
        AiFlowGenerationRequest request,
        AgentRunEvent? terminal)
    {
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCancelled,
            FailureType = AiFlowGenerationResult.FailureTypeUserCancelled,
            ErrorMessage = terminal?.Summary ?? "Vision Agent BuildFromPlan was cancelled.",
            PlanId = request.BuildFromPlan?.PlanSnapshot?.PlanId ?? request.BuildFromPlan?.PlanId ?? string.Empty,
            PlanHash = request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? request.BuildFromPlan?.PlanHash ?? string.Empty,
            ContractVersion = request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion ?? string.Empty,
            SessionId = request.SessionId,
            RequestedMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
            EffectiveMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
            ToolLoopEntered = false,
            FallbackReason = string.Empty,
            InteractionState = AiInteractionStates.Idle,
            TurnIntent = AiTurnIntents.NewFlow,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private static AiFlowGenerationResult BuildSystemErrorProjectionResult(
        AiFlowGenerationRequest request,
        AgentRunEvent? terminal)
    {
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeSystemError,
            ErrorMessage = terminal?.Summary ?? "Vision Agent BuildFromPlan failed before completion.",
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_build_from_plan",
                Code = VisionAgentBuildFailureCodes.SystemException,
                Message = "Vision Agent BuildFromPlan failed before completion.",
                RepairTarget = "Review backend logs and retry after fixing the BuildFromPlan failure."
            },
            PlanId = request.BuildFromPlan?.PlanSnapshot?.PlanId ?? request.BuildFromPlan?.PlanId ?? string.Empty,
            PlanHash = request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? request.BuildFromPlan?.PlanHash ?? string.Empty,
            ContractVersion = request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion ?? string.Empty,
            RequestedMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
            EffectiveMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
            ToolLoopEntered = false,
            FallbackReason = string.Empty,
            InteractionState = AiInteractionStates.Failed,
            TurnIntent = AiTurnIntents.NewFlow,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private static AiFlowGenerationResult BuildAssociationFailureResult(
        AiFlowGenerationRequest request,
        VisionAgentWorkspaceSnapshotMutationResult association)
    {
        var code = string.IsNullOrWhiteSpace(association.ErrorCode)
            ? "session_persistence_failed"
            : association.ErrorCode;
        var message = code switch
        {
            "workspace_revision_required" => "Build 创建失败：Plan 状态缺少版本号。",
            "workspace_revision_conflict" => "Build 创建失败：Plan 状态已变化。",
            _ => "Build 创建失败：会话状态未能保存。"
        };

        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeSystemError,
            ErrorMessage = message,
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_build_from_plan",
                Code = code,
                Message = message,
                RepairTarget = code is "workspace_revision_required" or "workspace_revision_conflict"
                    ? "请确认最新 Plan 状态后重新构建。"
                    : "请检查本机会话存储权限或磁盘空间后重试 Build。"
            },
            PlanId = request.BuildFromPlan?.PlanSnapshot?.PlanId ?? request.BuildFromPlan?.PlanId ?? string.Empty,
            PlanHash = request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? request.BuildFromPlan?.PlanHash ?? string.Empty,
            ContractVersion = request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion ?? string.Empty,
            SessionId = request.SessionId,
            RequestedMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
            EffectiveMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
            ToolLoopEntered = false,
            FallbackReason = string.Empty,
            InteractionState = AiInteractionStates.Failed,
            TurnIntent = AiTurnIntents.NewFlow,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private static string ComputeSubmittedBuildFingerprint(AiFlowGenerationRequest request) =>
        ComputeJsonFingerprint(new
        {
            request.BuildFromPlan?.PlanId,
            planHash = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash,
            request.RequirementMode,
            request.BuildFromPlan?.AcceptedRecommendedDefaults,
            request.BuildFromPlan?.AcceptedDefaults,
            request.BuildFromPlan?.ConfirmedAnswers,
            request.BuildFromPlan?.UserSelections
        });

    private static string ComputeJsonFingerprint(object value)
    {
        var json = JsonSerializer.Serialize(value);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveResultPlanId(
        AiFlowGenerationResult result,
        VisionAgentBuildFromPlanRequest? buildFromPlan)
    {
        return FirstNonBlank(
            result.PlanId,
            result.BuildResult?.PlanId,
            buildFromPlan?.PlanSnapshot?.PlanId,
            buildFromPlan?.PlanId);
    }

    private static string ResolveResultPlanHash(
        AiFlowGenerationResult result,
        VisionAgentBuildFromPlanRequest? buildFromPlan)
    {
        return FirstNonBlank(
            result.PlanHash,
            result.BuildResult?.PlanHash,
            buildFromPlan?.PlanSnapshot?.PlanHash,
            buildFromPlan?.PlanHash);
    }

    private static string ResolveOwnerHash(AiFlowGenerationRequest request) =>
        ConversationOwnerAuthority.Require(request.OwnerHash);

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private sealed record BuildAssociationProjectionBasis(
        long? AssociationWorkspaceRevision,
        string SubmittedBuildFingerprint,
        bool AssociationCommitted)
    {
        public static BuildAssociationProjectionBasis Empty { get; } = new(null, string.Empty, false);

        public static BuildAssociationProjectionBasis FromAssociation(
            VisionAgentWorkspaceSnapshotMutationResult association,
            string submittedBuildFingerprint)
        {
            return new BuildAssociationProjectionBasis(
                association.Snapshot?.Revision,
                FirstNonBlank(association.Snapshot?.SubmittedBuildFingerprint, submittedBuildFingerprint),
                AssociationCommitted: true);
        }
    }

    private sealed record BuildTerminalProjectionBasis(
        string ProjectionDisposition,
        bool AssociationCommitted,
        long? AssociationWorkspaceRevision,
        string SubmittedBuildFingerprint,
        string PlanId,
        string PlanHash,
        string AnswerSetFingerprint,
        string BuildIdentity);
}
