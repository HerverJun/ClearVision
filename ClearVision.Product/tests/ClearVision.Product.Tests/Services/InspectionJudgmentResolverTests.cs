using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public class InspectionJudgmentResolverTests
{
    [Theory]
    [InlineData("OK", DecisionOutcome.Ok)]
    [InlineData("Pass", DecisionOutcome.Ok)]
    [InlineData("Passed", DecisionOutcome.Ok)]
    [InlineData("NG", DecisionOutcome.Ng)]
    [InlineData("Fail", DecisionOutcome.Ng)]
    [InlineData("Failed", DecisionOutcome.Ng)]
    [InlineData("Unknown", DecisionOutcome.Undetermined)]
    [InlineData("Pending", DecisionOutcome.Undetermined)]
    [InlineData("Skipped", DecisionOutcome.NotApplicable)]
    [InlineData("NotApplicable", DecisionOutcome.NotApplicable)]
    [InlineData("Error", DecisionOutcome.Invalid)]
    [InlineData("surprise", DecisionOutcome.Invalid)]
    public void DetermineDecisionFromFlowOutput_MapsExplicitJudgmentKeywords(
        string value,
        DecisionOutcome expected)
    {
        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromFlowOutput(
            new Dictionary<string, object> { ["JudgmentResult"] = value });

        evaluation.Decision.Should().Be(expected);
        evaluation.DecisionSource.Should().Be("JudgmentResult");
        evaluation.MissingJudgmentSignal.Should().BeFalse();
    }

    [Fact]
    public void DetermineDecisionFromFlowOutput_WhenAcceptedIsFalse_ReturnsNg()
    {
        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromFlowOutput(
            new Dictionary<string, object> { ["Accepted"] = false });

        evaluation.Decision.Should().Be(DecisionOutcome.Ng);
        evaluation.DecisionSource.Should().Be("Accepted");
        evaluation.ReasonCode.Should().Be("DerivedFromAccepted");
    }

    [Fact]
    public void DetermineDecisionFromFlowOutput_PreservesLegacyRecursiveScanForPromptOne()
    {
        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromFlowOutput(new Dictionary<string, object>
        {
            ["Diagnostics"] = new Dictionary<string, object> { ["HueValid"] = true }
        });

        evaluation.Decision.Should().Be(DecisionOutcome.Ok);
        evaluation.DecisionSource.Should().Be("Diagnostics.HueValid");
    }

    [Fact]
    public void DetermineDecisionFromFlowOutput_WhenNoJudgmentSignalExists_ReturnsUndetermined()
    {
        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromFlowOutput(
            new Dictionary<string, object> { ["BlobCount"] = 3, ["Measurement"] = 12.5 });

        evaluation.Decision.Should().Be(DecisionOutcome.Undetermined);
        evaluation.DecisionSource.Should().Be("None");
        evaluation.ReasonCode.Should().Be("MissingJudgmentSignal");
        evaluation.Message.Should().BeNull();
        evaluation.MissingJudgmentSignal.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(42)]
    public void DetermineDecisionFromFlowOutput_WhenExplicitJudgmentTypeIsInvalid_ReturnsInvalid(object? value)
    {
        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromFlowOutput(
            new Dictionary<string, object> { ["JudgmentResult"] = value! });

        evaluation.Decision.Should().Be(DecisionOutcome.Invalid);
        evaluation.ReasonCode.Should().Be("InvalidJudgmentType");
        evaluation.Message.Should().NotBeNullOrWhiteSpace();
    }
}
