using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime.Tensors;

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

var result = PressureRunner.Run();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));
if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine($"DeepLearning postprocess pressure complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, output={options.OutputPath}");
return result.Summary.Failed == 0 ? 0 : 1;

internal static class PressureRunner
{
    private const int InputSize = 640;
    private const int ClassCount = 3;
    private const float ConfidenceThreshold = 0.35f;
    private const float NmsIouThreshold = 0.45f;
    private const int CandidateLimit = 10000;
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);

    public static PressureResult Run()
    {
        var cases = new[]
        {
            new PressureCase("postprocess_1k_balanced_classes", 1000, "balanced", 1920, 1080, RuntimeBudgetMs: 750, MemoryBudgetBytes: 8 * 1024 * 1024),
            new PressureCase("postprocess_5k_balanced_classes", 5000, "balanced", 1920, 1080, RuntimeBudgetMs: 750, MemoryBudgetBytes: 24 * 1024 * 1024),
            new PressureCase("postprocess_10k_skewed_classes", 10000, "skewed", 1920, 1080, RuntimeBudgetMs: 1000, MemoryBudgetBytes: 48 * 1024 * 1024)
        };
        var results = cases.Select(RunCase).ToList();
        var failed = results.Count(item => !item.Passed);
        return new PressureResult(
            "contract",
            new Summary(DateTimeOffset.UtcNow, results.Count, results.Count - failed, failed),
            [new OperatorSummary("DeepLearning", results.Count, results.Count - failed, failed, true, "postprocess-pressure")],
            results);
    }

    private static CaseResult RunCase(PressureCase pressureCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            var tensor = CreateTensor(pressureCase.Candidates);
            var classDistribution = WriteCandidates(tensor, pressureCase);
            var detections = InvokePostprocessYoloV8(tensor, pressureCase.Width, pressureCase.Height);
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            var memoryAllocationBytes = Math.Max(0, allocationAfter - allocationBefore);
            var passed = detections.Count > 0
                && detections.Count <= pressureCase.Candidates
                && stopwatch.Elapsed.TotalMilliseconds <= pressureCase.RuntimeBudgetMs
                && memoryAllocationBytes <= pressureCase.MemoryBudgetBytes;
            return new CaseResult(
                pressureCase.Id,
                pressureCase.Candidates,
                pressureCase.Distribution,
                classDistribution,
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                pressureCase.RuntimeBudgetMs,
                memoryAllocationBytes,
                pressureCase.MemoryBudgetBytes,
                detections.Count,
                CandidateLimit,
                Math.Max(0, pressureCase.Candidates - CandidateLimit),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                pressureCase.Id,
                pressureCase.Candidates,
                pressureCase.Distribution,
                new Dictionary<int, int>(),
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                pressureCase.RuntimeBudgetMs,
                Math.Max(0, allocationAfter - allocationBefore),
                pressureCase.MemoryBudgetBytes,
                0,
                CandidateLimit,
                0,
                ex.GetBaseException().Message);
        }
    }

    private static DenseTensor<float> CreateTensor(int anchorCount) =>
        new(new float[1 * (4 + ClassCount) * anchorCount], [1, 4 + ClassCount, anchorCount]);

    private static Dictionary<int, int> WriteCandidates(DenseTensor<float> tensor, PressureCase pressureCase)
    {
        var counts = new Dictionary<int, int>();
        var scale = Math.Min((float)InputSize / pressureCase.Width, (float)InputSize / pressureCase.Height);
        var xPad = (InputSize - pressureCase.Width * scale) / 2f;
        var yPad = (InputSize - pressureCase.Height * scale) / 2f;
        var columns = (int)Math.Ceiling(Math.Sqrt(pressureCase.Candidates));

        for (var i = 0; i < pressureCase.Candidates; i++)
        {
            var classId = pressureCase.Distribution == "skewed"
                ? (i % 10 == 0 ? 1 : i % 31 == 0 ? 2 : 0)
                : i % ClassCount;
            counts[classId] = counts.TryGetValue(classId, out var count) ? count + 1 : 1;
            var gridX = i % columns;
            var gridY = i / columns;
            var x = 20 + gridX * 17 % Math.Max(32, pressureCase.Width - 40);
            var y = 20 + gridY * 17 % Math.Max(32, pressureCase.Height - 40);
            tensor[0, 0, i] = x * scale + xPad;
            tensor[0, 1, i] = y * scale + yPad;
            tensor[0, 2, i] = 10 * scale;
            tensor[0, 3, i] = 10 * scale;
            tensor[0, 4 + classId, i] = 0.99f - (i % 100) * 0.0001f;
        }

        return counts;
    }

    private static List<DetectionRecord> InvokePostprocessYoloV8(DenseTensor<float> tensor, int width, int height)
    {
        var method = typeof(DeepLearningOperator).GetMethod("PostprocessYoloV8V11", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DeepLearningOperator), "PostprocessYoloV8V11");
        var values = (IEnumerable)(method.Invoke(Operator, [tensor, ConfidenceThreshold, width, height, InputSize, true, NmsIouThreshold])
            ?? throw new InvalidOperationException("PostprocessYoloV8V11 returned null."));
        return values.Cast<object>().Select(ReadDetection).ToList();
    }

    private static DetectionRecord ReadDetection(object detection) =>
        new(ReadProperty<float>(detection, "Confidence"), ReadProperty<int>(detection, "ClassId"));

    private static T ReadProperty<T>(object instance, string propertyName) =>
        (T)(instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Property not found: {propertyName}"));
}

internal sealed record PressureCase(string Id, int Candidates, string Distribution, int Width, int Height, double RuntimeBudgetMs, long MemoryBudgetBytes);
internal sealed record DetectionRecord(float Confidence, int ClassId);
internal sealed record PressureResult(string EvidenceKind, Summary Summary, List<OperatorSummary> Operators, List<CaseResult> Cases);
internal sealed record Summary(DateTimeOffset GeneratedAtUtc, int CaseCount, int Passed, int Failed);
internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, bool HasBenchmark, string Benchmark);
internal sealed record CaseResult(string CaseId, int CandidateCount, string ClassDistributionMode, Dictionary<int, int> CandidatesByClass, bool Passed, double RuntimeMs, double RuntimeBudgetMs, long MemoryAllocationBytes, long MemoryBudgetBytes, int SelectedCount, int CandidateLimit, int DroppedBeforeNms, string? Failure);

internal static class MarkdownReport
{
    public static string Create(PressureResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning Postprocess Pressure Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "",
            "| Case | Candidates | Class distribution | Selected | CandidateLimit | DroppedBeforeNms | Runtime ms | Runtime budget ms | Memory bytes | Memory budget bytes | Passed | Failure |",
            "| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        };
        lines.AddRange(result.Cases.Select(item => $"| {item.CaseId} | {item.CandidateCount} | {FormatDistribution(item.CandidatesByClass)} | {item.SelectedCount} | {item.CandidateLimit} | {item.DroppedBeforeNms} | {item.RuntimeMs:0.###} | {item.RuntimeBudgetMs:0.###} | {item.MemoryAllocationBytes} | {item.MemoryBudgetBytes} | {item.Passed} | {item.Failure ?? "-"} |"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string FormatDistribution(Dictionary<int, int> values) =>
        string.Join(", ", values.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}"));
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions("quality/evals/reports/DeepLearning_postprocess_pressure_baseline.json", "quality/evals/reports/DeepLearning_postprocess_pressure_baseline.md", false, null);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help") return options with { ShowHelp = true };
            if (i + 1 >= args.Length) return options with { ParseError = $"Missing value for {arg}" };
            var value = args[++i];
            options = arg switch
            {
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };
        }
        return options;
    }

    public static void PrintHelp() => Console.WriteLine("Usage: dotnet run --project quality/tools/DeepLearningPostprocessPressureRunner/DeepLearningPostprocessPressureRunner.csproj -- [--output path] [--report path]");
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
