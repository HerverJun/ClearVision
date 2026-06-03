using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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

var result = await GoldenRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"TemplateMatching golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var caseDirs = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Where(dir => Path.GetFileName(dir)!.StartsWith("TemplateMatching_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<CaseResult>();
        foreach (var caseDir in caseDirs)
        {
            results.Add(await RunCaseAsync(caseDir));
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
                Math.Round(group.Max(item => item.RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.MemoryAllocationBytes))))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                options.CasesRoot,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                byOperator.Sum(item => item.MemoryAllocationBytesAvg)),
            byOperator,
            results);
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

        ImageWrapper? sceneWrapper = null;
        ImageWrapper? templateWrapper = null;
        ImageWrapper? maskWrapper = null;
        OperatorExecutionOutput? execution = null;

        try
        {
            var inputsNode = inputJson["inputs"] ?? throw new InvalidOperationException("Missing inputs");
            sceneWrapper = LoadImageWrapper(caseDir, inputsNode["image"]?.GetValue<string>() ?? "scene.png", ImreadModes.Color);
            templateWrapper = LoadImageWrapper(caseDir, inputsNode["template"]?.GetValue<string>() ?? "template.png", ImreadModes.Color);

            var inputs = new Dictionary<string, object>
            {
                ["Image"] = sceneWrapper,
                ["Template"] = templateWrapper
            };

            var maskFile = inputsNode["mask"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(maskFile))
            {
                maskWrapper = LoadImageWrapper(caseDir, maskFile, ImreadModes.Grayscale);
                inputs["Mask"] = maskWrapper;
            }

            var op = CreateOperator(inputJson);
            var executor = new TemplateMatchOperator(NullLogger<TemplateMatchOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var runtimeMs = stopwatch.Elapsed.TotalMilliseconds;
            var memoryBytes = Math.Max(0, allocationAfter - allocationBefore);
            var metrics = Evaluate(execution, expectedJson["expected"]);
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
            sceneWrapper?.Dispose();
            templateWrapper?.Dispose();
            maskWrapper?.Dispose();
        }
    }

    private static ImageWrapper LoadImageWrapper(string caseDir, string fileName, ImreadModes mode)
    {
        var path = Path.Combine(caseDir, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Image not found: {path}");
        }

        var mat = Cv2.ImRead(path, mode);
        if (mat == null || mat.Empty())
        {
            throw new InvalidOperationException($"Failed to load image: {path}");
        }

        return new ImageWrapper(mat);
    }

    private static Operator CreateOperator(JsonNode inputJson)
    {
        var op = new Operator("TemplateMatching", OperatorType.TemplateMatching, 0, 0);
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

            var value = ReadParameterValue(prop.Value);
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

    private static object ReadParameterValue(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b)) return b;
            if (value.TryGetValue<int>(out var i)) return i;
            if (value.TryGetValue<long>(out var l)) return l;
            if (value.TryGetValue<double>(out var d)) return d;
            if (value.TryGetValue<string>(out var s)) return s ?? string.Empty;
        }

        return node.ToJsonString();
    }

    private static Dictionary<string, object> Evaluate(OperatorExecutionOutput execution, JsonNode? expectedNode)
    {
        var expectedIsMatch = expectedNode?["is_match"]?.GetValue<bool>() ?? true;
        var expectedMethod = expectedNode?["method"]?.GetValue<string>() ?? string.Empty;
        var expectedMatchCount = expectedNode?["match_count"]?.GetValue<int>() ?? 0;
        var expectedPositions = ReadPositions(expectedNode?["positions"]);
        var allowedPositions = ReadPositions(expectedNode?["allowed_positions"]);
        if (allowedPositions.Count == 0)
        {
            allowedPositions = expectedPositions;
        }

        var metrics = new Dictionary<string, object>
        {
            ["ExpectedIsMatch"] = expectedIsMatch,
            ["ActualIsMatch"] = false,
            ["IsMatchCorrect"] = false,
            ["ExpectedMatchCount"] = expectedMatchCount,
            ["ActualMatchCount"] = 0,
            ["MatchCountCorrect"] = false,
            ["PositionErrorPx"] = double.MaxValue,
            ["AllowedPositionErrorPx"] = double.MaxValue,
            ["ScoreValue"] = 0.0,
            ["NormalizedScoreValue"] = 0.0,
            ["RawResponseValue"] = 0.0,
            ["ScoreContractCorrect"] = false,
            ["NormalizedScoreInRange"] = false,
            ["MinScoreSatisfied"] = !expectedIsMatch,
            ["MethodDescriptorCorrect"] = false,
            ["ExpectedFailureCorrect"] = expectedIsMatch,
            ["NmsDistinct"] = true,
            ["IsFinite"] = false,
        };

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return metrics;
        }

        var output = execution.OutputData;
        var actualIsMatch = output.TryGetValue("IsMatch", out var isMatchObj) && isMatchObj is bool b && b;
        var actualMatchCount = TryGetInt(output, "MatchCount", out var matchCountValue) ? matchCountValue : 0;
        var score = TryGetDouble(output, "Score", out var scoreValue) ? scoreValue : 0.0;
        var normalized = TryGetDouble(output, "NormalizedScore", out var normalizedValue) ? normalizedValue : 0.0;
        var raw = TryGetDouble(output, "RawResponse", out var rawValue) ? rawValue : 0.0;
        var method = output.TryGetValue("Method", out var methodObj) ? methodObj?.ToString() ?? string.Empty : string.Empty;
        var failureReason = output.TryGetValue("FailureReason", out var failureObj) ? failureObj?.ToString() ?? string.Empty : string.Empty;
        var matches = ExtractMatches(output);
        var actualPositions = matches.Select(item => item.Position).ToList();
        if (actualPositions.Count == 0 && TryGetPosition(output.TryGetValue("Position", out var positionObj) ? positionObj : null, out var position))
        {
            actualPositions.Add(position);
        }

        var positionError = expectedIsMatch
            ? allowedPositions.Count > expectedPositions.Count
                ? AllActualWithinAllowed(allowedPositions, actualPositions)
                : PositionError(expectedPositions, actualPositions)
            : 0.0;
        var allowedPositionError = expectedIsMatch
            ? AllActualWithinAllowed(allowedPositions, actualPositions)
            : 0.0;
        var scoreContractCorrect = ScoreContractCorrect(
            expectedNode?["score_contract"]?.GetValue<string>() ?? expectedMethod,
            actualIsMatch,
            score,
            normalized,
            raw);
        var requireDistinct = expectedNode?["require_distinct"]?.GetValue<bool>() ?? false;
        var nmsDistinct = !expectedIsMatch || !requireDistinct || MatchesAreDistinct(matches, 0.35);

        var expectedFailureCorrect = true;
        if (!expectedIsMatch)
        {
            var expectedFailure = expectedNode?["expected_failure_contains"]?.GetValue<string>() ?? string.Empty;
            expectedFailureCorrect = !actualIsMatch &&
                (string.IsNullOrWhiteSpace(expectedFailure) ||
                    failureReason.Contains(expectedFailure, StringComparison.OrdinalIgnoreCase));
        }

        metrics["ActualIsMatch"] = actualIsMatch;
        metrics["IsMatchCorrect"] = actualIsMatch == expectedIsMatch;
        metrics["ActualMatchCount"] = actualMatchCount;
        metrics["MatchCountCorrect"] = actualMatchCount == expectedMatchCount;
        metrics["PositionErrorPx"] = Round(positionError);
        metrics["AllowedPositionErrorPx"] = Round(allowedPositionError);
        metrics["ScoreValue"] = Round(score);
        metrics["NormalizedScoreValue"] = Round(normalized);
        metrics["RawResponseValue"] = Round(raw);
        metrics["ScoreContractCorrect"] = scoreContractCorrect;
        metrics["NormalizedScoreInRange"] = normalized >= -1e-6 && normalized <= 1.0 + 1e-6;
        metrics["MinScoreSatisfied"] = !expectedIsMatch ||
            normalized >= (expectedNode?["min_normalized_score"]?.GetValue<double>() ?? 0.0);
        metrics["MethodDescriptorCorrect"] = string.IsNullOrWhiteSpace(expectedMethod) ||
            string.Equals(method, expectedMethod, StringComparison.OrdinalIgnoreCase);
        metrics["ExpectedFailureCorrect"] = expectedFailureCorrect;
        metrics["NmsDistinct"] = nmsDistinct;
        metrics["FailureReason"] = failureReason;
        metrics["IsFinite"] =
            double.IsFinite(score) &&
            double.IsFinite(normalized) &&
            double.IsFinite(raw) &&
            double.IsFinite(positionError) &&
            double.IsFinite(allowedPositionError);

        return metrics;
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics, JsonNode? expectedNode)
    {
        if (!BoolMetric(metrics, "IsMatchCorrect") ||
            !BoolMetric(metrics, "MatchCountCorrect") ||
            !BoolMetric(metrics, "ScoreContractCorrect") ||
            !BoolMetric(metrics, "NormalizedScoreInRange") ||
            !BoolMetric(metrics, "MethodDescriptorCorrect") ||
            !BoolMetric(metrics, "ExpectedFailureCorrect") ||
            !BoolMetric(metrics, "NmsDistinct") ||
            !BoolMetric(metrics, "IsFinite"))
        {
            return false;
        }

        var expectedIsMatch = expectedNode?["is_match"]?.GetValue<bool>() ?? true;
        if (!expectedIsMatch)
        {
            return true;
        }

        if (!BoolMetric(metrics, "MinScoreSatisfied"))
        {
            return false;
        }

        var tolerance = expectedNode?["position_tolerance_px"]?.GetValue<double>() ?? 1.0;
        return DoubleMetric(metrics, "PositionErrorPx") <= tolerance &&
            DoubleMetric(metrics, "AllowedPositionErrorPx") <= tolerance;
    }

    private static bool ScoreContractCorrect(string methodDescriptor, bool isMatch, double score, double normalized, double raw)
    {
        var method = methodDescriptor.Split(':')[0];
        if (!isMatch)
        {
            return Math.Abs(score) <= 1e-9 && Math.Abs(normalized) <= 1e-9 && Math.Abs(raw) <= 1e-9;
        }

        if (!double.IsFinite(score) || !double.IsFinite(normalized) || !double.IsFinite(raw))
        {
            return false;
        }

        if (normalized < -1e-6 || normalized > 1.0 + 1e-6)
        {
            return false;
        }

        if (method.Equals("SqDiff", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("SqDiffNormed", StringComparison.OrdinalIgnoreCase))
        {
            if (Math.Abs(score - normalized) > 1e-6)
            {
                return false;
            }

            if (raw < -1e-6)
            {
                return false;
            }

            if (method.Equals("SqDiffNormed", StringComparison.OrdinalIgnoreCase) && raw > 1.0 + 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    private static List<MatchInfo> ExtractMatches(IReadOnlyDictionary<string, object> output)
    {
        if (!output.TryGetValue("Matches", out var matchesObj) || matchesObj is string || matchesObj is not IEnumerable enumerable)
        {
            return new List<MatchInfo>();
        }

        var matches = new List<MatchInfo>();
        foreach (var item in enumerable)
        {
            if (item is not IDictionary<string, object> dict)
            {
                continue;
            }

            if (!TryGetPosition(dict.TryGetValue("Position", out var positionObj) ? positionObj : null, out var position))
            {
                continue;
            }

            var width = TryGetConvertible(dict, "Width", out var widthValue) ? Convert.ToDouble(widthValue) : 0.0;
            var height = TryGetConvertible(dict, "Height", out var heightValue) ? Convert.ToDouble(heightValue) : 0.0;
            PositionValue topLeft;
            if (!TryGetPosition(dict.TryGetValue("TopLeft", out var topLeftObj) ? topLeftObj : null, out topLeft))
            {
                topLeft = new PositionValue(position.X - (width / 2.0), position.Y - (height / 2.0));
            }

            matches.Add(new MatchInfo(position, topLeft, width, height));
        }

        return matches;
    }

    private static bool TryGetPosition(object? value, out PositionValue position)
    {
        position = default;
        if (value is Position pos)
        {
            position = new PositionValue(pos.X, pos.Y);
            return true;
        }

        if (value is IDictionary<string, object> dict)
        {
            if (TryGetConvertible(dict, "X", out var xObj) && TryGetConvertible(dict, "Y", out var yObj))
            {
                position = new PositionValue(Convert.ToDouble(xObj), Convert.ToDouble(yObj));
                return true;
            }
        }

        return false;
    }

    private static bool TryGetConvertible(IDictionary<string, object> dict, string key, out IConvertible value)
    {
        value = 0;
        if (!dict.TryGetValue(key, out var raw) || raw is not IConvertible convertible)
        {
            return false;
        }

        value = convertible;
        return true;
    }

    private static List<PositionValue> ReadPositions(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return new List<PositionValue>();
        }

        var positions = new List<PositionValue>();
        foreach (var item in array)
        {
            if (item is null)
            {
                continue;
            }

            var x = item["x"]?.GetValue<double>() ?? item["X"]?.GetValue<double>() ?? 0.0;
            var y = item["y"]?.GetValue<double>() ?? item["Y"]?.GetValue<double>() ?? 0.0;
            positions.Add(new PositionValue(x, y));
        }

        return positions;
    }

    private static double PositionError(IReadOnlyList<PositionValue> expected, IReadOnlyList<PositionValue> actual)
    {
        if (expected.Count == 0)
        {
            return 0.0;
        }

        if (actual.Count < expected.Count)
        {
            return double.MaxValue;
        }

        var remaining = actual.ToList();
        var maxError = 0.0;
        foreach (var exp in expected)
        {
            var bestIndex = -1;
            var bestError = double.MaxValue;
            for (var i = 0; i < remaining.Count; i++)
            {
                var error = Distance(exp, remaining[i]);
                if (error < bestError)
                {
                    bestError = error;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return double.MaxValue;
            }

            maxError = Math.Max(maxError, bestError);
            remaining.RemoveAt(bestIndex);
        }

        return maxError;
    }

    private static double AllActualWithinAllowed(IReadOnlyList<PositionValue> allowed, IReadOnlyList<PositionValue> actual)
    {
        if (allowed.Count == 0 || actual.Count == 0)
        {
            return 0.0;
        }

        var maxError = 0.0;
        foreach (var act in actual)
        {
            maxError = Math.Max(maxError, allowed.Min(allowedPos => Distance(allowedPos, act)));
        }

        return maxError;
    }

    private static bool MatchesAreDistinct(IReadOnlyList<MatchInfo> matches, double iouThreshold)
    {
        for (var i = 0; i < matches.Count; i++)
        {
            for (var j = i + 1; j < matches.Count; j++)
            {
                if (IoU(matches[i], matches[j]) >= iouThreshold)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static double IoU(MatchInfo a, MatchInfo b)
    {
        var left = Math.Max(a.TopLeft.X, b.TopLeft.X);
        var top = Math.Max(a.TopLeft.Y, b.TopLeft.Y);
        var right = Math.Min(a.TopLeft.X + a.Width, b.TopLeft.X + b.Width);
        var bottom = Math.Min(a.TopLeft.Y + a.Height, b.TopLeft.Y + b.Height);
        var intersection = Math.Max(0.0, right - left) * Math.Max(0.0, bottom - top);
        var union = (a.Width * a.Height) + (b.Width * b.Height) - intersection;
        return union <= 0 ? 0.0 : intersection / union;
    }

    private static double Distance(PositionValue a, PositionValue b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
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

    private static string FormatFailure(OperatorExecutionOutput? execution, IReadOnlyDictionary<string, object> metrics)
    {
        if (execution is not null && !execution.IsSuccess)
        {
            return execution.ErrorMessage ?? "Execution failed.";
        }

        var keys = new[]
        {
            "IsMatchCorrect",
            "MatchCountCorrect",
            "PositionErrorPx",
            "AllowedPositionErrorPx",
            "ScoreContractCorrect",
            "NormalizedScoreInRange",
            "MinScoreSatisfied",
            "MethodDescriptorCorrect",
            "ExpectedFailureCorrect",
            "NmsDistinct",
            "IsFinite"
        };
        return string.Join(", ", keys.Where(metrics.ContainsKey).Select(key => $"{key}={FormatValue(metrics[key])}"));
    }

    private static async Task<JsonNode> ReadJsonAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
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

internal readonly record struct PositionValue(double X, double Y);

internal readonly record struct MatchInfo(PositionValue Position, PositionValue TopLeft, double Width, double Height);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# TemplateMatching Golden Runner Report",
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
            "| Case | Scenario | Passed | Runtime Ms | Pos Error Px | Score | Norm Score | Match Count | Error |",
            "|---|---|---|---:|---:|---:|---:|---:|---|"
        ]);

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("PositionErrorPx", out var posError);
            item.Metrics.TryGetValue("ScoreValue", out var score);
            item.Metrics.TryGetValue("NormalizedScoreValue", out var normScore);
            item.Metrics.TryGetValue("ActualMatchCount", out var count);
            lines.Add(
                $"| {item.CaseId} | {item.Scenario} | {BoolToMark(item.Passed)} | {item.RuntimeMs:0.###} | " +
                $"{FormatValue(posError)} | {FormatValue(score)} | {FormatValue(normScore)} | {count ?? "-"} | {item.ErrorMessage ?? "-"} |");
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
        var casesRoot = "quality/synthetic/cases/template_matching";
        var output = "quality/evals/reports/TemplateMatching_baseline.json";
        string? report = "quality/evals/reports/TemplateMatching_baseline.md";
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
            TemplateMatching golden runner

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
