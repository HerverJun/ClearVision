using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Core.Continuous;

public readonly record struct ContinuousInspectionOutcomePolicyResult(
    int ConsecutiveNgCount,
    bool IsExecutionFailure,
    bool ShouldUseNormalInterval,
    bool IsNgStopCandidate);

public static class ContinuousInspectionOutcomePolicy
{
    public static ContinuousInspectionOutcomePolicyResult Evaluate(
        int currentConsecutiveNgCount,
        InspectionOutcome outcome)
    {
        var consecutiveNgCount = outcome.Execution == ExecutionOutcome.Succeeded
            ? outcome.Decision switch
            {
                DecisionOutcome.Ng => currentConsecutiveNgCount + 1,
                DecisionOutcome.Ok => 0,
                _ => currentConsecutiveNgCount
            }
            : currentConsecutiveNgCount;

        var isExecutionFailure = outcome.Execution is ExecutionOutcome.Failed or ExecutionOutcome.TimedOut;
        return new ContinuousInspectionOutcomePolicyResult(
            consecutiveNgCount,
            isExecutionFailure,
            ShouldUseNormalInterval: !isExecutionFailure,
            IsNgStopCandidate: outcome.Execution == ExecutionOutcome.Succeeded && outcome.Decision == DecisionOutcome.Ng);
    }
}
