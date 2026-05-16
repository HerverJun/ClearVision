using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Calibration;
using Acme.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Acme.Product.Tests.Operators;

public class NPointCalibrationOperatorTests
{
    private readonly NPointCalibrationOperator _operator;

    public NPointCalibrationOperatorTests()
    {
        _operator = new NPointCalibrationOperator(Substitute.For<ILogger<NPointCalibrationOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeNPointCalibration()
    {
        Assert.Equal(OperatorType.NPointCalibration, _operator.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithAffinePairs_ShouldReturnMatrix()
    {
        var pairsJson = "[" +
                        "{\"ImageX\":0,\"ImageY\":0,\"WorldX\":0,\"WorldY\":0}," +
                        "{\"ImageX\":10,\"ImageY\":0,\"WorldX\":20,\"WorldY\":0}," +
                        "{\"ImageX\":0,\"ImageY\":10,\"WorldX\":0,\"WorldY\":20}" +
                        "]";

        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", pairsJson }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        var calibrationData = Assert.IsType<string>(result.OutputData!["CalibrationData"]);
        Assert.True(CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out var error), error);
        Assert.NotNull(bundle.Transform2D);
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitAcceptanceThresholds_ShouldAllowTighterQualityGate()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", CreateNoisyAffinePairsJson() },
            { "MaxAcceptedReprojectionError", 0.001 },
            { "MinInlierCount", 4 },
            { "MinInlierRatio", 0.75 },
            { "CalibrationUnit", "um" }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(Assert.IsType<bool>(result.OutputData!["Accepted"]));
        Assert.Equal("um", Assert.IsType<string>(result.OutputData["CalibrationUnit"]));
        Assert.Equal("um", Assert.IsType<string>(result.OutputData["ReprojectionErrorUnit"]));

        var calibrationData = Assert.IsType<string>(result.OutputData["CalibrationData"]);
        Assert.True(CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out var error), error);
        Assert.Equal("um", bundle.Unit);
        Assert.False(bundle.Quality.Accepted);
        Assert.Contains(bundle.Quality.Diagnostics, d => d.Contains("MaxAcceptedReprojectionError=0.001000 um", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitRansacParameters_ShouldExposeConfiguredValues()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Perspective" },
            { "PointPairs", CreatePerspectivePairsJson() },
            { "RansacReprojectionThreshold", 1.25 },
            { "RansacMaxIterations", 777 },
            { "RansacConfidence", 0.99 },
            { "MinInlierCount", 4 },
            { "MinInlierRatio", 1.0 },
            { "MaxAcceptedReprojectionError", 0.01 },
            { "CalibrationUnit", "mm" }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1.25, Assert.IsType<double>(result.OutputData!["RansacReprojectionThreshold"]), precision: 6);
        Assert.Equal(777, Assert.IsType<int>(result.OutputData["RansacMaxIterations"]));
        Assert.Equal(0.99, Assert.IsType<double>(result.OutputData["RansacConfidence"]), precision: 6);
        Assert.Equal(4, Assert.IsType<int>(result.OutputData["MinInlierCount"]));
        Assert.Equal(1.0, Assert.IsType<double>(result.OutputData["MinInlierRatio"]), precision: 6);

        var calibrationData = Assert.IsType<string>(result.OutputData["CalibrationData"]);
        Assert.True(CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out var error), error);
        Assert.Contains(bundle.Quality.Diagnostics, d => d.Contains("RansacReprojectionThreshold=1.250000 mm", StringComparison.Ordinal));
        Assert.Contains(bundle.Quality.Diagnostics, d => d.Contains("RansacMaxIterations=777", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithAffineOutliers_ShouldKeepInlierCalibrationAccepted()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", CreateRegressionAffinePairsJson(
                imagePoints: new[]
                {
                    (0.0, 0.0), (100.0, 0.0), (0.0, 100.0), (100.0, 100.0),
                    (40.0, 20.0), (80.0, 50.0), (25.0, 90.0), (120.0, 30.0)
                },
                worldFunc: p => ((p.X * 0.5) + 12.0, (p.Y * 0.5) - 7.0),
                extraPairs: new[]
                {
                    (10.0, 10.0, 200.0, -200.0),
                    (110.0, 90.0, -120.0, 160.0)
                }) },
            { "RansacReprojectionThreshold", 1.0 },
            { "MaxAcceptedReprojectionError", 0.05 },
            { "MinInlierCount", 8 },
            { "MinInlierRatio", 0.75 }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Convert.ToBoolean(result.OutputData!["Accepted"]).Should().BeTrue();
        Convert.ToInt32(result.OutputData["TotalSampleCount"]).Should().Be(10);
        Convert.ToInt32(result.OutputData["InlierCount"]).Should().BeGreaterThanOrEqualTo(8);
        Convert.ToDouble(result.OutputData["ReprojectionError"]).Should().BeLessThan(0.05);
    }

    [Fact]
    public async Task ExecuteAsync_WithLooseAffineResidualThreshold_ShouldAcceptSmallSyntheticNoise()
    {
        var imagePoints = new[]
        {
            (0.0, 0.0), (100.0, 0.0), (0.0, 100.0), (100.0, 100.0),
            (50.0, 20.0), (25.0, 80.0), (90.0, 60.0), (120.0, 40.0)
        };
        var noise = new[]
        {
            (0.00, 0.00), (0.16, -0.10), (-0.12, 0.15), (0.18, 0.12),
            (-0.10, -0.14), (0.13, 0.08), (-0.16, 0.10), (0.10, -0.18)
        };

        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", CreateRegressionAffinePairsJson(
                imagePoints,
                p =>
                {
                    var i = Array.IndexOf(imagePoints, p);
                    return ((p.X * 0.25) + 3.0 + noise[i].Item1, (p.Y * 0.25) - 5.0 + noise[i].Item2);
                }) },
            { "RansacReprojectionThreshold", 1.0 },
            { "MaxAcceptedReprojectionError", 0.5 },
            { "MinInlierCount", 8 },
            { "MinInlierRatio", 1.0 }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Convert.ToBoolean(result.OutputData!["Accepted"]).Should().BeTrue();
        Convert.ToDouble(result.OutputData["MaxReprojectionError"]).Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithSingleLargeInlierResidual_ShouldRejectWhenMaxExceedsThreshold()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", CreateSingleSpikeAffinePairsJson() },
            { "RansacReprojectionThreshold", 100.0 },
            { "MaxAcceptedReprojectionError", 10.0 },
            { "MinInlierCount", 21 },
            { "MinInlierRatio", 1.0 }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Convert.ToBoolean(result.OutputData!["Accepted"]).Should().BeFalse();

        var meanError = Convert.ToDouble(result.OutputData["InlierMeanReprojectionError"]);
        var maxError = Convert.ToDouble(result.OutputData["InlierMaxReprojectionError"]);
        meanError.Should().BeLessThan(10.0);
        maxError.Should().BeGreaterThan(10.0);

        result.OutputData.Should().ContainKey("ReprojectionError");
        result.OutputData.Should().ContainKey("MaxReprojectionError");
        Convert.ToDouble(result.OutputData["ReprojectionError"]).Should().BeApproximately(meanError, 1e-9);
        Convert.ToDouble(result.OutputData["MaxReprojectionError"]).Should().BeApproximately(maxError, 1e-9);
        result.OutputData["ReprojectionErrorScope"].Should().Be("Inlier");
    }

    [Fact]
    public async Task ExecuteAsync_WithSingleLargeInlierResidual_ShouldAcceptWhenMaxThresholdIsLoose()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", CreateSingleSpikeAffinePairsJson() },
            { "RansacReprojectionThreshold", 100.0 },
            { "MaxAcceptedReprojectionError", 40.0 },
            { "MinInlierCount", 21 },
            { "MinInlierRatio", 1.0 }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Convert.ToBoolean(result.OutputData!["Accepted"]).Should().BeTrue();
        Convert.ToDouble(result.OutputData["InlierMaxReprojectionError"]).Should().BeLessThan(40.0);
        Convert.ToDouble(result.OutputData["AllSampleMaxReprojectionError"]).Should().BeApproximately(
            Convert.ToDouble(result.OutputData["InlierMaxReprojectionError"]),
            1e-9);
    }

    [Fact]
    public async Task ExecuteAsync_WithStrictAffineResidualThreshold_ShouldRejectSameSyntheticNoise()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Affine" },
            { "PointPairs", CreateNoisyAffinePairsJson() },
            { "RansacReprojectionThreshold", 1.0 },
            { "MaxAcceptedReprojectionError", 0.001 },
            { "MinInlierCount", 4 },
            { "MinInlierRatio", 0.75 }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Convert.ToBoolean(result.OutputData!["Accepted"]).Should().BeFalse();
        result.OutputData.Should().ContainKeys(
            "ReprojectionError",
            "MaxReprojectionError",
            "MeanReprojectionError",
            "InlierMeanReprojectionError",
            "InlierMaxReprojectionError",
            "AllSampleMeanReprojectionError",
            "AllSampleMaxReprojectionError",
            "ReprojectionErrorScope");

        var calibrationData = Assert.IsType<string>(result.OutputData["CalibrationData"]);
        Assert.True(CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out var error), error);
        bundle.Quality.Accepted.Should().BeFalse();
        bundle.Quality.Diagnostics.Should().Contain(d => d.Contains("MaxAcceptedReprojectionError", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithInsufficientPerspectivePairs_ShouldReturnFailure()
    {
        var pairsJson = "[" +
                        "{\"ImageX\":0,\"ImageY\":0,\"WorldX\":0,\"WorldY\":0}," +
                        "{\"ImageX\":10,\"ImageY\":0,\"WorldX\":20,\"WorldY\":0}," +
                        "{\"ImageX\":0,\"ImageY\":10,\"WorldX\":0,\"WorldY\":20}" +
                        "]";

        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationMode", "Perspective" },
            { "PointPairs", pairsJson }
        });

        var result = await _operator.ExecuteAsync(op, null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateParameters_WithInvalidMode_ShouldReturnInvalid()
    {
        var op = CreateOperator(new Dictionary<string, object> { { "CalibrationMode", "Unknown" } });

        var validation = _operator.ValidateParameters(op);

        Assert.False(validation.IsValid);
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

    private static string CreateNoisyAffinePairsJson()
    {
        return "[" +
               "{\"ImageX\":0,\"ImageY\":0,\"WorldX\":0.00,\"WorldY\":0.03}," +
               "{\"ImageX\":10,\"ImageY\":0,\"WorldX\":20.04,\"WorldY\":0.00}," +
               "{\"ImageX\":0,\"ImageY\":10,\"WorldX\":0.00,\"WorldY\":19.96}," +
               "{\"ImageX\":10,\"ImageY\":10,\"WorldX\":20.03,\"WorldY\":20.02}," +
               "{\"ImageX\":5,\"ImageY\":6,\"WorldX\":10.02,\"WorldY\":12.03}" +
               "]";
    }

    private static string CreateSingleSpikeAffinePairsJson()
    {
        var imagePoints = new[]
        {
            (0.0, 0.0), (40.0, 0.0), (80.0, 0.0), (120.0, 0.0),
            (0.0, 40.0), (40.0, 40.0), (80.0, 40.0), (120.0, 40.0),
            (0.0, 80.0), (40.0, 80.0), (80.0, 80.0), (120.0, 80.0),
            (20.0, 120.0), (60.0, 120.0), (100.0, 120.0), (140.0, 120.0),
            (30.0, 20.0), (70.0, 60.0), (110.0, 100.0), (150.0, 50.0)
        };

        return CreateRegressionAffinePairsJson(
            imagePoints,
            p => ((p.X * 0.5) + 12.0, (p.Y * 0.5) - 7.0),
            extraPairs: new[]
            {
                (160.0, 120.0, 92.0, 83.0)
            });
    }

    private static string CreatePerspectivePairsJson()
    {
        return "[" +
               "{\"ImageX\":0,\"ImageY\":0,\"WorldX\":0,\"WorldY\":0}," +
               "{\"ImageX\":100,\"ImageY\":0,\"WorldX\":200,\"WorldY\":10}," +
               "{\"ImageX\":100,\"ImageY\":80,\"WorldX\":190,\"WorldY\":160}," +
               "{\"ImageX\":0,\"ImageY\":80,\"WorldX\":5,\"WorldY\":150}," +
               "{\"ImageX\":50,\"ImageY\":40,\"WorldX\":98,\"WorldY\":78}" +
               "]";
    }

    private static string CreateRegressionAffinePairsJson(
        IReadOnlyList<(double X, double Y)> imagePoints,
        Func<(double X, double Y), (double X, double Y)> worldFunc,
        IReadOnlyList<(double ImageX, double ImageY, double WorldX, double WorldY)>? extraPairs = null)
    {
        var rows = new List<string>();
        foreach (var imagePoint in imagePoints)
        {
            var worldPoint = worldFunc(imagePoint);
            rows.Add(CreatePointPairJson(imagePoint.X, imagePoint.Y, worldPoint.X, worldPoint.Y));
        }

        if (extraPairs != null)
        {
            rows.AddRange(extraPairs.Select(p => CreatePointPairJson(p.ImageX, p.ImageY, p.WorldX, p.WorldY)));
        }

        return "[" + string.Join(",", rows) + "]";
    }

    private static string CreatePointPairJson(double imageX, double imageY, double worldX, double worldY)
    {
        return FormattableString.Invariant(
            $"{{\"ImageX\":{imageX},\"ImageY\":{imageY},\"WorldX\":{worldX},\"WorldY\":{worldY}}}");
    }
}
