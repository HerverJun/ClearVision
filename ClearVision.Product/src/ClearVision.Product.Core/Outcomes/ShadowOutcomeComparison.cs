namespace ClearVision.Product.Core.Outcomes;

public readonly record struct ShadowOutcomeComparisonResult(
    bool Comparable,
    string ComparisonReason,
    bool? Matched);

public static class ShadowOutcomeComparison
{
    public static ShadowOutcomeComparisonResult Evaluate(
        InspectionOutcome baseline,
        InspectionOutcome candidate)
    {
        var comparable = IsComparable(baseline) && IsComparable(candidate);
        return comparable
            ? new ShadowOutcomeComparisonResult(
                true,
                "ComparableFinalDecisions",
                baseline.Decision == candidate.Decision)
            : new ShadowOutcomeComparisonResult(false, "NonComparableOutcome", null);
    }

    private static bool IsComparable(InspectionOutcome outcome) =>
        outcome.Execution == ExecutionOutcome.Succeeded &&
        outcome.Decision is DecisionOutcome.Ok or DecisionOutcome.Ng;
}
