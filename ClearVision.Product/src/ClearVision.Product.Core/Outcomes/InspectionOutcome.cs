using System.Text.Json.Serialization;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Outcomes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionOutcome
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Skipped
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionOutcome
{
    Ok,
    Ng,
    Undetermined,
    NotApplicable,
    Invalid
}

public readonly record struct InspectionOutcome(
    ExecutionOutcome Execution,
    DecisionOutcome Decision,
    string? DecisionSource,
    string? ReasonCode,
    string? Message,
    bool HasJudgmentSignal = false);

public static class LegacyInspectionStatusProjection
{
    public static InspectionStatus Project(InspectionOutcome outcome) =>
        Project(outcome.Execution, outcome.Decision);

    public static InspectionStatus Project(ExecutionOutcome execution, DecisionOutcome decision)
    {
        return execution switch
        {
            ExecutionOutcome.Failed or ExecutionOutcome.TimedOut => InspectionStatus.Error,
            ExecutionOutcome.Cancelled or ExecutionOutcome.Skipped => InspectionStatus.NotInspected,
            ExecutionOutcome.Succeeded => decision switch
            {
                DecisionOutcome.Ok => InspectionStatus.OK,
                DecisionOutcome.Ng => InspectionStatus.NG,
                DecisionOutcome.Undetermined or DecisionOutcome.NotApplicable => InspectionStatus.NotInspected,
                DecisionOutcome.Invalid => InspectionStatus.Error,
                _ => InspectionStatus.Error
            },
            _ => InspectionStatus.Error
        };
    }

    public static InspectionOutcome FromLegacy(InspectionStatus status)
    {
        return status switch
        {
            InspectionStatus.OK => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Ok),
            InspectionStatus.NG => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Ng),
            InspectionStatus.Error => Legacy(ExecutionOutcome.Failed, DecisionOutcome.Undetermined),
            InspectionStatus.NotInspected => Legacy(ExecutionOutcome.Skipped, DecisionOutcome.NotApplicable),
            InspectionStatus.Inspecting => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined),
            _ => Legacy(ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined)
        };
    }

    private static InspectionOutcome Legacy(ExecutionOutcome execution, DecisionOutcome decision) =>
        new(
            execution,
            decision,
            "LegacyInspectionStatus",
            "LegacyInspectionStatusProjection",
            null,
            execution == ExecutionOutcome.Succeeded && decision is DecisionOutcome.Ok or DecisionOutcome.Ng);
}
