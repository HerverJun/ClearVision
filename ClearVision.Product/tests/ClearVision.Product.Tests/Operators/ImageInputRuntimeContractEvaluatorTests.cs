using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public sealed class ImageInputRuntimeContractEvaluatorTests
{
    [Fact]
    public void PriorityModes_ShouldBeRejectedByTheUnifiedEvaluatorBeforeExecution()
    {
        AssertRejected(
            typeof(ThresholdOperator),
            OperatorType.Thresholding,
            new Mat(3, 3, MatType.CV_32FC1, Scalar.All(0.5)),
            "IMAGE_MODE_DEPTH_UNSUPPORTED",
            "Mode=Otsu",
            ("Type", (int)ThresholdTypes.Otsu));
        AssertRejected(
            typeof(GaussianBlurOperator),
            OperatorType.Filtering,
            new Mat(9, 9, MatType.CV_32FC1, Scalar.All(0.5)),
            "IMAGE_MODE_DEPTH_UNSUPPORTED",
            "Mode=Median:Kernel7Plus",
            ("FilterMode", "Median"),
            ("KernelSize", 7));
        AssertRejected(
            typeof(BilateralFilterOperator),
            OperatorType.BilateralFilter,
            new Mat(9, 9, MatType.CV_16UC1, Scalar.All(1000)),
            "IMAGE_DEPTH_UNSUPPORTED",
            "Mode=Bilateral");
        AssertRejected(
            typeof(HistogramAnalysisOperator),
            OperatorType.HistogramAnalysis,
            new Mat(3, 3, MatType.CV_8UC1, Scalar.All(10)),
            "IMAGE_CHANNELS_UNSUPPORTED",
            "Mode=Channel=R",
            ("Channel", "R"));
        AssertRejected(
            typeof(SharpnessEvaluationOperator),
            OperatorType.SharpnessEvaluation,
            new Mat(9, 9, MatType.CV_32FC1, Scalar.All(0.5)),
            "IMAGE_DYNAMIC_RANGE_UNDEFINED",
            "Mode=Laplacian:Manual:FullOverlay",
            ("Method", "Laplacian"),
            ("ThresholdMode", "Manual"),
            ("OutputImagePolicy", "FullOverlay"));
        AssertRejected(
            typeof(ImageNormalizeOperator),
            OperatorType.ImageNormalize,
            new Mat(3, 3, MatType.CV_64FC3, Scalar.All(0.5)),
            "IMAGE_MODE_DEPTH_UNSUPPORTED",
            "Mode=MinMax:LumaOnly",
            ("Method", "MinMax"),
            ("ColorMode", "LumaOnly"));
    }

    [Fact]
    public void PriorityModes_ShouldAllowDeclaredExactVariants()
    {
        AssertAllowed(
            typeof(ThresholdOperator),
            OperatorType.Thresholding,
            new Mat(3, 3, MatType.CV_64FC1, Scalar.All(0.5)),
            ("Type", 0));
        AssertAllowed(
            typeof(GaussianBlurOperator),
            OperatorType.Filtering,
            new Mat(9, 9, MatType.CV_32FC1, Scalar.All(0.5)),
            ("FilterMode", "Median"),
            ("KernelSize", 5));
        AssertAllowed(
            typeof(HistogramAnalysisOperator),
            OperatorType.HistogramAnalysis,
            new Mat(3, 3, MatType.CV_8UC1, Scalar.All(10)),
            ("Channel", "Gray"));
        AssertAllowed(
            typeof(SharpnessEvaluationOperator),
            OperatorType.SharpnessEvaluation,
            new Mat(9, 9, MatType.CV_32FC1, Scalar.All(0.5)),
            ("Method", "Laplacian"),
            ("ThresholdMode", "Manual"),
            ("OutputImagePolicy", "None"));
        AssertAllowed(
            typeof(ImageNormalizeOperator),
            OperatorType.ImageNormalize,
            new Mat(3, 3, MatType.CV_64FC3, Scalar.All(0.5)),
            ("Method", "ZScore"),
            ("ColorMode", "PerChannel"));
    }

    [Fact]
    public void InvalidHistogramMode_ShouldFailClosedInsteadOfFallingBackToBlue()
    {
        using var source = new Mat(3, 3, MatType.CV_8UC3, Scalar.All(10));
        using var wrapper = new ImageWrapper(source.Clone());
        var op = CreateOperator(OperatorType.HistogramAnalysis, ("Channel", "HSV"));

        var allowed = ImageInputRuntimeContractEvaluator.TryValidate(
            typeof(HistogramAnalysisOperator),
            OperatorType.HistogramAnalysis,
            op,
            new Dictionary<string, object> { ["Image"] = wrapper },
            out var error);

        allowed.Should().BeFalse();
        error.Should().StartWith("IMAGE_MODE_UNRESOLVED");
        error.Should().Contain("Channel must be Gray, B, G or R");
    }

    private static void AssertRejected(
        Type executorType,
        OperatorType operatorType,
        Mat source,
        string errorCode,
        string modeFragment,
        params (string Name, object Value)[] parameters)
    {
        using (source)
        using (var wrapper = new ImageWrapper(source.Clone()))
        {
            var allowed = ImageInputRuntimeContractEvaluator.TryValidate(
                executorType,
                operatorType,
                CreateOperator(operatorType, parameters),
                new Dictionary<string, object> { ["Image"] = wrapper },
                out var error);

            allowed.Should().BeFalse();
            error.Should().StartWith(errorCode);
            error.Should().Contain(modeFragment);
            error.Should().NotContainEquivalentOf("OpenCV");
            error.Should().NotContainEquivalentOf("Assertion");
        }
    }

    private static void AssertAllowed(
        Type executorType,
        OperatorType operatorType,
        Mat source,
        params (string Name, object Value)[] parameters)
    {
        using (source)
        using (var wrapper = new ImageWrapper(source.Clone()))
        {
            ImageInputRuntimeContractEvaluator.TryValidate(
                    executorType,
                    operatorType,
                    CreateOperator(operatorType, parameters),
                    new Dictionary<string, object> { ["Image"] = wrapper },
                    out var error)
                .Should().BeTrue(error);
        }
    }

    private static Operator CreateOperator(
        OperatorType type,
        params (string Name, object Value)[] parameters)
    {
        var op = new Operator($"{type}-contract-evaluator", type, 0, 0);
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
