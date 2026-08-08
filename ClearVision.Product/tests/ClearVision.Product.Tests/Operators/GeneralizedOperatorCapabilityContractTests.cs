using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public sealed class GeneralizedOperatorCapabilityContractTests
{
    [Fact]
    public void MetadataAndAiCatalog_ShouldExposeTheSameGeneralizedCapabilityFamilies()
    {
        var factory = new OperatorFactory();
        factory.GetAllMetadata().Should().HaveCount(158);

        ((int)OperatorType.Filtering).Should().Be(2);
        ((int)OperatorType.Measurement).Should().Be(8);
        ((int)OperatorType.DeepLearning).Should().Be(10);

        var filtering = factory.GetMetadata(OperatorType.Filtering)!;
        filtering.Parameters.Select(parameter => parameter.Name).Should().Contain(
            ["FilterMode", "KernelSize", "SigmaX", "SigmaY", "BorderType", "Diameter", "SigmaColor", "SigmaSpace"]);
        filtering.OutputPorts.Select(port => port.Name).Should().Contain(["Image", "FilterMode", "FilterDiagnostics"]);

        var measurement = factory.GetMetadata(OperatorType.Measurement)!;
        measurement.InputPorts.Select(port => port.Name).Should().Contain(["PointA", "PointB", "PointC", "Line1", "Line2"]);
        measurement.Parameters.Select(parameter => parameter.Name).Should().Contain(
            ["MeasureType", "DistanceModel", "ParallelThreshold", "AngleUnit"]);
        measurement.OutputPorts.Select(port => port.Name).Should().Contain(
            ["Value", "Unit", "MeasurementType", "FootPoint", "Intersection", "UncertaintyDeg"]);

        var deepLearning = factory.GetMetadata(OperatorType.DeepLearning)!;
        deepLearning.Parameters.Select(parameter => parameter.Name).Should().Contain(
            [
                "TaskType", "Confidence", "OutputFormat", "TopK", "ClassificationInputSize",
                "ClassificationScoreMode", "SegmentationInputSize", "NumClasses", "MaxClassMasks"
            ]);
        deepLearning.OutputPorts.Select(port => port.Name).Should().Contain(
            [
                "TaskType", "TaskResolutionSource", "DetectionList", "ClassificationResult",
                "SegmentationMap", "ClassMasks"
            ]);

        VisionAgentReadOnlyCatalog.Schemas.Should().ContainKey("Filtering");
        AssertAiSchemaMatchesMetadata(VisionAgentReadOnlyCatalog.Schemas["Filtering"], filtering);
        AssertAiSchemaMatchesMetadata(VisionAgentReadOnlyCatalog.Schemas["Measurement"], measurement);
        AssertAiSchemaMatchesMetadata(VisionAgentReadOnlyCatalog.Schemas["DeepLearning"], deepLearning);
    }

    [Fact]
    public async Task LegacyFlowsWithoutNewModeParameters_ShouldRoundTripAndKeepLegacyDefaults()
    {
        var legacy = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "legacy-generalized-operators",
            Operators =
            [
                OperatorDto(
                    "legacy-filtering",
                    OperatorType.Filtering,
                    Parameter("KernelSize", "int", 5),
                    Parameter("SigmaX", "double", 1.0),
                    Parameter("SigmaY", "double", 0.0),
                    Parameter("BorderType", "enum", "4")),
                OperatorDto(
                    "legacy-measurement",
                    OperatorType.Measurement,
                    Parameter("X1", "int", 0),
                    Parameter("Y1", "int", 0),
                    Parameter("X2", "int", 3),
                    Parameter("Y2", "int", 4),
                    Parameter("MeasureType", "enum", "PointToPoint")),
                OperatorDto(
                    "legacy-deep-learning",
                    OperatorType.DeepLearning,
                    Parameter("ModelPath", "file", "legacy.onnx"),
                    Parameter("Confidence", "double", 0.5))
            ]
        };

        var json = JsonSerializer.Serialize(legacy);
        var restoredDto = JsonSerializer.Deserialize<OperatorFlowDto>(json)!;
        var restored = restoredDto.ToEntity();

        restored.Operators.Select(op => op.Type).Should().Equal(
            OperatorType.Filtering,
            OperatorType.Measurement,
            OperatorType.DeepLearning);
        restored.Operators.Select(op => op.Name).Should().Equal(
            "legacy-filtering",
            "legacy-measurement",
            "legacy-deep-learning");
        restored.Operators[0].Parameters.Select(parameter => parameter.Name).Should().NotContain("FilterMode");
        restored.Operators[2].Parameters.Select(parameter => parameter.Name).Should().NotContain("TaskType");

        using var image = TestHelpers.CreateTestImage(width: 32, height: 32);
        var filterResult = await new GaussianBlurOperator(NullLogger<GaussianBlurOperator>.Instance)
            .ExecuteAsync(restored.Operators[0], TestHelpers.CreateImageInputs(image));
        filterResult.IsSuccess.Should().BeTrue(filterResult.ErrorMessage);
        filterResult.OutputData!["FilterMode"].Should().Be("Gaussian");
        (filterResult.OutputData["Image"] as IDisposable)?.Dispose();

        var measurementResult = await new MeasureDistanceOperator(NullLogger<MeasureDistanceOperator>.Instance)
            .ExecuteAsync(restored.Operators[1], new Dictionary<string, object>
            {
                ["PointA"] = new Position(0, 0),
                ["PointB"] = new Position(3, 4)
            });
        measurementResult.IsSuccess.Should().BeTrue(measurementResult.ErrorMessage);
        Convert.ToDouble(measurementResult.OutputData!["Distance"]).Should().Be(5.0);
        measurementResult.OutputData["MeasurementType"].Should().Be("PointToPoint");

        DeepLearningTaskResolver.TryParse(null, out var legacyTask).Should().BeTrue();
        legacyTask.Should().Be(DeepLearningTaskType.ObjectDetection);
    }

    private static OperatorDto OperatorDto(string name, OperatorType type, params ParameterDto[] parameters)
    {
        return new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            X = 0,
            Y = 0,
            Parameters = parameters.ToList()
        };
    }

    private static ParameterDto Parameter(string name, string dataType, object value)
    {
        return new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = name,
            DataType = dataType,
            DefaultValue = value,
            Value = value
        };
    }

    private static void AssertAiSchemaMatchesMetadata(
        OperatorSchemaItem schema,
        ClearVision.Product.Core.Services.OperatorMetadata metadata)
    {
        schema.InputPorts.Should().BeEquivalentTo(metadata.InputPorts.Select(port => port.Name));
        schema.OutputPorts.Should().BeEquivalentTo(metadata.OutputPorts.Select(port => port.Name));
        schema.Parameters.Select(parameter => parameter.Name)
            .Should().BeEquivalentTo(metadata.Parameters.Select(parameter => parameter.Name));
    }
}
