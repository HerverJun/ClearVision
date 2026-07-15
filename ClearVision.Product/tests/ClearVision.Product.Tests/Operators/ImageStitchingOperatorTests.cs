using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Trait("Category", "Sprint6_Phase3")]
public class ImageStitchingOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeImageStitching()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.ImageStitching, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_ManualMode_ShouldReturnMergedImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "Manual" },
            { "OverlapPercent", 25.0 },
            { "BlendMode", "Linear" }
        });

        using var img1 = CreateImage1();
        using var img2 = CreateImage2();
        var inputs = new Dictionary<string, object>
        {
            { "Image1", img1 },
            { "Image2", img2 }
        };

        var result = await sut.ExecuteAsync(op, inputs);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.True(Convert.ToDouble(result.OutputData!["OverlapRatio"]) > 0);
        Assert.True(Convert.ToInt32(result.OutputData["Width"]) > 120);
        Assert.Equal("FeatherDistanceBlend", result.OutputData["BlendImplementation"]);
    }

    [Fact]
    public async Task ExecuteAsync_MultiBandMode_ShouldUseDifferentBlendImplementationThanLinear()
    {
        var sut = CreateSut();
        using var linearImg1 = CreateTexturedExposureImage(leftImage: true);
        using var linearImg2 = CreateTexturedExposureImage(leftImage: false);
        var linearInputs = new Dictionary<string, object>
        {
            { "Image1", linearImg1 },
            { "Image2", linearImg2 }
        };

        var linear = await sut.ExecuteAsync(CreateOperator(new Dictionary<string, object>
        {
            { "Method", "Manual" },
            { "OverlapPercent", 50.0 },
            { "BlendMode", "Linear" }
        }), linearInputs);

        using var multiBandImg1 = CreateTexturedExposureImage(leftImage: true);
        using var multiBandImg2 = CreateTexturedExposureImage(leftImage: false);
        var multiBandInputs = new Dictionary<string, object>
        {
            { "Image1", multiBandImg1 },
            { "Image2", multiBandImg2 }
        };

        var multiBand = await sut.ExecuteAsync(CreateOperator(new Dictionary<string, object>
        {
            { "Method", "Manual" },
            { "OverlapPercent", 50.0 },
            { "BlendMode", "MultiBand" }
        }), multiBandInputs);

        Assert.True(linear.IsSuccess);
        Assert.True(multiBand.IsSuccess);
        Assert.Equal("FeatherDistanceBlend", linear.OutputData!["BlendImplementation"]);
        Assert.Equal("LaplacianPyramidMultiBand", multiBand.OutputData!["BlendImplementation"]);

        var linearImage = Assert.IsType<ImageWrapper>(linear.OutputData["Image"]);
        var multiBandImage = Assert.IsType<ImageWrapper>(multiBand.OutputData["Image"]);
        using var diff = new Mat();
        Cv2.Absdiff(linearImage.GetMat(), multiBandImage.GetMat(), diff);
        var meanDiff = Cv2.Mean(diff);
        var totalMeanDiff = meanDiff.Val0 + meanDiff.Val1 + meanDiff.Val2;
        Assert.True(totalMeanDiff > 0.1, $"Expected MultiBand output to differ from Linear/Feather output, got mean diff {totalMeanDiff:F4}.");
    }

    [Fact]
    public void ValidateParameters_WithInvalidMethod_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Method", "HomographyOnly" } });
        Assert.False(sut.ValidateParameters(op).IsValid);
    }

    private static ImageStitchingOperator CreateSut()
    {
        return new ImageStitchingOperator(Substitute.For<ILogger<ImageStitchingOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("ImageStitching", OperatorType.ImageStitching, 0, 0);
        if (parameters != null)
        {
            foreach (var (k, v) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), k, k, string.Empty, "string", v));
            }
        }

        return op;
    }

    private static ImageWrapper CreateImage1()
    {
        var mat = new Mat(80, 120, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(20, 20, 60, 30), Scalar.White, -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateImage2()
    {
        var mat = new Mat(80, 120, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(10, 20, 60, 30), Scalar.White, -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateTexturedExposureImage(bool leftImage)
    {
        var mat = new Mat(96, 96, MatType.CV_8UC3, Scalar.Black);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var checker = ((x / 6) + (y / 6)) % 2 == 0 ? 70 : 0;
                var ramp = leftImage ? x : 95 - x;
                var exposure = leftImage ? 80 : 130;
                var value = (byte)Math.Clamp(exposure + checker + ramp, 0, 255);
                mat.Set(y, x, new Vec3b(value, (byte)Math.Clamp(value * 0.8, 0, 255), (byte)Math.Clamp(value * 0.6, 0, 255)));
            }
        }

        return new ImageWrapper(mat);
    }
}
