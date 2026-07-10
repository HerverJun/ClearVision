using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
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
            item.Should().ContainKeys("Id", "Area", "Circularity", "CenterX");
            item.Should().ContainKey("BlobId");
            item.Should().ContainKey("Features");
            item["Features"].Should().BeOfType<Dictionary<string, object>>();
        });
        features[0].Should().NotBeSameAs(blobs[0]);
        Convert.ToInt32(features[0]["Id"]).Should().Be(Convert.ToInt32(blobs[0]["Id"]));
        Convert.ToInt32(features[0]["BlobId"]).Should().Be(Convert.ToInt32(blobs[0]["Id"]));

        var nestedFeatures = features[0]["Features"].Should().BeOfType<Dictionary<string, object>>().Subject;
        foreach (var field in new[] { "Area", "Circularity", "CenterX" })
        {
            Convert.ToDouble(features[0][field]).Should().Be(Convert.ToDouble(nestedFeatures[field]));
        }

        blobs.Should().OnlyContain(item =>
            item.ContainsKey("Id") && item.ContainsKey("Area") && item.ContainsKey("Width") && item.ContainsKey("Height"));
    }

    [Fact]
    public async Task ExecuteAsync_WithDetailedFeatures_ShouldExposeLegacyPathsToJsonExtractor()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("OutputDetailedFeatures", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 10, "int"));

        using var image = TestHelpers.CreateShapeTestImage();
        var blobResult = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        blobResult.IsSuccess.Should().BeTrue(blobResult.ErrorMessage);
        var features = blobResult.OutputData!["BlobFeatures"]
            .Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        features.Should().NotBeEmpty();

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["BlobFeatures"] = features
        });
        var extractor = new JsonExtractorOperator(Substitute.For<ILogger<JsonExtractorOperator>>());

        foreach (var field in new[] { "Area", "Circularity", "CenterX" })
        {
            var extractOp = new Operator($"extract-{field}", OperatorType.JsonExtractor, 0, 0);
            extractOp.AddParameter(TestHelpers.CreateParameter("JsonPath", $"$.BlobFeatures[0].{field}", "string"));
            extractOp.AddParameter(TestHelpers.CreateParameter("OutputType", "Double", "string"));

            var extracted = await extractor.ExecuteAsync(extractOp, new Dictionary<string, object> { ["Json"] = json });

            extracted.IsSuccess.Should().BeTrue(extracted.ErrorMessage);
            Convert.ToDouble(extracted.OutputData!["Value"])
                .Should().Be(Convert.ToDouble(features[0][field]), because: $"the legacy path for {field} must remain readable");
        }

        var nestedOp = new Operator("extract-nested-area", OperatorType.JsonExtractor, 0, 0);
        nestedOp.AddParameter(TestHelpers.CreateParameter("JsonPath", "$.BlobFeatures[0].Features.Area", "string"));
        nestedOp.AddParameter(TestHelpers.CreateParameter("OutputType", "Double", "string"));
        var nestedExtracted = await extractor.ExecuteAsync(nestedOp, new Dictionary<string, object> { ["Json"] = json });

        nestedExtracted.IsSuccess.Should().BeTrue(nestedExtracted.ErrorMessage);
        Convert.ToDouble(nestedExtracted.OutputData!["Value"])
            .Should().Be(Convert.ToDouble(features[0]["Area"]));
    }

    [Fact]
    public async Task ExecuteAsync_WithDetailedFeaturesDisabled_ShouldAlwaysReturnEmptyFeatureList()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("OutputDetailedFeatures", false, "bool"));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var image = TestHelpers.CreateShapeTestImage();
            var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputData.Should().ContainKey("BlobFeatures");
            result.OutputData!["BlobFeatures"]
                .Should().BeOfType<List<Dictionary<string, object>>>()
                .Which.Should().BeEmpty();
        }
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepOnlyBlobWithinMaxArea_AndCountOnlyFilteredBlobs()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 200, "int"));

        using var image = CreateTwoAreaBlobImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);
        var blobs = result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Subject;
        blobs.Should().ContainSingle();
        Convert.ToInt32(blobs[0]["Area"]).Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepBlobWhenMaxAreaEqualsItsArea()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 100, "int"));

        using var image = CreateTwoAreaBlobImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);
        Convert.ToInt32(((List<Dictionary<string, object>>)result.OutputData["Blobs"])[0]["Area"]).Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepBlobWhenMinAreaEqualsItsArea()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));

        using var image = CreateTwoAreaBlobImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(2);
        ((List<Dictionary<string, object>>)result.OutputData["Blobs"])
            .Select(blob => Convert.ToInt32(blob["Area"]))
            .Should().Contain(100);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void ValidateParameters_ShouldKeepExistingStrictMinAreaLessThanMaxAreaContract(int minArea, int maxArea)
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", minArea, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", maxArea, "int"));

        var validation = _operator.ValidateParameters(op);

        validation.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_ShouldTreatEmptyAsNoFilter_AndApplyValidExpressions()
    {
        var emptyFilter = new Operator("empty", OperatorType.BlobAnalysis, 0, 0);
        emptyFilter.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        emptyFilter.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        emptyFilter.AddParameter(TestHelpers.CreateParameter("FeatureFilter", string.Empty, "string"));
        OperatorExecutionOutput emptyResult;
        using (var image = CreateTwoAreaBlobImage())
        {
            emptyResult = await _operator.ExecuteAsync(emptyFilter, TestHelpers.CreateImageInputs(image));
        }

        emptyResult.IsSuccess.Should().BeTrue(emptyResult.ErrorMessage);
        Convert.ToInt32(emptyResult.OutputData!["BlobCount"]).Should().Be(2);

        var passingFilter = new Operator("passing", OperatorType.BlobAnalysis, 0, 0);
        passingFilter.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        passingFilter.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        passingFilter.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area >= 100", "string"));
        OperatorExecutionOutput passingResult;
        using (var image = CreateTwoAreaBlobImage())
        {
            passingResult = await _operator.ExecuteAsync(passingFilter, TestHelpers.CreateImageInputs(image));
        }

        passingResult.IsSuccess.Should().BeTrue(passingResult.ErrorMessage);
        Convert.ToInt32(passingResult.OutputData!["BlobCount"]).Should().Be(2);

        var rejectingFilter = new Operator("rejecting", OperatorType.BlobAnalysis, 0, 0);
        rejectingFilter.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        rejectingFilter.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        rejectingFilter.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area > 1000", "string"));
        OperatorExecutionOutput rejectingResult;
        using (var image = CreateTwoAreaBlobImage())
        {
            rejectingResult = await _operator.ExecuteAsync(rejectingFilter, TestHelpers.CreateImageInputs(image));
        }

        rejectingResult.IsSuccess.Should().BeTrue(rejectingResult.ErrorMessage);
        Convert.ToInt32(rejectingResult.OutputData!["BlobCount"]).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_ShouldReturnClearFailureForInvalidExpression()
    {
        var op = new Operator("test", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area >", "string"));

        using var image = CreateTwoAreaBlobImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("FeatureFilter 表达式无效");
        result.ErrorMessage.Should().Contain("Area >= 100");
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_InvalidSyntax_ShouldFailForBlankImage()
    {
        var op = new Operator("blank-invalid", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area >", "string"));

        using var image = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(new ImageWrapper(image)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("表达式意外结束");
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_InvalidSyntax_ShouldFailWhenMaxAreaExcludesAllBlobs()
    {
        var op = new Operator("excluded-invalid", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 50, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area >", "string"));

        using var image = CreateTwoAreaBlobImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("表达式意外结束");
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_UnknownField_ShouldFailForBlankImage()
    {
        var op = new Operator("blank-unknown-field", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "UnknownField > 1", "string"));

        using var image = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(new ImageWrapper(image)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("未知 FeatureFilter 字段");
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_ValidExpression_ShouldSucceedWithZeroBlobs()
    {
        var op = new Operator("blank-valid", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area >= 1 && Width > 0", "string"));

        using var image = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(new ImageWrapper(image)));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_FeatureFilter_ValidExpression_ShouldKeepNormalBlobResults()
    {
        var op = new Operator("normal-valid", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 1000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FeatureFilter", "Area >= 100 && Width > 0", "string"));

        using var image = CreateTwoAreaBlobImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(2);
        result.OutputData["Blobs"].Should().BeOfType<List<Dictionary<string, object>>>().Which.Should().HaveCount(2);
    }

    [Fact]
    public void Metadata_FeatureFilter_ShouldBeOptionalAndDocumentSupportedFields()
    {
        var metadata = new OperatorFactory().GetMetadata(OperatorType.BlobAnalysis)!;
        var featureFilter = metadata.Parameters.Single(parameter => parameter.Name == "FeatureFilter");

        featureFilter.DisplayName.Should().Be("特征过滤表达式");
        featureFilter.IsRequired.Should().BeFalse();
        featureFilter.Description.Should().ContainAll("Area", "Circularity", "CenterX", "HoleCount", "Area >= 100");
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

    private static ImageWrapper CreateTwoAreaBlobImage()
    {
        var image = new Mat(100, 120, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(image, new Rect(10, 10, 10, 10), Scalar.White, -1);
        Cv2.Rectangle(image, new Rect(60, 10, 30, 20), Scalar.White, -1);
        return new ImageWrapper(image);
    }
}
