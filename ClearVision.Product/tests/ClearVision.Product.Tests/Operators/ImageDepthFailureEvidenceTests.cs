using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public sealed class ImageDepthFailureEvidenceTests
{
    [Fact]
    public async Task Threshold_16U_ShouldUseNativeThresholdAndMaxValueUnits()
    {
        using var source = new Mat(1, 2, MatType.CV_16UC1);
        source.Set(0, 0, (ushort)500);
        source.Set(0, 1, (ushort)4000);
        var op = CreateOperator(
            OperatorType.Thresholding,
            ("Threshold", 1000.0),
            ("MaxValue", 60000.0),
            ("Type", (int)ThresholdTypes.Binary));
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var output = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        output.MatReadOnly.Type().Should().Be(MatType.CV_16UC1);
        output.MatReadOnly.At<ushort>(0, 0).Should().Be(0);
        output.MatReadOnly.At<ushort>(0, 1).Should().Be(60000);
        Convert.ToDouble(result.OutputData["ActualThreshold"]).Should().Be(1000.0);
    }

    [Fact]
    public async Task Threshold_32FOtsu_ShouldFailBeforeOpenCvWithStableCode()
    {
        using var source = new Mat(2, 2, MatType.CV_32FC1, Scalar.All(0.5));
        var op = CreateOperator(
            OperatorType.Thresholding,
            ("Type", (int)ThresholdTypes.Otsu));
        var executor = new ThresholdOperator(NullLogger<ThresholdOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        AssertStableFailure(result, "IMAGE_MODE_DEPTH_UNSUPPORTED");
    }

    [Fact]
    public async Task Bilateral_16U_ShouldFailBeforeOpenCvWithStableCode()
    {
        using var source = new Mat(5, 5, MatType.CV_16UC1, Scalar.All(1000));
        var executor = new BilateralFilterOperator(NullLogger<BilateralFilterOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(OperatorType.BilateralFilter),
            CreateImageInputs(source));

        AssertStableFailure(result, "IMAGE_DEPTH_UNSUPPORTED");
    }

    [Fact]
    public async Task Median_32FKernelSeven_ShouldFailBeforeOpenCvWithStableCode()
    {
        using var source = new Mat(9, 9, MatType.CV_32FC1, Scalar.All(0.5));
        var executor = new MedianBlurOperator(NullLogger<MedianBlurOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(OperatorType.MedianBlur, ("KernelSize", 7)),
            CreateImageInputs(source));

        AssertStableFailure(result, "IMAGE_MODE_DEPTH_UNSUPPORTED");
    }

    [Fact]
    public async Task Histogram_16U_ShouldRejectInsteadOfReportingEightBitBins()
    {
        using var source = new Mat(1, 2, MatType.CV_16UC1);
        source.Set(0, 0, (ushort)256);
        source.Set(0, 1, (ushort)4095);
        var executor = new HistogramAnalysisOperator(NullLogger<HistogramAnalysisOperator>.Instance);

        var result = await executor.ExecuteAsync(
            CreateOperator(OperatorType.HistogramAnalysis),
            CreateImageInputs(source));

        AssertStableFailure(result, "IMAGE_DEPTH_UNSUPPORTED");
    }

    [Fact]
    public async Task Sharpness_Brenner16U_ShouldReadNativeValuesAndDisableDefaultDecision()
    {
        using var source = new Mat(1, 4, MatType.CV_16UC1);
        source.Set(0, 0, (ushort)0);
        source.Set(0, 1, (ushort)0);
        source.Set(0, 2, (ushort)256);
        source.Set(0, 3, (ushort)256);
        var executor = new SharpnessEvaluationOperator(NullLogger<SharpnessEvaluationOperator>.Instance);
        var op = CreateOperator(
            OperatorType.SharpnessEvaluation,
            ("Method", "Brenner"),
            ("ThresholdMode", "PerMethodDefault"),
            ("OutputImagePolicy", "None"));

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Score"]).Should().BeApproximately(32768.0, 1e-9);
        Convert.ToBoolean(result.OutputData["DecisionReady"]).Should().BeFalse();
        result.OutputData.Should().NotContainKey("IsSharp");
    }

    [Fact]
    public async Task Sharpness_Smd32F_ShouldReadNativeValuesAndDisableDefaultDecision()
    {
        using var source = new Mat(2, 2, MatType.CV_32FC1);
        source.Set(0, 0, 0f);
        source.Set(0, 1, 1f);
        source.Set(1, 0, 1f);
        source.Set(1, 1, 1f);
        var executor = new SharpnessEvaluationOperator(NullLogger<SharpnessEvaluationOperator>.Instance);
        var op = CreateOperator(
            OperatorType.SharpnessEvaluation,
            ("Method", "SMD"),
            ("ThresholdMode", "PerMethodDefault"),
            ("OutputImagePolicy", "None"));

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Score"]).Should().BeApproximately(0.5, 1e-9);
        Convert.ToBoolean(result.OutputData["DecisionReady"]).Should().BeFalse();
        result.OutputData.Should().NotContainKey("IsSharp");
    }

    [Fact]
    public async Task Sharpness_NonFinite32F_ShouldFailWithStableCode()
    {
        using var source = new Mat(2, 2, MatType.CV_32FC1, Scalar.All(0.5));
        source.Set(0, 0, float.NaN);
        var executor = new SharpnessEvaluationOperator(NullLogger<SharpnessEvaluationOperator>.Instance);
        var op = CreateOperator(
            OperatorType.SharpnessEvaluation,
            ("Method", "Laplacian"),
            ("OutputImagePolicy", "None"));

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        AssertStableFailure(result, "IMAGE_NONFINITE_INPUT");
    }

    private static void AssertStableFailure(
        ClearVision.Product.Core.Operators.OperatorExecutionOutput result,
        string errorCode)
    {
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(errorCode);
        result.ErrorMessage.Should().NotContainEquivalentOf("OpenCV");
        result.ErrorMessage.Should().NotContainEquivalentOf("Assertion");
    }

    private static Dictionary<string, object> CreateImageInputs(Mat source) =>
        new() { ["Image"] = new ImageWrapper(source.Clone()) };

    private static Operator CreateOperator(
        OperatorType type,
        params (string Name, object? Value)[] parameters)
    {
        var op = new Operator($"{type}-depth-evidence", type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(
                Guid.NewGuid(),
                name,
                name,
                string.Empty,
                "object",
                value,
                isRequired: false));
        }

        return op;
    }
}
