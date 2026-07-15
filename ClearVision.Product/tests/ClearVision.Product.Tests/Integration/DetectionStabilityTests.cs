using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Detection, TestPurpose.Stability, TestLane.Nightly, TestEvidenceType.StatisticalDistribution, TestOracleType.Statistical, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "operator-quality", SeedControl = "Fixed: seeds 20260715 through 20260722 generate equal-size impulse perturbations")]
public sealed class DetectionStabilityTests
{
    [Fact]
    public async Task ColorDetection_AcrossFixedSeedImpulseNoise_ShouldKeepCoverageDistributionStable()
    {
        const int width = 60;
        const int height = 60;
        const int noisePixels = 20;
        var executor = new ColorDetectionOperator(NullLogger<ColorDetectionOperator>.Instance);
        var op = new Operator("color-stability", OperatorType.ColorDetection, 0, 0);
        foreach (var (name, value) in new Dictionary<string, object>
                 {
                     ["AnalysisMode"] = "HsvInspection",
                     ["HueLow"] = 170,
                     ["HueHigh"] = 10,
                     ["SatLow"] = 150,
                     ["SatHigh"] = 255,
                     ["ValLow"] = 150,
                     ["ValHigh"] = 255
                 })
        {
            op.AddParameter(TestHelpers.CreateParameter(name, value, "string"));
        }

        var coverages = new List<double>();
        for (var seed = 20260715; seed < 20260723; seed++)
        {
            using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(0, 0, 255));
            AddBlackImpulseNoise(mat, noisePixels, seed);
            using var image = new ImageWrapper(mat.Clone());
            var result = await executor.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            coverages.Add(Convert.ToDouble(result.OutputData!["Coverage"]));
            (result.OutputData["Image"] as ImageWrapper)?.Dispose();
        }

        var expectedCoverage = (width * height - noisePixels) / (double)(width * height);
        coverages.Should().AllSatisfy(coverage => coverage.Should().BeApproximately(expectedCoverage, 1e-9));
        (coverages.Max() - coverages.Min()).Should().BeLessThan(1e-12);
    }

    private static void AddBlackImpulseNoise(Mat image, int count, int seed)
    {
        var random = new Random(seed);
        var coordinates = new HashSet<(int X, int Y)>();
        while (coordinates.Count < count)
        {
            coordinates.Add((random.Next(image.Width), random.Next(image.Height)));
        }

        foreach (var (x, y) in coordinates)
        {
            image.Set(y, x, new Vec3b(0, 0, 0));
        }
    }
}
