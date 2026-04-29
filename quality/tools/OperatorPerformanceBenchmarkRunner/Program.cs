using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
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

var result = await OperatorPerformanceBenchmark.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"Operator performance benchmark complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"total={result.Summary.TotalRuntimeMs:F3} ms, mode={result.Summary.Mode}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class OperatorPerformanceBenchmark
{
    public static async Task<BenchmarkResult> RunAsync(RunnerOptions options)
    {
        var specs = BuildCases(options).ToList();
        var results = new List<CaseResult>(specs.Count);

        foreach (var spec in specs)
        {
            results.Add(await RunCaseAsync(spec, options));
        }

        var failed = results.Count(item => !item.Passed);
        var operatorSummaries = results
            .GroupBy(item => item.OperatorName)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.MeanRuntimeMs), 3),
                Math.Round(group.Max(item => item.P95RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.AllocatedBytesPerIteration)),
                string.Join(", ", group.Select(item => item.Scenario).Distinct().OrderBy(item => item))))
            .ToList();

        return new BenchmarkResult(
            new BenchmarkSummary(
                DateTimeOffset.UtcNow,
                options.Mode,
                options.WarmupIterations,
                options.Iterations,
                specs.Count,
                results.Count - failed,
                failed,
                Math.Round(results.Sum(item => item.TotalRuntimeMs), 3),
                results.Sum(item => item.TotalAllocatedBytes),
                "Synthetic in-process operator benchmark intended for CI smoke and local trend checks."),
            operatorSummaries,
            results);
    }

    private static IEnumerable<CaseSpec> BuildCases(RunnerOptions options)
    {
        yield return new CaseSpec(
            "mean_filter_640x480_k5",
            "MeanFilter",
            "preprocess",
            640,
            480,
            () => new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance),
            () => CreateOperator("MeanFilter", OperatorType.MeanFilter, ("KernelSize", 5), ("BorderType", 4)),
            () => ImageInput(CreateTextureScene(640, 480, channels: 3)));

        yield return new CaseSpec(
            "caliper_tool_horizontal_edge_pair",
            "CaliperTool",
            "measurement",
            640,
            240,
            () => new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance),
            () => CreateOperator(
                "CaliperTool",
                OperatorType.CaliperTool,
                ("Direction", "Horizontal"),
                ("Polarity", "Both"),
                ("EdgeThreshold", 10.0),
                ("ExpectedCount", 1),
                ("MeasureMode", "edge_pairs"),
                ("PairDirection", "any"),
                ("SubpixelAccuracy", options.Mode.Equals("local", StringComparison.OrdinalIgnoreCase))),
            () => ImageInput(CreateCaliperScene(640, 240), new Rect(80, 60, 480, 120)));

        yield return new CaseSpec(
            "edge_detection_640x480_auto_threshold",
            "EdgeDetection",
            "edge",
            640,
            480,
            () => new CannyEdgeOperator(NullLogger<CannyEdgeOperator>.Instance),
            () => CreateOperator(
                "EdgeDetection",
                OperatorType.EdgeDetection,
                ("Threshold1", 50.0),
                ("Threshold2", 150.0),
                ("AutoThreshold", true),
                ("EnableGaussianBlur", true),
                ("GaussianKernelSize", 5),
                ("ApertureSize", 3),
                ("L2Gradient", false)),
            () => ImageInput(CreateTextureScene(640, 480, channels: 1)));

        yield return new CaseSpec(
            "translation_rotation_calibration_20_points_svd",
            "TranslationRotationCalibration",
            "calibration_geometry",
            0,
            0,
            () => new TranslationRotationCalibrationOperator(NullLogger<TranslationRotationCalibrationOperator>.Instance),
            () => CreateOperator(
                "TranslationRotationCalibration",
                OperatorType.TranslationRotationCalibration,
                ("CalibrationPoints", CalibrationPointFactory.CreateJson(20)),
                ("Method", "SVD"),
                ("SavePath", string.Empty)),
            () => new Dictionary<string, object>());
    }

    private static async Task<CaseResult> RunCaseAsync(CaseSpec spec, RunnerOptions options)
    {
        var executor = spec.CreateExecutor();
        var warmupFailures = 0;

        for (var i = 0; i < options.WarmupIterations; i++)
        {
            await using var input = BenchmarkInput.Create(spec.CreateInputs());
            var warmup = await executor.ExecuteAsync(spec.CreateOperator(), input.Values);
            if (!warmup.IsSuccess)
            {
                warmupFailures++;
            }
        }

        var samples = new List<double>(options.Iterations);
        var outputSignals = new Dictionary<string, object>();
        var totalAllocatedBytes = 0L;
        string? error = null;

        for (var i = 0; i < options.Iterations; i++)
        {
            await using var input = BenchmarkInput.Create(spec.CreateInputs());
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            var execution = await executor.ExecuteAsync(spec.CreateOperator(), input.Values);
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);

            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            totalAllocatedBytes += Math.Max(0, allocationAfter - allocationBefore);

            if (!execution.IsSuccess)
            {
                error ??= execution.ErrorMessage ?? "Operator returned failure.";
            }
            else if (outputSignals.Count == 0 && execution.OutputData is not null)
            {
                outputSignals = ExtractSignals(execution.OutputData);
            }
        }

        samples.Sort();
        var passed = warmupFailures == 0 && error is null && samples.Count == options.Iterations;
        return new CaseResult(
            spec.Id,
            spec.OperatorName,
            spec.Scenario,
            passed,
            options.Iterations,
            spec.Width,
            spec.Height,
            Math.Round(samples.Sum(), 3),
            Math.Round(samples.Average(), 3),
            Math.Round(samples[0], 3),
            Math.Round(samples[^1], 3),
            Math.Round(Percentile(samples, 0.95), 3),
            totalAllocatedBytes,
            options.Iterations > 0 ? totalAllocatedBytes / options.Iterations : 0,
            outputSignals,
            error);
    }

    private static Dictionary<string, object> ExtractSignals(Dictionary<string, object> output)
    {
        var signals = new Dictionary<string, object>();
        CopyIfPresent(output, signals, "Width");
        CopyIfPresent(output, signals, "PairCount");
        CopyIfPresent(output, signals, "EdgeCount");
        CopyIfPresent(output, signals, "Threshold1Used");
        CopyIfPresent(output, signals, "Threshold2Used");
        CopyIfPresent(output, signals, "CalibrationError");
        CopyIfPresent(output, signals, "MaxCalibrationError");
        CopyIfPresent(output, signals, "Accepted");
        return signals;
    }

    private static void CopyIfPresent(Dictionary<string, object> source, Dictionary<string, object> target, string key)
    {
        if (source.TryGetValue(key, out var value))
        {
            target[key] = value;
        }
    }

    private static double Percentile(IReadOnlyList<double> sortedSamples, double percentile)
    {
        if (sortedSamples.Count == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(percentile * sortedSamples.Count) - 1, 0, sortedSamples.Count - 1);
        return sortedSamples[index];
    }

    private static Operator CreateOperator(string name, OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(name, type, 0, 0);
        foreach (var parameter in parameters)
        {
            op.Parameters.Add(new Parameter(
                Guid.NewGuid(),
                parameter.Name,
                parameter.Name,
                string.Empty,
                InferParameterType(parameter.Value),
                parameter.Value,
                isRequired: false));
        }

        return op;
    }

    private static string InferParameterType(object value) => value switch
    {
        bool => "bool",
        int => "int",
        long => "int",
        float => "double",
        double => "double",
        _ => "string"
    };

    private static Dictionary<string, object> ImageInput(Mat image, Rect? searchRegion = null)
    {
        var input = new Dictionary<string, object> { ["Image"] = new ImageWrapper(image) };
        if (searchRegion is Rect rect)
        {
            input["SearchRegion"] = rect;
        }

        return input;
    }

    private static Mat CreateTextureScene(int width, int height, int channels)
    {
        var image = channels == 1
            ? new Mat(height, width, MatType.CV_8UC1, Scalar.All(40))
            : new Mat(height, width, MatType.CV_8UC3, new Scalar(40, 56, 72));

        Cv2.Rectangle(image, new Rect(width / 8, height / 8, width / 3, height / 4), Scalar.All(190), -1);
        Cv2.Circle(image, new Point(width * 2 / 3, height / 2), Math.Min(width, height) / 7, Scalar.All(120), -1);
        Cv2.Line(image, new Point(0, height - 1), new Point(width - 1, 0), Scalar.All(230), 2);
        Cv2.PutText(image, "CV", new Point(width / 3, height * 3 / 4), HersheyFonts.HersheySimplex, 2.0, Scalar.All(210), 3);
        return image;
    }

    private static Mat CreateCaliperScene(int width, int height)
    {
        var image = new Mat(height, width, MatType.CV_8UC1, Scalar.All(35));
        Cv2.Rectangle(image, new Rect(210, 30, 120, height - 60), Scalar.All(220), -1);
        Cv2.GaussianBlur(image, image, new Size(3, 3), 0.6);
        return image;
    }
}

internal sealed class BenchmarkInput : IAsyncDisposable
{
    private BenchmarkInput(Dictionary<string, object> values)
    {
        Values = values;
    }

    public Dictionary<string, object> Values { get; }

    public static BenchmarkInput Create(Dictionary<string, object> values) => new(values);

    public ValueTask DisposeAsync()
    {
        foreach (var value in Values.Values)
        {
            if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }
}

internal static class CalibrationPointFactory
{
    public static string CreateJson(int count)
    {
        var points = new List<Dictionary<string, double>>(count);
        const double scale = 0.25;
        const double rotationDeg = 12.0;
        const double tx = 38.0;
        const double ty = -17.0;
        var radians = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        for (var i = 0; i < count; i++)
        {
            var x = 20 + (i % 5) * 35;
            var y = 30 + (i / 5) * 28;
            var robotX = (scale * ((cos * x) - (sin * y))) + tx;
            var robotY = (scale * ((sin * x) + (cos * y))) + ty;
            points.Add(new Dictionary<string, double>
            {
                ["imageX"] = x,
                ["imageY"] = y,
                ["robotX"] = robotX,
                ["robotY"] = robotY,
                ["angle"] = rotationDeg
            });
        }

        return JsonSerializer.Serialize(points, JsonSettings.Default);
    }
}

internal static class MarkdownReport
{
    public static string Create(BenchmarkResult result)
    {
        var lines = new List<string>
        {
            "# Operator Performance Benchmark Report",
            string.Empty,
            $"- Generated UTC: {result.Summary.GeneratedAtUtc:O}",
            $"- Mode: {result.Summary.Mode}",
            $"- Warmup iterations: {result.Summary.WarmupIterations}",
            $"- Measured iterations: {result.Summary.Iterations}",
            $"- Cases: {result.Summary.Passed}/{result.Summary.CaseCount} passed",
            $"- Total runtime: {result.Summary.TotalRuntimeMs:F3} ms",
            $"- Total allocated bytes: {result.Summary.TotalAllocatedBytes}",
            string.Empty,
            "## Operator Summary",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Mean ms | P95 ms | Alloc/iter bytes | Scenarios |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        };

        foreach (var item in result.Operators)
        {
            lines.Add($"| {item.OperatorName} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.MeanRuntimeMs:F3} | {item.P95RuntimeMs:F3} | {item.AllocatedBytesPerIteration} | {item.Scenarios} |");
        }

        lines.AddRange([
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Operator | Scenario | Mean ms | Min ms | Max ms | P95 ms | Alloc/iter bytes | Passed | Error |",
            "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        ]);

        foreach (var item in result.Cases)
        {
            lines.Add($"| {item.CaseId} | {item.OperatorName} | {item.Scenario} | {item.MeanRuntimeMs:F3} | {item.MinRuntimeMs:F3} | {item.MaxRuntimeMs:F3} | {item.P95RuntimeMs:F3} | {item.AllocatedBytesPerIteration} | {item.Passed} | {item.ErrorMessage ?? string.Empty} |");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    string Mode,
    int WarmupIterations,
    int Iterations,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "artifacts/operator-performance-benchmark.json";
        string? report = null;
        var mode = "smoke";
        int? warmup = null;
        int? iterations = null;
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
                    if (!TryReadValue(args, ref i, out output))
                    {
                        return Invalid("Missing value for --output.");
                    }
                    break;
                case "--report":
                    if (!TryReadValue(args, ref i, out report))
                    {
                        return Invalid("Missing value for --report.");
                    }
                    break;
                case "--mode":
                    if (!TryReadValue(args, ref i, out mode))
                    {
                        return Invalid("Missing value for --mode.");
                    }
                    break;
                case "--warmup":
                    if (!TryReadInt(args, ref i, out warmup))
                    {
                        return Invalid("Invalid value for --warmup.");
                    }
                    break;
                case "--iterations":
                    if (!TryReadInt(args, ref i, out iterations))
                    {
                        return Invalid("Invalid value for --iterations.");
                    }
                    break;
                default:
                    return Invalid($"Unknown argument: {arg}");
            }
        }

        if (!mode.Equals("smoke", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("--mode must be smoke or local.");
        }

        warmup ??= mode.Equals("local", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
        iterations ??= mode.Equals("local", StringComparison.OrdinalIgnoreCase) ? 25 : 3;
        if (warmup < 0 || iterations <= 0)
        {
            return Invalid("--warmup must be >= 0 and --iterations must be > 0.");
        }

        return new RunnerOptions(output, report, mode.ToLowerInvariant(), warmup.Value, iterations.Value, showHelp, null);

        RunnerOptions Invalid(string error) => new(output, report, mode, warmup ?? 1, iterations ?? 3, showHelp, error);
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
        {
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInt(string[] args, ref int index, out int? value)
    {
        value = null;
        return TryReadValue(args, ref index, out var raw) && int.TryParse(raw, out var parsed) && (value = parsed) is not null;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            Usage: dotnet run --project quality/tools/OperatorPerformanceBenchmarkRunner/OperatorPerformanceBenchmarkRunner.csproj -- [options]

            Options:
              --output <path>       JSON result output path. Default: artifacts/operator-performance-benchmark.json
              --report <path>       Markdown report output path.
              --mode <smoke|local>  smoke is CI-friendly; local runs more iterations. Default: smoke
              --warmup <count>      Override warmup iterations.
              --iterations <count>  Override measured iterations.
              -h, --help            Show help.
            """);
    }
}

internal sealed record CaseSpec(
    string Id,
    string OperatorName,
    string Scenario,
    int Width,
    int Height,
    Func<OperatorBase> CreateExecutor,
    Func<Operator> CreateOperator,
    Func<Dictionary<string, object>> CreateInputs);

internal sealed record BenchmarkResult(
    BenchmarkSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<CaseResult> Cases);

internal sealed record BenchmarkSummary(
    DateTimeOffset GeneratedAtUtc,
    string Mode,
    int WarmupIterations,
    int Iterations,
    int CaseCount,
    int Passed,
    int Failed,
    double TotalRuntimeMs,
    long TotalAllocatedBytes,
    string Notes);

internal sealed record OperatorSummary(
    string OperatorName,
    int CaseCount,
    int Passed,
    int Failed,
    double MeanRuntimeMs,
    double P95RuntimeMs,
    long AllocatedBytesPerIteration,
    string Scenarios);

internal sealed record CaseResult(
    string CaseId,
    string OperatorName,
    string Scenario,
    bool Passed,
    int Iterations,
    int Width,
    int Height,
    double TotalRuntimeMs,
    double MeanRuntimeMs,
    double MinRuntimeMs,
    double MaxRuntimeMs,
    double P95RuntimeMs,
    long TotalAllocatedBytes,
    long AllocatedBytesPerIteration,
    IReadOnlyDictionary<string, object> OutputSignals,
    string? ErrorMessage);

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Indented = new(Default)
    {
        WriteIndented = true
    };
}
