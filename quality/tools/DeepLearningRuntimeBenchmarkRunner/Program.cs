using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime.Tensors;
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

var result = RuntimeBenchmark.Run();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"DeepLearning runtime benchmark complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class RuntimeBenchmark
{
    private const int InputSize = 640;
    private const int AnchorCount = 8400;
    private const int ClassCount = 3;
    private const float ConfidenceThreshold = 0.35f;
    private const float NmsIouThreshold = 0.45f;
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);

    public static BenchmarkResult Run()
    {
        var provider = ResolveExecutionProvider();
        var cases = new List<BenchmarkCase>();

        cases.AddRange(CreateScenarioCases("1080p_cpu_preprocess_postprocess", width: 1920, height: 1080, batchSize: 1, iterations: 6, provider));
        cases.AddRange(CreateScenarioCases("4k_cpu_preprocess_postprocess", width: 3840, height: 2160, batchSize: 1, iterations: 4, provider));
        cases.AddRange(CreateScenarioCases("batch_pressure_1080p_x4", width: 1920, height: 1080, batchSize: 4, iterations: 5, provider));
        cases.AddRange(CreateScenarioCases("gpu_cpu_fallback_contract", width: 1280, height: 720, batchSize: 1, iterations: 5, provider));

        var results = new List<CaseResult>(cases.Count);
        foreach (var benchmarkCase in cases)
        {
            results.Add(RunCase(benchmarkCase));
        }

        var failed = results.Count(item => !item.Passed);
        var groups = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                Math.Round(group.Average(item => item.PreprocessMs), 3),
                Math.Round(group.Average(item => item.PostprocessMs), 3),
                Math.Round(group.Average(item => item.DetectionCount), 3)))
            .ToList();

        return new BenchmarkResult(
            new BenchmarkSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count - failed,
                failed,
                Math.Round(results.Sum(item => item.RuntimeMs), 3),
                results.Sum(item => item.MemoryAllocationBytes),
                provider.AvailableProviders,
                provider.RequestedProvider,
                provider.ActiveProvider,
                provider.FallbackToCpu,
                "preprocess+YOLO postprocess benchmark; ONNX provider availability/fallback metadata is recorded without model inference"),
            [
                new OperatorSummary(
                    "DeepLearning",
                    results.Count,
                    results.Count - failed,
                    failed,
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    (long)Math.Round(results.Average(item => item.MemoryAllocationBytes)),
                    true,
                    provider.ActiveProvider,
                    provider.FallbackToCpu)
            ],
            groups,
            results);
    }

    private static IEnumerable<BenchmarkCase> CreateScenarioCases(
        string scenario,
        int width,
        int height,
        int batchSize,
        int iterations,
        ProviderInfo provider)
    {
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            yield return new BenchmarkCase(
                $"{scenario}_{iteration:0000}",
                scenario,
                width,
                height,
                batchSize,
                iteration,
                provider);
        }
    }

    private static CaseResult RunCase(BenchmarkCase benchmarkCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var preprocessMs = 0d;
        var postprocessMs = 0d;
        var detectionCount = 0;
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);

        try
        {
            using var image = CreateImage(benchmarkCase.Width, benchmarkCase.Height, benchmarkCase.Iteration);
            var tensor = CreateYoloV8Tensor();
            WriteBenchmarkDetections(tensor, benchmarkCase.Width, benchmarkCase.Height);

            for (var frame = 0; frame < benchmarkCase.BatchSize; frame++)
            {
                var preprocessStopwatch = Stopwatch.StartNew();
                _ = InvokePreprocessImage(image, InputSize);
                preprocessStopwatch.Stop();
                preprocessMs += preprocessStopwatch.Elapsed.TotalMilliseconds;

                var postprocessStopwatch = Stopwatch.StartNew();
                var detections = InvokePostprocessYoloV8(
                    tensor,
                    ConfidenceThreshold,
                    benchmarkCase.Width,
                    benchmarkCase.Height,
                    InputSize,
                    enableNms: true,
                    nmsIou: NmsIouThreshold);
                postprocessStopwatch.Stop();
                postprocessMs += postprocessStopwatch.Elapsed.TotalMilliseconds;
                detectionCount += detections.Count;
            }

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            var expectedDetections = 4 * benchmarkCase.BatchSize;
            if (detectionCount != expectedDetections)
            {
                throw new InvalidOperationException($"Expected {expectedDetections} detections, got {detectionCount}.");
            }

            return new CaseResult(
                benchmarkCase.Id,
                benchmarkCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                Math.Round(preprocessMs, 3),
                Math.Round(postprocessMs, 3),
                detectionCount,
                benchmarkCase.Width,
                benchmarkCase.Height,
                benchmarkCase.BatchSize,
                benchmarkCase.Provider.AvailableProviders,
                benchmarkCase.Provider.RequestedProvider,
                benchmarkCase.Provider.ActiveProvider,
                benchmarkCase.Provider.FallbackToCpu,
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                benchmarkCase.Id,
                benchmarkCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                Math.Round(preprocessMs, 3),
                Math.Round(postprocessMs, 3),
                detectionCount,
                benchmarkCase.Width,
                benchmarkCase.Height,
                benchmarkCase.BatchSize,
                benchmarkCase.Provider.AvailableProviders,
                benchmarkCase.Provider.RequestedProvider,
                benchmarkCase.Provider.ActiveProvider,
                benchmarkCase.Provider.FallbackToCpu,
                ex.GetBaseException().Message);
        }
    }

    private static Mat CreateImage(int width, int height, int iteration)
    {
        var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(32 + iteration, 64, 96));
        Cv2.Rectangle(image, new Rect(width / 8, height / 7, width / 5, height / 4), new Scalar(180, 80, 40), -1);
        Cv2.Circle(image, new Point(width * 2 / 3, height / 2), Math.Max(12, Math.Min(width, height) / 10), new Scalar(40, 190, 120), -1);
        Cv2.Line(image, new Point(0, height - 1), new Point(width - 1, 0), new Scalar(220, 220, 40), 3);
        return image;
    }

    private static DenseTensor<float> CreateYoloV8Tensor()
    {
        return new DenseTensor<float>(new float[1 * (4 + ClassCount) * AnchorCount], [1, 4 + ClassCount, AnchorCount]);
    }

    private static void WriteBenchmarkDetections(DenseTensor<float> tensor, int originalWidth, int originalHeight)
    {
        var scale = Math.Min((float)InputSize / originalWidth, (float)InputSize / originalHeight);
        var xPad = (InputSize - originalWidth * scale) / 2f;
        var yPad = (InputSize - originalHeight * scale) / 2f;

        WriteBox(tensor, 0, originalWidth * 0.22f, originalHeight * 0.25f, originalWidth * 0.11f, originalHeight * 0.12f, 0, 0.91f, scale, xPad, yPad);
        WriteBox(tensor, 1, originalWidth * 0.58f, originalHeight * 0.42f, originalWidth * 0.08f, originalHeight * 0.18f, 1, 0.88f, scale, xPad, yPad);
        WriteBox(tensor, 2, originalWidth * 0.74f, originalHeight * 0.63f, originalWidth * 0.13f, originalHeight * 0.11f, 2, 0.86f, scale, xPad, yPad);
        WriteBox(tensor, 3, originalWidth * 0.37f, originalHeight * 0.72f, originalWidth * 0.09f, originalHeight * 0.13f, 1, 0.82f, scale, xPad, yPad);
    }

    private static void WriteBox(
        DenseTensor<float> tensor,
        int anchor,
        float x,
        float y,
        float width,
        float height,
        int classId,
        float confidence,
        float scale,
        float xPad,
        float yPad)
    {
        tensor[0, 0, anchor] = x * scale + xPad;
        tensor[0, 1, anchor] = y * scale + yPad;
        tensor[0, 2, anchor] = width * scale;
        tensor[0, 3, anchor] = height * scale;
        tensor[0, 4 + classId, anchor] = confidence;
    }

    private static ProviderInfo ResolveExecutionProvider()
    {
        var providers = GetAvailableOnnxProviders();
        var gpuProvider = providers.FirstOrDefault(item =>
            item.Contains("CUDA", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("TensorRT", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("Dml", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("DirectML", StringComparison.OrdinalIgnoreCase));

        return new ProviderInfo(
            AvailableProviders: providers,
            RequestedProvider: "GPU",
            ActiveProvider: gpuProvider ?? "CPUExecutionProvider",
            FallbackToCpu: gpuProvider is null);
    }

    private static string[] GetAvailableOnnxProviders()
    {
        try
        {
            var ortEnvType = Type.GetType("Microsoft.ML.OnnxRuntime.OrtEnv, Microsoft.ML.OnnxRuntime");
            var instance = ortEnvType?.GetMethod("Instance", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)?.Invoke(null, null)
                ?? ortEnvType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var providers = instance?.GetType().GetMethod("GetAvailableProviders", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, null);
            if (providers is IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Select(item => item.ToString() ?? string.Empty).Where(item => item.Length > 0).ToArray();
            }
        }
        catch
        {
            // Provider discovery is diagnostic only; benchmark execution does not depend on it.
        }

        return ["CPUExecutionProvider"];
    }

    private static DenseTensor<float> InvokePreprocessImage(Mat image, int inputSize)
    {
        return (DenseTensor<float>)InvokeInstance("PreprocessImage", image, inputSize)!;
    }

    private static List<DetectionRecord> InvokePostprocessYoloV8(
        DenseTensor<float> tensor,
        float threshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        bool enableNms,
        float nmsIou)
    {
        return ToDetectionRecords(InvokeInstanceEnumerable(
            "PostprocessYoloV8V11",
            tensor,
            threshold,
            originalWidth,
            originalHeight,
            inputSize,
            enableNms,
            nmsIou));
    }

    private static object? InvokeInstance(string methodName, params object?[] args)
    {
        var method = typeof(DeepLearningOperator).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DeepLearningOperator), methodName);
        return method.Invoke(Operator, args);
    }

    private static IEnumerable InvokeInstanceEnumerable(string methodName, params object?[] args)
    {
        return (IEnumerable)(InvokeInstance(methodName, args)
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static List<DetectionRecord> ToDetectionRecords(IEnumerable values)
    {
        return values.Cast<object>().Select(ReadDetection).ToList();
    }

    private static DetectionRecord ReadDetection(object detection)
    {
        return new DetectionRecord(
            ReadProperty<float>(detection, "X"),
            ReadProperty<float>(detection, "Y"),
            ReadProperty<float>(detection, "Width"),
            ReadProperty<float>(detection, "Height"),
            ReadProperty<float>(detection, "Confidence"),
            ReadProperty<int>(detection, "ClassId"));
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        return (T)(instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance)
            ?? throw new InvalidOperationException($"Property not found: {propertyName}"));
    }
}

internal sealed record ProviderInfo(
    string[] AvailableProviders,
    string RequestedProvider,
    string ActiveProvider,
    bool FallbackToCpu);

internal sealed record BenchmarkCase(
    string Id,
    string Scenario,
    int Width,
    int Height,
    int BatchSize,
    int Iteration,
    ProviderInfo Provider);

internal sealed record DetectionRecord(float X, float Y, float Width, float Height, float Confidence, int ClassId);

internal sealed record BenchmarkResult(
    BenchmarkSummary Summary,
    List<OperatorSummary> Operators,
    List<ScenarioSummary> Scenarios,
    List<CaseResult> Cases);

internal sealed record BenchmarkSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string[] AvailableProviders,
    string RequestedProvider,
    string ActiveProvider,
    bool FallbackToCpu,
    string Scope);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    bool HasBenchmark,
    string ActiveProvider,
    bool FallbackToCpu);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    double PreprocessMsAvg,
    double PostprocessMsAvg,
    double DetectionCountAvg);

internal sealed record CaseResult(
    string CaseId,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    double PreprocessMs,
    double PostprocessMs,
    int DetectionCount,
    int Width,
    int Height,
    int BatchSize,
    string[] AvailableProviders,
    string RequestedProvider,
    string ActiveProvider,
    bool FallbackToCpu,
    string? Failure);

internal static class MarkdownReport
{
    public static string Create(BenchmarkResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning Runtime Benchmark",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Scope: `{result.Summary.Scope}`",
            string.Empty,
            "## Provider",
            string.Empty,
            "| Metric | Value |",
            "| --- | --- |",
            $"| Requested provider | {result.Summary.RequestedProvider} |",
            $"| Active provider | {result.Summary.ActiveProvider} |",
            $"| Fallback to CPU | {result.Summary.FallbackToCpu} |",
            $"| Available providers | {string.Join(", ", result.Summary.AvailableProviders)} |",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            $"| Memory bytes | {result.Summary.MemoryAllocationBytes} |",
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Avg total ms | Avg preprocess ms | Avg postprocess ms | Avg detections |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.PreprocessMsAvg:0.###} | {item.PostprocessMsAvg:0.###} | {item.DetectionCountAvg:0.###} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Passed | Size | Batch | Total ms | Pre ms | Post ms | Detections | Failure |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Scenario} | {item.Passed} | {item.Width}x{item.Height} | {item.BatchSize} | {item.RuntimeMs:0.###} | {item.PreprocessMs:0.###} | {item.PostprocessMs:0.###} | {item.DetectionCount} | {item.Failure ?? "-"} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Notes",
            string.Empty,
            "- This benchmark uses deterministic generated images and controlled YOLOv8 tensors.",
            "- It measures DeepLearningOperator preprocessing and YOLO post-processing paths, not model accuracy.",
            "- GPU/CPU fallback is recorded from ONNX Runtime provider availability because no production ONNX model is required for this contract benchmark."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/DeepLearning_runtime_benchmark_baseline.json",
            "quality/evals/reports/DeepLearning_runtime_benchmark_baseline.md",
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (i + 1 >= args.Length)
            {
                return options with { ParseError = $"Missing value for {arg}" };
            }

            var value = args[++i];
            options = arg switch
            {
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };

            if (options.ParseError is not null)
            {
                return options;
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        Usage: dotnet run --project quality/tools/DeepLearningRuntimeBenchmarkRunner/DeepLearningRuntimeBenchmarkRunner.csproj -- [options]

        Options:
          --output <path>   Baseline JSON output path.
          --report <path>   Baseline Markdown report path.
        """);
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
