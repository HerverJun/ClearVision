using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record VisionAgentTaskTypeEvidence(
    string Source,
    string RawValue,
    string CanonicalValue,
    bool ExplicitUserChoice);

internal sealed record VisionAgentTaskTypeResolution(
    string CanonicalValue,
    bool BlockingConflict,
    bool ObservedConflict,
    bool ExplicitOverride,
    IReadOnlyList<VisionAgentTaskTypeEvidence> Evidence,
    IReadOnlyList<string> Warnings);

internal static class VisionAgentTaskTypeResolver
{
    public static VisionAgentTaskTypeResolution Resolve(
        VisionAgentPlanModeResult? plan,
        IEnumerable<VisionAgentPlanAnswer>? answers)
    {
        var evidence = new List<VisionAgentTaskTypeEvidence>();
        Add(evidence, "semantic", plan?.SemanticExtraction?.TaskType, explicitUserChoice: false);
        Add(evidence, "maturity", plan?.RequirementMaturity?.TaskType, explicitUserChoice: false);

        var warnings = new List<string>();
        foreach (var answer in answers ?? [])
        {
            if (!string.Equals(
                    VisionAgentPlanFieldPolicy.NormalizeField(answer.Field),
                    VisionAgentPlanAnswerFields.TaskType,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var origin = answer.Origin?.Trim().ToLowerInvariant() ?? string.Empty;
            var explicitChoice = origin is VisionAgentPlanAnswerOrigins.ExplicitUserSelection or
                VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault;
            if (origin == VisionAgentPlanAnswerOrigins.ExplicitUserText)
            {
                explicitChoice = !string.IsNullOrWhiteSpace(answer.EvidenceText);
                if (!explicitChoice)
                {
                    warnings.Add("explicit_user_text_missing_evidence:task_type");
                }
            }

            Add(evidence, origin, answer.Value, explicitChoice);
        }

        var canonicalValues = evidence
            .Select(item => item.CanonicalValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitValues = evidence
            .Where(item => item.ExplicitUserChoice)
            .Select(item => item.CanonicalValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (explicitValues.Count > 1)
        {
            warnings.Add("task_type_conflict");
            return Result(string.Empty, blocking: true, observed: true, explicitOverride: false, evidence: evidence, warnings: warnings);
        }

        if (explicitValues.Count == 1)
        {
            var observedConflict = canonicalValues.Count > 1;
            if (observedConflict)
            {
                warnings.Add("task_type_conflict_overridden_by_explicit_user");
            }
            return Result(
                explicitValues[0],
                blocking: false,
                observed: observedConflict,
                explicitOverride: observedConflict,
                evidence: evidence,
                warnings: warnings);
        }

        if (canonicalValues.Count > 1)
        {
            warnings.Add("task_type_conflict");
            return Result(string.Empty, blocking: true, observed: true, explicitOverride: false, evidence: evidence, warnings: warnings);
        }

        return Result(
            canonicalValues.FirstOrDefault() ?? string.Empty,
            blocking: false,
            observed: false,
            explicitOverride: false,
            evidence: evidence,
            warnings: warnings);
    }

    private static void Add(
        ICollection<VisionAgentTaskTypeEvidence> evidence,
        string source,
        string? rawValue,
        bool explicitUserChoice)
    {
        var raw = rawValue?.Trim() ?? string.Empty;
        if (!AiVisionTaskCatalog.TryNormalizePrimary(raw, out var canonical))
        {
            return;
        }

        evidence.Add(new VisionAgentTaskTypeEvidence(
            source,
            raw,
            canonical,
            explicitUserChoice));
    }

    private static VisionAgentTaskTypeResolution Result(
        string canonical,
        bool blocking,
        bool observed,
        bool explicitOverride,
        IReadOnlyList<VisionAgentTaskTypeEvidence> evidence,
        IReadOnlyList<string> warnings) =>
        new(
            canonical,
            blocking,
            observed,
            explicitOverride,
            evidence,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
}
