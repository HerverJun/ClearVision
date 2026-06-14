using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentEffectiveRequirement(
    Dictionary<string, string> Values,
    AiRequirementMaturityResult Maturity,
    List<string> ResolvedFields,
    List<string> RemainingFields);

public sealed class VisionAgentPlanRequirementOverlay
{
    public VisionAgentEffectiveRequirement Build(
        VisionAgentPlanModeResult? plan,
        VisionAgentPlanAnswerValidationResult validation,
        VisionAgentRequirementMaturityRequest maturityRequest)
    {
        var values = ReadSemanticValues(plan?.SemanticExtraction);
        foreach (var item in validation.RequirementAnswers)
        {
            values[item.Key] = item.Value;
        }

        var semantic = BuildSemantic(plan?.SemanticExtraction, values);
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(maturityRequest, semantic);
        var resolved = values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value) &&
                           VisionAgentPlanFieldPolicy.TryGet(item.Key, out var rule) &&
                           rule.Category == VisionAgentPlanFieldCategories.Requirement)
            .Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remaining = maturity.MissingFields
            .Select(VisionAgentPlanFieldPolicy.NormalizeField)
            .Where(field => !string.IsNullOrWhiteSpace(field) &&
                            !resolved.Contains(field, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new VisionAgentEffectiveRequirement(values, maturity, resolved, remaining);
    }

    private static Dictionary<string, string> ReadSemanticValues(VisionAgentSemanticExtractionResult? semantic)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(values, VisionAgentPlanAnswerFields.InspectionObject, semantic?.InspectionObject);
        Add(values, VisionAgentPlanAnswerFields.TaskType, NormalizeTaskType(semantic?.TaskType));
        Add(values, VisionAgentPlanAnswerFields.ImageSource, semantic?.ImageSource);
        Add(values, VisionAgentPlanAnswerFields.OutputTarget, semantic?.OutputTarget);
        Add(values, VisionAgentPlanAnswerFields.TargetAttribute, semantic?.TargetAttribute);
        Add(values, VisionAgentPlanAnswerFields.DefectType, semantic?.DefectType);
        Add(values, VisionAgentPlanAnswerFields.MeasurementTarget, semantic?.MeasurementTarget);
        var acceptance = FirstNonEmpty(semantic?.OkCondition, semantic?.NgCondition);
        Add(values, VisionAgentPlanAnswerFields.AcceptanceCriteria, acceptance);
        return values;
    }

    private static VisionAgentSemanticExtractionResult BuildSemantic(
        VisionAgentSemanticExtractionResult? semantic,
        IReadOnlyDictionary<string, string> values)
    {
        var taskType = Read(values, VisionAgentPlanAnswerFields.TaskType);
        var inspectionObject = Read(values, VisionAgentPlanAnswerFields.InspectionObject);
        var targetAttribute = Read(values, VisionAgentPlanAnswerFields.TargetAttribute);
        var defectType = Read(values, VisionAgentPlanAnswerFields.DefectType);
        var measurementTarget = Read(values, VisionAgentPlanAnswerFields.MeasurementTarget);
        var imageSource = Read(values, VisionAgentPlanAnswerFields.ImageSource);
        var outputTarget = Read(values, VisionAgentPlanAnswerFields.OutputTarget);
        var acceptance = Read(values, VisionAgentPlanAnswerFields.AcceptanceCriteria);

        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = semantic?.IsVisionRequest ?? true,
            Intent = FirstNonEmpty(semantic?.Intent, "new_flow"),
            TaskType = string.IsNullOrWhiteSpace(taskType) ? AiVisionTaskTypes.Unknown : taskType,
            Confidence = Math.Max(semantic?.Confidence ?? 0.8, 0.8),
            TaskTypeConfidence = Math.Max(semantic?.TaskTypeConfidence ?? 0.8, 0.8),
            InspectionObject = inspectionObject,
            TargetAttribute = targetAttribute,
            DefectType = defectType,
            MeasurementTarget = measurementTarget,
            ImageSource = imageSource,
            OkCondition = FirstNonEmpty(semantic?.OkCondition, acceptance),
            NgCondition = semantic?.NgCondition ?? string.Empty,
            OutputTarget = outputTarget,
            SuggestedRoute = semantic?.SuggestedRoute ?? string.Empty,
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            ObjectSignals = SplitSignals(inspectionObject),
            TaskSignals = SplitSignals(FirstNonEmpty(targetAttribute, defectType, measurementTarget, taskType, acceptance)),
            MissingFields = [],
            ClarificationQuestions = [],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
    }

    private static void Add(Dictionary<string, string> values, string field, string? value)
    {
        var clean = Clean(value);
        if (!string.IsNullOrWhiteSpace(clean))
        {
            values[field] = clean;
        }
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string field)
    {
        return values.TryGetValue(field, out var value) ? Clean(value) : string.Empty;
    }

    private static List<string> SplitSignals(string? value)
    {
        var text = Clean(value);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }

    private static string NormalizeTaskType(string? taskType)
    {
        return Clean(taskType).ToLowerInvariant() switch
        {
            "" => string.Empty,
            AiVisionTaskTypes.Unknown => string.Empty,
            AiVisionTaskTypes.AbstractGoal => string.Empty,
            var value => value
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values
            .Select(Clean)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
