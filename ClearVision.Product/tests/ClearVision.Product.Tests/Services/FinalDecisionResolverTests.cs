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

    [Fact]
    public void ConfigurationCatalog_ReturnsOnlySimpleCompatibleOutputs()
    {
        var flow = new OperatorFlow("catalog");
        var enabled = new Operator(Guid.NewGuid(), "Enabled", OperatorType.ResultOutput, 0, 0);
        enabled.AddOutputPort("Bool", PortDataType.Boolean);
        enabled.AddOutputPort("Text", PortDataType.String);
        enabled.AddOutputPort("Count", PortDataType.Integer);
        enabled.AddOutputPort("Score", PortDataType.Float);
        enabled.AddOutputPort("Image", PortDataType.Image);
        enabled.AddOutputPort("Payload", PortDataType.Any);
        var disabled = new Operator(Guid.NewGuid(), "Disabled", OperatorType.ResultOutput, 0, 0);
        disabled.AddOutputPort("Decision", PortDataType.Boolean);
        disabled.Disable();
        flow.AddOperator(enabled);
        flow.AddOperator(disabled);

        var candidates = FinalDecisionConfigurationCatalog.GetEligibleOutputs(flow);

        candidates.Select(candidate => candidate.OutputName)
            .Should().BeEquivalentTo("Bool", "Text", "Count", "Score");
        candidates.Should().OnlyContain(candidate => candidate.OperatorId == enabled.Id);
    }

    [Fact]
    public void Validate_RejectsAmbiguousAnyOutput()
    {
        var flow = new OperatorFlow("any-output");
        var op = new Operator(Guid.NewGuid(), "Any", OperatorType.ResultOutput, 0, 0);
        op.AddOutputPort("Payload", PortDataType.Any);
        flow.AddOperator(op);
        var port = op.OutputPorts.Single();
        flow.DecisionConfiguration = new DecisionConfiguration
        {
            FinalDecisionBinding = new FinalDecisionBinding
            {
                SourceOperatorId = op.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = DecisionValueType.String,
                Rule = DecisionInterpretationRule.StringMap,
                OkValue = "OK",
                NgValue = "NG"
            }
        };

        FinalDecisionResolver.Validate(flow)
            .Should().ContainSingle(issue => issue.Code == "DECISION_SOURCE_TYPE_MISMATCH");
    }
}
