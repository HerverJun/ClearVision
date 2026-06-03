using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
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
await File.WriteAllTextAsync(
    options.OutputPath,
    JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"InverseFFT1D golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var caseDirs = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Where(dir => Path.GetFileName(dir)!.StartsWith("InverseFFT1D_", StringComparison.OrdinalIgnoreCase))
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

        InputBundle? bundle = null;
        Dictionary<string, object>? outputData = null;
        try
        {
            var op = new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0);
            bundle = CreateInputs(inputJson, caseDir);
            var executor = new InverseFFT1DOperator(NullLogger<InverseFFT1DOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, bundle.Inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var runtimeMs = stopwatch.Elapsed.TotalMilliseconds;
            var memoryBytes = Math.Max(0, allocationAfter - allocationBefore);

            if (!execution.IsSuccess)
            {
                return CaseResult.Failed(caseId, operatorName, scenario, inputPath, runtimeMs, memoryBytes, execution.ErrorMessage ?? "Execution failed.");
            }

            outputData = execution.OutputData;
            var metrics = Evaluate(outputData, expectedJson["expected"], inputJson, caseDir, scenario);
            var passed = IsPassing(metrics, scenario);

            return new CaseResult(caseId, operatorName, scenario, inputPath, passed, Math.Round(runtimeMs, 3), memoryBytes, null, metrics);
        }
        catch (Exception ex)
        {
            return CaseResult.Failed(caseId, operatorName, scenario, inputPath, 0, 0, ex.Message);
        }
        finally
        {
            ReleaseImageOutputs(outputData);
            bundle?.Release();
        }
    }

    private static InputBundle CreateInputs(JsonNode inputJson, string caseDir)
    {
        var inputsNode = inputJson["inputs"] ?? throw new InvalidOperationException("Missing inputs");
        var inputType = inputsNode["input_type"]?.GetValue<string>() ?? "complex_array";
        var inputs = new Dictionary<string, object>();
        var ownedImages = new List<ImageWrapper>();

        if (inputType == "complex_array")
        {
            var spectrumNode = inputsNode["spectrum"] ?? throw new InvalidOperationException("Missing spectrum");
            var real = RequiredDoubleArray(spectrumNode, "real");
            var imaginary = RequiredDoubleArray(spectrumNode, "imaginary");
            if (real.Length != imaginary.Length)
                throw new InvalidOperationException("Spectrum real/imaginary arrays must have the same length.");

            var spectrum = new Complex[real.Length];
            for (var i = 0; i < real.Length; i++)
            {
                spectrum[i] = new Complex(real[i], imaginary[i]);
            }

            inputs["Spectrum"] = spectrum;
            if (inputsNode["output_size"] is JsonNode outputSizeNode)
            {
                inputs["OutputSize"] = outputSizeNode.GetValue<int>();
            }

            return new InputBundle(inputs, ownedImages);
        }

        if (inputType == "image_source")
        {
            var imageFile = inputsNode["image"]?.GetValue<string>() ?? "input_image.png";
            var imagePath = Path.Combine(caseDir, imageFile);
            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image not found: {imagePath}");

            var sourceMat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (sourceMat == null || sourceMat.Empty())
                throw new InvalidOperationException("Failed to load image.");

            var sourceWrapper = new ImageWrapper(sourceMat);
            ownedImages.Add(sourceWrapper);

            var fft = new FFT1DOperator(NullLogger<FFT1DOperator>.Instance);
            var fftResult = fft.ExecuteAsync(new Operator("FFT1D", OperatorType.FFT1D, 0, 0), new Dictionary<string, object>
            {
                ["Input"] = sourceWrapper
            }).Result;

            if (!fftResult.IsSuccess || fftResult.OutputData is null)
                throw new InvalidOperationException(fftResult.ErrorMessage ?? "Failed to create image spectrum.");

            foreach (var wrapper in fftResult.OutputData.Values.OfType<ImageWrapper>())
            {
                ownedImages.Add(wrapper);
            }

            if (!fftResult.OutputData.TryGetValue("Spectrum", out var spectrum) || spectrum is not ImageWrapper)
                throw new InvalidOperationException("FFT image spectrum was not produced.");

            inputs["Spectrum"] = spectrum;
            return new InputBundle(inputs, ownedImages);
        }

        throw new InvalidOperationException($"Unsupported input_type: {inputType}");
    }

    private static Dictionary<string, object> Evaluate(
        Dictionary<string, object>? outputData,
        JsonNode? expectedNode,
        JsonNode inputJson,
        string caseDir,
        string scenario)
    {
        var metrics = new Dictionary<string, object>
        {
            ["SignalLengthCorrect"] = false,
            ["MaxRealError"] = double.MaxValue,
            ["RmseReal"] = double.MaxValue,
            ["MaxImaginaryError"] = double.MaxValue,
            ["ImaginaryMaxAbs"] = double.MaxValue,
            ["EnergyError"] = double.MaxValue,
            ["ImageRmse"] = double.MaxValue,
            ["IsFinite"] = false,
            ["OutputShapeCorrect"] = false,
        };

        if (outputData is null || expectedNode is null)
            return metrics;

        var inputType = inputJson["inputs"]?["input_type"]?.GetValue<string>() ?? "complex_array";
        if (inputType == "image_source")
        {
            EvaluateImage(outputData, expectedNode, inputJson, caseDir, metrics);
            return metrics;
        }

        if (!outputData.TryGetValue("Real", out var realObj) || realObj is not double[] real)
            return metrics;
        if (!outputData.TryGetValue("Imaginary", out var imaginaryObj) || imaginaryObj is not double[] imaginary)
            return metrics;

        var expectedReal = RequiredDoubleArray(expectedNode, "real");
        var expectedImaginary = RequiredDoubleArray(expectedNode, "imaginary");
        var expectedLength = expectedNode["signal_length"]?.GetValue<int>() ?? expectedReal.Length;

        var lengthCorrect = real.Length == expectedLength && imaginary.Length == expectedLength && expectedReal.Length == expectedLength && expectedImaginary.Length == expectedLength;
        metrics["SignalLengthCorrect"] = lengthCorrect;
        metrics["OutputShapeCorrect"] = lengthCorrect;
        metrics["IsFinite"] = real.All(double.IsFinite) && imaginary.All(double.IsFinite);

        if (!lengthCorrect)
            return metrics;

        var maxRealError = 0.0;
        var maxImaginaryError = 0.0;
        var sqReal = 0.0;
        var actualEnergy = 0.0;
        for (var i = 0; i < expectedLength; i++)
        {
            var realDiff = real[i] - expectedReal[i];
            var imaginaryDiff = imaginary[i] - expectedImaginary[i];
            maxRealError = Math.Max(maxRealError, Math.Abs(realDiff));
            maxImaginaryError = Math.Max(maxImaginaryError, Math.Abs(imaginaryDiff));
            sqReal += realDiff * realDiff;
            actualEnergy += real[i] * real[i] + imaginary[i] * imaginary[i];
        }

        var expectedEnergy = expectedNode["energy"]?.GetValue<double>() ?? 0.0;
        metrics["MaxRealError"] = maxRealError;
        metrics["RmseReal"] = Math.Sqrt(sqReal / Math.Max(expectedLength, 1));
        metrics["MaxImaginaryError"] = maxImaginaryError;
        metrics["ImaginaryMaxAbs"] = imaginary.Length == 0 ? 0.0 : imaginary.Max(value => Math.Abs(value));
        metrics["EnergyError"] = RelativeError(expectedEnergy, actualEnergy);

        return metrics;
    }

    private static void EvaluateImage(
        Dictionary<string, object> outputData,
        JsonNode expectedNode,
        JsonNode inputJson,
        string caseDir,
        Dictionary<string, object> metrics)
    {
        if (!outputData.TryGetValue("Signal", out var signalObj) || signalObj is not ImageWrapper signalWrapper)
            return;

        var actual = signalWrapper.GetMat();
        if (actual.Empty() || actual.Channels() != 1)
            return;

        var expectedShape = expectedNode["image_shape"]?.AsArray();
        if (expectedShape is null || expectedShape.Count != 2)
            return;

        var expectedH = expectedShape[0]?.GetValue<int>() ?? 0;
        var expectedW = expectedShape[1]?.GetValue<int>() ?? 0;
        var shapeCorrect = actual.Rows == expectedH && actual.Cols == expectedW;
        metrics["OutputShapeCorrect"] = shapeCorrect;
        metrics["SignalLengthCorrect"] = shapeCorrect;

        var imageFile = inputJson["inputs"]?["image"]?.GetValue<string>() ?? "input_image.png";
        var imagePath = Path.Combine(caseDir, imageFile);
        using var reference = Cv2.ImRead(imagePath, ImreadModes.Grayscale);
        if (reference.Empty() || !shapeCorrect)
            return;

        var sq = 0.0;
        var finite = true;
        for (var y = 0; y < actual.Rows; y++)
        {
            for (var x = 0; x < actual.Cols; x++)
            {
                var value = actual.At<float>(y, x);
                finite &= float.IsFinite(value);
                var diff = value - reference.At<byte>(y, x);
                sq += diff * diff;
            }
        }

        metrics["IsFinite"] = finite;
        metrics["ImageRmse"] = Math.Sqrt(sq / Math.Max(actual.Rows * actual.Cols, 1));
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics, string scenario)
    {
        var isFinite = metrics.TryGetValue("IsFinite", out var fin) && fin is bool b && b;
        if (!isFinite)
            return false;

        var shapeCorrect = metrics.TryGetValue("OutputShapeCorrect", out var sc) && sc is bool scb && scb;
        if (!shapeCorrect)
            return false;

        if (scenario == "image_round_trip")
        {
            return Convert.ToDouble(metrics["ImageRmse"]) <= 0.05;
        }

        return Convert.ToBoolean(metrics["SignalLengthCorrect"])
            && Convert.ToDouble(metrics["MaxRealError"]) <= 1e-3
            && Convert.ToDouble(metrics["RmseReal"]) <= 1e-4
            && Convert.ToDouble(metrics["MaxImaginaryError"]) <= 1e-3
            && Convert.ToDouble(metrics["EnergyError"]) <= 1e-3;
    }

    private static async Task<JsonNode> ReadJsonAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
    }

    private static double[] RequiredDoubleArray(JsonNode node, string propertyName)
    {
        var array = node[propertyName]?.AsArray()
            ?? throw new InvalidOperationException($"Missing numeric array: {propertyName}");
        return array.Select(item => item?.GetValue<double>() ?? 0.0).ToArray();
    }

    private static double RelativeError(double expected, double actual)
    {
        if (expected == 0.0)
            return Math.Abs(actual);
        return Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-12);
    }

    private static void ReleaseImageOutputs(Dictionary<string, object>? data)
    {
        if (data is null) return;
        foreach (var image in data.Values.OfType<ImageWrapper>().Distinct(ReferenceEqualityComparer<ImageWrapper>.Instance))
        {
            SafeRelease(image);
        }
    }

    public static void SafeRelease(ImageWrapper image)
    {
        if (image.RefCount > 0)
        {
            image.Release();
        }
    }
}

internal sealed class InputBundle
{
    private readonly IReadOnlyList<ImageWrapper> _ownedImages;

    public InputBundle(Dictionary<string, object> inputs, IReadOnlyList<ImageWrapper> ownedImages)
    {
        Inputs = inputs;
        _ownedImages = ownedImages;
    }

    public Dictionary<string, object> Inputs { get; }

    public void Release()
    {
        foreach (var image in _ownedImages.Distinct(ReferenceEqualityComparer<ImageWrapper>.Instance))
        {
            GoldenRunner.SafeRelease(image);
        }
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# InverseFFT1D Golden Runner Report",
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
        var parts = metrics.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}");
        return string.Join(", ", parts);
    }

    private static string FormatValue(object value) =>
        value switch
        {
            double d => d.ToString("0.####"),
            float f => f.ToString("0.####"),
            bool b => b.ToString(),
            _ => value.ToString() ?? string.Empty
        };
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
        var output = "quality/evals/reports/InverseFFT1D_baseline.json";
        string? report = "quality/evals/reports/InverseFFT1D_baseline.md";
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
            InverseFFT1D golden runner

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
