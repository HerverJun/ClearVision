using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Core.Services;

public static class InspectionOutcomeResolver
{
    public static InspectionOutcome Resolve(FlowExecutionResult flowResult)
    {
        return ResolveCore(flowResult, flow: null, allowLegacyHeuristic: false);
    }

    public static InspectionOutcome Resolve(FlowExecutionResult flowResult, OperatorFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return ResolveCore(flowResult, flow, allowLegacyHeuristic: false);
    }

    public static InspectionOutcome ResolvePreview(
        FlowExecutionResult flowResult,
        OperatorFlow? flow,
        bool allowLegacyHeuristic = true)
    {
        return ResolveCore(flowResult, flow, allowLegacyHeuristic);
    }

    private static InspectionOutcome ResolveCore(
        FlowExecutionResult flowResult,
        OperatorFlow? flow,
        bool allowLegacyHeuristic)
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

        var evaluation = flow?.DecisionConfiguration?.FinalDecisionBinding != null
            ? FinalDecisionResolver.Resolve(flow, flowResult)
            : allowLegacyHeuristic
                ? InspectionJudgmentResolver.DetermineDecisionFromLegacyHeuristic(flowResult.OutputData)
                : new InspectionDecisionEvaluation(
                    DecisionOutcome.Undetermined,
                    "None",
                    "MissingDecisionConfiguration",
                    null,
                    HasJudgmentSignal: false);
        return new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            evaluation.Decision,
            evaluation.DecisionSource,
            evaluation.ReasonCode,
            evaluation.Message,
            evaluation.HasJudgmentSignal);
    }

    public static void SetDiagnostics(Dictionary<string, object> outputData, InspectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outputData);
        outputData["MissingJudgmentSignal"] = !outcome.HasJudgmentSignal;
        outputData["HasJudgmentSignal"] = outcome.HasJudgmentSignal;
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
