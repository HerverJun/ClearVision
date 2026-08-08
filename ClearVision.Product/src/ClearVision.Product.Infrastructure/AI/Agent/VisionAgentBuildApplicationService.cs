using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
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
    private readonly IWorkflowArtifactAdmissionGate? _workflowArtifactAdmissionGate;

    public VisionAgentBuildApplicationService(
        IVisionAgentOrchestrator execution,
        VisionAgentPlanAnswerValidator answerValidator,
        VisionAgentPlanRequirementOverlay requirementOverlay,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService> logger,
        IOptions<AgentGenerateFlowOptions>? options = null,
        IAgentRunEventSink? eventSink = null,
        IWorkflowArtifactAdmissionGate? workflowArtifactAdmissionGate = null)
    {
        _execution = execution;
        _answerValidator = answerValidator;
        _requirementOverlay = requirementOverlay;
        _logger = logger;
        _options = options?.Value ?? new AgentGenerateFlowOptions();
        _eventSink = eventSink;
        _workflowArtifactAdmissionGate = workflowArtifactAdmissionGate;
    }

    public Task<VisionAgentBuildReadinessPreviewResult> PreviewBuildReadinessAsync(
        VisionAgentBuildReadinessPreviewRequest previewRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previewRequest);
        cancellationToken.ThrowIfCancellationRequested();

        var request = BuildPreviewGenerationRequest(previewRequest);
        var modeDecision = AiAgentGenerateFlowModePolicy.Evaluate(
            request.AgentGenerateFlowMode,
            AiAgentGenerateFlowPolicyKind.Production);
        var requestedMode = modeDecision.RequestedMode;
        var effectiveMode = modeDecision.EffectiveMode;

        if (!_options.Enabled)
        {
            return Task.FromResult(InvalidPreviewResult(
                previewRequest,
                VisionAgentBuildFailureCodes.Disabled,
                "Vision Agent BuildFromPlan is disabled by configuration."));
        }

        if (!modeDecision.Allowed)
        {
            return Task.FromResult(InvalidPreviewResult(
                previewRequest,
                modeDecision.FailureCode,
                modeDecision.FailureMessage));
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

    public Task<VisionAgentPublicBuildResultV1> RevalidateAsync(
        VisionAgentBuildRevalidationRequest request,
        CancellationToken cancellationToken)
    {
        return new VisionAgentBuildRevalidator().RevalidateAsync(request, cancellationToken);
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
        var modeDecision = AiAgentGenerateFlowModePolicy.Evaluate(
            request.AgentGenerateFlowMode,
            AiAgentGenerateFlowPolicyKind.Production);
        var requestedMode = modeDecision.RequestedMode;
        var effectiveMode = modeDecision.EffectiveMode;

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

        if (!modeDecision.Allowed)
        {
            return Complete(command, request, Failure(
                command,
                request,
                null,
                requestedMode,
                effectiveMode,
                modeDecision.FailureCode,
                modeDecision.FailureMessage,
                "请返回 Plan 视图修正需求或使用正式 BuildFromPlan 重试。",
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
        var partition = PartitionIncompleteItems(readiness);

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
            PendingConfirmationCount = partition.BuildBlockingConfirmationCount + partition.DeferredFieldCount,
            ResourcePendingCount = partition.BuildRequiredResourceCount + partition.DraftAllowedResourceCount,
            HardBlockerCount = partition.BuildBlockingConfirmationCount,
            BuildBlockingConfirmationCount = partition.BuildBlockingConfirmationCount,
            BuildRequiredResourceCount = partition.BuildRequiredResourceCount,
            DeferredFieldCount = partition.DeferredFieldCount,
            DraftAllowedResourceCount = partition.DraftAllowedResourceCount,
            MustConfirmBeforeBuildCount = partition.MustConfirmBeforeBuildCount,
            FillLaterCount = partition.FillLaterCount,
            TotalIncompleteCount = partition.TotalIncompleteCount,
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
            PendingConfirmationCount = 1,
            HardBlockerCount = 1,
            BuildBlockingConfirmationCount = 1,
            MustConfirmBeforeBuildCount = 1,
            TotalIncompleteCount = 1,
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

    private static IncompleteItemPartition PartitionIncompleteItems(VisionAgentBuildReadinessSnapshot readiness)
    {
        var resources = readiness.Blockers
            .Where(blocker => blocker.Resource != null)
            .Select(blocker => (Blocker: blocker, Resource: blocker.Resource!))
            .GroupBy(item => ResourceItemKey(item.Resource), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Blocker.BlocksBuild).First())
            .ToList();

        var resourceFields = resources
            .Select(item => CanonicalField(item.Blocker.Field))
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blockingResourceKeys = resources
            .Where(item => item.Blocker.BlocksBuild)
            .Select(item => ResourceItemKey(item.Resource))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deferredResourceKeys = resources
            .Where(item => !item.Blocker.BlocksBuild)
            .Select(item => ResourceItemKey(item.Resource))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buildRequiredResourceCount = resources.Count(item =>
            item.Resource.DraftPolicy.Equals(VisionAgentResourceDraftPolicies.BuildRequired, StringComparison.OrdinalIgnoreCase));
        var draftAllowedResourceCount = resources.Count - buildRequiredResourceCount;

        var blockingFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deferredFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var blocker in readiness.Blockers)
        {
            if (blocker.Resource != null ||
                blocker.Category.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase) ||
                blocker.Category.Equals(VisionAgentBuildBlockerCategories.ContractWarning, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var field = CanonicalField(blocker.Field);
            if (!string.IsNullOrWhiteSpace(field) && resourceFields.Contains(field))
            {
                continue;
            }

            var key = FieldItemKey(blocker);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (blocker.BlocksBuild) blockingFieldKeys.Add(key);
            else deferredFieldKeys.Add(key);
        }

        foreach (var field in readiness.RemainingFields)
        {
            var canonicalField = CanonicalField(field);
            if (string.IsNullOrWhiteSpace(canonicalField) || resourceFields.Contains(canonicalField)) continue;
            var key = $"field:{canonicalField}";
            if (!blockingFieldKeys.Contains(key)) deferredFieldKeys.Add(key);
        }

        deferredFieldKeys.ExceptWith(blockingFieldKeys);
        var mustConfirmKeys = new HashSet<string>(blockingFieldKeys, StringComparer.OrdinalIgnoreCase);
        mustConfirmKeys.UnionWith(blockingResourceKeys);
        var fillLaterKeys = new HashSet<string>(deferredFieldKeys, StringComparer.OrdinalIgnoreCase);
        fillLaterKeys.UnionWith(deferredResourceKeys);
        fillLaterKeys.ExceptWith(mustConfirmKeys);
        var allKeys = new HashSet<string>(mustConfirmKeys, StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(fillLaterKeys);

        return new IncompleteItemPartition(
            blockingFieldKeys.Count,
            buildRequiredResourceCount,
            deferredFieldKeys.Count,
            draftAllowedResourceCount,
            mustConfirmKeys.Count,
            fillLaterKeys.Count,
            allKeys.Count);
    }

    private static string ResourceItemKey(VisionAgentResourceRequirement resource)
    {
        var canonicalId = VisionAgentResourceIdentity.TryParseCanonicalId(resource.CanonicalId, out _, out _, out _)
            ? VisionAgentResourceIdentity.Canonicalize(resource.CanonicalId)
            : VisionAgentResourceIdentity.CreateCanonicalId(
                resource.ResourceType,
                resource.OperatorKey,
                resource.ParameterName,
                resource.ResourceKey);
        return $"resource:{canonicalId}";
    }

    private static string FieldItemKey(VisionAgentBuildBlocker blocker)
    {
        var field = CanonicalField(blocker.Field);
        if (!string.IsNullOrWhiteSpace(field)) return $"field:{field}";
        var questionId = Clean(blocker.QuestionId);
        if (!string.IsNullOrWhiteSpace(questionId)) return $"question:{questionId}";
        var blockerId = Clean(blocker.Id);
        return string.IsNullOrWhiteSpace(blockerId) ? string.Empty : $"blocker:{blockerId}";
    }

    private static string CanonicalField(string? field) =>
        VisionAgentPlanFieldPolicy.NormalizeField(field);

    private sealed record IncompleteItemPartition(
        int BuildBlockingConfirmationCount,
        int BuildRequiredResourceCount,
        int DeferredFieldCount,
        int DraftAllowedResourceCount,
        int MustConfirmBeforeBuildCount,
        int FillLaterCount,
        int TotalIncompleteCount);

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
        var publicWarnings = result.BuildResult?.PublicWarnings?.ToList() ?? [];
        publicWarnings.AddRange(contract.PublicWarnings);

        result.PlanId = contract.PlanId;
        result.PlanHash = contract.PlanHash;
        result.ContractVersion = contract.ContractVersion;
        result.AnswerSetFingerprint = answerSetFingerprint;
        result.RequestedMode = requestedMode;
        result.EffectiveMode = effectiveMode;
        // Retained for wire compatibility; the retired runtime cannot enter or fall back to Tool Loop.
        result.ToolLoopEntered = false;
        result.FallbackReason = string.Empty;
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
            EffectiveMode = effectiveMode,
            ToolLoopEntered = false,
            FallbackReason = string.Empty,
            Flow = buildResult.Flow ?? result.Flow,
            PublicWarnings = publicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        EnforceBuildArtifactAdmission(result);
    }

    private void EnforceBuildArtifactAdmission(AiFlowGenerationResult result)
    {
        var flow = result.BuildResult?.Flow as OperatorFlowDto ?? result.Flow as OperatorFlowDto;
        if (flow == null)
        {
            return;
        }

        if (_workflowArtifactAdmissionGate == null)
        {
            throw WorkflowArtifactAdmissionFailures.GateUnavailable("vision_agent.build_result");
        }

        var admittedBuildResult = result.BuildResult;
        var admission = _workflowArtifactAdmissionGate.Inspect(
            flow,
            "vision_agent.build_result",
            context: admittedBuildResult == null
                ? null
                : new WorkflowArtifactAdmissionContext
                {
                    TaskType = admittedBuildResult.TaskType,
                    RouteSemanticsSatisfied = admittedBuildResult.RouteSemanticsSatisfied,
                    ArtifactFingerprint = admittedBuildResult.ArtifactFingerprint
                });
        if (admission.Disposition == WorkflowArtifactAdmissionDisposition.Canonical && admission.Flow != null)
        {
            result.Flow = admission.Flow;
            result.BuildResult = result.BuildResult! with { Flow = admission.Flow };
            return;
        }

        if (admission.Report.PreviewOnly && admission.Flow != null)
        {
            result.Flow = admission.Flow;
            if (result.BuildResult != null)
            {
                result.BuildResult = result.BuildResult with
                {
                    Flow = admission.Flow,
                    PublicWarnings = result.BuildResult.PublicWarnings
                        .Append("safe_scaffold_requires_user_review")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            }

            return;
        }

        var diagnostic = admission.Report.Diagnostics.FirstOrDefault()?.Code ??
            $"workflow_artifact_{admission.Disposition.ToString().ToLowerInvariant()}";
        var message = admission.Report.PublicMessage;
        result.Success = false;
        result.CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed;
        result.FailureType = AiFlowGenerationResult.FailureTypeSystemError;
        result.ErrorMessage = $"Build result was blocked by workflow artifact admission: {diagnostic}. {message}";
        result.FailureSummary = new AiFailureSummary
        {
            Category = "workflow_artifact_admission",
            Code = diagnostic,
            Message = result.ErrorMessage,
            RepairTarget = "修复并重新构建完整 Workflow Artifact；禁止将当前结果直接应用或部署。"
        };
        result.Flow = null;
        result.InteractionState = AiInteractionStates.Failed;
        var buildResult = result.BuildResult ?? new VisionAgentBuildResult();
        var blockedGate = buildResult.ApplyGate with
        {
            Blocked = true,
            CanvasApplyReady = false,
            RuntimeDraftReady = false,
            DeploymentReady = false,
            Status = "blocked",
            ApplyBlockers = buildResult.ApplyGate.ApplyBlockers
                .Append(diagnostic)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DeploymentBlockers = buildResult.ApplyGate.DeploymentBlockers
                .Append(diagnostic)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ArtifactDisposition = admission.Disposition.ToString().ToLowerInvariant(),
            MetadataOnly = true
        };
        result.BuildResult = buildResult with
        {
            Flow = null,
            ArtifactDisposition = admission.Disposition.ToString().ToLowerInvariant(),
            ApplyGate = blockedGate,
            PublicWarnings = buildResult.PublicWarnings
                .Append(diagnostic)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MetadataOnly = true
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
            ? plan.ClarificationQuestions.Any(question => !string.IsNullOrWhiteSpace(question.Field))
                ? VisionAgentPlanContractVersions.V2
                : VisionAgentPlanContractVersions.V1
            : string.Empty;
    }

    private static string NormalizeRequirementMode(string? value)
    {
        return string.Equals(value, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
            ? AiRequirementModes.Draft
            : AiRequirementModes.Strict;
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
