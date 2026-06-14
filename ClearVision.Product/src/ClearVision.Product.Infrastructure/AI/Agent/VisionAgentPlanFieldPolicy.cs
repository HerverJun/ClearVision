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
        ["template_asset"] = VisionAgentPlanAnswerFields.TemplateStrategy
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
            : string.Empty;
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
}
