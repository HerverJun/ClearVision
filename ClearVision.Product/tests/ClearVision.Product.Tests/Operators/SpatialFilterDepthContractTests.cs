using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public sealed class SpatialFilterDepthContractTests
{
    [Theory]
    [InlineData("Gaussian", "CV_64F", 4, 3, true, null)]
    [InlineData("Mean", "CV_16S", 3, 4, true, null)]
    [InlineData("Median", "CV_16U", 4, 5, true, null)]
    [InlineData("Median", "CV_16S", 1, 5, false, "IMAGE_MODE_DEPTH_UNSUPPORTED")]
    [InlineData("Median", "CV_32F", 3, 7, false, "IMAGE_MODE_DEPTH_UNSUPPORTED")]
    [InlineData("Median", "CV_64F", 1, 1, true, null)]
    [InlineData("Median", "CV_64F", 1, 3, false, "IMAGE_MODE_DEPTH_UNSUPPORTED")]
    [InlineData("Bilateral", "CV_8U", 3, 5, true, null)]
    [InlineData("Bilateral", "CV_32F", 1, 5, true, null)]
    [InlineData("Bilateral", "CV_16U", 1, 5, false, "IMAGE_DEPTH_UNSUPPORTED")]
    [InlineData("Bilateral", "CV_32F", 4, 5, false, "IMAGE_CHANNELS_UNSUPPORTED")]
    public void SharedKernel_ShouldMatchDeclaredMatrix(
        string modeName,
        string depthName,
        int channels,
        int kernelOrDiameter,
        bool expected,
        string? errorCode)
    {
        var mode = Enum.Parse<SpatialFilterMode>(modeName);
        using var source = CreateMat(depthName, channels);
        var settings = mode == SpatialFilterMode.Bilateral
            ? new SpatialFilterSettings(mode, Diameter: kernelOrDiameter)
            : new SpatialFilterSettings(mode, KernelSize: kernelOrDiameter);
        var operatorType = mode switch
        {
            SpatialFilterMode.Mean => OperatorType.MeanFilter,
            SpatialFilterMode.Median => OperatorType.MedianBlur,
            SpatialFilterMode.Bilateral => OperatorType.BilateralFilter,
            _ => OperatorType.Filtering
        };

        var actual = SpatialFilterKernel.TryValidateInput(source, settings, operatorType, out var error);

        actual.Should().Be(expected, error);
        if (!expected)
        {
            error.Should().StartWith(errorCode);
            error.Should().NotContainEquivalentOf("OpenCV");
        }
    }

    [Fact]
    public void SharedKernel_ShouldRejectNonFiniteFloatInput()
    {
        using var source = new Mat(3, 3, MatType.CV_32FC1, Scalar.All(1));
        source.Set(1, 1, float.PositiveInfinity);

        SpatialFilterKernel.TryValidateInput(
                source,
                new SpatialFilterSettings(SpatialFilterMode.Gaussian),
                OperatorType.Filtering,
                out var error)
            .Should().BeFalse();
        error.Should().StartWith("IMAGE_NONFINITE_INPUT");
    }

    private static Mat CreateMat(string depthName, int channels)
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
        return new Mat(9, 9, MatType.MakeType(depth, channels), Scalar.All(1));
    }
}
