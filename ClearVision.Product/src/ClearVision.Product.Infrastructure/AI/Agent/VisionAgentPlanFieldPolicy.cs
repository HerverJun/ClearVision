using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentPlanFieldRule(
    string Field,
    string Category,
    bool AllowFreeText,
    bool AllowRecommendedConfirmation);

public static class VisionAgentPlanFieldCategories
{
    public const string Requirement = "requirement";
    public const string BuildDecision = "build_decision";
}

public static class VisionAgentPlanFieldPolicy
{
    private static readonly Dictionary<string, VisionAgentPlanFieldRule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        [VisionAgentPlanAnswerFields.InspectionObject] = Requirement(VisionAgentPlanAnswerFields.InspectionObject, allowFreeText: true),
        [VisionAgentPlanAnswerFields.TaskType] = Requirement(VisionAgentPlanAnswerFields.TaskType, allowFreeText: true),
        [VisionAgentPlanAnswerFields.ImageSource] = Requirement(VisionAgentPlanAnswerFields.ImageSource, allowFreeText: true),
        [VisionAgentPlanAnswerFields.AcceptanceCriteria] = Requirement(VisionAgentPlanAnswerFields.AcceptanceCriteria, allowFreeText: true),
        [VisionAgentPlanAnswerFields.OutputTarget] = Requirement(VisionAgentPlanAnswerFields.OutputTarget, allowFreeText: true),
        [VisionAgentPlanAnswerFields.TargetAttribute] = Requirement(VisionAgentPlanAnswerFields.TargetAttribute, allowFreeText: true),
        [VisionAgentPlanAnswerFields.DefectType] = Requirement(VisionAgentPlanAnswerFields.DefectType, allowFreeText: true),
        [VisionAgentPlanAnswerFields.MeasurementTarget] = Requirement(VisionAgentPlanAnswerFields.MeasurementTarget, allowFreeText: true),

        [VisionAgentPlanAnswerFields.AlgorithmStrategy] = BuildDecision(VisionAgentPlanAnswerFields.AlgorithmStrategy),
        [VisionAgentPlanAnswerFields.RoiStrategy] = BuildDecision(VisionAgentPlanAnswerFields.RoiStrategy),
        [VisionAgentPlanAnswerFields.TemplateStrategy] = BuildDecision(VisionAgentPlanAnswerFields.TemplateStrategy)
    };

    public static readonly Dictionary<string, string> LegacyQuestionFieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["object_type"] = VisionAgentPlanAnswerFields.InspectionObject,
        ["product_type"] = VisionAgentPlanAnswerFields.InspectionObject,
        ["part_type"] = VisionAgentPlanAnswerFields.InspectionObject,
        ["inspection_target"] = VisionAgentPlanAnswerFields.InspectionObject,
        ["detection_target"] = VisionAgentPlanAnswerFields.InspectionObject,
        ["task_category"] = VisionAgentPlanAnswerFields.TaskType,
        ["detection_task"] = VisionAgentPlanAnswerFields.TaskType,
        ["inspection_task"] = VisionAgentPlanAnswerFields.TaskType,
        ["visual_task"] = VisionAgentPlanAnswerFields.TaskType,
        ["medical_modality"] = VisionAgentPlanAnswerFields.TaskType,
        ["lesion_type"] = VisionAgentPlanAnswerFields.TaskType,
        ["medical_modality_and_lesion_type"] = VisionAgentPlanAnswerFields.TaskType,
        ["image_input"] = VisionAgentPlanAnswerFields.ImageSource,
        ["input_source"] = VisionAgentPlanAnswerFields.ImageSource,
        ["source_image"] = VisionAgentPlanAnswerFields.ImageSource,
        ["camera_source"] = VisionAgentPlanAnswerFields.ImageSource,
        ["image_source_roi"] = VisionAgentPlanAnswerFields.ImageSource,
        ["ok_condition"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["ng_condition"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["judgment_rule"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["result_rule"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["output_target"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["output_goal"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["output_destination"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["result_output"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["local_result_payload"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["structured_result_output"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["business_system_output"] = VisionAgentPlanAnswerFields.OutputTarget,
        ["classification_strategy"] = VisionAgentPlanAnswerFields.AlgorithmStrategy,
        ["model_or_rule_strategy"] = VisionAgentPlanAnswerFields.AlgorithmStrategy,
        ["algorithm_strategy"] = VisionAgentPlanAnswerFields.AlgorithmStrategy,
        ["defect_definition"] = VisionAgentPlanAnswerFields.DefectType,
        ["attribute_target"] = VisionAgentPlanAnswerFields.TargetAttribute,
        ["ok_ng_rule"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["presence_judgment"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["decode_policy"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["sequence_rule"] = VisionAgentPlanAnswerFields.AcceptanceCriteria,
        ["measurement_target"] = VisionAgentPlanAnswerFields.MeasurementTarget,
        ["template_asset"] = VisionAgentPlanAnswerFields.TemplateStrategy,
        ["button_layout"] = VisionAgentPlanAnswerFields.TemplateStrategy,
        ["layout_strategy"] = VisionAgentPlanAnswerFields.TemplateStrategy
    };

    public static bool TryGet(string? field, out VisionAgentPlanFieldRule rule)
    {
        return Rules.TryGetValue(Clean(field), out rule!);
    }

    public static string NormalizeField(string? field)
    {
        var cleaned = Clean(field);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }
        if (TryGet(cleaned, out var rule))
        {
            return rule.Field;
        }
        if (LegacyQuestionFieldMap.TryGetValue(cleaned, out var mapped))
        {
            return mapped;
        }
        var inferred = InferFieldFromIdentifier(cleaned);
        if (!string.IsNullOrWhiteSpace(inferred))
        {
            return inferred;
        }
        return string.Empty;
    }

    public static string FormatAcceptanceCriteria(string? ok, string? ng, string? output = null)
    {
        var hasOk = !string.IsNullOrWhiteSpace(ok);
        var hasNg = !string.IsNullOrWhiteSpace(ng);
        if (hasOk && hasNg)
        {
            return $"OK: {ok.Trim()}；NG: {ng.Trim()}";
        }
        if (hasOk)
        {
            return ok.Trim();
        }
        if (hasNg)
        {
            return ng.Trim();
        }
        return !string.IsNullOrWhiteSpace(output) ? output.Trim() : string.Empty;
    }

    public static (string Ok, string Ng) ParseAcceptanceCriteria(string? acceptance)
    {
        if (string.IsNullOrWhiteSpace(acceptance))
        {
            return (string.Empty, string.Empty);
        }
        
        var clean = acceptance.Trim();
        if (clean.Contains("OK:", StringComparison.OrdinalIgnoreCase) && 
            clean.Contains("NG:", StringComparison.OrdinalIgnoreCase))
        {
            var okIdx = clean.IndexOf("OK:", StringComparison.OrdinalIgnoreCase);
            var ngIdx = clean.IndexOf("NG:", StringComparison.OrdinalIgnoreCase);
            if (okIdx >= 0 && ngIdx > okIdx)
            {
                var okPart = clean[(okIdx + 3)..ngIdx].Trim();
                var ngPart = clean[(ngIdx + 3)..].Trim();
                okPart = okPart.TrimEnd('；', ';', ' ', '\t', ',');
                return (okPart, ngPart);
            }
            else if (ngIdx >= 0 && okIdx > ngIdx)
            {
                var ngPart = clean[(ngIdx + 3)..okIdx].Trim();
                var okPart = clean[(okIdx + 3)..].Trim();
                ngPart = ngPart.TrimEnd('；', ';', ' ', '\t', ',');
                return (okPart, ngPart);
            }
        }
        return (clean, string.Empty);
    }


    public static string ResolveQuestionField(VisionAgentClarificationQuestion question)
    {
        var field = NormalizeField(question.Field);
        if (!string.IsNullOrWhiteSpace(field))
        {
            return field;
        }

        var id = Clean(question.Id);
        if (Rules.ContainsKey(id))
        {
            return id;
        }

        return LegacyQuestionFieldMap.TryGetValue(id, out var mapped)
            ? mapped
            : InferFieldFromIdentifier(id);
    }

    public static string ResolveQuestionField(
        VisionAgentClarificationQuestion question,
        IEnumerable<string>? blockingReasons)
    {
        var field = ResolveQuestionField(question);
        if (!string.IsNullOrWhiteSpace(field))
        {
            return field;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Clean(question.Id),
            Clean(question.Field)
        };
        ids.Remove(string.Empty);
        if (ids.Count == 0)
        {
            return string.Empty;
        }

        foreach (var reason in blockingReasons ?? [])
        {
            var blocker = ParseBlockingReason(reason);
            if (string.IsNullOrWhiteSpace(blocker.Key) ||
                !ids.Contains(blocker.Key))
            {
                continue;
            }

            var blockerField = InferFieldFromIdentifier(blocker.Key);
            if (!string.IsNullOrWhiteSpace(blockerField))
            {
                return blockerField;
            }

            if (blocker.Kind.Equals("strategy_confirmation", StringComparison.OrdinalIgnoreCase))
            {
                return VisionAgentPlanAnswerFields.AlgorithmStrategy;
            }
        }

        return string.Empty;
    }

    public static bool IsStrictBlocking(
        string? field,
        string? taskType,
        AiRequirementMaturityResult? maturity)
    {
        var normalized = NormalizeField(field);
        return string.Equals(normalized, VisionAgentPlanAnswerFields.InspectionObject, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, VisionAgentPlanAnswerFields.AcceptanceCriteria, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDraftBlocking(
        string? field,
        string? taskType,
        AiRequirementMaturityResult? maturity)
    {
        var normalized = NormalizeField(field);
        if (string.Equals(normalized, VisionAgentPlanAnswerFields.InspectionObject, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase))
        {
            return maturity?.CanPlan != true;
        }

        return string.Equals(normalized, VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase) &&
               maturity?.CanPlan != true;
    }

    public static IReadOnlyList<string> CanonicalFields => Rules.Keys.ToList();

    private static VisionAgentPlanFieldRule Requirement(string field, bool allowFreeText)
    {
        return new VisionAgentPlanFieldRule(
            field,
            VisionAgentPlanFieldCategories.Requirement,
            allowFreeText,
            AllowRecommendedConfirmation: true);
    }

    private static VisionAgentPlanFieldRule BuildDecision(string field)
    {
        return new VisionAgentPlanFieldRule(
            field,
            VisionAgentPlanFieldCategories.BuildDecision,
            AllowFreeText: false,
            AllowRecommendedConfirmation: true);
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static (string Kind, string Key) ParseBlockingReason(string? reason)
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

    private static string InferFieldFromIdentifier(string value)
    {
        var normalized = Clean(value)
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Contains("inspection_object", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("object_type", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("inspection_target", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("detection_target", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.InspectionObject;
        }

        if (normalized.Contains("task_type", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("task_category", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("inspection_task", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("detection_task", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("visual_task", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("medical_modality", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("lesion_type", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.TaskType;
        }

        if (normalized.Contains("image_source", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("image_input", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("input_source", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("source_image", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("camera_source", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.ImageSource;
        }

        if (normalized.Contains("acceptance_criteria", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ok_ng", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ok_condition", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ng_condition", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("judgment_rule", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("result_rule", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.AcceptanceCriteria;
        }

        if (normalized.Contains("output_target", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("output_goal", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("output_destination", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("result_output", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("local_result_payload", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("structured_result", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("business_system", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.OutputTarget;
        }

        if (normalized.Contains("algorithm_strategy", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("model_or_rule_strategy", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("classification_strategy", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.AlgorithmStrategy;
        }

        return string.Empty;
    }

    public static List<VisionAgentClarificationQuestion> NormalizeQuestions(
        IEnumerable<VisionAgentClarificationQuestion>? questions,
        IReadOnlyList<string> remainingPlanFields,
        IReadOnlyList<string> resolvedPlanFields,
        IReadOnlyList<VisionAgentPlanAnswer> confirmedPlanAnswers)
    {
        if (remainingPlanFields == null || remainingPlanFields.Count == 0)
        {
            return new List<VisionAgentClarificationQuestion>();
        }

        var remainingSet = remainingPlanFields
            .Select(NormalizeField)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resolvedSet = (resolvedPlanFields ?? Array.Empty<string>())
            .Select(NormalizeField)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var confirmedSet = (confirmedPlanAnswers ?? Array.Empty<VisionAgentPlanAnswer>())
            .Select(a => NormalizeField(a.Field))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedSet = remainingSet
            .Except(resolvedSet)
            .Except(confirmedSet)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<VisionAgentClarificationQuestion>();
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (questions != null)
        {
            foreach (var q in questions)
            {
                if (string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Title))
                {
                    continue;
                }

                var field = ResolveQuestionField(q);
                if (string.IsNullOrWhiteSpace(field))
                {
                    field = !string.IsNullOrWhiteSpace(q.Field) ? q.Field : q.Id;
                }

                var canonicalField = NormalizeField(field);
                if (string.IsNullOrWhiteSpace(canonicalField))
                {
                    continue;
                }

                if (!allowedSet.Contains(canonicalField))
                {
                    continue;
                }

                if (seenFields.Add(canonicalField))
                {
                    var options = (q.Options ?? [])
                        .Where(option => !string.IsNullOrWhiteSpace(option.Value) &&
                                         !string.IsNullOrWhiteSpace(option.Label))
                        .Take(5)
                        .ToList();
                    if (options.Count > 0 && options.All(option => !option.Recommended))
                    {
                        options[0] = options[0] with { Recommended = true };
                    }
                    var recommended = options.FirstOrDefault(option => option.Recommended)?.Value ??
                                      options.FirstOrDefault()?.Value ??
                                      q.DefaultValue;

                    result.Add(q with
                    {
                        Id = string.IsNullOrWhiteSpace(q.Id) ? $"q_{canonicalField}" : q.Id,
                        Field = canonicalField,
                        Title = string.IsNullOrWhiteSpace(q.Title) ? $"请确认 {canonicalField}" : q.Title,
                        Why = string.IsNullOrWhiteSpace(q.Why) ? "这会影响流程规划。" : q.Why,
                        DefaultValue = string.IsNullOrWhiteSpace(q.DefaultValue) ? recommended : q.DefaultValue,
                        DefaultAssumption = string.IsNullOrWhiteSpace(q.DefaultAssumption) ? "使用推荐选项。" : q.DefaultAssumption,
                        Impact = string.IsNullOrWhiteSpace(q.Impact) ? "修改该选项会改变构建假设。" : q.Impact,
                        Options = options
                    });
                }
            }
        }

        return result.Take(5).ToList();
    }

    public static List<VisionAgentClarificationQuestion> BuildFallbackQuestionsForRemaining(
        IEnumerable<string> allowedFields)
    {
        var list = new List<VisionAgentClarificationQuestion>();
        foreach (var field in allowedFields)
        {
            var title = field switch
            {
                VisionAgentPlanAnswerFields.InspectionObject => "检测对象说明",
                VisionAgentPlanAnswerFields.TaskType => "检测任务说明",
                VisionAgentPlanAnswerFields.ImageSource => "图像来源说明",
                VisionAgentPlanAnswerFields.AcceptanceCriteria => "合格判定标准说明",
                VisionAgentPlanAnswerFields.OutputTarget => "结果输出目标说明",
                VisionAgentPlanAnswerFields.TargetAttribute => "检测目标属性说明",
                VisionAgentPlanAnswerFields.DefectType => "缺陷类型说明",
                VisionAgentPlanAnswerFields.MeasurementTarget => "测量目标说明",
                VisionAgentPlanAnswerFields.AlgorithmStrategy => "算法策略说明",
                VisionAgentPlanAnswerFields.RoiStrategy => "ROI策略说明",
                VisionAgentPlanAnswerFields.TemplateStrategy => "模板策略说明",
                _ => $"{field} 属性说明"
            };

            var why = $"流程规划需要明确 {title}。";
            var defaultAssumption = "暂无默认假设，请手动输入以补齐槽位。";
            var impact = "缺少此字段将阻碍流程的自动构建。";

            list.Add(new VisionAgentClarificationQuestion
            {
                Id = $"q_fallback_{field}",
                Field = field,
                Title = title,
                Why = why,
                DefaultValue = string.Empty,
                DefaultAssumption = defaultAssumption,
                Impact = impact,
                Options =
                [
                    new VisionAgentClarificationOption
                    {
                        Value = "custom_input",
                        Label = "自定义输入",
                        Recommended = true,
                        Description = "输入您的实际需求",
                        Impact = "基于输入内容重新规划"
                    }
                ]
            });
        }
        return list;
    }
}
