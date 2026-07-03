using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

public class NPointCalibrationSolverTests
{
    [Theory]
    [InlineData(NPointCalibrationMode.Affine)]
    [InlineData(NPointCalibrationMode.Perspective)]
    public async Task Solve_WithStableSamples_ShouldMatchOperatorLegacyOutputs(NPointCalibrationMode mode)
    {
        var pairs = mode == NPointCalibrationMode.Affine
            ? CreateAffinePairs()
            : CreatePerspectivePairs();
        var options = CreateOptions(mode);
        var solver = new NPointCalibrationSolver();

        var solverResult = solver.Solve(new NPointCalibrationRequest(mode, pairs, options));
        solverResult.Success.Should().BeTrue(solverResult.ErrorMessage);

        var operatorExecutor = new NPointCalibrationOperator(Substitute.For<ILogger<NPointCalibrationOperator>>());
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["CalibrationMode"] = mode == NPointCalibrationMode.Perspective ? "Perspective" : "Affine",
            ["PointPairs"] = ToPointPairsJson(pairs),
            ["RansacReprojectionThreshold"] = options.RansacReprojectionThreshold,
            ["RansacMaxIterations"] = options.RansacMaxIterations,
            ["RansacConfidence"] = options.RansacConfidence,
            ["MaxAcceptedReprojectionError"] = options.MaxAcceptedReprojectionError,
            ["MinInlierCount"] = options.MinInlierCount,
            ["MinInlierRatio"] = options.MinInlierRatio,
            ["CalibrationUnit"] = options.CalibrationUnit
        });

        var operatorResult = await operatorExecutor.ExecuteAsync(op, null);

        operatorResult.IsSuccess.Should().BeTrue(operatorResult.ErrorMessage);
        operatorResult.OutputData.Should().NotBeNull();
        var output = operatorResult.OutputData!;
        output.Keys.Should().Contain(new[]
        {
            "CalibrationData",
            "CalibrationBundle",
            "ReprojectionError",
            "MaxReprojectionError",
            "MeanReprojectionError",
            "InlierMeanReprojectionError",
            "InlierMaxReprojectionError",
            "AllSampleMeanReprojectionError",
            "AllSampleMaxReprojectionError",
            "ReprojectionErrorScope",
            "InlierCount",
            "TotalSampleCount",
            "InlierRatio",
            "Accepted",
            "RansacReprojectionThreshold",
            "RansacMaxIterations",
            "RansacConfidence",
            "MaxAcceptedReprojectionError",
            "MinInlierCount",
            "MinInlierRatio",
            "ReprojectionErrorUnit",
            "CalibrationUnit"
        });

        var operatorBundle = output["CalibrationBundle"].Should().BeOfType<CalibrationBundleV2>().Subject;
        operatorBundle.ProducerOperator.Should().Be(nameof(NPointCalibrationOperator));
        operatorBundle.CalibrationKind.Should().Be(CalibrationKindV2.PlanarTransform2D);
        operatorBundle.TransformModel.Should().Be(solverResult.Bundle.TransformModel);
        operatorBundle.Transform2D.Should().NotBeNull();
        operatorBundle.Quality.Accepted.Should().Be(solverResult.Bundle.Quality.Accepted);
        operatorBundle.Quality.InlierCount.Should().Be(solverResult.Bundle.Quality.InlierCount);
        operatorBundle.Quality.TotalSampleCount.Should().Be(solverResult.Bundle.Quality.TotalSampleCount);
        operatorBundle.Quality.Diagnostics.Should().Equal(solverResult.Bundle.Quality.Diagnostics);
        AssertMatrixEquivalent(solverResult.TransformMatrix, operatorBundle.Transform2D!.Matrix);

        Convert.ToDouble(output["ReprojectionError"]).Should().BeApproximately(solverResult.ErrorStats.MeanError, 1e-9);
        Convert.ToDouble(output["MaxReprojectionError"]).Should().BeApproximately(solverResult.ErrorStats.MaxError, 1e-9);
        Convert.ToDouble(output["InlierMeanReprojectionError"]).Should().BeApproximately(solverResult.ErrorStats.InlierMeanError, 1e-9);
        Convert.ToDouble(output["InlierMaxReprojectionError"]).Should().BeApproximately(solverResult.ErrorStats.InlierMaxError, 1e-9);
        Convert.ToDouble(output["AllSampleMeanReprojectionError"]).Should().BeApproximately(solverResult.ErrorStats.AllSampleMeanError, 1e-9);
        Convert.ToDouble(output["AllSampleMaxReprojectionError"]).Should().BeApproximately(solverResult.ErrorStats.AllSampleMaxError, 1e-9);
        Convert.ToInt32(output["InlierCount"]).Should().Be(solverResult.ErrorStats.InlierCount);
        Convert.ToInt32(output["TotalSampleCount"]).Should().Be(pairs.Count);
        output["ReprojectionErrorScope"].Should().Be("Inlier");
    }

    [Fact]
    public void Solve_WithNonFinitePoint_ShouldFailClosed()
    {
        var pairs = CreateAffinePairs().ToArray();
        pairs[1] = new NPointCalibrationPointPair(new Position(double.PositiveInfinity, 0), pairs[1].WorldPoint);

        var result = new NPointCalibrationSolver().Solve(new NPointCalibrationRequest(
            NPointCalibrationMode.Affine,
            pairs,
            CreateOptions(NPointCalibrationMode.Affine)));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("non-finite");
    }

    [Fact]
    public void Solve_WithDuplicatePoint_ShouldFailClosed()
    {
        var pairs = CreateAffinePairs().ToArray();
        pairs[2] = pairs[1] with { WorldPoint = pairs[2].WorldPoint };

        var result = new NPointCalibrationSolver().Solve(new NPointCalibrationRequest(
            NPointCalibrationMode.Affine,
            pairs,
            CreateOptions(NPointCalibrationMode.Affine)));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("duplicate");
    }

    [Fact]
    public void Solve_WithCollinearImagePoints_ShouldFailClosed()
    {
        var pairs = new[]
        {
            Pair(0, 0, 0, 0),
            Pair(10, 0, 10, 0),
            Pair(20, 0, 20, 0),
            Pair(30, 0, 30, 0)
        };

        var result = new NPointCalibrationSolver().Solve(new NPointCalibrationRequest(
            NPointCalibrationMode.Affine,
            pairs,
            CreateOptions(NPointCalibrationMode.Affine)));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("degenerate");
    }

    [Fact]
    public void Solve_WithDegenerateWorldPoints_ShouldFailClosed()
    {
        var pairs = new[]
        {
            Pair(0, 0, 0, 0),
            Pair(100, 0, 10, 0),
            Pair(100, 80, 20, 0),
            Pair(0, 80, 30, 0),
            Pair(50, 40, 40, 0)
        };

        var result = new NPointCalibrationSolver().Solve(new NPointCalibrationRequest(
            NPointCalibrationMode.Perspective,
            pairs,
            CreateOptions(NPointCalibrationMode.Perspective)));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("degenerate");
    }

    [Fact]
    public void Solve_WithInvalidOptions_ShouldFailClosed()
    {
        var options = CreateOptions(NPointCalibrationMode.Affine) with
        {
            RansacConfidence = 1.0
        };

        var result = new NPointCalibrationSolver().Solve(new NPointCalibrationRequest(
            NPointCalibrationMode.Affine,
            CreateAffinePairs(),
            options));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RansacConfidence");
    }

    [Fact]
    public async Task ExecuteAsync_WithListPointPairsInput_ShouldPreserveLegacyListContract()
    {
        var operatorExecutor = new NPointCalibrationOperator(Substitute.For<ILogger<NPointCalibrationOperator>>());
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["CalibrationMode"] = "Affine"
        });
        var inputs = new Dictionary<string, object>
        {
            ["PointPairs"] = CreateAffinePairs()
                .Select(pair => new Dictionary<string, object>
                {
                    ["ImagePoint"] = new Dictionary<string, object>
                    {
                        ["X"] = pair.ImagePoint.X,
                        ["Y"] = pair.ImagePoint.Y
                    },
                    ["WorldPoint"] = new Dictionary<string, object>
                    {
                        ["X"] = pair.WorldPoint.X,
                        ["Y"] = pair.WorldPoint.Y
                    }
                })
                .ToList()
        };

        var result = await operatorExecutor.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["TotalSampleCount"]).Should().Be(CreateAffinePairs().Count);
        var bundle = result.OutputData["CalibrationBundle"].Should().BeOfType<CalibrationBundleV2>().Subject;
        bundle.ProducerOperator.Should().Be(nameof(NPointCalibrationOperator));
    }

    [Fact]
    public void NPointCalibrationOperator_ShouldNotKeepSecondOpenCvSolvePath()
    {
        var repoRoot = ResolveRepoRoot();
        var sourcePath = Path.Combine(
            repoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Operators",
            "NPointCalibrationOperator.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("NPointCalibrationRequest");
        source.Should().NotContain("EstimateAffine2D");
        source.Should().NotContain("FindHomography");
        source.Should().NotContain("HomographyMethods.Ransac");
    }

    private static NPointCalibrationOptions CreateOptions(NPointCalibrationMode mode)
    {
        return new NPointCalibrationOptions(
            RansacReprojectionThreshold: 1.0,
            RansacMaxIterations: 3000,
            RansacConfidence: 0.995,
            MaxAcceptedReprojectionError: 0.05,
            MinInlierCount: mode == NPointCalibrationMode.Perspective ? 4 : 3,
            MinInlierRatio: 0.5,
            CalibrationUnit: "mm",
            ProducerOperator: nameof(NPointCalibrationOperator));
    }

    private static IReadOnlyList<NPointCalibrationPointPair> CreateAffinePairs()
    {
        return new[]
        {
            Pair(0, 0, 12, -7),
            Pair(100, 0, 62, -7),
            Pair(0, 100, 12, 43),
            Pair(100, 100, 62, 43),
            Pair(40, 20, 32, 3),
            Pair(80, 50, 52, 18)
        };
    }

    private static IReadOnlyList<NPointCalibrationPointPair> CreatePerspectivePairs()
    {
        return new[]
        {
            Pair(0, 0, 0, 0),
            Pair(100, 0, 200, 10),
            Pair(100, 80, 190, 160),
            Pair(0, 80, 5, 150),
            Pair(50, 40, 98, 78)
        };
    }

    private static NPointCalibrationPointPair Pair(double imageX, double imageY, double worldX, double worldY)
    {
        return new NPointCalibrationPointPair(new Position(imageX, imageY), new Position(worldX, worldY));
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("NPoint", OperatorType.NPointCalibration, 0, 0);

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
            }
        }

        return op;
    }

    private static string ToPointPairsJson(IReadOnlyList<NPointCalibrationPointPair> pairs)
    {
        var rows = pairs.Select(pair => FormattableString.Invariant(
            $"{{\"ImageX\":{pair.ImagePoint.X},\"ImageY\":{pair.ImagePoint.Y},\"WorldX\":{pair.WorldPoint.X},\"WorldY\":{pair.WorldPoint.Y}}}"));
        return "[" + string.Join(",", rows) + "]";
    }

    private static void AssertMatrixEquivalent(IReadOnlyList<double[]> expected, IReadOnlyList<double[]> actual)
    {
        actual.Count.Should().Be(expected.Count);
        for (var row = 0; row < expected.Count; row++)
        {
            actual[row].Length.Should().Be(expected[row].Length);
            for (var col = 0; col < expected[row].Length; col++)
            {
                actual[row][col].Should().BeApproximately(expected[row][col], 1e-6);
            }
        }
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
