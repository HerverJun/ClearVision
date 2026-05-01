using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
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

var result = await ArcCaliperGoldenRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"ArcCaliper golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ArcCaliperGoldenRunner
{
    public static async Task<BaselineResult> RunAsync()
    {
        var cases = CreateCases();
        var results = new List<CaseResult>();

        if (cases.Count > 0)
        {
            _ = await RunCaseAsync(cases[0] with { CaseId = "ArcCaliper_warmup" });
        }

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
                "embedded synthetic arc scenes",
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                byOperator.Sum(item => item.MemoryAllocationBytesAvg)),
            byOperator,
            results);
    }

    private static IReadOnlyList<ArcCaliperCase> CreateCases()
    {
        var cases = new List<ArcCaliperCase>();
        var index = 0;

        foreach (var radius in new[] { 42, 50, 58, 66 })
        {
            foreach (var span in new[] { (15.0, 125.0), (45.0, 210.0), (120.0, 280.0) })
            {
                cases.Add(ArcCaliperCase.ExpectEdges(
                    CaseId("negative_filled_circle", index++),
                    SceneKind.WhiteDiskOnBlack,
                    transition: "negative",
                    radius,
                    span.Item1,
                    span.Item2));
            }
        }

        foreach (var radius in new[] { 44, 52, 60, 68 })
        {
            foreach (var span in new[] { (20.0, 160.0), (75.0, 230.0), (190.0, 315.0) })
            {
                cases.Add(ArcCaliperCase.ExpectEdges(
                    CaseId("positive_dark_disk", index++),
                    SceneKind.BlackDiskOnWhite,
                    transition: "positive",
                    radius,
                    span.Item1,
                    span.Item2));
            }
        }

        cases.Add(ArcCaliperCase.ExpectEdges(CaseId("wraparound_negative", index++), SceneKind.WhiteDiskOnBlack, "negative", 56, 330.0, 30.0));
        cases.Add(ArcCaliperCase.ExpectEdges(CaseId("wraparound_positive", index++), SceneKind.BlackDiskOnWhite, "positive", 56, 315.0, 45.0));
        cases.Add(ArcCaliperCase.ExpectNoEdges(CaseId("wrong_polarity_negative", index++), SceneKind.WhiteDiskOnBlack, "positive", 56, 25.0, 155.0));
        cases.Add(ArcCaliperCase.ExpectNoEdges(CaseId("wrong_polarity_positive", index++), SceneKind.BlackDiskOnWhite, "negative", 56, 25.0, 155.0));
        cases.Add(ArcCaliperCase.ExpectNoEdges(CaseId("low_texture", index++), SceneKind.LowTextureGray, "all", 56, 25.0, 155.0));
        cases.Add(ArcCaliperCase.ExpectFailure(CaseId("outside_sampling", index++), SceneKind.WhiteDiskOnBlack, "all", 150, 25.0, 155.0, "sampling region"));
        cases.Add(ArcCaliperCase.ExpectFailure(CaseId("zero_span", index++), SceneKind.WhiteDiskOnBlack, "all", 56, 45.0, 45.0, "span"));

        return cases;
    }

    private static async Task<CaseResult> RunCaseAsync(ArcCaliperCase testCase)
    {
        var sut = new ArcCaliperOperator(NullLogger<ArcCaliperOperator>.Instance);
        var op = new Operator("ArcCaliper", OperatorType.ArcCaliper, 0, 0);
        var inputs = CreateInputs(testCase);

        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var execution = await sut.ExecuteAsync(op, inputs);
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        stopwatch.Stop();

        var metrics = new Dictionary<string, object?>
        {
            ["ExpectedSuccess"] = testCase.ExpectedSuccess,
            ["ExpectedMinCount"] = testCase.ExpectedMinCount,
            ["ExpectedMaxCount"] = testCase.ExpectedMaxCount,
            ["ExpectedRadius"] = testCase.Radius,
            ["RadiusTolerancePx"] = testCase.RadiusTolerancePx,
            ["ExpectedErrorContains"] = testCase.ExpectedErrorContains
        };

        var passed = Evaluate(testCase, execution, metrics);
        ReleaseImageOutputs(execution.OutputData);

        return new CaseResult(
            testCase.CaseId,
            "ArcCaliper",
            testCase.Scenario,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            passed ? null : execution.ErrorMessage ?? "Metric mismatch",
            metrics);
    }

    private static Dictionary<string, object> CreateInputs(ArcCaliperCase testCase)
    {
        return new Dictionary<string, object>
        {
            ["Image"] = new ImageWrapper(CreateScene(testCase)),
            ["CenterX"] = testCase.CenterX,
            ["CenterY"] = testCase.CenterY,
            ["Radius"] = testCase.Radius,
            ["StartAngle"] = testCase.StartAngle,
            ["EndAngle"] = testCase.EndAngle,
            ["Transition"] = testCase.Transition
        };
    }

    private static Mat CreateScene(ArcCaliperCase testCase)
    {
        var mat = testCase.Scene switch
        {
            SceneKind.BlackDiskOnWhite => new Mat(testCase.Height, testCase.Width, MatType.CV_8UC3, Scalar.White),
            SceneKind.LowTextureGray => new Mat(testCase.Height, testCase.Width, MatType.CV_8UC3, new Scalar(128, 128, 128)),
            _ => new Mat(testCase.Height, testCase.Width, MatType.CV_8UC3, Scalar.Black)
        };

        if (testCase.Scene == SceneKind.WhiteDiskOnBlack)
        {
            Cv2.Circle(mat, new Point(testCase.CenterX, testCase.CenterY), testCase.Radius, Scalar.White, -1);
        }
        else if (testCase.Scene == SceneKind.BlackDiskOnWhite)
        {
            Cv2.Circle(mat, new Point(testCase.CenterX, testCase.CenterY), testCase.Radius, Scalar.Black, -1);
        }

        return mat;
    }

    private static bool Evaluate(ArcCaliperCase testCase, OperatorExecutionOutput execution, Dictionary<string, object?> metrics)
    {
        metrics["ActualSuccess"] = execution.IsSuccess;
        metrics["ErrorMessage"] = execution.ErrorMessage;

        if (!testCase.ExpectedSuccess)
        {
            return !execution.IsSuccess &&
                (testCase.ExpectedErrorContains is null ||
                    (execution.ErrorMessage?.Contains(testCase.ExpectedErrorContains, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return false;
        }

        var points = execution.OutputData.TryGetValue("Points", out var pointsValue) && pointsValue is List<ArcCaliperPoint> typedPoints
            ? typedPoints
            : new List<ArcCaliperPoint>();

        var count = points.Count;
        var averageContrast = execution.OutputData.TryGetValue("AverageContrast", out var averageContrastValue)
            ? Convert.ToDouble(averageContrastValue)
            : 0.0;
        var radiusMeanError = points.Count == 0
            ? (double?)null
            : points.Average(point => Math.Abs(Distance(point.X, point.Y, testCase.CenterX, testCase.CenterY) - testCase.Radius));

        metrics["ActualCount"] = count;
        metrics["ActualAverageContrast"] = Math.Round(averageContrast, 3);
        metrics["RadiusMeanErrorPx"] = radiusMeanError.HasValue ? Math.Round(radiusMeanError.Value, 3) : null;
        metrics["OutputProcessingTimeMs"] = execution.OutputData.TryGetValue("ProcessingTimeMs", out var processingTime)
            ? processingTime
            : null;

        if (testCase.ExpectedMinCount.HasValue && count < testCase.ExpectedMinCount.Value)
        {
            return false;
        }

        if (testCase.ExpectedMaxCount.HasValue && count > testCase.ExpectedMaxCount.Value)
        {
            return false;
        }

        if (testCase.MinAverageContrast.HasValue && averageContrast < testCase.MinAverageContrast.Value)
        {
            return false;
        }

        if (testCase.ExpectedMinCount.GetValueOrDefault() > 0 &&
            (!radiusMeanError.HasValue || radiusMeanError.Value > testCase.RadiusTolerancePx))
        {
            return false;
        }

        return true;
    }

    private static double Distance(double x, double y, int centerX, int centerY)
    {
        var dx = x - centerX;
        var dy = y - centerY;
        return Math.Sqrt((dx * dx) + (dy * dy));
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

    private static string CaseId(string scenario, int index) => $"ArcCaliper_{scenario}_{index:0000}";
}

internal sealed record ArcCaliperCase(
    string CaseId,
    string Scenario,
    SceneKind Scene,
    string Transition,
    int Width,
    int Height,
    int CenterX,
    int CenterY,
    int Radius,
    double StartAngle,
    double EndAngle,
    bool ExpectedSuccess,
    int? ExpectedMinCount,
    int? ExpectedMaxCount,
    double? MinAverageContrast,
    double RadiusTolerancePx,
    string? ExpectedErrorContains)
{
    public static ArcCaliperCase ExpectEdges(string caseId, SceneKind scene, string transition, int radius, double startAngle, double endAngle)
    {
        var minCount = Math.Max(8, (int)Math.Floor(ArcSpan(startAngle, endAngle) * 0.55));
        return new ArcCaliperCase(
            caseId,
            ScenarioFrom(caseId),
            scene,
            transition,
            220,
            220,
            110,
            110,
            radius,
            startAngle,
            endAngle,
            ExpectedSuccess: true,
            ExpectedMinCount: minCount,
            ExpectedMaxCount: null,
            MinAverageContrast: 50.0,
            RadiusTolerancePx: 4.0,
            ExpectedErrorContains: null);
    }

    public static ArcCaliperCase ExpectNoEdges(string caseId, SceneKind scene, string transition, int radius, double startAngle, double endAngle)
    {
        return new ArcCaliperCase(
            caseId,
            ScenarioFrom(caseId),
            scene,
            transition,
            220,
            220,
            110,
            110,
            radius,
            startAngle,
            endAngle,
            ExpectedSuccess: true,
            ExpectedMinCount: null,
            ExpectedMaxCount: 0,
            MinAverageContrast: null,
            RadiusTolerancePx: 4.0,
            ExpectedErrorContains: null);
    }

    public static ArcCaliperCase ExpectFailure(string caseId, SceneKind scene, string transition, int radius, double startAngle, double endAngle, string expectedErrorContains)
    {
        return new ArcCaliperCase(
            caseId,
            ScenarioFrom(caseId),
            scene,
            transition,
            220,
            220,
            110,
            110,
            radius,
            startAngle,
            endAngle,
            ExpectedSuccess: false,
            ExpectedMinCount: null,
            ExpectedMaxCount: null,
            MinAverageContrast: null,
            RadiusTolerancePx: 4.0,
            ExpectedErrorContains: expectedErrorContains);
    }

    private static string ScenarioFrom(string caseId)
    {
        var prefix = "ArcCaliper_";
        var scenario = caseId.StartsWith(prefix, StringComparison.Ordinal) ? caseId[prefix.Length..] : caseId;
        var lastUnderscore = scenario.LastIndexOf('_');
        return lastUnderscore > 0 ? scenario[..lastUnderscore] : scenario;
    }

    private static double ArcSpan(double startAngle, double endAngle)
    {
        var span = endAngle - startAngle;
        if (Math.Abs(span) > 360.0)
        {
            span %= 360.0;
        }

        while (span < 0.0)
        {
            span += 360.0;
        }

        return Math.Abs(span) <= 1e-6 ? 0.0 : span;
    }
}

internal enum SceneKind
{
    WhiteDiskOnBlack,
    BlackDiskOnWhite,
    LowTextureGray
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# ArcCaliper Golden Runner Report",
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
            "| Case | Scenario | Passed | Runtime Ms | Count | Radius Error Px | Error |",
            "|---|---|---|---:|---:|---:|---|"
        ]);

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("ActualCount", out var count);
            item.Metrics.TryGetValue("RadiusMeanErrorPx", out var radiusError);
            lines.Add($"| {item.CaseId} | {item.Scenario} | {BoolToMark(item.Passed)} | {item.RuntimeMs:0.###} | {count ?? "-"} | {radiusError ?? "-"} | {item.ErrorMessage ?? "-"} |");
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
        var output = "quality/evals/reports/ArcCaliper_baseline.json";
        string? report = "quality/evals/reports/ArcCaliper_baseline.md";
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
            ArcCaliper golden runner

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
