using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentBuildApplicationService : IVisionAgentBuildApplicationService
{
    private readonly IVisionAgentOrchestrator _execution;
    private readonly VisionAgentPlanAnswerValidator _answerValidator;
    private readonly VisionAgentPlanRequirementOverlay _requirementOverlay;
    private readonly IAgentRunEventSink? _eventSink;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService> _logger;
    private readonly AgentGenerateFlowOptions _options;

    public VisionAgentBuildApplicationService(
        IVisionAgentOrchestrator execution,
        VisionAgentPlanAnswerValidator answerValidator,
        VisionAgentPlanRequirementOverlay requirementOverlay,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService> logger,
        IOptions<AgentGenerateFlowOptions>? options = null,
        IAgentRunEventSink? eventSink = null)
    {
        _execution = execution;
        _answerValidator = answerValidator;
        _requirementOverlay = requirementOverlay;
        _logger = logger;
        _options = options?.Value ?? new AgentGenerateFlowOptions();
        _options.Mode = AiAgentGenerateFlowModes.Normalize(_options.Mode);
        _eventSink = eventSink;
    }

    public Task<VisionAgentBuildReadinessPreviewResult> PreviewBuildReadinessAsync(
        VisionAgentBuildReadinessPreviewRequest previewRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previewRequest);
        cancellationToken.ThrowIfCancellationRequested();

        var request = BuildPreviewGenerationRequest(previewRequest);
        var requestedMode = ResolveRequestedMode(request);
        var effectiveMode = requestedMode;

        if (!_options.Enabled)
        {
            return Task.FromResult(InvalidPreviewResult(
                previewRequest,
                VisionAgentBuildFailureCodes.Disabled,
                "Vision Agent BuildFromPlan is disabled by configuration."));
        }

        var contract = ValidateContract(request, requestedMode, effectiveMode);
        if (!contract.Valid)
        {
            return Task.FromResult(InvalidPreviewResult(
                previewRequest,
                contract.FailureCode,
                contract.FailureMessage,
                contract.PlanId,
                contract.PlanHash,
                contract.ContractVersion));
        }

        var readinessContext = BuildReadinessContext(request, contract);
        return Task.FromResult(BuildPreviewResult(previewRequest, contract, readinessContext));
    }

    public async Task<CanonicalBuildOutcome> BuildAsync(
        BuildCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        cancellationToken.ThrowIfCancellationRequested();
        var runId = FirstNonBlank(command.RunId, command.Request.AgentRunId);
        var request = string.IsNullOrWhiteSpace(runId)
            ? command.Request
            : command.Request with { AgentRunId = runId };
        var requestedMode = ResolveRequestedMode(request);
        var effectiveMode = requestedMode;

        if (!_options.Enabled)
        {
            return Complete(command, request, Failure(
                command,
                request,
                null,
                requestedMode,
                effectiveMode,
                VisionAgentBuildFailureCodes.Disabled,
                "Vision Agent BuildFromPlan is disabled by configuration.",
                "Enable AI:VisionAgent:GenerateFlow before starting BuildFromPlan.",
                AiFlowGenerationResult.FailureTypeSystemError,
                AiFlowGenerationResult.CompletionStatusFailed));
        }

        var contract = ValidateContract(request, requestedMode, effectiveMode);
        if (!contract.Valid)
        {
            EmitContractRejected(runId, contract);
            return Complete(command, request, Failure(
                command,
                request,
                contract,
                requestedMode,
                effectiveMode,
                contract.FailureCode,
                contract.FailureMessage,
                contract.RepairTarget,
                AiFlowGenerationResult.FailureTypeSystemError,
                AiFlowGenerationResult.CompletionStatusFailed));
        }

        EmitContractAccepted(runId, contract);
        var readinessContext = BuildReadinessContext(request, contract);
        if (!readinessContext.Readiness.CanBuild)
        {
            EmitReadinessBlocked(runId, contract, readinessContext.Readiness);
            return Complete(command, request, Failure(
                command,
                request,
                contract,
                requestedMode,
                effectiveMode,
                VisionAgentBuildFailureCodes.ReadinessBlocked,
                readinessContext.Readiness.PrimaryMessage,
                "Resolve the blocking Plan questions before starting BuildFromPlan.",
                AiFlowGenerationResult.FailureTypeClarificationRequired,
                AiFlowGenerationResult.CompletionStatusClarificationRequired,
                readinessContext.Readiness,
                readinessContext.AnswerSetFingerprint,
                clarificationRequired: true));
        }

        EmitReadinessAccepted(runId, contract, readinessContext.Readiness, readinessContext.AnswerSetFingerprint);

        var canonicalRequest = request with
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = effectiveMode,
            BuildFromPlan = contract.Build
        };

        try
        {
            var result = await _execution.BuildFromPlanAsync(canonicalRequest, cancellationToken);
            NormalizeResult(
                result,
                contract,
                requestedMode,
                effectiveMode,
                readinessContext.Readiness,
                readinessContext.AnswerSetFingerprint);
            return Complete(command, canonicalRequest, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var result = Failure(
                command,
                canonicalRequest,
                contract,
                requestedMode,
                effectiveMode,
                VisionAgentBuildFailureCodes.Cancelled,
                "Vision Agent BuildFromPlan was cancelled.",
                "Start a new BuildFromPlan run when ready.",
                AiFlowGenerationResult.FailureTypeUserCancelled,
                AiFlowGenerationResult.CompletionStatusCancelled,
                readinessContext.Readiness,
                readinessContext.AnswerSetFingerprint);
            return Complete(command, canonicalRequest, result);
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                ex,
                "Canonical Vision Agent BuildFromPlan failed. RunId={RunId}",
                runId);
            return Complete(command, canonicalRequest, Failure(
                command,
                canonicalRequest,
                contract,
                requestedMode,
                effectiveMode,
                VisionAgentBuildFailureCodes.SystemException,
                "Vision Agent BuildFromPlan failed before completion.",
                "Review public diagnostics and retry after fixing the BuildFromPlan failure.",
                AiFlowGenerationResult.FailureTypeSystemError,
                AiFlowGenerationResult.CompletionStatusFailed,
                readinessContext.Readiness,
                readinessContext.AnswerSetFingerprint));
        }
    }

    private CanonicalBuildOutcome Complete(
        BuildCommand command,
        AiFlowGenerationRequest request,
        AiFlowGenerationResult result)
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
            Persisted = false
        };
    }

    private BuildContractValidation ValidateContract(
        AiFlowGenerationRequest request,
        string requestedMode,
        string effectiveMode)
    {
        var build = request.BuildFromPlan;
        if (build == null)
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.ContractInvalid,
                "BuildFromPlan payload is required.");
        }

        var snapshot = build.PlanSnapshot;
        if (snapshot == null)
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.ContractInvalid,
                "BuildFromPlan.PlanSnapshot is required.");
        }

        var topLevelPlanId = Clean(build.PlanId);
        var snapshotPlanId = Clean(snapshot.PlanId);
        if (string.IsNullOrWhiteSpace(topLevelPlanId) || string.IsNullOrWhiteSpace(snapshotPlanId))
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.ContractInvalid,
                "BuildFromPlan requires both top-level PlanId and PlanSnapshot.PlanId.");
        }

        if (!string.Equals(topLevelPlanId, snapshotPlanId, StringComparison.OrdinalIgnoreCase))
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.PlanIdMismatch,
                "Plan context is stale. Please rebuild from the current Plan before starting Build.");
        }

        var contractVersion = NormalizeContractVersion(snapshot);
        if (string.IsNullOrWhiteSpace(contractVersion))
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.ContractInvalid,
                $"Unsupported Plan contract version: {snapshot.PlanContractVersion}.");
        }

        if (build.ConfirmedAnswers.Any(answer =>
                string.IsNullOrWhiteSpace(answer.Field) &&
                string.IsNullOrWhiteSpace(answer.QuestionId)))
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.ContractInvalid,
                "BuildFromPlan confirmed answers must include a field or question id.");
        }

        var computedHash = VisionAgentOrchestrator.ComputePlanHash(snapshot);
        var topLevelHash = Clean(build.PlanHash);
        var snapshotHash = Clean(snapshot.PlanHash);
        var providedHash = FirstNonBlank(topLevelHash, snapshotHash);
        var legacyMissingHash = string.IsNullOrWhiteSpace(providedHash) &&
                                string.Equals(contractVersion, VisionAgentPlanContractVersions.V1, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(providedHash) && !legacyMissingHash)
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.PlanHashMissing,
                "BuildFromPlan requires a PlanHash for the canonical Plan contract.",
                topLevelPlanId,
                computedHash,
                contractVersion);
        }

        if (!string.IsNullOrWhiteSpace(providedHash) &&
            !string.Equals(providedHash, computedHash, StringComparison.OrdinalIgnoreCase))
        {
            return BuildContractValidation.Invalid(
                VisionAgentBuildFailureCodes.StalePlan,
                "Plan context is stale. Please rebuild from the current Plan before starting Build.",
                topLevelPlanId,
                computedHash,
                contractVersion);
        }

        var normalizedPlan = snapshot with
        {
            PlanContractVersion = contractVersion,
            PlanId = topLevelPlanId,
            PlanHash = computedHash
        };
        var warnings = legacyMissingHash
            ? new List<string> { "legacy_plan_hash_missing" }
            : [];
        var normalizedBuild = build with
        {
            PlanId = topLevelPlanId,
            PlanHash = computedHash,
            PlanSnapshot = normalizedPlan
        };

        return BuildContractValidation.ValidContract(
            normalizedBuild,
            normalizedPlan,
            topLevelPlanId,
            computedHash,
            contractVersion,
            warnings,
            requestedMode,
            effectiveMode);
    }

    private CanonicalReadinessContext BuildReadinessContext(
        AiFlowGenerationRequest request,
        BuildContractValidation contract)
    {
        var build = contract.Build!;
        var plan = contract.Plan!;
        var requirementMode = NormalizeRequirementMode(request.RequirementMode);
        var maturityRequest = new VisionAgentRequirementMaturityRequest
        {
            Description = FirstNonBlank(build.OriginalUserPrompt, request.Description),
            AdditionalContext = request.AdditionalContext,
            Mode = FirstNonBlank(build.BuildIntent, request.Mode.ToWireValue()),
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                             !string.IsNullOrWhiteSpace(build.CurrentFlowSnapshot),
            HasPendingPlan = true,
            TemplateSelection = build.TemplateSelection ?? request.TemplateSelection ?? plan.TemplateSelection,
            RequirementMode = requirementMode
        };
        var validatedAnswers = _answerValidator.Validate(
            plan,
            build.ConfirmedAnswers,
            build.UserSelections,
            build.AcceptedRecommendedDefaults);
        var effectiveRequirement = _requirementOverlay.Build(plan, validatedAnswers, maturityRequest);
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            validatedAnswers.BuildDecisions,
            build.AcceptedDefaults,
            build.AcceptedRecommendedDefaults,
            validatedAnswers,
            effectiveRequirement,
            requirementMode,
            build.ResourceDecisions) with
        {
            ContractVersion = contract.ContractVersion
        };
        return new CanonicalReadinessContext(
            readiness,
            validatedAnswers,
            validatedAnswers.AnswerSetFingerprint);
    }

    private static AiFlowGenerationRequest BuildPreviewGenerationRequest(
        VisionAgentBuildReadinessPreviewRequest request)
    {
        var build = new VisionAgentBuildFromPlanRequest
        {
            PlanId = request.PlanId,
            PlanHash = request.PlanHash,
            PlanSnapshot = request.PlanSnapshot,
            ConfirmedAnswers = request.ConfirmedAnswers,
            UserSelections = request.UserSelections,
            AcceptedDefaults = request.AcceptedDefaults,
            CurrentFlowSnapshot = request.CurrentFlowSnapshot,
            TemplateSelection = request.TemplateSelection ?? request.PlanSnapshot?.TemplateSelection,
            AttachmentSummary = request.AttachmentSummary,
            OperatorCatalogVersion = request.OperatorCatalogVersion,
            StationBoundarySummary = request.StationBoundarySummary,
            PlcOutputPolicy = request.PlcOutputPolicy,
            BuildIntent = request.BuildIntent,
            OriginalUserPrompt = request.OriginalUserPrompt,
            AcceptedRecommendedDefaults = request.AcceptedRecommendedDefaults,
            RequirementMaturity = request.RequirementMaturity,
            DecisionTrace = request.DecisionTrace,
            ResourceDecisions = request.ResourceDecisions,
            MetadataOnly = true
        };

        return new AiFlowGenerationRequest(
            FirstNonBlank(request.OriginalUserPrompt, request.PlanSnapshot?.OriginalUserPrompt, request.PlanSnapshot?.Goal),
            request.AdditionalContext,
            null,
            request.CurrentFlowSnapshot,
            Array.Empty<string>(),
            GenerateFlowModeExtensions.ParseOrAuto(request.BuildIntent),
            false,
            build.TemplateSelection)
        {
            RequirementMode = NormalizeRequirementMode(request.RequirementMode),
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted,
            RuntimePreviewConsent = false,
            BuildFromPlan = build
        };
    }

    private VisionAgentBuildReadinessPreviewResult BuildPreviewResult(
        VisionAgentBuildReadinessPreviewRequest request,
        BuildContractValidation contract,
        CanonicalReadinessContext readinessContext)
    {
        var readiness = readinessContext.Readiness;
        var deferredQuestionIds = FindDeferredQuestionIds(contract.Plan!, contract.Build!.UserSelections);
        var pendingConfirmationCount = CountPendingConfirmations(readiness);
        var resourcePendingCount = readiness.Blockers
            .Where(blocker => blocker.Category.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase))
            .Select(blocker => FirstNonBlank(blocker.QuestionId, blocker.Field, blocker.Id))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var hardBlockerCount = readiness.Blockers.Count(blocker =>
            blocker.BlocksBuild &&
            !blocker.Category.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase));

        return new VisionAgentBuildReadinessPreviewResult
        {
            PlanId = contract.PlanId,
            PlanHash = contract.PlanHash,
            RequirementMode = NormalizeRequirementMode(request.RequirementMode),
            AnswerRevision = request.AnswerRevision,
            ResourceRevision = request.ResourceRevision,
            AcceptedAnswers = readinessContext.Validation.AcceptedAnswers,
            AnswerSetFingerprint = readinessContext.AnswerSetFingerprint,
            BuildReadiness = readiness,
            DeferredQuestionIds = deferredQuestionIds,
            PendingConfirmationCount = pendingConfirmationCount,
            ResourcePendingCount = resourcePendingCount,
            HardBlockerCount = hardBlockerCount,
            ContractValid = true,
            MetadataOnly = true
        };
    }

    private static VisionAgentBuildReadinessPreviewResult InvalidPreviewResult(
        VisionAgentBuildReadinessPreviewRequest request,
        string failureCode,
        string failureMessage,
        string planId = "",
        string planHash = "",
        string contractVersion = "")
    {
        var blocker = new VisionAgentBuildBlocker
        {
            Id = failureCode,
            Category = VisionAgentBuildBlockerCategories.HardRequirement,
            BlocksBuild = true,
            ResolutionMode = VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
            PublicLabel = failureMessage
        };
        return new VisionAgentBuildReadinessPreviewResult
        {
            PlanId = FirstNonBlank(planId, request.PlanId, request.PlanSnapshot?.PlanId),
            PlanHash = FirstNonBlank(planHash, request.PlanHash, request.PlanSnapshot?.PlanHash),
            RequirementMode = NormalizeRequirementMode(request.RequirementMode),
            AnswerRevision = request.AnswerRevision,
            ResourceRevision = request.ResourceRevision,
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = false,
                Blockers = [blocker],
                PrimaryMessage = failureMessage,
                ContractVersion = FirstNonBlank(contractVersion, request.PlanSnapshot?.PlanContractVersion, VisionAgentPlanContractVersions.V2)
            },
            HardBlockerCount = 1,
            ContractValid = false,
            FailureCode = failureCode,
            FailureMessage = failureMessage,
            MetadataOnly = true
        };
    }

    private static List<string> FindDeferredQuestionIds(
        VisionAgentPlanModeResult plan,
        IReadOnlyDictionary<string, string>? userSelections)
    {
        if (userSelections == null || userSelections.Count == 0)
        {
            return [];
        }

        var questions = plan.ClarificationQuestions ?? [];
        var deferred = new List<string>();
        foreach (var question in questions)
        {
            var questionId = Clean(question.Id);
            if (string.IsNullOrWhiteSpace(questionId))
            {
                continue;
            }

            var field = VisionAgentPlanFieldPolicy.ResolveQuestionField(question, plan.BlockingReasons);
            if (!userSelections.TryGetValue(questionId, out var selected) &&
                (string.IsNullOrWhiteSpace(field) || !userSelections.TryGetValue(field, out selected)))
            {
                continue;
            }

            var selectedValue = Clean(selected);
            var option = question.Options.FirstOrDefault(item =>
                Clean(item.Value).Equals(selectedValue, StringComparison.OrdinalIgnoreCase));
            if (option != null &&
                string.Equals(option.AnswerEffect, VisionAgentClarificationAnswerEffects.Defer, StringComparison.OrdinalIgnoreCase))
            {
                deferred.Add(questionId);
            }
        }

        return deferred.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int CountPendingConfirmations(VisionAgentBuildReadinessSnapshot readiness)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var blocker in readiness.Blockers)
        {
            if (blocker.Category.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase) ||
                blocker.Category.Equals(VisionAgentBuildBlockerCategories.ContractWarning, StringComparison.OrdinalIgnoreCase) ||
                blocker.Category.Equals(VisionAgentBuildBlockerCategories.SafetyBlocker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = FirstNonBlank(blocker.QuestionId, blocker.Field, blocker.Id);
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key);
            }
        }

        foreach (var field in readiness.RemainingFields)
        {
            if (!string.IsNullOrWhiteSpace(field))
            {
                keys.Add(field);
            }
        }

        return keys.Count;
    }

    private AiFlowGenerationResult Failure(
        BuildCommand command,
        AiFlowGenerationRequest request,
        BuildContractValidation? contract,
        string requestedMode,
        string effectiveMode,
        string code,
        string message,
        string repairTarget,
        string failureType,
        string completionStatus,
        VisionAgentBuildReadinessSnapshot? readiness = null,
        string answerSetFingerprint = "",
        bool clarificationRequired = false)
    {
        var planId = FirstNonBlank(contract?.PlanId, request.BuildFromPlan?.PlanId, request.BuildFromPlan?.PlanSnapshot?.PlanId);
        var planHash = FirstNonBlank(contract?.PlanHash, request.BuildFromPlan?.PlanHash, request.BuildFromPlan?.PlanSnapshot?.PlanHash);
        var contractVersion = FirstNonBlank(contract?.ContractVersion, request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion, VisionAgentPlanContractVersions.V2);
        var warnings = contract?.PublicWarnings.ToList() ?? [];
        var buildReadiness = readiness;
        var blockingFields = buildReadiness?.RemainingFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var applyGate = new VisionAgentApplyGate
        {
            Blocked = true,
            Status = "blocked",
            ApplyBlockers = [code],
            DeploymentBlockers = [code],
            FirstFixRecommendation = repairTarget,
            MetadataOnly = true
        };
        var buildResult = new VisionAgentBuildResult
        {
            PlanId = planId,
            PlanHash = planHash,
            ContractVersion = contractVersion,
            AnswerSetFingerprint = answerSetFingerprint,
            RequestedMode = requestedMode,
            EffectiveMode = effectiveMode,
            ToolLoopEntered = false,
            FallbackReason = string.Empty,
            RemainingFields = buildReadiness?.RemainingFields.ToList() ?? [],
            ResolvedFields = buildReadiness?.ResolvedFields.ToList() ?? [],
            WorkflowDiff = new VisionAgentWorkflowDiff
            {
                ValidationFailures = [code],
                MetadataOnly = true
            },
            ApplyGate = applyGate,
            PublicWarnings = warnings,
            FirstFixRecommendation = repairTarget,
            MetadataOnly = true
        };

        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = completionStatus,
            FailureType = failureType,
            ErrorMessage = message,
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_build_from_plan",
                Code = code,
                Message = message,
                RepairTarget = repairTarget
            },
            ClarificationRequired = clarificationRequired,
            RequirementBrief = clarificationRequired
                ? new AiRequirementBrief
                {
                    RequirementMode = request.RequirementMode,
                    HasOpenQuestions = true,
                    ClarificationRequired = true,
                    CanGenerateDraftNow = false,
                    DraftRiskLevel = "high",
                    MissingFacts = blockingFields,
                    RequiredFields = blockingFields,
                    BlockingClarificationFields = blockingFields
                }
                : null,
            BuildReadiness = buildReadiness,
            BuildResult = buildResult,
            PlanId = planId,
            PlanHash = planHash,
            ContractVersion = contractVersion,
            AnswerSetFingerprint = answerSetFingerprint,
            RequestedMode = requestedMode,
            EffectiveMode = effectiveMode,
            ToolLoopEntered = buildResult.ToolLoopEntered,
            FallbackReason = string.Empty,
            BlockingClarificationFields = blockingFields,
            NonBlockingMissingFields = [],
            TurnIntent = AiTurnIntents.NewFlow,
            InteractionState = clarificationRequired
                ? AiInteractionStates.Clarifying
                : completionStatus == AiFlowGenerationResult.CompletionStatusCancelled
                    ? AiInteractionStates.Idle
                    : AiInteractionStates.Failed,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private void NormalizeResult(
        AiFlowGenerationResult result,
        BuildContractValidation contract,
        string requestedMode,
        string effectiveMode,
        VisionAgentBuildReadinessSnapshot readiness,
        string answerSetFingerprint)
    {
        var fallbackReason = ResolveFallbackReason(result);
        var toolLoopEntered = string.Equals(requestedMode, AiAgentGenerateFlowModes.ToolLoop, StringComparison.OrdinalIgnoreCase) ||
                              HasToolLoopEvidence(result);
        var actualEffectiveMode = !string.IsNullOrWhiteSpace(fallbackReason)
            ? AiAgentGenerateFlowModes.Scripted
            : effectiveMode;
        var publicWarnings = result.BuildResult?.PublicWarnings?.ToList() ?? [];
        publicWarnings.AddRange(contract.PublicWarnings);
        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            publicWarnings.Add($"tool_loop_fallback:{fallbackReason}");
        }

        result.PlanId = contract.PlanId;
        result.PlanHash = contract.PlanHash;
        result.ContractVersion = contract.ContractVersion;
        result.AnswerSetFingerprint = answerSetFingerprint;
        result.RequestedMode = requestedMode;
        result.EffectiveMode = actualEffectiveMode;
        result.ToolLoopEntered = toolLoopEntered;
        result.FallbackReason = fallbackReason;
        result.BuildReadiness = result.BuildReadiness == null
            ? readiness
            : result.BuildReadiness with { ContractVersion = contract.ContractVersion };

        var buildResult = result.BuildResult ?? new VisionAgentBuildResult();
        result.BuildResult = buildResult with
        {
            PlanId = contract.PlanId,
            PlanHash = contract.PlanHash,
            ContractVersion = contract.ContractVersion,
            AnswerSetFingerprint = answerSetFingerprint,
            RequestedMode = requestedMode,
            EffectiveMode = actualEffectiveMode,
            ToolLoopEntered = toolLoopEntered,
            FallbackReason = fallbackReason,
            Flow = buildResult.Flow ?? result.Flow,
            PublicWarnings = publicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private void EmitContractAccepted(string? runId, BuildContractValidation contract)
    {
        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.StageCompleted,
            Stage = "canonical_build_contract",
            Title = "Build contract accepted",
            Summary = "BuildFromPlan contract was normalized and Plan identity was verified.",
            Status = AgentRunEventStatuses.Completed,
            MetadataOnly = true,
            Payload = new
            {
                planId = contract.PlanId,
                planHash = contract.PlanHash,
                contractVersion = contract.ContractVersion,
                warnings = contract.PublicWarnings,
                metadataOnly = true,
                redactionPass = true
            }
        });
    }

    private void EmitContractRejected(string? runId, BuildContractValidation contract)
    {
        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.StageCompleted,
            Stage = "canonical_build_contract",
            Title = "Build contract rejected",
            Summary = contract.FailureMessage,
            Status = AgentRunEventStatuses.Failed,
            MetadataOnly = true,
            Payload = new
            {
                planId = contract.PlanId,
                planHash = contract.PlanHash,
                contractVersion = contract.ContractVersion,
                failureCode = contract.FailureCode,
                metadataOnly = true,
                redactionPass = true
            }
        });
    }

    private void EmitReadinessAccepted(
        string? runId,
        BuildContractValidation contract,
        VisionAgentBuildReadinessSnapshot readiness,
        string answerSetFingerprint)
    {
        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ReadinessChecked,
            Stage = "canonical_build_readiness",
            Title = "Build readiness accepted",
            Summary = "Confirmed answers and requirement overlay are ready for BuildFromPlan.",
            Status = AgentRunEventStatuses.Completed,
            MetadataOnly = true,
            Payload = new
            {
                planId = contract.PlanId,
                planHash = contract.PlanHash,
                contractVersion = contract.ContractVersion,
                answerSetFingerprint,
                canBuild = readiness.CanBuild,
                resolvedFields = readiness.ResolvedFields,
                metadataOnly = true,
                redactionPass = true
            }
        });
    }

    private void EmitReadinessBlocked(
        string? runId,
        BuildContractValidation contract,
        VisionAgentBuildReadinessSnapshot readiness)
    {
        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ReadinessChecked,
            Stage = "canonical_build_readiness",
            Title = "Build readiness blocked",
            Summary = readiness.PrimaryMessage,
            Status = AgentRunEventStatuses.Blocked,
            MetadataOnly = true,
            Payload = new
            {
                planId = contract.PlanId,
                planHash = contract.PlanHash,
                contractVersion = contract.ContractVersion,
                blockers = readiness.Blockers,
                remainingFields = readiness.RemainingFields,
                metadataOnly = true,
                redactionPass = true
            }
        });
    }

    private static string ResolveRequestedMode(AiFlowGenerationRequest request)
    {
        return AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode);
    }

    private static string NormalizeContractVersion(VisionAgentPlanModeResult plan)
    {
        var version = Clean(plan.PlanContractVersion);
        if (version.Equals(VisionAgentPlanContractVersions.V1, StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanContractVersions.V1;
        }

        if (version.Equals(VisionAgentPlanContractVersions.V2, StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanContractVersions.V2;
        }

        return string.IsNullOrWhiteSpace(version)
            ? VisionAgentPlanContractVersions.V1
            : string.Empty;
    }

    private static string NormalizeRequirementMode(string? value)
    {
        return string.Equals(value, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
            ? AiRequirementModes.Draft
            : AiRequirementModes.Strict;
    }

    private static bool HasToolLoopEvidence(AiFlowGenerationResult result)
    {
        return result.BuildResult?.ToolEvidenceTimeline.Any(evidence =>
            evidence.ToolName.Contains("tool_loop", StringComparison.OrdinalIgnoreCase) ||
            evidence.Source.Contains("tool_loop", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string ResolveFallbackReason(AiFlowGenerationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FallbackReason))
        {
            return Clean(result.FallbackReason);
        }

        var evidence = result.BuildResult?.ToolEvidenceTimeline.FirstOrDefault(item =>
            item.ToolName.Equals("tool_loop_fallback", StringComparison.OrdinalIgnoreCase) ||
            item.Source.Equals("fallback_build_orchestrator", StringComparison.OrdinalIgnoreCase));
        return Clean(evidence?.WarningCode);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string Clean(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private sealed record CanonicalReadinessContext(
        VisionAgentBuildReadinessSnapshot Readiness,
        VisionAgentPlanAnswerValidationResult Validation,
        string AnswerSetFingerprint);

    private sealed record BuildContractValidation(
        bool Valid,
        VisionAgentBuildFromPlanRequest? Build,
        VisionAgentPlanModeResult? Plan,
        string PlanId,
        string PlanHash,
        string ContractVersion,
        IReadOnlyList<string> PublicWarnings,
        string FailureCode,
        string FailureMessage,
        string RepairTarget)
    {
        public static BuildContractValidation ValidContract(
            VisionAgentBuildFromPlanRequest build,
            VisionAgentPlanModeResult plan,
            string planId,
            string planHash,
            string contractVersion,
            IReadOnlyList<string> publicWarnings,
            string requestedMode,
            string effectiveMode)
        {
            _ = requestedMode;
            _ = effectiveMode;
            return new BuildContractValidation(
                true,
                build,
                plan,
                planId,
                planHash,
                contractVersion,
                publicWarnings,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        public static BuildContractValidation Invalid(
            string code,
            string message,
            string planId = "",
            string planHash = "",
            string contractVersion = "")
        {
            return new BuildContractValidation(
                false,
                null,
                null,
                planId,
                planHash,
                contractVersion,
                [],
                code,
                message,
                "Return to the Plan workspace and rebuild from the current Plan.");
        }
    }
}
