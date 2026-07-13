using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class MeasureDistanceOperatorTests
{
    private readonly MeasureDistanceOperator _operator;

    public MeasureDistanceOperatorTests()
    {
        _operator = new MeasureDistanceOperator(Substitute.For<ILogger<MeasureDistanceOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeMeasurement()
    {
        _operator.OperatorType.Should().Be(OperatorType.Measurement);
    }

    [Fact]
    public async Task ExecuteAsync_PointInputsShouldRespectHorizontalMeasureType()
    {
        var op = new Operator("measure", OperatorType.Measurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MeasureType", "Horizontal", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PointA"] = new Point(10, 10),
            ["PointB"] = new Point(25, 30)
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Distance"]).Should().BeApproximately(15.0, 1e-6);
        Convert.ToInt32(result.OutputData["Y2"]).Should().Be(10);
        Convert.ToDouble(result.OutputData["UncertaintyPx"]).Should().BeApproximately(Math.Sqrt(0.5), 1e-6);
    }

    [Fact]
    public async Task ExecuteAsync_PointInputsShouldPreserveSubpixelPointToPointDistance()
    {
        var op = new Operator("measure", OperatorType.Measurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MeasureType", "PointToPoint", "string"));

        var pointA = new Position(10.25, 20.50);
        var pointB = new Position(42.75, 63.125);
        var expected = Math.Sqrt(Math.Pow(pointB.X - pointA.X, 2) + Math.Pow(pointB.Y - pointA.Y, 2));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PointA"] = pointA,
            ["PointB"] = pointB
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Distance"]).Should().BeApproximately(expected, 1e-9);
        Convert.ToDouble(result.OutputData["X1"]).Should().BeApproximately(pointA.X, 1e-9);
        Convert.ToDouble(result.OutputData["Y2"]).Should().BeApproximately(pointB.Y, 1e-9);
        Convert.ToDouble(result.OutputData["UncertaintyPx"]).Should().BeLessThan(0.08);
    }

    [Fact]
    public async Task ExecuteAsync_PointInputsShouldPreserveSubpixelHorizontalDistance()
    {
        var op = new Operator("measure", OperatorType.Measurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MeasureType", "Horizontal", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PointA"] = new Point2d(11.25, 18.75),
            ["PointB"] = new Point2d(30.90, 47.50)
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Distance"]).Should().BeApproximately(19.65, 1e-9);
        Convert.ToDouble(result.OutputData["Y2"]).Should().BeApproximately(18.75, 1e-9);
        Convert.ToDouble(result.OutputData["DeltaY"]).Should().BeApproximately(0.0, 1e-9);
        Convert.ToDouble(result.OutputData["UncertaintyPx"]).Should().BeLessThan(0.08);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("measure", OperatorType.Measurement, 0, 0);
        (await _operator.ExecuteAsync(op, null)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_PointToLine_ShouldMatchProfessionalOperator()
    {
        var unified = new Operator("measure", OperatorType.Measurement, 0, 0);
        unified.AddParameter(TestHelpers.CreateParameter("MeasureType", "PointToLine", "enum"));
        unified.AddParameter(TestHelpers.CreateParameter("DistanceModel", "Segment", "enum"));
        var professional = new Operator("point-line", OperatorType.PointLineDistance, 0, 0);
        professional.AddParameter(TestHelpers.CreateParameter("DistanceModel", "Segment", "enum"));
        professional.AddParameter(TestHelpers.CreateParameter("Unit", "Pixel", "enum"));
        var point = new Position(5.5, 8.0);
        var line = new LineData(0, 0, 20, 0);
        var inputs = new Dictionary<string, object> { ["PointA"] = point, ["Line1"] = line };
        var professionalInputs = new Dictionary<string, object> { ["Point"] = point, ["Line"] = line };
        var professionalExecutor = new PointLineDistanceOperator(
            Substitute.For<ILogger<PointLineDistanceOperator>>());

        var unifiedResult = await _operator.ExecuteAsync(unified, inputs);
        var professionalResult = await professionalExecutor.ExecuteAsync(professional, professionalInputs);

        unifiedResult.IsSuccess.Should().BeTrue(unifiedResult.ErrorMessage);
        professionalResult.IsSuccess.Should().BeTrue(professionalResult.ErrorMessage);
        Convert.ToDouble(unifiedResult.OutputData!["Distance"])
            .Should().BeApproximately(Convert.ToDouble(professionalResult.OutputData!["Distance"]), 1e-9);
        unifiedResult.OutputData["FootPoint"].Should().Be(professionalResult.OutputData["FootPoint"]);
        unifiedResult.OutputData["MeasurementType"].Should().Be("PointToLine");
        unifiedResult.OutputData["Unit"].Should().Be("Pixel");
    }

    [Fact]
    public async Task ExecuteAsync_LineToLine_ShouldMatchProfessionalDistanceAndAngle()
    {
        var unified = new Operator("measure", OperatorType.Measurement, 0, 0);
        unified.AddParameter(TestHelpers.CreateParameter("MeasureType", "LineToLine", "enum"));
        unified.AddParameter(TestHelpers.CreateParameter("DistanceModel", "Segment", "enum"));
        unified.AddParameter(TestHelpers.CreateParameter("ParallelThreshold", 2.0, "double"));
        var professional = new Operator("line-line", OperatorType.LineLineDistance, 0, 0);
        professional.AddParameter(TestHelpers.CreateParameter("DistanceModel", "Segment", "enum"));
        professional.AddParameter(TestHelpers.CreateParameter("ParallelThreshold", 2.0, "double"));
        professional.AddParameter(TestHelpers.CreateParameter("Unit", "Pixel", "enum"));
        var line1 = new LineData(0, 0, 20, 0);
        var line2 = new LineData(0, 8, 20, 8);
        var inputs = new Dictionary<string, object> { ["Line1"] = line1, ["Line2"] = line2 };
        var professionalExecutor = new LineLineDistanceOperator(
            Substitute.For<ILogger<LineLineDistanceOperator>>());

        var unifiedResult = await _operator.ExecuteAsync(unified, inputs);
        var professionalResult = await professionalExecutor.ExecuteAsync(professional, inputs);

        unifiedResult.IsSuccess.Should().BeTrue(unifiedResult.ErrorMessage);
        professionalResult.IsSuccess.Should().BeTrue(professionalResult.ErrorMessage);
        Convert.ToDouble(unifiedResult.OutputData!["Distance"])
            .Should().BeApproximately(Convert.ToDouble(professionalResult.OutputData!["Distance"]), 1e-9);
        Convert.ToDouble(unifiedResult.OutputData["Angle"])
            .Should().BeApproximately(Convert.ToDouble(professionalResult.OutputData["Angle"]), 1e-9);
        unifiedResult.OutputData["IsParallel"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_ThreePointAngle_ShouldMatchProfessionalOperator()
    {
        var unified = new Operator("measure", OperatorType.Measurement, 0, 0);
        unified.AddParameter(TestHelpers.CreateParameter("MeasureType", "ThreePointAngle", "enum"));
        unified.AddParameter(TestHelpers.CreateParameter("AngleUnit", "Degree", "enum"));
        var professional = new Operator("angle", OperatorType.AngleMeasurement, 0, 0);
        professional.AddParameter(TestHelpers.CreateParameter("Unit", "Degree", "enum"));
        var pointA = new Position(0, 0);
        var vertex = new Position(10, 0);
        var pointC = new Position(10, 10);
        using var unifiedImage = TestHelpers.CreateShapeTestImage();
        using var professionalImage = TestHelpers.CreateShapeTestImage();
        var unifiedInputs = TestHelpers.CreateImageInputs(unifiedImage);
        unifiedInputs["PointA"] = pointA;
        unifiedInputs["PointB"] = vertex;
        unifiedInputs["PointC"] = pointC;
        var professionalInputs = TestHelpers.CreateImageInputs(professionalImage);
        professionalInputs["Point1"] = pointA;
        professionalInputs["Point2"] = vertex;
        professionalInputs["Point3"] = pointC;
        var professionalExecutor = new AngleMeasurementOperator(
            Substitute.For<ILogger<AngleMeasurementOperator>>());

        var unifiedResult = await _operator.ExecuteAsync(unified, unifiedInputs);
        var professionalResult = await professionalExecutor.ExecuteAsync(professional, professionalInputs);

        unifiedResult.IsSuccess.Should().BeTrue(unifiedResult.ErrorMessage);
        professionalResult.IsSuccess.Should().BeTrue(professionalResult.ErrorMessage);
        Convert.ToDouble(unifiedResult.OutputData!["Angle"])
            .Should().BeApproximately(Convert.ToDouble(professionalResult.OutputData!["Angle"]), 1e-9);
        Convert.ToDouble(unifiedResult.OutputData["UncertaintyDeg"])
            .Should().BeApproximately(Convert.ToDouble(professionalResult.OutputData["UncertaintyDeg"]), 1e-6);
        Convert.ToDouble(unifiedResult.OutputData["UncertaintyPx"]).Should().BeGreaterThan(0.0);
        Convert.ToDouble(unifiedResult.OutputData["Confidence"])
            .Should().BeApproximately(Convert.ToDouble(professionalResult.OutputData["Confidence"]), 1e-6);
        unifiedResult.OutputData["Value"].Should().Be(unifiedResult.OutputData["Angle"]);
        unifiedResult.OutputData["Unit"].Should().Be("Degree");
        (unifiedResult.OutputData["Image"] as IDisposable)?.Dispose();
        (professionalResult.OutputData["Image"] as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData("PointToLine", "PointToLine requires PointA")]
    [InlineData("LineToLine", "LineToLine requires Line1 and Line2")]
    [InlineData("ThreePointAngle", "ThreePointAngle requires PointA, PointB and PointC")]
    public async Task ExecuteAsync_ModeMissingInputs_ShouldFailActionably(string mode, string expectedMessage)
    {
        var op = new Operator("measure", OperatorType.Measurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MeasureType", mode, "enum"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedMessage);
    }
}
