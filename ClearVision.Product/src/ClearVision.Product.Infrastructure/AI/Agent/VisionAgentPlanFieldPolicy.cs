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
    private const int MaxPublicReasonChars = 160;

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

    public static string FormatAcceptanceCriteria(string? ok, string? ng)
    {
        var hasOk = !string.IsNullOrWhiteSpace(ok);
        var hasNg = !string.IsNullOrWhiteSpace(ng);
        if (hasOk && hasNg)
        {
            return $"OK: {ok!.Trim()}; NG: {ng!.Trim()}";
        }
        if (hasOk)
        {
            return ok!.Trim();
        }
        if (hasNg)
        {
            return ng!.Trim();
        }
        return string.Empty;
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
            return true;
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
                        .Select(NormalizeOptionContract)
                        .Take(5)
                        .ToList();
                    var rawDefault = IsPlaceholderValue(q.DefaultValue) ? string.Empty : q.DefaultValue;
                    var recommended = options.FirstOrDefault(option => option.Recommended)?.Value ??
                                      options.FirstOrDefault()?.Value ??
                                      rawDefault;

                    result.Add(q with
                    {
                        Id = string.IsNullOrWhiteSpace(q.Id) ? $"q_{canonicalField}" : q.Id,
                        Field = canonicalField,
                        Title = string.IsNullOrWhiteSpace(q.Title) ? $"请确认 {canonicalField}" : q.Title,
                        Why = string.IsNullOrWhiteSpace(q.Why) ? "这会影响流程规划。" : q.Why,
                        DefaultValue = string.IsNullOrWhiteSpace(rawDefault) ? recommended : rawDefault,
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
            var impact = "缺少此字段将阻碍流程的自动构建。";

            var options = BuildFallbackOptions(field);
            var recommended = options.FirstOrDefault(option => option.Recommended)?.Value ??
                              options.FirstOrDefault()?.Value ??
                              string.Empty;
            var defaultAssumption = BuildFallbackDefaultAssumption(field, recommended);

            list.Add(new VisionAgentClarificationQuestion
            {
                Id = $"q_fallback_{field}",
                Field = field,
                Title = title,
                Why = why,
                DefaultValue = recommended,
                DefaultAssumption = defaultAssumption,
                Impact = impact,
                Options = options
            });
        }
        return list;
    }

    private static List<VisionAgentClarificationOption> BuildFallbackOptions(string field)
    {
        return field switch
        {
            VisionAgentPlanAnswerFields.InspectionObject =>
            [
                Option("object_pending", "保持检测对象待确认", true, "规划阶段不猜测检测对象，先把对象作为待确认元数据保留。", "可以继续形成规划，但不会解除构建阻断。"),
                Option("use_prompt_object", "使用用户描述中的对象", false, "当提示词已明确对象时使用该对象。", "对象描述过宽时仍需要复核。"),
                Option("operator_input", "由操作员补充对象", false, "要求操作员明确输入检测对象。", "增加人工确认步骤。")
            ],
            VisionAgentPlanAnswerFields.TaskType =>
            [
                Option("task_type_pending", "保持任务类型待确认", true, "任务类型不在安全白名单或语义不够明确时，先保留为待确认。", "可以进入规划，但不会解除构建阻断。"),
                Option("general_inspection", "通用视觉检查", false, "仅在用户确认这是通用检查时使用。", "可能不适合测量、引导或定位任务。"),
                Option("custom_task", "自定义视觉任务", false, "保留为用户确认后的自定义任务。", "构建前仍需要确认算子策略。")
            ],
            VisionAgentPlanAnswerFields.ImageSource =>
            [
                Option("camera_pending", "保持图像来源待确认", true, "不猜测相机、文件路径或采集资源，先保留为待确认输入。", "可以规划采集环节，但不会解除构建阻断。"),
                Option("file_sample", "离线样张", false, "使用用户确认的离线样张作为输入。", "只适合早期验证，不代表产线采集。"),
                Option("station_camera", "工站相机", false, "使用已绑定的工站相机元数据。", "需要确认相机资源和采集配置。")
            ],
            VisionAgentPlanAnswerFields.AcceptanceCriteria =>
            [
                Option("ok_ng_pending", "保持判定标准待确认", true, "不伪造 OK/NG、阈值或公差，先保留为待确认标准。", "规划可继续，但构建前必须确认。"),
                Option("defect_is_ng", "缺陷即 NG", false, "用户确认可见缺陷或不匹配即判 NG 时使用。", "只适合常见外观或有无检测。"),
                Option("measure_tolerance_pending", "公差待确认", false, "测量、公差或轨迹偏差标准暂不确定时使用。", "构建前仍需补充具体数值或规则。")
            ],
            VisionAgentPlanAnswerFields.OutputTarget =>
            [
                Option("output_pending", "保持输出目标待确认", true, "不猜测 PLC、网络或文件输出，先保留为待确认输出。", "不会解除构建或部署前的输出阻断。"),
                Option("local_result", "本地结构化结果", false, "仅输出本地结果字段。", "适合早期验证，避免不安全外部写入。"),
                Option("plc_pending", "PLC 输出待确认", false, "保留 PLC 地址、握手和失效保护为待确认元数据。", "需要工程师确认后才能部署。")
            ],
            VisionAgentPlanAnswerFields.AlgorithmStrategy =>
            [
                Option("strategy_pending", "保持算法策略待确认", true, "不默认选择规则、模板或模型，先保留策略确认。", "不会解除构建阻断。"),
                Option("traditional_rule", "优先规则/几何算法", false, "目标特征稳定、可测量且用户确认时使用。", "不适合外观变化大或需模型识别的任务。"),
                Option("model_pending", "模型资源待确认", false, "规划模型算子，但模型资源仍待绑定。", "构建或部署前需要模型元数据。")
            ],
            _ =>
            [
                Option("metadata_pending", "保持待确认", true, "该字段暂无安全默认值，需用户或工程师确认。", "可以继续规划，但不会解除构建阻断。"),
                Option("use_prompt_value", "使用提示词中的值", false, "仅在提示词已明确该值时使用。", "构建前仍建议复核。"),
                Option("operator_input", "由操作员补充", false, "要求操作员明确输入该字段。", "增加人工确认步骤。")
            ]
        };
    }

    private static string BuildFallbackDefaultAssumption(string field, string recommended)
    {
        if (IsPlaceholderValue(recommended))
        {
            return field switch
            {
                VisionAgentPlanAnswerFields.InspectionObject => "暂无安全默认检测对象，推荐保持待确认；该选择不视为已解决字段。",
                VisionAgentPlanAnswerFields.TaskType => "暂无安全默认任务类型，推荐保持待确认；该选择不视为已解决字段。",
                VisionAgentPlanAnswerFields.ImageSource => "暂无安全默认图像来源，推荐保持待确认；该选择不视为已解决字段。",
                VisionAgentPlanAnswerFields.AcceptanceCriteria => "暂无安全默认判定标准，推荐保持待确认；该选择不视为已解决字段。",
                VisionAgentPlanAnswerFields.OutputTarget => "暂无安全默认输出目标，推荐保持待确认；该选择不视为已解决字段。",
                VisionAgentPlanAnswerFields.AlgorithmStrategy => "暂无安全默认算法策略，推荐保持待确认；该选择不视为已解决字段。",
                _ => "暂无安全默认值，推荐保持待确认；该选择不视为已解决字段。"
            };
        }

        return "推荐项可作为规划阶段默认值；构建前仍会按字段完整性和资源就绪状态复核。";
    }

    private static VisionAgentClarificationOption Option(
        string value,
        string label,
        bool recommended,
        string description,
        string impact)
    {
        var effect = InferLegacyAnswerEffect(value);
        return new VisionAgentClarificationOption
        {
            Value = value,
            Label = label,
            Recommended = recommended,
            AnswerEffect = effect,
            RecommendationReason = recommended
                ? effect == VisionAgentClarificationAnswerEffects.ResolveField
                    ? "This option can resolve the canonical field; resource readiness is evaluated separately."
                    : "This option keeps the field pending until a user or engineer confirms it."
                : string.Empty,
            Description = description,
            Impact = impact
        };
    }

    public static VisionAgentClarificationOption NormalizeOptionContract(VisionAgentClarificationOption option)
    {
        return option with
        {
            AnswerEffect = NormalizeAnswerEffect(option),
            RecommendationReason = NormalizeRecommendationReason(option.RecommendationReason)
        };
    }

    public static string NormalizeAnswerEffect(VisionAgentClarificationOption? option)
    {
        var raw = Clean(option?.AnswerEffect).ToLowerInvariant();
        return raw switch
        {
            VisionAgentClarificationAnswerEffects.ResolveField => VisionAgentClarificationAnswerEffects.ResolveField,
            VisionAgentClarificationAnswerEffects.Defer => VisionAgentClarificationAnswerEffects.Defer,
            VisionAgentClarificationAnswerEffects.Informational => VisionAgentClarificationAnswerEffects.Informational,
            _ => InferLegacyAnswerEffect(option?.Value)
        };
    }

    public static string NormalizeRecommendationReason(string? value)
    {
        var text = Clean(value);
        if (text.Length > MaxPublicReasonChars)
        {
            text = text[..MaxPublicReasonChars];
        }

        return text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    public static bool IsResolveFieldOption(VisionAgentClarificationOption? option)
    {
        return NormalizeAnswerEffect(option) == VisionAgentClarificationAnswerEffects.ResolveField;
    }

    private static string InferLegacyAnswerEffect(string? value)
    {
        return IsPlaceholderValue(value)
            ? VisionAgentClarificationAnswerEffects.Defer
            : VisionAgentClarificationAnswerEffects.ResolveField;
    }

    public static bool IsPlaceholderValue(string? value)
    {
        var normalized = Clean(value).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized is "custom_input" or "unknown" or "unspecified" or "metadata_only" or "pending")
        {
            return true;
        }

        return normalized.EndsWith("_pending", StringComparison.OrdinalIgnoreCase);
    }
}
