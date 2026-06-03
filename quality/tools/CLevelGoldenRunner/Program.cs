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

var result = await CLevelGoldenRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"C-level golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class CLevelGoldenRunner
{
    public static async Task<BaselineResult> RunAsync()
    {
        var cases = CreateCases();

        foreach (var warmup in cases.GroupBy(item => item.Operator).Select(group => group.First()))
        {
            _ = await RunCaseAsync(warmup);
        }

        var results = new List<CaseResult>();
        foreach (var testCase in cases)
        {
            results.Add(await RunCaseAsync(testCase));
        }

        var byOperator = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                Math.Round(group.Max(item => item.RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.MemoryAllocationBytes))))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                "embedded synthetic C-level cases",
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                byOperator.Sum(item => item.MemoryAllocationBytesAvg)),
            byOperator,
            results);
    }

    private static IReadOnlyList<GoldenCase> CreateCases()
    {
        var cases = new List<GoldenCase>();
        AddContourExtremaCases(cases);
        AddPhaseClosureCases(cases);
        AddCommentCases(cases);
        return cases;
    }

    private static async Task<CaseResult> RunCaseAsync(GoldenCase testCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);

        try
        {
            var evaluation = await testCase.ExecuteAsync();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                evaluation.Passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                evaluation.Passed ? null : evaluation.ErrorMessage ?? "Metric mismatch",
                evaluation.Metrics);
        }
        catch (Exception ex)
        {
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                ex.Message,
                new Dictionary<string, object?>
                {
                    ["ExceptionType"] = ex.GetType().Name
                });
        }
    }

    private static void AddContourExtremaCases(List<GoldenCase> cases)
    {
        var specs = new[]
        {
            new ContourSpec("quad", [new(20, 40), new(10, 12), new(50, 18), new(40, 60)], new Point2f(0, 0)),
            new ContourSpec("negative", [new(-8, 4), new(12, -3), new(20, 16), new(-2, 22)], new Point2f(3, 1)),
            new ContourSpec("collinear", [new(40, 10), new(10, 10), new(25, 10)], new Point2f(0, 0)),
            new ContourSpec("duplicate_extreme", [new(4, 6), new(4, 2), new(12, 2), new(12, 9), new(8, 9)], new Point2f(6, 5)),
            new ContourSpec("slanted", [new(3, 40), new(15, 22), new(31, 18), new(42, 34), new(25, 52)], new Point2f(20, 30)),
            new ContourSpec("single_point", [new(12, 34)], new Point2f(0, 0))
        };

        var index = 0;
        foreach (var spec in specs)
        {
            foreach (var direction in new[] { "horizontal", "vertical", "distance" })
            {
                var caseId = $"ContourExtrema_{spec.Name}_{direction}_{index++:0000}";
                cases.Add(new GoldenCase(
                    caseId,
                    "ContourExtrema",
                    $"{spec.Name}_{direction}",
                    () => RunContourCaseAsync(ContourCase.ExpectSuccess(caseId, spec, direction, ContourPayloadKind.Point2fArray))));
            }
        }

        cases.Add(new GoldenCase(
            $"ContourExtrema_point_array_horizontal_{index++:0000}",
            "ContourExtrema",
            "point_array_horizontal",
            () => RunContourCaseAsync(ContourCase.ExpectSuccess(
                $"ContourExtrema_point_array_horizontal_{index:0000}",
                specs[0],
                "horizontal",
                ContourPayloadKind.PointArray))));

        cases.Add(new GoldenCase(
            $"ContourExtrema_unknown_direction_fallback_{index++:0000}",
            "ContourExtrema",
            "unknown_direction_fallback",
            () => RunContourCaseAsync(ContourCase.ExpectSuccess(
                $"ContourExtrema_unknown_direction_fallback_{index:0000}",
                specs[1],
                "diagonal",
                ContourPayloadKind.ListPoint2f,
                expectedDirection: "horizontal"))));

        cases.Add(new GoldenCase(
            $"ContourExtrema_empty_contour_{index++:0000}",
            "ContourExtrema",
            "empty_contour",
            () => RunContourCaseAsync(ContourCase.ExpectFailure(
                $"ContourExtrema_empty_contour_{index:0000}",
                new ContourSpec("empty", [], new Point2f(0, 0)),
                "horizontal",
                ContourPayloadKind.Point2fArray,
                "at least one point"))));

        cases.Add(new GoldenCase(
            $"ContourExtrema_distance_missing_ref_{index++:0000}",
            "ContourExtrema",
            "distance_missing_reference",
            () => RunContourCaseAsync(ContourCase.ExpectFailure(
                $"ContourExtrema_distance_missing_ref_{index:0000}",
                specs[2],
                "distance",
                ContourPayloadKind.Point2fArray,
                "ReferencePoint",
                includeReferencePoint: false))));
    }

    private static async Task<CaseEvaluation> RunContourCaseAsync(ContourCase testCase)
    {
        var sut = new ContourExtremaOperator(NullLogger<ContourExtremaOperator>.Instance);
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);
        var inputs = new Dictionary<string, object>
        {
            ["Contour"] = CreateContourPayload(testCase.Spec.Points, testCase.PayloadKind),
            ["Direction"] = testCase.Direction
        };

        if (testCase.IncludeReferencePoint)
        {
            inputs["ReferencePoint"] = testCase.Spec.ReferencePoint;
        }

        var execution = await sut.ExecuteAsync(op, inputs);
        var metrics = new Dictionary<string, object?>
        {
            ["ExpectedSuccess"] = testCase.ExpectedSuccess,
            ["ActualSuccess"] = execution.IsSuccess,
            ["Direction"] = testCase.Direction,
            ["PayloadKind"] = testCase.PayloadKind.ToString(),
            ["ErrorMessage"] = execution.ErrorMessage
        };

        if (!testCase.ExpectedSuccess)
        {
            var passed = !execution.IsSuccess &&
                (testCase.ExpectedErrorContains is null ||
                    (execution.ErrorMessage?.Contains(testCase.ExpectedErrorContains, StringComparison.OrdinalIgnoreCase) ?? false));
            return new CaseEvaluation(passed, passed ? null : execution.ErrorMessage, metrics);
        }

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return new CaseEvaluation(false, execution.ErrorMessage ?? "Missing output data", metrics);
        }

        var expected = ComputeExpectedExtrema(testCase.Spec.Points, testCase.ExpectedDirection, testCase.Spec.ReferencePoint);
        var actualMin = (Point2f)execution.OutputData["MinPoint"];
        var actualMax = (Point2f)execution.OutputData["MaxPoint"];
        var actualMinValue = Convert.ToDouble(execution.OutputData["MinValue"]);
        var actualMaxValue = Convert.ToDouble(execution.OutputData["MaxValue"]);
        var extremaCount = execution.OutputData.TryGetValue("ExtremaPoints", out var extremaValue) && extremaValue is List<Point2f> extrema
            ? extrema.Count
            : -1;

        metrics["ExpectedMinPoint"] = FormatPoint(expected.MinPoint);
        metrics["ExpectedMaxPoint"] = FormatPoint(expected.MaxPoint);
        metrics["ActualMinPoint"] = FormatPoint(actualMin);
        metrics["ActualMaxPoint"] = FormatPoint(actualMax);
        metrics["ActualExtremaCount"] = extremaCount;
        metrics["MinValueError"] = Math.Round(Math.Abs(actualMinValue - expected.MinValue), 6);
        metrics["MaxValueError"] = Math.Round(Math.Abs(actualMaxValue - expected.MaxValue), 6);

        var passedResult = NearlyEqual(actualMin, expected.MinPoint)
            && NearlyEqual(actualMax, expected.MaxPoint)
            && Math.Abs(actualMinValue - expected.MinValue) < 1e-5
            && Math.Abs(actualMaxValue - expected.MaxValue) < 1e-5
            && extremaCount == (NearlyEqual(expected.MinPoint, expected.MaxPoint) ? 1 : 2);

        ReleaseImageOutputs(execution.OutputData);
        return new CaseEvaluation(passedResult, passedResult ? null : "Contour extrema metrics did not match expected values.", metrics);
    }

    private static object CreateContourPayload(IReadOnlyList<Point2f> points, ContourPayloadKind kind)
    {
        return kind switch
        {
            ContourPayloadKind.PointArray => points.Select(point => new Point((int)Math.Round(point.X), (int)Math.Round(point.Y))).ToArray(),
            ContourPayloadKind.ListPoint2f => points.ToList(),
            _ => points.ToArray()
        };
    }

    private static ContourExpected ComputeExpectedExtrema(IReadOnlyList<Point2f> points, string direction, Point2f referencePoint)
    {
        var values = points
            .Select(point => new ContourPointValue(point, ContourValue(point, direction, referencePoint)))
            .ToList();

        var min = OrderContourValues(values, direction, descending: false).First();
        var max = OrderContourValues(values, direction, descending: true).First();
        return new ContourExpected(min.Point, max.Point, min.Value, max.Value);
    }

    private static double ContourValue(Point2f point, string direction, Point2f referencePoint)
    {
        return direction switch
        {
            "vertical" or "y" => point.Y,
            "distance" => Math.Sqrt(Math.Pow(point.X - referencePoint.X, 2) + Math.Pow(point.Y - referencePoint.Y, 2)),
            _ => point.X
        };
    }

    private static IOrderedEnumerable<ContourPointValue> OrderContourValues(
        IEnumerable<ContourPointValue> values,
        string direction,
        bool descending)
    {
        return direction switch
        {
            "vertical" or "y" => descending
                ? values.OrderByDescending(v => v.Value).ThenByDescending(v => v.Point.X).ThenByDescending(v => v.Point.Y)
                : values.OrderBy(v => v.Value).ThenBy(v => v.Point.X).ThenBy(v => v.Point.Y),
            "distance" => descending
                ? values.OrderByDescending(v => v.Value).ThenByDescending(v => v.Point.X).ThenByDescending(v => v.Point.Y)
                : values.OrderBy(v => v.Value).ThenBy(v => v.Point.X).ThenBy(v => v.Point.Y),
            _ => descending
                ? values.OrderByDescending(v => v.Value).ThenByDescending(v => v.Point.Y).ThenByDescending(v => v.Point.X)
                : values.OrderBy(v => v.Value).ThenBy(v => v.Point.Y).ThenBy(v => v.Point.X)
        };
    }

    private static void AddPhaseClosureCases(List<GoldenCase> cases)
    {
        var index = 0;
        var rampCases = new[]
        {
            new PhaseCase("ramp_32", PhaseScene.Ramp, "itoh", 32, 32, 0.19, 0.11, 0.0, false, 0, false, null, 0.22, 0, 0.0),
            new PhaseCase("ramp_48", PhaseScene.Ramp, "itoh", 48, 40, 0.14, 0.09, 0.4, false, 0, false, null, 0.22, 0, 0.0),
            new PhaseCase("ramp_wide", PhaseScene.Ramp, "itoh", 64, 36, 0.08, 0.16, -0.3, false, 0, false, null, 0.22, 0, 0.0),
            new PhaseCase("ramp_tall", PhaseScene.Ramp, "itoh", 36, 64, 0.17, 0.07, 0.8, false, 0, false, null, 0.22, 0, 0.0),
            new PhaseCase("ramp_gentle", PhaseScene.Ramp, "itoh", 56, 56, 0.05, 0.06, -1.1, false, 0, false, null, 0.12, 0, 0.0),
            new PhaseCase("ramp_x_only", PhaseScene.Ramp, "itoh", 60, 28, 0.18, 0.0, 0.2, false, 0, false, null, 0.16, 0, 0.0),
            new PhaseCase("ramp_y_only", PhaseScene.Ramp, "itoh", 28, 60, 0.0, 0.18, -0.2, false, 0, false, null, 0.16, 0, 0.0),
            new PhaseCase("ramp_offset", PhaseScene.Ramp, "itoh", 40, 40, 0.12, 0.13, 1.3, false, 0, false, null, 0.22, 0, 0.0)
        };

        foreach (var testCase in rampCases)
        {
            AddPhaseCase(cases, testCase, ref index);
        }

        foreach (var testCase in new[]
        {
            new PhaseCase("quality_centered", PhaseScene.Ramp, "quality", 48, 48, 0.11, 0.10, 0.0, true, 0, false, null, 0.35, 0, 0.0),
            new PhaseCase("quality_rect", PhaseScene.Ramp, "quality", 52, 40, 0.09, 0.12, 0.5, true, 0, false, null, 0.35, 0, 0.0),
            new PhaseCase("quality_gentle", PhaseScene.Ramp, "quality", 36, 36, 0.04, 0.06, -0.8, true, 0, false, null, 0.22, 0, 0.0),
            new PhaseCase("quality_x_only", PhaseScene.Ramp, "quality", 44, 28, 0.13, 0.0, 0.2, true, 0, false, null, 0.3, 0, 0.0)
        })
        {
            AddPhaseCase(cases, testCase, ref index);
        }

        foreach (var testCase in new[]
        {
            new PhaseCase("floodfill_centered", PhaseScene.Ramp, "floodfill", 40, 40, 0.10, 0.08, 0.0, false, 0, false, null, 0.3, 0, 0.0),
            new PhaseCase("floodfill_offset", PhaseScene.Ramp, "floodfill", 44, 36, 0.07, 0.11, -0.4, false, 0, false, null, 0.3, 0, 0.0),
            new PhaseCase("floodfill_discontinuity", PhaseScene.Discontinuity, "floodfill", 48, 48, 0.0, 0.04, 0.0, false, 0, false, null, null, 1, 0.0),
            new PhaseCase("itoh_discontinuity", PhaseScene.Discontinuity, "itoh", 48, 48, 0.0, 0.04, 0.0, false, 0, false, null, null, 1, 0.0)
        })
        {
            AddPhaseCase(cases, testCase, ref index);
        }

        foreach (var testCase in new[]
        {
            new PhaseCase("uniform_zero", PhaseScene.Uniform, "itoh", 32, 32, 0, 0, 0.0, false, 0, false, null, 1e-4, 0, 0.99),
            new PhaseCase("uniform_positive", PhaseScene.Uniform, "quality", 40, 40, 0, 0, 1.25, true, 0, false, null, 1e-4, 0, 0.99),
            new PhaseCase("uniform_negative", PhaseScene.Uniform, "floodfill", 36, 36, 0, 0, -2.25, false, 0, false, null, 1e-4, 0, 0.99),
            new PhaseCase("wavelength_scaled", PhaseScene.Ramp, "itoh", 32, 32, 0.08, 0.07, 0.2, false, 532.0, false, null, 0.25, 0, 0.0),
            new PhaseCase("bad_quality_size", PhaseScene.Ramp, "quality", 32, 32, 0.08, 0.07, 0.0, true, 0, true, "QualityMap", null, 0, 0.0),
            new PhaseCase("missing_image", PhaseScene.MissingImage, "itoh", 32, 32, 0, 0, 0.0, false, 0, false, "PhaseImage", null, 0, 0.0)
        })
        {
            AddPhaseCase(cases, testCase, ref index);
        }
    }

    private static void AddPhaseCase(List<GoldenCase> cases, PhaseCase testCase, ref int index)
    {
        var caseId = $"PhaseClosure_{testCase.Name}_{index++:0000}";
        cases.Add(new GoldenCase(
            caseId,
            "PhaseClosure",
            testCase.Name,
            () => RunPhaseCaseAsync(testCase)));
    }

    private static async Task<CaseEvaluation> RunPhaseCaseAsync(PhaseCase testCase)
    {
        var sut = new PhaseClosureOperator(NullLogger<PhaseClosureOperator>.Instance);
        var op = new Operator("PhaseClosure", OperatorType.PhaseClosure, 0, 0);
        var inputs = new Dictionary<string, object>
        {
            ["UnwrapMethod"] = testCase.Method
        };

        if (testCase.Scene != PhaseScene.MissingImage)
        {
            inputs["PhaseImage"] = new ImageWrapper(CreatePhaseMat(testCase));
        }

        if (testCase.Wavelength > 0)
        {
            inputs["Wavelength"] = testCase.Wavelength;
        }

        if (testCase.UseQualityMap)
        {
            var rows = testCase.BadQualityMapSize ? Math.Max(1, testCase.Height / 2) : testCase.Height;
            var cols = testCase.BadQualityMapSize ? Math.Max(1, testCase.Width / 2) : testCase.Width;
            inputs["QualityMap"] = new ImageWrapper(CreatePhaseQualityMap(rows, cols));
        }

        var execution = await sut.ExecuteAsync(op, inputs);
        var metrics = new Dictionary<string, object?>
        {
            ["ExpectedSuccess"] = testCase.ExpectedSuccess,
            ["ActualSuccess"] = execution.IsSuccess,
            ["Method"] = testCase.Method,
            ["Scene"] = testCase.Scene.ToString(),
            ["ErrorMessage"] = execution.ErrorMessage
        };

        if (!testCase.ExpectedSuccess)
        {
            var passed = !execution.IsSuccess &&
                (testCase.ExpectedErrorContains is null ||
                    (execution.ErrorMessage?.Contains(testCase.ExpectedErrorContains, StringComparison.OrdinalIgnoreCase) ?? false));
            return new CaseEvaluation(passed, passed ? null : execution.ErrorMessage, metrics);
        }

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return new CaseEvaluation(false, execution.ErrorMessage ?? "Missing output data", metrics);
        }

        var unwrappedWrapper = execution.OutputData["UnwrappedPhase"] as ImageWrapper;
        var discontinuitiesWrapper = execution.OutputData["Discontinuities"] as ImageWrapper;
        if (unwrappedWrapper is null || discontinuitiesWrapper is null)
        {
            ReleaseImageOutputs(execution.OutputData);
            return new CaseEvaluation(false, "Phase output wrappers are missing.", metrics);
        }

        var unwrapped = unwrappedWrapper.GetMat();
        var discontinuities = discontinuitiesWrapper.GetMat();
        var nonFinite = CountNonFinitePixels(unwrapped);
        var discontinuityCount = Cv2.CountNonZero(discontinuities);
        var quality = Convert.ToDouble(execution.OutputData["Quality"]);
        double? mae = testCase.MaxMae.HasValue
            ? ComputePhaseMaeAllowingGlobalOffset(unwrapped, testCase)
            : null;

        metrics["NonFinitePixels"] = nonFinite;
        metrics["DiscontinuityCount"] = discontinuityCount;
        metrics["Quality"] = Math.Round(quality, 6);
        metrics["Mae"] = mae.HasValue ? Math.Round(mae.Value, 6) : null;

        var passedResult = nonFinite == 0
            && double.IsFinite(quality)
            && quality >= testCase.MinQuality
            && quality <= 1.0
            && discontinuityCount >= testCase.MinDiscontinuities
            && (!testCase.MaxMae.HasValue || mae <= testCase.MaxMae.Value);

        ReleaseImageOutputs(execution.OutputData);
        return new CaseEvaluation(passedResult, passedResult ? null : "Phase metrics did not meet golden tolerances.", metrics);
    }

    private static Mat CreatePhaseMat(PhaseCase testCase)
    {
        var mat = new Mat(testCase.Height, testCase.Width, MatType.CV_32FC1);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var value = testCase.Scene switch
                {
                    PhaseScene.Uniform => testCase.Offset,
                    PhaseScene.Discontinuity => testCase.Offset + (y * testCase.SlopeY) + (x >= testCase.Width / 2 ? Math.PI - 0.05 : 0.0),
                    _ => testCase.Offset + (x * testCase.SlopeX) + (y * testCase.SlopeY)
                };
                mat.Set(y, x, WrapPhase(value));
            }
        }

        return mat;
    }

    private static Mat CreatePhaseQualityMap(int rows, int cols)
    {
        var quality = new Mat(rows, cols, MatType.CV_8UC1);
        var centerX = (cols - 1) / 2.0;
        var centerY = (rows - 1) / 2.0;

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                var value = 255.0 - (distance * 5.5);
                quality.Set(y, x, (byte)Math.Clamp((int)Math.Round(value), 1, 255));
            }
        }

        return quality;
    }

    private static double ComputePhaseMaeAllowingGlobalOffset(Mat unwrapped, PhaseCase testCase)
    {
        var scale = testCase.Wavelength > 0 ? testCase.Wavelength / (2.0 * Math.PI) : 1.0;
        var globalOffset = unwrapped.At<float>(0, 0) - (float)(testCase.Offset * scale);
        var mae = 0.0;

        for (var y = 0; y < unwrapped.Rows; y++)
        {
            for (var x = 0; x < unwrapped.Cols; x++)
            {
                var expected = testCase.Scene == PhaseScene.Uniform
                    ? testCase.Offset
                    : testCase.Offset + (x * testCase.SlopeX) + (y * testCase.SlopeY);
                mae += Math.Abs((unwrapped.At<float>(y, x) - globalOffset) - (expected * scale));
            }
        }

        return mae / (unwrapped.Rows * unwrapped.Cols);
    }

    private static void AddCommentCases(List<GoldenCase> cases)
    {
        var index = 0;
        var commentCases = new List<CommentCase>
        {
            CommentCase.Success("missing_input_default_text", null, null, string.Empty, false),
            CommentCase.Success("string_payload", "checkpoint", () => "payload", "payload", false),
            CommentCase.Success("int_payload", "count", () => 42, 42, false),
            CommentCase.Success("double_payload", "score", () => 12.5, 12.5, false),
            CommentCase.Success("bool_payload", "flag", () => true, true, false),
            CommentCase.Success("dictionary_payload", "dict", () => new Dictionary<string, object> { ["Station"] = "A" }, null, true),
            CommentCase.Success("list_payload", "list", () => new List<int> { 1, 2, 3 }, null, true),
            CommentCase.Success("byte_array_payload", "bytes", () => new byte[] { 1, 2, 3 }, null, true),
            CommentCase.Success("empty_text", string.Empty, () => "value", "value", false),
            CommentCase.Success("long_valid_text", new string('x', 4096), () => "value", "value", false),
            CommentCase.Success("numeric_text_param", 123, () => "value", "value", false),
            CommentCase.Success("bool_text_param", false, () => "value", "value", false),
            CommentCase.Success("image_payload_small", "image", () => new ImageWrapper(new Mat(16, 16, MatType.CV_8UC3, Scalar.White)), null, true),
            CommentCase.Success("image_payload_gray", "image-gray", () => new ImageWrapper(new Mat(12, 20, MatType.CV_8UC1, Scalar.All(127))), null, true),
            CommentCase.Success("large_scalar_payload", "large", () => new string('p', 256), new string('p', 256), false),
            CommentCase.Success("zero_payload", "zero", () => 0, 0, false),
            CommentCase.Success("negative_payload", "negative", () => -7, -7, false),
            CommentCase.Success("decimal_payload", "decimal", () => 4.75m, 4.75m, false),
            CommentCase.Success("object_payload", "object", () => new PayloadBox("box-a"), null, true),
            CommentCase.Success("max_minus_one_text", new string('m', 4095), () => "value", "value", false),
            CommentCase.Failure("too_long_text", new string('x', 4097), "4096"),
            CommentCase.Failure("much_too_long_text", new string('y', 5000), "4096")
        };

        foreach (var testCase in commentCases)
        {
            var caseId = $"Comment_{testCase.Name}_{index++:0000}";
            cases.Add(new GoldenCase(
                caseId,
                "Comment",
                testCase.Name,
                () => RunCommentCaseAsync(testCase)));
        }
    }

    private static async Task<CaseEvaluation> RunCommentCaseAsync(CommentCase testCase)
    {
        var sut = new CommentOperator(NullLogger<CommentOperator>.Instance);
        var op = new Operator("Comment", OperatorType.Comment, 0, 0);
        if (testCase.TextConfigured)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), "Text", "Text", string.Empty, "string", testCase.TextValue));
        }

        var validation = sut.ValidateParameters(op);
        var input = testCase.InputFactory?.Invoke();
        var inputs = testCase.InputFactory is null
            ? null
            : new Dictionary<string, object> { ["Input"] = input! };

        var execution = await sut.ExecuteAsync(op, inputs);
        var metrics = new Dictionary<string, object?>
        {
            ["ExpectedSuccess"] = testCase.ExpectedSuccess,
            ["ActualSuccess"] = execution.IsSuccess,
            ["ValidationIsValid"] = validation.IsValid,
            ["TextLength"] = testCase.TextValue?.ToString()?.Length ?? 0,
            ["ErrorMessage"] = execution.ErrorMessage
        };

        if (!testCase.ExpectedSuccess)
        {
            var passed = !validation.IsValid
                && !execution.IsSuccess
                && (testCase.ExpectedErrorContains is null ||
                    (execution.ErrorMessage?.Contains(testCase.ExpectedErrorContains, StringComparison.OrdinalIgnoreCase) ?? false));
            return new CaseEvaluation(passed, passed ? null : execution.ErrorMessage, metrics);
        }

        if (!validation.IsValid || !execution.IsSuccess || execution.OutputData is null)
        {
            ReleaseImageOutputs(execution.OutputData);
            return new CaseEvaluation(false, execution.ErrorMessage ?? "Missing output data", metrics);
        }

        var actualMessage = execution.OutputData["Message"]?.ToString() ?? string.Empty;
        var actualOutput = execution.OutputData["Output"];
        var expectedMessage = testCase.TextConfigured ? testCase.TextValue?.ToString() ?? string.Empty : string.Empty;
        var expectedOutput = testCase.InputFactory is null ? string.Empty : testCase.ExpectedOutput ?? input;

        metrics["ExpectedMessageLength"] = expectedMessage.Length;
        metrics["ActualMessageLength"] = actualMessage.Length;
        metrics["OutputType"] = actualOutput.GetType().Name;
        metrics["ReferencePreserved"] = testCase.ExpectSameReference ? ReferenceEquals(actualOutput, input) : null;

        var passedResult = actualMessage == expectedMessage &&
            (testCase.ExpectSameReference
                ? ReferenceEquals(actualOutput, input)
                : Equals(actualOutput, expectedOutput));

        ReleaseImageOutputs(execution.OutputData);
        return new CaseEvaluation(passedResult, passedResult ? null : "Comment contract output did not match expected value.", metrics);
    }

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

    private static int CountNonFinitePixels(Mat mat)
    {
        var count = 0;
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var value = mat.At<float>(y, x);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static float WrapPhase(double value) => (float)Math.Atan2(Math.Sin(value), Math.Cos(value));

    private static bool NearlyEqual(Point2f actual, Point2f expected)
    {
        return Math.Abs(actual.X - expected.X) < 1e-4f && Math.Abs(actual.Y - expected.Y) < 1e-4f;
    }

    private static string FormatPoint(Point2f point) => $"{point.X:0.###},{point.Y:0.###}";
}

internal sealed record GoldenCase(
    string CaseId,
    string Operator,
    string Scenario,
    Func<Task<CaseEvaluation>> ExecuteAsync);

internal sealed record CaseEvaluation(
    bool Passed,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object?> Metrics);

internal sealed record ContourSpec(
    string Name,
    IReadOnlyList<Point2f> Points,
    Point2f ReferencePoint);

internal sealed record ContourCase(
    string CaseId,
    ContourSpec Spec,
    string Direction,
    ContourPayloadKind PayloadKind,
    bool ExpectedSuccess,
    string? ExpectedErrorContains,
    bool IncludeReferencePoint,
    string ExpectedDirection)
{
    public static ContourCase ExpectSuccess(
        string caseId,
        ContourSpec spec,
        string direction,
        ContourPayloadKind payloadKind,
        string? expectedDirection = null)
    {
        return new ContourCase(caseId, spec, direction, payloadKind, true, null, true, expectedDirection ?? direction);
    }

    public static ContourCase ExpectFailure(
        string caseId,
        ContourSpec spec,
        string direction,
        ContourPayloadKind payloadKind,
        string expectedErrorContains,
        bool includeReferencePoint = true)
    {
        return new ContourCase(caseId, spec, direction, payloadKind, false, expectedErrorContains, includeReferencePoint, direction);
    }
}

internal enum ContourPayloadKind
{
    Point2fArray,
    PointArray,
    ListPoint2f
}

internal sealed record ContourPointValue(Point2f Point, double Value);

internal sealed record ContourExpected(Point2f MinPoint, Point2f MaxPoint, double MinValue, double MaxValue);

internal sealed record PhaseCase(
    string Name,
    PhaseScene Scene,
    string Method,
    int Width,
    int Height,
    double SlopeX,
    double SlopeY,
    double Offset,
    bool UseQualityMap,
    double Wavelength,
    bool BadQualityMapSize,
    string? ExpectedErrorContains,
    double? MaxMae,
    int MinDiscontinuities,
    double MinQuality)
{
    public bool ExpectedSuccess => ExpectedErrorContains is null;
}

internal enum PhaseScene
{
    Ramp,
    Uniform,
    Discontinuity,
    MissingImage
}

internal sealed record CommentCase(
    string Name,
    bool TextConfigured,
    object? TextValue,
    Func<object>? InputFactory,
    object? ExpectedOutput,
    bool ExpectSameReference,
    bool ExpectedSuccess,
    string? ExpectedErrorContains)
{
    public static CommentCase Success(
        string name,
        object? textValue,
        Func<object>? inputFactory,
        object? expectedOutput,
        bool expectSameReference)
    {
        return new CommentCase(name, textValue is not null, textValue, inputFactory, expectedOutput, expectSameReference, true, null);
    }

    public static CommentCase Failure(string name, object? textValue, string expectedErrorContains)
    {
        return new CommentCase(name, true, textValue, null, null, false, false, expectedErrorContains);
    }
}

internal sealed record PayloadBox(string Name);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# C-Level Golden Runner Report",
            string.Empty,
            $"GeneratedAtUtc: {result.Summary.GeneratedAtUtc:O}",
            $"CasesRoot: `{result.Summary.CasesRoot}`",
            string.Empty,
            "## Summary",
            string.Empty,
            $"Cases: {result.Summary.CaseCount}",
            $"Passed: {result.Summary.Passed}",
            $"Failed: {result.Summary.Failed}",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg Runtime Ms | Max Runtime Ms | Avg Allocation Bytes |",
            "|---|---:|---:|---:|---:|---:|---:|"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.RuntimeMsMax:0.###} | {item.MemoryAllocationBytesAvg} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Scenario Results",
            string.Empty,
            "| Case | Operator | Scenario | Passed | Runtime Ms | Error |",
            "|---|---|---|---|---:|---|"
        ]);

        foreach (var item in result.Cases)
        {
            lines.Add($"| {item.CaseId} | {item.Operator} | {item.Scenario} | {BoolToMark(item.Passed)} | {item.RuntimeMs:0.###} | {item.ErrorMessage ?? "-"} |");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BoolToMark(bool value) => value ? "Yes" : "No";
}

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/CLevel_contract_baseline.json";
        string? report = "quality/evals/reports/CLevel_contract_baseline.md";
        string? parseError = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--output":
                    output = NextValue(args, ref i, arg, ref parseError);
                    break;
                case "--report":
                    report = NextValue(args, ref i, arg, ref parseError);
                    break;
                default:
                    parseError = $"Unknown argument: {arg}";
                    break;
            }
        }

        return new RunnerOptions(output, report, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            C-level golden runner

            Options:
              --output <path>      Baseline JSON output path.
              --report <path>      Markdown report output path.
              --help               Show help.
            """);
    }

    private static string NextValue(string[] args, ref int index, string optionName, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"Missing value for {optionName}";
            return string.Empty;
        }

        index++;
        return args[index];
    }
}

internal sealed record BaselineResult(
    BaselineSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<CaseResult> Cases);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    string CasesRoot,
    int CaseCount,
    int Passed,
    int Failed,
    long MemoryAllocationBytesAvgSum);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    double RuntimeMsMax,
    long MemoryAllocationBytesAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object?> Metrics);

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
