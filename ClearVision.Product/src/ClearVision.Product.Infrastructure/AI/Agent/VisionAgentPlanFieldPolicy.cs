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

    private static readonly Dictionary<string, string> LegacyQuestionFieldMap = new(StringComparer.OrdinalIgnoreCase)
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
        return TryGet(field, out var rule) ? rule.Field : string.Empty;
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
}
