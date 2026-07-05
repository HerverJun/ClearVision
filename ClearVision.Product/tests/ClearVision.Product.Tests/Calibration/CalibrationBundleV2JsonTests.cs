using ClearVision.Product.Infrastructure.Calibration;
using FluentAssertions;

namespace ClearVision.Product.Tests.Calibration;

public sealed class CalibrationBundleV2JsonTests
{
    [Fact]
    public void TryRequireIntrinsics_WithUnsupportedDistortionModel_ShouldReturnChineseStructuredError()
    {
        var bundle = CreateIntrinsicsBundle(DistortionModelV2.KannalaBrandt);

        var result = CalibrationBundleV2Json.TryRequireIntrinsics(
            bundle,
            [DistortionModelV2.BrownConrady],
            out _,
            out _,
            out var error);

        result.Should().BeFalse();
        error.Should().Contain($"[{CalibrationBundleV2Json.UnsupportedDistortionModelErrorCode}]");
        error.Should().Contain("不支持的畸变模型");
        error.Should().Contain(nameof(DistortionModelV2.KannalaBrandt));
        error.Should().NotContain("is not supported by this operator");
    }

    [Fact]
    public void TryRequireTransform2D_WithUnsupportedTransformModel_ShouldReturnChineseStructuredError()
    {
        var bundle = CreateTransform2DBundle(TransformModelV2.Homography);

        var result = CalibrationBundleV2Json.TryRequireTransform2D(
            bundle,
            [TransformModelV2.ScaleOffset],
            out _,
            out var error);

        result.Should().BeFalse();
        error.Should().Contain($"[{CalibrationBundleV2Json.UnsupportedTransform2DModelErrorCode}]");
        error.Should().Contain("不支持的二维变换模型");
        error.Should().Contain(nameof(TransformModelV2.Homography));
        error.Should().NotContain("is not supported by this operator");
    }

    private static CalibrationBundleV2 CreateIntrinsicsBundle(DistortionModelV2 distortionModel)
    {
        return new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.CameraIntrinsics,
            SourceFrame = "image",
            TargetFrame = "image.undistorted",
            Quality = new CalibrationQualityV2 { Accepted = true },
            Intrinsics = new CalibrationIntrinsicsV2
            {
                CameraMatrix =
                [
                    [500.0, 0.0, 160.0],
                    [0.0, 500.0, 120.0],
                    [0.0, 0.0, 1.0]
                ]
            },
            Distortion = new CalibrationDistortionV2
            {
                Model = distortionModel,
                Coefficients = [0.1, 0.01, 0.0, 0.0]
            }
        };
    }

    private static CalibrationBundleV2 CreateTransform2DBundle(TransformModelV2 transformModel)
    {
        return new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.PlanarTransform2D,
            TransformModel = transformModel,
            SourceFrame = "image",
            TargetFrame = "world",
            Quality = new CalibrationQualityV2 { Accepted = true },
            Transform2D = new CalibrationTransform2DV2
            {
                Model = transformModel,
                Matrix =
                [
                    [1.0, 0.0, 0.0],
                    [0.0, 1.0, 0.0],
                    [0.0, 0.0, 1.0]
                ]
            }
        };
    }
}
