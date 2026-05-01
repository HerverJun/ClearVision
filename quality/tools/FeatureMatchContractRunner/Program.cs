using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
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
    $"FeatureMatch contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        Directory.CreateDirectory(options.WorkDir);
        var cases = new List<ContractCase>();
        AddOperatorCases(cases, FeatureOperatorKind.Akaze);
        AddOperatorCases(cases, FeatureOperatorKind.Orb);

        var results = new List<CaseResult>();
        foreach (var testCase in cases)
        {
            results.Add(await RunCaseAsync(testCase, options));
        }

        var byOperator = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
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

    private static void AddOperatorCases(List<ContractCase> cases, FeatureOperatorKind kind)
    {
        var op = OperatorName(kind);
        var prefix = op;

        cases.Add(ExecutionCase(
            $"{prefix}_translation_center_input",
            op,
            "Positive localization",
            kind,
            SceneSpec.Translation(74, 92),
            ExpectedPoint.Center,
            useTemplateInput: true,
            minInliers: 6,
            minScore: 0.25));
        cases.Add(ExecutionCase(
            $"{prefix}_translation_topleft_origin",
            op,
            "Origin contract",
            kind,
            SceneSpec.Translation(88, 70),
            ExpectedPoint.TopLeft,
            useTemplateInput: true,
            configure: parameters => parameters["OriginMode"] = "TopLeft",
            minInliers: 6,
            minScore: 0.25));
        cases.Add(ExecutionCase(
            $"{prefix}_translation_custom_origin",
            op,
            "Origin contract",
            kind,
            SceneSpec.Translation(52, 116),
            ExpectedPoint.Custom,
            useTemplateInput: true,
            configure: parameters =>
            {
                parameters["OriginMode"] = "Custom";
                parameters["OriginX"] = 35.0;
                parameters["OriginY"] = 46.0;
            },
            minInliers: 6,
            minScore: 0.25));
        cases.Add(ExecutionCase(
            $"{prefix}_template_path_center",
            op,
            "Template source",
            kind,
            SceneSpec.Translation(116, 84),
            ExpectedPoint.Center,
            useTemplatePath: true,
            minInliers: 6,
            minScore: 0.25));
        cases.Add(ExecutionCase(
            $"{prefix}_template_path_cache_repeat",
            op,
            "Template source",
            kind,
            SceneSpec.Translation(116, 84),
            ExpectedPoint.Center,
            useTemplatePath: true,
            sharedTemplatePathKey: $"{op}_cache_template",
            minInliers: 6,
            minScore: 0.25));
        cases.Add(ExecutionCase(
            $"{prefix}_symmetry_disabled_translation",
            op,
            "Matcher options",
            kind,
            SceneSpec.Translation(130, 118),
            ExpectedPoint.Center,
            useTemplateInput: true,
            configure: parameters => parameters["EnableSymmetryTest"] = false,
            minInliers: 6,
            minScore: 0.25));
        cases.Add(ExecutionCase(
            $"{prefix}_min_match_count_four",
            op,
            "Matcher options",
            kind,
            SceneSpec.Translation(64, 72),
            ExpectedPoint.Center,
            useTemplateInput: true,
            configure: parameters => parameters["MinMatchCount"] = 4,
            minInliers: 4,
            minScore: 0.2));
        cases.Add(ExecutionCase(
            $"{prefix}_max_features_low_boundary",
            op,
            "Matcher options",
            kind,
            SceneSpec.Translation(96, 122),
            ExpectedPoint.Center,
            useTemplateInput: true,
            configure: parameters => parameters["MaxFeatures"] = 100,
            minInliers: 4,
            minScore: 0.2));
        cases.Add(ExecutionCase(
            $"{prefix}_scaled_up",
            op,
            "Scale and rotation",
            kind,
            SceneSpec.Transform(70, 78, scale: 1.12, angleDeg: 0),
            ExpectedPoint.Center,
            useTemplateInput: true,
            minInliers: 5,
            minScore: 0.2,
            positionTolerancePx: 14));
        cases.Add(ExecutionCase(
            $"{prefix}_scaled_down",
            op,
            "Scale and rotation",
            kind,
            SceneSpec.Transform(112, 96, scale: 0.88, angleDeg: 0),
            ExpectedPoint.Center,
            useTemplateInput: true,
            minInliers: 5,
            minScore: 0.2,
            positionTolerancePx: 14));
        cases.Add(ExecutionCase(
            $"{prefix}_rotated_small_angle",
            op,
            "Scale and rotation",
            kind,
            SceneSpec.Transform(92, 92, scale: 1.0, angleDeg: 7.0),
            ExpectedPoint.Center,
            useTemplateInput: true,
            minInliers: 5,
            minScore: 0.2,
            positionTolerancePx: 16));
        cases.Add(ExecutionCase(
            $"{prefix}_grayscale_inputs",
            op,
            "Input formats",
            kind,
            SceneSpec.Translation(78, 128, grayscale: true),
            ExpectedPoint.Center,
            useTemplateInput: true,
            minInliers: 5,
            minScore: 0.2));
        cases.Add(ExecutionCase(
            $"{prefix}_color_scene_grayscale_template",
            op,
            "Input formats",
            kind,
            SceneSpec.Translation(128, 62, grayscaleTemplate: true),
            ExpectedPoint.Center,
            useTemplateInput: true,
            minInliers: 5,
            minScore: 0.2));
        cases.Add(ExecutionCase(
            $"{prefix}_blank_scene_no_features",
            op,
            "Failure contract",
            kind,
            SceneSpec.CreateBlankScene(),
            ExpectedPoint.Center,
            useTemplateInput: true,
            expectIsMatch: false));
        cases.Add(ExecutionCase(
            $"{prefix}_blank_template_no_features",
            op,
            "Failure contract",
            kind,
            SceneSpec.CreateBlankTemplate(),
            ExpectedPoint.Center,
            useTemplateInput: true,
            expectIsMatch: false));
        cases.Add(ExecutionCase(
            $"{prefix}_missing_template_source",
            op,
            "Failure contract",
            kind,
            SceneSpec.Translation(80, 80),
            ExpectedPoint.Center,
            useTemplateInput: false,
            useTemplatePath: false,
            expectIsMatch: false));
        cases.Add(ExecutionCase(
            $"{prefix}_operator_failure_without_image",
            op,
            "Failure contract",
            kind,
            SceneSpec.Translation(80, 80),
            ExpectedPoint.Center,
            useTemplateInput: false,
            expectOperatorFailure: true,
            omitImage: true));

        cases.Add(ValidationCase(
            $"{prefix}_validate_defaults",
            op,
            "Validation contract",
            kind,
            expectedValid: true));
        cases.Add(ValidationCase(
            $"{prefix}_validate_min_match_low_invalid",
            op,
            "Validation contract",
            kind,
            expectedValid: false,
            configure: parameters => parameters["MinMatchCount"] = 2));
        cases.Add(ValidationCase(
            $"{prefix}_validate_min_match_high_invalid",
            op,
            "Validation contract",
            kind,
            expectedValid: false,
            configure: parameters => parameters["MinMatchCount"] = 101));

        if (kind == FeatureOperatorKind.Akaze)
        {
            cases.Add(ValidationCase(
                $"{prefix}_validate_threshold_low_invalid",
                op,
                "Validation contract",
                kind,
                expectedValid: false,
                configure: parameters => parameters["Threshold"] = 0.00001));
            cases.Add(ValidationCase(
                $"{prefix}_validate_threshold_high_invalid",
                op,
                "Validation contract",
                kind,
                expectedValid: false,
                configure: parameters => parameters["Threshold"] = 0.2));
        }
        else
        {
            cases.Add(ValidationCase(
                $"{prefix}_validate_scale_factor_low_invalid",
                op,
                "Validation contract",
                kind,
                expectedValid: false,
                configure: parameters => parameters["ScaleFactor"] = 0.95));
            cases.Add(ValidationCase(
                $"{prefix}_validate_max_features_high_invalid",
                op,
                "Validation contract",
                kind,
                expectedValid: false,
                configure: parameters => parameters["MaxFeatures"] = 2001));
        }
    }

    private static ContractCase ExecutionCase(
        string caseId,
        string operatorName,
        string scenario,
        FeatureOperatorKind kind,
        SceneSpec sceneSpec,
        ExpectedPoint expectedPoint,
        bool useTemplateInput = false,
        bool useTemplatePath = false,
        bool expectIsMatch = true,
        bool expectOperatorFailure = false,
        bool omitImage = false,
        int minInliers = 0,
        double minScore = 0,
        double positionTolerancePx = 10,
        string? expectedFailureContains = null,
        string? sharedTemplatePathKey = null,
        Action<Dictionary<string, object?>>? configure = null)
    {
        return new ContractCase(caseId, operatorName, scenario, async options =>
        {
            using var pair = SyntheticScene.Create(sceneSpec);
            var parameters = DefaultParameters(kind);
            configure?.Invoke(parameters);
            var inputs = new Dictionary<string, object>();
            ImageWrapper? sceneWrapper = null;
            ImageWrapper? templateWrapper = null;
            OperatorExecutionOutput? execution = null;

            try
            {
                if (!omitImage)
                {
                    sceneWrapper = new ImageWrapper(pair.Scene.Clone());
                    inputs["Image"] = sceneWrapper;
                }

                if (useTemplateInput)
                {
                    templateWrapper = new ImageWrapper(pair.Template.Clone());
                    inputs["Template"] = templateWrapper;
                }
                else if (useTemplatePath)
                {
                    var key = sharedTemplatePathKey ?? caseId;
                    var templatePath = Path.Combine(options.WorkDir, $"{key}.png");
                    Cv2.ImWrite(templatePath, pair.Template);
                    parameters["TemplatePath"] = templatePath;
                }

                var op = CreateOperator(operatorName, kind, parameters);
                var executor = CreateExecutor(kind);
                var stopwatch = Stopwatch.StartNew();
                var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
                execution = await executor.ExecuteAsync(op, inputs);
                var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
                stopwatch.Stop();

                var metrics = EvaluateExecution(
                    execution,
                    pair,
                    expectedPoint,
                    expectIsMatch,
                    expectOperatorFailure,
                    minInliers,
                    minScore,
                    positionTolerancePx,
                    expectedFailureContains);
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

    private static ContractCase ValidationCase(
        string caseId,
        string operatorName,
        string scenario,
        FeatureOperatorKind kind,
        bool expectedValid,
        Action<Dictionary<string, object?>>? configure = null)
    {
        return new ContractCase(caseId, operatorName, scenario, _ =>
        {
            var parameters = DefaultParameters(kind);
            configure?.Invoke(parameters);
            var op = CreateOperator(operatorName, kind, parameters);
            var executor = CreateExecutor(kind);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var validation = executor.ValidateParameters(op);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var passed = validation.IsValid == expectedValid;
            var validationError = string.Join("; ", validation.Errors);
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedValid"] = expectedValid,
                ["ActualValid"] = validation.IsValid,
                ["ValidationCorrect"] = passed,
                ["ErrorMessage"] = validationError,
                ["Passed"] = passed
            };

            return Task.FromResult(new CaseRunResult(
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                passed ? null : $"Expected validation={expectedValid}, got {validation.IsValid}: {validationError}",
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

    private static Dictionary<string, object?> DefaultParameters(FeatureOperatorKind kind)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TemplatePath"] = string.Empty,
            ["MinMatchCount"] = 6,
            ["EnableSymmetryTest"] = true,
            ["MaxFeatures"] = 900,
            ["OriginMode"] = "Center",
            ["OriginX"] = 0.0,
            ["OriginY"] = 0.0
        };

        if (kind == FeatureOperatorKind.Akaze)
        {
            parameters["Threshold"] = 0.001;
        }
        else
        {
            parameters["ScaleFactor"] = 1.2;
            parameters["NLevels"] = 8;
            parameters["EdgeThreshold"] = 15;
        }

        return parameters;
    }

    private static Operator CreateOperator(string operatorName, FeatureOperatorKind kind, Dictionary<string, object?> parameters)
    {
        var op = new Operator(operatorName, kind == FeatureOperatorKind.Akaze ? OperatorType.AkazeFeatureMatch : OperatorType.OrbFeatureMatch, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, DataTypeFor(value), value));
        }

        return op;
    }

    private static OperatorBase CreateExecutor(FeatureOperatorKind kind)
    {
        return kind == FeatureOperatorKind.Akaze
            ? new AkazeFeatureMatchOperator(NullLogger<AkazeFeatureMatchOperator>.Instance)
            : new OrbFeatureMatchOperator(NullLogger<OrbFeatureMatchOperator>.Instance);
    }

    private static Dictionary<string, object> EvaluateExecution(
        OperatorExecutionOutput execution,
        SyntheticScene pair,
        ExpectedPoint expectedPoint,
        bool expectIsMatch,
        bool expectOperatorFailure,
        int minInliers,
        double minScore,
        double positionTolerancePx,
        string? expectedFailureContains)
    {
        var metrics = new Dictionary<string, object>
        {
            ["ExpectedIsMatch"] = expectIsMatch,
            ["ExpectedOperatorFailure"] = expectOperatorFailure,
            ["ActualSuccess"] = execution.IsSuccess,
            ["ActualIsMatch"] = false,
            ["IsMatchCorrect"] = false,
            ["Inliers"] = 0,
            ["TotalMatches"] = 0,
            ["Score"] = 0.0,
            ["ScoreInRange"] = false,
            ["MinScoreSatisfied"] = !expectIsMatch,
            ["MinInliersSatisfied"] = !expectIsMatch,
            ["PositionErrorPx"] = 0.0,
            ["PositionCorrect"] = !expectIsMatch,
            ["ScoreDefinitionCorrect"] = false,
            ["FailureReasonCorrect"] = expectIsMatch,
            ["OutputImagePresent"] = false,
            ["Passed"] = false
        };

        if (expectOperatorFailure)
        {
            var operatorFailurePassed = !execution.IsSuccess;
            metrics["Passed"] = operatorFailurePassed;
            metrics["FailureReasonCorrect"] = operatorFailurePassed;
            return metrics;
        }

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return metrics;
        }

        var output = execution.OutputData;
        var actualIsMatch = TryGetBool(output, "IsMatch", out var isMatchValue) && isMatchValue;
        var inliers = TryGetInt(output, "Inliers", out var inliersValue) ? inliersValue : 0;
        var totalMatches = TryGetInt(output, "TotalMatches", out var totalValue) ? totalValue : 0;
        var score = TryGetDouble(output, "Score", out var scoreValue) ? scoreValue : 0.0;
        var scoreDefinition = output.TryGetValue("ScoreDefinition", out var scoreDefinitionObj)
            ? scoreDefinitionObj?.ToString() ?? string.Empty
            : string.Empty;
        var failureReason = output.TryGetValue("FailureReason", out var failureObj)
            ? failureObj?.ToString() ?? string.Empty
            : output.TryGetValue("Message", out var messageObj)
                ? messageObj?.ToString() ?? string.Empty
                : string.Empty;

        var positionError = 0.0;
        var positionCorrect = !expectIsMatch;
        if (expectIsMatch)
        {
            var expected = pair.ExpectedPosition(expectedPoint);
            positionCorrect = TryGetPosition(output.TryGetValue("Position", out var positionObj) ? positionObj : null, out var actualPosition);
            if (positionCorrect)
            {
                positionError = Distance(expected, actualPosition);
                positionCorrect = positionError <= positionTolerancePx;
            }
            else
            {
                positionError = double.MaxValue;
            }
        }

        var failureReasonCorrect = expectIsMatch ||
            (!actualIsMatch &&
                (string.IsNullOrWhiteSpace(expectedFailureContains) ||
                 failureReason.Contains(expectedFailureContains, StringComparison.OrdinalIgnoreCase)));
        var scoreInRange = double.IsFinite(score) && score >= -1e-9 && score <= 1.0 + 1e-9;
        var minScoreSatisfied = !expectIsMatch || score >= minScore;
        var minInliersSatisfied = !expectIsMatch || inliers >= minInliers;
        var outputImagePresent = output.Values.OfType<ImageWrapper>().Any();
        var scoreDefinitionCorrect = string.Equals(scoreDefinition, "HomographyVerificationScore", StringComparison.OrdinalIgnoreCase);
        var isMatchCorrect = actualIsMatch == expectIsMatch;
        var passed =
            isMatchCorrect &&
            scoreInRange &&
            minScoreSatisfied &&
            minInliersSatisfied &&
            positionCorrect &&
            scoreDefinitionCorrect &&
            failureReasonCorrect &&
            outputImagePresent;

        metrics["ActualIsMatch"] = actualIsMatch;
        metrics["IsMatchCorrect"] = isMatchCorrect;
        metrics["Inliers"] = inliers;
        metrics["TotalMatches"] = totalMatches;
        metrics["Score"] = Round(score);
        metrics["ScoreInRange"] = scoreInRange;
        metrics["MinScoreSatisfied"] = minScoreSatisfied;
        metrics["MinInliersSatisfied"] = minInliersSatisfied;
        metrics["PositionErrorPx"] = Round(positionError);
        metrics["PositionCorrect"] = positionCorrect;
        metrics["ScoreDefinitionCorrect"] = scoreDefinitionCorrect;
        metrics["FailureReason"] = failureReason;
        metrics["FailureReasonCorrect"] = failureReasonCorrect;
        metrics["OutputImagePresent"] = outputImagePresent;
        metrics["Passed"] = passed;
        return metrics;
    }

    private static string OperatorName(FeatureOperatorKind kind) =>
        kind == FeatureOperatorKind.Akaze ? "AkazeFeatureMatch" : "OrbFeatureMatch";

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
        if (!output.TryGetValue(key, out var obj))
        {
            return false;
        }

        if (obj is bool b)
        {
            value = b;
            return true;
        }

        return bool.TryParse(obj?.ToString(), out value);
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

        if (value is IDictionary<string, object> dict &&
            TryGetDictionaryDouble(dict, "X", out var x) &&
            TryGetDictionaryDouble(dict, "Y", out var y))
        {
            position = new PointValue(x, y);
            return true;
        }

        return false;
    }

    private static bool TryGetDictionaryDouble(IDictionary<string, object> dict, string key, out double value)
    {
        value = 0;
        if (!dict.TryGetValue(key, out var obj))
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
            "MinScoreSatisfied",
            "MinInliersSatisfied",
            "PositionErrorPx",
            "PositionCorrect",
            "ScoreDefinitionCorrect",
            "FailureReasonCorrect",
            "OutputImagePresent"
        };
        return string.Join(", ", keys.Where(metrics.ContainsKey).Select(key => $"{key}={FormatValue(metrics[key])}"));
    }

    private static string FormatValue(object value) => value switch
    {
        double d => d.ToString("0.###"),
        float f => f.ToString("0.###"),
        _ => value.ToString() ?? string.Empty
    };
}

internal sealed class SyntheticScene : IDisposable
{
    private SyntheticScene(Mat scene, Mat template, SceneSpec spec, Point2f[] projectedCorners)
    {
        Scene = scene;
        Template = template;
        Spec = spec;
        ProjectedCorners = projectedCorners;
    }

    public Mat Scene { get; }
    public Mat Template { get; }
    public SceneSpec Spec { get; }
    public Point2f[] ProjectedCorners { get; }

    public static SyntheticScene Create(SceneSpec spec)
    {
        var template = CreateTemplate(spec.BlankTemplate, spec.TemplateVariant);
        var scene = spec.BlankScene
            ? new Mat(320, 440, MatType.CV_8UC3, new Scalar(224, 224, 224))
            : CreateBackground(440, 320, spec.BackgroundVariant);

        var corners = ProjectCorners(template.Width, template.Height, spec.X, spec.Y, spec.Scale, spec.AngleDeg);
        if (!spec.BlankScene)
        {
            PasteTemplate(scene, template, corners);
        }

        if (spec.GrayscaleTemplate)
        {
            using var grayTemplate = ToGray(template);
            template.Dispose();
            template = grayTemplate.Clone();
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

        return new SyntheticScene(scene, template, spec, corners);
    }

    public PointValue ExpectedPosition(ExpectedPoint point)
    {
        var origin = point switch
        {
            ExpectedPoint.TopLeft => new Point2f(0, 0),
            ExpectedPoint.Custom => new Point2f(35, 46),
            _ => new Point2f(Template.Width / 2f, Template.Height / 2f)
        };
        var projected = ProjectPoint(origin, Template.Width, Template.Height, Spec.X, Spec.Y, Spec.Scale, Spec.AngleDeg);
        return new PointValue(projected.X, projected.Y);
    }

    public void Dispose()
    {
        Scene.Dispose();
        Template.Dispose();
    }

    private static Mat CreateTemplate(bool blank, int variant)
    {
        var template = new Mat(120, 160, MatType.CV_8UC3, new Scalar(248, 248, 248));
        if (blank)
        {
            return template;
        }

        Cv2.Rectangle(template, new Rect(3, 3, 154, 114), new Scalar(20, 20, 20), 3);
        Cv2.Line(template, new Point(9, 104), new Point(150, 18), new Scalar(30, 30, 30), 2);
        Cv2.Line(template, new Point(15, 22), new Point(144, 96), new Scalar(70, 70, 70), 2);
        Cv2.Circle(template, new Point(38, 34), 14, new Scalar(0, 0, 180), -1);
        Cv2.Circle(template, new Point(115, 78), 18, new Scalar(0, 150, 0), 3);
        Cv2.Rectangle(template, new Rect(65, 18, 28, 24), new Scalar(190, 20, 20), -1);
        Cv2.PutText(template, variant % 2 == 0 ? "CV42" : "FX17", new Point(25, 76), HersheyFonts.HersheySimplex, 0.85, new Scalar(0, 0, 0), 2);

        var random = new Random(3100 + variant);
        for (var i = 0; i < 42; i++)
        {
            var center = new Point(random.Next(8, template.Width - 8), random.Next(8, template.Height - 8));
            var color = new Scalar(random.Next(30, 230), random.Next(30, 230), random.Next(30, 230));
            Cv2.Circle(template, center, random.Next(2, 5), color, -1);
        }

        for (var i = 0; i < 12; i++)
        {
            var p1 = new Point(random.Next(5, template.Width - 5), random.Next(5, template.Height - 5));
            var p2 = new Point(random.Next(5, template.Width - 5), random.Next(5, template.Height - 5));
            Cv2.Line(template, p1, p2, new Scalar(random.Next(20, 210), random.Next(20, 210), random.Next(20, 210)), 1);
        }

        return template;
    }

    private static Mat CreateBackground(int width, int height, int variant)
    {
        var scene = new Mat(height, width, MatType.CV_8UC3, new Scalar(232, 232, 232));
        var random = new Random(9100 + variant);
        for (var i = 0; i < 110; i++)
        {
            var p1 = new Point(random.Next(0, width), random.Next(0, height));
            var p2 = new Point(random.Next(0, width), random.Next(0, height));
            var shade = random.Next(205, 245);
            Cv2.Line(scene, p1, p2, new Scalar(shade, shade, shade), 1);
        }

        return scene;
    }

    private static void PasteTemplate(Mat scene, Mat template, Point2f[] destinationCorners)
    {
        var sourceCorners = new[]
        {
            new Point2f(0, 0),
            new Point2f(template.Width, 0),
            new Point2f(template.Width, template.Height),
            new Point2f(0, template.Height)
        };

        using var homography = Cv2.GetPerspectiveTransform(sourceCorners, destinationCorners);
        using var warped = new Mat(scene.Size(), scene.Type(), Scalar.Black);
        using var maskSource = new Mat(template.Size(), MatType.CV_8UC1, Scalar.White);
        using var mask = new Mat(scene.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.WarpPerspective(template, warped, homography, scene.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        Cv2.WarpPerspective(maskSource, mask, homography, scene.Size(), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
        warped.CopyTo(scene, mask);
    }

    private static Point2f[] ProjectCorners(int width, int height, double x, double y, double scale, double angleDeg)
    {
        return new[]
        {
            ProjectPoint(new Point2f(0, 0), width, height, x, y, scale, angleDeg),
            ProjectPoint(new Point2f(width, 0), width, height, x, y, scale, angleDeg),
            ProjectPoint(new Point2f(width, height), width, height, x, y, scale, angleDeg),
            ProjectPoint(new Point2f(0, height), width, height, x, y, scale, angleDeg)
        };
    }

    private static Point2f ProjectPoint(Point2f point, int width, int height, double x, double y, double scale, double angleDeg)
    {
        var radians = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var cx = width / 2.0;
        var cy = height / 2.0;
        var targetCenterX = x + (width * scale / 2.0);
        var targetCenterY = y + (height * scale / 2.0);
        var dx = (point.X - cx) * scale;
        var dy = (point.Y - cy) * scale;
        return new Point2f(
            (float)(targetCenterX + (dx * cos) - (dy * sin)),
            (float)(targetCenterY + (dx * sin) + (dy * cos)));
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

internal readonly record struct SceneSpec(
    double X,
    double Y,
    double Scale,
    double AngleDeg,
    bool BlankScene,
    bool BlankTemplate,
    bool Grayscale,
    bool GrayscaleTemplate,
    int TemplateVariant,
    int BackgroundVariant)
{
    public static SceneSpec Translation(double x, double y, bool grayscale = false, bool grayscaleTemplate = false) =>
        new(x, y, 1.0, 0, false, false, grayscale, grayscaleTemplate, 0, 0);

    public static SceneSpec Transform(double x, double y, double scale, double angleDeg) =>
        new(x, y, scale, angleDeg, false, false, false, false, 0, 1);

    public static SceneSpec CreateBlankScene() =>
        new(80, 80, 1.0, 0, true, false, false, false, 0, 0);

    public static SceneSpec CreateBlankTemplate() =>
        new(80, 80, 1.0, 0, false, true, false, false, 0, 0);
}

internal enum FeatureOperatorKind
{
    Akaze,
    Orb
}

internal enum ExpectedPoint
{
    Center,
    TopLeft,
    Custom
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

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs);

internal sealed record OperatorSummary(
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
    IReadOnlyDictionary<string, object> Metrics);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# Feature Match Contract Baseline",
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
            "| Case | Operator | Scenario | Passed | Runtime ms | IsMatch | Inliers | Score | Position Error | Failure |",
            "| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | --- |"
        });

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("ActualIsMatch", out var isMatch);
            item.Metrics.TryGetValue("Inliers", out var inliers);
            item.Metrics.TryGetValue("Score", out var score);
            item.Metrics.TryGetValue("PositionErrorPx", out var posError);
            lines.Add(
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {(item.Passed ? "Yes" : "No")} | {item.RuntimeMs:0.###} | " +
                $"{isMatch ?? "-"} | {inliers ?? "-"} | {FormatValue(score)} | {FormatValue(posError)} | {item.ErrorMessage ?? "-"} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Notes",
            string.Empty,
            "- This baseline uses deterministic synthetic textured templates and transformed scenes.",
            "- It validates AKAZE and ORB execution contracts: template input/path, origin modes, matcher options, score ranges, homography-gated positions, and failure/validation behavior.",
            "- It is contract evidence, not a public-image benchmark."
        });

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "-",
            double d => double.IsFinite(d) ? d.ToString("0.###") : d.ToString(),
            float f => float.IsFinite(f) ? f.ToString("0.###") : f.ToString(),
            _ => value.ToString() ?? "-"
        };
    }
}

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    string WorkDir,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/FeatureMatch_contract_baseline.json";
        string? report = "quality/evals/reports/FeatureMatch_contract_baseline.md";
        var workDir = ".tmp/feature-match-contract";
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
            FeatureMatch contract runner

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
