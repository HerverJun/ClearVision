using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class CircleMeasurementLegacyGoldenTests
{
    [Fact]
    public async Task HoughCircle_Golden_ShouldKeepLegacyOutputsAndCandidateOrdering()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = new Operator("circle-hough-golden", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", "HoughCircle", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("MinRadius", 52, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxRadius", 68, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Dp", 1.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MinDist", 80.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Param1", 100.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Param2", 20.0, "double"));

        using var image = TestHelpers.CreateGrayShapeTestImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!.Keys.Should().Contain(new[]
        {
            "Image",
            "Center",
            "Radius",
            "Circle",
            "CircleCount",
            "Circularity",
            "CircleDataList",
            "Circles",
            "ResidualRmse",
            "StatusCode",
            "StatusMessage",
            "Confidence",
            "UncertaintyPx",
            "Method"
        });
        result.OutputData["StatusCode"].Should().Be("OK");
        result.OutputData["Method"].Should().Be("HoughCircle");
        result.OutputData["CircleCount"].Should().Be(1);

        var center = result.OutputData["Center"].Should().BeOfType<Position>().Subject;
        var radius = Convert.ToDouble(result.OutputData["Radius"]);
        center.X.Should().BeApproximately(300.0, 0.85);
        center.Y.Should().BeApproximately(200.0, 0.85);
        radius.Should().BeApproximately(60.0, 1.25);

        var circles = result.OutputData["Circles"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object>>>().Subject;
        circles.Should().HaveCount(1);
        circles[0]["Center"].Should().BeOfType<Position>();
        Convert.ToDouble(circles[0]["Radius"]).Should().BeApproximately(radius, 0.0001);
        result.OutputData["Circle"].Should().BeOfType<CircleData>();
        result.OutputData["CircleDataList"].Should().BeAssignableTo<IReadOnlyList<CircleData>>().Which.Should().HaveCount(1);
    }

    [Fact]
    public async Task FitEllipse_Golden_ShouldKeepLegacySuccessFields()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = new Operator("circle-ellipse-golden", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", "FitEllipse", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("MinRadius", 60, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxRadius", 84, "int"));

        using var image = IndustrialMeasurementSceneFactory.CreateFilledCircleImage(
            width: 420,
            height: 320,
            center: new Point2d(210.0, 154.0),
            radius: 72.0,
            supersample: 16);
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["StatusCode"].Should().Be("OK");
        result.OutputData["Method"].Should().Be("FitEllipse");
        result.OutputData["CircleCount"].Should().Be(1);
        result.OutputData.Keys.Should().NotContain("CenterX");
        result.OutputData.Keys.Should().NotContain("CenterY");

        var center = result.OutputData["Center"].Should().BeOfType<Position>().Subject;
        var radius = Convert.ToDouble(result.OutputData["Radius"]);
        center.X.Should().BeApproximately(210.0, 0.50);
        center.Y.Should().BeApproximately(154.0, 0.50);
        radius.Should().BeApproximately(72.0, 0.75);
        Convert.ToDouble(result.OutputData["Circularity"]).Should().BeGreaterThan(0.89);
    }

    [Fact]
    public async Task FitEllipse_Golden_ShouldKeepLegacyNoFeatureFailure()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = new Operator("circle-ellipse-empty-golden", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", "FitEllipse", "enum"));

        using var image = TestHelpers.CreateGrayTestImage(200, 200, 0);
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.OutputData.Should().BeNull();
        result.ErrorMessage.Should().Be("[NoFeature] FitEllipse: Not enough contour points for ellipse fitting");
    }
}
