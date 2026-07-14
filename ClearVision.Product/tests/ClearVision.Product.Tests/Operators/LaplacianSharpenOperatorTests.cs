using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class LaplacianSharpenOperatorTests
{
    private readonly LaplacianSharpenOperator _operator =
        new(Substitute.For<ILogger<LaplacianSharpenOperator>>());

    [Fact]
    public void OperatorType_ShouldBeLaplacianSharpen()
    {
        _operator.OperatorType.Should().Be(OperatorType.LaplacianSharpen);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnSharpenedImageAndMetadata()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("KernelSize", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Scale", 1.5, "double"));
        op.AddParameter(TestHelpers.CreateParameter("SharpenStrength", 1.2, "double"));

        using var image = TestHelpers.CreateGradientTestImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        outputImage.Width.Should().Be(200);
        outputImage.Height.Should().Be(200);
        result.OutputData["KernelSize"].Should().Be(3);
        result.OutputData["Scale"].Should().Be(1.5);
        result.OutputData["SharpenStrength"].Should().Be(1.2);
        result.OutputData["OutputMatType"].Should().Be("CV_8UC3");
        result.OutputData["ColorPolicy"].Should().Be("LuminanceBroadcast");
    }

    [Fact]
    public async Task ExecuteAsync_WithEvenKernelSize_ShouldNormalizeToOddKernel()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("KernelSize", 4, "int"));

        using var image = TestHelpers.CreateGradientTestImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["KernelSize"].Should().Be(5);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ExecuteAsync_ConstantImage_ShouldRemainUnchanged(int channels)
    {
        var type = channels == 1 ? MatType.CV_8UC1 : MatType.CV_8UC3;
        using var source = new Mat(9, 9, type, Scalar.All(83));
        using var input = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            ("KernelSize", 3, "int"),
            ("SharpenStrength", 2.0, "double"));

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(input));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        AssertImagesEqual(source, outputImage.GetMat());
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroStrength_ShouldBeExactIdentity()
    {
        using var source = CreateColorProbe();
        using var input = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            ("KernelSize", 3, "int"),
            ("Scale", 2.5, "double"),
            ("SharpenStrength", 0.0, "double"));

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(input));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        AssertImagesEqual(source, outputImage.GetMat());
    }

    [Fact]
    public async Task ExecuteAsync_SignedLaplacian_ShouldMatchIndependentKernelOracle()
    {
        using var source = CreateSignedResponseProbe();
        using var input = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            ("KernelSize", 1, "int"),
            ("Scale", 1.0, "double"),
            ("SharpenStrength", 1.0, "double"));

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(input));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        var output = outputImage.GetMat();
        var mismatches = new List<string>();
        for (var y = 1; y < source.Rows - 1; y++)
        {
            for (var x = 1; x < source.Cols - 1; x++)
            {
                var center = source.At<byte>(y, x);
                var signedLaplacian = source.At<byte>(y - 1, x) +
                                      source.At<byte>(y + 1, x) +
                                      source.At<byte>(y, x - 1) +
                                      source.At<byte>(y, x + 1) -
                                      (4 * center);
                var expected = SaturateToByte(center - signedLaplacian);
                var actual = output.At<byte>(y, x);
                if (expected != actual)
                {
                    mismatches.Add($"({x},{y}) expected={expected} actual={actual} src={center} laplacian={signedLaplacian}");
                }
            }
        }

        mismatches.Should().BeEmpty(
            $"the output must follow dst = src - strength * signed_laplacian; first mismatches: {string.Join("; ", mismatches.Take(5))}");
    }

    [Fact]
    public async Task ExecuteAsync_PositiveAndNegativeResponses_ShouldCorrectInOppositeDirections()
    {
        using var source = CreateSignedResponseProbe();
        using var input = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            ("KernelSize", 1, "int"),
            ("SharpenStrength", 1.0, "double"));

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(input));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        var output = outputImage.GetMat();
        var brightPeakNeighbor = output.At<byte>(2, 1);
        var darkPitNeighbor = output.At<byte>(4, 3);

        Assert.True(
            brightPeakNeighbor < 100 && darkPitNeighbor > 100,
            $"Expected a positive Laplacian response to darken and a negative response to brighten; " +
            $"actual bright-peak neighbor={brightPeakNeighbor}, dark-pit neighbor={darkPitNeighbor}.");
    }

    [Fact]
    public async Task ExecuteAsync_UndeclaredLegacyDelta_ShouldNotAffectRuntimeResult()
    {
        using var source = new Mat(7, 7, MatType.CV_8UC1, Scalar.All(100));
        using var baselineInput = new ImageWrapper(source.Clone());
        using var legacyInput = new ImageWrapper(source.Clone());
        var baselineOp = CreateOperator(("SharpenStrength", 1.0, "double"));
        var legacyOp = CreateOperator(
            ("SharpenStrength", 1.0, "double"),
            ("Delta", 50.0, "double"));

        var baseline = await _operator.ExecuteAsync(baselineOp, TestHelpers.CreateImageInputs(baselineInput));
        var legacy = await _operator.ExecuteAsync(legacyOp, TestHelpers.CreateImageInputs(legacyInput));

        baseline.IsSuccess.Should().BeTrue(baseline.ErrorMessage);
        legacy.IsSuccess.Should().BeTrue(legacy.ErrorMessage);
        using var baselineImage = baseline.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        using var legacyImage = legacy.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        AssertImagesEqual(baselineImage.GetMat(), legacyImage.GetMat());
    }

    [Theory]
    [InlineData("8UC1")]
    [InlineData("8UC3")]
    [InlineData("16UC1")]
    [InlineData("32FC1")]
    public async Task ExecuteAsync_ShouldPreserveDeclaredOutputDepthAndChannels(string caseId)
    {
        using var source = CreateDepthProbe(caseId);
        using var input = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            ("KernelSize", 1, "int"),
            ("SharpenStrength", 0.75, "double"));

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(input));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        var output = outputImage.GetMat();
        output.Type().Should().Be(source.Type());
        output.Channels().Should().Be(source.Channels());
        AssertFinite(output);
    }

    [Theory]
    [InlineData("64FC1")]
    [InlineData("64FC3")]
    public async Task ExecuteAsync_WithUnsupportedSixtyFourBitDepth_ShouldFailFast(string caseId)
    {
        using var source = CreateDepthProbe(caseId);
        using var input = new ImageWrapper(source.Clone());

        var result = await _operator.ExecuteAsync(CreateOperator(), TestHelpers.CreateImageInputs(input));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("IMAGE_DEPTH_UNSUPPORTED:");
        result.ErrorMessage.Should().Contain("OperatorType=LaplacianSharpen")
            .And.Contain($"InputMatType=CV_{caseId}")
            .And.Contain("Supported=CV_8UC1,CV_8UC3,CV_16UC1,CV_16UC3,CV_32FC1,CV_32FC3");
    }

    [Fact]
    public void ValidateParameters_WithKernelSizeOutOfRange_ShouldReturnInvalid()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("KernelSize", 9, "int"));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Metadata_ShouldDeclareEffectiveOutputsAndNoDeltaParameter()
    {
        var metadata = new OperatorMetadataScanner()
            .Scan()
            .Single(item => item.Type == OperatorType.LaplacianSharpen);

        metadata.Version.Should().Be("1.0.3");
        metadata.Parameters.Should().NotContain(parameter => parameter.Name == "Delta");
        metadata.Parameters.Single(parameter => parameter.Name == "KernelSize")
            .Description.Should().Contain("偶数").And.Contain("下一奇数");

        foreach (var output in new[] { "KernelSize", "Scale", "SharpenStrength", "OutputMatType", "ColorPolicy" })
        {
            metadata.OutputPorts.Should().Contain(port => port.Name == output && port.DataType != PortDataType.Any);
        }
    }

    private static Operator CreateOperator(params (string Name, object Value, string DataType)[] parameters)
    {
        var op = new Operator("LaplacianSharpen", OperatorType.LaplacianSharpen, 0, 0);
        foreach (var parameter in parameters)
        {
            op.AddParameter(TestHelpers.CreateParameter(parameter.Name, parameter.Value, parameter.DataType));
        }

        return op;
    }

    private static Mat CreateSignedResponseProbe()
    {
        var mat = new Mat(7, 7, MatType.CV_8UC1, Scalar.All(100));
        mat.Set(2, 2, (byte)120);
        mat.Set(4, 4, (byte)80);
        return mat;
    }

    private static Mat CreateColorProbe()
    {
        var mat = new Mat(7, 8, MatType.CV_8UC3);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                mat.Set(y, x, new Vec3b(
                    (byte)(30 + x * 7),
                    (byte)(50 + y * 9),
                    (byte)(90 + (x + y) * 5)));
            }
        }

        return mat;
    }

    private static Mat CreateDepthProbe(string caseId)
    {
        var mat = caseId switch
        {
            "8UC1" => new Mat(7, 7, MatType.CV_8UC1),
            "8UC3" => new Mat(7, 7, MatType.CV_8UC3),
            "16UC1" => new Mat(7, 7, MatType.CV_16UC1),
            "32FC1" => new Mat(7, 7, MatType.CV_32FC1),
            "64FC1" => new Mat(7, 7, MatType.CV_64FC1),
            "64FC3" => new Mat(7, 7, MatType.CV_64FC3),
            _ => throw new ArgumentOutOfRangeException(nameof(caseId), caseId, null)
        };

        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var value = 20 + x * 4 + y * 7 + ((x + y) % 3) * 11;
                switch (caseId)
                {
                    case "8UC1":
                        mat.Set(y, x, (byte)value);
                        break;
                    case "8UC3":
                        mat.Set(y, x, new Vec3b((byte)value, (byte)(value + 20), (byte)(value + 40)));
                        break;
                    case "16UC1":
                        mat.Set(y, x, (ushort)(value * 200));
                        break;
                    case "32FC1":
                        mat.Set(y, x, (float)(value / 10.0));
                        break;
                    case "64FC1":
                        mat.Set(y, x, value / 10.0);
                        break;
                    case "64FC3":
                        mat.Set(y, x, new Vec3d(value / 10.0, value / 8.0, value / 6.0));
                        break;
                }
            }
        }

        return mat;
    }

    private static byte SaturateToByte(int value)
    {
        return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
    }

    private static void AssertImagesEqual(Mat expected, Mat actual)
    {
        actual.Type().Should().Be(expected.Type());
        actual.Size().Should().Be(expected.Size());
        using var difference = new Mat();
        Cv2.Absdiff(expected, actual, difference);
        using var flattened = difference.Reshape(1);
        Cv2.CountNonZero(flattened).Should().Be(0);
    }

    private static void AssertFinite(Mat mat)
    {
        Cv2.Split(mat, out var channels);
        try
        {
            foreach (var channel in channels)
            {
                double min;
                double max;
                Cv2.MinMaxLoc(channel, out min, out max);
                double.IsFinite(min).Should().BeTrue();
                double.IsFinite(max).Should().BeTrue();
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
}
