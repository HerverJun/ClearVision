using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Measurement, TestPurpose.Stability, TestLane.Nightly, TestEvidenceType.StatisticalDistribution, TestOracleType.Statistical, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "operator-quality", SeedControl = "Fixed: System.Random seed 20260715 drives bounded coordinate perturbations")]
public sealed class MeasurementStabilityTests
{
    [Fact]
    public async Task MeasureDistance_WithBoundedCoordinateNoise_ShouldKeepErrorDistributionStable()
    {
        const int sampleCount = 64;
        var executor = new MeasureDistanceOperator(NullLogger<MeasureDistanceOperator>.Instance);
        var op = new Operator("measure-stability", OperatorType.Measurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MeasureType", "PointToPoint", "string"));
        var random = new Random(20260715);
        var errors = new List<double>(sampleCount);

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var pointA = new Position(
                10.25 + ((random.NextDouble() - 0.5) * 0.4),
                20.50 + ((random.NextDouble() - 0.5) * 0.4));
            var pointB = new Position(
                42.75 + ((random.NextDouble() - 0.5) * 0.4),
                63.125 + ((random.NextDouble() - 0.5) * 0.4));
            var expected = Math.Sqrt(
                Math.Pow(pointB.X - pointA.X, 2) +
                Math.Pow(pointB.Y - pointA.Y, 2));

            var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
            {
                ["PointA"] = pointA,
                ["PointB"] = pointB
            });

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var actual = Convert.ToDouble(result.OutputData!["Distance"]);
            errors.Add(actual - expected);
        }

        errors.Max(error => Math.Abs(error)).Should().BeLessThan(1e-10);
        ComputeStandardDeviation(errors).Should().BeLessThan(1e-11);
    }

    private static double ComputeStandardDeviation(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / values.Count);
    }
}
