using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Calibration;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;

var options = RunnerOptions.Parse(args);
if (options.ShowHelp)
{
    RunnerOptions.PrintHelp();
    return options.ParseError is null ? 0 : 2;
}

if (options.ParseError is not null)
{
    Console.Error.WriteLine(options.ParseError);
    RunnerOptions.PrintHelp();
    return 2;
}

var result = await P2CalibrationResidualRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"P2 calibration residual baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class P2CalibrationResidualRunner
{
    public static async Task<BaselineResult> RunAsync()
    {
        using var workspace = TempWorkspace.Create();
        var cases = new List<RunnerCase>();
        AddCalibrationLoaderCases(cases, workspace.Root);
        AddNPointCalibrationCases(cases);
        AddTranslationRotationCalibrationCases(cases);

        var results = new List<CaseResult>(cases.Count);
        foreach (var runnerCase in cases)
        {
            results.Add(await RunCaseAsync(runnerCase));
        }

        var operators = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperatorEvidence(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(group.Average(item => item.MemoryAllocationBytes)))))
            .ToArray();

        var scenarios = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToArray();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                Math.Round(results.Sum(item => item.RuntimeMs), 3)),
            operators,
            scenarios,
            results);
    }

    private static async Task<CaseResult> RunCaseAsync(RunnerCase runnerCase)
    {
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var observed = await runnerCase.Body();
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                runnerCase.Id,
                runnerCase.Operator,
                runnerCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                null,
                observed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                runnerCase.Id,
                runnerCase.Operator,
                runnerCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                ex.GetBaseException().Message,
                new Dictionary<string, object?>());
        }
    }

    private static void AddCalibrationLoaderCases(List<RunnerCase> cases, string tempRoot)
    {
        var sut = new CalibrationLoaderOperator(NullLogger<CalibrationLoaderOperator>.Instance);

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "CalibrationLoader", $"valid_v2_bundle_{index:00}", "Valid bundle load", async () =>
            {
                var path = Path.Combine(tempRoot, $"calibration_loader_valid_{index:00}.json");
                await File.WriteAllTextAsync(path, CreateAcceptedBundleJson(index));
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.CalibrationLoader, ("FilePath", path)),
                    null);

                RequireSuccess(result);
                RequireOutput(result, "CalibrationData");
                RequireOutput(result, "CalibrationBundle");
                RequireBool(result, "Accepted", true);
                return Observed(("Accepted", true), ("PathExists", File.Exists(path)));
            });
        }

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "CalibrationLoader", $"missing_file_{index:00}", "Missing file failure contract", async () =>
            {
                var missingPath = Path.Combine(tempRoot, $"missing_{index:00}.json");
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.CalibrationLoader, ("FilePath", missingPath)),
                    null);

                RequireFailure(result, "Calibration file not found");
                return Observed(("FailureReason", "MissingFile"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "CalibrationLoader", $"invalid_json_{index:00}", "Invalid JSON failure contract", async () =>
            {
                var path = Path.Combine(tempRoot, $"calibration_loader_invalid_{index:00}.json");
                await File.WriteAllTextAsync(path, "{ invalid-json");
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.CalibrationLoader, ("FilePath", path)),
                    null);

                RequireFailure(result, "Invalid CalibrationBundleV2");
                return Observed(("FailureReason", "InvalidJson"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "CalibrationLoader", $"empty_path_validation_{index:00}", "Parameter validation contract", () =>
            {
                var validation = sut.ValidateParameters(
                    CreateOperator(OperatorType.CalibrationLoader, ("FilePath", string.Empty)));

                RequireInvalid(validation, "FilePath is required");
                return Task.FromResult(Observed(("FailureReason", "EmptyPath")));
            });
        }
    }

    private static void AddNPointCalibrationCases(List<RunnerCase> cases)
    {
        var sut = new NPointCalibrationOperator(NullLogger<NPointCalibrationOperator>.Instance);

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "NPointCalibration", $"affine_round_trip_{index:00}", "Affine geometry oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.NPointCalibration,
                        ("CalibrationMode", "Affine"),
                        ("PointPairs", BuildAffinePointPairsJson(index))),
                    null);

                RequireSuccess(result);
                var error = RequireDouble(result, "ReprojectionError");
                RequireLessOrEqual(error, 0.001, "Affine reprojection error");
                RequireAcceptedBundle(result, TransformModelV2.Affine);
                return Observed(("ReprojectionError", error), ("TransformModel", "Affine"));
            });
        }

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "NPointCalibration", $"perspective_round_trip_{index:00}", "Perspective geometry oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.NPointCalibration,
                        ("CalibrationMode", "Perspective"),
                        ("PointPairs", BuildPerspectivePointPairsJson(index))),
                    null);

                RequireSuccess(result);
                var error = RequireDouble(result, "ReprojectionError");
                RequireLessOrEqual(error, 0.001, "Perspective reprojection error");
                RequireAcceptedBundle(result, TransformModelV2.Homography);
                return Observed(("ReprojectionError", error), ("TransformModel", "Homography"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "NPointCalibration", $"insufficient_perspective_pairs_{index:00}", "Insufficient points failure contract", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.NPointCalibration,
                        ("CalibrationMode", "Perspective"),
                        ("PointPairs", BuildThreePointPairsJson(index))),
                    null);

                RequireFailure(result, "requires at least 4 point pairs");
                return Observed(("FailureReason", "InsufficientPerspectivePairs"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "NPointCalibration", $"invalid_mode_validation_{index:00}", "Parameter validation contract", () =>
            {
                var validation = sut.ValidateParameters(
                    CreateOperator(
                        OperatorType.NPointCalibration,
                        ("CalibrationMode", "Rigid"),
                        ("PointPairs", BuildAffinePointPairsJson(index))));

                RequireInvalid(validation, "CalibrationMode must be Affine or Perspective");
                return Task.FromResult(Observed(("FailureReason", "InvalidMode")));
            });
        }
    }

    private static void AddTranslationRotationCalibrationCases(List<RunnerCase> cases)
    {
        var sut = new TranslationRotationCalibrationOperator(NullLogger<TranslationRotationCalibrationOperator>.Instance);

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "TranslationRotationCalibration", $"least_squares_similarity_{index:00}", "Similarity transform oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.TranslationRotationCalibration,
                        ("Method", "LeastSquares"),
                        ("CalibrationPoints", BuildTranslationRotationPointsJson(index, scale: 1.18, rotationDeg: 4.0 + index))),
                    null);

                RequireSuccess(result);
                var error = RequireDouble(result, "CalibrationError");
                RequireLessOrEqual(error, 0.001, "LeastSquares calibration error");
                RequireString(result, "TransformModel", "Similarity");
                return Observed(("CalibrationError", error), ("TransformModel", "Similarity"));
            });
        }

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "TranslationRotationCalibration", $"svd_rigid_{index:00}", "Rigid transform oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.TranslationRotationCalibration,
                        ("Method", "SVD"),
                        ("CalibrationPoints", BuildTranslationRotationPointsJson(index, scale: 1.0, rotationDeg: -6.0 - index))),
                    null);

                RequireSuccess(result);
                var error = RequireDouble(result, "CalibrationError");
                RequireLessOrEqual(error, 0.001, "SVD calibration error");
                RequireString(result, "TransformModel", "Rigid");
                return Observed(("CalibrationError", error), ("TransformModel", "Rigid"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "TranslationRotationCalibration", $"too_few_points_{index:00}", "Insufficient points failure contract", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.TranslationRotationCalibration,
                        ("Method", "LeastSquares"),
                        ("CalibrationPoints", BuildTooFewCalibrationPointsJson(index))),
                    null);

                RequireFailure(result, "at least 3 valid points");
                return Observed(("FailureReason", "TooFewPoints"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "TranslationRotationCalibration", $"degenerate_points_{index:00}", "Degenerate geometry failure contract", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.TranslationRotationCalibration,
                        ("Method", "SVD"),
                        ("CalibrationPoints", BuildDegenerateCalibrationPointsJson(index))),
                    null);

                RequireFailure(result, "degenerate");
                return Observed(("FailureReason", "DegenerateGeometry"));
            });
        }
    }

    private static void Add(
        List<RunnerCase> cases,
        string operatorName,
        string id,
        string scenario,
        Func<Task<Dictionary<string, object?>>> body)
    {
        cases.Add(new RunnerCase($"{operatorName}_{id}", operatorName, scenario, body));
    }

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(type.ToString(), type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, ParameterType(value), value));
        }

        return op;
    }

    private static string ParameterType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            float or double or decimal => "float",
            _ => "string"
        };
    }

    private static string CreateAcceptedBundleJson(int index)
    {
        var focal = 500.0 + index;
        var cx = 160.0 + (index * 0.25);
        var cy = 120.0 - (index * 0.2);
        return $$"""
                 {
                   "schemaVersion": 2,
                   "calibrationKind": "cameraIntrinsics",
                   "transformModel": "none",
                   "sourceFrame": "image",
                   "targetFrame": "imageUndistorted",
                   "unit": "mm",
                   "imageSize": {
                     "width": 320,
                     "height": 240
                   },
                   "intrinsics": {
                     "cameraMatrix": [
                       [{{Invariant(focal)}}, 0.0, {{Invariant(cx)}}],
                       [0.0, {{Invariant(focal)}}, {{Invariant(cy)}}],
                       [0.0, 0.0, 1.0]
                     ]
                   },
                   "distortion": {
                     "model": "brownConrady",
                     "coefficients": [0.1, 0.01, 0.0, 0.0, 0.0]
                   },
                   "quality": {
                     "accepted": true,
                     "meanError": 0.11,
                     "maxError": 0.23,
                     "inlierCount": 24,
                     "totalSampleCount": 24,
                     "diagnostics": []
                   },
                   "producerOperator": "P2CalibrationResidualRunner"
                 }
                 """;
    }

    private static string BuildAffinePointPairsJson(int index)
    {
        var scale = 1.7 + (index * 0.03);
        var angle = (5.0 + index) * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var tx = 12.0 + index;
        var ty = -7.0 + (index * 0.5);
        var points = BasePoints(index)
            .Select(point =>
            {
                var worldX = tx + (scale * ((cos * point.X) - (sin * point.Y)));
                var worldY = ty + (scale * ((sin * point.X) + (cos * point.Y)));
                return new PointPairPayload(point.X, point.Y, worldX, worldY);
            })
            .ToArray();

        return JsonSerializer.Serialize(points, JsonSettings.Compact);
    }

    private static string BuildPerspectivePointPairsJson(int index)
    {
        var h00 = 1.05 + (index * 0.002);
        var h01 = 0.015;
        var h02 = 8.0 + index;
        var h10 = -0.012;
        var h11 = 0.98 + (index * 0.001);
        var h12 = -5.0;
        var h20 = 0.00008;
        var h21 = -0.00005;
        const double h22 = 1.0;

        var points = BasePoints(index)
            .Select(point =>
            {
                var denom = (h20 * point.X) + (h21 * point.Y) + h22;
                var worldX = ((h00 * point.X) + (h01 * point.Y) + h02) / denom;
                var worldY = ((h10 * point.X) + (h11 * point.Y) + h12) / denom;
                return new PointPairPayload(point.X, point.Y, worldX, worldY);
            })
            .ToArray();

        return JsonSerializer.Serialize(points, JsonSettings.Compact);
    }

    private static string BuildThreePointPairsJson(int index)
    {
        return JsonSerializer.Serialize(BasePoints(index).Take(3).Select(point =>
            new PointPairPayload(point.X, point.Y, point.X + 1.0, point.Y + 1.0)), JsonSettings.Compact);
    }

    private static string BuildTranslationRotationPointsJson(int index, double scale, double rotationDeg)
    {
        var angle = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var tx = 32.0 + index;
        var ty = -15.0 + (index * 0.75);

        var points = BasePoints(index)
            .Select(point =>
            {
                var robotX = tx + (scale * ((cos * point.X) - (sin * point.Y)));
                var robotY = ty + (scale * ((sin * point.X) + (cos * point.Y)));
                return new CalibrationPointPayload(point.X, point.Y, robotX, robotY, null);
            })
            .ToArray();

        return JsonSerializer.Serialize(points, JsonSettings.Compact);
    }

    private static string BuildTooFewCalibrationPointsJson(int index)
    {
        return JsonSerializer.Serialize(BasePoints(index).Take(2).Select(point =>
            new CalibrationPointPayload(point.X, point.Y, point.X + 10.0, point.Y + 20.0, null)), JsonSettings.Compact);
    }

    private static string BuildDegenerateCalibrationPointsJson(int index)
    {
        var offset = index * 0.01;
        var points = new[]
        {
            new CalibrationPointPayload(10.0 + offset, 10.0, 20.0, 20.0, null),
            new CalibrationPointPayload(10.0 + offset, 10.0, 20.0, 20.0, null),
            new CalibrationPointPayload(10.0 + offset, 10.0, 20.0, 20.0, null)
        };
        return JsonSerializer.Serialize(points, JsonSettings.Compact);
    }

    private static Vec2[] BasePoints(int index)
    {
        var shift = index * 0.35;
        return new[]
        {
            new Vec2(0.0 + shift, 0.0),
            new Vec2(40.0 + shift, 0.0),
            new Vec2(0.0 + shift, 35.0),
            new Vec2(45.0 + shift, 30.0),
            new Vec2(20.0 + shift, 18.0)
        };
    }

    private static string Invariant(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object?> Observed(params (string Name, object? Value)[] values)
    {
        return values.ToDictionary(item => item.Name, item => item.Value);
    }

    private static void RequireSuccess(OperatorExecutionOutput result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected success, got failure: {result.ErrorMessage}");
        }
    }

    private static void RequireFailure(OperatorExecutionOutput result, string messageFragment)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Expected failure, got success.");
        }

        if (result.ErrorMessage is null ||
            result.ErrorMessage.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException($"Expected failure containing '{messageFragment}', got '{result.ErrorMessage}'.");
        }
    }

    private static void RequireInvalid(ValidationResult validation, string messageFragment)
    {
        if (validation.IsValid)
        {
            throw new InvalidOperationException("Expected invalid validation result.");
        }

        if (!validation.Errors.Any(error => error.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            throw new InvalidOperationException($"Expected validation error containing '{messageFragment}', got '{string.Join("; ", validation.Errors)}'.");
        }
    }

    private static void RequireOutput(OperatorExecutionOutput result, string key)
    {
        if (result.OutputData is null || !result.OutputData.ContainsKey(key))
        {
            throw new InvalidOperationException($"Missing output key '{key}'.");
        }
    }

    private static void RequireBool(OperatorExecutionOutput result, string key, bool expected)
    {
        RequireOutput(result, key);
        if (result.OutputData![key] is not bool actual || actual != expected)
        {
            throw new InvalidOperationException($"Expected {key}={expected}.");
        }
    }

    private static void RequireString(OperatorExecutionOutput result, string key, string expected)
    {
        RequireOutput(result, key);
        if (result.OutputData![key]?.ToString() != expected)
        {
            throw new InvalidOperationException($"Expected {key}={expected}, got {result.OutputData[key]}.");
        }
    }

    private static double RequireDouble(OperatorExecutionOutput result, string key)
    {
        RequireOutput(result, key);
        var value = result.OutputData![key];
        return value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            _ => throw new InvalidOperationException($"Expected numeric output '{key}', got {value?.GetType().Name ?? "null"}.")
        };
    }

    private static void RequireLessOrEqual(double actual, double max, string label)
    {
        if (!double.IsFinite(actual) || actual > max)
        {
            throw new InvalidOperationException($"{label} {actual.ToString("G17", CultureInfo.InvariantCulture)} exceeds {max.ToString("G17", CultureInfo.InvariantCulture)}.");
        }
    }

    private static void RequireAcceptedBundle(OperatorExecutionOutput result, TransformModelV2 expectedModel)
    {
        RequireOutput(result, "CalibrationData");
        var calibrationData = result.OutputData!["CalibrationData"]?.ToString() ?? string.Empty;
        if (!CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out var error))
        {
            throw new InvalidOperationException($"CalibrationData is not a valid CalibrationBundleV2: {error}");
        }

        if (bundle.Transform2D is null)
        {
            throw new InvalidOperationException("Calibration bundle is missing Transform2D.");
        }

        if (bundle.TransformModel != expectedModel || bundle.Transform2D.Model != expectedModel)
        {
            throw new InvalidOperationException($"Expected transform model {expectedModel}, got {bundle.TransformModel}/{bundle.Transform2D.Model}.");
        }

        if (!bundle.Quality.Accepted)
        {
            throw new InvalidOperationException("Calibration bundle was not accepted.");
        }
    }
}

internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    private TempWorkspace(string root)
    {
        Root = root;
    }

    public static TempWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"clearvision_p2_calibration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new TempWorkspace(root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp fixtures.
        }
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# P2 Calibration Residual Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "",
            "## Summary",
            "",
            $"CaseCount: {result.Summary.CaseCount}",
            $"Passed: {result.Summary.Passed}",
            $"Failed: {result.Summary.Failed}",
            $"RuntimeMs: {result.Summary.RuntimeMs}",
            "",
            "## Operators",
            "",
            "| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryBytesAvg |",
            "|---|---:|---:|---:|---:|---:|"
        };

        foreach (var op in result.Operators)
        {
            lines.Add($"| {op.Operator} | {op.CaseCount} | {op.Passed} | {op.Failed} | {op.RuntimeMsAvg} | {op.MemoryAllocationBytesAvg} |");
        }

        lines.Add("");
        lines.Add("## Scenarios");
        lines.Add("");
        lines.Add("| Scenario | Cases | Passed | Failed | RuntimeMsAvg |");
        lines.Add("|---|---:|---:|---:|---:|");
        foreach (var scenario in result.Scenarios)
        {
            lines.Add($"| {scenario.Scenario} | {scenario.CaseCount} | {scenario.Passed} | {scenario.Failed} | {scenario.RuntimeMsAvg} |");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerCase(
    string Id,
    string Operator,
    string Scenario,
    Func<Task<Dictionary<string, object?>>> Body);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    OperatorEvidence[] Operators,
    ScenarioSummary[] Scenarios,
    List<CaseResult> Cases);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs);

internal sealed record OperatorEvidence(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    Dictionary<string, object?> Observed);

internal sealed record Vec2(double X, double Y);

internal sealed record PointPairPayload(double ImageX, double ImageY, double WorldX, double WorldY);

internal sealed record CalibrationPointPayload(double ImageX, double ImageY, double RobotX, double RobotY, double? Angle);

internal static class RunnerOptions
{
    public static ParsedOptions Parse(string[] args)
    {
        var outputPath = "quality/evals/reports/P2CalibrationResidual_baseline.json";
        var reportPath = "quality/evals/reports/P2CalibrationResidual_baseline.md";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return new ParsedOptions(outputPath, reportPath, true, null);
            }

            if ((arg is "-o" or "--output") && i + 1 < args.Length)
            {
                outputPath = args[++i];
                continue;
            }

            if ((arg is "-r" or "--report") && i + 1 < args.Length)
            {
                reportPath = args[++i];
                continue;
            }

            return new ParsedOptions(outputPath, reportPath, false, $"Unknown or incomplete argument: {arg}");
        }

        return new ParsedOptions(outputPath, reportPath, false, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            Usage: dotnet run --project quality/tools/P2CalibrationResidualRunner/P2CalibrationResidualRunner.csproj -- [options]

            Options:
              -o, --output <path>   JSON baseline output path.
              -r, --report <path>   Markdown report output path.
              -h, --help            Show help.
            """);
    }
}

internal sealed record ParsedOptions(
    string OutputPath,
    string ReportPath,
    bool ShowHelp,
    string? ParseError);

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false
    };
}
