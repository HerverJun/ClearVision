using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public sealed class SpatialFilterRuntimeParityTests
{
    [Theory]
    [InlineData("Mean", OperatorType.MeanFilter, "CV_16S", 4)]
    [InlineData("Median", OperatorType.MedianBlur, "CV_16U", 5)]
    [InlineData("Bilateral", OperatorType.BilateralFilter, "CV_32F", 5)]
    public async Task UnifiedAndDedicatedWrappers_ShouldPreserveTypeAndPixels(
        string mode,
        OperatorType dedicatedType,
        string depthName,
        int kernelOrDiameter)
    {
        using var source = CreateMat(depthName, mode == "Bilateral" ? 3 : 1);
        var unified = new GaussianBlurOperator(NullLogger<GaussianBlurOperator>.Instance);
        var unifiedOperator = CreateOperator(
            OperatorType.Filtering,
            ("FilterMode", mode),
            ("KernelSize", kernelOrDiameter),
            ("Diameter", kernelOrDiameter));
        var dedicated = CreateDedicated(dedicatedType);
        var dedicatedOperator = CreateOperator(
            dedicatedType,
            ("KernelSize", kernelOrDiameter),
            ("Diameter", kernelOrDiameter));

        var unifiedResult = await unified.ExecuteAsync(unifiedOperator, CreateImageInputs(source));
        var dedicatedResult = await dedicated.ExecuteAsync(dedicatedOperator, CreateImageInputs(source));

        unifiedResult.IsSuccess.Should().BeTrue(unifiedResult.ErrorMessage);
        dedicatedResult.IsSuccess.Should().BeTrue(dedicatedResult.ErrorMessage);
        using var unifiedImage = unifiedResult.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        using var dedicatedImage = dedicatedResult.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        unifiedImage.MatReadOnly.Type().Should().Be(source.Type());
        dedicatedImage.MatReadOnly.Type().Should().Be(source.Type());
        Cv2.Norm(unifiedImage.MatReadOnly, dedicatedImage.MatReadOnly, NormTypes.L1).Should().Be(0.0);
    }

    [Fact]
    public async Task UnsupportedCombination_ShouldReturnSameStableFailureAcrossWrappers()
    {
        using var source = CreateMat("CV_16U", 1);
        var unified = new GaussianBlurOperator(NullLogger<GaussianBlurOperator>.Instance);
        var dedicated = new BilateralFilterOperator(NullLogger<BilateralFilterOperator>.Instance);
        var unifiedResult = await unified.ExecuteAsync(
            CreateOperator(OperatorType.Filtering, ("FilterMode", "Bilateral")),
            CreateImageInputs(source));
        var dedicatedResult = await dedicated.ExecuteAsync(
            CreateOperator(OperatorType.BilateralFilter),
            CreateImageInputs(source));

        unifiedResult.IsSuccess.Should().BeFalse();
        dedicatedResult.IsSuccess.Should().BeFalse();
        unifiedResult.ErrorMessage.Should().StartWith("IMAGE_DEPTH_UNSUPPORTED");
        dedicatedResult.ErrorMessage.Should().StartWith("IMAGE_DEPTH_UNSUPPORTED");
        unifiedResult.ErrorMessage.Should().NotContainEquivalentOf("OpenCV");
        dedicatedResult.ErrorMessage.Should().NotContainEquivalentOf("OpenCV");
    }

    [Fact]
    public async Task Median16S_ShouldRejectBeforeOpenCvAndAdvertiseOnlyRealKernelFiveCombinations()
    {
        using var source = CreateMat("CV_16S", 1);
        var unified = new GaussianBlurOperator(NullLogger<GaussianBlurOperator>.Instance);

        var result = await unified.ExecuteAsync(
            CreateOperator(
                OperatorType.Filtering,
                ("FilterMode", "Median"),
                ("KernelSize", 5)),
            CreateImageInputs(source));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("IMAGE_MODE_DEPTH_UNSUPPORTED");
        result.ErrorMessage.Should().Contain("Mode=Median:Kernel3Or5");
        var supported = result.ErrorMessage!.Split("Supported=", 2)[1].Split(';', 2)[0];
        supported.Should().NotContain("CV_16SC1");
        result.ErrorMessage.Should().NotContainEquivalentOf("OpenCV");
        result.ErrorMessage.Should().NotContainEquivalentOf("Assertion");
    }

    private static OperatorBase CreateDedicated(OperatorType type) => type switch
    {
        OperatorType.MeanFilter => new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance),
        OperatorType.MedianBlur => new MedianBlurOperator(NullLogger<MedianBlurOperator>.Instance),
        OperatorType.BilateralFilter => new BilateralFilterOperator(NullLogger<BilateralFilterOperator>.Instance),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static Dictionary<string, object> CreateImageInputs(Mat source) =>
        new() { ["Image"] = new ImageWrapper(source.Clone()) };

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(type.ToString(), type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "object", value, isRequired: false));
        }
        return op;
    }

    private static Mat CreateMat(string depthName, int channels)
    {
        var depth = depthName switch
        {
            "CV_16U" => MatType.CV_16U,
            "CV_16S" => MatType.CV_16S,
            "CV_32F" => MatType.CV_32F,
            _ => MatType.CV_8U
        };
        var mat = new Mat(9, 9, MatType.MakeType(depth, channels));
        mat.SetTo(new Scalar(10, 20, 30, 40));
        return mat;
    }
}
