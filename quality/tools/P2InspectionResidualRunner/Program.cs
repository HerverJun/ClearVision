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

var result = await P2InspectionResidualRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"P2 inspection residual baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class P2InspectionResidualRunner
{
    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<RunnerCase>();
        AddDetectionSequenceJudgeCases(cases);
        AddSurfaceDefectDetectionCases(cases);

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

    private static void AddDetectionSequenceJudgeCases(List<RunnerCase> cases)
    {
        var sut = new DetectionSequenceJudgeOperator(NullLogger<DetectionSequenceJudgeOperator>.Instance);

        for (var i = 0; i < 6; i++)
        {
            var index = i;
            Add(cases, "DetectionSequenceJudge", $"expected_order_{index:00}", "Expected order oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateJudgeOperator("Wire_Brown,Wire_Black,Wire_Blue"),
                    Inputs(("Detections", new DetectionList(CreateDetections(
                        ("Wire_Black", 0.96f, 30f + index),
                        ("Wire_Blue", 0.94f, 50f + index),
                        ("Wire_Brown", 0.98f, 10f + index))))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireSequence(result, "ActualOrder", "Wire_Brown", "Wire_Black", "Wire_Blue");
                return Observed(("ActualOrder", "Wire_Brown,Wire_Black,Wire_Blue"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "DetectionSequenceJudge", $"wrong_order_{i:00}", "Order mismatch contract", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateJudgeOperator("Wire_Brown,Wire_Black,Wire_Blue"),
                    Inputs(("Detections", new DetectionList(CreateDetections(
                        ("Wire_Brown", 0.98f, 10f),
                        ("Wire_Blue", 0.94f, 30f),
                        ("Wire_Black", 0.96f, 50f))))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", false);
                RequireStringContains(result, "Message", "Order mismatch");
                return Observed(("FailureReason", "OrderMismatch"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "DetectionSequenceJudge", $"missing_label_{i:00}", "Missing label contract", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateJudgeOperator("Wire_Brown,Wire_Black,Wire_Blue"),
                    Inputs(("Detections", new DetectionList(CreateDetections(
                        ("Wire_Brown", 0.98f, 10f),
                        ("Wire_Blue", 0.94f, 30f))))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", false);
                RequireSequence(result, "MissingLabels", "Wire_Black");
                RequireStringContains(result, "Message", "Missing labels");
                return Observed(("FailureReason", "MissingLabel"));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "DetectionSequenceJudge", $"allow_missing_{i:00}", "Allow-missing subsequence contract", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateJudgeOperator("Wire_Brown,Wire_Black,Wire_Blue", allowMissing: true),
                    Inputs(("Detections", new DetectionList(CreateDetections(
                        ("Wire_Brown", 0.98f, 10f),
                        ("Wire_Blue", 0.94f, 30f))))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireSequence(result, "MissingLabels", "Wire_Black");
                return Observed(("AllowMissing", true));
            });
        }

        for (var i = 0; i < 2; i++)
        {
            Add(cases, "DetectionSequenceJudge", $"row_cluster_{i:00}", "Row cluster ordering oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateJudgeOperator(
                        "Wire_TL,Wire_TR,Wire_BL,Wire_BR",
                        direction: "LeftToRight",
                        groupingMode: "RowCluster",
                        rowTolerance: 10.0),
                    Inputs(("Detections", new DetectionList(CreateDetections(
                        ("Wire_TL", 0.98f, 10f, 10f, 8f, 8f),
                        ("Wire_BL", 0.96f, 12f, 40f, 8f, 8f),
                        ("Wire_TR", 0.97f, 42f, 12f, 8f, 8f),
                        ("Wire_BR", 0.95f, 44f, 42f, 8f, 8f))))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireSequence(result, "ActualOrder", "Wire_TL", "Wire_TR", "Wire_BL", "Wire_BR");
                RequireIntEquals(result, "RowCount", 2);
                return Observed(("GroupingMode", "RowCluster"));
            });
        }

        for (var i = 0; i < 2; i++)
        {
            Add(cases, "DetectionSequenceJudge", $"slot_assignment_{i:00}", "Slot assignment oracle", async () =>
            {
                var result = await sut.ExecuteAsync(
                    CreateJudgeOperator(
                        "Wire_A,Wire_B,Wire_C,Wire_D",
                        direction: "LeftToRight",
                        groupingMode: "SlotAssignment",
                        expectedSlots: "10:10;30:10;10:30;30:30",
                        rowTolerance: 8.0,
                        slotTolerance: 12.0),
                    Inputs(("Detections", new DetectionList(CreateDetections(
                        ("Wire_A", 0.98f, 10f, 10f, 8f, 8f),
                        ("Wire_C", 0.96f, 10f, 30f, 8f, 8f),
                        ("Wire_B", 0.97f, 30f, 10f, 8f, 8f),
                        ("Wire_D", 0.95f, 30f, 30f, 8f, 8f))))));

                RequireSuccess(result);
                RequireBool(result, "IsMatch", true);
                RequireSequence(result, "ActualOrder", "Wire_A", "Wire_B", "Wire_C", "Wire_D");
                return Observed(("GroupingMode", "SlotAssignment"));
            });
        }

        Add(cases, "DetectionSequenceJudge", "invalid_slot_points", "Invalid slot points failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(
                CreateJudgeOperator("Wire_1", groupingMode: "SlotAssignment"),
                Inputs(
                    ("Detections", new DetectionList(new[] { CreateDetection("Wire_1", new Point2f(10, 10)) })),
                    ("SlotPoints", "[{\"x\":10}]")));

            RequireFailure(result, "SlotPoints input contains invalid point data");
            return Observed(("FailureReason", "InvalidSlotPoints"));
        });

        Add(cases, "DetectionSequenceJudge", "missing_detections", "Missing detections failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateJudgeOperator("Wire_1"), null);
            RequireFailure(result, "Missing Detections input");
            return Observed(("FailureReason", "MissingDetections"));
        });
    }

    private static void AddSurfaceDefectDetectionCases(List<RunnerCase> cases)
    {
        var sut = new SurfaceDefectDetectionOperator(NullLogger<SurfaceDefectDetectionOperator>.Instance);

        for (var i = 0; i < 6; i++)
        {
            Add(cases, "SurfaceDefectDetection", $"reference_diff_{i:00}", "Reference diff defect oracle", async () =>
            {
                using var source = CreateSourceWithDefect(i);
                using var reference = CreateReferenceImage();
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.SurfaceDefectDetection,
                        ("Method", "ReferenceDiff"),
                        ("Threshold", 10.0),
                        ("MinArea", 20),
                        ("MaxArea", 100000),
                        ("MorphCleanSize", 3),
                        ("ThresholdMode", "Auto"),
                        ("AlignmentMode", "None")),
                    Inputs(("Image", source), ("Reference", reference)));

                RequireSuccess(result);
                RequireAtLeast(RequireInt(result, "DefectCount"), 1, "ReferenceDiff defect count");
                RequireOutput(result, "DefectMask");
                RequireOutput(result, "ResponseImage");
                return Observed(("Method", "ReferenceDiff"), ("DefectCount", RequireInt(result, "DefectCount")));
            });
        }

        for (var i = 0; i < 6; i++)
        {
            Add(cases, "SurfaceDefectDetection", $"gradient_scratch_{i:00}", "Gradient scratch oracle", async () =>
            {
                using var source = CreateScratchImage(i);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.SurfaceDefectDetection,
                        ("Method", "GradientMagnitude"),
                        ("Threshold", 35.0),
                        ("ThresholdMode", "Manual"),
                        ("MinArea", 5),
                        ("MaxArea", 100000),
                        ("MorphCleanSize", 1),
                        ("NormalizationMode", "None")),
                    Inputs(("Image", source)));

                RequireSuccess(result);
                RequireAtLeast(RequireInt(result, "DefectCount"), 1, "Gradient defect count");
                return Observed(("Method", "GradientMagnitude"), ("DefectCount", RequireInt(result, "DefectCount")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "SurfaceDefectDetection", $"local_contrast_{i:00}", "Local contrast defect oracle", async () =>
            {
                using var source = CreateLocalContrastSpot(i);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.SurfaceDefectDetection,
                        ("Method", "LocalContrast"),
                        ("Threshold", 20.0),
                        ("ThresholdMode", "Manual"),
                        ("MinArea", 8),
                        ("MaxArea", 100000),
                        ("MorphCleanSize", 1),
                        ("BackgroundKernelSize", 31)),
                    Inputs(("Image", source)));

                RequireSuccess(result);
                RequireAtLeast(RequireInt(result, "DefectCount"), 1, "LocalContrast defect count");
                return Observed(("Method", "LocalContrast"), ("DefectCount", RequireInt(result, "DefectCount")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "SurfaceDefectDetection", $"shifted_reference_{i:00}", "Phase-correlation alignment oracle", async () =>
            {
                using var reference = CreateStructuredReference();
                using var source = CreateShiftedSourceWithDefect(i);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.SurfaceDefectDetection,
                        ("Method", "ReferenceDiff"),
                        ("Threshold", 20.0),
                        ("ThresholdMode", "ReferenceStats"),
                        ("AlignmentMode", "PhaseCorrelation"),
                        ("NormalizationMode", "LocalMean"),
                        ("MinArea", 10),
                        ("MaxArea", 200000)),
                    Inputs(("Image", source), ("Reference", reference)));

                RequireSuccess(result);
                RequireString(result, "RejectedReason", string.Empty);
                RequireAtLeast(RequireDouble(result, "AlignmentScore"), 0.0, "Alignment score");
                return Observed(("AlignmentScore", RequireDouble(result, "AlignmentScore")));
            });
        }

        for (var i = 0; i < 2; i++)
        {
            Add(cases, "SurfaceDefectDetection", $"missing_image_{i:00}", "Missing image failure contract", async () =>
            {
                var result = await sut.ExecuteAsync(CreateOperator(OperatorType.SurfaceDefectDetection), null);
                RequireFailure(result, "Input image is required");
                return Observed(("FailureReason", "MissingImage"));
            });
        }

        for (var i = 0; i < 2; i++)
        {
            Add(cases, "SurfaceDefectDetection", $"invalid_method_{i:00}", "Parameter validation contract", () =>
            {
                var validation = sut.ValidateParameters(
                    CreateOperator(OperatorType.SurfaceDefectDetection, ("Method", "PhaseOnly")));
                RequireInvalid(validation, "Method must be GradientMagnitude");
                return Task.FromResult(Observed(("FailureReason", "InvalidMethod")));
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

    private static Operator CreateJudgeOperator(
        string expectedLabels,
        double minConfidence = 0.0,
        string sortBy = "CenterX",
        string direction = "Ascending",
        string groupingMode = "SingleRow",
        string expectedSlots = "",
        double rowTolerance = 0.0,
        double slotTolerance = 0.0,
        bool allowMissing = false,
        bool allowDuplicate = false)
    {
        return CreateOperator(
            OperatorType.DetectionSequenceJudge,
            ("ExpectedLabels", expectedLabels),
            ("SortBy", sortBy),
            ("Direction", direction),
            ("ExpectedCount", 0),
            ("MinConfidence", minConfidence),
            ("AllowMissing", allowMissing),
            ("AllowDuplicate", allowDuplicate),
            ("GroupingMode", groupingMode),
            ("ExpectedSlots", expectedSlots),
            ("RowTolerance", rowTolerance),
            ("SlotTolerance", slotTolerance));
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

    private static IEnumerable<DetectionResult> CreateDetections(params (string Label, float Confidence, float X)[] items)
    {
        return items.Select(item => new DetectionResult(item.Label, item.Confidence, item.X, 10f, 8f, 8f));
    }

    private static IEnumerable<DetectionResult> CreateDetections(params (string Label, float Confidence, float X, float Y, float Width, float Height)[] items)
    {
        return items.Select(item => new DetectionResult(item.Label, item.Confidence, item.X, item.Y, item.Width, item.Height));
    }

    private static DetectionResult CreateDetection(string label, Point2f center)
    {
        return new DetectionResult(label, 0.95f, center.X - 4, center.Y - 4, 8, 8);
    }

    private static ImageWrapper CreateSourceWithDefect(int index)
    {
        var mat = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(35 + index, 40, 30, 30), Scalar.White, -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateReferenceImage()
    {
        var mat = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateScratchImage(int index)
    {
        var mat = new Mat(140, 140, MatType.CV_8UC3, new Scalar(80, 80, 80));
        Cv2.Line(mat, new Point(18, 30 + index), new Point(118, 92 + index), Scalar.White, 3);
        Cv2.Circle(mat, new Point(95, 35), 8, new Scalar(150, 150, 150), -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateLocalContrastSpot(int index)
    {
        var mat = new Mat(140, 140, MatType.CV_8UC3, new Scalar(96, 96, 96));
        Cv2.Circle(mat, new Point(56 + index, 72), 10, new Scalar(180, 180, 180), -1);
        Cv2.Rectangle(mat, new Rect(88, 42, 14, 16), new Scalar(30, 30, 30), -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateStructuredReference()
    {
        var mat = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(40, 40, 50, 30), Scalar.White, -1);
        Cv2.Circle(mat, new Point(110, 100), 14, new Scalar(180, 180, 180), -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateShiftedSourceWithDefect(int index)
    {
        using var reference = CreateStructuredReference();
        var source = new Mat();
        using var transform = new Mat(2, 3, MatType.CV_64FC1, Scalar.All(0));
        transform.Set(0, 0, 1.0);
        transform.Set(1, 1, 1.0);
        transform.Set(0, 2, 4.0 + (index * 0.2));
        transform.Set(1, 2, -3.0);
        Cv2.WarpAffine(reference.MatReadOnly, source, transform, reference.MatReadOnly.Size(), InterpolationFlags.Linear, BorderTypes.Constant);
        Cv2.Rectangle(source, new Rect(120, 30, 12, 12), Scalar.White, -1);
        return new ImageWrapper(source);
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
        var actual = result.OutputData![key]?.ToString() ?? string.Empty;
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {key}='{expected}', got '{actual}'.");
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

    private static double RequireDouble(OperatorExecutionOutput result, string key)
    {
        RequireOutput(result, key);
        return Convert.ToDouble(result.OutputData![key]);
    }

    private static void RequireIntEquals(OperatorExecutionOutput result, string key, int expected)
    {
        var actual = RequireInt(result, key);
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected {key}={expected}, got {actual}.");
        }
    }

    private static void RequireAtLeast(int actual, int min, string label)
    {
        if (actual < min)
        {
            throw new InvalidOperationException($"{label} {actual} is below {min}.");
        }
    }

    private static void RequireAtLeast(double actual, double min, string label)
    {
        if (!double.IsFinite(actual) || actual < min)
        {
            throw new InvalidOperationException($"{label} {actual} is below {min}.");
        }
    }

    private static void RequireSequence(OperatorExecutionOutput result, string key, params string[] expected)
    {
        RequireOutput(result, key);
        var actual = ((IEnumerable<object>)result.OutputData![key]).Select(item => item.ToString() ?? string.Empty).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Expected {key}=[{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
        }
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# P2 Inspection Residual Baseline",
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
        var outputPath = "quality/evals/reports/P2InspectionResidual_baseline.json";
        var reportPath = "quality/evals/reports/P2InspectionResidual_baseline.md";

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
            Usage: dotnet run --project quality/tools/P2InspectionResidualRunner/P2InspectionResidualRunner.csproj -- [options]

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
