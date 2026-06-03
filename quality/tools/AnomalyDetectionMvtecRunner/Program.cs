using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Infrastructure.AI.Anomaly;
using ClearVision.Product.Infrastructure.AI.Runtime;
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
    $"AnomalyDetection MVTec {result.Summary.CandidateVersion} complete: " +
    $"image_auroc={result.Summary.ImageAuroc:F4}, pixel_auroc={result.Summary.PixelAuroc:F4}, " +
    $"test={result.Summary.TestCount}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class MvtecRunner
{
    private static readonly string[] SupportedEmbeddingCatalogTypes = ["anomaly_embedding", "embedding"];

    public static BaselineResult Run(RunnerOptions options)
    {
        var index = LoadIndex(options.IndexPath);
        var testResults = new List<ImageResult>();
        var categoryResults = new List<CategoryResult>();
        var allPixelScores = new List<ScoredLabel>(capacity: 1024 * 1024);
        var stopwatchAll = Stopwatch.StartNew();
        var allocationBeforeAll = GC.GetTotalAllocatedBytes(precise: true);
        ValidateCaseIds(index, options);
        var embedding = ResolveEmbeddingModel(options);

        var selectedTestRecords = index.Records
            .Where(item => item.Split == "test")
            .Where(item => !options.HasCaseFilter || options.CaseIds.Contains(CaseId(item)))
            .ToList();
        if (selectedTestRecords.Count == 0)
        {
            throw new InvalidOperationException("No MVTec test records selected for AnomalyDetection evaluation.");
        }

        foreach (var category in selectedTestRecords.Select(item => item.Category).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var trainRecords = index.Records
                .Where(item => item.Category == category && item.Split == "train" && !item.IsAnomaly)
                .OrderBy(item => item.ImagePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var testRecords = selectedTestRecords
                .Where(item => item.Category == category)
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
                FeatureExtractorId = options.FeatureExtractorId,
                EmbeddingModelId = embedding.ModelId,
                EmbeddingModelPath = embedding.Path
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
                            CaseId(record),
                            record.Category,
                            record.DefectType,
                            record.ImagePath,
                            record.MaskPath,
                            record.IsAnomaly,
                            analysis.Score,
                            analysis.IsAnomaly,
                            analysis.PatchCount,
                            record.IsAnomaly == analysis.IsAnomaly,
                            BuildFailureTaxonomy(record, analysis.Score, analysis.IsAnomaly, options.Threshold),
                            true);
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
                    RoundMetric(ComputeAuroc(categoryImageResults.Select(item => new ScoredLabel(item.Score, item.IsAnomaly)))),
                    RoundMetric(ComputeAuroc(categoryPixelScores))));
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
        var imageTruePositive = testResults.Count(item => item.IsAnomaly && item.PredictedAnomaly);
        var imageFalsePositive = testResults.Count(item => !item.IsAnomaly && item.PredictedAnomaly);
        var imageFalseNegative = testResults.Count(item => item.IsAnomaly && !item.PredictedAnomaly);
        var imageTrueNegative = testResults.Count(item => !item.IsAnomaly && !item.PredictedAnomaly);
        var imagePrecision = SafeDivide(imageTruePositive, imageTruePositive + imageFalsePositive);
        var imageRecall = SafeDivide(imageTruePositive, imageTruePositive + imageFalseNegative);
        var imageF1 = SafeDivide(2 * imagePrecision * imageRecall, imagePrecision + imageRecall);

        var errorCount = testResults.Count(item => item.Error is not null);
        var metricFailures = EvaluateThresholds(imageAuroc, pixelAuroc, categoryResults, options);
        var failed = errorCount + metricFailures.Count;
        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                options.IndexPath,
                options.CandidateVersion,
                options.ProfileName,
                options.MaxSide,
                options.PatchSize,
                options.PatchStride,
                options.PixelSampleStride,
                options.CoresetRatio,
                options.Threshold,
                options.FeatureExtractorId,
                embedding.ModelId,
                embedding.Source,
                embedding.Configured,
                categoryResults.Sum(item => item.TrainCount),
                testResults.Count,
                testResults.Count(item => item.IsAnomaly),
                testResults.Count(item => !item.IsAnomaly),
                RoundMetric(imageAuroc),
                RoundMetric(pixelAuroc),
                imageTruePositive,
                imageFalsePositive,
                imageFalseNegative,
                imageTrueNegative,
                Math.Round(imagePrecision, 6),
                Math.Round(imageRecall, 6),
                Math.Round(imageF1, 6),
                options.MinImageAuroc,
                options.MinPixelAuroc,
                options.MinCategoryImageAuroc,
                options.MinCategoryPixelAuroc,
                failed,
                Math.Round(stopwatchAll.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfterAll - allocationBeforeAll)),
            [
                new OperatorSummary(
                    "AnomalyDetection",
                    testResults.Count + metricFailures.Count,
                    testResults.Count - errorCount,
                    failed,
                    Math.Round(stopwatchAll.Elapsed.TotalMilliseconds / Math.Max(1, testResults.Count), 3),
                    Math.Max(0, allocationAfterAll - allocationBeforeAll),
                    true)
            ],
            categoryResults,
            metricFailures,
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

    private static void ValidateCaseIds(MvtecIndex index, RunnerOptions options)
    {
        if (!options.HasCaseFilter)
        {
            return;
        }

        var available = index.Records
            .Where(item => item.Split == "test")
            .Select(CaseId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = options.CaseIds
            .Where(id => !available.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Unknown MVTec case id(s): {string.Join(", ", missing)}");
        }
    }

    private static EmbeddingResolution ResolveEmbeddingModel(RunnerOptions options)
    {
        if (!options.FeatureExtractorId.Equals("onnx_embedding", StringComparison.OrdinalIgnoreCase))
        {
            return EmbeddingResolution.Empty;
        }

        if (!string.IsNullOrWhiteSpace(options.EmbeddingModelPath))
        {
            var fullPath = Path.GetFullPath(options.EmbeddingModelPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Embedding model not found: {fullPath}");
            }

            return new EmbeddingResolution(fullPath, options.EmbeddingModelId, "ExplicitPath", true);
        }

        if (!string.IsNullOrWhiteSpace(options.EmbeddingModelId))
        {
            var resolved = ModelCatalog.ResolveExplicitOrCatalogPath(
                explicitPath: null,
                modelId: options.EmbeddingModelId,
                catalogPath: options.ModelCatalogPath,
                expectedTypes: SupportedEmbeddingCatalogTypes,
                out _);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Embedding model not found: {resolved}");
            }

            return new EmbeddingResolution(Path.GetFullPath(resolved), options.EmbeddingModelId, "ModelCatalog", true);
        }

        throw new InvalidOperationException("FeatureExtractorId=onnx_embedding requires --embedding-model or --embedding-model-id.");
    }

    private static string CaseId(MvtecRecord record)
    {
        var path = record.ImagePath.Replace('\\', '/');
        var fileName = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? record.ImagePath;
        return $"{record.Category}/{record.DefectType}/{Path.GetFileNameWithoutExtension(fileName)}";
    }

    private static List<string> BuildFailureTaxonomy(MvtecRecord record, double score, bool predictedAnomaly, double threshold)
    {
        var tags = new List<string>();
        if (record.IsAnomaly && !predictedAnomaly)
        {
            tags.Add("anomaly_miss");
            tags.Add(score <= 1e-9 ? "zero_score_anomaly" : "below_threshold_anomaly");
            tags.Add($"defect_{record.DefectType}");
        }
        else if (!record.IsAnomaly && predictedAnomaly)
        {
            tags.Add("good_false_positive");
            tags.Add(score >= threshold ? "above_threshold_good" : "threshold_margin_good");
        }

        return tags;
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

    private static double SafeDivide(double numerator, double denominator)
    {
        return denominator <= 0 ? 0 : numerator / denominator;
    }

    private static double RoundMetric(double value)
    {
        return double.IsFinite(value) ? Math.Round(value, 6) : 0;
    }

    private static List<MetricFailure> EvaluateThresholds(
        double imageAuroc,
        double pixelAuroc,
        IReadOnlyList<CategoryResult> categoryResults,
        RunnerOptions options)
    {
        var failures = new List<MetricFailure>();
        AddIfBelow(failures, "overall", "ImageAuroc", imageAuroc, options.MinImageAuroc);
        AddIfBelow(failures, "overall", "PixelAuroc", pixelAuroc, options.MinPixelAuroc);

        foreach (var category in categoryResults)
        {
            AddIfBelow(failures, category.Category, "ImageAuroc", category.ImageAuroc, options.MinCategoryImageAuroc);
            AddIfBelow(failures, category.Category, "PixelAuroc", category.PixelAuroc, options.MinCategoryPixelAuroc);
        }

        return failures;
    }

    private static void AddIfBelow(List<MetricFailure> failures, string scope, string metric, double value, double minimum)
    {
        if (double.IsNaN(value) && minimum <= 0)
        {
            return;
        }

        if (double.IsNaN(value) || value < minimum)
        {
            failures.Add(new MetricFailure(scope, metric, RoundMetric(value), minimum));
        }
    }
}

internal sealed record RunnerOptions(
    string IndexPath,
    string OutputPath,
    string ReportPath,
    string CandidateVersion,
    string ProfileName,
    IReadOnlySet<string> CaseIds,
    int MaxSide,
    int PatchSize,
    int PatchStride,
    int PixelSampleStride,
    double CoresetRatio,
    double Threshold,
    string FeatureExtractorId,
    string EmbeddingModelPath,
    string EmbeddingModelId,
    string ModelCatalogPath,
    double MinImageAuroc,
    double MinPixelAuroc,
    double MinCategoryImageAuroc,
    double MinCategoryPixelAuroc,
    bool ShowHelp,
    string? ParseError)
{
    public bool HasCaseFilter => CaseIds.Count > 0;

    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            IndexPath: "quality/datasets/mvtec_ad_lite_index.json",
            OutputPath: "quality/evals/reports/AnomalyDetection_mvtec_baseline.json",
            ReportPath: "quality/evals/reports/AnomalyDetection_mvtec_baseline.md",
            CandidateVersion: "baseline",
            ProfileName: "baseline_default",
            CaseIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            MaxSide: 128,
            PatchSize: 16,
            PatchStride: 16,
            PixelSampleStride: 2,
            CoresetRatio: 0.02,
            Threshold: 0.35,
            FeatureExtractorId: "lab_gradient_stats",
            EmbeddingModelPath: string.Empty,
            EmbeddingModelId: string.Empty,
            ModelCatalogPath: string.Empty,
            MinImageAuroc: 0.5,
            MinPixelAuroc: 0.5,
            MinCategoryImageAuroc: 0.5,
            MinCategoryPixelAuroc: 0.5,
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
                    "--case-ids" => options with { CaseIds = ParseCaseIds(NextValue()) },
                    "--max-side" => options with { MaxSide = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--patch-size" => options with { PatchSize = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--patch-stride" => options with { PatchStride = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--pixel-sample-stride" => options with { PixelSampleStride = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--coreset-ratio" => options with { CoresetRatio = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--threshold" => options with { Threshold = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--feature-extractor-id" => options with { FeatureExtractorId = NextValue() },
                    "--embedding-model" => options with { EmbeddingModelPath = NextValue() },
                    "--embedding-model-id" => options with { EmbeddingModelId = NextValue() },
                    "--model-catalog" => options with { ModelCatalogPath = NextValue() },
                    "--min-image-auroc" => options with { MinImageAuroc = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-pixel-auroc" => options with { MinPixelAuroc = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-category-image-auroc" => options with { MinCategoryImageAuroc = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                    "--min-category-pixel-auroc" => options with { MinCategoryPixelAuroc = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
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

    private static HashSet<string> ParseCaseIds(string raw)
    {
        return raw
            .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        Usage: dotnet run --project quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj -- [options]

        Options:
          --index <path>          MVTec AD Lite JSON index.
          --output <path>         Baseline JSON output path.
          --report <path>         Baseline Markdown report path.
          --candidate-version <id>
                                  Candidate version label. Default: baseline.
          --profile <name>        Candidate profile name. Default: baseline_default.
          --case-ids <csv>        Optional comma-separated test case ids, e.g. grid/bent/000.
          --max-side <int>        Resize long image side before evaluation. Default: 128.
          --patch-size <int>      SimplePatchCore patch size. Default: 16.
          --patch-stride <int>    SimplePatchCore patch stride. Default: 16.
          --pixel-sample-stride <int>
                                  Pixel AUROC sampling stride. Default: 2.
          --coreset-ratio <float> Feature-bank coreset ratio. Default: 0.02.
          --threshold <float>     Inference threshold for IsAnomaly/mask. Default: 0.35.
          --feature-extractor-id <id>
                                  lab_gradient_stats or onnx_embedding. Default: lab_gradient_stats.
          --embedding-model <path>
                                  External ONNX embedding model for FeatureExtractorId=onnx_embedding.
          --embedding-model-id <id>
                                  Model catalog id for FeatureExtractorId=onnx_embedding.
          --model-catalog <path>  Optional model catalog path for embedding model resolution.
          --min-image-auroc <float>
                                  Overall image AUROC release gate. Default: 0.5.
          --min-pixel-auroc <float>
                                  Overall pixel AUROC release gate. Default: 0.5.
          --min-category-image-auroc <float>
                                  Per-category image AUROC release gate. Default: 0.5.
          --min-category-pixel-auroc <float>
                                  Per-category pixel AUROC release gate. Default: 0.5.
        """);
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var label = string.Equals(result.Summary.CandidateVersion, "baseline", StringComparison.OrdinalIgnoreCase)
            ? "Baseline"
            : $"Candidate {result.Summary.CandidateVersion}";
        var lines = new List<string>
        {
            $"# AnomalyDetection MVTec AD Lite {label}",
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
            $"| Train images | {result.Summary.TrainCount} |",
            $"| Test images | {result.Summary.TestCount} |",
            $"| Test anomaly images | {result.Summary.TestAnomalyCount} |",
            $"| Test good images | {result.Summary.TestGoodCount} |",
            $"| Image AUROC | {result.Summary.ImageAuroc:F4} |",
            $"| Pixel AUROC | {result.Summary.PixelAuroc:F4} |",
            $"| Image precision | {result.Summary.ImagePrecision:F4} |",
            $"| Image recall | {result.Summary.ImageRecall:F4} |",
            $"| Image F1 | {result.Summary.ImageF1:F4} |",
            $"| Image TP / FP / FN / TN | {result.Summary.ImageTruePositive} / {result.Summary.ImageFalsePositive} / {result.Summary.ImageFalseNegative} / {result.Summary.ImageTrueNegative} |",
            $"| Min image AUROC | {result.Summary.MinImageAuroc:F4} |",
            $"| Min pixel AUROC | {result.Summary.MinPixelAuroc:F4} |",
            $"| Min category image AUROC | {result.Summary.MinCategoryImageAuroc:F4} |",
            $"| Min category pixel AUROC | {result.Summary.MinCategoryPixelAuroc:F4} |",
            $"| Failed gates | {result.Summary.Failed} |",
            $"| Max side | {result.Summary.MaxSide} |",
            $"| Patch size / stride | {result.Summary.PatchSize} / {result.Summary.PatchStride} |",
            $"| Pixel sample stride | {result.Summary.PixelSampleStride} |",
            $"| Coreset ratio | {result.Summary.CoresetRatio:F4} |",
            $"| Feature extractor | {result.Summary.FeatureExtractorId} |",
            $"| Embedding model id | {result.Summary.EmbeddingModelId} |",
            $"| Embedding model source | {result.Summary.EmbeddingModelSource} |",
            $"| Embedding model configured | {result.Summary.EmbeddingModelConfigured} |",
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

        var failureTags = result.Images
            .SelectMany(item => item.FailureTaxonomy)
            .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (failureTags.Count > 0)
        {
            lines.AddRange([
                "",
                "## Failure Taxonomy",
                "",
                "| Tag | Count |",
                "| --- | ---: |"
            ]);

            foreach (var group in failureTags)
            {
                lines.Add($"| {group.Key} | {group.Count()} |");
            }
        }

        var imageRows = result.Images
            .Where(item => item.FailureTaxonomy.Count > 0)
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DefectType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CaseId, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
        if (imageRows.Count > 0)
        {
            lines.AddRange([
                "",
                "## Diagnostic Images",
                "",
                "| Case | Is anomaly | Predicted | Score | Taxonomy |",
                "| --- | --- | --- | ---: | --- |"
            ]);

            foreach (var image in imageRows)
            {
                lines.Add($"| {image.CaseId} | {image.IsAnomaly} | {image.PredictedAnomaly} | {image.Score:F4} | {string.Join(", ", image.FailureTaxonomy)} |");
            }
        }

        lines.AddRange([
            "",
            "## Notes",
            "",
            "- Baseline uses the current SimplePatchCore-Lite implementation; `onnx_embedding` is an explicit candidate path and keeps model artifacts outside git.",
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

internal readonly record struct EmbeddingResolution(string Path, string ModelId, string Source, bool Configured)
{
    public static EmbeddingResolution Empty { get; } = new(string.Empty, string.Empty, "None", false);
}

internal sealed record BaselineResult(
    BaselineSummary Summary,
    List<OperatorSummary> Operators,
    List<CategoryResult> Categories,
    List<MetricFailure> MetricFailures,
    List<ImageResult> Images);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    string IndexPath,
    string CandidateVersion,
    string ProfileName,
    int MaxSide,
    int PatchSize,
    int PatchStride,
    int PixelSampleStride,
    double CoresetRatio,
    double Threshold,
    string FeatureExtractorId,
    string EmbeddingModelId,
    string EmbeddingModelSource,
    bool EmbeddingModelConfigured,
    int TrainCount,
    int TestCount,
    int TestAnomalyCount,
    int TestGoodCount,
    double ImageAuroc,
    double PixelAuroc,
    int ImageTruePositive,
    int ImageFalsePositive,
    int ImageFalseNegative,
    int ImageTrueNegative,
    double ImagePrecision,
    double ImageRecall,
    double ImageF1,
    double MinImageAuroc,
    double MinPixelAuroc,
    double MinCategoryImageAuroc,
    double MinCategoryPixelAuroc,
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

internal sealed record MetricFailure(
    string Scope,
    string Metric,
    double Value,
    double Minimum);

internal sealed record ImageResult(
    string CaseId,
    string Category,
    string DefectType,
    string ImagePath,
    string? MaskPath,
    bool IsAnomaly,
    double Score,
    bool PredictedAnomaly,
    int PatchCount,
    bool ImageCorrect,
    List<string> FailureTaxonomy,
    bool Passed,
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
