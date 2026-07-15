using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Preprocessing, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "operator-quality")]
[Trait("Category", "Sprint5_Phase2")]
public class ShadingCorrectionOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeShadingCorrection()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.ShadingCorrection, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithGaussianModel_ShouldReturnCorrectedImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "GaussianModel" },
            { "KernelSize", 31 }
        });

        using var image = CreateGradientImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var outputMat = outputImage.GetMat();
        Assert.Equal(200, outputMat.Width);
        Assert.Equal(120, outputMat.Height);
    }

    [Fact]
    public async Task ExecuteAsync_WithColorInput_ShouldPreserveColorSemanticsByDefault()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "GaussianModel" },
            { "KernelSize", 31 },
            { "ColorMode", "LumaOnly" }
        });

        using var image = CreateColorGradientImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var pixel = outputImage.GetMat().At<Vec3b>(30, 40);
        Assert.False(pixel.Item0 == pixel.Item1 && pixel.Item1 == pixel.Item2);
        Assert.Equal("LumaOnly", result.OutputData["ColorMode"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithPerChannelColorMode_ShouldKeepThreeChannels()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "GaussianModel" },
            { "KernelSize", 31 },
            { "ColorMode", "PerChannel" }
        });

        using var image = CreateColorGradientImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        Assert.Equal(3, outputImage.GetMat().Channels());
        Assert.Equal("PerChannel", result.OutputData["ColorMode"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithSixteenBitColorInputInLumaOnlyMode_ShouldPreserveSixteenBitColorImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "GaussianModel" },
            { "KernelSize", 31 },
            { "ColorMode", "LumaOnly" }
        });

        using var image = CreateColorGradientImage16U();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var outputMat = outputImage.GetMat();
        Assert.Equal(MatType.CV_16UC3, outputMat.Type());
        Assert.Equal(3, outputMat.Channels());

        var pixel = outputMat.At<Vec3w>(30, 40);
        Assert.False(pixel.Item0 == pixel.Item1 && pixel.Item1 == pixel.Item2);
        Assert.Equal("LumaOnly", result.OutputData["ColorMode"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithThirtyTwoBitColorInputInLumaOnlyMode_ShouldPreserveThirtyTwoBitColorImage()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "GaussianModel",
            ["KernelSize"] = 31,
            ["ColorMode"] = "LumaOnly"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_32FC3, new Scalar(0.2, 0.4, 0.6)));

        var result = await CreateSut().ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        Assert.Equal(MatType.CV_32FC3, output.GetMat().Type());
    }

    [Fact]
    public async Task ExecuteAsync_WithSixtyFourBitColorInputInLumaOnlyMode_ShouldRejectBeforeNativeColorConversion()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "GaussianModel",
            ["KernelSize"] = 31,
            ["ColorMode"] = "LumaOnly"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_64FC3, new Scalar(0.2, 0.4, 0.6)));

        var result = await CreateSut().ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.False(result.IsSuccess);
        Assert.StartsWith("IMAGE_MODE_DEPTH_UNSUPPORTED", result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenCVException", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Assertion failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(-215:", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithSixtyFourBitColorInputInPerChannelMode_ShouldPreserveSixtyFourBitColorImage()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "GaussianModel",
            ["KernelSize"] = 31,
            ["ColorMode"] = "PerChannel"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_64FC3, new Scalar(0.2, 0.4, 0.6)));

        var result = await CreateSut().ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        Assert.Equal(MatType.CV_64FC3, output.GetMat().Type());
    }

    [Fact]
    public async Task ExecuteAsync_WithSixtyFourBitGrayInput_ShouldPreserveSixtyFourBitSingleChannelImage()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "GaussianModel",
            ["KernelSize"] = 31,
            ["ColorMode"] = "LumaOnly"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_64FC1, Scalar.All(0.4)));

        var result = await CreateSut().ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        Assert.Equal(MatType.CV_64FC1, output.GetMat().Type());
        Assert.Equal("Gray", result.OutputData["ColorMode"]);
    }

    [Fact]
    public async Task ExecuteAsync_DivideByBackgroundWithCompatibleSharedGrayBackground_ShouldSucceed()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "DivideByBackground",
            ["ColorMode"] = "PerChannel"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_16UC3, Scalar.All(4000)));
        using var background = new ImageWrapper(new Mat(32, 48, MatType.CV_16UC1, Scalar.All(2000)));

        var result = await CreateSut().ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["Background"] = background
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        Assert.Equal(MatType.CV_16UC3, output.GetMat().Type());
    }

    [Fact]
    public async Task ExecuteAsync_DivideByBackgroundWithMismatchedSize_ShouldFailClosed()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "DivideByBackground",
            ["ColorMode"] = "PerChannel"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_8UC3, Scalar.All(40)));
        using var background = new ImageWrapper(new Mat(16, 48, MatType.CV_8UC3, Scalar.All(20)));

        var result = await CreateSut().ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["Background"] = background
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("IMAGE_BACKGROUND_SIZE_MISMATCH", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DivideByBackgroundWithMismatchedDepth_ShouldFailClosed()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "DivideByBackground",
            ["ColorMode"] = "PerChannel"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_16UC3, Scalar.All(4000)));
        using var background = new ImageWrapper(new Mat(32, 48, MatType.CV_32FC3, Scalar.All(20)));

        var result = await CreateSut().ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["Background"] = background
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("IMAGE_BACKGROUND_DEPTH_MISMATCH", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DivideByBackgroundForGrayInputWithColorBackground_ShouldFailClosed()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "DivideByBackground",
            ["ColorMode"] = "LumaOnly"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_8UC1, Scalar.All(40)));
        using var background = new ImageWrapper(new Mat(32, 48, MatType.CV_8UC3, Scalar.All(20)));

        var result = await CreateSut().ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["Background"] = background
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("IMAGE_BACKGROUND_CHANNEL_MISMATCH", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundProvidedForGaussianModel_ShouldRejectUnusedMode()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Method"] = "GaussianModel",
            ["ColorMode"] = "PerChannel"
        });
        using var image = new ImageWrapper(new Mat(32, 48, MatType.CV_8UC3, Scalar.All(40)));
        using var background = new ImageWrapper(new Mat(32, 48, MatType.CV_8UC3, Scalar.All(20)));

        var result = await CreateSut().ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["Background"] = background
        });

        Assert.False(result.IsSuccess);
        Assert.StartsWith("IMAGE_BACKGROUND_MODE_UNSUPPORTED", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DivideByBackgroundWithoutBackground_ShouldReturnFailure()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Method", "DivideByBackground" } });

        using var image = CreateGradientImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateParameters_WithInvalidMethod_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Method", "Homomorphic" } });

        var validation = sut.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    private static ShadingCorrectionOperator CreateSut()
    {
        return new ShadingCorrectionOperator(Substitute.For<ILogger<ShadingCorrectionOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("ShadingCorrection", OperatorType.ShadingCorrection, 0, 0);

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
            }
        }

        return op;
    }

    private static ImageWrapper CreateGradientImage()
    {
        var mat = new Mat(120, 200, MatType.CV_8UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var value = (byte)Math.Clamp((x * 255) / mat.Cols, 0, 255);
                mat.Set(y, x, new Vec3b(value, value, value));
            }
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateColorGradientImage()
    {
        var mat = new Mat(120, 200, MatType.CV_8UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var blue = (byte)Math.Clamp(x * 255 / Math.Max(1, mat.Cols - 1), 0, 255);
                var green = (byte)Math.Clamp(y * 255 / Math.Max(1, mat.Rows - 1), 0, 255);
                var red = (byte)Math.Clamp((x + y) * 255 / Math.Max(1, mat.Rows + mat.Cols - 2), 0, 255);
                mat.Set(y, x, new Vec3b(blue, green, red));
            }
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateColorGradientImage16U()
    {
        var mat = new Mat(120, 200, MatType.CV_16UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var blue = (ushort)Math.Clamp(x * ushort.MaxValue / Math.Max(1, mat.Cols - 1), 0, ushort.MaxValue);
                var green = (ushort)Math.Clamp(y * ushort.MaxValue / Math.Max(1, mat.Rows - 1), 0, ushort.MaxValue);
                var red = (ushort)Math.Clamp((x + y) * ushort.MaxValue / Math.Max(1, mat.Rows + mat.Cols - 2), 0, ushort.MaxValue);
                mat.Set(y, x, new Vec3w(blue, green, red));
            }
        }

        return new ImageWrapper(mat);
    }
}
