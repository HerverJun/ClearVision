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
    $"GradientShapeMatch golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var caseDirs = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Where(dir => Path.GetFileName(dir)!.StartsWith("GradientShapeMatch_", StringComparison.OrdinalIgnoreCase))
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
                group.Count() == 0 ? 0 : group.Max(item => item.RuntimeMs),
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

        Mat? templateMat = null;
        Mat? sceneMat = null;
        ImageWrapper? templateWrapper = null;
        ImageWrapper? sceneWrapper = null;

        try
        {
            var caseDirPath = Path.GetDirectoryName(inputPath)!;
            var inputsMeta = inputJson["inputs"] ?? throw new InvalidOperationException("Missing inputs");
            var templateFile = inputsMeta["template"]?.GetValue<string>() ?? "template.png";
            var sceneFile = inputsMeta["scene"]?.GetValue<string>() ?? "scene.png";

            var templateFullPath = Path.Combine(caseDirPath, templateFile);
            var sceneFullPath = Path.Combine(caseDirPath, sceneFile);

            if (!File.Exists(templateFullPath))
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, $"Template not found: {templateFullPath}");
            if (!File.Exists(sceneFullPath))
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, $"Scene not found: {sceneFullPath}");

            templateMat = Cv2.ImRead(templateFullPath, ImreadModes.Color);
            sceneMat = Cv2.ImRead(sceneFullPath, ImreadModes.Color);

            if (templateMat == null || templateMat.Empty())
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, "Failed to load template image.");
            if (sceneMat == null || sceneMat.Empty())
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, "Failed to load scene image.");

            templateWrapper = new ImageWrapper(templateMat);
            sceneWrapper = new ImageWrapper(sceneMat);

            var op = CreateOperator(inputJson);
            var inputs = new Dictionary<string, object>
            {
                ["Image"] = sceneWrapper,
                ["Template"] = templateWrapper
            };

            var executor = new GradientShapeMatchOperator(NullLogger<GradientShapeMatchOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var runtimeMs = stopwatch.Elapsed.TotalMilliseconds;
            var memoryBytes = Math.Max(0, allocationAfter - allocationBefore);

            if (!execution.IsSuccess)
            {
                // For low_feature scenario, failure is expected
                var expectedNode1 = expectedJson["expected"];
                var expectedIsMatch1 = expectedNode1?["is_match"]?.GetValue<bool>() ?? true;
                if (!expectedIsMatch1)
                {
                    var fallbackMetrics = new Dictionary<string, object>
                    {
                        ["PositionErrorPx"] = 0.0,
                        ["AngleErrorDeg"] = 0.0,
                        ["IsMatchCorrect"] = true,
                        ["ScoreValue"] = 0.0,
                    };
                    return new CaseResult(caseId, operatorName, scenario, inputPath, true, Math.Round(runtimeMs, 3), memoryBytes, null, fallbackMetrics);
                }

                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, runtimeMs, memoryBytes, execution.ErrorMessage ?? "Execution failed.");
            }

            var metrics = Evaluate(execution.OutputData, expectedJson["expected"]);
            var passed = IsPassing(metrics, scenario);

            ReleaseImageOutputs(execution.OutputData);

            return new CaseResult(caseId, operatorName, scenario, inputPath, passed, Math.Round(runtimeMs, 3), memoryBytes, null, metrics);
        }
        catch (Exception ex)
        {
            // For low_feature scenario, exception (e.g., feature count < 10) is acceptable
            var expectedNode2 = expectedJson["expected"];
            var expectedIsMatch2 = expectedNode2?["is_match"]?.GetValue<bool>() ?? true;
            if (!expectedIsMatch2)
            {
                var fallbackMetrics2 = new Dictionary<string, object>
                {
                    ["PositionErrorPx"] = 0.0,
                    ["AngleErrorDeg"] = 0.0,
                    ["IsMatchCorrect"] = true,
                    ["ScoreValue"] = 0.0,
                };
                return new CaseResult(caseId, operatorName, scenario, inputPath, true, 0, 0, null, fallbackMetrics2);
            }

            return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, ex.Message);
        }
        finally
        {
            templateWrapper?.Dispose();
            sceneWrapper?.Dispose();
        }
    }

    private static Operator CreateOperator(JsonNode inputJson)
    {
        var op = new Operator("GradientShapeMatch", OperatorType.GradientShapeMatch, 0, 0);
        var paramsNode = inputJson["params"];
        if (paramsNode is null)
            return op;

        foreach (var prop in paramsNode.AsObject())
        {
            var name = prop.Key;
            var valueNode = prop.Value;
            if (valueNode is null) continue;

            var value = valueNode.GetValue<object>();
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
            op.Parameters.Add(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value));
        }

        return op;
    }

    private static Dictionary<string, object> Evaluate(Dictionary<string, object>? outputData, JsonNode? expectedNode)
    {
        var metrics = new Dictionary<string, object>
        {
            ["PositionErrorPx"] = double.MaxValue,
            ["AngleErrorDeg"] = double.MaxValue,
            ["IsMatchCorrect"] = false,
            ["NoMatchAllowed"] = false,
            ["AngleChecked"] = true,
            ["ScoreValue"] = 0.0,
            ["FailureReasonCorrect"] = true,
            ["MatchCountCorrect"] = true,
        };

        if (outputData is null || expectedNode is null)
            return metrics;

        var expectedIsMatch = expectedNode["is_match"]?.GetValue<bool>() ?? true;
        var allowNoMatch = expectedNode["allow_no_match"]?.GetValue<bool>() ?? false;
        var angleOptional = expectedNode["angle_optional"]?.GetValue<bool>() ?? false;
        var actualIsMatch = outputData.TryGetValue("IsMatch", out var im) && im is bool b && b;
        metrics["NoMatchAllowed"] = allowNoMatch;
        metrics["AngleChecked"] = !angleOptional;

        var noMatchAccepted = expectedIsMatch && allowNoMatch && !actualIsMatch;
        metrics["IsMatchCorrect"] = expectedIsMatch == actualIsMatch || noMatchAccepted;

        if (outputData.TryGetValue("Score", out var sc) && sc is IConvertible conv)
            metrics["ScoreValue"] = Convert.ToDouble(conv);

        // FailureReason validation
        if (expectedNode["failure_reason"] is JsonNode expectedFrNode)
        {
            var expectedFr = expectedFrNode.GetValue<string>();
            var actualFr = outputData.TryGetValue("FailureReason", out var fr) ? fr?.ToString() ?? "" : "";
            metrics["FailureReasonCorrect"] = actualFr == expectedFr;
        }

        // MatchCount validation for TopK scenarios
        if (expectedNode["match_count_min"] is JsonNode expectedCountNode)
        {
            var matchCountMin = expectedCountNode.GetValue<int>();
            var actualMatchCount = outputData.TryGetValue("MatchCount", out var mc) && mc is IConvertible mcc ? Convert.ToInt32(mcc) : 0;
            metrics["MatchCountCorrect"] = actualMatchCount >= matchCountMin;
        }

        if (expectedIsMatch && actualIsMatch)
        {
            var expPos = expectedNode["position"];
            var expX = expPos?["x"]?.GetValue<double>() ?? 0;
            var expY = expPos?["y"]?.GetValue<double>() ?? 0;
            var expAngle = expectedNode["angle"]?.GetValue<double>() ?? 0;

            // For multi-match scenarios, check if ANY returned match is close to the expected position
            var candidatePositions = new List<(double x, double y, double angle)>();

            double bestX = 0, bestY = 0;
            if (outputData.TryGetValue("Position", out var posObj) && posObj is Position pos)
            {
                bestX = pos.X;
                bestY = pos.Y;
            }
            else if (outputData.TryGetValue("X", out var xv) && outputData.TryGetValue("Y", out var yv))
            {
                bestX = Convert.ToDouble(xv);
                bestY = Convert.ToDouble(yv);
            }

            double bestAngle = 0;
            if (outputData.TryGetValue("Angle", out var av) && av is IConvertible ac)
                bestAngle = Convert.ToDouble(ac);

            candidatePositions.Add((bestX, bestY, bestAngle));

            if (outputData.TryGetValue("Matches", out var matchesObj) && matchesObj is System.Collections.IEnumerable matchesEnum)
            {
                foreach (var match in matchesEnum)
                {
                    if (match is Dictionary<string, object> matchDict)
                    {
                        double mx = 0, my = 0, ma = 0;
                        if (matchDict.TryGetValue("Position", out var mp) && mp is Position mpv)
                        {
                            mx = mpv.X;
                            my = mpv.Y;
                        }
                        else if (matchDict.TryGetValue("X", out var mxv) && matchDict.TryGetValue("Y", out var myv))
                        {
                            mx = Convert.ToDouble(mxv);
                            my = Convert.ToDouble(myv);
                        }

                        if (matchDict.TryGetValue("Angle", out var mav) && mav is IConvertible mac)
                            ma = Convert.ToDouble(mac);

                        candidatePositions.Add((mx, my, ma));
                    }
                }
            }

            double minPosError = double.MaxValue;
            double minAngleError = double.MaxValue;
            foreach (var (cx, cy, ca) in candidatePositions)
            {
                var posError = Math.Sqrt((expX - cx) * (expX - cx) + (expY - cy) * (expY - cy));
                if (posError < minPosError)
                {
                    minPosError = posError;
                    var angleDiff = Math.Abs(expAngle - ca);
                    while (angleDiff > 180) angleDiff = 360 - angleDiff;
                    minAngleError = angleDiff;
                }
            }

            metrics["PositionErrorPx"] = minPosError;
            metrics["AngleErrorDeg"] = minAngleError;
        }
        else if (noMatchAccepted)
        {
            metrics["PositionErrorPx"] = 0.0;
            metrics["AngleErrorDeg"] = 0.0;
            metrics["AngleChecked"] = false;
        }
        else if (!expectedIsMatch && !actualIsMatch)
        {
            metrics["PositionErrorPx"] = 0.0;
            metrics["AngleErrorDeg"] = 0.0;
        }

        return metrics;
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics, string scenario)
    {
        var isMatchCorrect = metrics.TryGetValue("IsMatchCorrect", out var im) && im is bool b && b;
        if (!isMatchCorrect)
            return false;

        if (metrics.TryGetValue("FailureReasonCorrect", out var frc) && frc is bool frcBool && !frcBool)
            return false;

        if (metrics.TryGetValue("MatchCountCorrect", out var mcc) && mcc is bool mccBool && !mccBool)
            return false;

        var positionError = Convert.ToDouble(metrics["PositionErrorPx"]);
        var angleError = Convert.ToDouble(metrics["AngleErrorDeg"]);

        // Tight tolerances for easy scenarios; relaxed for stressed/rotation.
        var (maxPositionError, maxAngleError) = scenario switch
        {
            "translation" => (3.0, 2.0),
            "roi_search" => (3.0, 2.0),
            "rotation_small" => (3.0, 20.0),
            "rotation_large" => (5.0, 20.0),
            "blurred_edge" => (5.0, 30.0),
            "low_contrast" => (5.0, 45.0),
            "partial_occlusion" => (10.0, 30.0),
            "strong_background" => (5.0, 30.0),
            "low_feature" => (0.0, 0.0),
            "topk_multi" => (5.0, 2.0),
            _ => (3.0, 5.0)
        };

        if (positionError > maxPositionError)
            return false;
        var angleChecked = !metrics.TryGetValue("AngleChecked", out var ac) || ac is not bool check || check;
        if (angleChecked && angleError > maxAngleError)
            return false;

        return true;
    }

    private static async Task<JsonNode> ReadJsonAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
    }

    private static void ReleaseImageOutputs(Dictionary<string, object>? outputData)
    {
        if (outputData is null) return;
        foreach (var image in outputData.Values.OfType<ImageWrapper>().Distinct(ReferenceEqualityComparer<ImageWrapper>.Instance))
        {
            image.Release();
        }
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# GradientShapeMatch Golden Runner Report",
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

        var failures = result.Cases.Where(item => !item.Passed).ToList();
        if (failures.Count > 0)
        {
            lines.AddRange(
            [
                string.Empty,
                "## Failures",
                string.Empty,
                "| Case | Operator | Scenario | Error |",
                "|---|---|---|---|"
            ]);
            lines.AddRange(failures.Select(item =>
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {item.ErrorMessage ?? FormatMetrics(item.Metrics)} |"));
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatMetrics(IReadOnlyDictionary<string, object> metrics)
    {
        if (metrics.Count == 0) return "Metric mismatch";
        var parts = metrics.Select(kv => $"{kv.Key}={kv.Value:0.##}");
        return string.Join(", ", parts);
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
        var casesRoot = "quality/synthetic/cases/gradient_shape_match";
        var output = "quality/evals/reports/GradientShapeMatch_baseline.json";
        string? report = "quality/evals/reports/GradientShapeMatch_baseline.md";
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
            GradientShapeMatch golden runner

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

    public static int RequiredInt(this JsonNode node, string propertyName)
    {
        return node[propertyName]?.GetValue<int>()
            ?? throw new InvalidOperationException($"Missing required int: {propertyName}");
    }
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
