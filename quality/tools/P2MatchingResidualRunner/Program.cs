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

var result = await P2MatchingResidualRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"P2 matching residual baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class P2MatchingResidualRunner
{
    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<RunnerCase>();
        AddShapeMatchingCases(cases);
        AddPlanarMatchingCases(cases);
        AddLocalDeformableMatchingCases(cases);

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

    private static void AddShapeMatchingCases(List<RunnerCase> cases)
    {
        var sut = new ShapeMatchingOperator(NullLogger<ShapeMatchingOperator>.Instance);

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "ShapeMatching", $"direct_match_{index:00}", "Direct pose oracle", async () =>
            {
                using var template = CreateShapeTemplate();
                using var scene = new Mat(220, 220, MatType.CV_8UC3, Scalar.Black);
                CopyTemplate(scene, template.MatReadOnly, 40 + index, 56 + index);

                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.ShapeMatching,
                        ("MinScore", 0.6),
                        ("MaxMatches", 1),
                        ("AngleStart", 0.0),
                        ("AngleExtent", 0.0),
                        ("AngleStep", 1.0),
                        ("ScaleMin", 1.0),
                        ("ScaleMax", 1.0),
                        ("ScaleStep", 0.1),
                        ("NumLevels", 1)),
                    Inputs(("Image", new ImageWrapper(scene)), ("Template", template)));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                var matchCount = RequireInt(result, "MatchCount");
                RequireAtLeast(matchCount, 1, "Shape match count");
                return Observed(("MatchCount", matchCount));
            });
        }

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "ShapeMatching", $"blank_scene_{index:00}", "Blank scene no-match contract", async () =>
            {
                using var template = CreateShapeTemplate();
                using var scene = new Mat(220, 220, MatType.CV_8UC3, Scalar.Black);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.ShapeMatching,
                        ("MinScore", 0.75),
                        ("AngleStart", -30.0),
                        ("AngleExtent", 60.0),
                        ("AngleStep", 1.0),
                        ("NumLevels", 1)),
                    Inputs(("Image", new ImageWrapper(scene)), ("Template", template)));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", false);
                RequireIntEquals(result, "MatchCount", 0);
                RequireStringContains(result, "FailureReason", "No rotation-scale template match");
                return Observed(("FailureReason", "NoMatch"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "ShapeMatching", $"rotated_match_{index:00}", "Rotation-scale oracle", async () =>
            {
                var angle = 20.0 + (index * 5.0);
                using var template = CreateShapeTemplate();
                using var rotated = RotateExpanded(template.MatReadOnly, angle);
                using var scene = new Mat(260, 260, MatType.CV_8UC3, Scalar.Black);
                CopyTemplate(scene, rotated, 82, 76);

                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.ShapeMatching,
                        ("MinScore", 0.45),
                        ("MaxMatches", 1),
                        ("AngleStart", -60.0),
                        ("AngleExtent", 120.0),
                        ("AngleStep", 1.0),
                        ("ScaleMin", 1.0),
                        ("ScaleMax", 1.0),
                        ("ScaleStep", 0.1),
                        ("NumLevels", 2)),
                    Inputs(("Image", new ImageWrapper(scene)), ("Template", template)));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireAtLeast(RequireInt(result, "MatchCount"), 1, "Rotated shape match count");
                return Observed(("ExpectedAngle", angle), ("MatchCount", RequireInt(result, "MatchCount")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "ShapeMatching", $"missing_inputs_{i:00}", "Missing input failure contract", async () =>
            {
                var result = await sut.ExecuteAsync(CreateOperator(OperatorType.ShapeMatching), null);
                RequireFailure(result, "Search image is required");
                return Observed(("FailureReason", "MissingInput"));
            });
        }
    }

    private static void AddPlanarMatchingCases(List<RunnerCase> cases)
    {
        var sut = new PlanarMatchingOperator(NullLogger<PlanarMatchingOperator>.Instance);

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "PlanarMatching", $"same_image_{index:00}", "Feature homography identity oracle", async () =>
            {
                using var image = CreateFeatureRichImage();
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.PlanarMatching,
                        ("DetectorType", DetectorFor(index)),
                        ("MinMatchCount", 4),
                        ("MinInliers", 4),
                        ("ScoreThreshold", 0.2)),
                    Inputs(("Image", image), ("Template", image)));

                RequireSuccess(result);
                RequireBool(result, "VerificationPassed", true);
                RequireBool(result, "IsMatch", true);
                return Observed(("DetectorType", DetectorFor(index)), ("InlierCount", RequireInt(result, "InlierCount")));
            });
        }

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "PlanarMatching", $"perspective_warp_{index:00}", "Perspective homography oracle", async () =>
            {
                using var template = CreateFeatureRichImage();
                using var scene = WarpIntoScene(template.MatReadOnly, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.PlanarMatching,
                        ("DetectorType", "ORB"),
                        ("MinMatchCount", 6),
                        ("MinInliers", 4),
                        ("ScoreThreshold", 0.2)),
                    Inputs(("Image", new ImageWrapper(scene)), ("Template", template)));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireBool(result, "VerificationPassed", true);
                RequireAtLeast(RequireInt(result, "InlierCount"), 4, "Planar inlier count");
                return Observed(("InlierCount", RequireInt(result, "InlierCount")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "PlanarMatching", $"blank_scene_{i:00}", "Blank scene rejection contract", async () =>
            {
                using var template = CreateFeatureRichImage();
                using var blank = new Mat(400, 400, MatType.CV_8UC3, Scalar.Black);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.PlanarMatching,
                        ("DetectorType", "ORB"),
                        ("MinMatchCount", 4),
                        ("MinInliers", 4)),
                    Inputs(("Image", new ImageWrapper(blank)), ("Template", template)));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", false);
                RequireBool(result, "VerificationPassed", false);
                RequireOutput(result, "FailureReason");
                return Observed(("FailureReason", "BlankScene"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "PlanarMatching", $"invalid_detector_{i:00}", "Parameter validation contract", () =>
            {
                var validation = sut.ValidateParameters(
                    CreateOperator(OperatorType.PlanarMatching, ("DetectorType", "SIFT")));
                RequireInvalid(validation, "DetectorType must be ORB, AKAZE, or BRISK");
                return Task.FromResult(Observed(("FailureReason", "InvalidDetector")));
            });
        }
    }

    private static void AddLocalDeformableMatchingCases(List<RunnerCase> cases)
    {
        var sut = new LocalDeformableMatchingOperator(NullLogger<LocalDeformableMatchingOperator>.Instance);

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            Add(cases, "LocalDeformableMatching", $"local_warp_{index:00}", "Local deformation oracle", async () =>
            {
                using var template = CreateLocalPatternTemplate();
                using var scene = CreateLocallyWarpedScene(template, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.LocalDeformableMatching,
                        ("MinMatchScore", 0.05),
                        ("PyramidLevels", 2),
                        ("CandidateThreshold", 0.1),
                        ("MaxDeformation", 18.0)),
                    Inputs(("Image", new ImageWrapper(scene)), ("Template", new ImageWrapper(template))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireBool(result, "VerificationPassed", true);
                RequireString(result, "Method", "MLS_Deformable");
                return Observed(("Method", "MLS_Deformable"), ("DeformationMagnitude", RequireDouble(result, "DeformationMagnitude")));
            });
        }

        for (var i = 0; i < 8; i++)
        {
            Add(cases, "LocalDeformableMatching", $"low_feature_seed_{i:00}", "Low-feature no-match contract", async () =>
            {
                using var template = new Mat(60, 60, MatType.CV_8UC3, Scalar.Black);
                Cv2.Rectangle(template, new Rect(6, 6, 48, 48), Scalar.White, -1);
                using var scene = new Mat(220, 220, MatType.CV_8UC3, Scalar.Black);
                Cv2.Rectangle(scene, new Rect(80, 80, 48, 48), Scalar.White, -1);

                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.LocalDeformableMatching,
                        ("MinMatchScore", 0.2),
                        ("CandidateThreshold", 0.2)),
                    Inputs(("Image", new ImageWrapper(scene)), ("Template", new ImageWrapper(template))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", false);
                RequireOutput(result, "FailureReason");
                return Observed(("FailureReason", "LowFeatureNoMatch"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "LocalDeformableMatching", $"missing_template_{i:00}", "Missing template contract", async () =>
            {
                using var image = CreateFeatureRichImage();
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.LocalDeformableMatching),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", false);
                return Observed(("FailureReason", "MissingTemplate"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "LocalDeformableMatching", $"invalid_pyramid_{i:00}", "Parameter validation contract", () =>
            {
                var validation = sut.ValidateParameters(
                    CreateOperator(OperatorType.LocalDeformableMatching, ("PyramidLevels", 0)));
                RequireInvalid(validation, "PyramidLevels");
                return Task.FromResult(Observed(("FailureReason", "InvalidPyramidLevels")));
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
            float or double or decimal => "double",
            _ => "string"
        };
    }

    private static Dictionary<string, object> Inputs(params (string Name, object Value)[] values)
    {
        return values.ToDictionary(item => item.Name, item => item.Value);
    }

    private static ImageWrapper CreateShapeTemplate()
    {
        var mat = new Mat(48, 48, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(2, 2, 44, 44), Scalar.White, -1);
        Cv2.Line(mat, new Point(4, 24), new Point(44, 24), Scalar.Black, 2);
        Cv2.Line(mat, new Point(24, 4), new Point(24, 44), Scalar.Black, 2);
        Cv2.Circle(mat, new Point(15, 15), 5, Scalar.Black, -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateFeatureRichImage()
    {
        var mat = new Mat(400, 400, MatType.CV_8UC3, Scalar.Gray);
        Cv2.Rectangle(mat, new Rect(50, 50, 100, 100), Scalar.Black, -1);
        Cv2.Rectangle(mat, new Rect(200, 150, 120, 80), Scalar.White, -1);
        Cv2.Circle(mat, new Point(300, 300), 50, Scalar.Black, -1);
        for (var i = 0; i < 10; i++)
        {
            Cv2.Line(mat, new Point(i * 40, 0), new Point(i * 40, 400), Scalar.DarkGray, 2);
        }

        return new ImageWrapper(mat);
    }

    private static Mat CreateLocalPatternTemplate()
    {
        var template = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(template, new Rect(12, 10, 28, 24), Scalar.White, -1);
        Cv2.Circle(template, new Point(84, 28), 14, Scalar.White, -1);
        Cv2.Line(template, new Point(8, 96), new Point(110, 68), new Scalar(180, 180, 180), 3);
        Cv2.Line(template, new Point(20, 54), new Point(92, 102), Scalar.White, 2);
        Cv2.Rectangle(template, new Rect(54, 62, 28, 18), new Scalar(120, 120, 120), -1);
        for (var index = 0; index < 5; index++)
        {
            Cv2.Line(template, new Point(4 + (index * 20), 0), new Point(4 + (index * 20), 119), new Scalar(40, 40, 40), 1);
        }

        return template;
    }

    private static Mat CreateLocallyWarpedScene(Mat template, int index)
    {
        using var mapX = new Mat(template.Size(), MatType.CV_32FC1);
        using var mapY = new Mat(template.Size(), MatType.CV_32FC1);
        var amplitudeX = 2.0 + (index * 0.12);
        var amplitudeY = 1.5 + (index * 0.08);
        for (var y = 0; y < template.Rows; y++)
        {
            for (var x = 0; x < template.Cols; x++)
            {
                var offsetX = amplitudeX * Math.Sin((Math.PI * y) / template.Rows);
                var offsetY = amplitudeY * Math.Sin((Math.PI * x) / template.Cols) * Math.Exp(-Math.Pow((y - (template.Rows / 2.0)) / 40.0, 2));
                mapX.Set(y, x, (float)Math.Clamp(x - offsetX, 0, template.Cols - 1));
                mapY.Set(y, x, (float)Math.Clamp(y - offsetY, 0, template.Rows - 1));
            }
        }

        using var warped = new Mat();
        Cv2.Remap(template, warped, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        var scene = new Mat(260, 260, MatType.CV_8UC3, Scalar.Black);
        CopyTemplate(scene, warped, 70 + (index % 2), 80 + (index % 3));
        return scene;
    }

    private static Mat WarpIntoScene(Mat template, int index)
    {
        var scene = new Mat(520, 520, MatType.CV_8UC3, Scalar.Gray);
        var src = new[]
        {
            new Point2f(0, 0),
            new Point2f(template.Width - 1, 0),
            new Point2f(template.Width - 1, template.Height - 1),
            new Point2f(0, template.Height - 1)
        };
        var jitter = index * 2.0f;
        var dst = new[]
        {
            new Point2f(90 + jitter, 70),
            new Point2f(390, 110 + jitter),
            new Point2f(360 - jitter, 410),
            new Point2f(120, 430 - jitter)
        };

        using var homography = Cv2.GetPerspectiveTransform(src, dst);
        Cv2.WarpPerspective(template, scene, homography, scene.Size(), InterpolationFlags.Linear, BorderTypes.Transparent);
        return scene;
    }

    private static Mat RotateExpanded(Mat src, double angle)
    {
        var center = new Point2f(src.Width / 2f, src.Height / 2f);
        using var rotMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
        var cos = Math.Abs(rotMatrix.Get<double>(0, 0));
        var sin = Math.Abs(rotMatrix.Get<double>(0, 1));
        var boundWidth = Math.Max(1, (int)Math.Ceiling((src.Height * sin) + (src.Width * cos)));
        var boundHeight = Math.Max(1, (int)Math.Ceiling((src.Height * cos) + (src.Width * sin)));
        rotMatrix.Set(0, 2, rotMatrix.Get<double>(0, 2) + (boundWidth / 2.0) - center.X);
        rotMatrix.Set(1, 2, rotMatrix.Get<double>(1, 2) + (boundHeight / 2.0) - center.Y);

        var rotated = new Mat();
        Cv2.WarpAffine(src, rotated, rotMatrix, new Size(boundWidth, boundHeight), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        return rotated;
    }

    private static void CopyTemplate(Mat scene, Mat template, int x, int y)
    {
        using var roi = new Mat(scene, new Rect(x, y, template.Width, template.Height));
        template.CopyTo(roi);
    }

    private static string DetectorFor(int index)
    {
        return (index % 3) switch
        {
            1 => "AKAZE",
            2 => "BRISK",
            _ => "ORB"
        };
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
            throw new InvalidOperationException($"Expected {key}={expected}, got {result.OutputData[key]}.");
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

    private static void RequireStringContains(OperatorExecutionOutput result, string key, string expectedFragment)
    {
        RequireOutput(result, key);
        var actual = result.OutputData![key]?.ToString() ?? string.Empty;
        if (actual.IndexOf(expectedFragment, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException($"Expected {key} to contain '{expectedFragment}', got '{actual}'.");
        }
    }

    private static int RequireInt(OperatorExecutionOutput result, string key)
    {
        RequireOutput(result, key);
        return Convert.ToInt32(result.OutputData![key]);
    }

    private static void RequireIntEquals(OperatorExecutionOutput result, string key, int expected)
    {
        var actual = RequireInt(result, key);
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected {key}={expected}, got {actual}.");
        }
    }

    private static double RequireDouble(OperatorExecutionOutput result, string key)
    {
        RequireOutput(result, key);
        return Convert.ToDouble(result.OutputData![key]);
    }

    private static void RequireAtLeast(int actual, int min, string label)
    {
        if (actual < min)
        {
            throw new InvalidOperationException($"{label} {actual} is below {min}.");
        }
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# P2 Matching Residual Baseline",
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

internal static class RunnerOptions
{
    public static ParsedOptions Parse(string[] args)
    {
        var outputPath = "quality/evals/reports/P2MatchingResidual_baseline.json";
        var reportPath = "quality/evals/reports/P2MatchingResidual_baseline.md";

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
            Usage: dotnet run --project quality/tools/P2MatchingResidualRunner/P2MatchingResidualRunner.csproj -- [options]

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
}
