using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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

var result = await GoldenRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(
    options.OutputPath,
    JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"CaliperTool golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var caseDirs = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Where(dir => Path.GetFileName(dir)!.StartsWith("CaliperTool_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var caseResults = new List<CaseResult>();
        foreach (var caseDir in caseDirs)
        {
            caseResults.Add(await RunCaseAsync(caseDir));
        }

        var byOperator = caseResults
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                group.Count() == 0 ? 0 : Math.Round(group.Average(item => item.RuntimeMs), 3),
                group.Count() == 0 ? 0 : Math.Round(group.Max(item => item.RuntimeMs), 3),
                group.Count() == 0 ? 0 : (long)Math.Round(group.Average(item => item.MemoryAllocationBytes))))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                options.CasesRoot,
                caseResults.Count,
                caseResults.Count(item => item.Passed),
                caseResults.Count(item => !item.Passed),
                byOperator.Sum(item => item.MemoryAllocationBytesAvg)),
            byOperator,
            caseResults);
    }

    private static async Task<CaseResult> RunCaseAsync(string caseDir)
    {
        var inputPath = Path.Combine(caseDir, "input.json");
        var expectedPath = Path.Combine(caseDir, "expected.json");

        var inputJson = await ReadJsonAsync(inputPath);
        var expectedJson = await ReadJsonAsync(expectedPath);
        var caseId = inputJson.RequiredString("case_id");
        var operatorName = inputJson.RequiredString("operator");
        var scenario = inputJson.RequiredString("scenario");

        ImageWrapper? imageWrapper = null;
        OperatorExecutionOutput? execution = null;

        try
        {
            var inputsNode = inputJson["inputs"] ?? throw new InvalidOperationException("Missing inputs");
            var imageFile = inputsNode["image"]?.GetValue<string>() ?? "image.png";
            var imagePath = Path.Combine(caseDir, imageFile);
            if (!File.Exists(imagePath))
            {
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, $"Image not found: {imagePath}");
            }

            var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (mat == null || mat.Empty())
            {
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, "Failed to load image.");
            }

            imageWrapper = new ImageWrapper(mat);
            var inputs = new Dictionary<string, object>
            {
                ["Image"] = imageWrapper
            };
            if (TryReadSearchRegion(inputsNode["search_region"], out var searchRegion))
            {
                inputs["SearchRegion"] = searchRegion;
            }

            var op = CreateOperator(inputJson);
            var executor = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var runtimeMs = stopwatch.Elapsed.TotalMilliseconds;
            var memoryBytes = Math.Max(0, allocationAfter - allocationBefore);
            var metrics = Evaluate(execution, expectedJson["expected"], inputJson["params"]);
            var passed = IsPassing(metrics, expectedJson["expected"]);
            ReleaseImageOutputs(execution.OutputData);

            return new CaseResult(
                caseId,
                operatorName,
                scenario,
                inputPath,
                passed,
                Math.Round(runtimeMs, 3),
                memoryBytes,
                passed ? null : FormatFailure(execution, metrics),
                metrics);
        }
        catch (Exception ex)
        {
            return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, ex.Message);
        }
        finally
        {
            imageWrapper?.Dispose();
        }
    }

    private static Operator CreateOperator(JsonNode inputJson)
    {
        var op = new Operator("CaliperTool", OperatorType.CaliperTool, 0, 0);
        var paramsNode = inputJson["params"];
        if (paramsNode is null)
        {
            return op;
        }

        foreach (var prop in paramsNode.AsObject())
        {
            if (prop.Value is null)
            {
                continue;
            }

            var value = prop.Value.GetValue<object>();
            var dataType = value switch
            {
                bool => "bool",
                int => "int",
                long => "int",
                float => "double",
                double => "double",
                string => "string",
                _ => "string"
            };
            op.Parameters.Add(new Parameter(Guid.NewGuid(), prop.Key, prop.Key, string.Empty, dataType, value));
        }

        return op;
    }

    private static Dictionary<string, object> Evaluate(
        OperatorExecutionOutput execution,
        JsonNode? expectedNode,
        JsonNode? paramsNode)
    {
        var expectedSuccess = expectedNode?["is_success"]?.GetValue<bool>() ?? true;
        var expectedError = expectedNode?["expected_error_contains"]?.GetValue<string>();
        var metrics = new Dictionary<string, object>
        {
            ["ExpectedSuccess"] = expectedSuccess,
            ["ActualSuccess"] = execution.IsSuccess,
            ["ExpectedCountFailureCorrectness"] = false,
            ["PolarityCorrect"] = true,
            ["WidthErrorPx"] = double.MaxValue,
            ["EdgePositionErrorPx"] = double.MaxValue,
            ["PairDistanceMaxErrorPx"] = double.MaxValue,
            ["PairCountAccuracy"] = 0.0,
            ["UncertaintyPxCalibration"] = false,
            ["IsFinite"] = false,
            ["SubpixelEnabled"] = paramsNode?["SubpixelAccuracy"]?.GetValue<bool>() ?? false,
        };

        if (!expectedSuccess)
        {
            var failureCorrect = !execution.IsSuccess &&
                (expectedError is null ||
                    (execution.ErrorMessage?.Contains(expectedError, StringComparison.OrdinalIgnoreCase) ?? false));
            metrics["ExpectedCountFailureCorrectness"] = failureCorrect;
            metrics["PolarityCorrect"] = failureCorrect;
            if (failureCorrect)
            {
                metrics["WidthErrorPx"] = 0.0;
                metrics["EdgePositionErrorPx"] = 0.0;
                metrics["PairDistanceMaxErrorPx"] = 0.0;
                metrics["PairCountAccuracy"] = 1.0;
                metrics["UncertaintyPxCalibration"] = true;
                metrics["IsFinite"] = true;
            }

            return metrics;
        }

        if (!execution.IsSuccess || execution.OutputData is null || expectedNode is null)
        {
            return metrics;
        }

        var output = execution.OutputData;
        var expectedWidth = expectedNode["width"]?.GetValue<double>() ?? 0.0;
        var expectedPairCount = expectedNode["pair_count"]?.GetValue<int>() ?? 1;
        var expectedDistances = ReadDoubleArray(expectedNode["pair_distances"]);
        var actualWidth = TryGetDouble(output, "Width", out var widthValue) ? widthValue : double.NaN;
        var actualPairCount = TryGetInt(output, "PairCount", out var pairCountValue) ? pairCountValue : 0;
        var actualDistances = output.TryGetValue("PairDistances", out var distancesObj)
            ? ExtractDoubleList(distancesObj)
            : new List<double>();
        var uncertainty = TryGetDouble(output, "UncertaintyPx", out var uncertaintyValue) ? uncertaintyValue : double.NaN;
        var stdDev = TryGetDouble(output, "DistanceStdDev", out var stdDevValue) ? stdDevValue : double.NaN;
        var samplePitch = TryGetDouble(output, "SamplePitchPx", out var samplePitchValue) ? samplePitchValue : double.NaN;

        var widthError = Math.Abs(actualWidth - expectedWidth);
        var pairDistanceMaxError = PairDistanceMaxError(expectedDistances, actualDistances);
        metrics["ActualWidth"] = Round(actualWidth);
        metrics["ExpectedWidth"] = Round(expectedWidth);
        metrics["WidthErrorPx"] = Round(widthError);
        metrics["EdgePositionErrorPx"] = Round(pairDistanceMaxError);
        metrics["PairDistanceMaxErrorPx"] = Round(pairDistanceMaxError);
        metrics["ActualPairCount"] = actualPairCount;
        metrics["ExpectedPairCount"] = expectedPairCount;
        metrics["PairCountAccuracy"] = actualPairCount == expectedPairCount ? 1.0 : 0.0;
        metrics["ActualDistanceStdDevPx"] = Round(stdDev);
        metrics["ActualUncertaintyPx"] = Round(uncertainty);
        metrics["OutputSamplePitchPx"] = Round(samplePitch);
        metrics["UncertaintyPxCalibration"] = double.IsFinite(uncertainty) && uncertainty >= 0.0;
        metrics["ExpectedCountFailureCorrectness"] = true;
        metrics["IsFinite"] =
            double.IsFinite(actualWidth) &&
            double.IsFinite(widthError) &&
            double.IsFinite(pairDistanceMaxError) &&
            actualDistances.All(double.IsFinite);

        return metrics;
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics, JsonNode? expectedNode)
    {
        var expectedSuccess = metrics.TryGetValue("ExpectedSuccess", out var exp) && exp is bool expBool && expBool;
        if (!expectedSuccess)
        {
            return metrics.TryGetValue("ExpectedCountFailureCorrectness", out var failureCorrect) &&
                failureCorrect is bool b &&
                b;
        }

        if (!BoolMetric(metrics, "ActualSuccess") ||
            !BoolMetric(metrics, "IsFinite") ||
            !BoolMetric(metrics, "UncertaintyPxCalibration"))
        {
            return false;
        }

        if (DoubleMetric(metrics, "PairCountAccuracy") < 1.0)
        {
            return false;
        }

        var widthTolerance = expectedNode?["width_tolerance_px"]?.GetValue<double>() ?? 1.0;
        var pairTolerance = expectedNode?["pair_distance_tolerance_px"]?.GetValue<double>() ?? widthTolerance;

        if (DoubleMetric(metrics, "WidthErrorPx") > widthTolerance)
        {
            return false;
        }

        if (DoubleMetric(metrics, "PairDistanceMaxErrorPx") > pairTolerance)
        {
            return false;
        }

        return true;
    }

    private static string FormatFailure(OperatorExecutionOutput? execution, IReadOnlyDictionary<string, object> metrics)
    {
        if (execution is not null && !execution.IsSuccess)
        {
            return execution.ErrorMessage ?? "Execution failed.";
        }

        var keys = new[]
        {
            "WidthErrorPx",
            "PairDistanceMaxErrorPx",
            "PairCountAccuracy",
            "ExpectedCountFailureCorrectness",
            "UncertaintyPxCalibration",
            "IsFinite"
        };
        return string.Join(", ", keys.Where(metrics.ContainsKey).Select(key => $"{key}={FormatValue(metrics[key])}"));
    }

    private static bool TryReadSearchRegion(JsonNode? node, out Rect rect)
    {
        rect = default;
        if (node is null)
        {
            return false;
        }

        var x = node["X"]?.GetValue<int>() ?? node["x"]?.GetValue<int>() ?? 0;
        var y = node["Y"]?.GetValue<int>() ?? node["y"]?.GetValue<int>() ?? 0;
        var width = node["Width"]?.GetValue<int>() ?? node["width"]?.GetValue<int>() ?? 0;
        var height = node["Height"]?.GetValue<int>() ?? node["height"]?.GetValue<int>() ?? 0;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        rect = new Rect(x, y, width, height);
        return true;
    }

    private static async Task<JsonNode> ReadJsonAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
    }

    private static IReadOnlyList<double> ReadDoubleArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return Array.Empty<double>();
        }

        return array
            .Select(item => item?.GetValue<double>() ?? double.NaN)
            .ToList();
    }

    private static List<double> ExtractDoubleList(object? value)
    {
        if (value is null || value is string)
        {
            return new List<double>();
        }

        if (value is IEnumerable<double> doubles)
        {
            return doubles.ToList();
        }

        if (value is IEnumerable enumerable)
        {
            var values = new List<double>();
            foreach (var item in enumerable)
            {
                if (item is IConvertible convertible)
                {
                    values.Add(Convert.ToDouble(convertible));
                }
            }

            return values;
        }

        return new List<double>();
    }

    private static double PairDistanceMaxError(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        if (expected.Count != actual.Count)
        {
            return double.MaxValue;
        }

        var max = 0.0;
        for (var i = 0; i < expected.Count; i++)
        {
            max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        }

        return max;
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, object> output, string key, out double value)
    {
        value = double.NaN;
        if (!output.TryGetValue(key, out var raw) || raw is not IConvertible convertible)
        {
            return false;
        }

        value = Convert.ToDouble(convertible);
        return true;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object> output, string key, out int value)
    {
        value = 0;
        if (!output.TryGetValue(key, out var raw) || raw is not IConvertible convertible)
        {
            return false;
        }

        value = Convert.ToInt32(convertible);
        return true;
    }

    private static bool BoolMetric(IReadOnlyDictionary<string, object> metrics, string key)
    {
        return metrics.TryGetValue(key, out var raw) && raw is bool value && value;
    }

    private static double DoubleMetric(IReadOnlyDictionary<string, object> metrics, string key)
    {
        return metrics.TryGetValue(key, out var raw) && raw is IConvertible convertible
            ? Convert.ToDouble(convertible)
            : double.NaN;
    }

    private static double Round(double value)
    {
        return double.IsFinite(value) ? Math.Round(value, 6) : value;
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

    private static string FormatValue(object value)
    {
        return value switch
        {
            double d => double.IsFinite(d) ? d.ToString("0.####") : d.ToString(),
            float f => float.IsFinite(f) ? f.ToString("0.####") : f.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# CaliperTool Golden Runner Report",
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
            "| Case | Scenario | Passed | Runtime Ms | Width Error Px | Pair Count | Error |",
            "|---|---|---|---:|---:|---:|---|"
        ]);

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("WidthErrorPx", out var widthError);
            item.Metrics.TryGetValue("ActualPairCount", out var actualPairCount);
            item.Metrics.TryGetValue("ExpectedPairCount", out var expectedPairCount);
            var pairText = actualPairCount is null && expectedPairCount is null
                ? "-"
                : $"{actualPairCount ?? "-"}/{expectedPairCount ?? "-"}";
            lines.Add(
                $"| {item.CaseId} | {item.Scenario} | {BoolToMark(item.Passed)} | {item.RuntimeMs:0.###} | " +
                $"{FormatValue(widthError)} | {pairText} | {item.ErrorMessage ?? "-"} |");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BoolToMark(bool value) => value ? "Yes" : "No";

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "-",
            double d => double.IsFinite(d) ? d.ToString("0.####") : d.ToString(),
            float f => float.IsFinite(f) ? f.ToString("0.####") : f.ToString(),
            _ => value.ToString() ?? "-"
        };
    }
}

internal sealed record RunnerOptions(
    string CasesRoot,
    string OutputPath,
    string? ReportPath,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var casesRoot = "quality/synthetic/cases/caliper_tool";
        var output = "quality/evals/reports/CaliperTool_baseline.json";
        string? report = "quality/evals/reports/CaliperTool_baseline.md";
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
                case "--cases-root":
                    casesRoot = NextValue(args, ref i, arg, ref parseError);
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

        if (!showHelp && !Directory.Exists(casesRoot))
        {
            parseError ??= $"Cases root does not exist: {casesRoot}";
        }

        return new RunnerOptions(casesRoot, output, report, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            CaliperTool golden runner

            Options:
              --cases-root <dir>   Directory containing generated case folders.
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
    string InputPath,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object> Metrics)
{
    public static CaseResult Failed(
        string caseId,
        string operatorName,
        string scenario,
        string inputPath,
        double runtimeMs,
        long memoryAllocationBytes,
        string errorMessage)
    {
        return new CaseResult(
            caseId,
            operatorName,
            scenario,
            inputPath,
            false,
            Math.Round(runtimeMs, 3),
            memoryAllocationBytes,
            errorMessage,
            new Dictionary<string, object>());
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}

internal static class JsonExtensions
{
    public static string RequiredString(this JsonNode node, string propertyName)
    {
        return node[propertyName]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Missing required string: {propertyName}");
    }
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
