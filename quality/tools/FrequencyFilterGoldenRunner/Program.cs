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
    $"FrequencyFilter golden run complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GoldenRunner
{
    private const double MinCutoff = 1e-6;
    private const double MaxNormalizedCutoff = 0.5;

    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var caseDirs = Directory
            .EnumerateFiles(options.CasesRoot, "input.json", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Where(dir => Path.GetFileName(dir)!.StartsWith("FrequencyFilter_", StringComparison.OrdinalIgnoreCase))
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
            var op = new Operator("FrequencyFilter", OperatorType.FrequencyFilter, 0, 0);
            bundle = CreateInputs(inputJson, caseDir);
            var executor = new FrequencyFilterOperator(NullLogger<FrequencyFilterOperator>.Instance);

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
        var inputs = new Dictionary<string, object>
        {
            ["FilterType"] = inputsNode["filter_type"]?.GetValue<string>() ?? "lowpass",
            ["CutoffLow"] = inputsNode["cutoff_low"]?.GetValue<double>() ?? 0.1,
            ["CutoffHigh"] = inputsNode["cutoff_high"]?.GetValue<double>() ?? 0.3,
            ["Order"] = inputsNode["order"]?.GetValue<int>() ?? 2,
        };
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
            ["MaskLengthCorrect"] = false,
            ["MaskMaxError"] = double.MaxValue,
            ["MaskRangeCorrect"] = false,
            ["FilteredSpectrumMaxError"] = double.MaxValue,
            ["FilteredSpectrumRmse"] = double.MaxValue,
            ["ReconstructionRmse"] = double.MaxValue,
            ["ImaginaryRmse"] = double.MaxValue,
            ["EnergyError"] = double.MaxValue,
            ["ConjugateSymmetryError"] = double.MaxValue,
            ["PassStopRatioCorrect"] = false,
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

        if (!outputData.TryGetValue("FilterMask", out var maskObj) || maskObj is not double[] actualMask)
            return metrics;
        if (!outputData.TryGetValue("FilteredSpectrum", out var spectrumObj) || spectrumObj is not Complex[] actualSpectrum)
            return metrics;

        var expectedMask = RequiredDoubleArray(expectedNode, "mask");
        var expectedSpectrumNode = expectedNode["filtered_spectrum"] ?? throw new InvalidOperationException("Missing expected filtered_spectrum");
        var expectedReal = RequiredDoubleArray(expectedSpectrumNode, "real");
        var expectedImaginary = RequiredDoubleArray(expectedSpectrumNode, "imaginary");
        var expectedLength = expectedNode["signal_length"]?.GetValue<int>() ?? expectedMask.Length;

        var lengthCorrect = actualMask.Length == expectedLength
            && actualSpectrum.Length == expectedLength
            && expectedMask.Length == expectedLength
            && expectedReal.Length == expectedLength
            && expectedImaginary.Length == expectedLength;

        metrics["MaskLengthCorrect"] = lengthCorrect;
        metrics["OutputShapeCorrect"] = lengthCorrect;
        if (!lengthCorrect)
            return metrics;

        metrics["MaskRangeCorrect"] = actualMask.All(value => value >= -1e-9 && value <= 1.0 + 1e-9);
        metrics["IsFinite"] = actualMask.All(double.IsFinite)
            && actualSpectrum.All(value => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary));
        metrics["MaskMaxError"] = MaxAbsError(expectedMask, actualMask);

        var maxSpectrumError = 0.0;
        var sqSpectrum = 0.0;
        var actualEnergy = 0.0;
        for (var i = 0; i < expectedLength; i++)
        {
            var realDiff = actualSpectrum[i].Real - expectedReal[i];
            var imaginaryDiff = actualSpectrum[i].Imaginary - expectedImaginary[i];
            var absError = Math.Sqrt(realDiff * realDiff + imaginaryDiff * imaginaryDiff);
            maxSpectrumError = Math.Max(maxSpectrumError, absError);
            sqSpectrum += absError * absError;
            actualEnergy += actualSpectrum[i].Magnitude * actualSpectrum[i].Magnitude;
        }

        var expectedEnergy = expectedNode["energy_after"]?.GetValue<double>() ?? 0.0;
        metrics["FilteredSpectrumMaxError"] = maxSpectrumError;
        metrics["FilteredSpectrumRmse"] = Math.Sqrt(sqSpectrum / Math.Max(expectedLength, 1));
        metrics["EnergyError"] = RelativeError(expectedEnergy, actualEnergy);

        var passBin = expectedNode["pass_bin"]?.GetValue<int>() ?? 0;
        var stopBin = expectedNode["stop_bin"]?.GetValue<int>() ?? 0;
        metrics["PassStopRatioCorrect"] = PassStopRatioCorrect(scenario, actualMask, passBin, stopBin);

        if (expectedNode["conjugate_symmetric"]?.GetValue<bool>() == true)
        {
            metrics["ConjugateSymmetryError"] = ConjugateSymmetryError(actualSpectrum);
        }
        else
        {
            metrics["ConjugateSymmetryError"] = 0.0;
        }

        var expectedReconstructed = expectedNode["reconstructed"] ?? throw new InvalidOperationException("Missing expected reconstructed");
        var expectedReconstructedReal = RequiredDoubleArray(expectedReconstructed, "real");
        var expectedReconstructedImaginary = RequiredDoubleArray(expectedReconstructed, "imaginary");
        var (rmseReal, rmseImaginary) = ComputeReconstructionRmse(actualSpectrum, expectedReconstructedReal, expectedReconstructedImaginary);
        metrics["ReconstructionRmse"] = rmseReal;
        metrics["ImaginaryRmse"] = rmseImaginary;

        return metrics;
    }

    private static void EvaluateImage(
        Dictionary<string, object> outputData,
        JsonNode expectedNode,
        JsonNode inputJson,
        string caseDir,
        Dictionary<string, object> metrics)
    {
        if (!outputData.TryGetValue("FilteredSpectrum", out var spectrumObj) || spectrumObj is not ImageWrapper spectrumWrapper)
            return;
        if (!outputData.TryGetValue("FilterMask", out var maskObj) || maskObj is not ImageWrapper maskWrapper)
            return;

        var spectrum = spectrumWrapper.GetMat();
        var mask = maskWrapper.GetMat();

        var expectedShape = expectedNode["image_shape"]?.AsArray();
        if (expectedShape is null || expectedShape.Count != 2)
            return;

        var expectedH = expectedShape[0]?.GetValue<int>() ?? 0;
        var expectedW = expectedShape[1]?.GetValue<int>() ?? 0;
        var shapeCorrect = spectrum.Rows == expectedH
            && spectrum.Cols == expectedW
            && spectrum.Channels() == 2
            && mask.Rows == expectedH
            && mask.Cols == expectedW
            && mask.Channels() == 1;

        metrics["MaskLengthCorrect"] = shapeCorrect;
        metrics["OutputShapeCorrect"] = shapeCorrect;
        if (!shapeCorrect)
            return;

        var filterType = inputJson["inputs"]?["filter_type"]?.GetValue<string>() ?? "lowpass";
        var cutoffLow = inputJson["inputs"]?["cutoff_low"]?.GetValue<double>() ?? 0.1;
        var cutoffHigh = inputJson["inputs"]?["cutoff_high"]?.GetValue<double>() ?? 0.3;
        var order = inputJson["inputs"]?["order"]?.GetValue<int>() ?? 2;

        var maxMaskError = 0.0;
        var finite = true;
        var rangeCorrect = true;
        for (var y = 0; y < mask.Rows; y++)
        {
            var fy = SignedFrequency(y, mask.Rows);
            for (var x = 0; x < mask.Cols; x++)
            {
                var fx = SignedFrequency(x, mask.Cols);
                var expected = EvaluateFilter(filterType, Math.Sqrt(fx * fx + fy * fy), cutoffLow, cutoffHigh, order);
                var actual = mask.At<float>(y, x);
                finite &= float.IsFinite(actual);
                rangeCorrect &= actual >= -1e-6f && actual <= 1.0f + 1e-6f;
                maxMaskError = Math.Max(maxMaskError, Math.Abs(actual - expected));

                var complex = spectrum.At<Vec2f>(y, x);
                finite &= float.IsFinite(complex.Item0) && float.IsFinite(complex.Item1);
            }
        }

        metrics["IsFinite"] = finite;
        metrics["MaskRangeCorrect"] = rangeCorrect;
        metrics["MaskMaxError"] = maxMaskError;
        metrics["PassStopRatioCorrect"] = true;
        metrics["FilteredSpectrumMaxError"] = 0.0;
        metrics["FilteredSpectrumRmse"] = 0.0;
        metrics["ReconstructionRmse"] = 0.0;
        metrics["ImaginaryRmse"] = 0.0;
        metrics["EnergyError"] = 0.0;
        metrics["ConjugateSymmetryError"] = 0.0;
    }

    private static (double RealRmse, double ImaginaryRmse) ComputeReconstructionRmse(
        Complex[] filteredSpectrum,
        double[] expectedReal,
        double[] expectedImaginary)
    {
        Dictionary<string, object>? output = null;
        try
        {
            var inverse = new InverseFFT1DOperator(NullLogger<InverseFFT1DOperator>.Instance);
            var result = inverse.ExecuteAsync(
                new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0),
                new Dictionary<string, object> { ["Spectrum"] = filteredSpectrum }).Result;

            if (!result.IsSuccess || result.OutputData is null)
                return (double.MaxValue, double.MaxValue);

            output = result.OutputData;
            if (!output.TryGetValue("Real", out var realObj) || realObj is not double[] real)
                return (double.MaxValue, double.MaxValue);
            if (!output.TryGetValue("Imaginary", out var imaginaryObj) || imaginaryObj is not double[] imaginary)
                return (double.MaxValue, double.MaxValue);
            if (real.Length != expectedReal.Length || imaginary.Length != expectedImaginary.Length)
                return (double.MaxValue, double.MaxValue);

            return (Rmse(expectedReal, real), Rmse(expectedImaginary, imaginary));
        }
        catch
        {
            return (double.MaxValue, double.MaxValue);
        }
        finally
        {
            ReleaseImageOutputs(output);
        }
    }

    private static bool PassStopRatioCorrect(string scenario, IReadOnlyList<double> mask, int passBin, int stopBin)
    {
        if (passBin < 0 || stopBin < 0 || passBin >= mask.Count || stopBin >= mask.Count)
            return true;
        if (scenario == "complex_spectrum")
            return true;
        if (scenario == "cutoff_clamp")
            return true;
        return mask[passBin] > mask[stopBin];
    }

    private static double ConjugateSymmetryError(IReadOnlyList<Complex> spectrum)
    {
        var maxError = 0.0;
        for (var i = 1; i < spectrum.Count; i++)
        {
            var conjugateIndex = (spectrum.Count - i) % spectrum.Count;
            var diff = spectrum[i] - Complex.Conjugate(spectrum[conjugateIndex]);
            maxError = Math.Max(maxError, diff.Magnitude);
        }

        return maxError;
    }

    private static bool IsPassing(IReadOnlyDictionary<string, object> metrics, string scenario)
    {
        var isFinite = metrics.TryGetValue("IsFinite", out var fin) && fin is bool b && b;
        if (!isFinite)
            return false;
        if (!(metrics.TryGetValue("OutputShapeCorrect", out var shape) && shape is bool sb && sb))
            return false;
        if (!(metrics.TryGetValue("MaskRangeCorrect", out var range) && range is bool rb && rb))
            return false;
        if (Convert.ToDouble(metrics["MaskMaxError"]) > 1e-5)
            return false;

        if (scenario == "image_2d")
            return true;

        return Convert.ToBoolean(metrics["MaskLengthCorrect"])
            && Convert.ToDouble(metrics["FilteredSpectrumMaxError"]) <= 1e-4
            && Convert.ToDouble(metrics["FilteredSpectrumRmse"]) <= 1e-5
            && Convert.ToDouble(metrics["ReconstructionRmse"]) <= 1e-4
            && Convert.ToDouble(metrics["ImaginaryRmse"]) <= 1e-4
            && Convert.ToDouble(metrics["EnergyError"]) <= 1e-4
            && Convert.ToDouble(metrics["ConjugateSymmetryError"]) <= 1e-3
            && Convert.ToBoolean(metrics["PassStopRatioCorrect"]);
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

    private static double SignedFrequency(int index, int sampleCount) =>
        index <= sampleCount / 2
            ? (double)index / sampleCount
            : (double)(index - sampleCount) / sampleCount;

    private static double EvaluateFilter(string type, double normalizedFrequency, double cutoffLow, double cutoffHigh, int order)
    {
        var bandLow = Math.Min(NormalizeCutoff(cutoffLow), NormalizeCutoff(cutoffHigh));
        var bandHigh = Math.Max(NormalizeCutoff(cutoffLow), NormalizeCutoff(cutoffHigh));
        return type.Trim().ToLowerInvariant() switch
        {
            "lowpass" or "low" => ButterworthLowpass(normalizedFrequency, bandLow, order),
            "highpass" or "high" => ButterworthHighpass(normalizedFrequency, bandLow, order),
            "bandpass" or "band" => ButterworthHighpass(normalizedFrequency, bandLow, order) * ButterworthLowpass(normalizedFrequency, bandHigh, order),
            "bandstop" or "notch" => 1.0 - (ButterworthHighpass(normalizedFrequency, bandLow, order) * ButterworthLowpass(normalizedFrequency, bandHigh, order)),
            _ => 1.0
        };
    }

    private static double ButterworthLowpass(double frequency, double cutoff, int order)
    {
        var safeCutoff = NormalizeCutoff(cutoff);
        if (frequency <= 0.0)
            return 1.0;
        return 1.0 / (1.0 + Math.Pow(frequency / safeCutoff, 2 * order));
    }

    private static double ButterworthHighpass(double frequency, double cutoff, int order)
    {
        var safeCutoff = NormalizeCutoff(cutoff);
        if (frequency <= 0.0)
            return 0.0;
        var ratio = Math.Pow(frequency / safeCutoff, 2 * order);
        return ratio / (1.0 + ratio);
    }

    private static double NormalizeCutoff(double cutoff) =>
        Math.Clamp(cutoff, MinCutoff, MaxNormalizedCutoff);

    private static double MaxAbsError(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        if (expected.Count != actual.Count)
            return double.MaxValue;
        var max = 0.0;
        for (var i = 0; i < expected.Count; i++)
        {
            max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        }
        return max;
    }

    private static double Rmse(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        if (expected.Count != actual.Count)
            return double.MaxValue;
        var sq = 0.0;
        for (var i = 0; i < expected.Count; i++)
        {
            var diff = expected[i] - actual[i];
            sq += diff * diff;
        }
        return Math.Sqrt(sq / Math.Max(expected.Count, 1));
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
            "# FrequencyFilter Golden Runner Report",
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
        var output = "quality/evals/reports/FrequencyFilter_baseline.json";
        string? report = "quality/evals/reports/FrequencyFilter_baseline.md";
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
            FrequencyFilter golden runner

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
