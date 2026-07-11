using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Runtime.Abstractions;

/// <summary>
/// Resolves Station result payloads into the canonical inspection outcome without
/// mutating legacy payload fields. Missing v2 fields are projected only at read time.
/// </summary>
public static class StationCanonicalOutcomeProjection
{
    public static InspectionOutcome Resolve(StationResultSummaryDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.ExecutionOutcome.HasValue && result.DecisionOutcome.HasValue)
        {
            var execution = result.ExecutionOutcome.Value;
            var decision = result.DecisionOutcome.Value;
            return new InspectionOutcome(
                execution,
                decision,
                result.DecisionSource,
                result.ReasonCode,
                result.DiagnosticMessage,
                result.HasJudgmentSignal ?? HasDecisionSignal(execution, decision));
        }

        return ProjectLegacy(result);
    }

    public static RuntimeRunOutcome ProjectRuntimeOutcome(InspectionOutcome outcome)
    {
        return outcome.Execution switch
        {
            ExecutionOutcome.Cancelled => RuntimeRunOutcome.Canceled,
            ExecutionOutcome.Failed or ExecutionOutcome.TimedOut => RuntimeRunOutcome.Error,
            ExecutionOutcome.Skipped => RuntimeRunOutcome.Undetermined,
            ExecutionOutcome.Succeeded when outcome.Decision == DecisionOutcome.Ok => RuntimeRunOutcome.Ok,
            ExecutionOutcome.Succeeded when outcome.Decision == DecisionOutcome.Ng => RuntimeRunOutcome.Ng,
            ExecutionOutcome.Succeeded when outcome.Decision == DecisionOutcome.Invalid => RuntimeRunOutcome.Error,
            _ => RuntimeRunOutcome.Undetermined
        };
    }

    private static InspectionOutcome ProjectLegacy(StationResultSummaryDto result)
    {
        var outcome = result.Outcome switch
        {
            RuntimeRunOutcome.Ok => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Ok),
            RuntimeRunOutcome.Ng => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Ng),
            RuntimeRunOutcome.Error => Legacy(ExecutionOutcome.Failed, DecisionOutcome.Undetermined),
            RuntimeRunOutcome.Canceled => Legacy(ExecutionOutcome.Cancelled, DecisionOutcome.NotApplicable),
            RuntimeRunOutcome.Undetermined => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined),
            _ => result.InspectionStatus.HasValue
                ? LegacyInspectionStatusProjection.FromLegacy(result.InspectionStatus.Value)
                : Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined)
        };

        return outcome with
        {
            DecisionSource = "LegacyStationResult",
            ReasonCode = "LegacyStationOutcomeProjection",
            Message = result.DiagnosticMessage
        };
    }

    private static InspectionOutcome Legacy(ExecutionOutcome execution, DecisionOutcome decision)
    {
        return new InspectionOutcome(
            execution,
            decision,
            "LegacyStationResult",
            "LegacyStationOutcomeProjection",
            null,
            HasDecisionSignal(execution, decision));
    }

    private static bool HasDecisionSignal(ExecutionOutcome execution, DecisionOutcome decision)
    {
        return execution == ExecutionOutcome.Succeeded &&
               decision is DecisionOutcome.Ok or DecisionOutcome.Ng;
    }
}
