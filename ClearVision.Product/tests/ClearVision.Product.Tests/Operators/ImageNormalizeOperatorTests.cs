using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[Trait("Category", "Sprint6_Phase3")]
public class ImageNormalizeOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeImageNormalize()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.ImageNormalize, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_MinMax_ShouldReturnImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "MinMax" },
            { "Alpha", 0.0 },
            { "Beta", 255.0 }
        });

        using var image = CreateGradientImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.True(result.OutputData!.ContainsKey("Image"));
    }

    [Fact]
    public async Task ExecuteAsync_ColorInput_ShouldPreserveColorSemanticsByDefault()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "MinMax" },
            { "ColorMode", "LumaOnly" }
        });

        using var image = CreateColorGradientImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var pixel = outputImage.GetMat().At<Vec3b>(20, 20);
        Assert.False(pixel.Item0 == pixel.Item1 && pixel.Item1 == pixel.Item2);
        Assert.Equal("LumaOnly", result.OutputData["ColorMode"]);
    }

    [Fact]
    public async Task ExecuteAsync_PerChannelMode_ShouldReturnThreeChannelImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "ZScore" },
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
    public async Task ExecuteAsync_ZScoreSingleChannel_ShouldReturnTrueFloatingStandardScores()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "ZScore" },
            { "Alpha", -100.0 },
            { "Beta", 100.0 }
        });

        using var image = CreateSingleChannelProbe("8UC1");
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var output = outputImage.GetMat();
        var (mean, sigma, min, max) = GetStatistics(output);
        var contractSatisfied = output.Type() == MatType.CV_32FC1 &&
                                Math.Abs(mean) <= 1e-5 &&
                                Math.Abs(sigma - 1.0) <= 1e-5 &&
                                min < 0.0 &&
                                max > 0.0;

        Assert.True(
            contractSatisfied,
            $"Expected CV_32FC1 z-scores with mean=0, sigma=1 and signed fractional values; " +
            $"actual type={output.Type()}, mean={mean:R}, sigma={sigma:R}, min={min:R}, max={max:R}.");
        Assert.Equal("ZScore", result.OutputData["Method"]);
        Assert.Equal("Gray", result.OutputData["ColorMode"]);
        Assert.Equal("CV_32FC1", result.OutputData["OutputMatType"]);
        Assert.Equal(false, result.OutputData["SigmaDegenerate"]);
    }

    [Fact]
    public async Task ExecuteAsync_ZScoreAndMinMax_ShouldProduceDistinctNumericMappings()
    {
        var sut = CreateSut();
        using var probe = CreateSingleChannelProbe("8UC1");
        using var source = probe.GetMat().Clone();
        using var minMaxInput = new ImageWrapper(source.Clone());
        using var zScoreInput = new ImageWrapper(source.Clone());

        var minMax = await sut.ExecuteAsync(
            CreateOperator(new Dictionary<string, object> { { "Method", "MinMax" } }),
            TestHelpers.CreateImageInputs(minMaxInput));
        var zScore = await sut.ExecuteAsync(
            CreateOperator(new Dictionary<string, object> { { "Method", "ZScore" } }),
            TestHelpers.CreateImageInputs(zScoreInput));

        Assert.True(minMax.IsSuccess, minMax.ErrorMessage);
        Assert.True(zScore.IsSuccess, zScore.ErrorMessage);
        using var minMaxImage = Assert.IsType<ImageWrapper>(minMax.OutputData!["Image"]);
        using var zScoreImage = Assert.IsType<ImageWrapper>(zScore.OutputData!["Image"]);
        using var minMax32 = new Mat();
        using var zScore32 = new Mat();
        minMaxImage.GetMat().ConvertTo(minMax32, MatType.CV_32FC1);
        zScoreImage.GetMat().ConvertTo(zScore32, MatType.CV_32FC1);

        var numericDifference = Cv2.Norm(minMax32, zScore32, NormTypes.L1);
        Assert.True(
            numericDifference > 1e-3,
            $"Expected MinMax and ZScore to be numerically distinct; actual L1 difference={numericDifference:R}.");
    }

    [Theory]
    [InlineData("8UC1")]
    [InlineData("16UC1")]
    [InlineData("32FC1")]
    public async Task ExecuteAsync_ZScore_ShouldSupportDeclaredSingleChannelInputDepths(string caseId)
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Method", "ZScore" } });
        using var image = CreateSingleChannelProbe(caseId);

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var output = outputImage.GetMat();
        var (mean, sigma, min, max) = GetStatistics(output);
        Assert.True(
            output.Type() == MatType.CV_32FC1 && Math.Abs(mean) <= 1e-5 && Math.Abs(sigma - 1.0) <= 1e-5,
            $"Input {caseId}: expected CV_32FC1 mean=0 sigma=1; " +
            $"actual type={output.Type()}, mean={mean:R}, sigma={sigma:R}, min={min:R}, max={max:R}.");
    }

    [Theory]
    [InlineData("8UC1")]
    [InlineData("16UC1")]
    [InlineData("32FC1")]
    public async Task ExecuteAsync_ZScoreConstantImage_ShouldReturnFiniteFloatingZeros(string caseId)
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Method", "ZScore" } });
        using var image = CreateConstantSingleChannelImage(caseId);

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var output = outputImage.GetMat();
        var (_, sigma, min, max) = GetStatistics(output);
        Assert.True(
            output.Type() == MatType.CV_32FC1 && sigma == 0.0 && min == 0.0 && max == 0.0,
            $"Input {caseId}: expected finite CV_32FC1 zeros; " +
            $"actual type={output.Type()}, sigma={sigma:R}, min={min:R}, max={max:R}.");
        Assert.Equal(true, result.OutputData["SigmaDegenerate"]);
    }

    [Theory]
    [InlineData("8UC3")]
    [InlineData("16UC3")]
    [InlineData("32FC3")]
    public async Task ExecuteAsync_ZScorePerChannel_ShouldStandardizeEachColorChannel(string caseId)
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "ZScore" },
            { "ColorMode", "PerChannel" }
        });
        using var image = CreateColorZScoreProbe(caseId);

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        var output = outputImage.GetMat();
        Assert.Equal(MatType.CV_32FC3, output.Type());
        Assert.Equal(3, output.Channels());

        Cv2.Split(output, out var channels);
        try
        {
            for (var index = 0; index < channels.Length; index++)
            {
                var (mean, sigma, min, max) = GetStatistics(channels[index]);
                Assert.True(
                    Math.Abs(mean) <= 1e-5 && Math.Abs(sigma - 1.0) <= 1e-5 && min < 0.0 && max > 0.0,
                    $"Input {caseId}, channel {index}: expected mean=0 sigma=1 with signed values; " +
                    $"actual mean={mean:R}, sigma={sigma:R}, min={min:R}, max={max:R}.");
            }
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ZScoreLumaOnlyColor_ShouldFailFastWithStableContractMessage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "ZScore" },
            { "ColorMode", "LumaOnly" }
        });
        using var image = CreateColorGradientImage();

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "ZScore with ColorMode=LumaOnly is not supported for 3-channel images; use ColorMode=PerChannel.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithSixteenBitColorInputInLumaOnlyMode_ShouldPreserveSixteenBitColorImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "MinMax" },
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
    public void ValidateParameters_WithInvalidMethod_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Method", "CLAHE" } });
        Assert.False(sut.ValidateParameters(op).IsValid);
    }

    [Fact]
    public void Metadata_ZScoreShouldDisableRangeParametersAndDeclareDiagnostics()
    {
        var metadata = new OperatorMetadataScanner()
            .Scan()
            .Single(item => item.Type == OperatorType.ImageNormalize);
        var states = OperatorParameterConstraintEvaluator.ResolveStates(
                metadata,
                new Dictionary<string, object?> { ["Method"] = "ZScore" })
            .ToDictionary(item => item.Constraint.Parameter, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in new[] { "Alpha", "Beta" })
        {
            Assert.True(states[parameter].EffectiveDisabled);
            Assert.True(states[parameter].EffectiveIgnored);
            Assert.False(states[parameter].EffectiveVisible);
        }

        foreach (var output in new[] { "Method", "ColorMode", "Channels", "OutputMatType", "SigmaDegenerate" })
        {
            Assert.Contains(metadata.OutputPorts, port => port.Name == output && port.DataType != PortDataType.Any);
        }
    }

    private static ImageNormalizeOperator CreateSut()
    {
        return new ImageNormalizeOperator(Substitute.For<ILogger<ImageNormalizeOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("ImageNormalize", OperatorType.ImageNormalize, 0, 0);
        if (parameters != null)
        {
            foreach (var (k, v) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), k, k, string.Empty, "string", v));
            }
        }

        return op;
    }

    private static ImageWrapper CreateGradientImage()
    {
        var mat = new Mat(80, 100, MatType.CV_8UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var v = (byte)(x * 255 / mat.Cols);
                mat.Set(y, x, new Vec3b(v, v, v));
            }
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateSingleChannelProbe(string caseId)
    {
        var values = new[] { 10, 20, 35, 50, 80, 120, 170, 230 };
        var mat = caseId switch
        {
            "8UC1" => new Mat(2, 4, MatType.CV_8UC1),
            "16UC1" => new Mat(2, 4, MatType.CV_16UC1),
            "32FC1" => new Mat(2, 4, MatType.CV_32FC1),
            _ => throw new ArgumentOutOfRangeException(nameof(caseId), caseId, null)
        };

        for (var index = 0; index < values.Length; index++)
        {
            var y = index / mat.Cols;
            var x = index % mat.Cols;
            switch (caseId)
            {
                case "8UC1":
                    mat.Set(y, x, (byte)values[index]);
                    break;
                case "16UC1":
                    mat.Set(y, x, (ushort)(values[index] * 200));
                    break;
                case "32FC1":
                    mat.Set(y, x, (float)(values[index] / 37.0 - 3.0));
                    break;
            }
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateConstantSingleChannelImage(string caseId)
    {
        var mat = caseId switch
        {
            "8UC1" => new Mat(5, 7, MatType.CV_8UC1, Scalar.All(37)),
            "16UC1" => new Mat(5, 7, MatType.CV_16UC1, Scalar.All(12345)),
            "32FC1" => new Mat(5, 7, MatType.CV_32FC1, Scalar.All(2.5)),
            _ => throw new ArgumentOutOfRangeException(nameof(caseId), caseId, null)
        };
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateColorZScoreProbe(string caseId)
    {
        var mat = caseId switch
        {
            "8UC3" => new Mat(4, 5, MatType.CV_8UC3),
            "16UC3" => new Mat(4, 5, MatType.CV_16UC3),
            "32FC3" => new Mat(4, 5, MatType.CV_32FC3),
            _ => throw new ArgumentOutOfRangeException(nameof(caseId), caseId, null)
        };

        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var index = y * mat.Cols + x;
                switch (caseId)
                {
                    case "8UC3":
                        mat.Set(y, x, new Vec3b(
                            (byte)(10 + index * 3),
                            (byte)(30 + index * 5),
                            (byte)(220 - index * 4)));
                        break;
                    case "16UC3":
                        mat.Set(y, x, new Vec3w(
                            (ushort)(1000 + index * 800),
                            (ushort)(5000 + index * 1200),
                            (ushort)(50000 - index * 900)));
                        break;
                    case "32FC3":
                        mat.Set(y, x, new Vec3f(
                            -2.0f + index * 0.25f,
                            1.0f + index * 0.75f,
                            8.0f - index * 0.4f));
                        break;
                }
            }
        }

        return new ImageWrapper(mat);
    }

    private static (double Mean, double Sigma, double Min, double Max) GetStatistics(Mat mat)
    {
        Cv2.MeanStdDev(mat, out var mean, out var stdDev);
        double min;
        double max;
        Cv2.MinMaxLoc(mat, out min, out max);
        return (mean.Val0, stdDev.Val0, min, max);
    }

    private static ImageWrapper CreateColorGradientImage()
    {
        var mat = new Mat(60, 80, MatType.CV_8UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var blue = (byte)Math.Clamp(x * 255 / Math.Max(1, mat.Cols - 1), 0, 255);
                var green = (byte)Math.Clamp(y * 255 / Math.Max(1, mat.Rows - 1), 0, 255);
                var red = (byte)Math.Clamp(255 - ((x + y) * 255 / Math.Max(1, mat.Rows + mat.Cols - 2)), 0, 255);
                mat.Set(y, x, new Vec3b(blue, green, red));
            }
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateColorGradientImage16U()
    {
        var mat = new Mat(60, 80, MatType.CV_16UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var blue = (ushort)Math.Clamp(x * ushort.MaxValue / Math.Max(1, mat.Cols - 1), 0, ushort.MaxValue);
                var green = (ushort)Math.Clamp(y * ushort.MaxValue / Math.Max(1, mat.Rows - 1), 0, ushort.MaxValue);
                var red = (ushort)Math.Clamp(ushort.MaxValue - ((x + y) * ushort.MaxValue / Math.Max(1, mat.Rows + mat.Cols - 2)), 0, ushort.MaxValue);
                mat.Set(y, x, new Vec3w(blue, green, red));
            }
        }

        return new ImageWrapper(mat);
    }
}

