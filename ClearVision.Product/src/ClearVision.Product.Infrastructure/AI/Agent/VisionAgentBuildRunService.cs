using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentBuildRunResult(
    CanonicalBuildOutcome Outcome,
    AgentRunEvent? TerminalEvent);

public interface IVisionAgentBuildRunService
{
    Task<VisionAgentBuildRunResult> RunAsync(
        BuildCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class VisionAgentBuildRunService : IVisionAgentBuildRunService
{
    private readonly IVisionAgentBuildApplicationService _applicationService;
    private readonly IAgentRunEventStreamService _streamService;
    private readonly IVisionAgentBuildTerminalProjector _terminalProjector;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildRunService> _logger;

    public VisionAgentBuildRunService(
        IVisionAgentBuildApplicationService applicationService,
        IAgentRunEventStreamService streamService,
        IVisionAgentBuildTerminalProjector terminalProjector,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildRunService> logger)
    {
        _applicationService = applicationService;
        _streamService = streamService;
        _terminalProjector = terminalProjector;
        _logger = logger;
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
                linkedCancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            var result = BuildCancelledProjectionResult(request, null);
            var terminal = TerminalOrReplay(_streamService.Cancel(runId), runId);
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
                    BuildFailurePayload(result, request)),
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
        bool cancellationRequested)
    {
        var result = outcome.Result;
        var runId = FirstNonBlank(command.RunId, request.AgentRunId);
        var terminal = AppendTerminal(runId, request, result, cancellationRequested);
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
        bool cancellationRequested)
    {
        if (cancellationRequested ||
            string.Equals(result.CompletionStatus, AiFlowGenerationResult.CompletionStatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalOrReplay(_streamService.Cancel(runId), runId);
        }

        if (result.Success)
        {
            return TerminalOrReplay(
                _streamService.Complete(
                    runId,
                    "Vision Agent completed the metadata-only workflow draft build.",
                    BuildSuccessPayload(result, request, runId)),
                runId);
        }

        return TerminalOrReplay(
            _streamService.Fail(
                runId,
                result.ErrorMessage ?? result.FailureSummary?.Message ?? "Vision Agent BuildFromPlan failed.",
                result.FailureSummary?.RepairTarget ??
                "Review public diagnostics, fill missing metadata, or resolve blocking intent before retrying.",
                BuildFailurePayload(result, request)),
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
        string runId)
    {
        return new
        {
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
            planId = ResolveResultPlanId(result, request.BuildFromPlan),
            planHash = ResolveResultPlanHash(result, request.BuildFromPlan),
            contractVersion = result.ContractVersion,
            answerSetFingerprint = result.AnswerSetFingerprint,
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
        AiFlowGenerationRequest request)
    {
        return new
        {
            status = result.CompletionStatus,
            failureType = result.FailureType,
            failureCode = result.FailureSummary?.Code ?? string.Empty,
            failureSummary = result.FailureSummary,
            diagnostics = result.LastAttemptDiagnostics,
            buildReadiness = result.BuildReadiness,
            planId = ResolveResultPlanId(result, request.BuildFromPlan),
            planHash = ResolveResultPlanHash(result, request.BuildFromPlan),
            contractVersion = result.ContractVersion,
            answerSetFingerprint = result.AnswerSetFingerprint,
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

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
