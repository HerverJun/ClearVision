using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Core.Services;

public static class InspectionOutcomeResolver
{
    public static InspectionOutcome Resolve(FlowExecutionResult flowResult)
    {
        ArgumentNullException.ThrowIfNull(flowResult);

        if (flowResult.WasShortCircuited)
        {
            return new InspectionOutcome(
                ExecutionOutcome.Skipped,
                DecisionOutcome.NotApplicable,
                "FlowExecution",
                "NoMaterialFrame",
                null);
        }

        if (!flowResult.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(flowResult.ErrorMessage)
                ? "Flow execution failed."
                : flowResult.ErrorMessage;
            return new InspectionOutcome(
                ExecutionOutcome.Failed,
                DecisionOutcome.Undetermined,
                "FlowExecution",
                "FlowExecutionFailed",
                message);
        }

        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromFlowOutput(flowResult.OutputData);
        return new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            evaluation.Decision,
            evaluation.DecisionSource,
            evaluation.ReasonCode,
            evaluation.Message);
    }

    public static void SetDiagnostics(Dictionary<string, object> outputData, InspectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outputData);
        outputData["MissingJudgmentSignal"] = outcome.ReasonCode == "MissingJudgmentSignal";
        outputData["JudgmentSource"] = outcome.DecisionSource ?? "None";
        outputData["StatusReason"] = outcome.ReasonCode ?? string.Empty;
        outputData["ExecutionOutcome"] = outcome.Execution.ToString();
        outputData["DecisionOutcome"] = outcome.Decision.ToString();
        if (!string.IsNullOrWhiteSpace(outcome.Message))
        {
            outputData["JudgmentMessage"] = outcome.Message;
        }
    }
}
