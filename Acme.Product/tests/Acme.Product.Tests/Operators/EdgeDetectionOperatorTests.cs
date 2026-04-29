using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace Acme.Product.Tests.Operators;

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
}
