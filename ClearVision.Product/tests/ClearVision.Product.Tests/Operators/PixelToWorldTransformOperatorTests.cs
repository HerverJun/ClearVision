using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.RuntimeAssets;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Calibration, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality", Suites = "Stage12Regression")]
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
    public async Task ExecuteAsync_WithRuntimeAssetContext_ShouldResolveSingleAuthorityBundle()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        var inputs = new Dictionary<string, object>
        {
            [RuntimeAssetInputKeys.RuntimeAssetContext] = CreateRuntimeAssetContext(("asset-runtime", "bundle-runtime")),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(160, 120) }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = Assert.IsType<List<Point3d>>(result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(3.2, 1e-9);
        point.Y.Should().BeApproximately(2.4, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["CalibrationDataSource"].Should().Be("RuntimePackageAsset");
        transformResult["CalibrationAssetId"].Should().Be("asset-runtime");
        transformResult["CalibrationBundleId"].Should().Be("bundle-runtime");
    }

    [Fact]
    public async Task ExecuteAsync_WithRuntimeBundleIdParameter_ShouldResolveMatchingBundle()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        op.Parameters.Add(TestHelpers.CreateParameter("CalibrationBundleId", "bundle-b"));
        var inputs = new Dictionary<string, object>
        {
            [RuntimeAssetInputKeys.RuntimeAssetContext] = CreateRuntimeAssetContext(("asset-a", "bundle-a"), ("asset-b", "bundle-b")),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData!["TransformResult"]);
        transformResult["CalibrationAssetId"].Should().Be("asset-b");
        transformResult["CalibrationBundleId"].Should().Be("bundle-b");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingRuntimeBundleId_ShouldFailClosed()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("CalibrationBundleId", "missing-bundle"));
        var inputs = new Dictionary<string, object>
        {
            [RuntimeAssetInputKeys.RuntimeAssetContext] = CreateRuntimeAssetContext(("asset-a", "bundle-a")),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RUNTIME_CALIBRATION_BUNDLE_MISSING");
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleRuntimeBundlesAndNoSelector_ShouldFailClosed()
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        var inputs = new Dictionary<string, object>
        {
            [RuntimeAssetInputKeys.RuntimeAssetContext] = CreateRuntimeAssetContext(("asset-a", "bundle-a"), ("asset-b", "bundle-b")),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RUNTIME_CALIBRATION_BUNDLE_AMBIGUOUS");
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
        report["RoundTripFrame"].Should().Be("image.full");
        report["RoundTripUnit"].Should().Be("px");
        report["RoundTripSpatialTransformCount"].Should().Be(0);
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
        report["RoundTripFrame"].Should().Be("world.2d");
        report["RoundTripUnit"].Should().Be("mm");
        report["RoundTripSpatialTransformCount"].Should().Be(0);
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

    [Fact]
    public async Task ExecuteAsync_WithRoiLocalSpatialContext_ShouldComposeCropChainBeforePlanarPixelToWorld()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(bundleId: "bundle-roi-chain");
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 2);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<Point3d>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(0.30, 1e-9);
        point.Y.Should().BeApproximately(0.56, 1e-9);
        point.Z.Should().BeApproximately(0.0, 1e-12);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["InputFrame"].Should().Be("roi.local.depth2");
        transformResult["CalibrationSourceFrame"].Should().Be("image.full");
        transformResult["CalibrationTargetFrame"].Should().Be("world.2d");
        transformResult["OutputFrame"].Should().Be("world.2d");
        Convert.ToInt32(transformResult["AppliedSpatialTransformCount"]).Should().Be(2);
        var chain = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["TransformChain"]).ToList();
        chain.Should().ContainInOrder("roi.local.depth2->roi.local.depth1", "roi.local.depth1->image.full", "image.full->world.2d");
        transformResult["CompatibilityMode"].Should().Be(true);

        var pointsContext = Assert.IsType<SpatialContextV1>(result.OutputData["TransformedPointsSpatialContext"]);
        pointsContext.CurrentFrame.Kind.Should().Be(SpatialFrameKindV1.World2D);
        pointsContext.Binding.SourceOperatorId.Should().Be(op.Id);
        pointsContext.Binding.OutputPortId.Should().Be(op.OutputPorts.Single(port => port.Name == "TransformedPoints").Id);
        pointsContext.Binding.OutputName.Should().Be("TransformedPoints");

        var imageContext = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
        imageContext.CurrentFrame.FrameId.Should().Be("roi.local.depth2");
        imageContext.Binding.SourceOperatorId.Should().Be(op.Id);
        imageContext.Binding.OutputPortId.Should().Be(op.OutputPorts.Single(port => port.Name == "Image").Id);
        imageContext.Binding.OutputName.Should().Be("Image");
        AssertRoundTripReportIsNearZero(result, expectedCount: 1);
    }

    [Fact]
    public async Task ExecuteAsync_WithThreeLayerRoiLocalSpatialContext_ShouldNotDoubleCountCropOffsets()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(1, 1) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 3);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<Point3d>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(0.34, 1e-9);
        point.Y.Should().BeApproximately(0.68, 1e-9);
        AssertRoundTripReportIsNearZero(result, expectedCount: 1);
    }

    [Fact]
    public async Task ExecuteAsync_WithWorldToPixelAndRoiContext_ShouldReturnRoiLocalWhenContextCurrentFrameIsRoiLocal()
    {
        var op = CreatePixelToWorldOperator("WorldToPixel");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<Point3d> { new(0.30, 0.56, 0) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 2);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<ClearVision.Product.Core.ValueObjects.Position>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(2.0, 1e-9);
        point.Y.Should().BeApproximately(4.0, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["InputFrame"].Should().Be("world.2d");
        transformResult["OutputFrame"].Should().Be("roi.local.depth2");
        Convert.ToInt32(transformResult["AppliedSpatialTransformCount"]).Should().Be(2);
        var chain = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["TransformChain"]).ToList();
        chain.Should().ContainInOrder("world.2d->image.full", "image.full->roi.local.depth1", "roi.local.depth1->roi.local.depth2");
        AssertRoundTripReportIsNearZero(result, expectedCount: 1);
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitRoiLocalButMissingSpatialContext_ShouldFailClosed()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        op.Parameters.Add(TestHelpers.CreateParameter("InputFrame", "RoiLocal"));
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RoiLocal");
        result.ErrorMessage.Should().Contain("SpatialContext");
    }

    [Fact]
    public async Task ExecuteAsync_WithMalformedSpatialContext_ShouldFailClosed()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = "{not valid json";

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Malformed SpatialContext");
    }

    [Fact]
    public async Task ExecuteAsync_WithUndistortedSourceFrame_ShouldTransformWithoutImageFullAssumption()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(sourceFrame: "imageUndistorted", bundleId: "bundle-undistorted");
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<Point3d>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(0.20, 1e-9);
        point.Y.Should().BeApproximately(0.20, 1e-9);
        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["InputFrame"].Should().Be("image.undistorted");
        transformResult["CalibrationSourceFrame"].Should().Be("image.undistorted");
        transformResult["OutputFrame"].Should().Be("world.2d");
    }

    [Fact]
    public async Task ExecuteAsync_WithFrameMismatch_ShouldFailClosed()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(sourceFrame: "imageUndistorted");
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = SpatialContextV1.DefaultImageFull();

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No spatial transform path");
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitRoiLocalInput_ShouldResolveCurrentSpatialContextFrame()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        op.Parameters.Add(TestHelpers.CreateParameter("InputFrame", "RoiLocal"));
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(bundleId: "bundle-explicit-roi-input");
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 2);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<Point3d>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(0.30, 1e-9);
        point.Y.Should().BeApproximately(0.56, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["InputFrame"].Should().Be("roi.local.depth2");
        var diagnostics = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["Diagnostics"]).ToList();
        diagnostics.Should().Contain(item => item.Contains("Requested RoiLocal input frame", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitRoiLocalOutput_ShouldResolveCurrentSpatialContextFrame()
    {
        var op = CreatePixelToWorldOperator("WorldToPixel");
        op.Parameters.Add(TestHelpers.CreateParameter("OutputFrame", "RoiLocal"));
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(bundleId: "bundle-explicit-roi-output");
        inputs["Points"] = new List<Point3d> { new(0.30, 0.56, 0) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 2);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<ClearVision.Product.Core.ValueObjects.Position>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(2.0, 1e-9);
        point.Y.Should().BeApproximately(4.0, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["OutputFrame"].Should().Be("roi.local.depth2");
        var diagnostics = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["Diagnostics"]).ToList();
        diagnostics.Should().Contain(item => item.Contains("Requested RoiLocal output frame", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithRayPlaneRoiLocalInput_ShouldComposeSpatialChainBeforeIntrinsics()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedRayPlaneBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(160, 100) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 1);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<Point3d>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(2.0, 1e-9);
        point.Y.Should().BeApproximately(0.0, 1e-9);
        point.Z.Should().BeApproximately(0.0, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["Path"].Should().Be("RayPlaneIntersection");
        transformResult["InputFrame"].Should().Be("roi.local.depth1");
        transformResult["CalibrationSourceFrame"].Should().Be("image.full");
        transformResult["CalibrationTargetFrame"].Should().Be("world.2d");
        transformResult["OutputFrame"].Should().Be("world.2d");
        Convert.ToInt32(transformResult["AppliedSpatialTransformCount"]).Should().Be(1);
        var chain = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["TransformChain"]).ToList();
        chain.Should().ContainInOrder("roi.local.depth1->image.full", "image.full->world.2d");
        AssertRoundTripReportIsNearZero(result, expectedCount: 1);
    }

    [Fact]
    public async Task ExecuteAsync_WithRayPlaneWorldToPixelAndRoiContext_ShouldReturnRoiLocalPixels()
    {
        var op = CreatePixelToWorldOperator("WorldToPixel");
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedRayPlaneBundleJson();
        inputs["Points"] = new List<Point3d> { new(2.0, 0.0, 0.0) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 1);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<ClearVision.Product.Core.ValueObjects.Position>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(160.0, 1e-9);
        point.Y.Should().BeApproximately(100.0, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        transformResult["Path"].Should().Be("RayPlaneIntersection");
        transformResult["InputFrame"].Should().Be("world.2d");
        transformResult["OutputFrame"].Should().Be("roi.local.depth1");
        Convert.ToInt32(transformResult["AppliedSpatialTransformCount"]).Should().Be(1);
        var chain = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["TransformChain"]).ToList();
        chain.Should().ContainInOrder("world.2d->image.full", "image.full->roi.local.depth1");
        AssertRoundTripReportIsNearZero(result, expectedCount: 1);
    }

    [Theory]
    [InlineData("PixelToWorld", "Auto", "ImageFull")]
    [InlineData("PixelToWorld", "Auto", "RoiLocal")]
    [InlineData("WorldToPixel", "ImageFull", "Auto")]
    public async Task ExecuteAsync_WithInvalidFrameDirection_ShouldFailClosed(string mode, string inputFrame, string outputFrame)
    {
        var op = CreatePixelToWorldOperator(mode);
        op.Parameters.Add(TestHelpers.CreateParameter("InputFrame", inputFrame));
        op.Parameters.Add(TestHelpers.CreateParameter("OutputFrame", outputFrame));
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = mode.Equals("PixelToWorld", StringComparison.Ordinal)
            ? new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) }
            : new List<Point3d> { new(0.30, 0.56, 0) };
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = CreateCropSpatialContext(op.Id, depth: 2);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_FRAME_DIRECTION_INVALID");
    }

    [Fact]
    public async Task ExecuteAsync_WithPointsAndImageSpatialContexts_ShouldUsePointsForCoordinatesAndImageForImageSidecar()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) };
        inputs["PointsSpatialContext"] = CreateCropSpatialContext(op.Id, depth: 2);
        inputs[RoiManagerOperator.ImageSpatialContextInputKey] = SpatialContextV1.DefaultImageFull();

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = ((List<Point3d>)result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(0.30, 1e-9);
        point.Y.Should().BeApproximately(0.56, 1e-9);
        var imageContext = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
        imageContext.CurrentFrame.Kind.Should().Be(SpatialFrameKindV1.ImageFull);
    }

    [Fact]
    public async Task ExecuteAsync_WithCentimeterWorldBundle_ShouldOutputCentimeterSpatialContext()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        using var image = TestHelpers.CreateTestImage(width: 120, height: 90);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(unit: "cm");
        inputs["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData!["TransformResult"]);
        transformResult["OutputUnit"].Should().Be("cm");
        var pointsContext = Assert.IsType<SpatialContextV1>(result.OutputData["TransformedPointsSpatialContext"]);
        pointsContext.CurrentFrame.Unit.Should().Be(SpatialUnitV1.Centimeter);
        pointsContext.CurrentFrame.UnitSymbol.Should().Be("cm");
    }

    [Theory]
    [InlineData("mm", SpatialUnitV1.Millimeter)]
    [InlineData("cm", SpatialUnitV1.Centimeter)]
    [InlineData("m", SpatialUnitV1.Meter)]
    public async Task ExecuteAsync_WorldToPixel_ShouldUsePointsSpatialContextWorldUnitAuthority(
        string upstreamUnit,
        SpatialUnitV1 expectedContextUnit)
    {
        var upstream = CreatePixelToWorldOperator("PixelToWorld");
        var upstreamInputs = new Dictionary<string, object>
        {
            ["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(unit: upstreamUnit),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(100, 50) }
        };

        var upstreamResult = await _operator.ExecuteAsync(upstream, upstreamInputs);
        upstreamResult.IsSuccess.Should().BeTrue(upstreamResult.ErrorMessage);
        var upstreamPoints = Assert.IsType<List<Point3d>>(upstreamResult.OutputData!["TransformedPoints"]);
        var upstreamContext = Assert.IsType<SpatialContextV1>(upstreamResult.OutputData["TransformedPointsSpatialContext"]);
        upstreamContext.CurrentFrame.Unit.Should().Be(expectedContextUnit);

        var downstream = CreatePixelToWorldOperator("WorldToPixel");
        var downstreamInputs = new Dictionary<string, object>
        {
            ["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(unit: "mm", bundleId: $"bundle-downstream-{upstreamUnit}"),
            ["Points"] = upstreamPoints,
            ["PointsSpatialContext"] = upstreamContext
        };

        var downstreamResult = await _operator.ExecuteAsync(downstream, downstreamInputs);

        downstreamResult.IsSuccess.Should().BeTrue(downstreamResult.ErrorMessage);
        var pixels = Assert.IsType<List<ClearVision.Product.Core.ValueObjects.Position>>(downstreamResult.OutputData!["TransformedPoints"]);
        pixels.Single().X.Should().BeApproximately(100, 1e-9);
        pixels.Single().Y.Should().BeApproximately(50, 1e-9);
        var transformResult = Assert.IsType<Dictionary<string, object>>(downstreamResult.OutputData["TransformResult"]);
        transformResult["InputUnit"].Should().Be(upstreamContext.CurrentFrame.UnitSymbol);
        transformResult["InputFrame"].Should().Be(upstreamContext.CurrentFrame.FrameId);
    }

    [Fact]
    public async Task ExecuteAsync_WorldToPixel_WithNonWorldPointsSpatialContext_ShouldFailClosed()
    {
        var op = CreatePixelToWorldOperator("WorldToPixel");
        var inputs = new Dictionary<string, object>
        {
            ["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(unit: "mm"),
            ["Points"] = new List<Point3d> { new(1, 1, 0) },
            ["PointsSpatialContext"] = SpatialContextV1.DefaultImageFull()
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_FRAME_DIRECTION_INVALID");
    }

    [Fact]
    public async Task ExecuteAsync_WithConflictingExplicitUnitScale_ShouldFailClosed()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        op.Parameters.Add(TestHelpers.CreateParameter("UnitScale", 1.0));
        var inputs = new Dictionary<string, object>
        {
            ["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(unit: "cm"),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_UNIT_INCOMPATIBLE");
    }

    [Fact]
    public async Task ExecuteAsync_WithPointsContextAndMalformedImageContext_ShouldTransformPointsAndOmitImageSidecar()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        var inputs = new Dictionary<string, object>
        {
            ["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(2, 4) },
            ["PointsSpatialContext"] = CreateCropSpatialContext(op.Id, depth: 2),
            [RoiManagerOperator.ImageSpatialContextInputKey] = "{not valid json"
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var point = Assert.IsType<List<Point3d>>(result.OutputData!["TransformedPoints"]).Single();
        point.X.Should().BeApproximately(0.30, 1e-9);
        point.Y.Should().BeApproximately(0.56, 1e-9);
        result.OutputData.Should().ContainKey("TransformedPointsSpatialContext");
        result.OutputData.Should().NotContainKey(RoiManagerOperator.SpatialContextOutputKey);
        var transformResult = Assert.IsType<Dictionary<string, object>>(result.OutputData["TransformResult"]);
        var diagnostics = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["Diagnostics"]).ToList();
        diagnostics.Should().Contain(item => item.Contains("IMAGE_SPATIAL_CONTEXT_MALFORMED", StringComparison.Ordinal));
        diagnostics.Should().Contain(item => item.Contains("SYNTHETIC_IMAGE_SPATIAL_CONTEXT_OMITTED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutInputImage_ShouldNotEmitBusinessImageSpatialContextForSyntheticVisualization()
    {
        var op = CreatePixelToWorldOperator("PixelToWorld");
        var inputs = new Dictionary<string, object>
        {
            ["CalibrationData"] = CreateAcceptedScaleOffsetBundleJson(),
            ["Points"] = new List<ClearVision.Product.Core.ValueObjects.Position> { new(10, 10) }
        };

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var outputData = result.OutputData!;
        outputData.Should().ContainKey("TransformedPoints");
        outputData.Should().ContainKey("TransformedPointsSpatialContext");
        outputData.Should().NotContainKey(RoiManagerOperator.SpatialContextOutputKey);
        var transformResult = Assert.IsType<Dictionary<string, object>>(outputData["TransformResult"]);
        var diagnostics = Assert.IsAssignableFrom<IEnumerable<string>>(transformResult["Diagnostics"]).ToList();
        diagnostics.Should().Contain(item => item.Contains("SYNTHETIC_IMAGE_SPATIAL_CONTEXT_OMITTED", StringComparison.Ordinal));
    }

    private static Operator CreatePixelToWorldOperator(string transformMode)
    {
        var op = new Operator("PixelToWorldTransform", OperatorType.PixelToWorldTransform, 0, 0);
        op.AddOutputPort("Image", PortDataType.Image);
        op.AddOutputPort("TransformedPoints", PortDataType.PointList);
        op.AddOutputPort("TransformResult", PortDataType.Any);
        op.Parameters.Add(TestHelpers.CreateParameter("TransformMode", transformMode));
        return op;
    }

    private static void AssertRoundTripReportIsNearZero(OperatorExecutionOutput result, int expectedCount)
    {
        var report = Assert.IsType<Dictionary<string, object>>(result.OutputData!["AccuracyReport"]);
        Assert.IsType<List<double>>(report["RoundTripErrors"]).Should().HaveCount(expectedCount);
        Convert.ToDouble(report["RoundTripMax"]).Should().BeLessThan(1e-6);
        Convert.ToDouble(report["RoundTripRmse"]).Should().BeLessThan(1e-6);
    }

    private static SpatialContextV1 CreateCropSpatialContext(Guid operatorId, int depth)
    {
        depth.Should().BeInRange(1, 3);

        var image = FrameRefV1.ImageFull();
        var depth1 = FrameRefV1.RoiLocal("roi.local.depth1", image.FrameId);
        var transforms = new List<SpatialTransform2DV1>
        {
            SpatialTransform2DV1.Identity(image),
            new(
                depth1,
                image,
                [
                    [1, 0, 10],
                    [0, 1, 20],
                    [0, 0, 1]
                ])
        };

        var current = depth1;
        if (depth >= 2)
        {
            var depth2 = FrameRefV1.RoiLocal("roi.local.depth2", depth1.FrameId);
            transforms.Add(new SpatialTransform2DV1(
                depth2,
                depth1,
                [
                    [1, 0, 3],
                    [0, 1, 4],
                    [0, 0, 1]
                ]));
            current = depth2;
        }

        if (depth >= 3)
        {
            var depth3 = FrameRefV1.RoiLocal("roi.local.depth3", current.FrameId);
            transforms.Add(new SpatialTransform2DV1(
                depth3,
                current,
                [
                    [1, 0, 3],
                    [0, 1, 9],
                    [0, 0, 1]
                ]));
            current = depth3;
        }

        return new SpatialContextV1(
            current,
            transforms,
            SpatialContextBindingV1.ForFlowOutput(operatorId, Guid.NewGuid(), "Image"));
    }

    private static string CreateAcceptedScaleOffsetBundleJson(
        string sourceFrame = "image",
        string targetFrame = "world",
        string bundleId = "bundle-scale-offset",
        string unit = "mm")
    {
        return $$"""
                 {
                   "schemaVersion": 2,
                   "bundleId": "{{bundleId}}",
                   "calibrationVersion": "v-test",
                   "datasetFingerprint": "dataset-test",
                   "checksumSha256": "0123456789abcdef",
                   "calibrationKind": "rigidTransform2D",
                   "transformModel": "scaleOffset",
                   "sourceFrame": "{{sourceFrame}}",
                   "targetFrame": "{{targetFrame}}",
                   "unit": "{{unit}}",
                   "transform2D": {
                     "model": "scaleOffset",
                     "matrix": [
                       [0.02, 0.0, 0.0],
                       [0.0, 0.02, 0.0]
                     ],
                     "pixelSizeX": 0.02,
                     "pixelSizeY": 0.02
                   },
                   "quality": {
                     "accepted": true,
                     "meanError": 0.05,
                     "maxError": 0.09,
                     "inlierCount": 8,
                     "totalSampleCount": 8,
                     "diagnostics": []
                   },
                   "producerOperator": "PixelToWorldTransformOperatorTests"
                 }
                 """;
    }

    private static string CreateAcceptedRayPlaneBundleJson()
    {
        return """
               {
                 "schemaVersion": 2,
                 "bundleId": "bundle-ray-plane",
                 "calibrationVersion": "v-test",
                 "datasetFingerprint": "dataset-ray-plane-test",
                 "checksumSha256": "abcdef0123456789",
                 "calibrationKind": "cameraIntrinsics",
                 "transformModel": "none",
                 "sourceFrame": "camera",
                 "targetFrame": "world",
                 "unit": "mm",
                 "intrinsics": {
                   "cameraMatrix": [
                     [500.0, 0.0, 160.0],
                     [0.0, 500.0, 120.0],
                     [0.0, 0.0, 1.0]
                   ]
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
                   "meanError": 0.05,
                   "maxError": 0.10,
                   "inlierCount": 12,
                   "totalSampleCount": 12,
                   "diagnostics": []
                 },
                 "producerOperator": "PixelToWorldTransformOperatorTests"
               }
               """;
    }

    private static RuntimeAssetContext CreateRuntimeAssetContext(params (string AssetId, string BundleId)[] assets) =>
        new(assets.Select(asset => new RuntimeCalibrationBundleAsset(
            asset.AssetId,
            asset.BundleId,
            "CalibrationBundleV2",
            "2.0",
            12,
            "sha256:" + new string('1', 64),
            "sha256:" + new string('2', 64),
            $"assets/calibration/{asset.AssetId}.json",
            CreateAcceptedScaleOffsetBundleJson(bundleId: asset.BundleId))));
}
