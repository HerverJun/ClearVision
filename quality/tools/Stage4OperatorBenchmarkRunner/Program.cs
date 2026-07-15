using System.Collections;
using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Memory;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using PointCloudModel = ClearVision.Product.Infrastructure.PointCloud.PointCloud;

var options = RunnerOptions.Parse(args);
if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    return 2;
}

var result = await Stage4Benchmark.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonOptions.Indented));
if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine($"Stage 4 benchmark complete: {result.Cases.Count(item => item.Passed)}/{result.Cases.Count} passed; output={options.OutputPath}");
return result.Cases.All(item => item.Passed) ? 0 : 1;

internal static class Stage4Benchmark
{
    public static async Task<BenchmarkResult> RunAsync(RunnerOptions options)
    {
        var cases = new[]
        {
            new CaseSpec(
                "translation_rotation_none_40_points",
                "TranslationRotationCalibration",
                "40 point similarity transform; RobustMode=None",
                "40 point pairs",
                SourceObservedCoreInvocations: 1,
                () => new TranslationRotationCalibrationOperator(NullLogger<TranslationRotationCalibrationOperator>.Instance),
                CreateCalibrationOperator,
                static () => new Dictionary<string, object>()),
            new CaseSpec(
                "euclidean_cluster_6000_materialized",
                "EuclideanClusterExtraction",
                "three deterministic clusters; indices and PointCloud outputs materialized",
                "6000 XYZRGB points; 3 x 2000",
                SourceObservedCoreInvocations: 2,
                () => new EuclideanClusterExtractionOperator(NullLogger<EuclideanClusterExtractionOperator>.Instance),
                () => CreateClusterOperator(materializePointClouds: true),
                CreateClusterInput),
            new CaseSpec(
                "euclidean_cluster_6000_indices_only",
                "EuclideanClusterExtraction",
                "three deterministic clusters; indices only",
                "6000 XYZRGB points; 3 x 2000",
                SourceObservedCoreInvocations: 1,
                () => new EuclideanClusterExtractionOperator(NullLogger<EuclideanClusterExtractionOperator>.Instance),
                () => CreateClusterOperator(materializePointClouds: false),
                CreateClusterInput)
        };

        var results = new List<CaseResult>(cases.Length);
        foreach (var spec in cases)
        {
            results.Add(await RunCaseAsync(spec, options));
        }

        return new BenchmarkResult(
            new BenchmarkEnvironment(
                options.Label,
                options.SourceSha,
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.OSVersion.ToString(),
                Environment.Version.ToString(),
                Environment.ProcessorCount,
                Environment.Is64BitProcess,
                GCSettings.IsServerGC,
                options.WarmupIterations,
                options.Iterations,
                "Inputs are created before timing. Each case is warmed up, then measured sequentially. P50/P95 use nearest-rank sorted samples. Allocations use process-wide GC.GetTotalAllocatedBytes(true). Memory is sampled before output disposal."),
            results,
            await CollectCalibrationRobustnessEvidence());
    }

    private static async Task<IReadOnlyList<CalibrationScenarioEvidence>> CollectCalibrationRobustnessEvidence()
    {
        var scenarios = new[]
        {
            (Name: "no_noise", Points: CalibrationPointFactory.CreateJson(20)),
            (Name: "single_outlier", Points: CalibrationPointFactory.CreateJson(20, new Dictionary<int, (double X, double Y)> { [7] = (40.0, -35.0) })),
            (Name: "multiple_outliers", Points: CalibrationPointFactory.CreateJson(30, new Dictionary<int, (double X, double Y)> { [2] = (80.0, -60.0), [11] = (-55.0, 75.0), [25] = (100.0, 40.0) }))
        };
        var evidence = new List<CalibrationScenarioEvidence>(scenarios.Length);
        foreach (var scenario in scenarios)
        {
            var modes = new List<CalibrationModeEvidence>();
            foreach (var mode in new[] { "None", "Ransac", "Huber" })
            {
                var executor = new TranslationRotationCalibrationOperator(NullLogger<TranslationRotationCalibrationOperator>.Instance);
                var op = CreateCalibrationOperator(scenario.Points, mode);
                var execution = await executor.ExecuteAsync(op, null);
                var error = execution.ErrorMessage ?? string.Empty;
                if (!execution.IsSuccess || execution.OutputData is null ||
                    !CalibrationBundleV2Json.TryDeserialize(Convert.ToString(execution.OutputData.GetValueOrDefault("CalibrationData")) ?? string.Empty, out var bundle, out error) ||
                    bundle.Transform2D is null)
                {
                    modes.Add(new CalibrationModeEvidence(mode, false, 0, 0, 0, string.IsNullOrWhiteSpace(error) ? "Calibration evidence execution failed." : error));
                    continue;
                }

                modes.Add(new CalibrationModeEvidence(
                    mode,
                    true,
                    MatrixError(bundle.Transform2D.Matrix, CalibrationPointFactory.ExpectedMatrix()),
                    Convert.ToDouble(execution.OutputData["CalibrationError"]),
                    Convert.ToInt32(execution.OutputData["OutlierCount"]),
                    null));
            }
            evidence.Add(new CalibrationScenarioEvidence(scenario.Name, modes));
        }
        return evidence;
    }

    private static double MatrixError(double[][] actual, double[][] expected) =>
        Math.Sqrt(actual.SelectMany((row, i) => row.Select((value, j) => Math.Pow(value - expected[i][j], 2))).Sum());

    private static async Task<CaseResult> RunCaseAsync(CaseSpec spec, RunnerOptions options)
    {
        var executor = spec.CreateExecutor();
        for (var i = 0; i < options.WarmupIterations; i++)
        {
            await using var input = BenchmarkInput.Create(spec.CreateInputs());
            var execution = await executor.ExecuteAsync(spec.CreateOperator(), input.Values);
            DisposeOutput(execution.OutputData);
            if (!execution.IsSuccess)
            {
                return CaseResult.Failure(spec, options.Iterations, execution.ErrorMessage ?? "Warmup failed.");
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var startWorkingSet = process.WorkingSet64;
        var startManagedHeap = GC.GetTotalMemory(forceFullCollection: false);
        var peakWorkingSet = startWorkingSet;
        var peakManagedHeap = startManagedHeap;
        var samples = new List<double>(options.Iterations);
        var totalAllocatedBytes = 0L;
        var observedCoreInvocations = new List<int>();
        var signals = new Dictionary<string, object>();
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

            if (execution.OutputData is not null)
            {
                if (execution.OutputData.TryGetValue("CoreInvocationCount", out var countValue))
                {
                    observedCoreInvocations.Add(Convert.ToInt32(countValue));
                }

                if (signals.Count == 0)
                {
                    CopySignal(execution.OutputData, signals, "CalibrationError");
                    CopySignal(execution.OutputData, signals, "MaxCalibrationError");
                    CopySignal(execution.OutputData, signals, "ClusterCount");
                    CopySignal(execution.OutputData, signals, "PointCloudsMaterialized");
                    CopySignal(execution.OutputData, signals, "RobustMode");
                }
            }

            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakManagedHeap = Math.Max(peakManagedHeap, GC.GetTotalMemory(forceFullCollection: false));
            DisposeOutput(execution.OutputData);
        }

        process.Refresh();
        samples.Sort();
        var coreCount = observedCoreInvocations.Count == options.Iterations && observedCoreInvocations.Distinct().Count() == 1
            ? observedCoreInvocations[0]
            : spec.SourceObservedCoreInvocations;
        var coreCountEvidence = observedCoreInvocations.Count == options.Iterations
            ? "runtime-output"
            : "source-observed-legacy-path";

        return new CaseResult(
            spec.Id,
            spec.OperatorName,
            spec.Scenario,
            spec.InputScale,
            error is null && samples.Count == options.Iterations,
            options.Iterations,
            Math.Round(Percentile(samples, 0.50), 4),
            Math.Round(Percentile(samples, 0.95), 4),
            Math.Round(samples.Average(), 4),
            totalAllocatedBytes,
            totalAllocatedBytes / Math.Max(1, options.Iterations),
            startManagedHeap,
            peakManagedHeap,
            Math.Max(0, peakManagedHeap - startManagedHeap),
            startWorkingSet,
            peakWorkingSet,
            Math.Max(0, peakWorkingSet - startWorkingSet),
            coreCount,
            coreCountEvidence,
            signals,
            error);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static Operator CreateCalibrationOperator() => CreateCalibrationOperator(CalibrationPointFactory.CreateJson(40), "None");

    private static Operator CreateCalibrationOperator(string pointsJson, string robustMode)
    {
        var op = new Operator("TranslationRotationCalibration", OperatorType.TranslationRotationCalibration, 0, 0);
        AddParameter(op, "CalibrationPoints", pointsJson, "string");
        AddParameter(op, "Method", "LeastSquares", "string");
        AddParameter(op, "RobustMode", robustMode, "string");
        AddParameter(op, "RobustResidualThreshold", 0.30, "double");
        AddParameter(op, "HuberDelta", 0.15, "double");
        AddParameter(op, "RobustMaxIterations", 256, "int");
        AddParameter(op, "RobustMinInlierRatio", 0.5, "double");
        AddParameter(op, "SavePath", string.Empty, "string");
        return op;
    }

    private static Operator CreateClusterOperator(bool materializePointClouds)
    {
        var op = new Operator("EuclideanClusterExtraction", OperatorType.EuclideanClusterExtraction, 0, 0);
        AddParameter(op, "ClusterTolerance", 0.007, "double");
        AddParameter(op, "MinClusterSize", 100, "int");
        AddParameter(op, "MaxClusterSize", 10_000, "int");
        AddParameter(op, "MaterializePointClouds", materializePointClouds, "bool");
        return op;
    }

    private static Dictionary<string, object> CreateClusterInput()
    {
        const int clusterCount = 3;
        const int pointsPerCluster = 2000;
        const int total = clusterCount * pointsPerCluster;
        var pool = MatPool.Shared;
        var points = pool.Rent(width: 3, height: total, type: MatType.CV_32FC1);
        var colors = pool.Rent(width: 3, height: total, type: MatType.CV_8UC1);
        var p = points.GetGenericIndexer<float>();
        var c = colors.GetGenericIndexer<byte>();

        for (var cluster = 0; cluster < clusterCount; cluster++)
        {
            var centerX = (cluster - 1) * 0.30f;
            for (var local = 0; local < pointsPerCluster; local++)
            {
                var row = (cluster * pointsPerCluster) + local;
                var x = local % 20;
                var y = (local / 20) % 10;
                var z = local / 200;
                p[row, 0] = centerX + ((x - 9.5f) * 0.004f);
                p[row, 1] = (y - 4.5f) * 0.004f;
                p[row, 2] = (z - 4.5f) * 0.004f;
                c[row, 0] = (byte)(50 + (cluster * 70));
                c[row, 1] = (byte)(120 + cluster);
                c[row, 2] = (byte)(200 - (cluster * 40));
            }
        }

        return new Dictionary<string, object>
        {
            ["PointCloud"] = new PointCloudModel(points, colors, normals: null, isOrganized: false, pool: pool)
        };
    }

    private static void AddParameter(Operator op, string name, object value, string type)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, type, value));
    }

    private static void CopySignal(Dictionary<string, object> source, Dictionary<string, object> destination, string key)
    {
        if (source.TryGetValue(key, out var value))
        {
            destination[key] = value;
        }
    }

    private static void DisposeOutput(Dictionary<string, object>? output)
    {
        if (output is null)
        {
            return;
        }

        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var value in output.Values)
        {
            DisposeValue(value, disposed);
        }
    }

    private static void DisposeValue(object? value, HashSet<object> disposed)
    {
        if (value is null || value is string || !disposed.Add(value))
        {
            return;
        }

        if (value is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                DisposeValue(item, disposed);
            }
        }
    }
}

internal sealed class BenchmarkInput : IAsyncDisposable
{
    private BenchmarkInput(Dictionary<string, object> values) => Values = values;

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
    public static string CreateJson(int count, IReadOnlyDictionary<int, (double X, double Y)>? outliers = null)
    {
        const double scale = 0.25;
        const double rotationDeg = 12.0;
        const double tx = 38.0;
        const double ty = -17.0;
        var radians = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var points = new List<object>(count);

        for (var i = 0; i < count; i++)
        {
            var x = 20 + (i % 8) * 35;
            var y = 30 + (i / 8) * 28;
            var robotX = (scale * ((cos * x) - (sin * y))) + tx;
            var robotY = (scale * ((sin * x) + (cos * y))) + ty;
            if (outliers is not null && outliers.TryGetValue(i, out var offset))
            {
                robotX += offset.X;
                robotY += offset.Y;
            }
            points.Add(new
            {
                imageX = x,
                imageY = y,
                robotX,
                robotY,
                angle = rotationDeg
            });
        }

        return JsonSerializer.Serialize(points);
    }

    public static double[][] ExpectedMatrix()
    {
        const double scale = 0.25;
        var radians = 12.0 * Math.PI / 180.0;
        return
        [
            [scale * Math.Cos(radians), -scale * Math.Sin(radians), 38.0],
            [scale * Math.Sin(radians), scale * Math.Cos(radians), -17.0]
        ];
    }
}

internal static class MarkdownReport
{
    public static string Create(BenchmarkResult result)
    {
        var lines = new List<string>
        {
            "# Stage 4 Operator Performance Evidence",
            string.Empty,
            $"- Label: {result.Environment.Label}",
            $"- Source SHA: `{result.Environment.SourceSha}`",
            $"- Generated UTC: {result.Environment.GeneratedAtUtc:O}",
            $"- Environment: {result.Environment.MachineName}; {result.Environment.OperatingSystem}; .NET {result.Environment.FrameworkVersion}; CPU logical cores {result.Environment.ProcessorCount}; Server GC {result.Environment.ServerGc}",
            $"- Warmup / measured iterations: {result.Environment.WarmupIterations} / {result.Environment.Iterations}",
            $"- Method: {result.Environment.Method}",
            string.Empty,
            "| Case | Input | P50 ms | P95 ms | Alloc/iter bytes | Managed peak delta | Working-set peak delta | Core calls | Evidence | Passed |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        };

        foreach (var item in result.Cases)
        {
            lines.Add($"| {item.CaseId} | {item.InputScale} | {item.P50Milliseconds:F4} | {item.P95Milliseconds:F4} | {item.AllocatedBytesPerIteration} | {item.ManagedHeapPeakDeltaBytes} | {item.WorkingSetPeakDeltaBytes} | {item.CoreInvocationCount} | {item.CoreInvocationEvidence} | {item.Passed} |");
        }

        lines.AddRange([string.Empty, "## Robust calibration quality", string.Empty,
            "| Scenario | Mode | Transform error | Inlier RMS | Outliers | Passed | Error |",
            "| --- | --- | ---: | ---: | ---: | --- | --- |"]);
        foreach (var scenario in result.CalibrationRobustness)
        foreach (var mode in scenario.Modes)
        {
            lines.Add($"| {scenario.Scenario} | {mode.Mode} | {mode.TransformError:G6} | {mode.InlierRmsError:G6} | {mode.OutlierCount} | {mode.Passed} | {mode.ErrorMessage ?? string.Empty} |");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string Label, string SourceSha, string OutputPath, string? ReportPath, int WarmupIterations, int Iterations, string? Error)
{
    public static RunnerOptions Parse(string[] args)
    {
        var label = "unspecified";
        var sourceSha = "unknown";
        var output = ".tmp/stage4-benchmark.json";
        string? report = null;
        var warmup = 5;
        var iterations = 50;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return new(label, sourceSha, output, report, warmup, iterations, $"Missing value for {arg}.");
            }

            var value = args[++i];
            switch (arg)
            {
                case "--label": label = value; break;
                case "--source-sha": sourceSha = value; break;
                case "--output": output = value; break;
                case "--report": report = value; break;
                case "--warmup" when int.TryParse(value, out var parsedWarmup): warmup = parsedWarmup; break;
                case "--iterations" when int.TryParse(value, out var parsedIterations): iterations = parsedIterations; break;
                default: return new(label, sourceSha, output, report, warmup, iterations, $"Unknown or invalid argument: {arg} {value}");
            }
        }

        return warmup >= 0 && iterations > 0
            ? new(label, sourceSha, output, report, warmup, iterations, null)
            : new(label, sourceSha, output, report, warmup, iterations, "Warmup must be >= 0 and iterations must be > 0.");
    }
}

internal sealed record CaseSpec(
    string Id,
    string OperatorName,
    string Scenario,
    string InputScale,
    int SourceObservedCoreInvocations,
    Func<OperatorBase> CreateExecutor,
    Func<Operator> CreateOperator,
    Func<Dictionary<string, object>> CreateInputs);

internal sealed record BenchmarkResult(
    BenchmarkEnvironment Environment,
    IReadOnlyList<CaseResult> Cases,
    IReadOnlyList<CalibrationScenarioEvidence> CalibrationRobustness);

internal sealed record CalibrationScenarioEvidence(string Scenario, IReadOnlyList<CalibrationModeEvidence> Modes);

internal sealed record CalibrationModeEvidence(
    string Mode,
    bool Passed,
    double TransformError,
    double InlierRmsError,
    int OutlierCount,
    string? ErrorMessage);

internal sealed record BenchmarkEnvironment(
    string Label,
    string SourceSha,
    DateTimeOffset GeneratedAtUtc,
    string MachineName,
    string OperatingSystem,
    string FrameworkVersion,
    int ProcessorCount,
    bool Is64BitProcess,
    bool ServerGc,
    int WarmupIterations,
    int Iterations,
    string Method);

internal sealed record CaseResult(
    string CaseId,
    string OperatorName,
    string Scenario,
    string InputScale,
    bool Passed,
    int Iterations,
    double P50Milliseconds,
    double P95Milliseconds,
    double MeanMilliseconds,
    long TotalAllocatedBytes,
    long AllocatedBytesPerIteration,
    long ManagedHeapStartBytes,
    long ManagedHeapPeakBytes,
    long ManagedHeapPeakDeltaBytes,
    long WorkingSetStartBytes,
    long WorkingSetPeakBytes,
    long WorkingSetPeakDeltaBytes,
    int CoreInvocationCount,
    string CoreInvocationEvidence,
    IReadOnlyDictionary<string, object> OutputSignals,
    string? ErrorMessage)
{
    public static CaseResult Failure(CaseSpec spec, int iterations, string error) => new(
        spec.Id, spec.OperatorName, spec.Scenario, spec.InputScale, false, iterations,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        spec.SourceObservedCoreInvocations, "source-observed-legacy-path",
        new Dictionary<string, object>(), error);
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
