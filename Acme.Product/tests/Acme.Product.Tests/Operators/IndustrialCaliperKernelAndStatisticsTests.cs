using System.Reflection;
using Acme.Product.Infrastructure.Operators;
using FluentAssertions;
using Xunit;

namespace Acme.Product.Tests.Operators;

public class IndustrialCaliperKernelAndStatisticsTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(CaliperToolOperator).Assembly;
    private static readonly Type KernelType = InfrastructureAssembly.GetType("Acme.Product.Infrastructure.Operators.IndustrialCaliperKernel", throwOnError: true)!;
    private static readonly Type StatisticsHelperType = InfrastructureAssembly.GetType("Acme.Product.Infrastructure.Operators.MeasurementStatisticsHelper", throwOnError: true)!;

    [Fact]
    public void GaussianSmooth_UsesReflectionAtProfileBoundary()
    {
        double[] profile = [0.0, 100.0, 100.0, 100.0, 100.0];

        var actual = InvokeGaussianSmooth(profile, sigma: 1.0);
        var expected = ComputeExpectedGaussianSmooth(profile, sigma: 1.0);
        var legacyClamp = ComputeExpectedGaussianSmooth(profile, sigma: 1.0, useClampBoundary: true);

        actual[0].Should().BeApproximately(expected[0], 1e-9);
        Math.Abs(actual[0] - legacyClamp[0]).Should().BeGreaterThan(10.0);
    }

    [Fact]
    public void GaussianSmooth_UsesFullThreeSigmaRadius_WhenSigmaIsLarge()
    {
        double[] profile =
        [
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
            100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0,
            100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0
        ];

        var actual = InvokeGaussianSmooth(profile, sigma: 4.0);
        var expected = ComputeExpectedGaussianSmooth(profile, sigma: 4.0);
        var legacyRadiusCap = ComputeExpectedGaussianSmooth(profile, sigma: 4.0, radiusOverride: 8);

        actual[8].Should().BeApproximately(expected[8], 1e-9);
        Math.Abs(actual[8] - legacyRadiusCap[8]).Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void MedianAbsoluteDeviation_PreservesRawSemantics_AndProvidesScaledVariant()
    {
        double[] values = [1.0, 2.0, 2.0, 4.0, 100.0];

        var median = InvokeStatisticsHelper<double>("ComputeMedian", values);
        var rawMad = InvokeStatisticsHelper<double>("ComputeMedianAbsoluteDeviation", values, median);
        var scaledMad = InvokeStatisticsHelper<double>("ComputeScaledMedianAbsoluteDeviation", values, median);

        median.Should().Be(2.0);
        rawMad.Should().Be(1.0);
        scaledMad.Should().BeApproximately(1.4826, 1e-12);
    }

    [Fact]
    public void ComputeConfidenceFromUncertainty_UsesEmpiricalMapping()
    {
        InvokeStatisticsHelper<double>("ComputeConfidenceFromUncertainty", 0.0).Should().Be(1.0);
        InvokeStatisticsHelper<double>("ComputeConfidenceFromUncertainty", 1.0).Should().Be(0.5);
        InvokeStatisticsHelper<double>("ComputeConfidenceFromUncertainty", -5.0).Should().Be(1.0);
        InvokeStatisticsHelper<double>("ComputeConfidenceFromUncertainty", double.PositiveInfinity).Should().Be(0.0);
    }

    private static double[] InvokeGaussianSmooth(IReadOnlyList<double> profile, double sigma)
    {
        var method = KernelType.GetMethod("GaussianSmooth", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (double[])method!.Invoke(null, new object?[] { profile, sigma })!;
    }

    private static T InvokeStatisticsHelper<T>(string methodName, params object?[] arguments)
    {
        var method = StatisticsHelperType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (T)method!.Invoke(null, arguments)!;
    }

    private static double[] ComputeExpectedGaussianSmooth(
        IReadOnlyList<double> profile,
        double sigma,
        int? radiusOverride = null,
        bool useClampBoundary = false)
    {
        if (profile.Count == 0)
        {
            return [];
        }

        if (!double.IsFinite(sigma) || sigma <= 1e-6 || profile.Count == 1)
        {
            return profile.ToArray();
        }

        var radius = radiusOverride ?? Math.Max(1, (int)Math.Ceiling(sigma * 3.0));
        var kernel = BuildGaussianKernel(radius, sigma);
        var smoothed = new double[profile.Count];

        for (var i = 0; i < profile.Count; i++)
        {
            double sum = 0.0;
            for (var k = -radius; k <= radius; k++)
            {
                var index = useClampBoundary
                    ? Math.Clamp(i + k, 0, profile.Count - 1)
                    : Reflect101Index(i + k, profile.Count);
                sum += profile[index] * kernel[k + radius];
            }

            smoothed[i] = sum;
        }

        return smoothed;
    }

    private static double[] BuildGaussianKernel(int radius, double sigma)
    {
        var kernel = new double[(radius * 2) + 1];
        double sum = 0.0;

        for (var i = -radius; i <= radius; i++)
        {
            var value = Math.Exp(-(i * i) / (2.0 * sigma * sigma));
            kernel[i + radius] = value;
            sum += value;
        }

        for (var i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }

    private static int Reflect101Index(int index, int length)
    {
        if (length <= 1)
        {
            return 0;
        }

        var period = (length * 2) - 2;
        var reflected = index % period;
        if (reflected < 0)
        {
            reflected += period;
        }

        return reflected < length ? reflected : period - reflected;
    }
}
