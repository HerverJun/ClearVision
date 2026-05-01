using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
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
    $"Region/Morphology golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    private static readonly HashSet<string> SupportedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "RegionUnion",
        "RegionIntersection",
        "RegionDifference",
        "RegionComplement",
        "RegionErosion",
        "RegionDilation",
        "RegionOpening",
        "RegionClosing",
        "RegionSkeleton"
    };

    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var inputFiles = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(options.OperatorFilter))
        {
            inputFiles = inputFiles
                .Where(path => path.Contains(
                    $"{Path.DirectorySeparatorChar}{options.OperatorFilter}{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            inputFiles = inputFiles
                .Where(path => SupportedOperators.Contains(OperatorDirectoryName(path)))
                .ToList();
        }

        if (inputFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No region/morphology golden cases found under {options.CasesRoot}. " +
                "Generate them with quality/synthetic/generators/region_generator.py and morphology_generator.py.");
        }

        var caseResults = new List<CaseResult>();
        foreach (var inputPath in inputFiles)
        {
            var expectedPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "expected.json");
            caseResults.Add(await RunCaseAsync(inputPath, expectedPath));
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

    private static string OperatorDirectoryName(string inputPath)
    {
        var caseDirectory = Path.GetDirectoryName(inputPath);
        var operatorDirectory = caseDirectory is null ? null : Directory.GetParent(caseDirectory);
        return operatorDirectory?.Name ?? string.Empty;
    }

    private static async Task<CaseResult> RunCaseAsync(string inputPath, string expectedPath)
    {
        var inputJson = await ReadJsonAsync(inputPath);
        var expectedJson = await ReadJsonAsync(expectedPath);
        var caseId = inputJson.RequiredString("case_id");
        var operatorName = inputJson.RequiredString("operator");
        var scenario = inputJson.RequiredString("scenario");

        try
        {
            var executor = CreateExecutor(operatorName);
            var op = CreateOperator(operatorName, inputJson);
            var inputs = CreateInputs(operatorName, inputJson);
            var expectedRegion = RegionFromRuns(expectedJson["expected"]?["runs"]?.AsArray());
            var expectedEndPoints = expectedJson["expected"]?["end_points"]?.GetValue<int?>();
            var expectedBranchPoints = expectedJson["expected"]?["branch_points"]?.GetValue<int?>();

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            if (!execution.IsSuccess)
            {
                return CaseResult.Failed(
                    caseId,
                    operatorName,
                    scenario,
                    inputPath,
                    stopwatch.Elapsed.TotalMilliseconds,
                    Math.Max(0, allocationAfter - allocationBefore),
                    execution.ErrorMessage ?? "Execution failed without error message.");
            }

            if (execution.OutputData is null || !execution.OutputData.TryGetValue("Region", out var actualValue) || actualValue is not Region actualRegion)
            {
                ReleaseImageOutputs(execution.OutputData);
                return CaseResult.Failed(
                    caseId,
                    operatorName,
                    scenario,
                    inputPath,
                    stopwatch.Elapsed.TotalMilliseconds,
                    Math.Max(0, allocationAfter - allocationBefore),
                    "Execution output did not contain a Region value.");
            }

            var metrics = MetricsCalculator.Evaluate(expectedRegion, actualRegion);
            if (expectedEndPoints.HasValue && execution.OutputData.TryGetValue("EndPoints", out var actualEndPoints))
            {
                metrics["EndPointCountError"] = Math.Abs(expectedEndPoints.Value - Convert.ToInt32(actualEndPoints));
            }

            if (expectedBranchPoints.HasValue && execution.OutputData.TryGetValue("BranchPoints", out var actualBranchPoints))
            {
                metrics["BranchPointCountError"] = Math.Abs(expectedBranchPoints.Value - Convert.ToInt32(actualBranchPoints));
            }

            ReleaseImageOutputs(execution.OutputData);

            var passed = IsPassing(metrics);
            return new CaseResult(
                caseId,
                operatorName,
                scenario,
                inputPath,
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                null,
                metrics);
        }
        catch (Exception ex)
        {
            return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, ex.Message);
        }
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics)
    {
        static double Number(IReadOnlyDictionary<string, object> metrics, string key)
        {
            return Convert.ToDouble(metrics[key]);
        }

        var passed = Number(metrics, "AreaError") == 0
            && Number(metrics, "ComponentCountError") == 0
            && Number(metrics, "BBoxIoU") == 1.0
            && Number(metrics, "MaskIoU") == 1.0
            && metrics.TryGetValue("EmptyRegionBehavior", out var emptyBehavior)
            && emptyBehavior is true;

        if (metrics.TryGetValue("EndPointCountError", out var endpointError))
        {
            passed &= Convert.ToInt32(endpointError) == 0;
        }

        if (metrics.TryGetValue("BranchPointCountError", out var branchPointError))
        {
            passed &= Convert.ToInt32(branchPointError) == 0;
        }

        return passed;
    }

    private static IOperatorExecutor CreateExecutor(string operatorName)
    {
        return operatorName switch
        {
            "RegionUnion" => new RegionUnionOperator(NullLogger<RegionUnionOperator>.Instance),
            "RegionIntersection" => new RegionIntersectionOperator(NullLogger<RegionIntersectionOperator>.Instance),
            "RegionDifference" => new RegionDifferenceOperator(NullLogger<RegionDifferenceOperator>.Instance),
            "RegionComplement" => new RegionComplementOperator(NullLogger<RegionComplementOperator>.Instance),
            "RegionErosion" => new RegionErosionOperator(NullLogger<RegionErosionOperator>.Instance),
            "RegionDilation" => new RegionDilationOperator(NullLogger<RegionDilationOperator>.Instance),
            "RegionOpening" => new RegionOpeningOperator(NullLogger<RegionOpeningOperator>.Instance),
            "RegionClosing" => new RegionClosingOperator(NullLogger<RegionClosingOperator>.Instance),
            "RegionSkeleton" => new RegionSkeletonOperator(NullLogger<RegionSkeletonOperator>.Instance),
            _ => throw new NotSupportedException($"Unsupported operator: {operatorName}")
        };
    }

    private static Operator CreateOperator(string operatorName, JsonNode inputJson)
    {
        var operatorType = Enum.Parse<OperatorType>(operatorName);
        var op = new Operator(operatorName, operatorType, 0, 0);

        var kernel = inputJson["inputs"]?["kernel"];
        if (kernel is null)
        {
            return op;
        }

        if (kernel["shape"]?.GetValue<string>() is { } shape)
        {
            op.Parameters.Add(CreateParameter("KernelShape", shape, "enum"));
        }

        if (kernel["width"]?.GetValue<int?>() is { } width)
        {
            op.Parameters.Add(CreateParameter("KernelWidth", width, "int"));
        }

        if (kernel["height"]?.GetValue<int?>() is { } height)
        {
            op.Parameters.Add(CreateParameter("KernelHeight", height, "int"));
        }

        if (kernel["iterations"]?.GetValue<int?>() is { } iterations)
        {
            op.Parameters.Add(CreateParameter("Iterations", iterations, "int"));
        }

        if (kernel["max_iterations"]?.GetValue<int?>() is { } maxIterations)
        {
            op.Parameters.Add(CreateParameter("MaxIterations", maxIterations, "int"));
        }

        return op;
    }

    private static Parameter CreateParameter(string name, object value, string dataType)
    {
        return new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value);
    }

    private static Dictionary<string, object> CreateInputs(string operatorName, JsonNode inputJson)
    {
        var inputsJson = inputJson["inputs"] ?? throw new InvalidOperationException("input.json is missing inputs.");
        var inputs = new Dictionary<string, object>();

        if (operatorName == "RegionComplement")
        {
            inputs["Region"] = RegionFromRuns(inputsJson["region"]?["runs"]?.AsArray());
            inputs["ImageWidth"] = inputJson.RequiredInt("width");
            inputs["ImageHeight"] = inputJson.RequiredInt("height");
            return inputs;
        }

        if (operatorName.StartsWith("Region", StringComparison.Ordinal) && inputsJson["region"] is not null)
        {
            inputs["Region"] = RegionFromRuns(inputsJson["region"]?["runs"]?.AsArray());
            return inputs;
        }

        inputs["Region1"] = RegionFromRuns(inputsJson["region1"]?["runs"]?.AsArray());
        inputs["Region2"] = RegionFromRuns(inputsJson["region2"]?["runs"]?.AsArray());
        return inputs;
    }

    private static Region RegionFromRuns(JsonArray? runs)
    {
        if (runs is null)
        {
            return new Region();
        }

        return new Region(runs.Select(run =>
        {
            if (run is null)
            {
                throw new InvalidOperationException("Run entry is null.");
            }

            return new RunLength(
                run.RequiredInt("y"),
                run.RequiredInt("start_x"),
                run.RequiredInt("end_x"));
        }));
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
}

internal static class MetricsCalculator
{
    public static Dictionary<string, object> Evaluate(Region expected, Region actual)
    {
        var expectedPoints = ToPoints(expected);
        var actualPoints = ToPoints(actual);
        var expectedBbox = BBox(expectedPoints);
        var actualBbox = BBox(actualPoints);

        return new Dictionary<string, object>
        {
            ["AreaError"] = Math.Abs(expectedPoints.Count - actualPoints.Count),
            ["ComponentCountError"] = Math.Abs(ComponentCount(expectedPoints) - ComponentCount(actualPoints)),
            ["BBoxIoU"] = BBoxIoU(expectedBbox, actualBbox),
            ["MaskIoU"] = MaskIoU(expectedPoints, actualPoints),
            ["EmptyRegionBehavior"] = expectedPoints.Count == 0 == (actualPoints.Count == 0),
            ["ExpectedArea"] = expectedPoints.Count,
            ["ActualArea"] = actualPoints.Count,
            ["ExpectedComponentCount"] = ComponentCount(expectedPoints),
            ["ActualComponentCount"] = ComponentCount(actualPoints),
            ["ExpectedBBox"] = expectedBbox,
            ["ActualBBox"] = actualBbox
        };
    }

    private static HashSet<(int X, int Y)> ToPoints(Region region)
    {
        var points = new HashSet<(int X, int Y)>();
        foreach (var run in region.RunLengths)
        {
            for (var x = run.StartX; x <= run.EndX; x++)
            {
                points.Add((x, run.Y));
            }
        }

        return points;
    }

    private static int[] BBox(HashSet<(int X, int Y)> points)
    {
        if (points.Count == 0)
        {
            return [0, 0, 0, 0];
        }

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        return [minX, minY, maxX - minX + 1, maxY - minY + 1];
    }

    private static int ComponentCount(HashSet<(int X, int Y)> points)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        var offsets = new[]
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0),            (1, 0),
            (-1, 1),  (0, 1),  (1, 1)
        };
        var remaining = new HashSet<(int X, int Y)>(points);
        var queue = new Queue<(int X, int Y)>();
        var components = 0;

        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            remaining.Remove(seed);
            queue.Enqueue(seed);
            components++;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var (dx, dy) in offsets)
                {
                    var next = (current.X + dx, current.Y + dy);
                    if (remaining.Remove(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return components;
    }

    private static double MaskIoU(HashSet<(int X, int Y)> expected, HashSet<(int X, int Y)> actual)
    {
        var union = expected.Union(actual).Count();
        if (union == 0)
        {
            return 1.0;
        }

        return (double)expected.Intersect(actual).Count() / union;
    }

    private static double BBoxIoU(int[] expected, int[] actual)
    {
        if (expected[2] == 0 && expected[3] == 0 && actual[2] == 0 && actual[3] == 0)
        {
            return 1.0;
        }

        if (expected[2] <= 0 || expected[3] <= 0 || actual[2] <= 0 || actual[3] <= 0)
        {
            return 0.0;
        }

        var left = Math.Max(expected[0], actual[0]);
        var top = Math.Max(expected[1], actual[1]);
        var right = Math.Min(expected[0] + expected[2], actual[0] + actual[2]);
        var bottom = Math.Min(expected[1] + expected[3], actual[1] + actual[3]);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = expected[2] * expected[3] + actual[2] * actual[3] - intersection;
        return union == 0 ? 1.0 : (double)intersection / union;
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# Region/Morphology Golden Runner Report",
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
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {item.ErrorMessage ?? "Metric mismatch"} |"));
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string CasesRoot,
    string OutputPath,
    string? ReportPath,
    string? OperatorFilter,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var casesRoot = "quality/synthetic/cases";
        var output = "quality/evals/reports/RegionMorphology_baseline.json";
        string? report = "quality/evals/reports/RegionMorphology_before_after_report.md";
        string? operatorFilter = null;
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
                case "--operator":
                    operatorFilter = NextValue(args, ref i, arg, ref parseError);
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

        return new RunnerOptions(casesRoot, output, report, operatorFilter, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            Region/Morphology golden runner

            Options:
              --cases-root <dir>   Directory containing generated input.json/expected.json files.
              --output <path>      Baseline JSON output path.
              --report <path>      Markdown report output path.
              --operator <name>    Optional operator filter, e.g. RegionUnion.
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
