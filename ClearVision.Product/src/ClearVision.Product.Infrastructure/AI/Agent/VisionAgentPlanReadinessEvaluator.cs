using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;
using System.Text.RegularExpressions;

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
        string requirementMode = AiRequirementModes.Strict)
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

        var effectiveForReadiness = ShouldUseEffectiveRequirement(plan, effectiveRequirement, requirementMode)
            ? effectiveRequirement
            : null;
        var maturity = effectiveForReadiness?.Maturity ?? plan.RequirementMaturity;
        var resolvedFields = BuildResolvedFields(plan, validatedAnswers, effectiveForReadiness);
        if (requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) &&
            !PlanHasObjectOrTaskFact(plan))
        {
            resolvedFields.RemoveAll(field => field.Equals(VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase));
        }

        var remainingFields = BuildRemainingFields(maturity, resolvedFields, effectiveForReadiness);
        var answers = validatedAnswers?.AcceptedAnswers ?? [];
        var blockers = new List<VisionAgentBuildBlocker>();
        var questionIndex = BuildQuestionIndex(plan);
        var hasSupportedRoute = HasSupportedRouteOrTemplate(plan, out var invalidOperators);
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

        var distinctBlockers = blockers
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.BlocksBuild).First())
            .Take(16)
            .ToList();
        var canBuild = distinctBlockers.All(blocker => !blocker.BlocksBuild);
        return Snapshot(
            canBuild,
            distinctBlockers,
            resolvedFields,
            remainingFields,
            PrimaryMessage(canBuild, distinctBlockers, maturity),
            VisionAgentPlanContractVersions.V2);
    }

    private static List<string> BuildResolvedFields(
        VisionAgentPlanModeResult plan,
        VisionAgentPlanAnswerValidationResult? validatedAnswers,
        VisionAgentEffectiveRequirement? effectiveRequirement)
    {
        var fields = new List<string>();
        if (plan.ResolvedPlanFields != null)
        {
            fields.AddRange(plan.ResolvedPlanFields);
        }
        if (plan.ConfirmedPlanAnswers != null)
        {
            fields.AddRange(plan.ConfirmedPlanAnswers.Select(a => a.Field));
        }
        if (effectiveRequirement != null)
        {
            fields.AddRange(effectiveRequirement.ResolvedFields);
        }
        else
        {
            fields.AddRange(ReadSemanticResolvedFields(plan.SemanticExtraction));
        }

        if (PlanHasTaskRoute(plan))
        {
            fields.Add(VisionAgentPlanAnswerFields.TaskType);
        }

        if (plan.AcceptanceCriteria.Count > 0)
        {
            fields.Add(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        }

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

    private static List<string> ReadSemanticResolvedFields(VisionAgentSemanticExtractionResult? semantic)
    {
        var fields = new List<string>();
        AddIfValue(fields, VisionAgentPlanAnswerFields.InspectionObject, semantic?.InspectionObject);
        var taskType = semantic?.TaskType ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(taskType) &&
            !taskType.Equals(AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
            !taskType.Equals(AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(VisionAgentPlanAnswerFields.TaskType);
        }

        AddIfValue(fields, VisionAgentPlanAnswerFields.ImageSource, semantic?.ImageSource);
        AddIfValue(fields, VisionAgentPlanAnswerFields.TargetAttribute, semantic?.TargetAttribute);
        AddIfValue(fields, VisionAgentPlanAnswerFields.DefectType, semantic?.DefectType);
        AddIfValue(fields, VisionAgentPlanAnswerFields.MeasurementTarget, semantic?.MeasurementTarget);
        AddIfValue(fields, VisionAgentPlanAnswerFields.OutputTarget, semantic?.OutputTarget);
        AddIfValue(fields, VisionAgentPlanAnswerFields.AcceptanceCriteria, VisionAgentPlanFieldPolicy.FormatAcceptanceCriteria(semantic?.OkCondition, semantic?.NgCondition));
        return fields;
    }

    private static void AddIfValue(List<string> fields, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(field);
        }
    }

    private static List<string> BuildRemainingFields(
        AiRequirementMaturityResult? maturity,
        IReadOnlyList<string> resolvedFields,
        VisionAgentEffectiveRequirement? effectiveRequirement)
    {
        var fields = effectiveRequirement?.RemainingFields ?? maturity?.MissingFields ?? [];
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
            return Blocker(
                $"resource_pending:{idKey}",
                VisionAgentBuildBlockerCategories.ResourcePending,
                field,
                questionId,
                false,
                VisionAgentBuildBlockerResolutionModes.ProvideResource,
                "资源可在开始构建后补齐。");
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
            return Blocker(
                $"resource_pending:{idKey}_missing",
                VisionAgentBuildBlockerCategories.ResourcePending,
                field,
                questionId,
                false,
                VisionAgentBuildBlockerResolutionModes.ProvideResource,
                "资源可在开始构建后补齐。");
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

        foreach (var answer in answers.Where(answer => !string.IsNullOrWhiteSpace(answer.Value)))
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
            !string.IsNullOrWhiteSpace(RecommendedValue(question)))
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
            ContractVersion = contractVersion
        };
    }

    private static VisionAgentBuildBlocker Blocker(
        string id,
        string category,
        string field,
        string questionId,
        bool blocksBuild,
        string resolutionMode,
        string publicLabel)
    {
        return new VisionAgentBuildBlocker
        {
            Id = id,
            Category = category,
            Field = VisionAgentPlanFieldPolicy.NormalizeField(field),
            QuestionId = Clean(questionId),
            BlocksBuild = blocksBuild,
            ResolutionMode = resolutionMode,
            PublicLabel = Clean(publicLabel)
        };
    }

    private static string PrimaryMessage(
        bool canBuild,
        IReadOnlyList<VisionAgentBuildBlocker> blockers,
        AiRequirementMaturityResult? maturity)
    {
        if (canBuild)
        {
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
        return Clean(question.Options.FirstOrDefault(option => option.Recommended)?.Value) is { Length: > 0 } recommended
            ? recommended
            : Clean(question.DefaultValue);
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
