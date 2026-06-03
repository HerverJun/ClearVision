using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

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

var result = await ContractRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"PyramidShapeMatch contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "PyramidShapeMatch";

    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        Directory.CreateDirectory(options.WorkDir);
        var cases = BuildCases();
        var results = new List<CaseResult>();
        foreach (var testCase in cases)
        {
            results.Add(await RunCaseAsync(testCase, options));
        }

        var byOperator = results
            .GroupBy(item => item.Operator)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.MemoryAllocationBytes))))
            .ToList();

        var byScenario = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                Math.Round(results.Sum(item => item.RuntimeMs), 3)),
            byOperator,
            byScenario,
            results);
    }

    private static List<ContractCase> BuildCases()
    {
        var cases = new List<ContractCase>
        {
            ExecutionCase("template_input_exact", "Template mode", MatchMode.Template, SceneSpec.Exact(84, 96), true, false, true),
            ExecutionCase("template_path_exact", "Template mode", MatchMode.Template, SceneSpec.Exact(116, 88), false, true, true),
            ExecutionCase("template_low_threshold_exact", "Template mode", MatchMode.Template, SceneSpec.Exact(62, 120), true, false, true, p => p["MinScore"] = 55.0),
            ExecutionCase("template_max_matches_one", "Template mode", MatchMode.Template, SceneSpec.Exact(96, 70), true, false, true, p => p["MaxMatches"] = 1),
            ExecutionCase("template_grayscale_inputs", "Input formats", MatchMode.Template, SceneSpec.Exact(118, 112, grayscale: true), true, false, true),
            ExecutionCase("template_blank_scene_no_match", "Failure contract", MatchMode.Template, SceneSpec.CreateBlankScene(), true, false, false),
            ExecutionCase("template_blank_template_training_fails", "Failure contract", MatchMode.Template, SceneSpec.CreateBlankTemplate(), true, false, false, expectOperatorFailure: true),
            ExecutionCase("template_missing_template_fails", "Failure contract", MatchMode.Template, SceneSpec.Exact(80, 80), false, false, false, expectOperatorFailure: true),
            ExecutionCase("shape_descriptor_input_exact", "ShapeDescriptor mode", MatchMode.ShapeDescriptor, SceneSpec.Exact(86, 88), true, false, true, p => p["MinScore"] = 60.0),
            ExecutionCase("shape_descriptor_path_exact", "ShapeDescriptor mode", MatchMode.ShapeDescriptor, SceneSpec.Exact(126, 84), false, true, true, p => p["MinScore"] = 60.0),
            ExecutionCase("shape_descriptor_hu_only", "ShapeDescriptor mode", MatchMode.ShapeDescriptor, SceneSpec.Exact(58, 116), true, false, true, p =>
            {
                p["DescriptorTypes"] = "Hu";
                p["MinScore"] = 55.0;
            }),
            ExecutionCase("shape_descriptor_fourier_only", "ShapeDescriptor mode", MatchMode.ShapeDescriptor, SceneSpec.Exact(106, 124), true, false, true, p =>
            {
                p["DescriptorTypes"] = "Fourier";
                p["MinScore"] = 55.0;
            }),
            ExecutionCase("shape_descriptor_scaled_area_rejects", "ShapeDescriptor mode", MatchMode.ShapeDescriptor, SceneSpec.ScaledObject(92, 84, 1.45), true, false, false, p =>
            {
                p["AreaTolerance"] = 0.05;
                p["MinScore"] = 60.0;
            }),
            ExecutionCase("shape_descriptor_blank_scene_no_match", "Failure contract", MatchMode.ShapeDescriptor, SceneSpec.CreateBlankScene(), true, false, false, p => p["MinScore"] = 60.0),
            ValidationCase("validate_defaults", true),
            ValidationCase("validate_min_score_low_invalid", false, p => p["MinScore"] = -1.0),
            ValidationCase("validate_min_score_high_invalid", false, p => p["MinScore"] = 101.0),
            ValidationCase("validate_pyramid_low_invalid", false, p => p["PyramidLevels"] = 0),
            ValidationCase("validate_pyramid_high_invalid", false, p => p["PyramidLevels"] = 6),
            ValidationCase("validate_num_features_low_invalid", false, p => p["NumFeatures"] = 49),
            ValidationCase("validate_num_features_high_invalid", false, p => p["NumFeatures"] = 8192),
            ValidationCase("validate_spread_low_invalid", false, p => p["SpreadT"] = 0),
            ValidationCase("validate_angle_range_high_invalid", false, p => p["AngleRange"] = 181),
            ValidationCase("validate_angle_step_high_invalid", false, p => p["AngleStep"] = 46)
        };

        return cases;
    }

    private static ContractCase ExecutionCase(
        string caseId,
        string scenario,
        MatchMode mode,
        SceneSpec spec,
        bool useTemplateInput,
        bool useTemplatePath,
        bool expectIsMatch,
        Action<Dictionary<string, object?>>? configure = null,
        bool expectOperatorFailure = false)
    {
        return new ContractCase(caseId, OperatorName, scenario, async options =>
        {
            using var pair = SyntheticScene.Create(spec);
            var parameters = DefaultParameters(mode);
            configure?.Invoke(parameters);

            ImageWrapper? sceneWrapper = null;
            ImageWrapper? templateWrapper = null;
            OperatorExecutionOutput? execution = null;

            try
            {
                var inputs = new Dictionary<string, object>();
                sceneWrapper = new ImageWrapper(pair.Scene.Clone());
                inputs["Image"] = sceneWrapper;

                if (useTemplateInput)
                {
                    templateWrapper = new ImageWrapper(pair.Template.Clone());
                    inputs["Template"] = templateWrapper;
                }
                else if (useTemplatePath)
                {
                    var templatePath = Path.Combine(options.WorkDir, $"{caseId}_template.png");
                    Cv2.ImWrite(templatePath, pair.Template);
                    parameters["TemplatePath"] = templatePath;
                }

                var op = CreateOperator(parameters);
                var executor = new PyramidShapeMatchOperator(NullLogger<PyramidShapeMatchOperator>.Instance);
                var stopwatch = Stopwatch.StartNew();
                var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
                execution = await executor.ExecuteAsync(op, inputs);
                var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
                stopwatch.Stop();

                var metrics = EvaluateExecution(execution, pair, mode, expectIsMatch, expectOperatorFailure);
                var passed = BoolMetric(metrics, "Passed");
                ReleaseImageOutputs(execution.OutputData);

                return new CaseRunResult(
                    passed,
                    Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    Math.Max(0, allocationAfter - allocationBefore),
                    passed ? null : FormatFailure(execution, metrics),
                    metrics);
            }
            finally
            {
                sceneWrapper?.Dispose();
                templateWrapper?.Dispose();
            }
        });
    }

    private static ContractCase ValidationCase(string caseId, bool expectedValid, Action<Dictionary<string, object?>>? configure = null)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", _ =>
        {
            var parameters = DefaultParameters(MatchMode.Template);
            configure?.Invoke(parameters);
            var op = CreateOperator(parameters);
            var executor = new PyramidShapeMatchOperator(NullLogger<PyramidShapeMatchOperator>.Instance);
            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var validation = executor.ValidateParameters(op);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var passed = validation.IsValid == expectedValid;
            var error = string.Join("; ", validation.Errors);
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedValid"] = expectedValid,
                ["ActualValid"] = validation.IsValid,
                ["ValidationCorrect"] = passed,
                ["ErrorMessage"] = error,
                ["Passed"] = passed
            };

            return Task.FromResult(new CaseRunResult(
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                passed ? null : $"Expected validation={expectedValid}, got {validation.IsValid}: {error}",
                metrics));
        });
    }

    private static async Task<CaseResult> RunCaseAsync(ContractCase testCase, RunnerOptions options)
    {
        try
        {
            var run = await testCase.RunAsync(options);
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                run.Passed,
                run.RuntimeMs,
                run.MemoryAllocationBytes,
                run.ErrorMessage,
                run.Metrics);
        }
        catch (Exception ex)
        {
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                false,
                0,
                0,
                $"{ex.GetType().Name}: {ex.Message}",
                new Dictionary<string, object>());
        }
    }

    private static Dictionary<string, object?> DefaultParameters(MatchMode mode)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TemplatePath"] = string.Empty,
            ["MinScore"] = mode == MatchMode.Template ? 70.0 : 60.0,
            ["AngleRange"] = 0,
            ["AngleStep"] = 5,
            ["PyramidLevels"] = 2,
            ["MagnitudeThreshold"] = 30,
            ["WeakThreshold"] = 20.0,
            ["StrongThreshold"] = 40.0,
            ["NumFeatures"] = 120,
            ["SpreadT"] = 4,
            ["MaxMatches"] = 5,
            ["MatchMode"] = mode == MatchMode.Template ? "Template" : "ShapeDescriptor",
            ["DescriptorTypes"] = "Hu+Fourier",
            ["PreFilterArea"] = true,
            ["AreaTolerance"] = 0.3
        };
    }

    private static Operator CreateOperator(Dictionary<string, object?> parameters)
    {
        var op = new Operator(OperatorName, OperatorType.PyramidShapeMatch, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, DataTypeFor(value), value));
        }

        return op;
    }

    private static Dictionary<string, object> EvaluateExecution(
        OperatorExecutionOutput execution,
        SyntheticScene pair,
        MatchMode mode,
        bool expectIsMatch,
        bool expectOperatorFailure)
    {
        var metrics = new Dictionary<string, object>
        {
            ["ExpectedIsMatch"] = expectIsMatch,
            ["ExpectedOperatorFailure"] = expectOperatorFailure,
            ["ActualSuccess"] = execution.IsSuccess,
            ["ActualIsMatch"] = false,
            ["IsMatchCorrect"] = false,
            ["Score"] = 0.0,
            ["ScoreInRange"] = false,
            ["MatchCount"] = 0,
            ["MatchCountCorrect"] = !expectIsMatch,
            ["PositionErrorPx"] = 0.0,
            ["PositionCorrect"] = !expectIsMatch,
            ["AngleFinite"] = false,
            ["ModeCorrect"] = false,
            ["ScoreScaleCorrect"] = false,
            ["DiagnosticsPresent"] = false,
            ["OutputImagePresent"] = false,
            ["Passed"] = false
        };

        if (expectOperatorFailure)
        {
            var operatorFailurePassed = !execution.IsSuccess;
            metrics["Passed"] = operatorFailurePassed;
            return metrics;
        }

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return metrics;
        }

        var output = execution.OutputData;
        var actualIsMatch = TryGetBool(output, "IsMatch", out var isMatchValue) && isMatchValue;
        var score = TryGetDouble(output, "Score", out var scoreValue) ? scoreValue : 0.0;
        var matchCount = TryGetInt(output, "MatchCount", out var matchCountValue) ? matchCountValue : 0;
        var angle = TryGetDouble(output, "Angle", out var angleValue) ? angleValue : double.NaN;
        var actualMode = output.TryGetValue("MatchMode", out var modeObj) ? modeObj?.ToString() ?? string.Empty : string.Empty;
        var scoreScale = output.TryGetValue("ScoreScale", out var scoreScaleObj) ? scoreScaleObj?.ToString() ?? string.Empty : string.Empty;
        var diagnosticsPresent = output.ContainsKey("MatcherDiagnostics") && output.ContainsKey("MatcherConfig");

        var positionCorrect = !expectIsMatch;
        var positionError = 0.0;
        if (expectIsMatch)
        {
            positionCorrect = TryGetPosition(output.TryGetValue("Position", out var positionObj) ? positionObj : null, out var actualPosition);
            if (positionCorrect)
            {
                var allowed = mode == MatchMode.Template
                    ? new[] { pair.ExpectedTopLeft, pair.ExpectedCenter }
                    : new[] { pair.ExpectedCenter };
                positionError = allowed.Min(expected => Distance(expected, actualPosition));
                positionCorrect = positionError <= (mode == MatchMode.Template ? 18.0 : 16.0);
            }
            else
            {
                positionError = double.MaxValue;
            }
        }

        var isMatchCorrect = actualIsMatch == expectIsMatch;
        var scoreInRange = double.IsFinite(score) && score >= -1e-9 && score <= 100.0 + 1e-9;
        var matchCountCorrect = !expectIsMatch || matchCount > 0;
        var angleFinite = !expectIsMatch || double.IsFinite(angle);
        var modeCorrect = string.Equals(actualMode, mode == MatchMode.Template ? "Template" : "ShapeDescriptor", StringComparison.OrdinalIgnoreCase);
        var scoreScaleCorrect = string.Equals(scoreScale, "Percent", StringComparison.OrdinalIgnoreCase);
        var outputImagePresent = output.Values.OfType<ImageWrapper>().Any();
        var passed =
            isMatchCorrect &&
            scoreInRange &&
            matchCountCorrect &&
            positionCorrect &&
            angleFinite &&
            modeCorrect &&
            scoreScaleCorrect &&
            diagnosticsPresent &&
            outputImagePresent;

        metrics["ActualIsMatch"] = actualIsMatch;
        metrics["IsMatchCorrect"] = isMatchCorrect;
        metrics["Score"] = Round(score);
        metrics["ScoreInRange"] = scoreInRange;
        metrics["MatchCount"] = matchCount;
        metrics["MatchCountCorrect"] = matchCountCorrect;
        metrics["PositionErrorPx"] = Round(positionError);
        metrics["PositionCorrect"] = positionCorrect;
        metrics["Angle"] = Round(angle);
        metrics["AngleFinite"] = angleFinite;
        metrics["ModeCorrect"] = modeCorrect;
        metrics["ScoreScaleCorrect"] = scoreScaleCorrect;
        metrics["DiagnosticsPresent"] = diagnosticsPresent;
        metrics["OutputImagePresent"] = outputImagePresent;
        metrics["Passed"] = passed;
        return metrics;
    }

    private static string DataTypeFor(object? value) => value switch
    {
        bool => "bool",
        int => "int",
        double or float or decimal => "double",
        _ => "string"
    };

    private static bool BoolMetric(IReadOnlyDictionary<string, object> metrics, string key) =>
        metrics.TryGetValue(key, out var value) && value is bool b && b;

    private static bool TryGetBool(IReadOnlyDictionary<string, object> output, string key, out bool value)
    {
        value = false;
        return output.TryGetValue(key, out var obj) && (obj is bool b ? value = b : bool.TryParse(obj?.ToString(), out value));
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object> output, string key, out int value)
    {
        value = 0;
        if (!output.TryGetValue(key, out var obj))
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, object> output, string key, out double value)
    {
        value = 0;
        if (!output.TryGetValue(key, out var obj))
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPosition(object? value, out PointValue position)
    {
        position = default;
        if (value is Position p)
        {
            position = new PointValue(p.X, p.Y);
            return true;
        }

        if (value is Point point)
        {
            position = new PointValue(point.X, point.Y);
            return true;
        }

        return false;
    }

    private static double Distance(PointValue expected, PointValue actual)
    {
        var dx = expected.X - actual.X;
        var dy = expected.Y - actual.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Round(double value) => double.IsFinite(value) ? Math.Round(value, 6) : value;

    private static void ReleaseImageOutputs(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var image in outputData.Values.OfType<ImageWrapper>().Distinct(ReferenceEqualityComparer<ImageWrapper>.Instance))
        {
            image.Release();
        }
    }

    private static string FormatFailure(OperatorExecutionOutput? execution, IReadOnlyDictionary<string, object> metrics)
    {
        if (execution is not null && !execution.IsSuccess)
        {
            return execution.ErrorMessage ?? "Execution failed.";
        }

        var keys = new[]
        {
            "IsMatchCorrect",
            "ScoreInRange",
            "MatchCountCorrect",
            "PositionErrorPx",
            "PositionCorrect",
            "AngleFinite",
            "ModeCorrect",
            "ScoreScaleCorrect",
            "DiagnosticsPresent",
            "OutputImagePresent"
        };
        return string.Join(", ", keys.Where(metrics.ContainsKey).Select(key => $"{key}={metrics[key]}"));
    }
}

internal sealed class SyntheticScene : IDisposable
{
    private SyntheticScene(Mat scene, Mat template, PointValue topLeft, PointValue center)
    {
        Scene = scene;
        Template = template;
        ExpectedTopLeft = topLeft;
        ExpectedCenter = center;
    }

    public Mat Scene { get; }
    public Mat Template { get; }
    public PointValue ExpectedTopLeft { get; }
    public PointValue ExpectedCenter { get; }

    public static SyntheticScene Create(SceneSpec spec)
    {
        var template = CreateTemplate(spec.BlankTemplate);
        var scene = spec.BlankScene
            ? new Mat(260, 360, MatType.CV_8UC3, Scalar.Black)
            : new Mat(260, 360, MatType.CV_8UC3, new Scalar(12, 12, 12));

        if (!spec.BlankScene)
        {
            var scaled = template;
            if (Math.Abs(spec.Scale - 1.0) > 1e-6)
            {
                scaled = new Mat();
                Cv2.Resize(template, scaled, new Size(), spec.Scale, spec.Scale, InterpolationFlags.Linear);
            }

            using var mask = new Mat();
            Cv2.CvtColor(scaled, mask, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(mask, mask, 1, 255, ThresholdTypes.Binary);
            var roi = new Rect(spec.X, spec.Y, scaled.Width, scaled.Height);
            using var sceneRoi = new Mat(scene, roi);
            scaled.CopyTo(sceneRoi, mask);

            if (!ReferenceEquals(scaled, template))
            {
                scaled.Dispose();
            }
        }

        if (spec.Grayscale)
        {
            using var grayScene = ToGray(scene);
            using var grayTemplate = ToGray(template);
            scene.Dispose();
            template.Dispose();
            scene = grayScene.Clone();
            template = grayTemplate.Clone();
        }

        var center = new PointValue(spec.X + (template.Width * spec.Scale / 2.0), spec.Y + (template.Height * spec.Scale / 2.0));
        return new SyntheticScene(scene, template, new PointValue(spec.X, spec.Y), center);
    }

    public void Dispose()
    {
        Scene.Dispose();
        Template.Dispose();
    }

    private static Mat CreateTemplate(bool blank)
    {
        var mat = new Mat(72, 96, MatType.CV_8UC3, Scalar.Black);
        if (blank)
        {
            return mat;
        }

        var polygon = new[]
        {
            new Point(8, 8),
            new Point(74, 8),
            new Point(74, 28),
            new Point(50, 28),
            new Point(50, 60),
            new Point(22, 60),
            new Point(22, 28),
            new Point(8, 28)
        };
        Cv2.FillPoly(mat, new[] { polygon }, new Scalar(245, 245, 245));
        Cv2.Line(mat, new Point(26, 14), new Point(66, 54), new Scalar(80, 80, 80), 3);
        Cv2.Circle(mat, new Point(34, 42), 7, new Scalar(30, 30, 30), -1);
        Cv2.Rectangle(mat, new Rect(58, 12, 10, 10), new Scalar(20, 20, 20), -1);
        return mat;
    }

    private static Mat ToGray(Mat src)
    {
        if (src.Channels() == 1)
        {
            return src.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }
}

internal readonly record struct SceneSpec(int X, int Y, double Scale, bool BlankScene, bool BlankTemplate, bool Grayscale)
{
    public static SceneSpec Exact(int x, int y, bool grayscale = false) => new(x, y, 1.0, false, false, grayscale);
    public static SceneSpec ScaledObject(int x, int y, double scale) => new(x, y, scale, false, false, false);
    public static SceneSpec CreateBlankScene() => new(80, 80, 1.0, true, false, false);
    public static SceneSpec CreateBlankTemplate() => new(80, 80, 1.0, false, true, false);
}

internal enum MatchMode
{
    Template,
    ShapeDescriptor
}

internal readonly record struct PointValue(double X, double Y);

internal sealed record ContractCase(
    string CaseId,
    string Operator,
    string Scenario,
    Func<RunnerOptions, Task<CaseRunResult>> RunAsync);

internal sealed record CaseRunResult(
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    Dictionary<string, object> Metrics);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

internal sealed record BaselineSummary(DateTimeOffset GeneratedAtUtc, int CaseCount, int Passed, int Failed, double RuntimeMs);

internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double RuntimeMsAvg, long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object> Metrics);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# PyramidShapeMatch Contract Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |",
            "| --- | ---: | ---: | ---: | ---: | ---: |"
        };

        foreach (var op in result.Operators)
        {
            lines.Add($"| {op.Operator} | {op.CaseCount} | {op.Passed} | {op.Failed} | {op.RuntimeMsAvg:0.###} | {op.MemoryAllocationBytesAvg} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        });

        foreach (var scenario in result.Scenarios)
        {
            lines.Add($"| {scenario.Scenario} | {scenario.CaseCount} | {scenario.Passed} | {scenario.Failed} | {scenario.RuntimeMsAvg:0.###} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Passed | Runtime ms | IsMatch | Score | Count | Pos Error | Failure |",
            "| --- | --- | --- | ---: | --- | ---: | ---: | ---: | --- |"
        });

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("ActualIsMatch", out var isMatch);
            item.Metrics.TryGetValue("Score", out var score);
            item.Metrics.TryGetValue("MatchCount", out var count);
            item.Metrics.TryGetValue("PositionErrorPx", out var error);
            lines.Add($"| {item.CaseId} | {item.Scenario} | {(item.Passed ? "Yes" : "No")} | {item.RuntimeMs:0.###} | {isMatch ?? "-"} | {score ?? "-"} | {count ?? "-"} | {error ?? "-"} | {item.ErrorMessage ?? "-"} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Notes",
            string.Empty,
            "- This baseline uses deterministic synthetic asymmetric shapes.",
            "- Template mode accepts either the current LINEMOD candidate point or the UI-drawn center as position-compatible evidence.",
            "- ShapeDescriptor mode is evaluated against contour-center localization.",
            "- The run locks output contract and validation behavior; it does not claim HPatches-style public benchmark coverage."
        });

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, string WorkDir, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/PyramidShapeMatch_contract_baseline.json";
        string? report = "quality/evals/reports/PyramidShapeMatch_contract_baseline.md";
        var workDir = ".tmp/pyramid-shape-match-contract";
        var showHelp = false;
        string? parseError = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--output":
                    output = ReadValue(args, ref index, arg, ref parseError) ?? output;
                    break;
                case "--report":
                    report = ReadValue(args, ref index, arg, ref parseError);
                    break;
                case "--work-dir":
                    workDir = ReadValue(args, ref index, arg, ref parseError) ?? workDir;
                    break;
                case "--no-report":
                    report = null;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    parseError = $"Unknown argument: {arg}";
                    break;
            }
        }

        return new RunnerOptions(output, report, workDir, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            PyramidShapeMatch contract runner

            Options:
              --output <path>     JSON baseline output path.
              --report <path>     Markdown report output path.
              --no-report         Skip markdown report generation.
              --work-dir <dir>    Scratch directory for template-path cases.
            """);
    }

    private static string? ReadValue(string[] args, ref int index, string name, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"{name} requires a value.";
            return null;
        }

        index++;
        return args[index];
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
