using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentIntentRouterService
{
    Task<VisionAgentIntentRouterResult> RouteAsync(
        VisionAgentIntentRouterRequest request,
        CancellationToken cancellationToken);
}

public interface IVisionAgentIntentRouterCompletionSource
{
    Task<string> CompleteAsync(
        VisionAgentIntentRouterCompletionRequest request,
        CancellationToken cancellationToken);
}

public sealed record VisionAgentIntentRouterCompletionRequest(
    string SystemPrompt,
    List<ChatMessage> Messages,
    string ModelRole);

public sealed class LlmVisionAgentIntentRouterCompletionSource : IVisionAgentIntentRouterCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;

    public LlmVisionAgentIntentRouterCompletionSource(AiGenerationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<string> CompleteAsync(
        VisionAgentIntentRouterCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var model = _orchestrator.ResolveModelForRole(request.ModelRole);
        var completion = await _orchestrator.CompleteAsync(
            request.SystemPrompt,
            request.Messages,
            model,
            cancellationToken);
        return completion.Content ?? string.Empty;
    }
}

public sealed class VisionAgentIntentRouterOptions
{
    public const string SectionName = "AI:VisionAgent:IntentRouter";

    public bool Enabled { get; set; } = true;

    public string ModelRole { get; set; } = AiModelConfig.RolePlanner;

    public bool AllowRuleFallback { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxContextChars { get; set; } = 8_000;

    public VisionAgentIntentRouterOptions Normalize()
    {
        ModelRole = AiModelConfig.NormalizeRoleName(ModelRole);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 90);
        MaxContextChars = Math.Clamp(MaxContextChars, 2_000, 24_000);
        return this;
    }
}

public sealed class VisionAgentIntentRouterService : IVisionAgentIntentRouterService
{
    public const string IntentCasualChat = "casual_chat";
    public const string IntentHelp = "help";
    public const string IntentAmbiguousVisionRequirement = "ambiguous_vision_requirement";
    public const string IntentActionableVisionPlan = "actionable_vision_plan";
    public const string IntentModifyExistingFlow = "modify_existing_flow";
    public const string IntentBuildFromConfirmedPlan = "build_from_confirmed_plan";
    public const string IntentDirectBuildDebug = "direct_build_debug";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Regex UnsafeRegex = new(
        @"(?i)((?:rawPrompt|raw_prompt|systemPrompt|system_prompt|chain[-_ ]?of[-_ ]?thought|reasoning_content)(?:\s*[:=]\s*[^\s,;]+)?|[A-Za-z]:\\[^\s,;]+|\\\\[^\s,;]+|/(?:users|home|var|tmp|mnt|data)/[^\s,;]+|data:image/[^\s,;]+|base64[^\s,;]*|sk-[A-Za-z0-9_\-]{8,}|(?:api[_-]?key|x-api-key|token|secret|authorization|headers?|baseUrl|base_url|endpoint)\s*[:=]\s*[^\s,;]+|bearer\s+[A-Za-z0-9._\-]+|\b(?:\d{1,3}\.){3}\d{1,3}\b|\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b|\bM\d+(?:\.\d+)?\b|\bD\d+\b|plc://[^\s,;]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IVisionAgentIntentRouterCompletionSource _completionSource;
    private readonly VisionAgentIntentRouterOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentIntentRouterService> _logger;
    private readonly IVisionAgentSemanticExtractorService? _semanticExtractor;

    public VisionAgentIntentRouterService(
        IVisionAgentIntentRouterCompletionSource completionSource,
        IOptions<VisionAgentIntentRouterOptions>? options,
        Microsoft.Extensions.Logging.ILogger<VisionAgentIntentRouterService> logger,
        IVisionAgentSemanticExtractorService? semanticExtractor = null)
    {
        _completionSource = completionSource;
        _options = (options?.Value ?? new VisionAgentIntentRouterOptions()).Normalize();
        _logger = logger;
        _semanticExtractor = semanticExtractor;
    }

    public async Task<VisionAgentIntentRouterResult> RouteAsync(
        VisionAgentIntentRouterRequest request,
        CancellationToken cancellationToken)
    {
        var routedRequest = await AttachSemanticExtractionAsync(request, cancellationToken);
        if (!_options.Enabled)
        {
            return RuleFallback(routedRequest, "router_disabled");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var prompt = BuildPrompt(routedRequest, _options);
            var completion = await _completionSource.CompleteAsync(
                new VisionAgentIntentRouterCompletionRequest(prompt.SystemPrompt, prompt.Messages, _options.ModelRole),
                timeout.Token);
            return RepairResult(ParseResult(completion), routedRequest, "model_router", string.Empty);
        }
        catch (Exception firstError) when (!cancellationToken.IsCancellationRequested &&
                                         !IsUnauthorized(firstError) &&
                                         IsRepairable(firstError))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                var repairPrompt = BuildRepairPrompt(routedRequest, firstError.Message, _options);
                var repaired = await _completionSource.CompleteAsync(
                    new VisionAgentIntentRouterCompletionRequest(repairPrompt.SystemPrompt, repairPrompt.Messages, _options.ModelRole),
                    timeout.Token);
                return RepairResult(ParseResult(repaired), routedRequest, "model_router_repaired", "router_json_repaired");
            }
            catch (Exception repairError) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Vision Agent Intent Router repair failed; rule fallback will be used. Error={Error}",
                    repairError.Message);
                return RuleFallback(routedRequest, IsUnauthorized(repairError) ? "router_unauthorized" : "router_repair_failed");
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Vision Agent Intent Router failed; rule fallback will be used. Error={Error}",
                error.Message);
            return RuleFallback(routedRequest, IsUnauthorized(error) ? "router_unauthorized" : "router_failed");
        }
    }

    private async Task<VisionAgentIntentRouterRequest> AttachSemanticExtractionAsync(
        VisionAgentIntentRouterRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SemanticExtraction != null)
        {
            return request with
            {
                SemanticExtraction = VisionAgentSemanticExtractionSafety.Sanitize(request.SemanticExtraction)
            };
        }

        if (_semanticExtractor == null)
        {
            return request;
        }

        var semantic = await _semanticExtractor.ExtractAsync(
            new VisionAgentSemanticExtractionRequest
            {
                Description = request.Description,
                OriginalUserPrompt = request.OriginalUserPrompt,
                AdditionalContext = request.AdditionalContext,
                SessionId = request.SessionId,
                Mode = request.Mode,
                HasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
                HasPendingPlan = request.HasPendingPlan,
                TemplateSelection = request.TemplateSelection,
                AttachmentSummary = request.AttachmentSummary,
                HistorySummary = request.HistorySummary,
                CurrentFlowSummary = SummarizeJsonText(request.CurrentFlowSnapshot, 1_000),
                MetadataOnly = true
            },
            cancellationToken);
        return request with { SemanticExtraction = semantic };
    }

    private static VisionAgentIntentRouterResult ParseResult(string completion)
    {
        var json = ExtractJsonObject(completion);
        return JsonSerializer.Deserialize<VisionAgentIntentRouterResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("Intent Router returned an empty JSON object.");
    }

    private static VisionAgentIntentRouterResult RepairResult(
        VisionAgentIntentRouterResult candidate,
        VisionAgentIntentRouterRequest request,
        string source,
        string fallbackReason)
    {
        var planAnswerUpdates = ExtractPlanAnswerUpdates(request);
        var shouldResetPendingPlan = request.HasPendingPlan && LooksLikeExplicitNewPlanRequest(Clean(request.Description));
        var shouldMergeIntoPendingPlan = request.HasPendingPlan &&
                                         planAnswerUpdates.Count > 0 &&
                                         !shouldResetPendingPlan;
        var resolvedPlanFields = BuildResolvedPlanFields(
            request,
            shouldMergeIntoPendingPlan ? planAnswerUpdates : []);
        var remainingPlanFields = shouldMergeIntoPendingPlan
            ? MergeRemainingPlanFields(request, planAnswerUpdates)
            : NormalizePlanFields(request.RemainingPlanFields);
        var effectiveRequest = request with
        {
            ResolvedPlanFields = resolvedPlanFields,
            RemainingPlanFields = remainingPlanFields
        };
        var maturity = EvaluateMaturity(effectiveRequest);
        resolvedPlanFields = MergeMaturityResolvedFields(resolvedPlanFields, maturity);
        remainingPlanFields = BuildRemainingPlanFields(remainingPlanFields, maturity, resolvedPlanFields);
        effectiveRequest = effectiveRequest with
        {
            ResolvedPlanFields = resolvedPlanFields,
            RemainingPlanFields = remainingPlanFields
        };
        var intent = NormalizeIntent(candidate.Intent);
        var confidence = NormalizeConfidence(candidate.Confidence);
        var canBuild = intent is IntentActionableVisionPlan or IntentModifyExistingFlow or IntentBuildFromConfirmedPlan or IntentDirectBuildDebug;
        var shouldOpenPlan = intent == IntentActionableVisionPlan;
        var shouldBuildDirectly = intent is IntentModifyExistingFlow or IntentBuildFromConfirmedPlan or IntentDirectBuildDebug;
        var needsClarification = intent == IntentAmbiguousVisionRequirement;
        var questions = SanitizeQuestions(candidate.ClarificationQuestions);

        if (intent == IntentBuildFromConfirmedPlan && !request.HasPendingPlan)
        {
            intent = IntentAmbiguousVisionRequirement;
            confidence = "medium";
            canBuild = false;
            shouldBuildDirectly = false;
            needsClarification = true;
            questions = DefaultClarificationQuestions();
            fallbackReason = "confirmed_plan_missing";
        }

        if (intent == IntentDirectBuildDebug && !request.DeveloperDirectBuildDebug)
        {
            intent = IntentActionableVisionPlan;
            confidence = "medium";
            shouldOpenPlan = true;
            shouldBuildDirectly = false;
            needsClarification = false;
            fallbackReason = "direct_build_debug_not_enabled";
        }

        if (intent == IntentAmbiguousVisionRequirement && questions.Count == 0)
        {
            questions = DefaultClarificationQuestions();
        }

        ApplyMaturityGate(
            effectiveRequest,
            maturity,
            ref intent,
            ref confidence,
            ref canBuild,
            ref shouldOpenPlan,
            ref shouldBuildDirectly,
            ref needsClarification,
            ref questions,
            ref fallbackReason);

        return candidate with
        {
            Intent = intent,
            Confidence = confidence,
            ShouldOpenPlan = shouldOpenPlan,
            ShouldBuildDirectly = shouldBuildDirectly,
            CanBuild = canBuild,
            NeedsClarification = needsClarification,
            PublicReason = SafeText(intent == IntentAmbiguousVisionRequirement && !maturity.CanPlan
                ? maturity.PublicReason
                : string.IsNullOrWhiteSpace(candidate.PublicReason)
                ? DefaultReason(intent)
                : candidate.PublicReason),
            AssistantReply = ResolveAssistantReply(candidate.AssistantReply, candidate.PublicReason, intent, request),
            ClarificationQuestions = questions,
            FallbackAllowed = candidate.FallbackAllowed,
            RouterSource = source,
            FallbackReason = SafeToken(fallbackReason),
            SemanticExtraction = request.SemanticExtraction,
            RequirementMaturity = maturity,
            DecisionTrace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
                ToMaturityRequest(effectiveRequest),
                maturity,
                intent,
                shouldBuildDirectly ? "build_ready" : shouldOpenPlan ? "planning" : needsClarification ? "clarifying" : "idle",
                fallbackReason),
            ShouldMergeIntoPendingPlan = shouldMergeIntoPendingPlan,
            ShouldResetPendingPlan = shouldResetPendingPlan || !request.HasPendingPlan,
            PlanAnswerUpdates = planAnswerUpdates,
            ResolvedPlanFields = resolvedPlanFields,
            RemainingPlanFields = remainingPlanFields,
            MetadataOnly = true
        };
    }

    private static VisionAgentIntentRouterResult RuleFallback(
        VisionAgentIntentRouterRequest request,
        string reason)
    {
        var text = Clean(request.Description);
        var planAnswerUpdates = ExtractPlanAnswerUpdates(request);
        var shouldResetPendingPlan = request.HasPendingPlan && LooksLikeExplicitNewPlanRequest(text);
        var shouldMergeIntoPendingPlan = request.HasPendingPlan &&
                                         planAnswerUpdates.Count > 0 &&
                                         !shouldResetPendingPlan;
        var resolvedPlanFields = BuildResolvedPlanFields(
            request,
            shouldMergeIntoPendingPlan ? planAnswerUpdates : []);
        var remainingPlanFields = shouldMergeIntoPendingPlan
            ? MergeRemainingPlanFields(request, planAnswerUpdates)
            : NormalizePlanFields(request.RemainingPlanFields);
        var effectiveRequest = request with
        {
            ResolvedPlanFields = resolvedPlanFields,
            RemainingPlanFields = remainingPlanFields
        };
        var maturity = EvaluateMaturity(effectiveRequest);
        resolvedPlanFields = MergeMaturityResolvedFields(resolvedPlanFields, maturity);
        remainingPlanFields = BuildRemainingPlanFields(remainingPlanFields, maturity, resolvedPlanFields);
        effectiveRequest = effectiveRequest with
        {
            ResolvedPlanFields = resolvedPlanFields,
            RemainingPlanFields = remainingPlanFields
        };
        var intent = ResolveRuleFallbackIntent(text, effectiveRequest, maturity);
        var isCasual = intent == IntentCasualChat;
        var isHelp = intent == IntentHelp;
        var isActionable = intent == IntentActionableVisionPlan;
        var isModify = intent == IntentModifyExistingFlow;
        var isConfirmed = intent == IntentBuildFromConfirmedPlan;
        var isDirectDebug = intent == IntentDirectBuildDebug;
        var isAmbiguous = intent == IntentAmbiguousVisionRequirement;
        var questions = isAmbiguous ? MaturityClarificationQuestions(maturity) : [];
        var canBuild = isActionable || isModify || isConfirmed || isDirectDebug;
        var shouldOpenPlan = isActionable;
        var shouldBuildDirectly = isModify || isConfirmed || isDirectDebug;
        var needsClarification = isAmbiguous;
        var confidence = reason == "router_unauthorized" && (isCasual || isHelp) ? "high" : "medium";
        var fallbackReason = reason;

        ApplyMaturityGate(
            effectiveRequest,
            maturity,
            ref intent,
            ref confidence,
            ref canBuild,
            ref shouldOpenPlan,
            ref shouldBuildDirectly,
            ref needsClarification,
            ref questions,
            ref fallbackReason);

        return new VisionAgentIntentRouterResult
        {
            Intent = intent,
            Confidence = confidence,
            ShouldOpenPlan = shouldOpenPlan,
            ShouldBuildDirectly = shouldBuildDirectly,
            CanBuild = canBuild,
            NeedsClarification = needsClarification,
            PublicReason = reason == "router_unauthorized"
                ? UnauthorizedReason(intent)
                : RuleFallbackReason(reason, maturity, intent),
            AssistantReply = DefaultReply(intent, request),
            ClarificationQuestions = questions,
            FallbackAllowed = true,
            RouterSource = "rule_fallback",
            FallbackReason = SafeToken(fallbackReason),
            SemanticExtraction = request.SemanticExtraction,
            RequirementMaturity = maturity,
            DecisionTrace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
                ToMaturityRequest(effectiveRequest),
                maturity,
                intent,
                shouldBuildDirectly ? "build_ready" : shouldOpenPlan ? "planning" : needsClarification ? "clarifying" : "idle",
                fallbackReason),
            ShouldMergeIntoPendingPlan = shouldMergeIntoPendingPlan,
            ShouldResetPendingPlan = shouldResetPendingPlan || !request.HasPendingPlan,
            PlanAnswerUpdates = planAnswerUpdates,
            ResolvedPlanFields = resolvedPlanFields,
            RemainingPlanFields = remainingPlanFields,
            MetadataOnly = true
        };
    }

    private static string ResolveRuleFallbackIntent(
        string text,
        VisionAgentIntentRouterRequest request,
        AiRequirementMaturityResult maturity)
    {
        if (request.DeveloperDirectBuildDebug)
        {
            return IntentDirectBuildDebug;
        }

        if (LooksLikeHelp(text))
        {
            return IntentHelp;
        }

        if (LooksLikeCasual(text))
        {
            return IntentCasualChat;
        }

        if (request.HasPendingPlan && LooksLikeConfirmedPlanBuild(text))
        {
            return IntentBuildFromConfirmedPlan;
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot) && LooksLikeModifyExistingFlow(text))
        {
            return IntentModifyExistingFlow;
        }

        return VisionAgentRequirementMaturityGate.ToRouterIntent(maturity);
    }

    private static AiRequirementMaturityResult EvaluateMaturity(VisionAgentIntentRouterRequest request)
    {
        return VisionAgentRequirementMaturityGate.Evaluate(ToMaturityRequest(request), request.SemanticExtraction);
    }

    private static List<VisionAgentPlanAnswer> ExtractPlanAnswerUpdates(VisionAgentIntentRouterRequest request)
    {
        if (!request.HasPendingPlan)
        {
            return [];
        }

        var remaining = NormalizePlanFields(request.RemainingPlanFields).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == 0)
        {
            return [];
        }

        var semantic = request.SemanticExtraction;
        var updates = new List<VisionAgentPlanAnswer>();
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.InspectionObject, semantic?.InspectionObject);
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.ImageSource, semantic?.ImageSource);
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.AcceptanceCriteria, VisionAgentPlanFieldPolicy.FormatAcceptanceCriteria(semantic?.OkCondition, semantic?.NgCondition));
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.OutputTarget, semantic?.OutputTarget);
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.TargetAttribute, semantic?.TargetAttribute);
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.DefectType, semantic?.DefectType);
        AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.MeasurementTarget, semantic?.MeasurementTarget);

        var taskType = Clean(semantic?.TaskType);
        if (!string.IsNullOrWhiteSpace(taskType) &&
            !string.Equals(taskType, AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(taskType, AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            AddRequirementAnswer(updates, remaining, VisionAgentPlanAnswerFields.TaskType, taskType);
        }

        if (updates.Count == 0 && remaining.Count == 1)
        {
            var field = remaining.First();
            if (IsRequirementAnswerField(field))
            {
                AddRequirementAnswer(updates, remaining, field, request.Description);
            }
        }

        return updates
            .GroupBy(answer => answer.Field, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddRequirementAnswer(
        List<VisionAgentPlanAnswer> updates,
        ISet<string> remaining,
        string field,
        string? value)
    {
        var normalizedField = NormalizePlanField(field);
        var text = SafeText(value);
        if (string.IsNullOrWhiteSpace(normalizedField) ||
            string.IsNullOrWhiteSpace(text) ||
            !remaining.Contains(normalizedField) ||
            !IsRequirementAnswerField(normalizedField))
        {
            return;
        }

        updates.Add(new VisionAgentPlanAnswer
        {
            Field = normalizedField,
            Value = Truncate(text, 256),
            Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
        });
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.Select(Clean).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static List<string> NormalizePlanFields(IEnumerable<string>? fields)
    {
        return (fields ?? [])
            .Select(NormalizePlanField)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePlanField(string? field)
    {
        var value = Clean(field).ToLowerInvariant();
        return value switch
        {
            VisionAgentPlanAnswerFields.InspectionObject => VisionAgentPlanAnswerFields.InspectionObject,
            VisionAgentPlanAnswerFields.TaskType => VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource => VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria => VisionAgentPlanAnswerFields.AcceptanceCriteria,
            VisionAgentPlanAnswerFields.OutputTarget => VisionAgentPlanAnswerFields.OutputTarget,
            VisionAgentPlanAnswerFields.TargetAttribute => VisionAgentPlanAnswerFields.TargetAttribute,
            VisionAgentPlanAnswerFields.DefectType => VisionAgentPlanAnswerFields.DefectType,
            VisionAgentPlanAnswerFields.MeasurementTarget => VisionAgentPlanAnswerFields.MeasurementTarget,
            VisionAgentPlanAnswerFields.AlgorithmStrategy => VisionAgentPlanAnswerFields.AlgorithmStrategy,
            VisionAgentPlanAnswerFields.RoiStrategy => VisionAgentPlanAnswerFields.RoiStrategy,
            VisionAgentPlanAnswerFields.TemplateStrategy => VisionAgentPlanAnswerFields.TemplateStrategy,
            "model_or_rule_strategy" or "classification_strategy" => VisionAgentPlanAnswerFields.AlgorithmStrategy,
            _ when value.Contains("inspection_object", StringComparison.OrdinalIgnoreCase) => VisionAgentPlanAnswerFields.InspectionObject,
            _ when value.Contains("task_type", StringComparison.OrdinalIgnoreCase) => VisionAgentPlanAnswerFields.TaskType,
            _ when value.Contains("image_source", StringComparison.OrdinalIgnoreCase) => VisionAgentPlanAnswerFields.ImageSource,
            _ when value.Contains("acceptance_criteria", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("condition", StringComparison.OrdinalIgnoreCase) => VisionAgentPlanAnswerFields.AcceptanceCriteria,
            _ when value.Contains("strategy", StringComparison.OrdinalIgnoreCase) => VisionAgentPlanAnswerFields.AlgorithmStrategy,
            _ => string.Empty
        };
    }

    private static bool IsRequirementAnswerField(string field)
    {
        return NormalizePlanField(field) is
            VisionAgentPlanAnswerFields.InspectionObject or
            VisionAgentPlanAnswerFields.TaskType or
            VisionAgentPlanAnswerFields.ImageSource or
            VisionAgentPlanAnswerFields.AcceptanceCriteria or
            VisionAgentPlanAnswerFields.OutputTarget or
            VisionAgentPlanAnswerFields.TargetAttribute or
            VisionAgentPlanAnswerFields.DefectType or
            VisionAgentPlanAnswerFields.MeasurementTarget;
    }

    private static List<string> BuildResolvedPlanFields(
        VisionAgentIntentRouterRequest request,
        IReadOnlyList<VisionAgentPlanAnswer>? updates = null)
    {
        return NormalizePlanFields(ResolvedFieldsFromAnswers(request.ConfirmedPlanAnswers)
                .Concat(ResolvedFieldsFromAnswers(updates ?? []))
                .Concat(ResolvedFieldsFromSemantic(request.SemanticExtraction)))
            .ToList();
    }

    private static IEnumerable<string> ResolvedFieldsFromAnswers(IEnumerable<VisionAgentPlanAnswer>? answers)
    {
        return (answers ?? [])
            .Where(answer => !string.IsNullOrWhiteSpace(answer.Value) &&
                             !VisionAgentPlanFieldPolicy.IsPlaceholderValue(answer.Value))
            .Select(answer => answer.Field);
    }

    private static IEnumerable<string> ResolvedFieldsFromSemantic(VisionAgentSemanticExtractionResult? semantic)
    {
        if (semantic == null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(semantic.InspectionObject) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(semantic.InspectionObject))
        {
            yield return VisionAgentPlanAnswerFields.InspectionObject;
        }

        var taskType = Clean(semantic.TaskType);
        if (!string.IsNullOrWhiteSpace(taskType) &&
            !taskType.Equals(AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
            !taskType.Equals(AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(taskType))
        {
            yield return VisionAgentPlanAnswerFields.TaskType;
        }

        if (!string.IsNullOrWhiteSpace(semantic.ImageSource) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(semantic.ImageSource))
        {
            yield return VisionAgentPlanAnswerFields.ImageSource;
        }

        if (!string.IsNullOrWhiteSpace(semantic.OutputTarget) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(semantic.OutputTarget))
        {
            yield return VisionAgentPlanAnswerFields.OutputTarget;
        }

        if (!string.IsNullOrWhiteSpace(semantic.TargetAttribute) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(semantic.TargetAttribute))
        {
            yield return VisionAgentPlanAnswerFields.TargetAttribute;
        }

        if (!string.IsNullOrWhiteSpace(semantic.DefectType) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(semantic.DefectType))
        {
            yield return VisionAgentPlanAnswerFields.DefectType;
        }

        if (!string.IsNullOrWhiteSpace(semantic.MeasurementTarget) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(semantic.MeasurementTarget))
        {
            yield return VisionAgentPlanAnswerFields.MeasurementTarget;
        }

        var acceptance = VisionAgentPlanFieldPolicy.FormatAcceptanceCriteria(semantic.OkCondition, semantic.NgCondition);
        if (!string.IsNullOrWhiteSpace(acceptance) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(acceptance))
        {
            yield return VisionAgentPlanAnswerFields.AcceptanceCriteria;
        }
    }

    private static List<string> MergeRemainingPlanFields(
        VisionAgentIntentRouterRequest request,
        IReadOnlyList<VisionAgentPlanAnswer> updates)
    {
        var updatedFields = updates.Select(answer => NormalizePlanField(answer.Field))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return NormalizePlanFields(request.RemainingPlanFields)
            .Where(field => !updatedFields.Contains(field))
            .ToList();
    }

    private static List<string> BuildRemainingPlanFields(
        IEnumerable<string> existingRemaining,
        AiRequirementMaturityResult maturity,
        IReadOnlyList<string> resolvedPlanFields)
    {
        var resolved = resolvedPlanFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return NormalizePlanFields(existingRemaining.Concat(maturity.MissingFields))
            .Where(field => !resolved.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> MergeMaturityResolvedFields(
        IEnumerable<string> existingResolved,
        AiRequirementMaturityResult maturity)
    {
        var fields = NormalizePlanFields(existingResolved).ToList();
        var missing = maturity.MissingFields
            .Select(NormalizePlanField)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!missing.Contains(VisionAgentPlanAnswerFields.InspectionObject) &&
            maturity.ObjectSignals.Any(signal => !string.IsNullOrWhiteSpace(signal)))
        {
            fields.Add(VisionAgentPlanAnswerFields.InspectionObject);
        }

        if (!missing.Contains(VisionAgentPlanAnswerFields.TaskType) &&
            !string.IsNullOrWhiteSpace(maturity.TaskType) &&
            !maturity.TaskType.Equals(AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
            !maturity.TaskType.Equals(AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(VisionAgentPlanAnswerFields.TaskType);
        }

        return NormalizePlanFields(fields);
    }

    private static bool HasBlockingRemainingPlanFields(
        VisionAgentIntentRouterRequest request,
        AiRequirementMaturityResult maturity)
    {
        return NormalizePlanFields(request.RemainingPlanFields).Any(field =>
            string.Equals(request.RequirementMode, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
                ? VisionAgentPlanFieldPolicy.IsDraftBlocking(field, maturity.TaskType, maturity)
                : VisionAgentPlanFieldPolicy.IsStrictBlocking(field, maturity.TaskType, maturity));
    }

    private static VisionAgentRequirementMaturityRequest ToMaturityRequest(VisionAgentIntentRouterRequest request)
    {
        return new VisionAgentRequirementMaturityRequest
        {
            Description = request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = request.Mode,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            HasPendingPlan = request.HasPendingPlan,
            DeveloperDirectBuildDebug = request.DeveloperDirectBuildDebug,
            TemplateSelection = request.TemplateSelection,
            RequirementMode = request.RequirementMode
        };
    }

    private static void ApplyMaturityGate(
        VisionAgentIntentRouterRequest request,
        AiRequirementMaturityResult maturity,
        ref string intent,
        ref string confidence,
        ref bool canBuild,
        ref bool shouldOpenPlan,
        ref bool shouldBuildDirectly,
        ref bool needsClarification,
        ref List<string> questions,
        ref string fallbackReason)
    {
        if (intent == IntentBuildFromConfirmedPlan && request.HasPendingPlan)
        {
            if (CanBuildFromConfirmedPlan(request, maturity))
            {
                return;
            }

            intent = IntentAmbiguousVisionRequirement;
            confidence = confidence == "high" ? "medium" : confidence;
            canBuild = false;
            shouldOpenPlan = false;
            shouldBuildDirectly = false;
            needsClarification = true;
            questions = questions.Count == 0 ? MaturityClarificationQuestions(maturity) : questions;
            fallbackReason = AppendFallbackReason(fallbackReason, "pending_plan_answers_required");
            return;
        }

        if (intent == IntentDirectBuildDebug && request.DeveloperDirectBuildDebug)
        {
            return;
        }

        if (maturity.Maturity == AiRequirementMaturity.ChatOrHelp)
        {
            intent = LooksLikeCasual(Clean(request.Description)) ? IntentCasualChat : IntentHelp;
            confidence = "high";
            canBuild = false;
            shouldOpenPlan = false;
            shouldBuildDirectly = false;
            needsClarification = false;
            questions = [];
            return;
        }

        if (maturity.Maturity == AiRequirementMaturity.ModifyExistingFlow &&
            !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot))
        {
            intent = IntentModifyExistingFlow;
            confidence = "high";
            canBuild = true;
            shouldOpenPlan = false;
            shouldBuildDirectly = true;
            needsClarification = false;
            questions = [];
            return;
        }

        if (!maturity.CanPlan)
        {
            if (ShouldOpenPlanDespiteMaturityBlock(request, maturity))
            {
                intent = IntentActionableVisionPlan;
                confidence = "low";
                canBuild = false;
                shouldOpenPlan = true;
                shouldBuildDirectly = false;
                needsClarification = false;
                questions = [];
                fallbackReason = AppendFallbackReason(fallbackReason, "planning_allowed_maturity_needs_plan");
                return;
            }

            intent = IntentAmbiguousVisionRequirement;
            confidence = "medium";
            canBuild = false;
            shouldOpenPlan = false;
            shouldBuildDirectly = false;
            needsClarification = true;
            questions = questions.Count == 0 ? MaturityClarificationQuestions(maturity) : questions;
            fallbackReason = AppendFallbackReason(fallbackReason, "maturity_gate_blocked");
            return;
        }

        if (intent == IntentAmbiguousVisionRequirement)
        {
            intent = IntentActionableVisionPlan;
            confidence = confidence == "low" ? "medium" : confidence;
        }

        if (intent == IntentActionableVisionPlan)
        {
            canBuild = maturity.CanBuild;
            shouldOpenPlan = true;
            shouldBuildDirectly = false;
            needsClarification = false;
            questions = [];
            if (!maturity.CanBuild)
            {
                confidence = "low";
                fallbackReason = AppendFallbackReason(fallbackReason, "planning_allowed_build_blocked");
            }
        }
    }

    private static bool ShouldOpenPlanDespiteMaturityBlock(
        VisionAgentIntentRouterRequest request,
        AiRequirementMaturityResult maturity)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return false;
        }

        if (maturity.Maturity == AiRequirementMaturity.AbstractGoal ||
            string.Equals(maturity.TaskType, AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.TemplateSelection != null)
        {
            return true;
        }

        if (request.SemanticExtraction is { IsVisionRequest: true } semantic &&
            !string.Equals(semantic.TaskType, AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (maturity.ObjectSignals.Count > 0 || maturity.TaskSignals.Count > 0)
        {
            return true;
        }

        return LooksLikeActionableVisionNeed(Clean(request.Description));
    }

    private static string AppendFallbackReason(string existing, string reason)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return reason;
        }

        return existing.Contains(reason, StringComparison.OrdinalIgnoreCase)
            ? existing
            : $"{existing}_{reason}";
    }

    private static bool CanBuildFromConfirmedPlan(
        VisionAgentIntentRouterRequest request,
        AiRequirementMaturityResult maturity)
    {
        var resolved = BuildResolvedPlanFields(request).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blockingRemainingFields = NormalizePlanFields(request.RemainingPlanFields.Concat(maturity.MissingFields))
            .Where(field => !resolved.Contains(field))
            .Where(field =>
                string.Equals(request.RequirementMode, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
                    ? VisionAgentPlanFieldPolicy.IsDraftBlocking(field, maturity.TaskType, maturity)
                    : VisionAgentPlanFieldPolicy.IsStrictBlocking(field, maturity.TaskType, maturity))
            .ToList();
        if (blockingRemainingFields.Count == 0)
        {
            return true;
        }

        if (!string.Equals(request.RequirementMode, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) ||
            maturity.CanPlan != true)
        {
            return false;
        }

        var hasObjectOrTask = resolved.Contains(VisionAgentPlanAnswerFields.InspectionObject) ||
                              resolved.Contains(VisionAgentPlanAnswerFields.TaskType) ||
                              maturity.ObjectSignals.Count > 0 ||
                              maturity.TaskSignals.Count > 0 ||
                              (maturity.TaskType != AiVisionTaskTypes.Unknown &&
                               maturity.TaskType != AiVisionTaskTypes.AbstractGoal);
        return hasObjectOrTask && RequestHasPendingPlannerRoute(request);
    }

    private static bool RequestHasPendingPlannerRoute(VisionAgentIntentRouterRequest request)
    {
        var summary = request.PendingPlanSummary;
        if (string.IsNullOrWhiteSpace(summary))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(summary);
            if (document.RootElement.TryGetProperty("route", out var route) ||
                document.RootElement.TryGetProperty("recommendedRoute", out route))
            {
                return RouteHasWorkAndOutput(route);
            }

            if (document.RootElement.TryGetProperty("operators", out var operators))
            {
                return OperatorsHaveWorkAndOutput(operators);
            }

            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool RouteHasWorkAndOutput(JsonElement route)
    {
        if (route.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (route.TryGetProperty("operators", out var operators) ||
            route.TryGetProperty("Operators", out operators))
        {
            return OperatorsHaveWorkAndOutput(operators);
        }

        return false;
    }

    private static bool OperatorsHaveWorkAndOutput(JsonElement operators)
    {
        if (operators.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = operators
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (values.Count == 0)
        {
            return false;
        }

        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ModbusCommunication",
            "HttpRequest",
            "ScriptOperator"
        };
        if (values.Any(forbidden.Contains))
        {
            return false;
        }

        return values.Any(op => !op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
                                !op.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase)) &&
               values.Any(op => op.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase));
    }

    private static VisionAgentIntentRouterPrompt BuildPrompt(
        VisionAgentIntentRouterRequest request,
        VisionAgentIntentRouterOptions options)
    {
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "You are ClearVision Intent Router for an industrial vision assistant.",
            "Return exactly one JSON object. No prose, markdown, comments, raw prompt, system prompt, reasoning, or chain-of-thought.",
            "Classify the user's latest input into exactly one intent: casual_chat, help, ambiguous_vision_requirement, actionable_vision_plan, modify_existing_flow, build_from_confirmed_plan, direct_build_debug.",
            "Rules: casual/help must not open Plan or Build. ambiguous_vision_requirement must ask concise public clarification questions and canBuild=false. actionable_vision_plan opens Plan only. modify_existing_flow may build directly only when current flow metadata exists. build_from_confirmed_plan may build directly only when a pending confirmed plan exists. direct_build_debug is only valid when developerDirectBuildDebug=true.",
            "Use the model for semantic judgment. Do not rely on keyword matching. Do not include chain-of-thought; publicReason must be short and safe.",
            "assistantReply must be the natural user-facing assistant message only. Do not put classification labels, intent names, canBuild, shouldOpenPlan, or phrases like 'ordinary greeting', 'no planning needed', or 'recognized as' in assistantReply.",
            "publicReason is an internal public diagnostic for debug/trace only and must not be written as the main assistant reply.",
            "Examples: for hi, assistantReply='在的。你可以直接描述检测目标、缺陷类型、测量项或流程修改需求，我会先帮你规划方案。'; for help, assistantReply='我可以帮你规划视觉检测流程、选择算子链、整理待确认资源，并在人工确认后生成可应用到画布的草稿。'; for ambiguous packaging box input, assistantReply='你想检测包装箱的哪一类问题？比如胶带贴歪、条码不可读、Logo 缺失、箱角破损，或外观污渍。'",
            "Safety boundary: never request or expose camera paths, PLC addresses, Station IPs, external URLs, API keys, headers, tokens, baseUrl, rawPrompt, systemPrompt, image bytes/base64, model/template/image/PLC filesystem paths, or deployment/config writes.",
            "Required JSON fields: intent, confidence, shouldOpenPlan, shouldBuildDirectly, canBuild, needsClarification, publicReason, assistantReply, clarificationQuestions, fallbackAllowed."
        ]);
        return new VisionAgentIntentRouterPrompt(systemPrompt, [new ChatMessage("user", BuildContext(request, options.MaxContextChars))]);
    }

    private static VisionAgentIntentRouterPrompt BuildRepairPrompt(
        VisionAgentIntentRouterRequest request,
        string error,
        VisionAgentIntentRouterOptions options)
    {
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "Repair the Intent Router output. Return exactly one valid JSON object and no other text.",
            "The object must use only public fields: intent, confidence, shouldOpenPlan, shouldBuildDirectly, canBuild, needsClarification, publicReason, assistantReply, clarificationQuestions, fallbackAllowed.",
            "assistantReply must be a natural user-facing reply, not a diagnostic explanation. Keep publicReason only as debug/trace diagnostics.",
            "Do not include rawPrompt, systemPrompt, reasoning, chain-of-thought, keys, tokens, baseUrl, paths, IPs, PLC addresses, or base64."
        ]);
        var context = BuildContext(request, options.MaxContextChars);
        return new VisionAgentIntentRouterPrompt(systemPrompt, [new ChatMessage("user", $"Previous output error={SafeText(error)}\n{context}")]);
    }

    private static string BuildContext(VisionAgentIntentRouterRequest request, int maxChars)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intent Router request context:");
        builder.AppendLine($"description={Truncate(SafeText(request.Description), 2_000)}");
        builder.AppendLine($"originalUserPrompt={Truncate(SafeText(request.OriginalUserPrompt), 2_000)}");
        builder.AppendLine($"additionalContext={Truncate(SafeText(request.AdditionalContext), 1_500)}");
        builder.AppendLine($"mode={SafeToken(request.Mode)}");
        builder.AppendLine($"historySummary={Truncate(SafeText(request.HistorySummary), 1_500)}");
        builder.AppendLine($"hasCurrentFlow={!string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot)}");
        builder.AppendLine($"currentResultSummary={SummarizeJsonText(request.CurrentResultSnapshot, 1_000)}");
        builder.AppendLine($"attachmentCount={request.AttachmentSummary.Count}");
        builder.AppendLine($"attachmentKinds={string.Join(",", request.AttachmentSummary.ResourceKinds.Select(SafeToken))}");
        builder.AppendLine($"attachmentPathsRedacted={request.AttachmentSummary.PathsRedacted.ToString().ToLowerInvariant()}");
        AppendSemanticContext(builder, request.SemanticExtraction);
        builder.AppendLine($"templateSelectionMode={SafeToken(request.TemplateSelection?.Mode)}");
        builder.AppendLine($"templateSelectionId={SafeToken(request.TemplateSelection?.TemplateId)}");
        builder.AppendLine($"hasPendingPlan={request.HasPendingPlan.ToString().ToLowerInvariant()}");
        builder.AppendLine($"pendingPlanSummary={Truncate(SafeText(request.PendingPlanSummary), 1_500)}");
        builder.AppendLine($"pendingPlanHash={SafeToken(request.PendingPlanHash)}");
        builder.AppendLine($"requirementMode={SafeToken(request.RequirementMode)}");
        builder.AppendLine($"confirmedPlanAnswerFields={string.Join(",", request.ConfirmedPlanAnswers.Select(answer => SafeToken(answer.Field)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))}");
        builder.AppendLine($"resolvedPlanFields={string.Join(",", request.ResolvedPlanFields.Select(SafeToken).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))}");
        builder.AppendLine($"remainingPlanFields={string.Join(",", request.RemainingPlanFields.Select(SafeToken).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))}");
        builder.AppendLine($"developerDirectBuildDebug={request.DeveloperDirectBuildDebug.ToString().ToLowerInvariant()}");
        return Truncate(builder.ToString(), maxChars);
    }

    private static void AppendSemanticContext(
        StringBuilder builder,
        VisionAgentSemanticExtractionResult? semantic)
    {
        if (semantic == null)
        {
            builder.AppendLine("semanticExtraction=unavailable");
            return;
        }

        builder.AppendLine("semanticExtraction:");
        builder.AppendLine($"- source={SafeToken(semantic.Source)}");
        builder.AppendLine($"- intent={SafeToken(semantic.Intent)}");
        builder.AppendLine($"- taskType={SafeToken(semantic.TaskType)}");
        builder.AppendLine($"- inspectionObject={Truncate(SafeText(semantic.InspectionObject), 200)}");
        builder.AppendLine($"- targetAttribute={Truncate(SafeText(semantic.TargetAttribute), 200)}");
        builder.AppendLine($"- okCondition={Truncate(SafeText(semantic.OkCondition), 240)}");
        builder.AppendLine($"- ngCondition={Truncate(SafeText(semantic.NgCondition), 240)}");
        builder.AppendLine($"- imageSource={Truncate(SafeText(semantic.ImageSource), 160)}");
        builder.AppendLine($"- suggestedRoute={Truncate(SafeText(semantic.SuggestedRoute), 240)}");
        builder.AppendLine($"- missingFields={string.Join(",", semantic.MissingFields.Select(SafeToken))}");
        builder.AppendLine($"- failureCode={SafeToken(semantic.FailureCode)}");
        builder.AppendLine("- safety=semantic extraction is read-only and cannot authorize Build.");
    }

    private static string ExtractJsonObject(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                text = text[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("Intent Router did not return a JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static bool IsRepairable(Exception error)
    {
        return error is JsonException or InvalidOperationException;
    }

    private static bool IsUnauthorized(Exception error)
    {
        return error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } ||
               error.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
               error.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIntent(string? intent)
    {
        return Clean(intent).ToLowerInvariant() switch
        {
            IntentCasualChat => IntentCasualChat,
            IntentHelp => IntentHelp,
            IntentAmbiguousVisionRequirement => IntentAmbiguousVisionRequirement,
            IntentActionableVisionPlan => IntentActionableVisionPlan,
            IntentModifyExistingFlow => IntentModifyExistingFlow,
            IntentBuildFromConfirmedPlan => IntentBuildFromConfirmedPlan,
            IntentDirectBuildDebug => IntentDirectBuildDebug,
            _ => IntentAmbiguousVisionRequirement
        };
    }

    private static string NormalizeConfidence(string? confidence)
    {
        return Clean(confidence).ToLowerInvariant() switch
        {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "low"
        };
    }

    private static List<string> SanitizeQuestions(IEnumerable<string>? questions)
    {
        return (questions ?? [])
            .Select(SafeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static List<string> DefaultClarificationQuestions()
    {
        return
        [
            "请补充检测目标或产品对象。",
            "请说明要判断的缺陷、测量项或识别内容。",
            "请说明输入来源是相机、文件还是仅先做元数据草稿。",
            "请说明 OK/NG 判定规则。"
        ];
    }

    private static List<string> MaturityClarificationQuestions(AiRequirementMaturityResult maturity)
    {
        var fields = maturity.MissingFields.Count > 0
            ? maturity.MissingFields
            : ["inspection_object", "task_type", "image_source", "acceptance_criteria"];
        return fields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(field => field switch
            {
                "inspection_object" => "请补充检测目标或产品对象。",
                "task_type" => "请说明要判断的缺陷、测量项或识别内容。",
                "image_source" => "请说明输入来源是相机、文件还是仅先做元数据草稿。",
                "acceptance_criteria" => "请说明 OK/NG 判定规则。",
                "model_or_rule_strategy" => "请说明倾向使用规则、传统算法、模板还是模型策略。",
                "output_target" => "请说明输出目标或验收结果字段。",
                _ => $"请补充 {field}。"
            })
            .Take(5)
            .ToList();
    }

    private static string DefaultReason(string intent)
    {
        return intent switch
        {
            IntentCasualChat => "这是普通寒暄，不需要进入规划。",
            IntentHelp => "这是能力咨询，不需要进入规划。",
            IntentActionableVisionPlan => "输入包含可规划的视觉检测、测量或识别目标。",
            IntentModifyExistingFlow => "输入是在已有流程上做修改。",
            IntentBuildFromConfirmedPlan => "用户已确认当前规划，可以进入构建。",
            IntentDirectBuildDebug => "开发者已显式启用直接 Build 调试。",
            _ => "需求信息不足，暂不可构建。"
        };
    }

    private static string UnauthorizedReason(string intent)
    {
        return intent switch
        {
            IntentCasualChat or IntentHelp => "模型路由鉴权失败，已使用安全规则回复。",
            IntentActionableVisionPlan => "模型路由鉴权失败，明显视觉需求将进入规则兜底规划。",
            _ => "模型路由鉴权失败，需求信息不足，暂不可构建。"
        };
    }

    private static string RuleFallbackReason(
        string reason,
        AiRequirementMaturityResult maturity,
        string intent)
    {
        var baseReason = string.IsNullOrWhiteSpace(maturity.PublicReason)
            ? DefaultReason(intent)
            : maturity.PublicReason;
        return reason.StartsWith("router_", StringComparison.OrdinalIgnoreCase)
            ? $"模型路由不可用，当前为规则降级解析。{baseReason}"
            : baseReason;
    }

    private static string ResolveAssistantReply(
        string? assistantReply,
        string? publicReason,
        string intent,
        VisionAgentIntentRouterRequest request)
    {
        var reply = SafeText(assistantReply);
        if (string.IsNullOrWhiteSpace(reply) || LooksLikeDiagnosticAssistantReply(reply, publicReason))
        {
            return DefaultReply(intent, request);
        }

        return reply;
    }

    private static bool LooksLikeDiagnosticAssistantReply(string reply, string? publicReason)
    {
        var text = Clean(reply);
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(publicReason) &&
            string.Equals(text, Clean(publicReason), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsAny(text,
        [
            "普通寒暄",
            "能力咨询",
            "不需要进入规划",
            "无需规划",
            "已识别为",
            "识别为",
            "意图",
            "intent",
            "canBuild",
            "shouldOpenPlan",
            "shouldBuildDirectly",
            "actionable_vision_plan",
            "casual_chat",
            "ambiguous_vision_requirement",
            "需求信息不足",
            "需求不足",
            "暂不可构建",
            "将先进入 Plan",
            "进入 Plan 规划"
        ]);
    }

    private static string DefaultReply(string intent, VisionAgentIntentRouterRequest? request = null)
    {
        return intent switch
        {
            IntentCasualChat => "在的。你可以直接描述检测目标、缺陷类型、测量项或流程修改需求，我会先帮你规划方案。",
            IntentHelp => "我可以帮你规划视觉检测流程、选择算子链、整理待确认资源，并在人工确认后生成可应用到画布的草稿。",
            IntentActionableVisionPlan => "我先帮你整理规划方案。",
            IntentModifyExistingFlow => "我会按当前流程上下文整理修改方案，并进入构建审计。",
            IntentBuildFromConfirmedPlan => "好的，我会按已确认的规划开始构建。",
            IntentDirectBuildDebug => "已进入直接 Build 调试入口，本轮会跳过规划，仅用于开发调试。",
            _ => DefaultAmbiguousReply(request)
        };
    }

    private static string DefaultAmbiguousReply(VisionAgentIntentRouterRequest? request)
    {
        var text = Clean(request?.Description);
        if (ContainsAny(text, ["包装箱", "纸箱", "箱"]))
        {
            return "你想检测包装箱的哪一类问题？比如胶带贴歪、条码不可读、Logo 缺失、箱角破损，或外观污渍。";
        }

        return "你想检测哪一类问题？请补充检测目标、缺陷类型、输入来源，以及 OK/NG 判定规则。";
    }

    private static bool LooksLikeCasual(string text)
    {
        var normalized = NormalizeChatText(text);
        return normalized is "hi" or "hello" or "hey" or "你好" or "您好" or "在吗" or "在不在" or "你在吗" or "早" or "早上好" or "下午好" or "晚上好" or "谢谢" or "thanks" or "thankyou";
    }

    private static bool LooksLikeHelp(string text)
    {
        var normalized = NormalizeChatText(text);
        return normalized.Contains("你能做什么", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("能做什么", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("你可以做什么", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("你会什么", StringComparison.OrdinalIgnoreCase) ||
               normalized is "help" or "帮助";
    }

    private static bool LooksLikeConfirmedPlanBuild(string text)
    {
        return ContainsAny(text, ["开始构建", "按推荐方案", "确认计划", "确认规划", "就按这个", "开始 build", "build now"]);
    }

    private static bool LooksLikeExplicitNewPlanRequest(string text)
    {
        return ContainsAny(text,
        [
            "restart",
            "reset plan",
            "new plan",
            "new task",
            "start over",
            "discard current plan",
            "abandon current plan",
            "重新规划",
            "重新开始",
            "新任务",
            "新需求",
            "放弃当前计划",
            "清空当前计划"
        ]);
    }

    private static bool LooksLikeModifyExistingFlow(string text)
    {
        return ContainsAny(text, ["当前流程", "已有流程", "现有流程", "这个流程", "算子", "参数", "阈值", "连线", "改成", "修改", "调整", "删除", "新增"]);
    }

    private static bool LooksLikeActionableVisionNeed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ContainsAny(text, ["构建", "生成", "创建", "新建", "搭建", "设计", "做一个"]) &&
            ContainsAny(text, ["检测", "测量", "识别", "流程", "视觉", "外观", "缺陷", "条码", "二维码", "OCR", "尺寸", "引导", "定位", "机械臂", "机器人"]))
        {
            return true;
        }

        var detailSignals = new[]
        {
            "贴歪", "条码", "可读", "Logo", "缺失", "箱角", "破损", "划痕", "裂纹", "孔距", "线序", "缺陷", "OK", "NG", "检测", "测量", "识别",
            "视觉引导", "机械臂", "机器人", "打螺钉", "拧螺丝", "锁螺丝", "焊缝", "涂胶", "胶路", "轨迹定位", "螺钉", "螺丝"
        };
        return detailSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    private static string NormalizeChatText(string text)
    {
        var chars = Clean(text)
            .Where(ch => !char.IsWhiteSpace(ch) &&
                         ch != '。' &&
                         ch != '！' &&
                         ch != '!' &&
                         ch != '？' &&
                         ch != '?' &&
                         ch != ',' &&
                         ch != '，' &&
                         ch != '.')
            .ToArray();
        return new string(chars).Trim();
    }

    private static string SummarizeJsonText(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = SafeText(text);
        return Truncate(value, maxChars);
    }

    private static string SafeText(string? value)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return UnsafeRegex.Replace(text, "<redacted>");
    }

    private static string SafeToken(string? value)
    {
        var text = SafeText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"[^A-Za-z0-9_\-.:]", "_");
    }

    private static string Truncate(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                                 text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

public sealed record VisionAgentIntentRouterPrompt(
    string SystemPrompt,
    List<ChatMessage> Messages);
