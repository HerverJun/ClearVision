namespace ClearVision.Product.Core.Outcomes;

public enum CanonicalInspectionOutcomeKind
{
    Ok,
    Ng,
    Undetermined,
    NotApplicable,
    Invalid,
    Failed,
    Cancelled,
    TimedOut,
    Skipped
}

public sealed class InspectionOutcomeStatistics
{
    public int TotalAttemptCount { get; init; }
    public int ExecutionSucceededCount { get; init; }
    public int ValidDecisionCount { get; init; }
    public int OkCount { get; init; }
    public int NgCount { get; init; }
    public int UndeterminedCount { get; init; }
    public int NotApplicableCount { get; init; }
    public int InvalidCount { get; init; }
    public int FailedCount { get; init; }
    public int CancelledCount { get; init; }
    public int TimedOutCount { get; init; }
    public int SkippedCount { get; init; }
    public int ExecutionFailureCount => FailedCount + TimedOutCount;
    public double YieldRate => ValidDecisionCount > 0 ? OkCount / (double)ValidDecisionCount : 0;
    public double DecisionCoverageRate => ExecutionSucceededCount > 0
        ? ValidDecisionCount / (double)ExecutionSucceededCount
        : 0;

    public static InspectionOutcomeStatistics Calculate(IEnumerable<InspectionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var total = 0;
        var succeeded = 0;
        var ok = 0;
        var ng = 0;
        var undetermined = 0;
        var notApplicable = 0;
        var invalid = 0;
        var failed = 0;
        var cancelled = 0;
        var timedOut = 0;
        var skipped = 0;

        foreach (var outcome in outcomes)
        {
            total++;
            if (outcome.Execution == ExecutionOutcome.Succeeded)
            {
                succeeded++;
            }

            switch (InspectionOutcomeClassifier.Classify(outcome))
            {
                case CanonicalInspectionOutcomeKind.Ok:
                    ok++;
                    break;
                case CanonicalInspectionOutcomeKind.Ng:
                    ng++;
                    break;
                case CanonicalInspectionOutcomeKind.Undetermined:
                    undetermined++;
                    break;
                case CanonicalInspectionOutcomeKind.NotApplicable:
                    notApplicable++;
                    break;
                case CanonicalInspectionOutcomeKind.Invalid:
                    invalid++;
                    break;
                case CanonicalInspectionOutcomeKind.Failed:
                    failed++;
                    break;
                case CanonicalInspectionOutcomeKind.Cancelled:
                    cancelled++;
                    break;
                case CanonicalInspectionOutcomeKind.TimedOut:
                    timedOut++;
                    break;
                case CanonicalInspectionOutcomeKind.Skipped:
                    skipped++;
                    break;
            }
        }

        return new InspectionOutcomeStatistics
        {
            TotalAttemptCount = total,
            ExecutionSucceededCount = succeeded,
            ValidDecisionCount = ok + ng,
            OkCount = ok,
            NgCount = ng,
            UndeterminedCount = undetermined,
            NotApplicableCount = notApplicable,
            InvalidCount = invalid,
            FailedCount = failed,
            CancelledCount = cancelled,
            TimedOutCount = timedOut,
            SkippedCount = skipped
        };
    }
}

public static class InspectionOutcomeClassifier
{
    public static CanonicalInspectionOutcomeKind Classify(InspectionOutcome outcome)
    {
        return outcome.Execution switch
        {
            ExecutionOutcome.Failed => CanonicalInspectionOutcomeKind.Failed,
            ExecutionOutcome.Cancelled => CanonicalInspectionOutcomeKind.Cancelled,
            ExecutionOutcome.TimedOut => CanonicalInspectionOutcomeKind.TimedOut,
            ExecutionOutcome.Skipped => CanonicalInspectionOutcomeKind.Skipped,
            ExecutionOutcome.Succeeded => outcome.Decision switch
            {
                DecisionOutcome.Ok => CanonicalInspectionOutcomeKind.Ok,
                DecisionOutcome.Ng => CanonicalInspectionOutcomeKind.Ng,
                DecisionOutcome.Undetermined => CanonicalInspectionOutcomeKind.Undetermined,
                DecisionOutcome.NotApplicable => CanonicalInspectionOutcomeKind.NotApplicable,
                DecisionOutcome.Invalid => CanonicalInspectionOutcomeKind.Invalid,
                _ => CanonicalInspectionOutcomeKind.Invalid
            },
            _ => CanonicalInspectionOutcomeKind.Invalid
        };
    }
}
