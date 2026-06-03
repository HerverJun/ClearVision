using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class PixelToWorldTransformOperatorTests
{
    private readonly PixelToWorldTransformOperator _operator;

    public PixelToWorldTransformOperatorTests()
    {
        _operator = new PixelToWorldTransformOperator(Substitute.For<ILogger<PixelToWorldTransformOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBePixelToWorldTransform()
    {
        _operator.OperatorType.Should().Be(OperatorType.PixelToWorldTransform);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutCalibrationData_ShouldReturnFailure()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCalibration_ShouldReturnSuccess()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(100, 100) };

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithPixelToWorldMode_ShouldTransformPoints()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(160, 120) };

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("TransformedPoints");
    }

    [Fact]
    public async Task ExecuteAsync_WithPlanarBundle_ShouldEmitIndustrialAccuracyReport()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(160, 120), new(40, 60) };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var report = Assert.IsType<Dictionary<string, object>>(result.OutputData!["AccuracyReport"]);
        report["RoundTripUnit"].Should().Be("px");
        Convert.ToDouble(report["RoundTripMax"]).Should().BeLessThan(1e-9);
        Convert.ToDouble(report["RoundTripRmse"]).Should().BeLessThan(1e-9);
        Assert.IsType<List<double>>(report["RoundTripErrors"]).Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithPlanarBundle_ShouldRoundTripPixelAndWorldPointLists()
    {
        var pixelPoints = new List<ClearVision.Product.Core.ValueObjects.Position>
        {
            new(0.0, 0.0),
            new(12.25, 18.5),
            new(160.0, 120.0),
            new(319.5, 239.25)
        };

        using var pixelImage = TestHelpers.CreateTestImage(width: 320, height: 240);
        var pixelToWorldOp = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        pixelToWorldOp.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        var pixelInputs = TestHelpers.CreateImageInputs(pixelImage);
        pixelInputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        pixelInputs["Points"] = pixelPoints;

        var pixelToWorld = await _operator.ExecuteAsync(pixelToWorldOp, pixelInputs);
        pixelToWorld.IsSuccess.Should().BeTrue(pixelToWorld.ErrorMessage);

        var worldPoints = ((List<Point3d>)pixelToWorld.OutputData!["TransformedPoints"]).ToList();
        worldPoints.Should().HaveCount(pixelPoints.Count);
        for (var i = 0; i < pixelPoints.Count; i++)
        {
            worldPoints[i].X.Should().BeApproximately(pixelPoints[i].X * 0.02, 1e-9);
            worldPoints[i].Y.Should().BeApproximately(pixelPoints[i].Y * 0.02, 1e-9);
            worldPoints[i].Z.Should().BeApproximately(0.0, 1e-12);
        }

        using var worldImage = TestHelpers.CreateTestImage(width: 320, height: 240);
        var worldToPixelOp = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        worldToPixelOp.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "WorldToPixel"));
        var worldInputs = TestHelpers.CreateImageInputs(worldImage);
        worldInputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        worldInputs["Points"] = worldPoints;

        var worldToPixel = await _operator.ExecuteAsync(worldToPixelOp, worldInputs);
        worldToPixel.IsSuccess.Should().BeTrue(worldToPixel.ErrorMessage);

        var roundTrippedPixels = (List<ClearVision.Product.Core.ValueObjects.Position>)worldToPixel.OutputData!["TransformedPoints"];
        roundTrippedPixels.Should().HaveCount(pixelPoints.Count);
        for (var i = 0; i < pixelPoints.Count; i++)
        {
            roundTrippedPixels[i].X.Should().BeApproximately(pixelPoints[i].X, 1e-9);
            roundTrippedPixels[i].Y.Should().BeApproximately(pixelPoints[i].Y, 1e-9);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithWorldToPixelMode_ShouldTransformPoints()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "WorldToPixel"));
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(0, 0) };

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithPlanarWorldToPixelMode_ShouldReportWorldUnitRoundTrip()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "WorldToPixel"));
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<Point3d> { new(2.0, 3.0, 0.0), new(0.8, 1.2, 0.0) };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var report = Assert.IsType<Dictionary<string, object>>(result.OutputData!["AccuracyReport"]);
        report["RoundTripUnit"].Should().Be("world_unit");
        Convert.ToDouble(report["RoundTripMax"]).Should().BeLessThan(1e-9);
        Convert.ToDouble(report["RoundTripRmse"]).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void ValidateParameters_WithValidUnitScale_ShouldBeValid()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("UnitScale", 0.001));

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidUnitScale_ShouldBeInvalid()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("UnitScale", 0.0));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidPointsJson_ShouldFailClosed()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = "[{\"X\":100}]";

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Points[0]");
    }

    [Fact]
    public async Task ExecuteAsync_WithKannalaBrandtDistortion_ShouldSupportPixelToWorld()
    {
        const string rayPlaneBundleWithKannala = """
                                                 {
                                                   "schemaVersion": 2,
                                                   "calibrationKind": "cameraIntrinsics",
                                                   "transformModel": "none",
                                                   "sourceFrame": "image",
                                                   "targetFrame": "world",
                                                   "unit": "mm",
                                                   "intrinsics": {
                                                     "cameraMatrix": [
                                                       [500.0, 0.0, 160.0],
                                                       [0.0, 500.0, 120.0],
                                                       [0.0, 0.0, 1.0]
                                                     ]
                                                   },
                                                   "distortion": {
                                                     "model": "kannalaBrandt",
                                                     "coefficients": [0.1, 0.01, 0.0, 0.0]
                                                   },
                                                   "transform3D": {
                                                     "model": "rigid3D",
                                                     "matrix": [
                                                       [1.0, 0.0, 0.0, 0.0],
                                                       [0.0, 1.0, 0.0, 0.0],
                                                       [0.0, 0.0, 1.0, -100.0],
                                                       [0.0, 0.0, 0.0, 1.0]
                                                     ]
                                                   },
                                                   "quality": {
                                                     "accepted": true,
                                                     "meanError": 0.10,
                                                     "maxError": 0.20,
                                                     "inlierCount": 12,
                                                     "totalSampleCount": 12,
                                                     "diagnostics": []
                                                   },
                                                   "producerOperator": "test"
                                                 }
                                                 """;

        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = rayPlaneBundleWithKannala;
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(100, 100) };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("TransformedPoints");
    }

    [Fact]
    public async Task ExecuteAsync_WithBrownDistortion_ShouldRoundTripWorldAndPixelCoordinates()
    {
        const string rayPlaneBundleWithBrown = """
                                               {
                                                 "schemaVersion": 2,
                                                 "calibrationKind": "cameraIntrinsics",
                                                 "transformModel": "none",
                                                 "sourceFrame": "camera",
                                                 "targetFrame": "world",
                                                 "unit": "mm",
                                                 "intrinsics": {
                                                   "cameraMatrix": [
                                                     [520.0, 0.0, 160.0],
                                                     [0.0, 515.0, 120.0],
                                                     [0.0, 0.0, 1.0]
                                                   ]
                                                 },
                                                 "distortion": {
                                                   "model": "brownConrady",
                                                   "coefficients": [0.08, -0.02, 0.001, -0.001, 0.0]
                                                 },
                                                 "transform3D": {
                                                   "model": "rigid3D",
                                                     "matrix": [
                                                       [1.0, 0.0, 0.0, 0.0],
                                                       [0.0, 1.0, 0.0, 0.0],
                                                       [0.0, 0.0, 1.0, -100.0],
                                                       [0.0, 0.0, 0.0, 1.0]
                                                     ]
                                                   },
                                                 "quality": {
                                                   "accepted": true,
                                                   "meanError": 0.10,
                                                   "maxError": 0.20,
                                                   "inlierCount": 12,
                                                   "totalSampleCount": 12,
                                                   "diagnostics": []
                                                 },
                                                 "producerOperator": "test"
                                               }
                                               """;

        using var imageForWorldToPixel = TestHelpers.CreateTestImage(width: 320, height: 240);
        var worldToPixelOp = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        worldToPixelOp.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "WorldToPixel"));
        worldToPixelOp.Parameters.Add(TestHelpers.CreateParameter("WorldPlaneZ", 0.0));

        var worldToPixelInputs = TestHelpers.CreateImageInputs(imageForWorldToPixel);
        worldToPixelInputs["CalibrationData"] = rayPlaneBundleWithBrown;
        worldToPixelInputs["Points"] = new List<Point3d> { new(12.0, -6.0, 0.0) };

        var worldToPixel = await _operator.ExecuteAsync(worldToPixelOp, worldToPixelInputs);
        worldToPixel.IsSuccess.Should().BeTrue(worldToPixel.ErrorMessage);

        var pixelPoint = ((List<ClearVision.Product.Core.ValueObjects.Position>)worldToPixel.OutputData!["TransformedPoints"]).Single();

        using var imageForPixelToWorld = TestHelpers.CreateTestImage(width: 320, height: 240);
        var pixelToWorldOp = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        pixelToWorldOp.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        pixelToWorldOp.Parameters.Add(TestHelpers.CreateParameter("WorldPlaneZ", 0.0));

        var pixelToWorldInputs = TestHelpers.CreateImageInputs(imageForPixelToWorld);
        pixelToWorldInputs["CalibrationData"] = rayPlaneBundleWithBrown;
        pixelToWorldInputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { pixelPoint };

        var pixelToWorld = await _operator.ExecuteAsync(pixelToWorldOp, pixelToWorldInputs);
        pixelToWorld.IsSuccess.Should().BeTrue(pixelToWorld.ErrorMessage);

        var worldPoint = ((List<Point3d>)pixelToWorld.OutputData!["TransformedPoints"]).Single();
        worldPoint.X.Should().BeApproximately(12.0, 0.2);
        worldPoint.Y.Should().BeApproximately(-6.0, 0.2);
        worldPoint.Z.Should().BeApproximately(0.0, 1e-6);

        var report = Assert.IsType<Dictionary<string, object>>(pixelToWorld.OutputData["AccuracyReport"]);
        report["RoundTripUnit"].Should().Be("px");
        Convert.ToDouble(report["RoundTripMax"]).Should().BeLessThan(1e-6);
    }
}
