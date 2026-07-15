using System.Linq;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Calibration, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
[Trait("Category", "Sprint5_Phase2")]
public class TranslationRotationCalibrationOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeTranslationRotationCalibration()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.TranslationRotationCalibration, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithAffineLikePoints_ShouldProduceLowErrorTransform()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Method", "LeastSquares" },
            { "CalibrationPoints", BuildCalibrationPointsJson() }
        });

        var result = await sut.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);

        var calibrationData = Assert.IsType<string>(result.OutputData!["CalibrationData"]);
        Assert.True(CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out var error), error);
        Assert.NotNull(bundle.Transform2D);
        Assert.True(bundle.Transform2D!.Matrix.All(row => row.Length == 3));
        Assert.Equal("Similarity", result.OutputData["TransformModel"]);
        Assert.True(result.OutputData.ContainsKey("Accepted"));
    }

    [Fact]
    public void ValidateParameters_WithTooFewPoints_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CalibrationPoints", "[{\"imageX\":0,\"imageY\":0,\"robotX\":0,\"robotY\":0}]" }
        });

        var validation = sut.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task RobustPublicOutputs_ShouldBeDeclaredByMetadataWithStableTypes()
    {
        var metadata = new OperatorFactory().GetMetadata(OperatorType.TranslationRotationCalibration)!;
        var expected = new Dictionary<string, PortDataType>(StringComparer.Ordinal)
        {
            ["AllPointCalibrationError"] = PortDataType.Float,
            ["AllPointMaxCalibrationError"] = PortDataType.Float,
            ["InlierIndices"] = PortDataType.Any,
            ["OutlierIndices"] = PortDataType.Any
        };

        Assert.Equal(
            new[]
            {
                "CalibrationData", "CalibrationError", "MaxCalibrationError", "Accepted",
                "TransformModel", "RotationDeg", "AngleConstraintApplied", "RobustMode",
                "InlierCount", "OutlierCount", "Residuals", "Diagnostics"
            },
            metadata.OutputPorts.Take(12).Select(port => port.Name));

        foreach (var (name, type) in expected)
        {
            Assert.Contains(metadata.OutputPorts, port => port.Name == name && port.DataType == type);
        }

        var result = await Execute(BuildKnownTransformPoints(20), "Ransac");
        foreach (var name in expected.Keys)
        {
            Assert.True(result.OutputData!.ContainsKey(name));
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingRobustMode_ShouldMatchExplicitNone()
    {
        var points = BuildKnownTransformPoints(count: 20);
        var missing = await CreateSut().ExecuteAsync(CreateOperator(new() { ["Method"] = "LeastSquares", ["CalibrationPoints"] = points }), null);
        var explicitNone = await CreateSut().ExecuteAsync(CreateOperator(new() { ["Method"] = "LeastSquares", ["RobustMode"] = "None", ["CalibrationPoints"] = points }), null);

        Assert.True(missing.IsSuccess, missing.ErrorMessage);
        Assert.True(explicitNone.IsSuccess, explicitNone.ErrorMessage);
        AssertMatricesClose(ReadMatrix(missing), ReadMatrix(explicitNone), 1e-12);
        Assert.Equal((double)missing.OutputData!["CalibrationError"], (double)explicitNone.OutputData!["CalibrationError"], 12);
    }

    [Fact]
    public async Task ExecuteAsync_WithSingleOutlier_RobustModesShouldImproveTransformError()
    {
        var points = BuildKnownTransformPoints(count: 20, outliers: new Dictionary<int, (double X, double Y)> { [7] = (40, -35) });
        var none = await Execute(points, "None");
        var ransac = await Execute(points, "Ransac");
        var huber = await Execute(points, "Huber");
        var truth = ExpectedMatrix();

        var noneError = MatrixError(ReadMatrix(none), truth);
        var ransacError = MatrixError(ReadMatrix(ransac), truth);
        var huberError = MatrixError(ReadMatrix(huber), truth);

        Assert.True(ransacError < noneError * 0.10, $"RANSAC={ransacError}, None={noneError}");
        Assert.True(huberError < noneError * 0.35, $"Huber={huberError}, None={noneError}");
        Assert.Equal(1, Convert.ToInt32(ransac.OutputData!["OutlierCount"]));
        Assert.True(Convert.ToInt32(huber.OutputData!["OutlierCount"]) >= 1);
        Assert.Contains("termination_reason=", Assert.IsAssignableFrom<IReadOnlyList<string>>(ransac.OutputData!["Diagnostics"]).Single(item => item.StartsWith("termination_reason=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleOutliers_RansacShouldRecoverKnownTransform()
    {
        var points = BuildKnownTransformPoints(count: 30, outliers: new Dictionary<int, (double X, double Y)>
        {
            [2] = (80, -60), [11] = (-55, 75), [25] = (100, 40)
        });

        var result = await Execute(points, "Ransac");

        AssertMatricesClose(ReadMatrix(result), ExpectedMatrix(), 1e-9);
        Assert.Equal(3, Convert.ToInt32(result.OutputData!["OutlierCount"]));
        Assert.True((double)result.OutputData["CalibrationError"] < 1e-9);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutNoise_AllModesShouldRemainEquivalent()
    {
        var points = BuildKnownTransformPoints(count: 20);
        foreach (var mode in new[] { "None", "Ransac", "Huber" })
        {
            var result = await Execute(points, mode);
            AssertMatricesClose(ReadMatrix(result), ExpectedMatrix(), 1e-9);
            Assert.True((double)result.OutputData!["CalibrationError"] < 1e-9, mode);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NearCollinearWithoutAngle_ShouldFailClosed()
    {
        var points = JsonSerializer.Serialize(new[]
        {
            Point(0, 0), Point(10, 1e-12), Point(20, 2e-12), Point(30, 3e-12)
        });

        var result = await CreateSut().ExecuteAsync(CreateOperator(new() { ["CalibrationPoints"] = points, ["RobustMode"] = "None" }), null);

        Assert.False(result.IsSuccess);
        Assert.Contains("near-collinear", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CollinearWithReliableAngleConstraint_ShouldSucceed()
    {
        var points = JsonSerializer.Serialize(Enumerable.Range(0, 5).Select(i => Point(i * 10, 0, angle: 12.0)));

        var result = await CreateSut().ExecuteAsync(CreateOperator(new() { ["CalibrationPoints"] = points, ["RobustMode"] = "Ransac" }), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True((bool)result.OutputData!["AngleConstraintApplied"]);
        Assert.Equal(0, Convert.ToInt32(result.OutputData["OutlierCount"]));
    }

    [Fact]
    public async Task ExecuteAsync_WithSubEpsilonBaseline_ShouldFailWithExplicitReason()
    {
        var points = JsonSerializer.Serialize(new[]
        {
            Point(0, 0), Point(2e-13, 0), Point(0, 2e-13), Point(2e-13, 2e-13)
        });

        var result = await CreateSut().ExecuteAsync(CreateOperator(new() { ["CalibrationPoints"] = points }), null);

        Assert.False(result.IsSuccess);
        Assert.Contains("baseline is too small", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithExtremeButSupportedScale_ShouldRemainFiniteAndAccurate()
    {
        const double scale = 1e9;
        const double tx = 3e8;
        const double ty = -7e8;
        var radians = -17.0 * Math.PI / 180.0;
        var expected = new[]
        {
            new[] { scale * Math.Cos(radians), -scale * Math.Sin(radians), tx },
            new[] { scale * Math.Sin(radians), scale * Math.Cos(radians), ty }
        };
        var coordinates = new[] { (0.0, 0.0), (2.0, 0.0), (0.0, 3.0), (5.0, 4.0), (8.0, -2.0) };
        var points = JsonSerializer.Serialize(coordinates.Select(item => new
        {
            imageX = item.Item1,
            imageY = item.Item2,
            robotX = expected[0][0] * item.Item1 + expected[0][1] * item.Item2 + expected[0][2],
            robotY = expected[1][0] * item.Item1 + expected[1][1] * item.Item2 + expected[1][2]
        }));

        var result = await CreateSut().ExecuteAsync(CreateOperator(new() { ["CalibrationPoints"] = points }), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var actual = ReadMatrix(result);
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 3; column++)
        {
            var relativeError = Math.Abs(actual[row][column] - expected[row][column]) / Math.Max(1.0, Math.Abs(expected[row][column]));
            Assert.True(relativeError < 1e-12, $"matrix[{row}][{column}] relative error {relativeError:G17}");
        }
    }

    [Fact]
    public async Task ExecuteAsync_RobustModeWithMalformedPoint_ShouldFailInsteadOfSilentlyDiscarding()
    {
        var valid = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(BuildKnownTransformPoints(6))!;
        valid.Insert(2, new Dictionary<string, object> { ["imageX"] = "bad" });
        var json = JsonSerializer.Serialize(valid);

        var robust = await CreateSut().ExecuteAsync(CreateOperator(new() { ["CalibrationPoints"] = json, ["RobustMode"] = "Ransac" }), null);
        var compatibility = await CreateSut().ExecuteAsync(CreateOperator(new() { ["CalibrationPoints"] = json }), null);

        Assert.False(robust.IsSuccess);
        Assert.Contains("malformed", robust.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(compatibility.IsSuccess, compatibility.ErrorMessage);
        Assert.Contains("invalid_sample_count=1", Assert.IsAssignableFrom<IReadOnlyList<string>>(compatibility.OutputData!["Diagnostics"]));
    }

    [Fact]
    public async Task ExecuteAndValidate_InvalidMethodAndPartialAngles_ShouldAgreeOnFailure()
    {
        var sut = CreateSut();
        var invalidMethod = CreateOperator(new() { ["CalibrationPoints"] = BuildKnownTransformPoints(6), ["Method"] = "Unknown" });
        Assert.False(sut.ValidateParameters(invalidMethod).IsValid);
        Assert.False((await sut.ExecuteAsync(invalidMethod, null)).IsSuccess);

        var partialAngles = JsonSerializer.Serialize(new[]
        {
            Point(0, 0, angle: 12), Point(10, 0, angle: 12), Point(0, 10, angle: null), Point(10, 10, angle: 12)
        });
        var partial = CreateOperator(new() { ["CalibrationPoints"] = partialAngles });
        Assert.False(sut.ValidateParameters(partial).IsValid);
        Assert.False((await sut.ExecuteAsync(partial, null)).IsSuccess);
    }

    private static TranslationRotationCalibrationOperator CreateSut()
    {
        return new TranslationRotationCalibrationOperator(Substitute.For<ILogger<TranslationRotationCalibrationOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("TranslationRotationCalibration", OperatorType.TranslationRotationCalibration, 0, 0);

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
            }
        }

        return op;
    }

    private static string BuildCalibrationPointsJson()
    {
        return "[" +
               "{\"imageX\":0,\"imageY\":0,\"robotX\":10,\"robotY\":20,\"angle\":0}," +
               "{\"imageX\":10,\"imageY\":0,\"robotX\":20,\"robotY\":20,\"angle\":0}," +
               "{\"imageX\":0,\"imageY\":10,\"robotX\":10,\"robotY\":30,\"angle\":0}," +
               "{\"imageX\":20,\"imageY\":10,\"robotX\":30,\"robotY\":30,\"angle\":0}" +
               "]";
    }

    private static async Task<ClearVision.Product.Core.Operators.OperatorExecutionOutput> Execute(string points, string mode)
    {
        var result = await CreateSut().ExecuteAsync(CreateOperator(new()
        {
            ["CalibrationPoints"] = points,
            ["Method"] = "LeastSquares",
            ["RobustMode"] = mode,
            ["RobustResidualThreshold"] = 0.30,
            ["HuberDelta"] = 0.15,
            ["RobustMaxIterations"] = 256,
            ["RobustMinInlierRatio"] = 0.5
        }), null);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result;
    }

    private static string BuildKnownTransformPoints(int count, Dictionary<int, (double X, double Y)>? outliers = null)
    {
        var matrix = ExpectedMatrix();
        var points = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            var x = 20 + (i % 6) * 35.0;
            var y = 30 + (i / 6) * 28.0;
            var robotX = matrix[0][0] * x + matrix[0][1] * y + matrix[0][2];
            var robotY = matrix[1][0] * x + matrix[1][1] * y + matrix[1][2];
            if (outliers != null && outliers.TryGetValue(i, out var offset))
            {
                robotX += offset.X;
                robotY += offset.Y;
            }
            points.Add(new { imageX = x, imageY = y, robotX, robotY, angle = 12.0 });
        }
        return JsonSerializer.Serialize(points);
    }

    private static object Point(double x, double y, double? angle = null)
    {
        var matrix = ExpectedMatrix();
        return new
        {
            imageX = x,
            imageY = y,
            robotX = matrix[0][0] * x + matrix[0][1] * y + matrix[0][2],
            robotY = matrix[1][0] * x + matrix[1][1] * y + matrix[1][2],
            angle
        };
    }

    private static double[][] ExpectedMatrix()
    {
        const double scale = 0.25;
        var radians = 12.0 * Math.PI / 180.0;
        return new[]
        {
            new[] { scale * Math.Cos(radians), -scale * Math.Sin(radians), 38.0 },
            new[] { scale * Math.Sin(radians), scale * Math.Cos(radians), -17.0 }
        };
    }

    private static double[][] ReadMatrix(ClearVision.Product.Core.Operators.OperatorExecutionOutput output)
    {
        var data = Assert.IsType<string>(output.OutputData!["CalibrationData"]);
        Assert.True(CalibrationBundleV2Json.TryDeserialize(data, out var bundle, out var error), error);
        return bundle.Transform2D!.Matrix;
    }

    private static double MatrixError(double[][] actual, double[][] expected)
    {
        return Math.Sqrt(actual.SelectMany((row, i) => row.Select((value, j) => Math.Pow(value - expected[i][j], 2))).Sum());
    }

    private static void AssertMatricesClose(double[][] actual, double[][] expected, double tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var row = 0; row < expected.Length; row++)
        for (var column = 0; column < expected[row].Length; column++)
        {
            Assert.True(Math.Abs(actual[row][column] - expected[row][column]) <= tolerance,
                $"matrix[{row}][{column}] expected {expected[row][column]:G17}, actual {actual[row][column]:G17}");
        }
    }
}
