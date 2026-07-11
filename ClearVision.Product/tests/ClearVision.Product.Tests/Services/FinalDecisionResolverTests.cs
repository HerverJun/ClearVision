using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class FinalDecisionResolverTests
{
    [Fact]
    public void Resolve_UsesOnlyBoundOperatorOutput_AndIgnoresRecursiveLegacyFields()
    {
        var flow = new OperatorFlow("explicit-decision");
        var bound = new Operator(Guid.NewGuid(), "Bound", OperatorType.ResultOutput, 0, 0);
        var unrelated = new Operator(Guid.NewGuid(), "Unrelated", OperatorType.ResultJudgment, 0, 0);
        flow.AddOperator(bound);
        flow.AddOperator(unrelated);
        flow.BindStringDecision(bound);

        var result = new FlowExecutionResult
        {
            IsSuccess = true,
            OutputData = new Dictionary<string, object>
            {
                ["Diagnostics"] = new Dictionary<string, object> { ["IsOk"] = false }
            },
            OperatorResults =
            [
                new OperatorExecutionResult
                {
                    OperatorId = unrelated.Id,
                    IsSuccess = true,
                    OutputData = new Dictionary<string, object> { ["IsOk"] = false }
                },
                new OperatorExecutionResult
                {
                    OperatorId = bound.Id,
                    IsSuccess = true,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                }
            ]
        };

        var outcome = InspectionOutcomeResolver.Resolve(result, flow);

        outcome.Execution.Should().Be(ExecutionOutcome.Succeeded);
        outcome.Decision.Should().Be(DecisionOutcome.Ok);
        outcome.DecisionSource.Should().StartWith("FinalDecisionBinding:");
        outcome.HasJudgmentSignal.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenBoundSignalMissing_ReturnsConfiguredUndetermined()
    {
        var flow = new OperatorFlow("missing-signal");
        var op = new Operator(Guid.NewGuid(), "Decision", OperatorType.ResultOutput, 0, 0);
        flow.AddOperator(op);
        flow.BindStringDecision(op);

        var outcome = InspectionOutcomeResolver.Resolve(
            new FlowExecutionResult
            {
                IsSuccess = true,
                OperatorResults =
                [
                    new OperatorExecutionResult
                    {
                        OperatorId = op.Id,
                        IsSuccess = true,
                        OutputData = new Dictionary<string, object> { ["IsOk"] = true }
                    }
                ]
            },
            flow);

        outcome.Execution.Should().Be(ExecutionOutcome.Succeeded);
        outcome.Decision.Should().Be(DecisionOutcome.Undetermined);
        outcome.ReasonCode.Should().Be("DECISION_SIGNAL_MISSING");
        outcome.HasJudgmentSignal.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WhenBindingIsInvalid_ReturnsSucceededInvalid()
    {
        var flow = new OperatorFlow("invalid-binding")
        {
            DecisionConfiguration = new DecisionConfiguration
            {
                FinalDecisionBinding = new FinalDecisionBinding
                {
                    SourceOperatorId = Guid.NewGuid(),
                    SourceOutputName = "IsOk",
                    DataType = DecisionValueType.Boolean,
                    Rule = DecisionInterpretationRule.Boolean
                }
            }
        };
        flow.AddOperator(new Operator(Guid.NewGuid(), "Other", OperatorType.ResultOutput, 0, 0));

        var outcome = InspectionOutcomeResolver.Resolve(
            new FlowExecutionResult { IsSuccess = true },
            flow);

        outcome.Execution.Should().Be(ExecutionOutcome.Succeeded);
        outcome.Decision.Should().Be(DecisionOutcome.Invalid);
        outcome.ReasonCode.Should().Be("DECISION_SOURCE_OPERATOR_NOT_FOUND");
        outcome.HasJudgmentSignal.Should().BeFalse();
    }
}
