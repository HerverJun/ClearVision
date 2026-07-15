using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Measurement, TestPurpose.Accuracy, TestLane.Nightly, TestEvidenceType.IndependentOracle, TestOracleType.Mathematical, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "operator-quality")]
public class IndustrialMeasurementAccuracyOracleTests
{
    private readonly CircleMeasurementOperator _circleOperator;
    private readonly LineMeasurementOperator _lineOperator;

    public IndustrialMeasurementAccuracyOracleTests()
    {
        _circleOperator = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        _lineOperator = new LineMeasurementOperator(Substitute.For<ILogger<LineMeasurementOperator>>());
    }

    [Fact]
    public async Task CircleMeasurement_SyntheticScene_ShouldMatchIndependentGeometryOracle()
    {
        var baseline = LoadBaseline().CircleBaseline;

        using var sample = CreateCircleBenchmarkImage(baseline);
        var op = new Operator("circle-industrial", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", baseline.Method, "string"));
        op.AddParameter(TestHelpers.CreateParameter("MinRadius", baseline.MinRadius, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxRadius", baseline.MaxRadius, "int"));

        var result = await _circleOperator.ExecuteAsync(op, TestHelpers.CreateImageInputs(sample));
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        var center = result.OutputData!["Center"].Should().BeOfType<Position>().Subject;
        var radius = Convert.ToDouble(result.OutputData["Radius"]);
        center.X.Should().BeApproximately(baseline.SceneCenterX, baseline.CenterTolerance);
        center.Y.Should().BeApproximately(baseline.SceneCenterY, baseline.CenterTolerance);
        radius.Should().BeApproximately(baseline.SceneRadius, baseline.RadiusTolerance);
    }

    [Fact]
    public async Task LineMeasurement_SyntheticScene_ShouldMatchIndependentGeometryOracle()
    {
        var baseline = LoadBaseline().LineBaseline;

        using var sample = CreateLineBenchmarkImage(baseline);
        var op = new Operator("line-industrial", OperatorType.LineMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", baseline.Method, "string"));
        op.AddParameter(TestHelpers.CreateParameter("Threshold", baseline.Threshold, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MinLength", baseline.MinLength, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MaxGap", baseline.MaxGap, "double"));

        var result = await _lineOperator.ExecuteAsync(op, TestHelpers.CreateImageInputs(sample));
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        var angle = Convert.ToDouble(result.OutputData!["Angle"]);
        var length = Convert.ToDouble(result.OutputData["Length"]);
        var residualMean = Convert.ToDouble(result.OutputData["ResidualMean"]);

        var expectedAngle = Math.Atan2(
            baseline.SceneEndY - baseline.SceneStartY,
            baseline.SceneEndX - baseline.SceneStartX) * 180.0 / Math.PI;
        var expectedLength = Math.Sqrt(
            Math.Pow(baseline.SceneEndX - baseline.SceneStartX, 2) +
            Math.Pow(baseline.SceneEndY - baseline.SceneStartY, 2));
        var lengthTolerance = Math.Max(
            Math.Abs(expectedLength - baseline.ExpectedMinLength),
            Math.Abs(baseline.ExpectedMaxLength - expectedLength));

        angle.Should().BeApproximately(expectedAngle, baseline.AngleTolerance);
        length.Should().BeApproximately(expectedLength, lengthTolerance);
        residualMean.Should().BeLessThan(baseline.MaxResidualMean);
    }

    private static IndustrialMeasurementBaseline LoadBaseline()
    {
        var repoRoot = FindRepoRoot();
        var baselinePath = Path.Combine(repoRoot, "ClearVision.Product", "tests", "TestData", "industrial_measurement_benchmark.json");
        var json = File.ReadAllText(baselinePath);
        return JsonSerializer.Deserialize<IndustrialMeasurementBaseline>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    private static ImageWrapper CreateCircleBenchmarkImage(CircleBaseline baseline)
    {
        return IndustrialMeasurementSceneFactory.CreateFilledCircleImage(
            width: baseline.ImageWidth,
            height: baseline.ImageHeight,
            center: new Point2d(baseline.SceneCenterX, baseline.SceneCenterY),
            radius: baseline.SceneRadius,
            supersample: baseline.Supersample);
    }

    private static ImageWrapper CreateLineBenchmarkImage(LineBaseline baseline)
    {
        return IndustrialMeasurementSceneFactory.CreateLineImage(
            width: baseline.ImageWidth,
            height: baseline.ImageHeight,
            start: new Point2d(baseline.SceneStartX, baseline.SceneStartY),
            end: new Point2d(baseline.SceneEndX, baseline.SceneEndY),
            thicknessPx: baseline.SceneThicknessPx,
            supersample: baseline.Supersample);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "ClearVision.Product")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private sealed class IndustrialMeasurementBaseline
    {
        public CircleBaseline CircleBaseline { get; set; } = new();
        public LineBaseline LineBaseline { get; set; } = new();
    }

    private sealed class CircleBaseline
    {
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public string Method { get; set; } = "HoughCircle";
        public int MinRadius { get; set; }
        public int MaxRadius { get; set; }
        public double SceneCenterX { get; set; }
        public double SceneCenterY { get; set; }
        public double SceneRadius { get; set; }
        public int Supersample { get; set; } = 16;
        public double CenterTolerance { get; set; }
        public double RadiusTolerance { get; set; }
    }

    private sealed class LineBaseline
    {
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public string Method { get; set; } = "FitLine";
        public int Threshold { get; set; }
        public double MinLength { get; set; }
        public double MaxGap { get; set; }
        public double SceneStartX { get; set; }
        public double SceneStartY { get; set; }
        public double SceneEndX { get; set; }
        public double SceneEndY { get; set; }
        public double SceneThicknessPx { get; set; } = 6.0;
        public int Supersample { get; set; } = 16;
        public double AngleTolerance { get; set; }
        public double ExpectedMinLength { get; set; }
        public double ExpectedMaxLength { get; set; }
        public double MaxResidualMean { get; set; }
    }
}
