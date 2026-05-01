using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
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

var result = await KolektorRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"KolektorSDD2 SurfaceDefectDetection run complete: " +
    $"profile={result.Summary.ProfileName}, candidate={result.Summary.CandidateVersion}, " +
    $"passed={result.Summary.Passed}/{result.Summary.CaseCount}, failed={result.Summary.Failed}, " +
    $"pixel_f1={result.Summary.PixelF1:F4}, image_auroc={result.Summary.ImageAuroc:F4}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class KolektorRunner
{
    private const string DatasetName = "KolektorSDD2";

    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var index = LoadIndex(options.IndexPath);
        var caseIds = options.CaseIds.Count == 0
            ? null
            : new HashSet<string>(options.CaseIds, StringComparer.OrdinalIgnoreCase);
        var records = index.Records
            .Where(item => item.Split.Equals(options.Split, StringComparison.OrdinalIgnoreCase))
            .Where(item => caseIds is null || caseIds.Contains(item.Id))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Take(options.Limit > 0 ? options.Limit : int.MaxValue)
            .ToList();

        if (records.Count == 0)
        {
            throw new InvalidOperationException($"No records found for split '{options.Split}' in {options.IndexPath}.");
        }

        var sut = new SurfaceDefectDetectionOperator(NullLogger<SurfaceDefectDetectionOperator>.Instance);
        var operatorConfig = CreateOperator(options);
        var results = new List<ImageResult>(records.Count);
        var pixelScores = new List<ScoredLabel>(capacity: Math.Min(records.Count * 4096, 1_000_000));
        var stopwatchAll = Stopwatch.StartNew();
        var allocationBeforeAll = GC.GetTotalAllocatedBytes(precise: true);

        foreach (var record in records)
        {
            results.Add(await RunRecordAsync(sut, operatorConfig, record, options, pixelScores));
        }

        stopwatchAll.Stop();
        var allocationAfterAll = GC.GetTotalAllocatedBytes(precise: true);
        var totals = PixelTotals.Combine(results.Select(item => item.PixelTotals));
        var imageAuroc = FiniteOrZero(ComputeAuroc(results.Select(item => new ScoredLabel(item.Score, item.IsDefect))));
        var pixelAuroc = FiniteOrZero(ComputeAuroc(pixelScores));
        var falsePositivePerImage = results.Count(item => !item.IsDefect && item.PredictedDefect) /
                                    (double)Math.Max(1, results.Count(item => !item.IsDefect));
        var runtimeP95 = Percentile(results.Select(item => item.RuntimeMs), 0.95);
        var errorCount = results.Count(item => item.Error is not null);
        var metricFailures = EvaluateThresholds(totals, imageAuroc, errorCount, options);
        var failedCaseCount = results.Count(item => !item.Passed);

        return new BaselineResult(
            "dataset",
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                options.CandidateVersion,
                options.ProfileName,
                options.IndexPath,
                options.Split,
                options.MaxSide,
                options.Method,
                options.ThresholdMode,
                options.NormalizationMode,
                options.Threshold,
                options.MinArea,
                options.MaxArea,
                options.MorphCleanSize,
                options.MorphMode,
                options.BackgroundKernelSize,
                options.ReferenceStatsSigma,
                options.RobustReferenceStats,
                options.ResponseNormalizeMode,
                options.ClaheClipLimit,
                options.ClaheTileGridSize,
                options.ComponentFilterMode,
                options.SmallNoiseAreaMax,
                options.MinElongationForSmallComponent,
                options.CompactNoiseAreaMax,
                options.CompactNoiseCircularityMin,
                options.CompactNoiseFillRatioMin,
                options.MinLocalResponseProminence,
                records.Count,
                results.Count(item => item.Passed),
                metricFailures.Count,
                results.Count(item => item.IsDefect),
                results.Count(item => !item.IsDefect),
                totals.TruePositive,
                totals.FalsePositive,
                totals.FalseNegative,
                totals.TrueNegative,
                Math.Round(totals.IoU, 6),
                Math.Round(totals.Dice, 6),
                Math.Round(totals.F1, 6),
                Math.Round(imageAuroc, 6),
                Math.Round(pixelAuroc, 6),
                Math.Round(SafeDivide(results.Count(item => item.IsDefect && item.PredictedDefect), results.Count(item => item.PredictedDefect)), 6),
                Math.Round(SafeDivide(results.Count(item => item.IsDefect && item.PredictedDefect), results.Count(item => item.IsDefect)), 6),
                Math.Round(ImageF1(results), 6),
                Math.Round(falsePositivePerImage, 6),
                Math.Round(runtimeP95, 3),
                options.MinImageAuroc,
                options.MinPixelF1,
                Math.Round(stopwatchAll.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfterAll - allocationBeforeAll)),
            [
                new OperatorSummary(
                    "SurfaceDefectDetection",
                    records.Count,
                    results.Count(item => item.Passed),
                    failedCaseCount,
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    Convert.ToInt64(Math.Round(results.Average(item => item.MemoryAllocationBytes))),
                    true,
                    "dataset",
                    DatasetName)
            ],
            new ConfusionSummary(
                results.Count(item => item.IsDefect && item.PredictedDefect),
                results.Count(item => !item.IsDefect && item.PredictedDefect),
                results.Count(item => item.IsDefect && !item.PredictedDefect),
                results.Count(item => !item.IsDefect && !item.PredictedDefect)),
            metricFailures,
            results);
    }

    private static async Task<ImageResult> RunRecordAsync(
        SurfaceDefectDetectionOperator sut,
        Operator operatorConfig,
        KolektorRecord record,
        RunnerOptions options,
        List<ScoredLabel> pixelScores)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        Dictionary<string, object>? outputData = null;

        try
        {
            using var image = LoadImage(record.ImagePath, options.MaxSide, InterpolationFlags.Area);
            using var groundTruthMask = LoadMask(record.MaskPath, image.Size(), options.MaxSide);
            var inputWrapper = new ImageWrapper(image.Clone());
            var output = await sut.ExecuteAsync(
                operatorConfig,
                new Dictionary<string, object> { ["Image"] = inputWrapper });

            if (!output.IsSuccess)
            {
                throw new InvalidOperationException(output.ErrorMessage ?? "SurfaceDefectDetection returned failure.");
            }

            outputData = output.OutputData;
            var defectMaskWrapper = GetImageWrapper(output, "DefectMask");
            var responseWrapper = GetImageWrapper(output, "ResponseImage");
            var diagnostics = ExtractDiagnostics(output.OutputData);
            using var predictedMask = NormalizeMask(defectMaskWrapper.MatReadOnly, image.Size());
            using var response = NormalizeResponse(responseWrapper.MatReadOnly, image.Size());

            var totals = EvaluateMask(predictedMask, groundTruthMask);
            var components = ExtractComponentTelemetry(record, predictedMask, groundTruthMask, response);
            CollectPixelScores(response, groundTruthMask, options.PixelSampleStride, pixelScores);
            var score = ImageScore(response, predictedMask, output);
            var predictedDefect = Convert.ToDouble(output.OutputData?["DefectArea"] ?? 0.0) > 0.0 ||
                                  Cv2.CountNonZero(predictedMask) > 0;
            var taxonomy = ClassifyImage(record, predictedDefect, totals, Convert.ToInt32(output.OutputData?["DefectCount"] ?? 0), Convert.ToDouble(output.OutputData?["DefectArea"] ?? 0.0), null);
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);

            return new ImageResult(
                record.Id,
                record.ImagePath,
                record.MaskPath,
                record.IsDefect,
                predictedDefect,
                Math.Round(score, 6),
                Convert.ToInt32(output.OutputData?["DefectCount"] ?? 0),
                Convert.ToDouble(output.OutputData?["DefectArea"] ?? 0.0),
                totals,
                record.IsDefect == predictedDefect,
                taxonomy,
                components,
                diagnostics,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new ImageResult(
                record.Id,
                record.ImagePath,
                record.MaskPath,
                record.IsDefect,
                false,
                0,
                0,
                0,
                PixelTotals.Empty,
                !record.IsDefect,
                ClassifyImage(record, false, PixelTotals.Empty, 0, 0, ex.GetBaseException().Message),
                [],
                new Dictionary<string, object?>(),
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                ex.GetBaseException().Message);
        }
        finally
        {
            ReleaseOutputImages(outputData);
        }
    }

    private static KolektorIndex LoadIndex(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"KolektorSDD2 index not found: {fullPath}.");
        }

        return JsonSerializer.Deserialize<KolektorIndex>(File.ReadAllText(fullPath), JsonSettings.Default)
               ?? throw new InvalidOperationException($"Failed to parse KolektorSDD2 index: {fullPath}");
    }

    private static Mat LoadImage(string relativePath, int maxSide, InterpolationFlags interpolation)
    {
        var path = Path.GetFullPath(relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Image not found: {path}");
        }

        using var source = Cv2.ImRead(path, ImreadModes.Color);
        if (source.Empty())
        {
            throw new InvalidOperationException($"Failed to load image: {path}");
        }

        return ResizeToMaxSide(source, maxSide, interpolation);
    }

    private static Mat LoadMask(string? relativePath, Size imageSize, int maxSide)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new Mat(imageSize, MatType.CV_8UC1, Scalar.Black);
        }

        var path = Path.GetFullPath(relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Mask not found: {path}");
        }

        using var source = Cv2.ImRead(path, ImreadModes.Grayscale);
        if (source.Empty())
        {
            throw new InvalidOperationException($"Failed to load mask: {path}");
        }

        using var resizedToMax = ResizeToMaxSide(source, maxSide, InterpolationFlags.Nearest);
        var resized = new Mat();
        Cv2.Resize(resizedToMax, resized, imageSize, 0, 0, InterpolationFlags.Nearest);
        Cv2.Threshold(resized, resized, 0, 255, ThresholdTypes.Binary);
        return resized;
    }

    private static Mat ResizeToMaxSide(Mat source, int maxSide, InterpolationFlags interpolation)
    {
        if (maxSide <= 0 || Math.Max(source.Width, source.Height) <= maxSide)
        {
            return source.Clone();
        }

        var scale = maxSide / (double)Math.Max(source.Width, source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, height), 0, 0, interpolation);
        return resized;
    }

    private static ImageWrapper GetImageWrapper(OperatorExecutionOutput output, string key)
    {
        if (output.OutputData is null || !output.OutputData.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Missing output key '{key}'.");
        }

        if (!ImageWrapper.TryGetFromObject(value, out var wrapper) || wrapper is null)
        {
            throw new InvalidOperationException($"Output key '{key}' is not an image.");
        }

        return wrapper;
    }

    private static void ReleaseOutputImages(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        var released = new HashSet<ImageWrapper>();
        foreach (var value in outputData.Values)
        {
            if (value is ImageWrapper wrapper && wrapper.RefCount > 0 && released.Add(wrapper))
            {
                wrapper.Release();
            }
        }
    }

    private static Mat NormalizeMask(Mat source, Size imageSize)
    {
        using var gray = ToGray(source);
        var resized = new Mat();
        Cv2.Resize(gray, resized, imageSize, 0, 0, InterpolationFlags.Nearest);
        Cv2.Threshold(resized, resized, 0, 255, ThresholdTypes.Binary);
        return resized;
    }

    private static Mat NormalizeResponse(Mat source, Size imageSize)
    {
        using var gray = ToGray(source);
        var resized = new Mat();
        Cv2.Resize(gray, resized, imageSize, 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private static Mat ToGray(Mat source)
    {
        if (source.Channels() == 1)
        {
            return source.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static PixelTotals EvaluateMask(Mat predicted, Mat groundTruth)
    {
        long tp = 0;
        long fp = 0;
        long fn = 0;
        long tn = 0;

        for (var y = 0; y < groundTruth.Rows; y++)
        {
            for (var x = 0; x < groundTruth.Cols; x++)
            {
                var p = predicted.At<byte>(y, x) > 0;
                var g = groundTruth.At<byte>(y, x) > 0;
                if (p && g)
                {
                    tp++;
                }
                else if (p)
                {
                    fp++;
                }
                else if (g)
                {
                    fn++;
                }
                else
                {
                    tn++;
                }
            }
        }

        return new PixelTotals(tp, fp, fn, tn);
    }

    private static List<ComponentTelemetry> ExtractComponentTelemetry(
        KolektorRecord record,
        Mat predictedMask,
        Mat groundTruthMask,
        Mat response)
    {
        var rows = new List<ComponentTelemetry>();
        AddComponentTelemetryRows(record, predictedMask, groundTruthMask, response, "predicted", rows);
        AddComponentTelemetryRows(record, groundTruthMask, predictedMask, response, "ground_truth", rows);
        return rows;
    }

    private static void AddComponentTelemetryRows(
        KolektorRecord record,
        Mat sourceMask,
        Mat overlapMask,
        Mat response,
        string source,
        List<ComponentTelemetry> destination)
    {
        using var contourInput = sourceMask.Clone();
        Cv2.FindContours(contourInput, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var componentIndex = 0;
        foreach (var contour in contours)
        {
            using var componentMask = new Mat(sourceMask.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.DrawContours(componentMask, new[] { contour }, -1, Scalar.White, -1);
            var pixelArea = Cv2.CountNonZero(componentMask);
            if (pixelArea <= 0)
            {
                continue;
            }

            using var overlap = new Mat();
            Cv2.BitwiseAnd(componentMask, overlapMask, overlap);
            var overlapPixels = Cv2.CountNonZero(overlap);

            string kind;
            long truePositivePixels;
            long falsePositivePixels;
            long falseNegativePixels;
            if (source == "predicted")
            {
                kind = overlapPixels > 0 ? "true_positive" : "false_positive";
                truePositivePixels = overlapPixels;
                falsePositivePixels = Math.Max(0, pixelArea - overlapPixels);
                falseNegativePixels = 0;
            }
            else
            {
                if (overlapPixels > 0)
                {
                    continue;
                }

                kind = "false_negative";
                truePositivePixels = 0;
                falsePositivePixels = 0;
                falseNegativePixels = pixelArea;
            }

            var rect = Cv2.BoundingRect(contour);
            var shorterSide = Math.Max(1, Math.Min(rect.Width, rect.Height));
            var longerSide = Math.Max(1, Math.Max(rect.Width, rect.Height));
            var area = Cv2.ContourArea(contour);
            var fillRatio = rect.Width <= 0 || rect.Height <= 0
                ? 0.0
                : area / (rect.Width * rect.Height);
            var perimeter = Cv2.ArcLength(contour, true);
            var circularity = perimeter <= 1e-6
                ? 0.0
                : Math.Clamp((4.0 * Math.PI * area) / (perimeter * perimeter), 0.0, 1.0);
            Cv2.MinMaxLoc(response, out _, out var componentPeak, out _, out _, componentMask);
            var componentMean = Cv2.Mean(response, componentMask).Val0;
            var ringMean = ComputeRingMean(response, componentMask);

            destination.Add(new ComponentTelemetry(
                record.Id,
                source,
                kind,
                componentIndex++,
                record.IsDefect,
                overlapPixels > 0,
                Math.Round(area, 6),
                pixelArea,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                Math.Round(longerSide / (double)shorterSide, 6),
                Math.Round(fillRatio, 6),
                Math.Round(circularity, 6),
                Math.Round(componentMean, 6),
                Math.Round(componentPeak, 6),
                Math.Round(ringMean, 6),
                Math.Round(componentPeak - ringMean, 6),
                overlapPixels,
                truePositivePixels,
                falsePositivePixels,
                falseNegativePixels));
        }
    }

    private static double ComputeRingMean(Mat response, Mat componentMask)
    {
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        using var dilated = new Mat();
        using var ring = new Mat();
        Cv2.Dilate(componentMask, dilated, kernel);
        Cv2.Subtract(dilated, componentMask, ring);
        return Cv2.CountNonZero(ring) == 0 ? 0.0 : Cv2.Mean(response, ring).Val0;
    }

    private static void CollectPixelScores(Mat response, Mat mask, int sampleStride, List<ScoredLabel> destination)
    {
        var stride = Math.Max(1, sampleStride);
        for (var y = 0; y < response.Rows; y += stride)
        {
            for (var x = 0; x < response.Cols; x += stride)
            {
                destination.Add(new ScoredLabel(response.At<byte>(y, x), mask.At<byte>(y, x) > 0));
            }
        }
    }

    private static double ImageScore(Mat response, Mat predictedMask, OperatorExecutionOutput output)
    {
        Cv2.MinMaxLoc(response, out double _, out var maxValue);
        var defectArea = Convert.ToDouble(output.OutputData?["DefectArea"] ?? 0.0);
        var maskArea = Cv2.CountNonZero(predictedMask);
        return maxValue + Math.Log(1.0 + defectArea + maskArea);
    }

    private static Operator CreateOperator(RunnerOptions options)
    {
        return CreateOperator(
            OperatorType.SurfaceDefectDetection,
            ("Method", options.Method),
            ("Threshold", options.Threshold),
            ("ThresholdMode", options.ThresholdMode),
            ("MinArea", options.MinArea),
            ("MaxArea", options.MaxArea),
            ("MorphCleanSize", options.MorphCleanSize),
            ("MorphMode", options.MorphMode),
            ("NormalizationMode", options.NormalizationMode),
            ("BackgroundKernelSize", options.BackgroundKernelSize),
            ("ReferenceStatsSigma", options.ReferenceStatsSigma),
            ("RobustReferenceStats", options.RobustReferenceStats),
            ("ResponseNormalizeMode", options.ResponseNormalizeMode),
            ("ClaheClipLimit", options.ClaheClipLimit),
            ("ClaheTileGridSize", options.ClaheTileGridSize),
            ("ComponentFilterMode", options.ComponentFilterMode),
            ("SmallNoiseAreaMax", options.SmallNoiseAreaMax),
            ("MinElongationForSmallComponent", options.MinElongationForSmallComponent),
            ("CompactNoiseAreaMax", options.CompactNoiseAreaMax),
            ("CompactNoiseCircularityMin", options.CompactNoiseCircularityMin),
            ("CompactNoiseFillRatioMin", options.CompactNoiseFillRatioMin),
            ("MinLocalResponseProminence", options.MinLocalResponseProminence));
    }

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(type.ToString(), type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, ParameterType(value), value));
        }

        return op;
    }

    private static string ParameterType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            float or double or decimal => "double",
            _ => "string"
        };
    }

    private static double ComputeAuroc(IEnumerable<ScoredLabel> values)
    {
        var items = values.ToList();
        var positives = items.Count(item => item.Label);
        var negatives = items.Count - positives;
        if (positives == 0 || negatives == 0)
        {
            return double.NaN;
        }

        items.Sort((left, right) => left.Score.CompareTo(right.Score));
        double rankSumPositive = 0;
        var rank = 1;
        var index = 0;
        while (index < items.Count)
        {
            var end = index + 1;
            while (end < items.Count && Math.Abs(items[end].Score - items[index].Score) <= 1e-12)
            {
                end++;
            }

            var averageRank = (rank + rank + (end - index) - 1) / 2.0;
            for (var i = index; i < end; i++)
            {
                if (items[i].Label)
                {
                    rankSumPositive += averageRank;
                }
            }

            rank += end - index;
            index = end;
        }

        var positiveCount = (double)positives;
        var negativeCount = (double)negatives;
        return (rankSumPositive - positiveCount * (positiveCount + 1) / 2.0) / (positiveCount * negativeCount);
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static double FiniteOrZero(double value)
    {
        return double.IsFinite(value) ? value : 0.0;
    }

    private static double SafeDivide(double numerator, double denominator)
    {
        return denominator <= 0 ? 0.0 : numerator / denominator;
    }

    private static double ImageF1(IEnumerable<ImageResult> results)
    {
        var materialized = results.ToList();
        var truePositive = materialized.Count(item => item.IsDefect && item.PredictedDefect);
        var falsePositive = materialized.Count(item => !item.IsDefect && item.PredictedDefect);
        var falseNegative = materialized.Count(item => item.IsDefect && !item.PredictedDefect);
        var denominator = (2 * truePositive) + falsePositive + falseNegative;
        return denominator <= 0 ? 0.0 : (2 * truePositive) / (double)denominator;
    }

    private static IReadOnlyDictionary<string, object?> ExtractDiagnostics(Dictionary<string, object>? outputData)
    {
        if (outputData is null ||
            !outputData.TryGetValue("Diagnostics", out var raw) ||
            raw is not IReadOnlyDictionary<string, object> diagnostics)
        {
            return new Dictionary<string, object?>();
        }

        return diagnostics.ToDictionary(item => item.Key, item => SanitizeDiagnosticValue(item.Value), StringComparer.Ordinal);
    }

    private static object? SanitizeDiagnosticValue(object? value)
    {
        return value switch
        {
            null => null,
            string or bool or int or long or double or float or decimal => value,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static List<string> ClassifyImage(
        KolektorRecord record,
        bool predictedDefect,
        PixelTotals totals,
        int defectCount,
        double defectArea,
        string? error)
    {
        var labels = new List<string>();
        if (!string.IsNullOrWhiteSpace(error))
        {
            labels.Add("execution_error");
            return labels;
        }

        var groundTruthArea = totals.TruePositive + totals.FalseNegative;
        var predictedArea = totals.TruePositive + totals.FalsePositive;

        if (!record.IsDefect && predictedDefect)
        {
            labels.Add(predictedArea <= 32 || defectArea <= 32 || defectCount <= 1
                ? "texture_noise_false_positive"
                : "oversegmentation_false_positive");
        }
        else if (record.IsDefect && !predictedDefect)
        {
            labels.Add(groundTruthArea <= 96
                ? "small_defect_miss"
                : "low_contrast_defect_miss");
        }
        else if (record.IsDefect && predictedDefect && totals.F1 < 0.35)
        {
            if (totals.FalseNegative > totals.FalsePositive * 2)
            {
                labels.Add("undersegmentation_false_negative");
            }
            else if (totals.FalsePositive > totals.FalseNegative * 2)
            {
                labels.Add("mask_overgrowth_false_positive");
            }
            else
            {
                labels.Add("mask_boundary_mismatch");
            }
        }

        return labels;
    }

    private static List<MetricFailure> EvaluateThresholds(PixelTotals totals, double imageAuroc, int errorCount, RunnerOptions options)
    {
        var failures = new List<MetricFailure>();
        if (errorCount > options.MaxErrors)
        {
            failures.Add(new MetricFailure("overall", "Errors", errorCount, options.MaxErrors));
        }

        if (!double.IsNaN(imageAuroc) && imageAuroc < options.MinImageAuroc)
        {
            failures.Add(new MetricFailure("overall", "ImageAuroc", Math.Round(imageAuroc, 6), options.MinImageAuroc));
        }

        if (totals.F1 < options.MinPixelF1)
        {
            failures.Add(new MetricFailure("overall", "PixelF1", Math.Round(totals.F1, 6), options.MinPixelF1));
        }

        return failures;
    }
}

internal sealed record RunnerOptions(
    string IndexPath,
    string OutputPath,
    string ReportPath,
    string CandidateVersion,
    string ProfileName,
    string Split,
    IReadOnlyList<string> CaseIds,
    int Limit,
    int MaxSide,
    string Method,
    string ThresholdMode,
    string NormalizationMode,
    double Threshold,
    int MinArea,
    int MaxArea,
    int MorphCleanSize,
    string MorphMode,
    int BackgroundKernelSize,
    double ReferenceStatsSigma,
    bool RobustReferenceStats,
    string ResponseNormalizeMode,
    double ClaheClipLimit,
    int ClaheTileGridSize,
    string ComponentFilterMode,
    int SmallNoiseAreaMax,
    double MinElongationForSmallComponent,
    int CompactNoiseAreaMax,
    double CompactNoiseCircularityMin,
    double CompactNoiseFillRatioMin,
    double MinLocalResponseProminence,
    int PixelSampleStride,
    int MaxErrors,
    double MinImageAuroc,
    double MinPixelF1,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            IndexPath: "quality/datasets/kolektorsdd2_index.json",
            OutputPath: "quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_baseline.json",
            ReportPath: "quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_baseline.md",
            CandidateVersion: "baseline",
            ProfileName: "baseline_default",
            Split: "test",
            CaseIds: Array.Empty<string>(),
            Limit: 0,
            MaxSide: 256,
            Method: "LocalContrast",
            ThresholdMode: "Manual",
            NormalizationMode: "LocalMean",
            Threshold: 15.0,
            MinArea: 4,
            MaxArea: 1_000_000,
            MorphCleanSize: 1,
            MorphMode: "OpenClose",
            BackgroundKernelSize: 31,
            ReferenceStatsSigma: 2.5,
            RobustReferenceStats: false,
            ResponseNormalizeMode: "RawClamp",
            ClaheClipLimit: 2.0,
            ClaheTileGridSize: 8,
            ComponentFilterMode: "AreaOnly",
            SmallNoiseAreaMax: 0,
            MinElongationForSmallComponent: 0.0,
            CompactNoiseAreaMax: 0,
            CompactNoiseCircularityMin: 0.0,
            CompactNoiseFillRatioMin: 0.0,
            MinLocalResponseProminence: 0.0,
            PixelSampleStride: 4,
            MaxErrors: 0,
            MinImageAuroc: 0.70,
            MinPixelF1: 0.20,
            ShowHelp: false,
            ParseError: null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            string NextValue()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {arg}");
                }

                return args[++i];
            }

            try
            {
                options = arg switch
                {
                    "--index" => options with { IndexPath = NextValue() },
                    "--output" => options with { OutputPath = NextValue() },
                    "--report" => options with { ReportPath = NextValue() },
                    "--candidate-version" => options with { CandidateVersion = NextValue() },
                    "--profile" => options with { ProfileName = NextValue() },
                    "--split" => options with { Split = NextValue() },
                    "--case-ids" => options with { CaseIds = ParseCaseIds(NextValue()) },
                    "--limit" => options with { Limit = int.Parse(NextValue()) },
                    "--max-side" => options with { MaxSide = int.Parse(NextValue()) },
                    "--method" => options with { Method = NextValue() },
                    "--threshold-mode" => options with { ThresholdMode = NextValue() },
                    "--normalization-mode" => options with { NormalizationMode = NextValue() },
                    "--threshold" => options with { Threshold = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-area" => options with { MinArea = int.Parse(NextValue()) },
                    "--max-area" => options with { MaxArea = int.Parse(NextValue()) },
                    "--morph-clean-size" => options with { MorphCleanSize = int.Parse(NextValue()) },
                    "--morph-mode" => options with { MorphMode = NextValue() },
                    "--background-kernel-size" => options with { BackgroundKernelSize = int.Parse(NextValue()) },
                    "--reference-stats-sigma" => options with { ReferenceStatsSigma = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--robust-reference-stats" => options with { RobustReferenceStats = bool.Parse(NextValue()) },
                    "--response-normalize-mode" => options with { ResponseNormalizeMode = NextValue() },
                    "--clahe-clip-limit" => options with { ClaheClipLimit = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--clahe-tile-grid-size" => options with { ClaheTileGridSize = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--component-filter-mode" => options with { ComponentFilterMode = NextValue() },
                    "--small-noise-area-max" => options with { SmallNoiseAreaMax = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-elongation-for-small-component" => options with { MinElongationForSmallComponent = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--compact-noise-area-max" => options with { CompactNoiseAreaMax = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--compact-noise-circularity-min" => options with { CompactNoiseCircularityMin = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--compact-noise-fill-ratio-min" => options with { CompactNoiseFillRatioMin = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-local-response-prominence" => options with { MinLocalResponseProminence = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--pixel-sample-stride" => options with { PixelSampleStride = int.Parse(NextValue()) },
                    "--max-errors" => options with { MaxErrors = int.Parse(NextValue()) },
                    "--min-image-auroc" => options with { MinImageAuroc = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-pixel-f1" => options with { MinPixelF1 = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    _ => options with { ParseError = $"Unknown argument: {arg}" }
                };
            }
            catch (Exception ex)
            {
                return options with { ParseError = ex.Message };
            }

            if (options.ParseError is not null)
            {
                return options;
            }
        }

        return options;
    }

    private static IReadOnlyList<string> ParseCaseIds(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        Usage: dotnet run --project quality/tools/KolektorSurfaceDefectDatasetRunner/KolektorSurfaceDefectDatasetRunner.csproj -- [options]

        Options:
          --index <path>                KolektorSDD2 JSON index.
          --output <path>               JSON output path.
          --report <path>               Markdown report path.
          --candidate-version <name>    Candidate/report version tag. Default: baseline.
          --profile <name>              Profile name recorded in output. Default: baseline_default.
          --split <name>                Dataset split to evaluate. Default: test.
          --case-ids <csv>              Optional comma-separated image ids to replay.
          --limit <int>                 Optional smoke subset size. Default: 0 (all records).
          --max-side <int>              Resize long image side before evaluation. Default: 256.
          --method <name>               Product operator method. Default: LocalContrast.
          --threshold-mode <name>       Product operator threshold mode. Default: Manual.
          --normalization-mode <name>   Product operator normalization mode. Default: LocalMean.
          --threshold <float>           Manual/floor threshold. Default: 15.
          --min-area <int>              Minimum accepted connected-component area. Default: 4.
          --max-area <int>              Maximum accepted connected-component area. Default: 1000000.
          --morph-clean-size <int>      Morphological cleanup kernel. Default: 1.
          --morph-mode <name>           Morphological cleanup mode. Default: OpenClose.
          --background-kernel-size <int>
                                      Local background kernel. Default: 31.
          --reference-stats-sigma <float>
                                      ReferenceStats sigma. Default: 2.5.
          --robust-reference-stats <bool>
                                      Use robust MAD reference stats. Default: false.
          --response-normalize-mode <name>
                                      RawClamp, MinMax, or PercentileClip. Default: RawClamp.
          --clahe-clip-limit <float>  CLAHE clip limit when NormalizationMode=ClaheLocalMean. Default: 2.
          --clahe-tile-grid-size <int>
                                      CLAHE tile grid size when NormalizationMode=ClaheLocalMean. Default: 8.
          --component-filter-mode <name>
                                      AreaOnly or ResponseStats. Default: AreaOnly.
          --pixel-sample-stride <int>   Pixel AUROC sampling stride. Default: 4.
          --max-errors <int>            Crash/failure gate. Default: 0.
          --min-image-auroc <float>     Optional image AUROC floor. Default: 0.70.
          --min-pixel-f1 <float>        Optional pixel F1 floor. Default: 0.20.
        """);
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# SurfaceDefectDetection KolektorSDD2 Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Index: `{result.Summary.IndexPath}`",
            $"CandidateVersion: `{result.Summary.CandidateVersion}`",
            $"Profile: `{result.Summary.ProfileName}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed gates | {result.Summary.Failed} |",
            $"| Defect images | {result.Summary.DefectImageCount} |",
            $"| Normal images | {result.Summary.NormalImageCount} |",
            $"| Pixel IoU | {result.Summary.MaskIoU:F4} |",
            $"| Dice | {result.Summary.Dice:F4} |",
            $"| Pixel F1 | {result.Summary.PixelF1:F4} |",
            $"| Image AUROC | {result.Summary.ImageAuroc:F4} |",
            $"| Pixel AUROC | {result.Summary.PixelAuroc:F4} |",
            $"| Image precision | {result.Summary.ImagePrecision:F4} |",
            $"| Image recall | {result.Summary.ImageRecall:F4} |",
            $"| Image F1 | {result.Summary.ImageF1:F4} |",
            $"| False positive per normal image | {result.Summary.FalsePositivePerImage:F4} |",
            $"| Runtime p95 ms | {result.Summary.RuntimeMsP95:F3} |",
            $"| Method | {result.Summary.Method} |",
            $"| Threshold mode | {result.Summary.ThresholdMode} |",
            $"| Threshold | {result.Summary.Threshold:F3} |",
            $"| Min area | {result.Summary.MinArea} |",
            $"| Morph clean size | {result.Summary.MorphCleanSize} |",
            $"| Morph mode | {result.Summary.MorphMode} |",
            $"| Background kernel | {result.Summary.BackgroundKernelSize} |",
            $"| Response normalize mode | {result.Summary.ResponseNormalizeMode} |",
            $"| CLAHE clip / tile | {result.Summary.ClaheClipLimit:0.###} / {result.Summary.ClaheTileGridSize} |",
            $"| Component filter mode | {result.Summary.ComponentFilterMode} |",
            $"| Small noise area max | {result.Summary.SmallNoiseAreaMax} |",
            $"| Min elongation small component | {result.Summary.MinElongationForSmallComponent:0.###} |",
            $"| Compact noise area max | {result.Summary.CompactNoiseAreaMax} |",
            $"| Compact noise circularity min | {result.Summary.CompactNoiseCircularityMin:0.###} |",
            $"| Compact noise fill ratio min | {result.Summary.CompactNoiseFillRatioMin:0.###} |",
            $"| Min local response prominence | {result.Summary.MinLocalResponseProminence:0.###} |",
            $"| Max side | {result.Summary.MaxSide} |"
        };

        lines.AddRange([
            "",
            "## Image Confusion",
            "",
            "| TP | FP | FN | TN |",
            "| ---: | ---: | ---: | ---: |",
            $"| {result.ImageConfusion.TruePositive} | {result.ImageConfusion.FalsePositive} | {result.ImageConfusion.FalseNegative} | {result.ImageConfusion.TrueNegative} |"
        ]);

        if (result.MetricFailures.Count > 0)
        {
            lines.AddRange([
                "",
                "## Metric Failures",
                "",
                "| Scope | Metric | Value | Minimum |",
                "| --- | --- | ---: | ---: |"
            ]);

            foreach (var failure in result.MetricFailures)
            {
                lines.Add($"| {failure.Scope} | {failure.Metric} | {failure.Value:F4} | {failure.Minimum:F4} |");
            }
        }

        var failures = result.Images.Where(item => item.Error is not null).Take(10).ToList();
        if (failures.Count > 0)
        {
            lines.AddRange([
                "",
                "## Execution Failures",
                "",
                "| Id | Image | Error |",
                "| --- | --- | --- |"
            ]);

            foreach (var failure in failures)
            {
                lines.Add($"| {failure.Id} | `{failure.ImagePath}` | {failure.Error} |");
            }
        }

        var taxonomyCounts = result.Images
            .SelectMany(item => item.FailureTaxonomy)
            .GroupBy(item => item, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        if (taxonomyCounts.Count > 0)
        {
            lines.AddRange([
                "",
                "## Failure Taxonomy",
                "",
                "| Taxonomy | Count |",
                "| --- | ---: |"
            ]);

            foreach (var group in taxonomyCounts)
            {
                lines.Add($"| {group.Key} | {group.Count()} |");
            }
        }

        lines.AddRange([
            "",
            "## Notes",
            "",
            "- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.",
            "- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.",
            "- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record KolektorIndex(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source_dataset")] string SourceDataset,
    [property: JsonPropertyName("local_root")] string LocalRoot,
    [property: JsonPropertyName("records")] List<KolektorRecord> Records);

internal sealed record KolektorRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("image_path")] string ImagePath,
    [property: JsonPropertyName("mask_path")] string? MaskPath,
    [property: JsonPropertyName("has_mask")] bool HasMask,
    [property: JsonPropertyName("is_defect")] bool IsDefect);

internal readonly record struct ScoredLabel(double Score, bool Label);

internal sealed record BaselineResult(
    string EvidenceKind,
    BaselineSummary Summary,
    List<OperatorSummary> Operators,
    ConfusionSummary ImageConfusion,
    List<MetricFailure> MetricFailures,
    List<ImageResult> Images);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    string Dataset,
    string CandidateVersion,
    string ProfileName,
    string IndexPath,
    string Split,
    int MaxSide,
    string Method,
    string ThresholdMode,
    string NormalizationMode,
    double Threshold,
    int MinArea,
    int MaxArea,
    int MorphCleanSize,
    string MorphMode,
    int BackgroundKernelSize,
    double ReferenceStatsSigma,
    bool RobustReferenceStats,
    string ResponseNormalizeMode,
    double ClaheClipLimit,
    int ClaheTileGridSize,
    string ComponentFilterMode,
    int SmallNoiseAreaMax,
    double MinElongationForSmallComponent,
    int CompactNoiseAreaMax,
    double CompactNoiseCircularityMin,
    double CompactNoiseFillRatioMin,
    double MinLocalResponseProminence,
    int CaseCount,
    int Passed,
    int Failed,
    int DefectImageCount,
    int NormalImageCount,
    long PixelTruePositive,
    long PixelFalsePositive,
    long PixelFalseNegative,
    long PixelTrueNegative,
    double MaskIoU,
    double Dice,
    double PixelF1,
    double ImageAuroc,
    double PixelAuroc,
    double ImagePrecision,
    double ImageRecall,
    double ImageF1,
    double FalsePositivePerImage,
    double RuntimeMsP95,
    double MinImageAuroc,
    double MinPixelF1,
    double RuntimeMs,
    long MemoryAllocationBytes);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    bool HasPublicDataset,
    string EvidenceKind,
    string Dataset);

internal sealed record ConfusionSummary(
    int TruePositive,
    int FalsePositive,
    int FalseNegative,
    int TrueNegative);

internal sealed record MetricFailure(
    string Scope,
    string Metric,
    double Value,
    double Minimum);

internal sealed record ImageResult(
    string Id,
    string ImagePath,
    string? MaskPath,
    bool IsDefect,
    bool PredictedDefect,
    double Score,
    int DefectCount,
    double DefectArea,
    PixelTotals PixelTotals,
    bool ImageCorrect,
    List<string> FailureTaxonomy,
    List<ComponentTelemetry> Components,
    IReadOnlyDictionary<string, object?> Diagnostics,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? Error);

internal sealed record ComponentTelemetry(
    string CaseId,
    string Source,
    string Kind,
    int ComponentIndex,
    bool IsDefectImage,
    bool HasOverlap,
    double Area,
    int PixelArea,
    int RectX,
    int RectY,
    int RectWidth,
    int RectHeight,
    double Elongation,
    double FillRatio,
    double Circularity,
    double ComponentMean,
    double ComponentPeak,
    double RingMean,
    double RingProminence,
    int OverlapPixels,
    long TruePositivePixels,
    long FalsePositivePixels,
    long FalseNegativePixels);

internal readonly record struct PixelTotals(long TruePositive, long FalsePositive, long FalseNegative, long TrueNegative)
{
    public static readonly PixelTotals Empty = new(0, 0, 0, 0);

    public double IoU => TruePositive + FalsePositive + FalseNegative == 0
        ? 1.0
        : TruePositive / (double)(TruePositive + FalsePositive + FalseNegative);

    public double Dice => (2 * TruePositive) + FalsePositive + FalseNegative == 0
        ? 1.0
        : (2 * TruePositive) / (double)((2 * TruePositive) + FalsePositive + FalseNegative);

    public double F1 => Dice;

    public static PixelTotals Combine(IEnumerable<PixelTotals> totals)
    {
        long tp = 0;
        long fp = 0;
        long fn = 0;
        long tn = 0;
        foreach (var item in totals)
        {
            tp += item.TruePositive;
            fp += item.FalsePositive;
            fn += item.FalseNegative;
            tn += item.TrueNegative;
        }

        return new PixelTotals(tp, fp, fn, tn);
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
