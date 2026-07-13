using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public sealed class ParameterMappingServiceTests
{
    [Fact]
    public void Map_ClassificationIntent_ShouldSetTaskTypeAndExcludeDetectionOnlyParameters()
    {
        var resolution = Map(
            "DeepLearning",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Confidence"] = "0.99",
                ["TopK"] = "3"
            },
            "classify product appearance into the expected class");

        resolution.ParameterStrategy.Should().Be("deep_learning_classification");
        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "TaskType" &&
            mapping.ValueSummary == "ImageClassification");
        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "TopK" &&
            mapping.ValueSummary == "3");
        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "ChannelOrder" &&
            mapping.ValueSummary == "RGB" &&
            !mapping.Pending);
        resolution.MissingResources.Should().NotContain(resource =>
            resource.ParameterName == "ChannelOrder" ||
            resource.ResourceType == "output_channel");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain(
            ["ClassificationInputSize", "ClassificationScoreMode", "ClassNames"]);
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            [
                "Confidence", "ModelVersion", "InputSize", "TargetClasses", "EnableInternalNms",
                "NmsIouThreshold", "OutputFormat", "DetectionMode", "SegmentationInputSize",
                "NumClasses", "MaxClassMasks"
            ]);
    }

    [Fact]
    public void Map_SemanticSegmentationIntent_ShouldExcludeDetectionAndClassificationOnlyParameters()
    {
        var resolution = Map(
            "DeepLearning",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "run semantic segmentation and return a segmentation mask");

        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "TaskType" &&
            mapping.ValueSummary == "SemanticSegmentation");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain(
            ["SegmentationInputSize", "NumClasses", "MaxClassMasks", "ClassNames"]);
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            [
                "Confidence", "ModelVersion", "InputSize", "TargetClasses", "EnableInternalNms",
                "NmsIouThreshold", "OutputFormat", "DetectionMode", "TopK",
                "ClassificationInputSize", "ClassificationScoreMode", "LabelsPath"
            ]);
    }

    [Fact]
    public void Map_MedianFilter_ShouldExcludeParametersDisabledByFilterMode()
    {
        var resolution = Map(
            "Filtering",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FilterMode"] = "Median",
                ["SigmaX"] = "9.0"
            });

        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "FilterMode" &&
            mapping.ValueSummary == "Median");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain("KernelSize");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            ["SigmaX", "SigmaY", "BorderType", "Diameter", "SigmaColor", "SigmaSpace"]);
    }

    [Fact]
    public void Map_LineToLineMeasurement_ShouldExposeOnlyLineDistanceParameters()
    {
        var resolution = Map(
            "Measurement",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MeasureType"] = "LineToLine",
                ["X1"] = "99"
            });

        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "MeasureType" &&
            mapping.ValueSummary == "LineToLine");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain(
            ["DistanceModel", "ParallelThreshold"]);
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            ["X1", "Y1", "X2", "Y2", "AngleUnit"]);
    }

    private static ParameterMappingResolution Map(
        string operatorType,
        IReadOnlyDictionary<string, string> parameterSelections,
        string originalUserPrompt = "")
    {
        var load = new BuildPlanLoad
        {
            OriginalUserPrompt = originalUserPrompt,
            ParameterSelections = parameterSelections
        };
        var pipeline = new OperatorPipelineResolution(
            [
                new VisionAgentOperatorPipelineStep
                {
                    TempId = "op_test",
                    OperatorType = operatorType,
                    Source = "test",
                    Status = "selected"
                }
            ],
            []);
        var selection = new PlanSelectionResolution(
            new VisionAgentRecommendedRoute(),
            SelectionSource: "test",
            Strategy: string.Empty,
            StrategyConfirmed: true,
            StrategyConfirmationSource: "test",
            UnresolvedStrategyBlockers: [],
            ParameterStrategy: string.Empty,
            BlockingReasons: [],
            Evidence: []);

        return new ParameterMappingService(new OperatorFactory())
            .Map(load, pipeline, selection)
            .Payload;
    }
}
