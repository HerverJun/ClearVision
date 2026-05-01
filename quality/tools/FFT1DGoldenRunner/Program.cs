using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    $"FFT1D golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var caseDirs = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Where(dir => Path.GetFileName(dir)!.StartsWith("FFT1D_", StringComparison.OrdinalIgnoreCase))
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

        try
        {
            var op = new Operator("FFT1D", OperatorType.FFT1D, 0, 0);
            var inputs = CreateInputs(inputJson, caseDir);
            var executor = new FFT1DOperator(NullLogger<FFT1DOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var runtimeMs = stopwatch.Elapsed.TotalMilliseconds;
            var memoryBytes = Math.Max(0, allocationAfter - allocationBefore);

            if (!execution.IsSuccess)
            {
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, runtimeMs, memoryBytes, execution.ErrorMessage ?? "Execution failed.");
            }

            var metrics = Evaluate(execution.OutputData, expectedJson["expected"], inputJson, scenario);
            var passed = IsPassing(metrics, scenario);

            ReleaseImageOutputs(execution.OutputData);
            // Do not release input ImageWrappers here to avoid double-release
            // (the operator may have taken ownership or the GC will clean up)

            return new CaseResult(caseId, operatorName, scenario, inputPath, passed, Math.Round(runtimeMs, 3), memoryBytes, null, metrics);
        }
        catch (Exception ex)
        {
            return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, ex.Message);
        }
    }

    private static Dictionary<string, object> CreateInputs(JsonNode inputJson, string caseDir)
    {
        var inputsNode = inputJson["inputs"] ?? throw new InvalidOperationException("Missing inputs");
        var inputType = inputsNode["input_type"]?.GetValue<string>() ?? "signal";
        var inputs = new Dictionary<string, object>();

        if (inputType == "signal")
        {
            var signalArray = inputsNode["signal"]?.AsArray() ?? throw new InvalidOperationException("Missing signal");
            var signal = signalArray.Select(node => node?.GetValue<double>() ?? 0.0).ToArray();
            inputs["Input"] = signal;
            return inputs;
        }

        // image input
        var imageFile = inputsNode["image"]?.GetValue<string>() ?? "input_image.png";
        var imagePath = Path.Combine(caseDir, imageFile);
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image not found: {imagePath}");

        var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (mat == null || mat.Empty())
            throw new InvalidOperationException("Failed to load image.");

        inputs["Input"] = new ImageWrapper(mat);
        return inputs;
    }

    private static Dictionary<string, object> Evaluate(
        Dictionary<string, object>? outputData,
        JsonNode? expectedNode,
        JsonNode inputJson,
        string scenario)
    {
        var metrics = new Dictionary<string, object>
        {
            ["DominantIndexError"] = int.MaxValue,
            ["DcMagnitudeError"] = double.MaxValue,
            ["MaxMagnitudeError"] = double.MaxValue,
            ["ReconstructionRmse"] = double.MaxValue,
            ["IsFinite"] = false,
            ["OutputShapeCorrect"] = false,
        };

        if (outputData is null || expectedNode is null)
            return metrics;

        var inputsNode = inputJson["inputs"];
        var inputTypeNode = inputsNode?["input_type"];
        var inputType = inputTypeNode?.GetValue<string>() ?? "signal";

        if (inputType == "signal")
        {
            if (!outputData.TryGetValue("Spectrum", out var spectrumObj) || spectrumObj is not Complex[] spectrum)
                return metrics;
            if (!outputData.TryGetValue("Magnitude", out var magObj) || magObj is not double[] magnitudes)
                return metrics;

            var isFinite = magnitudes.All(double.IsFinite) && spectrum.All(c => double.IsFinite(c.Real) && double.IsFinite(c.Imaginary));
            metrics["IsFinite"] = isFinite;
            metrics["OutputShapeCorrect"] = magnitudes.Length > 0;

            if (magnitudes.Length > 0)
            {
                var actualDominantIndex = 0;
                var maxMag = magnitudes[0];
                for (var i = 1; i < magnitudes.Length; i++)
                {
                    if (magnitudes[i] > maxMag)
                    {
                        maxMag = magnitudes[i];
                        actualDominantIndex = i;
                    }
                }
                var expectedDominantIndex = expectedNode["dominant_index"]?.GetValue<int>() ?? 0;
                var n = magnitudes.Length;
                // For real-valued signals, FFT magnitude is symmetric: |X[k]| == |X[N-k]|.
                // Tiny floating-point differences between OpenCV DFT and numpy FFT may
                // cause argmax to pick the conjugate bin. Accept either.
                var errDirect = Math.Abs(actualDominantIndex - expectedDominantIndex);
                var errConjugate = Math.Abs(actualDominantIndex - ((n - expectedDominantIndex) % n));
                metrics["DominantIndexError"] = Math.Min(errDirect, errConjugate);

                var actualDcMagnitude = magnitudes[0];
                var expectedDcMagnitude = expectedNode["dc_magnitude"]?.GetValue<double>() ?? 0.0;
                metrics["DcMagnitudeError"] = expectedDcMagnitude == 0.0
                    ? Math.Abs(actualDcMagnitude)
                    : Math.Abs(actualDcMagnitude - expectedDcMagnitude) / Math.Abs(expectedDcMagnitude);

                var actualMaxMagnitude = magnitudes.Max();
                var expectedMaxMagnitude = expectedNode["max_magnitude"]?.GetValue<double>() ?? 0.0;
                metrics["MaxMagnitudeError"] = expectedMaxMagnitude == 0.0
                    ? Math.Abs(actualMaxMagnitude)
                    : Math.Abs(actualMaxMagnitude - expectedMaxMagnitude) / Math.Abs(expectedMaxMagnitude);
            }

            // Round-trip via InverseFFT1DOperator
            var originalSignal = inputJson["inputs"]?["signal"]?.AsArray();
            if (originalSignal != null && spectrum.Length > 0)
            {
                var original = originalSignal.Select(node => node?.GetValue<double>() ?? 0.0).ToArray();
                var rmse = ComputeRoundTripRmse(spectrum, original);
                metrics["ReconstructionRmse"] = rmse;
            }
        }
        else
        {
            // image input
            if (outputData.TryGetValue("Spectrum", out var spectrumImg) && spectrumImg is ImageWrapper wrapper)
            {
                var mat = wrapper.GetMat();
                var isFinite = !mat.Empty() && mat.Channels() == 2;
                metrics["IsFinite"] = isFinite;

                var expectedShape = expectedNode["image_shape"]?.AsArray();
                if (expectedShape != null && expectedShape.Count == 2)
                {
                    var expectedH = expectedShape[0]?.GetValue<int>() ?? 0;
                    var expectedW = expectedShape[1]?.GetValue<int>() ?? 0;
                    metrics["OutputShapeCorrect"] = mat.Rows == expectedH && mat.Cols == expectedW;
                }
            }
        }

        return metrics;
    }

    private static double ComputeRoundTripRmse(Complex[] spectrum, double[] original)
    {
        try
        {
            var inverseOp = new InverseFFT1DOperator(NullLogger<InverseFFT1DOperator>.Instance);
            var inverseInputs = new Dictionary<string, object> { ["Spectrum"] = spectrum };
            var inverseResult = inverseOp.ExecuteAsync(new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0), inverseInputs).Result;

            if (!inverseResult.IsSuccess || inverseResult.OutputData == null)
                return double.MaxValue;

            if (!inverseResult.OutputData.TryGetValue("Signal", out var signalObj) || signalObj is not double[] reconstructed)
            {
                ReleaseImageOutputs(inverseResult.OutputData);
                return double.MaxValue;
            }

            ReleaseImageOutputs(inverseResult.OutputData);

            if (reconstructed.Length != original.Length)
                return double.MaxValue;

            var sq = 0.0;
            for (var i = 0; i < original.Length; i++)
            {
                var diff = original[i] - reconstructed[i];
                sq += diff * diff;
            }

            return Math.Sqrt(sq / original.Length);
        }
        catch
        {
            return double.MaxValue;
        }
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics, string scenario)
    {
        var isFinite = metrics.TryGetValue("IsFinite", out var fin) && fin is bool b && b;
        if (!isFinite)
            return false;

        var domErr = Convert.ToInt32(metrics["DominantIndexError"]);
        var dcErr = Convert.ToDouble(metrics["DcMagnitudeError"]);
        var maxErr = Convert.ToDouble(metrics["MaxMagnitudeError"]);
        var rmse = Convert.ToDouble(metrics["ReconstructionRmse"]);

        if (scenario == "image_2d")
        {
            var shapeCorrect = metrics.TryGetValue("OutputShapeCorrect", out var sc) && sc is bool scb && scb;
            return shapeCorrect;
        }

        if (scenario == "random_noise")
        {
            // For noise, dominant index and magnitudes are not deterministic
            return rmse <= 1e-4;
        }

        if (scenario != "square_wave" && scenario != "impulse" && domErr > 0)
            return false;

        if (scenario != "random_noise" && dcErr > 1e-3)
            return false;

        if (scenario != "random_noise" && maxErr > 1e-3)
            return false;

        if (rmse > 1e-4)
            return false;

        return true;
    }

    private static async Task<JsonNode> ReadJsonAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
    }

    private static void ReleaseImageOutputs(Dictionary<string, object>? data)
    {
        if (data is null) return;
        foreach (var image in data.Values.OfType<ImageWrapper>().Distinct(ReferenceEqualityComparer<ImageWrapper>.Instance))
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
            "# FFT1D Golden Runner Report",
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
        var casesRoot = "quality/synthetic/cases/fft";
        var output = "quality/evals/reports/FFT1D_baseline.json";
        string? report = "quality/evals/reports/FFT1D_baseline.md";
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
            FFT1D golden runner

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
