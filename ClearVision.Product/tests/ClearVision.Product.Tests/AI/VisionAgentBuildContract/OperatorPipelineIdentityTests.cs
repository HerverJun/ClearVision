using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentBuildContract;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class OperatorPipelineIdentityTests
{
    [Fact]
    public void Select_RepeatedOperators_ShouldAllocateCaseInsensitiveUniqueIds()
    {
        var pipeline = Select(
            "BlobAnalysis",
            "blobanalysis",
            "ResultOutput",
            "ResultOutput",
            "CircleMeasurement",
            "CircleMeasurement",
            "CircleMeasurement");

        pipeline.Steps.Select(step => step.TempId.ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();
        pipeline.Steps.Where(step => step.OperatorType == "BlobAnalysis")
            .Select(step => step.TempId)
            .Should().Equal("op_blob", "op_blob_2");
        pipeline.Steps.Where(step => step.OperatorType == "ResultOutput")
            .Select(step => step.TempId)
            .Should().Equal("op_out", "op_out_2");
        pipeline.Steps.Where(step => step.OperatorType == "CircleMeasurement")
            .Select(step => step.TempId)
            .Should().Equal("op_circle_a", "op_circle_b", "op_circle_3");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Select_TypeOrdinals_ShouldNotDependOnUnrelatedPrefixLength(int prefixCount)
    {
        var prefixes = Enumerable.Repeat("ImageAcquisition", prefixCount);
        var pipeline = Select(prefixes.Concat(["BlobAnalysis", "BlobAnalysis", "ResultOutput", "ResultOutput"]).ToArray());

        pipeline.Steps.Where(step => step.OperatorType == "BlobAnalysis")
            .Select(step => step.TempId)
            .Should().Equal("op_blob", "op_blob_2");
        pipeline.Steps.Where(step => step.OperatorType == "ResultOutput")
            .Select(step => step.TempId)
            .Should().Equal("op_out", "op_out_2");
        pipeline.Steps.Select(step => step.TempId.ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Select_DraftRepairs_ShouldShareTheSameAllocator()
    {
        var load = new BuildPlanLoad { RequirementMode = AiRequirementModes.Draft };
        var route = new VisionAgentRecommendedRoute
        {
            Operators = ["ImageAcquisition", "BlobAnalysis", "ResultOutput", "ResultOutput"]
        };
        var selection = Selection(route);

        var pipeline = new OperatorPipelineSelector()
            .Select(load, EmptyTemplate(), selection, [])
            .Payload;

        pipeline.Steps.Should().Contain(step =>
            step.OperatorType == "ResultJudgment" && step.TempId == "op_judge");
        pipeline.Steps.Where(step => step.OperatorType == "ResultOutput")
            .Select(step => step.TempId)
            .Should().Equal("op_out", "op_out_2");
        pipeline.Steps.Select(step => step.TempId.ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();
    }

    private static OperatorPipelineResolution Select(params string[] operatorTypes)
    {
        var route = new VisionAgentRecommendedRoute { Operators = operatorTypes.ToList() };
        return new OperatorPipelineSelector()
            .Select(new BuildPlanLoad(), EmptyTemplate(), Selection(route), [])
            .Payload;
    }

    private static PlanSelectionResolution Selection(VisionAgentRecommendedRoute route) => new(
        route,
        SelectionSource: "test",
        Strategy: string.Empty,
        StrategyConfirmed: true,
        StrategyConfirmationSource: "test",
        UnresolvedStrategyBlockers: [],
        ParameterStrategy: string.Empty,
        BlockingReasons: [],
        Evidence: []);

    private static TemplateStrategyResolution EmptyTemplate() => new(
        Strategy: string.Empty,
        TemplateId: string.Empty,
        ScenarioKey: string.Empty,
        TemplateSkeleton: null,
        GenerationMode: string.Empty,
        TemplateLockLevel: string.Empty);
}
