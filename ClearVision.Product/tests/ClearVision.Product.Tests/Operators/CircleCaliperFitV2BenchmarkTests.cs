using System.Diagnostics;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class CircleCaliperFitV2BenchmarkTests
{
    [Fact]
    public void Benchmark_ShouldRecordElapsedAllocationAndVariance()
    {
        var rows = new List<BenchmarkRow>();
        foreach (var (width, height) in new[] { (320, 240), (640, 480), (1920, 1080) })
        {
            using var gray = CreateFilledCircleImage(width, height);
            var request = new CircleCaliperFitV2Request
            {
                SearchCenterX = (width - 1) * 0.5,
                SearchCenterY = (height - 1) * 0.5,
                MinRadius = Math.Min(width, height) * 0.18,
                MaxRadius = Math.Min(width, height) * 0.27,
                NominalRadius = Math.Min(width, height) * 0.225,
                CaliperCount = 128,
                AveragingThickness = 5,
                ProfileSampleCount = 129,
                GaussianSigma = 1.2,
                EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
                MinValidCalipers = 48,
                MinCoverageRatio = 0.50,
                MinAngularCoverageDegrees = 240,
                MaxResidualRmse = 1.6
            };

            CircleCaliperFitV2Kernel.Fit(gray, request).Success.Should().BeTrue();

            var elapsed = new List<double>();
            var allocations = new List<long>();
            for (var i = 0; i < 5; i++)
            {
                var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                var result = CircleCaliperFitV2Kernel.Fit(gray, request);
                stopwatch.Stop();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;

                result.Success.Should().BeTrue(result.FailureMessage);
                elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
                allocations.Add(allocated);
            }

            rows.Add(new BenchmarkRow(
                width,
                height,
                request.CaliperCount,
                elapsed.Average(),
                allocations.Average(),
                ComputeVariance(elapsed),
                allocations.Max()));
        }

        rows.Select(row => row.MaxAllocatedBytes).Max().Should().BeLessThan(8_000_000);
        rows.Max(row => row.AverageAllocatedBytes).Should().BeLessThan(rows.Min(row => row.AverageAllocatedBytes) * 4.0);

        Console.WriteLine("| Size | Calipers | Avg ms | Avg allocated bytes | Elapsed variance | Max allocated bytes |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (var row in rows)
        {
            Console.WriteLine($"| {row.Width}x{row.Height} | {row.CaliperCount} | {row.AverageElapsedMs:F3} | {row.AverageAllocatedBytes:F0} | {row.ElapsedVariance:F6} | {row.MaxAllocatedBytes} |");
        }
    }

    private static Mat CreateFilledCircleImage(int width, int height)
    {
        var radius = Math.Min(width, height) * 0.225;
        var center = new Point((int)Math.Round((width - 1) * 0.5), (int)Math.Round((height - 1) * 0.5));
        var gray = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(gray, center, (int)Math.Round(radius), Scalar.White, -1, LineTypes.AntiAlias);
        return gray;
    }

    private static double ComputeVariance(IReadOnlyList<double> values)
    {
        var average = values.Average();
        return values.Sum(value => (value - average) * (value - average)) / values.Count;
    }

    private sealed record BenchmarkRow(
        int Width,
        int Height,
        int CaliperCount,
        double AverageElapsedMs,
        double AverageAllocatedBytes,
        double ElapsedVariance,
        long MaxAllocatedBytes);
}
