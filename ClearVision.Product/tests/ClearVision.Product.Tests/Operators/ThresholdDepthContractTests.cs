using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public sealed class ThresholdDepthContractTests
{
    [Theory]
    [InlineData("CV_8U")]
    [InlineData("CV_16U")]
    [InlineData("CV_16S")]
    [InlineData("CV_32F")]
    [InlineData("CV_64F")]
    public async Task FixedThreshold_C1_ShouldPreserveInputDepth(string depthName)
    {
        using var source = CreateTwoTone(depthName, 1);
        var threshold = depthName switch
        {
            "CV_16U" => 1000.0,
            "CV_16S" => 0.0,
            "CV_32F" or "CV_64F" => 0.5,
            _ => 100.0
        };
        var maxValue = depthName switch
        {
            "CV_16U" => 60000.0,
            "CV_16S" => 30000.0,
            "CV_32F" or "CV_64F" => 1.0,
            _ => 255.0
        };
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(("Threshold", threshold), ("MaxValue", maxValue), ("Type", 0)),
            CreateImageInputs(source));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var output = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        output.MatReadOnly.Depth().Should().Be(source.Depth());
        output.MatReadOnly.Channels().Should().Be(1);
        Convert.ToDouble(result.OutputData["ActualThreshold"]).Should().Be(threshold);
    }

    [Theory]
    [InlineData("CV_8U", 3)]
    [InlineData("CV_8U", 4)]
    [InlineData("CV_16U", 3)]
    [InlineData("CV_16U", 4)]
    [InlineData("CV_32F", 3)]
    [InlineData("CV_32F", 4)]
    public async Task FixedThreshold_Color_ShouldDeclareGrayConversionAndPreserveDepth(
        string depthName,
        int channels)
    {
        using var source = CreateTwoTone(depthName, channels);
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);
        var threshold = depthName == "CV_16U" ? 1000.0 : depthName == "CV_32F" ? 0.5 : 100.0;
        var maxValue = depthName == "CV_16U" ? 60000.0 : depthName == "CV_32F" ? 1.0 : 255.0;

        var result = await executor.ExecuteAsync(
            CreateOperator(("Threshold", threshold), ("MaxValue", maxValue), ("Type", 0)),
            CreateImageInputs(source));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var output = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        output.MatReadOnly.Depth().Should().Be(source.Depth());
        output.MatReadOnly.Channels().Should().Be(1);
        result.OutputData["ColorConversion"].Should().Be(channels == 4 ? "BGRA_TO_GRAY" : "BGR_TO_GRAY");
    }

    [Fact]
    public async Task FixedThreshold_64FColorFailure_ShouldNotAdvertiseNonexistentCombination()
    {
        using var source = CreateTwoTone("CV_64F", 3);
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(("Threshold", 0.5), ("MaxValue", 1.0), ("Type", 0)),
            CreateImageInputs(source));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("IMAGE_MODE_DEPTH_UNSUPPORTED");
        var supported = result.ErrorMessage!.Split("Supported=", 2)[1].Split(';', 2)[0];
        supported.Should().Contain("CV_64FC1");
        supported.Should().NotContain("CV_64FC3");
        result.ErrorMessage.Should().NotContainEquivalentOf("OpenCV");
    }

    [Theory]
    [InlineData("Otsu", "CV_8U", true)]
    [InlineData("Otsu", "CV_16U", true)]
    [InlineData("Otsu", "CV_32F", false)]
    [InlineData("Triangle", "CV_8U", true)]
    [InlineData("Triangle", "CV_16U", false)]
    public async Task AutomaticThreshold_ShouldMatchInstalledRuntimeMatrix(
        string mode,
        string depthName,
        bool expectedSuccess)
    {
        using var source = CreateTwoTone(depthName, 1);
        var type = mode == "Otsu" ? (int)ThresholdTypes.Otsu : (int)ThresholdTypes.Triangle;
        var maxValue = depthName == "CV_16U" ? 60000.0 : 255.0;
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(("Type", type), ("MaxValue", maxValue)),
            CreateImageInputs(source));

        result.IsSuccess.Should().Be(expectedSuccess, result.ErrorMessage);
        if (expectedSuccess)
        {
            using var output = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
            output.MatReadOnly.Depth().Should().Be(source.Depth());
            result.OutputData.Should().ContainKey("ActualThreshold");
        }
        else
        {
            result.ErrorMessage.Should().StartWith("IMAGE_MODE_DEPTH_UNSUPPORTED");
            result.ErrorMessage.Should().NotContainEquivalentOf("OpenCV");
        }
    }

    [Fact]
    public async Task UseOtsu_ShouldRemainEquivalentToExplicitOtsuFlag()
    {
        using var source = CreateTwoTone("CV_8U", 1);
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);
        var aliasResult = await executor.ExecuteAsync(
            CreateOperator(("Type", 0), ("UseOtsu", true)),
            CreateImageInputs(source));
        var explicitResult = await executor.ExecuteAsync(
            CreateOperator(("Type", (int)ThresholdTypes.Otsu), ("UseOtsu", false)),
            CreateImageInputs(source));

        aliasResult.IsSuccess.Should().BeTrue(aliasResult.ErrorMessage);
        explicitResult.IsSuccess.Should().BeTrue(explicitResult.ErrorMessage);
        using var aliasImage = aliasResult.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        using var explicitImage = explicitResult.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        Cv2.Norm(aliasImage.MatReadOnly, explicitImage.MatReadOnly, NormTypes.L1).Should().Be(0.0);
        aliasResult.OutputData["ActualThreshold"].Should().Be(explicitResult.OutputData["ActualThreshold"]);
    }

    [Fact]
    public void AutomaticThreshold_WithNonBinaryBase_ShouldFailParameterValidation()
    {
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);
        var op = CreateOperator(("Type", (int)(ThresholdTypes.Trunc | ThresholdTypes.Otsu)));

        var validation = executor.ValidateParameters(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error => error.Contains("Binary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FixedThreshold_8UWithNegativeValue_ShouldFailDepthAwareRuntimeValidation()
    {
        using var source = CreateTwoTone("CV_8U", 1);
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(("Threshold", -1.0), ("MaxValue", 255.0), ("Type", 0)),
            CreateImageInputs(source));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("IMAGE_DYNAMIC_RANGE_UNDEFINED");
    }

    private static Dictionary<string, object> CreateImageInputs(Mat source) =>
        new() { ["Image"] = new ImageWrapper(source.Clone()) };

    private static Operator CreateOperator(params (string Name, object Value)[] parameters)
    {
        var op = new Operator("ThresholdDepth", OperatorType.Thresholding, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "object", value, isRequired: false));
        }
        return op;
    }

    private static Mat CreateTwoTone(string depthName, int channels)
    {
        var depth = depthName switch
        {
            "CV_8U" => MatType.CV_8U,
            "CV_16U" => MatType.CV_16U,
            "CV_16S" => MatType.CV_16S,
            "CV_32F" => MatType.CV_32F,
            "CV_64F" => MatType.CV_64F,
            _ => throw new ArgumentOutOfRangeException(nameof(depthName))
        };
        var mat = new Mat(4, 4, MatType.MakeType(depth, channels));
        var low = depthName switch
        {
            "CV_16U" => 500.0,
            "CV_16S" => -100.0,
            "CV_32F" or "CV_64F" => 0.25,
            _ => 50.0
        };
        var high = depthName switch
        {
            "CV_16U" => 4000.0,
            "CV_16S" => 100.0,
            "CV_32F" or "CV_64F" => 0.75,
            _ => 200.0
        };
        using var lowMat = new Mat(mat.Rows, mat.Cols / 2, MatType.MakeType(depth, channels), Scalar.All(low));
        using var highMat = new Mat(mat.Rows, mat.Cols - mat.Cols / 2, MatType.MakeType(depth, channels), Scalar.All(high));
        using var left = new Mat(mat, new Rect(0, 0, lowMat.Cols, mat.Rows));
        using var right = new Mat(mat, new Rect(lowMat.Cols, 0, highMat.Cols, mat.Rows));
        lowMat.CopyTo(left);
        highMat.CopyTo(right);
        return mat;
    }
}
