using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Infrastructure.AI.Anomaly;
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

var result = MvtecRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"AnomalyDetection MVTec baseline complete: " +
    $"image_auroc={result.Summary.ImageAuroc:F4}, pixel_auroc={result.Summary.PixelAuroc:F4}, " +
    $"test={result.Summary.TestCount}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class MvtecRunner
{
    public static BaselineResult Run(RunnerOptions options)
    {
        var index = LoadIndex(options.IndexPath);
        var testResults = new List<ImageResult>();
        var categoryResults = new List<CategoryResult>();
        var allPixelScores = new List<ScoredLabel>(capacity: 1024 * 1024);
        var stopwatchAll = Stopwatch.StartNew();
        var allocationBeforeAll = GC.GetTotalAllocatedBytes(precise: true);

        foreach (var category in index.Records.Select(item => item.Category).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var trainRecords = index.Records
                .Where(item => item.Category == category && item.Split == "train" && !item.IsAnomaly)
                .OrderBy(item => item.ImagePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var testRecords = index.Records
                .Where(item => item.Category == category && item.Split == "test")
                .OrderBy(item => item.ImagePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (trainRecords.Count == 0 || testRecords.Count == 0)
            {
                throw new InvalidOperationException($"Category {category} has train={trainRecords.Count}, test={testRecords.Count}.");
            }

            var detectorOptions = new SimplePatchCoreOptions
            {
                PatchSize = options.PatchSize,
                PatchStride = options.PatchStride,
                CoresetRatio = options.CoresetRatio,
                Backbone = "simple_patchcore",
                FeatureExtractorId = "lab_gradient_stats"
            };

            var trainImages = new List<Mat>(trainRecords.Count);
            try
            {
                foreach (var record in trainRecords)
                {
                    trainImages.Add(LoadImage(record.ImagePath, options.MaxSide, interpolation: InterpolationFlags.Area));
                }

                var trainWatch = Stopwatch.StartNew();
                var bank = SimplePatchCoreDetector.BuildFeatureBank(trainImages, detectorOptions);
                trainWatch.Stop();

                var categoryImageResults = new List<ImageResult>();
                var categoryPixelScores = new List<ScoredLabel>();
                var inferenceWatch = Stopwatch.StartNew();
                Console.WriteLine(
                    $"Category {category}: train={trainRecords.Count}, test={testRecords.Count}, " +
                    $"bank_features={bank.Features.Count}");

                foreach (var record in testRecords)
                {
                    using var image = LoadImage(record.ImagePath, options.MaxSide, interpolation: InterpolationFlags.Area);
                    var analysis = SimplePatchCoreDetector.Analyze(image, bank, options.Threshold, detectorOptions);
                    try
                    {
                        using var mask = LoadMask(record, image.Size());
                        var pixelScores = CollectPixelScores(analysis.ScoreMap, mask, options.PixelSampleStride);
                        categoryPixelScores.AddRange(pixelScores);
                        allPixelScores.AddRange(pixelScores);

                        var imageResult = new ImageResult(
                            record.Category,
                            record.DefectType,
                            record.ImagePath,
                            record.MaskPath,
                            record.IsAnomaly,
                            analysis.Score,
                            analysis.IsAnomaly,
                            analysis.PatchCount);
                        categoryImageResults.Add(imageResult);
                        testResults.Add(imageResult);
                    }
                    finally
                    {
                        analysis.ScoreMap.Dispose();
                        analysis.Mask.Dispose();
                        analysis.Heatmap.Dispose();
                    }
                }

                inferenceWatch.Stop();

                categoryResults.Add(new CategoryResult(
                    category,
                    trainRecords.Count,
                    testRecords.Count,
                    testRecords.Count(item => item.IsAnomaly),
                    bank.Features.Count,
                    Math.Round(trainWatch.Elapsed.TotalMilliseconds, 3),
                    Math.Round(inferenceWatch.Elapsed.TotalMilliseconds, 3),
                    Math.Round(ComputeAuroc(categoryImageResults.Select(item => new ScoredLabel(item.Score, item.IsAnomaly))), 6),
                    Math.Round(ComputeAuroc(categoryPixelScores), 6)));
            }
            finally
            {
                foreach (var image in trainImages)
                {
                    image.Dispose();
                }
            }
        }

        stopwatchAll.Stop();
        var allocationAfterAll = GC.GetTotalAllocatedBytes(precise: true);
        var imageAuroc = ComputeAuroc(testResults.Select(item => new ScoredLabel(item.Score, item.IsAnomaly)));
        var pixelAuroc = ComputeAuroc(allPixelScores);

        var failed = testResults.Count(item => item.Error is not null);
        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                options.IndexPath,
                options.MaxSide,
                options.PatchSize,
                options.PatchStride,
                options.PixelSampleStride,
                options.CoresetRatio,
                options.Threshold,
                categoryResults.Sum(item => item.TrainCount),
                testResults.Count,
                testResults.Count(item => item.IsAnomaly),
                testResults.Count(item => !item.IsAnomaly),
                Math.Round(imageAuroc, 6),
                Math.Round(pixelAuroc, 6),
                failed,
                Math.Round(stopwatchAll.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfterAll - allocationBeforeAll)),
            [
                new OperatorSummary(
                    "AnomalyDetection",
                    testResults.Count,
                    testResults.Count - failed,
                    failed,
                    Math.Round(stopwatchAll.Elapsed.TotalMilliseconds / Math.Max(1, testResults.Count), 3),
                    Math.Max(0, allocationAfterAll - allocationBeforeAll),
                    true)
            ],
            categoryResults,
            testResults);
    }

    private static MvtecIndex LoadIndex(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"MVTec index not found: {fullPath}. Run quality/datasets/converters/convert_mvtec_ad.py first.");
        }

        var index = JsonSerializer.Deserialize<MvtecIndex>(File.ReadAllText(fullPath), JsonSettings.Default);
        return index ?? throw new InvalidOperationException($"Failed to parse MVTec index: {fullPath}");
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

    private static Mat LoadMask(MvtecRecord record, Size imageSize)
    {
        if (!record.IsAnomaly || string.IsNullOrWhiteSpace(record.MaskPath))
        {
            return new Mat(imageSize.Height, imageSize.Width, MatType.CV_8UC1, Scalar.All(0));
        }

        var path = Path.GetFullPath(record.MaskPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Mask not found: {path}");
        }

        using var source = Cv2.ImRead(path, ImreadModes.Grayscale);
        if (source.Empty())
        {
            throw new InvalidOperationException($"Failed to load mask: {path}");
        }

        var resized = new Mat();
        Cv2.Resize(source, resized, imageSize, 0, 0, InterpolationFlags.Nearest);
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

    private static List<ScoredLabel> CollectPixelScores(Mat scoreMap, Mat mask, int sampleStride)
    {
        if (scoreMap.Type() != MatType.CV_32FC1)
        {
            throw new InvalidOperationException($"Expected CV_32FC1 score map, got {scoreMap.Type()}.");
        }

        if (scoreMap.Size() != mask.Size())
        {
            throw new InvalidOperationException($"Score map and mask size mismatch: {scoreMap.Size()} vs {mask.Size()}.");
        }

        var stride = Math.Max(1, sampleStride);
        var scores = new List<ScoredLabel>((scoreMap.Rows / stride + 1) * (scoreMap.Cols / stride + 1));
        for (var y = 0; y < scoreMap.Rows; y += stride)
        {
            for (var x = 0; x < scoreMap.Cols; x += stride)
            {
                scores.Add(new ScoredLabel(scoreMap.At<float>(y, x), mask.At<byte>(y, x) > 0));
            }
        }

        return scores;
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
}

internal sealed record RunnerOptions(
    string IndexPath,
    string OutputPath,
    string ReportPath,
    int MaxSide,
    int PatchSize,
    int PatchStride,
    int PixelSampleStride,
    double CoresetRatio,
    double Threshold,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            IndexPath: "quality/datasets/mvtec_ad_lite_index.json",
            OutputPath: "quality/evals/reports/AnomalyDetection_mvtec_baseline.json",
            ReportPath: "quality/evals/reports/AnomalyDetection_mvtec_baseline.md",
            MaxSide: 128,
            PatchSize: 16,
            PatchStride: 16,
            PixelSampleStride: 2,
            CoresetRatio: 0.02,
            Threshold: 0.35,
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
                    "--max-side" => options with { MaxSide = int.Parse(NextValue()) },
                    "--patch-size" => options with { PatchSize = int.Parse(NextValue()) },
                    "--patch-stride" => options with { PatchStride = int.Parse(NextValue()) },
                    "--pixel-sample-stride" => options with { PixelSampleStride = int.Parse(NextValue()) },
                    "--coreset-ratio" => options with { CoresetRatio = double.Parse(NextValue()) },
                    "--threshold" => options with { Threshold = double.Parse(NextValue()) },
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

    public static void PrintHelp()
    {
        Console.WriteLine("""
        Usage: dotnet run --project quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj -- [options]

        Options:
          --index <path>          MVTec AD Lite JSON index.
          --output <path>         Baseline JSON output path.
          --report <path>         Baseline Markdown report path.
          --max-side <int>        Resize long image side before evaluation. Default: 128.
          --patch-size <int>      SimplePatchCore patch size. Default: 16.
          --patch-stride <int>    SimplePatchCore patch stride. Default: 16.
          --pixel-sample-stride <int>
                                  Pixel AUROC sampling stride. Default: 2.
          --coreset-ratio <float> Feature-bank coreset ratio. Default: 0.02.
          --threshold <float>     Inference threshold for IsAnomaly/mask. Default: 0.35.
        """);
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# AnomalyDetection MVTec AD Lite Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Index: `{result.Summary.IndexPath}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Train images | {result.Summary.TrainCount} |",
            $"| Test images | {result.Summary.TestCount} |",
            $"| Test anomaly images | {result.Summary.TestAnomalyCount} |",
            $"| Test good images | {result.Summary.TestGoodCount} |",
            $"| Image AUROC | {result.Summary.ImageAuroc:F4} |",
            $"| Pixel AUROC | {result.Summary.PixelAuroc:F4} |",
            $"| Max side | {result.Summary.MaxSide} |",
            $"| Patch size / stride | {result.Summary.PatchSize} / {result.Summary.PatchStride} |",
            $"| Pixel sample stride | {result.Summary.PixelSampleStride} |",
            $"| Coreset ratio | {result.Summary.CoresetRatio:F4} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:F3} |",
            "",
            "## Categories",
            "",
            "| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        foreach (var category in result.Categories)
        {
            lines.Add(
                $"| {category.Category} | {category.TrainCount} | {category.TestCount} | {category.TestAnomalyCount} | " +
                $"{category.FeatureBankCount} | {category.TrainRuntimeMs:F3} | {category.InferenceRuntimeMs:F3} | " +
                $"{category.ImageAuroc:F4} | {category.PixelAuroc:F4} |");
        }

        lines.AddRange([
            "",
            "## Notes",
            "",
            "- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.",
            "- Images and masks are resized to the configured max side before evaluation.",
            "- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.",
            "- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record MvtecIndex(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source_dataset")] string SourceDataset,
    [property: JsonPropertyName("local_root")] string LocalRoot,
    [property: JsonPropertyName("records")] List<MvtecRecord> Records);

internal sealed record MvtecRecord(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("defect_type")] string DefectType,
    [property: JsonPropertyName("image_path")] string ImagePath,
    [property: JsonPropertyName("mask_path")] string? MaskPath,
    [property: JsonPropertyName("is_anomaly")] bool IsAnomaly);

internal readonly record struct ScoredLabel(double Score, bool Label);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    List<OperatorSummary> Operators,
    List<CategoryResult> Categories,
    List<ImageResult> Images);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    string IndexPath,
    int MaxSide,
    int PatchSize,
    int PatchStride,
    int PixelSampleStride,
    double CoresetRatio,
    double Threshold,
    int TrainCount,
    int TestCount,
    int TestAnomalyCount,
    int TestGoodCount,
    double ImageAuroc,
    double PixelAuroc,
    int Failed,
    double RuntimeMs,
    long MemoryAllocationBytes);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    bool HasPublicDataset);

internal sealed record CategoryResult(
    string Category,
    int TrainCount,
    int TestCount,
    int TestAnomalyCount,
    int FeatureBankCount,
    double TrainRuntimeMs,
    double InferenceRuntimeMs,
    double ImageAuroc,
    double PixelAuroc);

internal sealed record ImageResult(
    string Category,
    string DefectType,
    string ImagePath,
    string? MaskPath,
    bool IsAnomaly,
    double Score,
    bool PredictedAnomaly,
    int PatchCount,
    string? Error = null);

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
