using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class BlobDetectionOperatorTests
{
    private readonly BlobDetectionOperator _operator;

    public BlobDetectionOperatorTests()
    {
        _operator = new BlobDetectionOperator(Substitute.For<ILogger<BlobDetectionOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeBlobAnalysis()
    {
        _operator.OperatorType.Should().Be(OperatorType.BlobAnalysis);
    }

    [Fact]
    public void Metadata_ShouldDescribeBlobListsWithoutContourOrAnyMasquerading()
    {
        var factory = new OperatorFactory();
        var metadata = factory.GetMetadata(OperatorType.BlobAnalysis)!;
        var labelingMetadata = factory.GetMetadata(OperatorType.BlobLabeling)!;

        metadata.OutputPorts.Should().ContainSingle(port =>
            port.Name == "Blobs" &&
            port.DataType == PortDataType.BlobList &&
            port.Description!.Contains("不是 Contour 或 Region", StringComparison.Ordinal));
        metadata.OutputPorts.Should().ContainSingle(port =>
            port.Name == "BlobFeatures" &&
            port.DataType == PortDataType.BlobFeatureList &&
            port.Description!.Contains("不是轮廓或像素区域", StringComparison.Ordinal));
        metadata.OutputPorts.Should().ContainSingle(port =>
            port.Name == "BlobCount" && port.DataType == PortDataType.Integer);
        metadata.OutputPorts.Single(port => port.Name == "Blobs").DataType.Should().NotBe(PortDataType.Contour);
        metadata.OutputPorts.Single(port => port.Name == "BlobFeatures").DataType.Should().NotBe(PortDataType.Any);
        labelingMetadata.InputPorts.Should().ContainSingle(port =>
            port.Name == "Blobs" && port.DataType == PortDataType.BlobList);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnSuccess()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        using var image = TestHelpers.CreateTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);
        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Image");
        result.OutputData.Should().ContainKey("Blobs");
        result.OutputData.Should().ContainKey("BlobFeatures");
        result.OutputData.Should().ContainKey("BlobCount");
        result.OutputData!["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>();
        result.OutputData["BlobFeatures"].Should().BeOfType<List<Dictionary<string, object>>>()
            .Which.Should().BeEmpty("detailed features are opt-in but the output structure remains stable");
    }

    [Fact]
    public async Task ExecuteAsync_WithDetailedFeatures_ShouldKeepBlobResultsAndFeatureTableDistinct()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("OutputDetailedFeatures", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 10, "int"));

        using var image = TestHelpers.CreateShapeTestImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var blobs = result.OutputData!["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        var features = result.OutputData["BlobFeatures"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;

        Convert.ToInt32(result.OutputData["BlobCount"]).Should().Be(blobs.Count);
        features.Should().HaveCount(blobs.Count);
        features.Should().NotBeSameAs(blobs);
        features.Should().AllSatisfy(item =>
        {
            item.Should().ContainKey("BlobId");
            item.Should().ContainKey("Features");
            item["Features"].Should().BeOfType<Dictionary<string, object>>();
        });
        blobs.Should().OnlyContain(item =>
            item.ContainsKey("Id") && item.ContainsKey("Area") && item.ContainsKey("Width") && item.ContainsKey("Height"));
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithMinRectangularity_ShouldFilterOutRoundBlobs()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinRectangularity", 0.9, "double"));

        using var image = TestHelpers.CreateShapeTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);

        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        blobs.Should().HaveCount(1);
        Convert.ToDouble(blobs[0]["Rectangularity"]).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public async Task ExecuteAsync_WithSyntheticCircle_ShouldHaveHighCircularity()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 300000, "int"));

        using var mat = new Mat(512, 512, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(mat, new Point(256, 256), 200, Scalar.White, -1);

        var inputs = TestHelpers.CreateImageInputs(new ImageWrapper(mat));
        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);

        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        Convert.ToDouble(blobs[0]["Circularity"]).Should().BeGreaterThan(0.99);
    }

    [Fact]
    public async Task ExecuteAsync_WithSyntheticRectangle_ShouldHaveHighRectangularity()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);

        using var mat = new Mat(512, 512, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(156, 206, 200, 100), Scalar.White, -1);

        var inputs = TestHelpers.CreateImageInputs(new ImageWrapper(mat));
        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);

        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        Convert.ToDouble(blobs[0]["Rectangularity"]).Should().BeGreaterThan(0.95);
    }

    [Fact]
    public async Task ExecuteAsync_WithLowIntensityBackground_ShouldNotTreatAllNonZeroPixelsAsForeground()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 10000, "int"));

        using var mat = new Mat(256, 256, MatType.CV_8UC1, new Scalar(10));
        Cv2.Circle(mat, new Point(128, 128), 30, new Scalar(220), -1);

        var inputs = TestHelpers.CreateImageInputs(new ImageWrapper(mat));
        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);

        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        blobs.Should().HaveCount(1);
        Convert.ToDouble(blobs[0]["Area"]).Should().BeLessThan(10000);
    }

    [Fact]
    public async Task ExecuteAsync_WithHueWrapAroundFilter_ShouldDetectRedAcrossZeroBoundary()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 10000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("EnableColorFilter", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("HueLow", 170, "int"));
        op.AddParameter(TestHelpers.CreateParameter("HueHigh", 10, "int"));
        op.AddParameter(TestHelpers.CreateParameter("SatLow", 100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("SatHigh", 255, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ValLow", 100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ValHigh", 255, "int"));

        using var mat = new Mat(200, 200, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(mat, new Point(60, 100), 24, new Scalar(0, 0, 255), -1);
        Cv2.Circle(mat, new Point(140, 100), 24, new Scalar(0, 255, 0), -1);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(new ImageWrapper(mat)));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);

        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        Convert.ToDouble(blobs[0]["CenterX"]).Should().BeLessThan(100.0);
    }

    [Fact]
    public async Task ExecuteAsync_WithMaskAndSourceImage_ShouldDetectFromMaskAndRenderOnSource()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 10, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 10000, "int"));

        using var mask = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mask, new Rect(30, 20, 20, 10), Scalar.White, -1);
        using var source = new Mat(100, 100, MatType.CV_8UC3, new Scalar(16, 32, 48));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = new ImageWrapper(mask),
            ["SourceImage"] = new ImageWrapper(source)
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);

        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        Convert.ToDouble(blobs[0]["CenterX"]).Should().BeApproximately(39.5, 1.0);
        Convert.ToDouble(blobs[0]["CenterY"]).Should().BeApproximately(24.5, 1.0);

        var outputImage = result.OutputData["Image"].Should().BeOfType<ImageWrapper>().Subject;
        outputImage.Width.Should().Be(100);
        outputImage.Height.Should().Be(100);
        outputImage.Channels.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptySourceImage_ShouldFallBackToInputImage()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 10, "int"));

        using var mask = new Mat(80, 80, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(mask, new Point(40, 40), 8, Scalar.White, -1);

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = new ImageWrapper(mask),
            ["SourceImage"] = new ImageWrapper(new Mat())
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);
        var outputImage = result.OutputData["Image"].Should().BeOfType<ImageWrapper>().Subject;
        outputImage.Width.Should().Be(80);
        outputImage.Height.Should().Be(80);
        outputImage.Channels.Should().Be(3);
    }
}
