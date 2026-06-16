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

        var semantic = BuildSemantic(plan?.SemanticExtraction, values, maturityRequest.RequirementMode);
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(maturityRequest, semantic);
        var resolved = values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value) &&
                           VisionAgentPlanFieldPolicy.TryGet(item.Key, out var rule) &&
                           rule.Category == VisionAgentPlanFieldCategories.Requirement)
            .Select(item => item.Key)
            .Concat(plan?.ResolvedPlanFields ?? [])
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
        var acceptance = VisionAgentPlanFieldPolicy.FormatAcceptanceCriteria(semantic?.OkCondition, semantic?.NgCondition);
        Add(values, VisionAgentPlanAnswerFields.AcceptanceCriteria, acceptance);
        return values;
    }

    private static VisionAgentSemanticExtractionResult BuildSemantic(
        VisionAgentSemanticExtractionResult? semantic,
        IReadOnlyDictionary<string, string> values,
        string requirementMode)
    {
        var taskType = Read(values, VisionAgentPlanAnswerFields.TaskType);
        var inspectionObject = Read(values, VisionAgentPlanAnswerFields.InspectionObject);
        var targetAttribute = Read(values, VisionAgentPlanAnswerFields.TargetAttribute);
        var defectType = Read(values, VisionAgentPlanAnswerFields.DefectType);
        var measurementTarget = Read(values, VisionAgentPlanAnswerFields.MeasurementTarget);
        var imageSource = Read(values, VisionAgentPlanAnswerFields.ImageSource);
        var outputTarget = Read(values, VisionAgentPlanAnswerFields.OutputTarget);
        var acceptance = Read(values, VisionAgentPlanAnswerFields.AcceptanceCriteria);
        var hasObjectOrTask = !string.IsNullOrWhiteSpace(inspectionObject) ||
                              (!string.IsNullOrWhiteSpace(taskType) &&
                               !string.Equals(taskType, AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(taskType, AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase));
        var canPlanCandidate = semantic?.CanPlanCandidate == true;
        if (string.Equals(requirementMode, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) &&
            !hasObjectOrTask)
        {
            canPlanCandidate = false;
        }

        var parsed = VisionAgentPlanFieldPolicy.ParseAcceptanceCriteria(acceptance);
        var okCondition = !string.IsNullOrWhiteSpace(parsed.Ok) ? parsed.Ok : (semantic?.OkCondition ?? string.Empty);
        var ngCondition = !string.IsNullOrWhiteSpace(parsed.Ng) ? parsed.Ng : (semantic?.NgCondition ?? string.Empty);

        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = semantic?.IsVisionRequest ?? true,
            Intent = FirstNonEmpty(semantic?.Intent, "new_flow"),
            TaskType = string.IsNullOrWhiteSpace(taskType) ? AiVisionTaskTypes.Unknown : taskType,
            Confidence = semantic?.Confidence ?? 0,
            TaskTypeConfidence = semantic?.TaskTypeConfidence ?? 0,
            InspectionObject = inspectionObject,
            TargetAttribute = targetAttribute,
            DefectType = defectType,
            MeasurementTarget = measurementTarget,
            ImageSource = imageSource,
            OkCondition = okCondition,
            NgCondition = ngCondition,
            OutputTarget = outputTarget,
            SuggestedRoute = semantic?.SuggestedRoute ?? string.Empty,
            CanPlanCandidate = canPlanCandidate,
            CanBuildCandidate = semantic?.CanBuildCandidate == true,
            ObjectSignals = MergeSignals(semantic?.ObjectSignals, inspectionObject),
            TaskSignals = MergeSignals(semantic?.TaskSignals, FirstNonEmpty(targetAttribute, defectType, measurementTarget, taskType, acceptance)),
            MissingFields = [],
            ClarificationQuestions = [],
            Source = semantic?.Source ?? VisionAgentSemanticSources.RuleFallback,
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

    private static List<string> MergeSignals(IEnumerable<string>? existing, string? value)
    {
        var text = Clean(value);
        return (existing ?? [])
            .Concat(string.IsNullOrWhiteSpace(text) ? [] : [text])
            .Select(Clean)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
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
