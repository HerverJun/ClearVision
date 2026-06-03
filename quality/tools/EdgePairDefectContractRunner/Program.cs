using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
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

var result = await ContractRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"EdgePairDefect contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private static readonly EdgePairDefectOperator Operator = new(NullLogger<EdgePairDefectOperator>.Instance);

    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<ContractCase>
        {
            new("provided_parallel_lines_zero_defects", "Provided line geometry", ProvidedParallelLinesZeroDefects),
            new("provided_wide_pair_single_defect", "Provided line geometry", ProvidedWidePairSingleDefect),
            new("provided_narrow_pair_single_defect", "Provided line geometry", ProvidedNarrowPairSingleDefect),
            new("tolerance_boundary_is_not_defect", "Tolerance contract", ToleranceBoundaryIsNotDefect),
            new("tolerance_exceeded_is_defect", "Tolerance contract", ToleranceExceededIsDefect),
            new("high_sample_count_returns_requested_deviations", "Sampling contract", HighSampleCountReturnsRequestedDeviations),
            new("min_sample_count_returns_requested_deviations", "Sampling contract", MinSampleCountReturnsRequestedDeviations),
            new("diagonal_parallel_lines_zero_defects", "Provided line geometry", DiagonalParallelLinesZeroDefects),
            new("sobel_provided_lines_zero_defects", "Edge method contract", SobelProvidedLinesZeroDefects),
            new("auto_detect_canny_pair_success", "Auto line detection", AutoDetectCannyPairSuccess),
            new("auto_detect_sobel_pair_success", "Auto line detection", AutoDetectSobelPairSuccess),
            new("auto_detect_blank_without_lines_fails", "Failure contract", AutoDetectBlankWithoutLinesFails),
            new("missing_image_fails", "Failure contract", MissingImageFails),
            new("degenerate_line_fails", "Failure contract", DegenerateLineFails),
            new("dict_start_end_line_parse", "Line input contract", DictStartEndLineParse),
            new("dict_x1_y1_line_parse", "Line input contract", DictX1Y1LineParse),
            new("legacy_hashtable_line_parse", "Line input contract", LegacyHashtableLineParse),
            new("validate_defaults_valid", "Validation contract", ValidateDefaultsValid),
            new("validate_negative_expected_invalid", "Validation contract", ValidateNegativeExpectedInvalid),
            new("validate_negative_tolerance_invalid", "Validation contract", ValidateNegativeToleranceInvalid),
            new("validate_bad_edge_method_invalid", "Validation contract", ValidateBadEdgeMethodInvalid),
            new("build_edge_map_canny_nonzero", "Private helper contract", BuildEdgeMapCannyNonzero),
            new("build_edge_map_sobel_nonzero", "Private helper contract", BuildEdgeMapSobelNonzero),
            new("distance_point_to_line_horizontal", "Private helper contract", DistancePointToLineHorizontal),
            new("angle_diff_wraps_180", "Private helper contract", AngleDiffWraps180),
            new("try_parse_line_rejects_bad_dict", "Line input contract", TryParseLineRejectsBadDict),
            new("output_image_is_color_and_same_size", "Output contract", OutputImageIsColorAndSameSize)
        };

        var results = new List<CaseResult>(cases.Count);
        foreach (var contractCase in cases)
        {
            results.Add(await RunCaseAsync(contractCase));
        }

        var scenarioSummaries = results
            .GroupBy(x => x.Scenario)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(x => x.Passed),
                group.Count(x => !x.Passed),
                Math.Round(group.Average(x => x.RuntimeMs), 3)))
            .ToArray();

        var passed = results.Count(x => x.Passed);
        var failed = results.Count - passed;
        var summary = new BaselineSummary(
            DateTimeOffset.UtcNow,
            results.Count,
            passed,
            failed,
            Math.Round(results.Sum(x => x.RuntimeMs), 3));

        var operatorEvidence = new[]
        {
            new OperatorEvidence(
                "EdgePairDefect",
                results.Count,
                passed,
                failed,
                Math.Round(results.Average(x => x.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(results.Average(x => x.MemoryAllocationBytes))))
        };

        return new BaselineResult(summary, operatorEvidence, scenarioSummaries, results.ToArray());
    }

    private static async Task<CaseResult> RunCaseAsync(ContractCase contractCase)
    {
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await contractCase.Body();
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(contractCase.Name, contractCase.Scenario, true, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3), Math.Max(0, afterBytes - beforeBytes), null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(contractCase.Name, contractCase.Scenario, false, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3), Math.Max(0, afterBytes - beforeBytes), FormatException(ex));
        }
    }

    private static async Task ProvidedParallelLinesZeroDefects()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1, samples: 25),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 40, 110, 40));

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 25);
            RequireAllDeviations(result, 0);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task ProvidedWidePairSingleDefect()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 22, tolerance: 2, samples: 40),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 46, 110, 46));

        try
        {
            AssertSuccess(result, expectedDefects: 1, expectedMaxDeviation: 4, expectedDeviationCount: 40);
            RequireAllDeviations(result, 4);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task ProvidedNarrowPairSingleDefect()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 2, samples: 35),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 37, 110, 37));

        try
        {
            AssertSuccess(result, expectedDefects: 1, expectedMaxDeviation: 3, expectedDeviationCount: 35);
            RequireAllDeviations(result, -3);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task ToleranceBoundaryIsNotDefect()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 2, samples: 25),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 42, 110, 42));

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 2, expectedDeviationCount: 25);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task ToleranceExceededIsDefect()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1.9, samples: 25),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 42, 110, 42));

        try
        {
            AssertSuccess(result, expectedDefects: 1, expectedMaxDeviation: 2, expectedDeviationCount: 25);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task HighSampleCountReturnsRequestedDeviations()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 22, tolerance: 2, samples: 250),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 46, 110, 46));

        try
        {
            AssertSuccess(result, expectedDefects: 1, expectedMaxDeviation: 4, expectedDeviationCount: 250);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task MinSampleCountReturnsRequestedDeviations()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1, samples: 5),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 40, 110, 40));

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 5);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task DiagonalParallelLinesZeroDefects()
    {
        using var image = CreateBlankImage();
        var line1 = new LineData(10, 20, 110, 60);
        var normalX = -40.0 / Math.Sqrt((100.0 * 100.0) + (40.0 * 40.0));
        var normalY = 100.0 / Math.Sqrt((100.0 * 100.0) + (40.0 * 40.0));
        var line2 = new LineData(
            (float)(line1.StartX + normalX * 20.0),
            (float)(line1.StartY + normalY * 20.0),
            (float)(line1.EndX + normalX * 20.0),
            (float)(line1.EndY + normalY * 20.0));

        var result = await ExecuteAsync(CreateOperator(expectedWidth: 20, tolerance: 0.01, samples: 50), image, line1, line2);
        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 50, tolerance: 0.001);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task SobelProvidedLinesZeroDefects()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1, samples: 25, edgeMethod: "Sobel"),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 40, 110, 40));

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 25);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task AutoDetectCannyPairSuccess()
    {
        using var image = CreateAutoDetectPreferenceImage();
        var result = await ExecuteAsync(CreateOperator(expectedWidth: 24, tolerance: 4, samples: 40, edgeMethod: "Canny"), image);
        try
        {
            RequireSuccess(result);
            Require(DeviationArray(result).Length == 40, "Expected 40 deviations.");
            Require(Convert.ToDouble(result.OutputData!["MaxDeviation"]) <= 15.0, "Expected detected pair near expected width.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task AutoDetectSobelPairSuccess()
    {
        using var image = CreateAutoDetectPreferenceImage();
        var result = await ExecuteAsync(CreateOperator(expectedWidth: 24, tolerance: 4, samples: 40, edgeMethod: "Sobel"), image);
        try
        {
            RequireSuccess(result);
            Require(DeviationArray(result).Length == 40, "Expected 40 deviations.");
            Require(Convert.ToDouble(result.OutputData!["MaxDeviation"]) <= 15.0, "Expected Sobel detected pair near expected width.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task AutoDetectBlankWithoutLinesFails()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(CreateOperator(), image);
        Require(!result.IsSuccess, "Blank image without lines should fail.");
        RequireContains(result.ErrorMessage, "Failed to resolve Line1/Line2");
    }

    private static async Task MissingImageFails()
    {
        var result = await Operator.ExecuteAsync(CreateOperator(), []);
        Require(!result.IsSuccess, "Missing image should fail.");
        RequireContains(result.ErrorMessage, "Input image is required");
    }

    private static async Task DegenerateLineFails()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(),
            image,
            new LineData(20, 20, 20, 20),
            new LineData(20, 40, 120, 40));

        Require(!result.IsSuccess, "Degenerate Line1 should fail.");
        RequireContains(result.ErrorMessage, "degenerate");
    }

    private static async Task DictStartEndLineParse()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1, samples: 25),
            image,
            new Dictionary<string, object> { ["StartX"] = 10, ["StartY"] = 20, ["EndX"] = 110, ["EndY"] = 20 },
            new Dictionary<string, object> { ["StartX"] = 10, ["StartY"] = 40, ["EndX"] = 110, ["EndY"] = 40 });

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 25);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task DictX1Y1LineParse()
    {
        using var image = CreateBlankImage();
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1, samples: 25),
            image,
            new Dictionary<string, object> { ["X1"] = 10, ["Y1"] = 20, ["X2"] = 110, ["Y2"] = 20 },
            new Dictionary<string, object> { ["X1"] = 10, ["Y1"] = 40, ["X2"] = 110, ["Y2"] = 40 });

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 25);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task LegacyHashtableLineParse()
    {
        using var image = CreateBlankImage();
        var line1 = new Hashtable { ["StartX"] = 10, ["StartY"] = 20, ["EndX"] = 110, ["EndY"] = 20 };
        var line2 = new Hashtable { ["StartX"] = 10, ["StartY"] = 40, ["EndX"] = 110, ["EndY"] = 40 };
        var result = await ExecuteAsync(CreateOperator(expectedWidth: 20, tolerance: 1, samples: 25), image, line1, line2);

        try
        {
            AssertSuccess(result, expectedDefects: 0, expectedMaxDeviation: 0, expectedDeviationCount: 25);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static Task ValidateDefaultsValid()
    {
        var validation = Operator.ValidateParameters(CreateOperator());
        Require(validation.IsValid, ValidationErrors(validation) ?? "Default parameters should validate.");
        return Task.CompletedTask;
    }

    private static Task ValidateNegativeExpectedInvalid()
    {
        var validation = Operator.ValidateParameters(CreateOperator(expectedWidth: -1));
        Require(!validation.IsValid, "Negative ExpectedWidth should be invalid.");
        RequireContains(ValidationErrors(validation), "ExpectedWidth");
        return Task.CompletedTask;
    }

    private static Task ValidateNegativeToleranceInvalid()
    {
        var validation = Operator.ValidateParameters(CreateOperator(tolerance: -1));
        Require(!validation.IsValid, "Negative Tolerance should be invalid.");
        RequireContains(ValidationErrors(validation), "Tolerance");
        return Task.CompletedTask;
    }

    private static Task ValidateBadEdgeMethodInvalid()
    {
        var validation = Operator.ValidateParameters(CreateOperator(edgeMethod: "Laplacian"));
        Require(!validation.IsValid, "Bad edge method should be invalid.");
        RequireContains(ValidationErrors(validation), "EdgeMethod");
        return Task.CompletedTask;
    }

    private static Task BuildEdgeMapCannyNonzero()
    {
        using var image = CreateAutoDetectPreferenceImage();
        using var edge = BuildEdgeMap(image.MatReadOnly, "Canny");
        Require(Cv2.CountNonZero(edge) > 0, "Canny edge map should contain edges.");
        return Task.CompletedTask;
    }

    private static Task BuildEdgeMapSobelNonzero()
    {
        using var image = CreateAutoDetectPreferenceImage();
        using var edge = BuildEdgeMap(image.MatReadOnly, "Sobel");
        Require(Cv2.CountNonZero(edge) > 0, "Sobel edge map should contain edges.");
        return Task.CompletedTask;
    }

    private static Task DistancePointToLineHorizontal()
    {
        var distance = DistancePointToLine(10, 25, new LineData(0, 20, 100, 20));
        RequireClose(distance, 5, "Distance to horizontal line");
        return Task.CompletedTask;
    }

    private static Task AngleDiffWraps180()
    {
        var diff = AngleDiff(179, -179);
        RequireClose(diff, 2, "Angle diff wrap");
        return Task.CompletedTask;
    }

    private static Task TryParseLineRejectsBadDict()
    {
        var ok = TryParseLine(new Dictionary<string, object> { ["StartX"] = 0, ["StartY"] = 0 });
        Require(!ok, "Incomplete line dictionary should be rejected.");
        return Task.CompletedTask;
    }

    private static async Task OutputImageIsColorAndSameSize()
    {
        using var image = CreateBlankImage(width: 140, height: 120);
        var result = await ExecuteAsync(
            CreateOperator(expectedWidth: 20, tolerance: 1, samples: 25),
            image,
            new LineData(10, 20, 110, 20),
            new LineData(10, 40, 110, 40));

        try
        {
            RequireSuccess(result);
            var outputImage = GetOutput<ImageWrapper>(result.OutputData!, "Image").MatReadOnly;
            Require(outputImage.Type() == MatType.CV_8UC3, $"Expected CV_8UC3 output image, got {outputImage.Type()}.");
            Require(outputImage.Width == 140 && outputImage.Height == 120, $"Unexpected output image size {outputImage.Width}x{outputImage.Height}.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task<ClearVision.Product.Core.Operators.OperatorExecutionOutput> ExecuteAsync(
        Operator op,
        ImageWrapper image,
        object? line1 = null,
        object? line2 = null)
    {
        var inputs = new Dictionary<string, object> { ["Image"] = image };
        if (line1 is not null)
        {
            inputs["Line1"] = line1;
        }

        if (line2 is not null)
        {
            inputs["Line2"] = line2;
        }

        return await Operator.ExecuteAsync(op, inputs);
    }

    private static Operator CreateOperator(
        double expectedWidth = 20,
        double tolerance = 2,
        int samples = 100,
        string edgeMethod = "Canny")
    {
        var op = new Operator("edge_pair_defect_contract", OperatorType.EdgePairDefect, 0, 0);
        AddParameter(op, "ExpectedWidth", expectedWidth, "double");
        AddParameter(op, "Tolerance", tolerance, "double");
        AddParameter(op, "NumSamples", samples, "int");
        AddParameter(op, "EdgeMethod", edgeMethod, "enum");
        return op;
    }

    private static void AddParameter(Operator op, string name, object? value, string dataType)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value));
    }

    private static ImageWrapper CreateBlankImage(int width = 140, int height = 120)
    {
        return new ImageWrapper(new Mat(height, width, MatType.CV_8UC3, Scalar.Black));
    }

    private static ImageWrapper CreateAutoDetectPreferenceImage()
    {
        var mat = new Mat(160, 240, MatType.CV_8UC3, Scalar.Black);
        Cv2.Line(mat, new Point(20, 30), new Point(220, 30), Scalar.White, 2);
        Cv2.Line(mat, new Point(20, 54), new Point(220, 54), Scalar.White, 2);
        Cv2.Line(mat, new Point(20, 118), new Point(220, 118), Scalar.White, 2);
        return new ImageWrapper(mat);
    }

    private static void AssertSuccess(
        ClearVision.Product.Core.Operators.OperatorExecutionOutput result,
        int expectedDefects,
        double expectedMaxDeviation,
        int expectedDeviationCount,
        double tolerance = 1e-3)
    {
        RequireSuccess(result);
        Require(Convert.ToInt32(result.OutputData!["DefectCount"]) == expectedDefects, $"Expected DefectCount={expectedDefects}, got {result.OutputData["DefectCount"]}.");
        RequireClose(Convert.ToDouble(result.OutputData["MaxDeviation"]), expectedMaxDeviation, "MaxDeviation", tolerance);
        Require(DeviationArray(result).Length == expectedDeviationCount, $"Expected {expectedDeviationCount} deviations.");
    }

    private static void RequireSuccess(ClearVision.Product.Core.Operators.OperatorExecutionOutput result)
    {
        Require(result.IsSuccess, result.ErrorMessage ?? "Expected operator execution success.");
        Require(result.OutputData is not null, "Expected output data.");
    }

    private static double[] DeviationArray(ClearVision.Product.Core.Operators.OperatorExecutionOutput result)
    {
        var raw = GetOutput<object>(result.OutputData!, "Deviations");
        if (raw is IEnumerable<double> typed)
        {
            return typed.ToArray();
        }

        if (raw is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(Convert.ToDouble).ToArray();
        }

        throw new InvalidOperationException($"Unexpected Deviations type: {raw.GetType().Name}.");
    }

    private static void RequireAllDeviations(ClearVision.Product.Core.Operators.OperatorExecutionOutput result, double expected)
    {
        foreach (var deviation in DeviationArray(result))
        {
            RequireClose(deviation, expected, "Deviation");
        }
    }

    private static T GetOutput<T>(Dictionary<string, object> output, string key)
    {
        Require(output.TryGetValue(key, out var value), $"Missing output key '{key}'.");
        Require(value is T, $"Output key '{key}' expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}.");
        return (T)value!;
    }

    private static Mat BuildEdgeMap(Mat image, string edgeMethod)
    {
        return (Mat)InvokeStatic("BuildEdgeMap", image, edgeMethod)!;
    }

    private static double DistancePointToLine(double px, double py, LineData line)
    {
        return (double)InvokeStatic("DistancePointToLine", px, py, line)!;
    }

    private static double AngleDiff(float a, float b)
    {
        return (double)InvokeStatic("AngleDiff", a, b)!;
    }

    private static bool TryParseLine(object raw)
    {
        object?[] args = [raw, new LineData()];
        return (bool)InvokeStatic("TryParseLine", args)!;
    }

    private static object? InvokeStatic(string name, params object?[] args)
    {
        var method = typeof(EdgePairDefectOperator).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(EdgePairDefectOperator), name);
        return method.Invoke(null, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string? value, string expected)
    {
        Require(value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true, $"Expected '{value}' to contain '{expected}'.");
    }

    private static void RequireClose(double actual, double expected, string label, double tolerance = 1e-3)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static string? ValidationErrors(ClearVision.Product.Core.Operators.ValidationResult validation)
    {
        return validation.Errors.Count == 0 ? null : string.Join("; ", validation.Errors);
    }

    private static void DisposeOutputImages(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var image in outputData.Values.OfType<ImageWrapper>())
        {
            image.Dispose();
        }
    }

    private static string FormatException(Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: not null } tie)
        {
            var inner = tie.InnerException!;
            return $"{ex.GetType().Name}: {ex.Message} Inner={inner.GetType().Name}: {inner.Message}";
        }

        return ex.GetType().Name + ": " + ex.Message;
    }
}

internal sealed record ContractCase(string Name, string Scenario, Func<Task> Body);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    OperatorEvidence[] Operators,
    ScenarioSummary[] Scenarios,
    CaseResult[] Cases);

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
    string Case,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? Failure);

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var outputPath = "quality/evals/reports/EdgePairDefect_contract_baseline.json";
        string? reportPath = "quality/evals/reports/EdgePairDefect_contract_baseline.md";
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
                    if (++i >= args.Length)
                    {
                        return new RunnerOptions(outputPath, reportPath, false, "--output requires a path.");
                    }

                    outputPath = args[i];
                    break;
                case "--report":
                    if (++i >= args.Length)
                    {
                        return new RunnerOptions(outputPath, reportPath, false, "--report requires a path.");
                    }

                    reportPath = args[i];
                    break;
                case "--no-report":
                    reportPath = null;
                    break;
                default:
                    return new RunnerOptions(outputPath, reportPath, false, $"Unknown argument: {arg}");
            }
        }

        return new RunnerOptions(outputPath, reportPath, showHelp, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run --project quality/tools/EdgePairDefectContractRunner/EdgePairDefectContractRunner.csproj -- [--output PATH] [--report PATH] [--no-report]");
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# EdgePairDefect Contract Baseline",
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
            $"| Runtime ms | {result.Summary.RuntimeMs:F3} |",
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(s =>
            $"| {s.Scenario} | {s.CaseCount} | {s.Passed} | {s.Failed} | {s.RuntimeMsAvg:F3} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Passed | Runtime ms | Failure |",
            "| --- | --- | --- | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(c =>
            $"| {c.Case} | {c.Scenario} | {c.Passed} | {c.RuntimeMs:F3} | {Escape(c.Failure)} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Notes",
            string.Empty,
            "- This is a synthetic contract baseline for edge-pair spacing inspection using generated line images and direct LineData inputs.",
            "- It validates deviation sign/magnitude, tolerance boundaries, sample counts, Canny/Sobel edge maps, line input formats, auto-detection fallback, output image contract, and parameter failures.",
            "- It does not claim field defect accuracy; real edge-pair robustness should be evaluated with production-like parts and optics."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
