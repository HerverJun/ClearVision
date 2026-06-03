using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class EdgeDetectionOperatorTests
{
    private readonly CannyEdgeOperator _operator =
        new(Substitute.For<ILogger<CannyEdgeOperator>>());

    [Fact]
    public void OperatorType_ShouldMapToEdgeDetection()
    {
        _operator.OperatorType.Should().Be(OperatorType.EdgeDetection);
    }

    [Fact]
    public async Task ExecuteAsync_WithShapeImage_ShouldReturnEdgeOutputs()
    {
        using var image = TestHelpers.CreateShapeTestImage();
        var result = await _operator.ExecuteAsync(CreateOperator(), TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Edges");
        result.OutputData!["Edges"].Should().BeOfType<byte[]>();
        result.OutputData["AutoThreshold"].Should().Be(false);
    }

    [Fact]
    public async Task ExecuteAsync_WithBgraImage_ShouldConvertToGrayAndReturnEdgeOutputs()
    {
        using var image = CreateBgraShapeImage();
        var result = await _operator.ExecuteAsync(CreateOperator(), TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Edges");
        result.OutputData!["Edges"].Should().BeOfType<byte[]>();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnsupportedChannelCount_ShouldReturnClearFailure()
    {
        using var image = CreateTwoChannelImage();
        var result = await _operator.ExecuteAsync(CreateOperator(), TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported image channel count");
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoThreshold_ShouldExposeThresholdsUsed()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("AutoThreshold", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("AutoThresholdSigma", 0.5, "double"));
        op.AddParameter(TestHelpers.CreateParameter("GaussianKernelSize", 4, "int"));

        using var image = TestHelpers.CreateGradientTestImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToBoolean(result.OutputData!["AutoThreshold"]).Should().BeTrue();
        Convert.ToDouble(result.OutputData["Threshold2Used"])
            .Should().BeGreaterThan(Convert.ToDouble(result.OutputData["Threshold1Used"]));
    }

    [Fact]
    public async Task ExecuteAsync_WithGradientPercentileAutoThreshold_ShouldExposeEdgeDensity()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("AutoThreshold", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("AutoThresholdStrategy", "GradientPercentile", "string"));

        using var image = TestHelpers.CreateShapeTestImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["AutoThresholdStrategy"].Should().Be("GradientPercentile");
        Convert.ToDouble(result.OutputData["Threshold2Used"])
            .Should().BeGreaterThan(Convert.ToDouble(result.OutputData["Threshold1Used"]));
        Convert.ToDouble(result.OutputData["EdgePixelRatio"]).Should().BeGreaterThan(0.0);
    }

    [Fact]
    public async Task ExecuteAsync_WithLowContrastNoisyRectangle_ShouldKeepSparseUsableEdges()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("Threshold1", 6.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Threshold2", 12.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("EnableGaussianBlur", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("GaussianKernelSize", 7, "int"));

        using var image = CreateLowContrastNoisyRectangleImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var edgeDensity = Convert.ToDouble(result.OutputData!["EdgePixelRatio"]);
        edgeDensity.Should().BeGreaterThan(0.003);
        edgeDensity.Should().BeLessThan(0.08);
        Convert.ToDouble(result.OutputData["Threshold2Used"])
            .Should().BeGreaterThan(Convert.ToDouble(result.OutputData["Threshold1Used"]));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutImage_ShouldReturnFailure()
    {
        var result = await _operator.ExecuteAsync(CreateOperator(), new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    private static Operator CreateOperator()
    {
        return new Operator("EdgeDetection", OperatorType.EdgeDetection, 0, 0);
    }

    private static ImageWrapper CreateBgraShapeImage()
    {
        var mat = new Mat(120, 120, MatType.CV_8UC4, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(20, 20, 40, 40), new Scalar(255, 255, 255, 255), -1);
        Cv2.Circle(mat, new Point(85, 75), 18, new Scalar(180, 180, 180, 255), -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateTwoChannelImage()
    {
        var mat = new Mat(32, 32, MatType.CV_8UC2, Scalar.All(128));
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateLowContrastNoisyRectangleImage()
    {
        var mat = new Mat(120, 180, MatType.CV_8UC1, Scalar.All(96));
        Cv2.Rectangle(mat, new Rect(45, 30, 90, 55), Scalar.All(112), -1);

        var rng = new Random(223);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var noise = rng.Next(-2, 3);
                var value = Math.Clamp(mat.At<byte>(y, x) + noise, 0, 255);
                mat.Set(y, x, (byte)value);
            }
        }

        return new ImageWrapper(mat);
    }
}
