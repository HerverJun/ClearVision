using System.Text.RegularExpressions;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public static class VisionAgentPlanReadinessEvaluator
{
    private static readonly HashSet<string> ForbiddenOperatorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator"
    };

    public static VisionAgentBuildReadinessSnapshot Evaluate(
        VisionAgentPlanModeResult? plan,
        IReadOnlyDictionary<string, string>? buildDecisions = null,
        IReadOnlyList<string>? acceptedDefaults = null,
        bool acceptedRecommendedDefaults = false,
        VisionAgentPlanAnswerValidationResult? validatedAnswers = null,
        VisionAgentEffectiveRequirement? effectiveRequirement = null,
        string requirementMode = AiRequirementModes.Strict,
        IReadOnlyList<VisionAgentResourceDecision>? resourceDecisions = null)
    {
        _ = acceptedDefaults;
        if (plan == null)
        {
            return Snapshot(
                false,
                [
                    Blocker(
                        "hard_requirement:plan_snapshot_missing",
                        VisionAgentBuildBlockerCategories.HardRequirement,
                        string.Empty,
                        string.Empty,
                        true,
                        VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                        "缺少规划快照，无法开始构建。")
                ],
                [],
                [],
                "缺少规划快照，无法开始构建。");
        }

        var planSnapshot = plan;

        if (effectiveRequirement == null)
        {
            var initialAnswers = validatedAnswers?.AcceptedAnswers ?? planSnapshot.ConfirmedPlanAnswers ?? [];
            var validated = validatedAnswers ?? new VisionAgentPlanAnswerValidator().Validate(planSnapshot, initialAnswers, null, false);
            validatedAnswers = validated;
            var maturityRequest = new VisionAgentRequirementMaturityRequest
            {
                Description = planSnapshot.OriginalUserPrompt ?? planSnapshot.Goal ?? string.Empty,
                RequirementMode = requirementMode,
                HasCurrentFlow = false,
                TemplateSelection = planSnapshot.TemplateSelection
            };
            var overlay = new VisionAgentPlanRequirementOverlay();
            effectiveRequirement = overlay.Build(planSnapshot, validated, maturityRequest);
        }

        var effectiveForReadiness = effectiveRequirement;
        var maturity = effectiveForReadiness?.Maturity ?? planSnapshot.RequirementMaturity;
        var resolvedFields = BuildResolvedFields(planSnapshot, validatedAnswers, effectiveForReadiness);
        if (requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) &&
            !PlanHasObjectOrTaskFact(planSnapshot))
        {
            resolvedFields.RemoveAll(field => field.Equals(VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase));
        }

        var remainingFields = BuildRemainingFields(planSnapshot, maturity, resolvedFields, effectiveForReadiness);
        var answers = validatedAnswers?.AcceptedAnswers ?? [];
        var blockers = new List<VisionAgentBuildBlocker>();
        var questionIndex = BuildQuestionIndex(planSnapshot);
        var hasSupportedRoute = HasSupportedRouteOrTemplate(planSnapshot, out var invalidOperators);
        var strictMode = !string.Equals(requirementMode, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase);

        AddValidationBlockers(validatedAnswers, blockers);

        foreach (var reason in plan.BlockingReasons)
        {
            AddIfUnresolved(
                blockers,
                UpgradeLegacyBlocker(plan, reason, questionIndex, requirementMode, maturity, hasSupportedRoute, validatedAnswers),
                answers,
                resolvedFields,
                acceptedRecommendedDefaults,
                questionIndex,
                buildDecisions);
        }

        if (maturity is { CanPlan: false } ||
            maturity?.Maturity is AiRequirementMaturity.AbstractGoal or AiRequirementMaturity.ChatOrHelp)
        {
            var reasons = maturity.BlockingReasons.Count > 0
                ? maturity.BlockingReasons
                : ["requirement_not_plannable"];
            foreach (var reason in reasons)
            {
                AddIfUnresolved(
                    blockers,
                    UpgradeLegacyBlocker(plan, $"hard_requirement:{reason}", questionIndex, requirementMode, maturity, hasSupportedRoute, validatedAnswers),
                    answers,
                    resolvedFields,
                    acceptedRecommendedDefaults,
                    questionIndex,
                    buildDecisions);
            }
        }

        foreach (var field in remainingFields)
        {
            if (ShouldBlockField(plan, field, requirementMode, maturity, explicitOutputTargetBlocker: false))
            {
                AddIfUnresolved(
                    blockers,
                    FieldBlocker(field),
                    answers,
                    resolvedFields,
                    acceptedRecommendedDefaults,
                    questionIndex,
                    buildDecisions);
            }
        }

        AddResourceBlockersForAcceptedAnswers(
            planSnapshot,
            blockers,
            answers,
            requirementMode,
            maturity,
            hasSupportedRoute,
            resourceDecisions);

        if (!hasSupportedRoute)
        {
            AddIfUnresolved(
                blockers,
                Blocker(
                    "strategy_confirmation:model_or_rule_strategy_missing",
                    VisionAgentBuildBlockerCategories.StrategyConfirmation,
                    VisionAgentPlanAnswerFields.AlgorithmStrategy,
                    FindQuestionIdForField(questionIndex, VisionAgentPlanAnswerFields.AlgorithmStrategy),
                    RequiresStrategyConfirmation(requirementMode, maturity, hasSupportedRoute),
                    VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                    "请选择构建策略。"),
                answers,
                resolvedFields,
                acceptedRecommendedDefaults,
                questionIndex,
                buildDecisions);
        }

        foreach (var op in invalidOperators)
        {
            AddOrReplace(blockers, Blocker(
                $"hard_requirement:invalid_operator:{SafeKey(op)}",
                VisionAgentBuildBlockerCategories.HardRequirement,
                string.Empty,
                string.Empty,
                true,
                VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                "规划包含当前不支持的算子，暂不能构建。"));
        }

        var effectiveRemainingFields = remainingFields
            .Where(field => !IsFieldSatisfiedByBoundResource(planSnapshot, field, resourceDecisions))
            .ToList();
        var distinctBlockers = blockers
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Where(item => item.Resource == null || !IsResourceBound(item.Resource, resourceDecisions))
            .Where(item => !IsFieldSatisfiedByBoundResource(planSnapshot, item.Field, resourceDecisions))
            .GroupBy(item => item.Resource?.CanonicalId is { Length: > 0 } canonicalId
                    ? $"resource:{canonicalId}"
                    : item.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.BlocksBuild).First())
            .Take(16)
            .ToList();
        var hasBlockingRemaining = effectiveRemainingFields.Any(field => ShouldBlockField(plan, field, requirementMode, maturity, explicitOutputTargetBlocker: false));
        var canBuild = !hasBlockingRemaining && distinctBlockers.All(blocker => !blocker.BlocksBuild);
        return Snapshot(
            canBuild,
            distinctBlockers,
            resolvedFields,
            effectiveRemainingFields,
            PrimaryMessage(canBuild, distinctBlockers, maturity),
            VisionAgentPlanContractVersions.V2);
    }

    private static List<string> BuildResolvedFields(
        VisionAgentPlanModeResult plan,
        VisionAgentPlanAnswerValidationResult? validatedAnswers,
        VisionAgentEffectiveRequirement? effectiveRequirement)
    {
        var fields = new List<string>();
        if (plan.ConfirmedPlanAnswers != null)
        {
            fields.AddRange(plan.ConfirmedPlanAnswers
                .Where(a => VisionAgentPlanFieldPolicy.IsAuthoritativeConfirmationOrigin(a.Origin) &&
                            !string.IsNullOrWhiteSpace(a.Value) &&
                            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(a.Value))
                .Select(a => a.Field));
        }
        fields.AddRange(plan.ResolvedPlanFields ?? []);

        if (validatedAnswers != null)
        {
            fields.AddRange(validatedAnswers.ResolvedFields);
        }

        return fields
            .Select(VisionAgentPlanFieldPolicy.NormalizeField)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool PlanHasTaskRoute(VisionAgentPlanModeResult plan)
    {
        var task = FirstNonEmpty(plan.Intent, plan.RequirementMaturity?.TaskType, plan.SemanticExtraction?.TaskType);
        if (!string.IsNullOrWhiteSpace(task) &&
            !task.Equals(AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
            !task.Equals(AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase) &&
            !task.Equals(AiRequirementMaturity.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (plan.TemplateSelection != null &&
            (!string.IsNullOrWhiteSpace(plan.TemplateSelection.TemplateId) ||
             !string.IsNullOrWhiteSpace(plan.TemplateSelection.ScenarioKey)))
        {
            return true;
        }

        return (plan.RecommendedRoute?.Operators ?? [])
            .Select(Clean)
            .Any(op => !string.IsNullOrWhiteSpace(op) &&
                       !op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
                       !op.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase) &&
                       !op.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldUseEffectiveRequirement(
        VisionAgentPlanModeResult plan,
        VisionAgentEffectiveRequirement? effectiveRequirement,
        string requirementMode)
    {
        if (effectiveRequirement == null)
        {
            return false;
        }

        if (requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) &&
            !PlanHasObjectOrTaskFact(plan))
        {
            return true;
        }

        var planMaturity = plan.RequirementMaturity;
        if (planMaturity?.CanPlan == true &&
            effectiveRequirement.Maturity.CanPlan == false)
        {
            return false;
        }

        return true;
    }

    private static bool PlanHasObjectOrTaskFact(VisionAgentPlanModeResult plan)
    {
        var semantic = plan.SemanticExtraction;
        var task = FirstNonEmpty(semantic?.TaskType, plan.RequirementMaturity?.TaskType);
        return !string.IsNullOrWhiteSpace(semantic?.InspectionObject) ||
               (!string.IsNullOrWhiteSpace(task) &&
                !task.Equals(AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
                !task.Equals(AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase)) ||
               (semantic?.ObjectSignals?.Any(signal => !string.IsNullOrWhiteSpace(signal)) == true) ||
               (semantic?.TaskSignals?.Any(signal => !string.IsNullOrWhiteSpace(signal)) == true) ||
               (plan.RequirementMaturity?.ObjectSignals.Any(signal => !string.IsNullOrWhiteSpace(signal)) == true) ||
               (plan.RequirementMaturity?.TaskSignals.Any(signal => !string.IsNullOrWhiteSpace(signal)) == true);
    }

    private static List<string> BuildRemainingFields(
        VisionAgentPlanModeResult plan,
        AiRequirementMaturityResult? maturity,
        IReadOnlyList<string> resolvedFields,
        VisionAgentEffectiveRequirement? effectiveRequirement)
    {
        var fields = (plan.RemainingPlanFields ?? [])
            .Concat(effectiveRequirement?.RemainingFields ?? maturity?.MissingFields ?? []);
        return fields
            .Select(VisionAgentPlanFieldPolicy.NormalizeField)
            .Where(field => !string.IsNullOrWhiteSpace(field) &&
                            !resolvedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddValidationBlockers(
        VisionAgentPlanAnswerValidationResult? validatedAnswers,
        List<VisionAgentBuildBlocker> blockers)
    {
        if (validatedAnswers == null)
        {
            return;
        }

        if (validatedAnswers.InvalidQuestionIds.Count > 0)
        {
            AddOrReplace(blockers, Blocker(
                "hard_requirement:invalid_plan_answer_question",
                VisionAgentBuildBlockerCategories.HardRequirement,
                string.Empty,
                string.Empty,
                true,
                VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                "存在无法识别的问题回答，请重新确认关键问题。"));
        }

        if (validatedAnswers.InvalidValues.Count > 0)
        {
            AddOrReplace(blockers, Blocker(
                "hard_requirement:invalid_plan_answer_value",
                VisionAgentBuildBlockerCategories.HardRequirement,
                string.Empty,
                string.Empty,
                true,
                VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                "存在无效的回答值，请重新确认关键问题。"));
        }

        foreach (var field in validatedAnswers.ConflictedFields)
        {
            AddOrReplace(blockers, Blocker(
                $"hard_requirement:conflicted_plan_answer:{SafeKey(field)}",
                VisionAgentBuildBlockerCategories.HardRequirement,
                VisionAgentPlanFieldPolicy.NormalizeField(field),
                string.Empty,
                true,
                VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                $"字段“{FieldLabel(field)}”存在冲突回答，请保留一个选择。"));
        }
    }

    private static VisionAgentBuildBlocker UpgradeLegacyBlocker(
        VisionAgentPlanModeResult plan,
        string reason,
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questionIndex,
        string requirementMode,
        AiRequirementMaturityResult? maturity,
        bool hasSupportedRoute,
        VisionAgentPlanAnswerValidationResult? validatedAnswers)
    {
        var parsed = ParseLegacyReason(reason);
        if (string.IsNullOrWhiteSpace(parsed.Key))
        {
            return Blocker(
                "contract_warning:empty_blocker",
                VisionAgentBuildBlockerCategories.ContractWarning,
                string.Empty,
                string.Empty,
                false,
                VisionAgentBuildBlockerResolutionModes.NonBlocking,
                "规划返回了空阻断项，已作为诊断保留。");
        }

        var question = FindQuestion(questionIndex, parsed.Key);
        var questionId = question?.Id ?? string.Empty;
        var field = question == null
            ? VisionAgentPlanFieldPolicy.ResolveQuestionField(new VisionAgentClarificationQuestion
            {
                Id = parsed.Key,
                Field = parsed.Key
            })
            : VisionAgentPlanFieldPolicy.ResolveQuestionField(question, [reason]);
        var idKey = SafeKey(parsed.Key);

        if (idKey.Equals("abstract_goal_needs_decomposition", StringComparison.OrdinalIgnoreCase) ||
            idKey.Equals("requirement_not_plannable", StringComparison.OrdinalIgnoreCase))
        {
            return Blocker(
                idKey,
                VisionAgentBuildBlockerCategories.HardRequirement,
                string.Empty,
                questionId,
                true,
                VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                "方案愿景，不是可直接构建的检测流程。");
        }

        if (parsed.Kind.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedResourceKey = VisionAgentResourceIdentity.NormalizeToken(parsed.Key);
            if ((normalizedResourceKey.Contains("localoutput", StringComparison.Ordinal) ||
                 VisionAgentPlanFieldPolicy.NormalizeField(field).Equals(VisionAgentPlanAnswerFields.OutputTarget, StringComparison.OrdinalIgnoreCase)) &&
                AllowsDefaultLocalOutput(plan, validatedAnswers))
            {
                return Blocker(
                    $"contract_warning:{idKey}",
                    VisionAgentBuildBlockerCategories.ContractWarning,
                    field,
                    questionId,
                    false,
                    VisionAgentBuildBlockerResolutionModes.NonBlocking,
                    "默认使用本地结构化结果输出，不需要额外输出资源。");
            }

            var resource = BuildLegacyResourceRequirement(plan, parsed.Key, field, questionId);
            var blocksResource = ResourceBlocksBuild(resource, requirementMode, maturity, hasSupportedRoute);
            resource = resource with
            {
                BlockingScope = blocksResource
                    ? VisionAgentResourceBlockingScopes.Build
                    : VisionAgentResourceBlockingScopes.DeployRun
            };
            return Blocker(
                ResourceBlockerId(resource),
                VisionAgentBuildBlockerCategories.ResourcePending,
                field,
                questionId,
                blocksResource,
                VisionAgentBuildBlockerResolutionModes.ProvideResource,
                ResourcePublicLabel(resource, blocksResource),
                resource);
        }

        if (parsed.Kind.Equals(VisionAgentBuildBlockerCategories.SafetyBlocker, StringComparison.OrdinalIgnoreCase))
        {
            return Blocker(
                $"safety_blocker:{idKey}",
                VisionAgentBuildBlockerCategories.SafetyBlocker,
                field,
                questionId,
                true,
                VisionAgentBuildBlockerResolutionModes.NonBlocking,
                "存在安全阻断，需要重新生成安全快照或明确安全决策。");
        }

        if (parsed.Key.Equals("planner_candidate_not_buildable", StringComparison.OrdinalIgnoreCase) &&
            question == null &&
            string.IsNullOrWhiteSpace(field))
        {
            return Blocker(
                "contract_warning:planner_candidate_not_buildable",
                VisionAgentBuildBlockerCategories.ContractWarning,
                string.Empty,
                string.Empty,
                false,
                VisionAgentBuildBlockerResolutionModes.NonBlocking,
                "Planner 未给出可构建候选，已降级为诊断项。");
        }

        if (field.Equals(VisionAgentPlanAnswerFields.OutputTarget, StringComparison.OrdinalIgnoreCase))
        {
            var blocksOutput = !AllowsDefaultLocalOutput(plan, validatedAnswers);
            return Blocker(
                blocksOutput ? $"hard_requirement:{idKey}_missing" : $"contract_warning:{idKey}",
                blocksOutput
                    ? VisionAgentBuildBlockerCategories.HardRequirement
                    : VisionAgentBuildBlockerCategories.ContractWarning,
                VisionAgentPlanAnswerFields.OutputTarget,
                questionId,
                blocksOutput,
                blocksOutput
                    ? VisionAgentBuildBlockerResolutionModes.AnswerQuestion
                    : VisionAgentBuildBlockerResolutionModes.NonBlocking,
                blocksOutput ? "请选择输出目标。" : "默认使用本地结构化结果输出。");
        }

        if (parsed.Kind.Equals(VisionAgentBuildBlockerCategories.StrategyConfirmation, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(field) &&
                !field.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase))
            {
                return Blocker(
                    $"hard_requirement:{idKey}_missing",
                    VisionAgentBuildBlockerCategories.HardRequirement,
                    field,
                    questionId,
                    ShouldBlockField(plan, field, requirementMode, maturity, explicitOutputTargetBlocker: true),
                    VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                    $"还缺：{FieldLabel(field)}");
            }

            if (question != null || field.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase))
            {
                return Blocker(
                    $"strategy_confirmation:{idKey}_missing",
                    VisionAgentBuildBlockerCategories.StrategyConfirmation,
                    string.IsNullOrWhiteSpace(field) ? VisionAgentPlanAnswerFields.AlgorithmStrategy : field,
                    questionId,
                    RequiresStrategyConfirmation(requirementMode, maturity, hasSupportedRoute),
                    VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                    "请选择构建策略。");
            }

            return Blocker(
                $"contract_warning:{idKey}",
                VisionAgentBuildBlockerCategories.ContractWarning,
                string.Empty,
                string.Empty,
                false,
                VisionAgentBuildBlockerResolutionModes.NonBlocking,
                "规划返回了无法映射到问题的策略确认项，已作为诊断保留。");
        }

        if ((parsed.Kind.Equals(VisionAgentBuildBlockerCategories.HardRequirement, StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(parsed.Kind)) &&
            field.Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase) &&
            !ShouldBlockField(plan, field, requirementMode, maturity, explicitOutputTargetBlocker: true))
        {
            var resource = BuildCameraRequirement(plan) with
            {
                Source = "field_blocker",
                BlockingScope = VisionAgentResourceBlockingScopes.DeployRun
            };
            return Blocker(
                ResourceBlockerId(resource),
                VisionAgentBuildBlockerCategories.ResourcePending,
                field,
                questionId,
                false,
                VisionAgentBuildBlockerResolutionModes.ProvideResource,
                ResourcePublicLabel(resource, false),
                resource);
        }

        if (parsed.Kind.Equals(VisionAgentBuildBlockerCategories.HardRequirement, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.Kind))
        {
            if (!string.IsNullOrWhiteSpace(field))
            {
                return Blocker(
                    $"hard_requirement:{idKey}_missing",
                    VisionAgentBuildBlockerCategories.HardRequirement,
                    field,
                    questionId,
                    ShouldBlockField(plan, field, requirementMode, maturity, explicitOutputTargetBlocker: true),
                    VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                    $"还缺：{FieldLabel(field)}");
            }

            if (question != null)
            {
                return Blocker(
                    $"hard_requirement:{idKey}_missing",
                    VisionAgentBuildBlockerCategories.HardRequirement,
                    string.Empty,
                    questionId,
                    true,
                    VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
                    $"请确认“{question.Title}”。");
            }
        }

        return Blocker(
            $"contract_warning:{idKey}",
            VisionAgentBuildBlockerCategories.ContractWarning,
            string.Empty,
            questionId,
            false,
            VisionAgentBuildBlockerResolutionModes.NonBlocking,
            "规划返回了无法映射的阻断项，已作为诊断保留。");
    }

    private static VisionAgentBuildBlocker FieldBlocker(string field)
    {
        var normalized = VisionAgentPlanFieldPolicy.NormalizeField(field);
        var id = string.IsNullOrWhiteSpace(normalized) ? SafeKey(field) : normalized;
        return Blocker(
            $"hard_requirement:{id}_missing",
            VisionAgentBuildBlockerCategories.HardRequirement,
            normalized,
            string.Empty,
            true,
            VisionAgentBuildBlockerResolutionModes.AnswerQuestion,
            $"还缺：{FieldLabel(normalized)}");
    }

    private static void AddIfUnresolved(
        List<VisionAgentBuildBlocker> blockers,
        VisionAgentBuildBlocker blocker,
        IReadOnlyList<VisionAgentPlanAnswer> answers,
        IReadOnlyList<string> resolvedFields,
        bool acceptedRecommendedDefaults,
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questionIndex,
        IReadOnlyDictionary<string, string>? buildDecisions)
    {
        if (string.IsNullOrWhiteSpace(blocker.Id))
        {
            return;
        }

        if (blocker.BlocksBuild &&
            IsBlockerResolved(blocker, answers, resolvedFields, acceptedRecommendedDefaults, questionIndex, buildDecisions))
        {
            return;
        }

        AddOrReplace(blockers, blocker);
    }

    private static bool IsBlockerResolved(
        VisionAgentBuildBlocker blocker,
        IReadOnlyList<VisionAgentPlanAnswer> answers,
        IReadOnlyList<string> resolvedFields,
        bool acceptedRecommendedDefaults,
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questionIndex,
        IReadOnlyDictionary<string, string>? buildDecisions)
    {
        if (blocker.Category.Equals(VisionAgentBuildBlockerCategories.SafetyBlocker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (blocker.Category.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(blocker.Field) &&
            resolvedFields.Contains(blocker.Field, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (blocker.Field.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase) &&
            buildDecisions?.ContainsKey(VisionAgentPlanAnswerFields.AlgorithmStrategy) == true)
        {
            return true;
        }

        foreach (var answer in answers.Where(answer => !string.IsNullOrWhiteSpace(answer.Value) &&
                                                       !VisionAgentPlanFieldPolicy.IsPlaceholderValue(answer.Value)))
        {
            if (!string.IsNullOrWhiteSpace(blocker.QuestionId) &&
                answer.QuestionId.Equals(blocker.QuestionId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(blocker.Field) &&
                answer.Field.Equals(blocker.Field, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (acceptedRecommendedDefaults &&
            !string.IsNullOrWhiteSpace(blocker.QuestionId) &&
            questionIndex.TryGetValue(blocker.QuestionId, out var question) &&
            !string.IsNullOrWhiteSpace(RecommendedValue(question)) &&
            !VisionAgentPlanFieldPolicy.IsPlaceholderValue(RecommendedValue(question)))
        {
            return true;
        }

        return false;
    }

    private static void AddOrReplace(List<VisionAgentBuildBlocker> blockers, VisionAgentBuildBlocker blocker)
    {
        var existingIndex = blockers.FindIndex(item => item.Id.Equals(blocker.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            blockers.Add(blocker);
            return;
        }

        if (blocker.BlocksBuild && !blockers[existingIndex].BlocksBuild)
        {
            blockers[existingIndex] = blocker;
        }
    }

    private static bool ShouldBlockField(
        VisionAgentPlanModeResult plan,
        string field,
        string requirementMode,
        AiRequirementMaturityResult? maturity,
        bool explicitOutputTargetBlocker)
    {
        var normalized = VisionAgentPlanFieldPolicy.NormalizeField(field);
        if (normalized.Equals(VisionAgentPlanAnswerFields.OutputTarget, StringComparison.OrdinalIgnoreCase))
        {
            return explicitOutputTargetBlocker && !AllowsDefaultLocalOutput(plan);
        }

        if (requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase) &&
                CanDraftWithPendingImageSource(plan))
            {
                return false;
            }

            return VisionAgentPlanFieldPolicy.IsDraftBlocking(normalized, maturity?.TaskType, maturity);
        }

        return VisionAgentPlanFieldPolicy.IsStrictBlocking(normalized, maturity?.TaskType, maturity);
    }

    private static void AddResourceBlockersForAcceptedAnswers(
        VisionAgentPlanModeResult plan,
        List<VisionAgentBuildBlocker> blockers,
        IReadOnlyList<VisionAgentPlanAnswer> answers,
        string requirementMode,
        AiRequirementMaturityResult? maturity,
        bool hasSupportedRoute,
        IReadOnlyList<VisionAgentResourceDecision>? resourceDecisions)
    {
        var requirements = BuildRouteResourceRequirements(plan, answers);
        foreach (var resource in requirements)
        {
            if (IsResourceBound(resource, resourceDecisions))
            {
                continue;
            }

            var blocksResource = ResourceBlocksBuild(resource, requirementMode, maturity, hasSupportedRoute);
            AddOrReplace(blockers, Blocker(
                ResourceBlockerId(resource),
                VisionAgentBuildBlockerCategories.ResourcePending,
                ResourceField(resource),
                string.Empty,
                blocksResource,
                VisionAgentBuildBlockerResolutionModes.ProvideResource,
                ResourcePublicLabel(resource, blocksResource),
                resource with
                {
                    BlockingScope = blocksResource
                        ? VisionAgentResourceBlockingScopes.Build
                        : VisionAgentResourceBlockingScopes.DeployRun
                }));
        }

        foreach (var answer in answers)
        {
            var field = VisionAgentPlanFieldPolicy.NormalizeField(answer.Field);
            var value = Clean(answer.Value).ToLowerInvariant();
            if (!field.Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase) ||
                value is not ("station_camera" or "line_camera") ||
                answer.Origin.Equals(VisionAgentPlanAnswerOrigins.ResourceBound, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resource = BuildCameraRequirement(plan);
            if (IsResourceBound(resource, resourceDecisions)) continue;
            var blocksResource = ResourceBlocksBuild(resource, requirementMode, maturity, hasSupportedRoute);
            AddOrReplace(blockers, Blocker(
                ResourceBlockerId(resource),
                VisionAgentBuildBlockerCategories.ResourcePending,
                VisionAgentPlanAnswerFields.ImageSource,
                Clean(answer.QuestionId),
                blocksResource,
                VisionAgentBuildBlockerResolutionModes.ProvideResource,
                ResourcePublicLabel(resource, blocksResource),
                resource with
                {
                    BlockingScope = blocksResource
                        ? VisionAgentResourceBlockingScopes.Build
                        : VisionAgentResourceBlockingScopes.DeployRun
                }));
        }
    }

    private static List<VisionAgentResourceRequirement> BuildRouteResourceRequirements(
        VisionAgentPlanModeResult plan,
        IReadOnlyList<VisionAgentPlanAnswer> answers)
    {
        var requirements = new List<VisionAgentResourceRequirement>();
        var operators = plan.RecommendedRoute?.Operators ?? [];
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var imageSource = answers
            .FirstOrDefault(answer => VisionAgentPlanFieldPolicy.NormalizeField(answer.Field)
                .Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

        foreach (var rawOperator in operators)
        {
            var operatorType = Clean(rawOperator);
            var normalizedType = VisionAgentResourceIdentity.NormalizeToken(operatorType);
            ordinals.TryGetValue(normalizedType, out var ordinal);
            ordinals[normalizedType] = ordinal + 1;
            var operatorKey = VisionAgentResourceIdentity.OperatorKey(operatorType, ordinal);

            if (operatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
                imageSource is not null &&
                (imageSource.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
                 imageSource.Contains("station", StringComparison.OrdinalIgnoreCase) ||
                 imageSource.Contains("line", StringComparison.OrdinalIgnoreCase)))
            {
                requirements.Add(CreateResourceRequirement(
                    "camera_binding", "相机绑定", operatorKey, operatorType, ordinal,
                    "CameraBindingId", VisionAgentResourceResolutionTargets.CameraSettings,
                    VisionAgentResourceDraftPolicies.DraftAllowed, "plan_route"));
            }
            else if (PlanMentionsResourceRequirement(plan, "model") &&
                     (operatorType.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase) ||
                     operatorType.Equals("OnnxInference", StringComparison.OrdinalIgnoreCase) ||
                     operatorType.Equals("SemanticSegmentation", StringComparison.OrdinalIgnoreCase) ||
                     operatorType.Equals("AnomalyDetection", StringComparison.OrdinalIgnoreCase)))
            {
                requirements.Add(CreateResourceRequirement(
                    "model_resource", "模型资源", operatorKey, operatorType, ordinal,
                    "ModelPath", VisionAgentResourceResolutionTargets.ModelPicker,
                    VisionAgentResourceDraftPolicies.DraftAllowed, "plan_route"));
            }
            else if (PlanMentionsResourceRequirement(plan, "template") &&
                     operatorType.Equals("TemplateMatching", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(CreateResourceRequirement(
                    "template_artifact", "模板资源", operatorKey, operatorType, ordinal,
                    "Template", VisionAgentResourceResolutionTargets.TemplatePicker,
                    VisionAgentResourceDraftPolicies.DraftAllowed, "plan_route"));
            }
            else if (PlanMentionsResourceRequirement(plan, "calibration", "measurement") &&
                     operatorType.Equals("UnitConvert", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(CreateResourceRequirement(
                    "calibration_resource", "标定参数", operatorKey, operatorType, ordinal,
                    "Scale", VisionAgentResourceResolutionTargets.CalibrationSettings,
                    VisionAgentResourceDraftPolicies.DraftAllowed, "plan_route"));
            }
        }

        return requirements
            .GroupBy(item => item.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool PlanMentionsResourceRequirement(VisionAgentPlanModeResult plan, params string[] markers)
    {
        return (plan.BlockingReasons ?? [])
            .Any(reason => markers.Any(marker => reason.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static VisionAgentResourceRequirement BuildCameraRequirement(VisionAgentPlanModeResult plan)
    {
        var operators = plan.RecommendedRoute?.Operators ?? [];
        var index = operators.FindIndex(item => item.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase));
        return CreateResourceRequirement(
            "camera_binding", "相机绑定",
            VisionAgentResourceIdentity.OperatorKey("ImageAcquisition", Math.Max(0, index)),
            "ImageAcquisition", Math.Max(0, index), "CameraBindingId",
            VisionAgentResourceResolutionTargets.CameraSettings,
            VisionAgentResourceDraftPolicies.DraftAllowed, "accepted_answer");
    }

    private static VisionAgentResourceRequirement BuildLegacyResourceRequirement(
        VisionAgentPlanModeResult plan,
        string key,
        string field,
        string questionId)
    {
        if (IsCameraResourceSignal(plan, key, field))
        {
            return BuildCameraRequirement(plan) with { Source = "legacy_blocker" };
        }

        if (VisionAgentResourceIdentity.TryParseCanonicalId(
                key,
                out var canonicalType,
                out var canonicalOperator,
                out var canonicalParameter) &&
            !canonicalType.Equals("resource", StringComparison.OrdinalIgnoreCase))
        {
            return CreateResourceRequirement(
                canonicalType,
                ResourceName(canonicalType),
                canonicalOperator,
                string.Empty,
                -1,
                canonicalParameter,
                ResolutionTarget(canonicalType),
                VisionAgentResourceDraftPolicies.DraftAllowed,
                "canonical_blocker");
        }

        var normalized = VisionAgentResourceIdentity.NormalizeToken($"{key}_{field}");
        if (normalized.Contains("model", StringComparison.Ordinal))
        {
            var operatorType = (plan.RecommendedRoute?.Operators ?? []).FirstOrDefault(op =>
                op.Contains("learning", StringComparison.OrdinalIgnoreCase) ||
                op.Contains("onnx", StringComparison.OrdinalIgnoreCase) ||
                op.Contains("segmentation", StringComparison.OrdinalIgnoreCase) ||
                op.Contains("anomaly", StringComparison.OrdinalIgnoreCase)) ?? "DeepLearning";
            return CreateResourceRequirement("model_resource", "模型资源", VisionAgentResourceIdentity.OperatorKey(operatorType, 0), operatorType, 0, "ModelPath", VisionAgentResourceResolutionTargets.ModelPicker, VisionAgentResourceDraftPolicies.DraftAllowed, "legacy_blocker");
        }
        if (normalized.Contains("template", StringComparison.Ordinal))
            return CreateResourceRequirement("template_artifact", "模板资源", VisionAgentResourceIdentity.OperatorKey("TemplateMatching", 0), "TemplateMatching", 0, "Template", VisionAgentResourceResolutionTargets.TemplatePicker, VisionAgentResourceDraftPolicies.DraftAllowed, "legacy_blocker");
        if (normalized.Contains("plc", StringComparison.Ordinal) || normalized.Contains("external", StringComparison.Ordinal))
            return CreateResourceRequirement("plc_output", "外部输出资源", "resultoutput#1", "ResultOutput", 0, "OutputChannel", VisionAgentResourceResolutionTargets.OutputSettings, VisionAgentResourceDraftPolicies.BuildRequired, "legacy_blocker");
        return CreateResourceRequirement("resource", "工程资源", string.Empty, string.Empty, -1, field, VisionAgentResourceResolutionTargets.Replan, VisionAgentResourceDraftPolicies.BuildRequired, "legacy_blocker", key);
    }

    private static bool IsCameraResourceSignal(VisionAgentPlanModeResult plan, string key, string field)
    {
        var normalizedField = VisionAgentPlanFieldPolicy.NormalizeField(field);
        if (normalizedField.Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hasAcquisition = (plan.RecommendedRoute?.Operators ?? [])
            .Any(op => op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase));
        if (!hasAcquisition)
        {
            return false;
        }

        var imageSourceMissing = (plan.SemanticExtraction?.MissingFields ?? [])
                .Concat(plan.RequirementMaturity?.MissingFields ?? [])
                .Any(item => VisionAgentPlanFieldPolicy.NormalizeField(item)
                    .Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase)) ||
            (string.IsNullOrWhiteSpace(plan.SemanticExtraction?.ImageSource) &&
             (plan.RemainingPlanFields ?? []).Any(item => VisionAgentPlanFieldPolicy.NormalizeField(item)
                 .Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase)));
        var normalizedKey = VisionAgentResourceIdentity.NormalizeToken(key);
        var genericCanonical = VisionAgentResourceIdentity.TryParseCanonicalId(
            key,
            out var canonicalType,
            out _,
            out _) && canonicalType.Equals("resource", StringComparison.OrdinalIgnoreCase);
        if (imageSourceMissing &&
            (genericCanonical || string.IsNullOrWhiteSpace(normalizedKey) ||
             !ContainsAny(normalizedKey, "model", "template", "calibration", "plc", "output")))
        {
            return true;
        }

        // Compatibility only: structured field, semantic and route evidence above remain authoritative.
        return ContainsAny(key, "camera", "image source", "image sample", "相机", "图像", "样本");
    }

    private static string ResourceName(string resourceType) => resourceType switch
    {
        "camera_binding" => "相机绑定",
        "model_resource" => "模型资源",
        "template_artifact" => "模板资源",
        "calibration_resource" => "标定参数",
        "plc_output" => "外部输出资源",
        _ => "工程资源"
    };

    private static string ResolutionTarget(string resourceType) => resourceType switch
    {
        "camera_binding" => VisionAgentResourceResolutionTargets.CameraSettings,
        "model_resource" => VisionAgentResourceResolutionTargets.ModelPicker,
        "template_artifact" => VisionAgentResourceResolutionTargets.TemplatePicker,
        "calibration_resource" => VisionAgentResourceResolutionTargets.CalibrationSettings,
        "plc_output" or "output_channel" => VisionAgentResourceResolutionTargets.OutputSettings,
        _ => VisionAgentResourceResolutionTargets.Replan
    };

    private static VisionAgentResourceRequirement CreateResourceRequirement(
        string resourceType,
        string resourceName,
        string operatorKey,
        string operatorType,
        int operatorIndex,
        string parameterName,
        string resolutionTarget,
        string draftPolicy,
        string source,
        string fallbackScope = "")
    {
        var canonicalId = VisionAgentResourceIdentity.CreateCanonicalId(resourceType, operatorKey, parameterName, fallbackScope);
        return new VisionAgentResourceRequirement
        {
            CanonicalId = canonicalId,
            ResourceType = VisionAgentResourceIdentity.NormalizeResourceType(resourceType),
            ResourceName = resourceName,
            ResourceKey = $"{operatorKey}.{parameterName}".Trim('.'),
            OperatorKey = operatorKey,
            OperatorType = operatorType,
            OperatorIndex = operatorIndex,
            ParameterName = parameterName,
            Status = VisionAgentResourceStatuses.Pending,
            BlockingScope = VisionAgentResourceBlockingScopes.Build,
            Source = source,
            ResolutionTarget = resolutionTarget,
            DraftPolicy = draftPolicy,
            Description = $"{resourceName}尚未绑定。",
            Aliases = VisionAgentResourceIdentity.BuildAliases(canonicalId, resourceType, operatorKey, parameterName, fallbackScope).ToList()
        };
    }

    private static bool IsResourceBound(
        VisionAgentResourceRequirement resource,
        IReadOnlyList<VisionAgentResourceDecision>? decisions)
    {
        return decisions?.Any(decision =>
            decision.Status.Equals(VisionAgentResourceStatuses.Bound, StringComparison.OrdinalIgnoreCase) &&
            (decision.CanonicalId.Equals(resource.CanonicalId, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(decision.OperatorKey) &&
             VisionAgentResourceIdentity.CreateCanonicalId(
                  decision.ResourceType,
                  decision.OperatorKey,
                  decision.ParameterName,
                  decision.ResourceKey).Equals(resource.CanonicalId, StringComparison.OrdinalIgnoreCase)))) == true;
    }

    private static bool IsFieldSatisfiedByBoundResource(
        VisionAgentPlanModeResult plan,
        string? field,
        IReadOnlyList<VisionAgentResourceDecision>? decisions)
    {
        return VisionAgentPlanFieldPolicy.NormalizeField(field)
                   .Equals(VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase) &&
               IsResourceBound(BuildCameraRequirement(plan), decisions);
    }

    private static bool ResourceBlocksBuild(
        VisionAgentResourceRequirement resource,
        string requirementMode,
        AiRequirementMaturityResult? maturity,
        bool hasSupportedRoute)
    {
        if (!requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)) return true;
        if (resource.DraftPolicy.Equals(VisionAgentResourceDraftPolicies.BuildRequired, StringComparison.OrdinalIgnoreCase)) return true;
        return !hasSupportedRoute || maturity?.CanPlan != true;
    }

    private static string ResourceBlockerId(VisionAgentResourceRequirement resource) =>
        $"resource_pending:{VisionAgentResourceIdentity.Canonicalize(resource.CanonicalId)}";

    private static string ResourceField(VisionAgentResourceRequirement resource) =>
        resource.ResourceType.Equals("camera_binding", StringComparison.OrdinalIgnoreCase)
            ? VisionAgentPlanAnswerFields.ImageSource
            : resource.ResourceType;

    private static string ResourcePublicLabel(VisionAgentResourceRequirement resource, bool blocksBuild) =>
        blocksBuild
            ? $"{resource.ResourceName}必须在构建前补齐。"
            : $"{resource.ResourceName}可在草稿生成后补齐；部署和运行仍被阻止。";

    private static bool ShouldBlockResourcePending(
        VisionAgentPlanModeResult plan,
        string field,
        string requirementMode,
        AiRequirementMaturityResult? maturity,
        bool hasSupportedRoute)
    {
        if (!requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = VisionAgentPlanFieldPolicy.NormalizeField(field);
        if (normalized.Equals(VisionAgentPlanAnswerFields.OutputTarget, StringComparison.OrdinalIgnoreCase) &&
            RequestsExternalOutput(plan, string.Empty))
        {
            return true;
        }

        return !hasSupportedRoute || maturity?.CanPlan != true;
    }

    private static bool RequiresStrategyConfirmation(
        string requirementMode,
        AiRequirementMaturityResult? maturity,
        bool hasSupportedRoute)
    {
        return !requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) ||
               maturity?.CanPlan != true ||
               !hasSupportedRoute;
    }

    private static bool CanDraftWithPendingImageSource(VisionAgentPlanModeResult plan)
    {
        return (plan.RecommendedRoute?.Operators ?? [])
            .Select(Clean)
            .Any(op => op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSupportedRouteOrTemplate(
        VisionAgentPlanModeResult plan,
        out List<string> invalidOperators)
    {
        invalidOperators = [];
        var routeOperators = plan.RecommendedRoute?.Operators?
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];

        if (routeOperators.Count > 0)
        {
            invalidOperators = routeOperators
                .Where(op => ForbiddenOperatorTypes.Contains(op))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var allowed = new VisionAgentOperatorContractCatalog().OperatorTypes
                .Where(type => !ForbiddenOperatorTypes.Contains(type))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var supportedOperators = routeOperators
                .Where(op => allowed.Contains(op))
                .ToList();
            if (invalidOperators.Count == 0 &&
                supportedOperators.Any(op => !string.Equals(op, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)) &&
                supportedOperators.Any(op => string.Equals(op, "ResultOutput", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (plan.TemplateSelection != null &&
            (!string.IsNullOrWhiteSpace(plan.TemplateSelection.TemplateId) ||
             !string.IsNullOrWhiteSpace(plan.TemplateSelection.ScenarioKey)))
        {
            return true;
        }

        return false;
    }

    private static bool AllowsDefaultLocalOutput(
        VisionAgentPlanModeResult plan,
        VisionAgentPlanAnswerValidationResult? validatedAnswers = null)
    {
        var selectedOutputTarget = ReadAcceptedOutputTarget(validatedAnswers);
        if (!string.IsNullOrWhiteSpace(selectedOutputTarget))
        {
            return !ContainsExternalOutputTarget(selectedOutputTarget) &&
                   !ContainsExternalOutputAction(selectedOutputTarget);
        }

        if (RequestsExternalOutput(plan, string.Empty))
        {
            return false;
        }

        var taskType = FirstNonEmpty(plan.SemanticExtraction?.TaskType, plan.RequirementMaturity?.TaskType);
        return !taskType.Equals(AiVisionTaskTypes.PlcOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAcceptedOutputTarget(VisionAgentPlanAnswerValidationResult? validation)
    {
        return validation?.AcceptedAnswers
            .FirstOrDefault(answer =>
                answer.Field.Equals(VisionAgentPlanAnswerFields.OutputTarget, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;
    }

    private static bool RequestsExternalOutput(VisionAgentPlanModeResult plan, string reason)
    {
        if (plan.Intent.Equals(AiVisionTaskTypes.PlcOutput, StringComparison.OrdinalIgnoreCase) ||
            plan.SemanticExtraction?.TaskType.Equals(AiVisionTaskTypes.PlcOutput, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (ContainsExternalOutputTarget(plan.SemanticExtraction?.OutputTarget))
        {
            return true;
        }

        if (PolicyRequestsExternalOutput(plan.PlcOutputPolicy))
        {
            return true;
        }

        if (ContainsExternalOutputTarget(plan.RecommendedRoute?.RouteId) ||
            ContainsExternalOutputAction(plan.RecommendedRoute?.Summary))
        {
            return true;
        }

        return ContainsExternalOutputAction(reason) ||
               ContainsExternalOutputAction(plan.Goal) ||
               ContainsExternalOutputAction(plan.OriginalUserPrompt);
    }

    private static bool PolicyRequestsExternalOutput(string? policy)
    {
        var text = Clean(policy);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (Regex.IsMatch(text, @"\b(disabled|disable|off|forbidden|denied)\b", RegexOptions.IgnoreCase) ||
            ContainsAny(text, "禁用", "关闭", "不写入", "不对接", "本地 ResultOutput"))
        {
            return false;
        }

        return ContainsExternalOutputTarget(text) || ContainsExternalOutputAction(text);
    }

    private static bool ContainsExternalOutputTarget(string? value)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (IsLocalOrDisabledOutput(text))
        {
            return false;
        }

        if (Regex.IsMatch(
                text,
                @"(^|[^A-Za-z0-9])(plc|mes|erp|api|webhook)([^A-Za-z0-9]|$)",
                RegexOptions.IgnoreCase))
        {
            return true;
        }

        return ContainsAny(
            text,
            "plc_output",
            "business_system_output",
            "external_system_output",
            "对接MES",
            "写入PLC",
            "发送到ERP",
            "发送ERP",
            "业务系统接口",
            "网络接口",
            "HTTP接口",
            "API接口",
            "Webhook");
    }

    private static bool ContainsExternalOutputAction(string? value)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (IsLocalOrDisabledOutput(text) &&
            !Regex.IsMatch(text, @"\b(mes|erp|api|webhook)\b", RegexOptions.IgnoreCase) &&
            !ContainsAny(text, "输出到 MES", "发送到 ERP", "调用 HTTP API", "推送 Webhook", "对接业务系统"))
        {
            return false;
        }

        return Regex.IsMatch(
                   text,
                   @"\b(send|write|output|push|post|call|publish|emit)\b.{0,32}\b(plc|mes|erp|http|api|webhook)\b",
                   RegexOptions.IgnoreCase) ||
               Regex.IsMatch(
                   text,
                   @"\b(plc|mes|erp|http|api|webhook)\b.{0,32}\b(output|endpoint|write|push|post|call)\b",
                   RegexOptions.IgnoreCase) ||
               Regex.IsMatch(
                   text,
                   @"(输出|发送|发|写入|推送|调用|对接).{0,16}(MES|PLC|ERP|HTTP|API|Webhook|业务系统)",
                   RegexOptions.IgnoreCase) ||
               ContainsAny(
                   text,
                   "输出到MES",
                   "输出到 MES",
                   "写入PLC",
                   "写入 PLC",
                   "发送到ERP",
                   "发送到 ERP",
                   "调用HTTP API",
                   "调用 HTTP API",
                   "推送Webhook",
                   "推送 Webhook",
                   "对接业务系统",
                   "业务系统接口");
    }

    private static bool IsLocalOrDisabledOutput(string value)
    {
        return ContainsAny(
            value,
            "local_result_payload",
            "structured_result_output",
            "local ResultOutput",
            "本地 ResultOutput",
            "本地输出",
            "本地结果",
            "PLC disabled",
            "PLC writes disabled",
            "PLC write disabled",
            "不写入 PLC",
            "不写入PLC",
            "不对接",
            "禁用 PLC",
            "禁用PLC");
    }

    private static Dictionary<string, VisionAgentClarificationQuestion> BuildQuestionIndex(VisionAgentPlanModeResult plan)
    {
        return (plan.ClarificationQuestions ?? [])
            .Where(question => !string.IsNullOrWhiteSpace(question.Id))
            .GroupBy(question => Clean(question.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static VisionAgentClarificationQuestion? FindQuestion(
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questionIndex,
        string key)
    {
        if (questionIndex.TryGetValue(key, out var question))
        {
            return question;
        }

        return questionIndex.Values.FirstOrDefault(item =>
            Clean(item.Field).Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindQuestionIdForField(
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questionIndex,
        string field)
    {
        return questionIndex.Values
            .FirstOrDefault(question =>
                VisionAgentPlanFieldPolicy.ResolveQuestionField(question).Equals(field, StringComparison.OrdinalIgnoreCase))
            ?.Id ?? string.Empty;
    }

    private static (string Kind, string Key) ParseLegacyReason(string? reason)
    {
        var clean = Clean(reason).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return (string.Empty, string.Empty);
        }

        var separator = clean.IndexOf(':', StringComparison.Ordinal);
        var kind = separator > 0 ? clean[..separator] : string.Empty;
        var key = separator > 0 ? clean[(separator + 1)..] : clean;
        const string suffix = "_missing";
        if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            key = key[..^suffix.Length];
        }

        return (kind, key);
    }

    private static VisionAgentBuildReadinessSnapshot Snapshot(
        bool canBuild,
        List<VisionAgentBuildBlocker> blockers,
        List<string> resolvedFields,
        List<string> remainingFields,
        string primaryMessage,
        string contractVersion = VisionAgentPlanContractVersions.V2)
    {
        return new VisionAgentBuildReadinessSnapshot
        {
            CanBuild = canBuild,
            Blockers = blockers,
            ResolvedFields = resolvedFields,
            RemainingFields = remainingFields,
            PrimaryMessage = primaryMessage,
            ContractVersion = contractVersion,
            MissingResources = blockers
                .Where(blocker => blocker.Resource != null)
                .Select(blocker => blocker.Resource!)
                .GroupBy(resource => resource.CanonicalId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList()
        };
    }

    private static VisionAgentBuildBlocker Blocker(
        string id,
        string category,
        string field,
        string questionId,
        bool blocksBuild,
        string resolutionMode,
        string publicLabel,
        VisionAgentResourceRequirement? resource = null)
    {
        return new VisionAgentBuildBlocker
        {
            Id = id,
            Category = category,
            Field = VisionAgentPlanFieldPolicy.NormalizeField(field),
            QuestionId = Clean(questionId),
            BlocksBuild = blocksBuild,
            ResolutionMode = resolutionMode,
            PublicLabel = Clean(publicLabel),
            Resource = resource
        };
    }

    private static string PrimaryMessage(
        bool canBuild,
        IReadOnlyList<VisionAgentBuildBlocker> blockers,
        AiRequirementMaturityResult? maturity)
    {
        if (canBuild)
        {
            if (blockers.Any(blocker =>
                    blocker.Category.Equals(VisionAgentBuildBlockerCategories.ResourcePending, StringComparison.OrdinalIgnoreCase) &&
                    blocker.BlocksBuild == false))
            {
                return "可以生成可编辑草稿；待补资源仍会阻止部署和运行。";
            }
            return "规划已完成，可以开始构建。";
        }

        var blocking = blockers.FirstOrDefault(blocker => blocker.BlocksBuild);
        if (blocking != null &&
            !string.IsNullOrWhiteSpace(blocking.PublicLabel))
        {
            return blocking.PublicLabel;
        }

        return maturity?.PublicReason ?? "当前规划仍需澄清，暂不可构建。";
    }

    private static string FieldLabel(string? field)
    {
        return VisionAgentPlanFieldPolicy.NormalizeField(field) switch
        {
            VisionAgentPlanAnswerFields.InspectionObject => "检测对象",
            VisionAgentPlanAnswerFields.TaskType => "任务类型",
            VisionAgentPlanAnswerFields.ImageSource => "图像来源",
            VisionAgentPlanAnswerFields.AcceptanceCriteria => "判定标准",
            VisionAgentPlanAnswerFields.OutputTarget => "输出目标",
            VisionAgentPlanAnswerFields.TargetAttribute => "目标属性",
            VisionAgentPlanAnswerFields.DefectType => "缺陷类型",
            VisionAgentPlanAnswerFields.MeasurementTarget => "测量目标",
            VisionAgentPlanAnswerFields.AlgorithmStrategy => "构建策略",
            VisionAgentPlanAnswerFields.RoiStrategy => "ROI 策略",
            VisionAgentPlanAnswerFields.TemplateStrategy => "模板策略",
            _ => Clean(field)
        };
    }

    private static string RecommendedValue(VisionAgentClarificationQuestion question)
    {
        var value = Clean(question.Options.FirstOrDefault(option =>
            option.Recommended &&
            VisionAgentPlanFieldPolicy.IsResolveFieldOption(option))?.Value);
        return VisionAgentPlanFieldPolicy.IsPlaceholderValue(value) ? string.Empty : value;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values
            .Select(Clean)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeKey(string? value)
    {
        var text = Clean(value)
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        var chars = text
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == ':' ? ch : '_')
            .ToArray();
        return string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
