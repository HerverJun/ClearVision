using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class FinalDecisionResolverTests
{
    [Fact]
    public void Resolve_UsesOnlyBoundOperatorOutput_AndIgnoresRecursiveLegacyFields()
    {
        var flow = new OperatorFlow("explicit-decision");
        var bound = new Operator(Guid.NewGuid(), "Bound", OperatorType.ResultJudgment, 0, 0);
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
        var op = new Operator(Guid.NewGuid(), "Decision", OperatorType.ResultJudgment, 0, 0);
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
    public void ConfigurationCatalog_ReturnsOnlyOutputsDeclaredByBackendDecisionContracts()
    {
        var flow = new OperatorFlow("catalog");
        var resultOutput = new Operator(Guid.NewGuid(), "Result Output", OperatorType.ResultOutput, 0, 0);
        resultOutput.AddOutputPort("Text", PortDataType.String);
        resultOutput.AddOutputPort("FilePath", PortDataType.String);
        var judgment = new Operator(Guid.NewGuid(), "Judgment", OperatorType.ResultJudgment, 0, 0);
        judgment.AddOutputPort("JudgmentResult", PortDataType.String);
        judgment.AddOutputPort("IsOk", PortDataType.Boolean);
        judgment.AddOutputPort("Details", PortDataType.String);
        var blob = new Operator(Guid.NewGuid(), "Blob", OperatorType.BlobAnalysis, 0, 0);
        blob.AddOutputPort("BlobCount", PortDataType.Integer);
        var forgedBlob = new Operator(Guid.NewGuid(), "Forged Blob", OperatorType.BlobAnalysis, 0, 0);
        forgedBlob.AddOutputPort("BlobCount", PortDataType.String);
        var disabled = new Operator(Guid.NewGuid(), "Disabled", OperatorType.ResultJudgment, 0, 0);
        disabled.AddOutputPort("IsOk", PortDataType.Boolean);
        disabled.Disable();
        flow.AddOperator(resultOutput);
        flow.AddOperator(judgment);
        flow.AddOperator(blob);
        flow.AddOperator(forgedBlob);
        flow.AddOperator(disabled);

        var candidates = FinalDecisionConfigurationCatalog.GetEligibleOutputs(flow);

        candidates.Select(candidate => candidate.OutputName)
            .Should().BeEquivalentTo("JudgmentResult", "IsOk", "BlobCount");
        candidates.Should().NotContain(candidate => candidate.OperatorId == resultOutput.Id);
        candidates.Should().NotContain(candidate => candidate.OperatorId == forgedBlob.Id);
        candidates.Should().NotContain(candidate => candidate.OperatorId == disabled.Id);
    }

    [Fact]
    public void DeclaredDecisionSourceCapabilities_ShouldMatchOperatorMetadata()
    {
        var metadata = new OperatorMetadataScanner().Scan()
            .ToDictionary(item => item.Type);

        foreach (var capability in FinalDecisionConfigurationCatalog.GetDeclaredCapabilities())
        {
            metadata.Should().ContainKey(capability.OperatorType);
            metadata[capability.OperatorType].OutputPorts.Should().ContainSingle(port =>
                port.Name.Equals(capability.OutputName, StringComparison.OrdinalIgnoreCase) &&
                port.DataType == capability.PortType);
        }
    }

    [Fact]
    public void Validate_ResultOutputTextStringMap_IsStaticallyIneligible()
    {
        var flow = new OperatorFlow("formatted-output");
        var output = new Operator(Guid.NewGuid(), "Result Output", OperatorType.ResultOutput, 0, 0);
        output.AddOutputPort("Text", PortDataType.String);
        flow.AddOperator(output);
        var port = output.OutputPorts.Single();
        flow.DecisionConfiguration = new DecisionConfiguration
        {
            FinalDecisionBinding = new FinalDecisionBinding
            {
                SourceOperatorId = output.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = DecisionValueType.String,
                Rule = DecisionInterpretationRule.StringMap,
                OkValue = "OK",
                NgValue = "NG"
            }
        };

        FinalDecisionResolver.Validate(flow)
            .Should().ContainSingle(issue => issue.Code == "DECISION_SOURCE_OUTPUT_INELIGIBLE");
    }

    [Theory]
    [InlineData("OK", DecisionOutcome.Ok, "DECISION_BOUND_VALUE_RESOLVED")]
    [InlineData("NG", DecisionOutcome.Ng, "DECISION_BOUND_VALUE_RESOLVED")]
    [InlineData("MAYBE", DecisionOutcome.Invalid, "DECISION_STRING_VALUE_UNMAPPED")]
    public void Resolve_LegalStringDecision_PreservesCanonicalMapping(
        string value,
        DecisionOutcome expected,
        string reasonCode)
    {
        var flow = new OperatorFlow("string-decision");
        var judgment = new Operator(Guid.NewGuid(), "Judgment", OperatorType.ResultJudgment, 0, 0);
        flow.AddOperator(judgment);
        flow.BindStringDecision(judgment);

        var evaluation = FinalDecisionResolver.Resolve(
            flow,
            ResultFor(judgment, "JudgmentResult", value));

        evaluation.Decision.Should().Be(expected);
        evaluation.ReasonCode.Should().Be(reasonCode);
    }

    [Fact]
    public void Resolve_UnmappedLongString_UsesBoundedDiagnosticSummary()
    {
        var flow = new OperatorFlow("long-string");
        var judgment = new Operator(Guid.NewGuid(), "Judgment", OperatorType.ResultJudgment, 0, 0);
        flow.AddOperator(judgment);
        flow.BindStringDecision(judgment);
        var json = "{\"detections\":[" + new string('x', 4000) + "]}";

        var evaluation = FinalDecisionResolver.Resolve(
            flow,
            ResultFor(judgment, "JudgmentResult", json));

        evaluation.Decision.Should().Be(DecisionOutcome.Invalid);
        evaluation.ReasonCode.Should().Be("DECISION_STRING_VALUE_UNMAPPED");
        evaluation.Message.Should().Contain("length");
        evaluation.Message!.Length.Should().BeLessThan(260);
        evaluation.Message.Should().NotContain(new string('x', 500));
    }

    [Theory]
    [InlineData(true, true, DecisionOutcome.Ok)]
    [InlineData(false, true, DecisionOutcome.Ng)]
    [InlineData(true, false, DecisionOutcome.Ng)]
    public void Resolve_LegalBooleanDecision_UsesConfiguredPolarity(
        bool value,
        bool trueMeansOk,
        DecisionOutcome expected)
    {
        var flow = new OperatorFlow("boolean-decision");
        var judgment = new Operator(Guid.NewGuid(), "Judgment", OperatorType.ResultJudgment, 0, 0);
        flow.AddOperator(judgment);
        flow.BindBooleanDecision(judgment, trueMeansOk: trueMeansOk);

        FinalDecisionResolver.Resolve(flow, ResultFor(judgment, "IsOk", value))
            .Decision.Should().Be(expected);
    }

    [Theory]
    [InlineData(4, DecisionComparator.GreaterThanOrEqual, 3, DecisionOutcome.Ok)]
    [InlineData(2, DecisionComparator.GreaterThanOrEqual, 3, DecisionOutcome.Ng)]
    [InlineData(2, DecisionComparator.LessThan, 3, DecisionOutcome.Ok)]
    public void Resolve_LegalNumericMeasurement_UsesConfiguredComparison(
        int value,
        DecisionComparator comparator,
        double threshold,
        DecisionOutcome expected)
    {
        var flow = new OperatorFlow("numeric-decision");
        var blob = new Operator(Guid.NewGuid(), "Blob", OperatorType.BlobAnalysis, 0, 0);
        blob.AddOutputPort("BlobCount", PortDataType.Integer);
        flow.AddOperator(blob);
        var port = blob.OutputPorts.Single();
        flow.DecisionConfiguration = new DecisionConfiguration
        {
            FinalDecisionBinding = new FinalDecisionBinding
            {
                SourceOperatorId = blob.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = DecisionValueType.Integer,
                Rule = DecisionInterpretationRule.NumericComparison,
                Comparator = comparator,
                Threshold = threshold
            }
        };

        FinalDecisionResolver.Resolve(flow, ResultFor(blob, "BlobCount", value))
            .Decision.Should().Be(expected);
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

    private static FlowExecutionResult ResultFor(Operator source, string outputName, object value) =>
        new()
        {
            IsSuccess = true,
            OperatorResults =
            [
                new OperatorExecutionResult
                {
                    OperatorId = source.Id,
                    IsSuccess = true,
                    OutputData = new Dictionary<string, object> { [outputName] = value }
                }
            ]
        };
}
