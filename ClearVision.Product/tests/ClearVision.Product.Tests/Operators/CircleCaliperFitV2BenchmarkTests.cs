using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
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
            foreach (var request in CreateBenchmarkRequests(width, height))
            {
                using var gray = CreateFilledCircleImage(width, height);

                var warmup = CircleCaliperFitV2Kernel.Fit(gray, request.Request);
                warmup.Success.Should().BeTrue(FormatFailure(request.Name, width, height, warmup));

                var elapsed = new List<double>();
                var allocations = new List<long>();
                for (var i = 0; i < 7; i++)
                {
                    var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
                    var stopwatch = Stopwatch.StartNew();
                    var result = CircleCaliperFitV2Kernel.Fit(gray, request.Request);
                    stopwatch.Stop();
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;

                    result.Success.Should().BeTrue(FormatFailure(request.Name, width, height, result));
                    result.ProfileEvidence.Count.Should().BeLessThanOrEqualTo(CircleCaliperFitV2Request.MaxProfileEvidenceCount);
                    elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
                    allocations.Add(allocated);
                }

                rows.Add(new BenchmarkRow(
                    request.Name,
                    width,
                    height,
                    request.Request.CaliperCount,
                    request.Request.ProfileSampleCount,
                    WorkUnits(request.Request),
                    elapsed.Average(),
                    Percentile(elapsed, 0.50),
                    Percentile(elapsed, 0.95),
                    allocations.Average(),
                    ComputeVariance(elapsed),
                    allocations.Max()));
            }
        }

        rows.Where(row => row.Profile == "typical")
            .Select(row => row.MaxAllocatedBytes)
            .Max()
            .Should().BeLessThan(8_000_000);
        rows.Select(row => row.MaxAllocatedBytes).Max().Should().BeLessThan(64_000_000);

        using var nearBudgetGray = CreateFilledCircleImage(640, 640);
        var nearBudgetRequest = new CircleCaliperFitV2Request
        {
            SearchCenterX = 319.5,
            SearchCenterY = 319.5,
            MinRadius = 120,
            MaxRadius = 170,
            NominalRadius = 144,
            CaliperCount = 640,
            AveragingThickness = 3,
            ProfileSampleCount = 4096,
            GaussianSigma = 1.2,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            EdgeThreshold = 0.05,
            MinEdgeStrength = 0.05,
            MinValidCalipers = 120,
            MinCoverageRatio = 0.35,
            MinAngularCoverageDegrees = 180,
            MaxResidualRmse = 2.0
        };
        var nearBudgetUnits = WorkUnits(nearBudgetRequest);
        nearBudgetUnits.Should().BeLessThan(CircleCaliperFitV2Request.MaxSamplingWorkUnits);
        var nearBudgetStopwatch = Stopwatch.StartNew();
        var nearBudgetResult = CircleCaliperFitV2Kernel.Fit(nearBudgetGray, nearBudgetRequest);
        nearBudgetStopwatch.Stop();
        nearBudgetResult.Success.Should().BeTrue(nearBudgetResult.FailureMessage);

        var overBudgetRequest = nearBudgetRequest with
        {
            CaliperCount = 720
        };
        WorkUnits(overBudgetRequest).Should().BeGreaterThan(CircleCaliperFitV2Request.MaxSamplingWorkUnits);
        var overBudgetStopwatch = Stopwatch.StartNew();
        var overBudgetResult = CircleCaliperFitV2Kernel.Fit(nearBudgetGray, overBudgetRequest);
        overBudgetStopwatch.Stop();
        overBudgetResult.Success.Should().BeFalse();
        overBudgetResult.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InvalidInput);
        overBudgetResult.EdgePoints.Should().BeEmpty();
        overBudgetResult.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "sampling.work-budget");

        Console.WriteLine($"Environment: {RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; serverGC={GCSettings.IsServerGC}");
        Console.WriteLine("| Profile | Size | Calipers | Samples | Work units | p50 ms | p95 ms | Avg ms | Avg allocated bytes | Elapsed variance | Max allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in rows)
        {
            Console.WriteLine($"| {row.Profile} | {row.Width}x{row.Height} | {row.CaliperCount} | {row.ProfileSampleCount} | {row.WorkUnits} | {row.P50ElapsedMs:F3} | {row.P95ElapsedMs:F3} | {row.AverageElapsedMs:F3} | {row.AverageAllocatedBytes:F0} | {row.ElapsedVariance:F6} | {row.MaxAllocatedBytes} |");
        }

        Console.WriteLine();
        Console.WriteLine("| Budget case | Work units | Elapsed ms | Result |");
        Console.WriteLine("|---|---:|---:|---|");
        Console.WriteLine($"| near-budget legal | {nearBudgetUnits} | {nearBudgetStopwatch.Elapsed.TotalMilliseconds:F3} | PASS |");
        Console.WriteLine($"| over-budget rejected | {WorkUnits(overBudgetRequest)} | {overBudgetStopwatch.Elapsed.TotalMilliseconds:F3} | {overBudgetResult.FailureCode} |");
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

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var ordered = values.OrderBy(static value => value).ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static long WorkUnits(CircleCaliperFitV2Request request)
    {
        var polarityMultiplier = request.EdgePolarity == CircleCaliperFitV2EdgePolarity.Auto ? 2L : 1L;
        return polarityMultiplier *
            request.CaliperCount *
            request.ProfileSampleCount *
            (long)Math.Ceiling(Math.Max(1.0, request.AveragingThickness));
    }

    private static IEnumerable<BenchmarkRequest> CreateBenchmarkRequests(int width, int height)
    {
        var minDimension = Math.Min(width, height);
        var common = new CircleCaliperFitV2Request
        {
            SearchCenterX = (width - 1) * 0.5,
            SearchCenterY = (height - 1) * 0.5,
            MinRadius = minDimension * 0.18,
            MaxRadius = minDimension * 0.27,
            NominalRadius = minDimension * 0.225,
            GaussianSigma = 1.2,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinCoverageRatio = 0.50,
            MinAngularCoverageDegrees = 240,
            MaxResidualRmse = 1.6,
            IncludeProfileEvidence = true
        };

        yield return new BenchmarkRequest(
            "typical",
            common with
            {
                CaliperCount = 128,
                AveragingThickness = 5,
                ProfileSampleCount = 129,
                MinValidCalipers = 48
            });

        yield return new BenchmarkRequest(
            "upper-bounded",
            common with
            {
                CaliperCount = 256,
                AveragingThickness = 5,
                ProfileSampleCount = 1025,
                MinValidCalipers = 72,
                MinCoverageRatio = 0.30,
                MinAngularCoverageDegrees = 180
            });
    }

    private static string FormatFailure(string profile, int width, int height, CircleCaliperFitV2Result result)
    {
        return $"{profile} {width}x{height}: {result.FailureCode} {result.FailureMessage} " +
            $"edges={result.CollectedPointCount} inliers={result.ValidCaliperCount} " +
            $"coverage={result.CoverageRatio:F3} angular={result.AngularCoverageDegrees:F1}";
    }

    private sealed record BenchmarkRow(
        string Profile,
        int Width,
        int Height,
        int CaliperCount,
        int ProfileSampleCount,
        long WorkUnits,
        double AverageElapsedMs,
        double P50ElapsedMs,
        double P95ElapsedMs,
        double AverageAllocatedBytes,
        double ElapsedVariance,
        long MaxAllocatedBytes);

    private sealed record BenchmarkRequest(string Name, CircleCaliperFitV2Request Request);
}
