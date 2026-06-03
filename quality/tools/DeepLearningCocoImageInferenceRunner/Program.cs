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

var result = CocoImageInferenceRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"DeepLearning COCO image inference complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"AP50={result.Summary.AP50:F4}, recall={result.Summary.RecallAt50:F4}, output={options.OutputPath}");

return result.Summary.AP50 >= 0.95 && result.Summary.RecallAt50 >= 0.95 && result.Summary.PrecisionAt50 >= 0.99 ? 0 : 1;

internal static class CocoImageInferenceRunner
{
    private const string EvidenceKind = "dataset";
    private const string DatasetName = "COCO 2017 real validation images";
    private const string DatasetKind = "COCO real-image inference protocol: image pixels pass through product preprocessing and YOLO postprocessing; tensor candidates are annotation-seeded because no trained COCO model is bundled.";
    private const int InputSize = 640;
    private const float ConfidenceThreshold = 0.45f;
    private const float NmsIouThreshold = 0.45f;
    private const float MatchIouThreshold = 0.50f;
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);

    public static BaselineResult Run(RunnerOptions options)
    {
        var dataset = CocoDataset.Load(options.IndexPath, options.MaxCases, options.MaxBoxesPerImage, options.MaxClasses);
        var results = dataset.Cases.Select(RunCase).ToList();
        var totalTruePositives = results.Sum(item => item.TruePositiveCount);
        var totalFalsePositives = results.Sum(item => item.FalsePositiveCount);
        var totalFalseNegatives = results.Sum(item => item.FalseNegativeCount);
        var totalGroundTruth = results.Sum(item => item.GroundTruthCount);
        var matchedIous = results.SelectMany(item => item.MatchedIous).ToArray();
        var scoredPredictions = results.SelectMany(item => item.ScoredPredictions).ToArray();
        var precision = totalTruePositives + totalFalsePositives == 0
            ? 1d
            : totalTruePositives / (double)(totalTruePositives + totalFalsePositives);
        var recall = totalGroundTruth == 0
            ? 1d
            : totalTruePositives / (double)totalGroundTruth;
        var ap50 = ComputeAP50(scoredPredictions, totalGroundTruth);
        var failed = results.Count(item => !item.Passed);

        return new BaselineResult(
            EvidenceKind,
            new DatasetSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                DatasetKind,
                options.IndexPath.Replace('\\', '/'),
                dataset.AnnotationPath.Replace('\\', '/'),
                dataset.Cases.Count,
                results.Count - failed,
                failed,
                dataset.CategoryCount,
                totalGroundTruth,
                totalTruePositives,
                totalFalsePositives,
                totalFalseNegatives,
                Math.Round(precision, 6),
                Math.Round(recall, 6),
                Math.Round(ap50, 6),
                Math.Round(matchedIous.Length == 0 ? 0 : matchedIous.Average(), 6),
                ConfidenceThreshold,
                NmsIouThreshold,
                MatchIouThreshold,
                Math.Round(results.Sum(item => item.RuntimeMs), 3),
                results.Sum(item => item.MemoryAllocationBytes)),
            [
                new OperatorSummary(
                    "DeepLearning",
                    dataset.Cases.Count,
                    results.Count - failed,
                    failed,
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    (long)Math.Round(results.Average(item => item.MemoryAllocationBytes)),
                    true,
                    "dataset",
                    DatasetName)
            ],
            results
                .GroupBy(item => item.CategorySet)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ScenarioSummary(
                    group.Key,
                    group.Count(),
                    group.Count(item => item.Passed),
                    group.Count(item => !item.Passed),
                    group.Sum(item => item.GroundTruthCount),
                    group.Sum(item => item.DetectionCount),
                    group.Sum(item => item.TruePositiveCount),
                    group.Sum(item => item.FalsePositiveCount),
                    group.Sum(item => item.FalseNegativeCount),
                    Math.Round(group.Average(item => item.RuntimeMs), 3)))
                .ToArray(),
            results);
    }

    private static CaseResult RunCase(CocoCaseSpec spec)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            using var image = Cv2.ImRead(spec.ImagePath, ImreadModes.Color);
            if (image.Empty())
            {
                throw new FileNotFoundException($"Unable to read COCO image: {spec.ImagePath}");
            }

            var tensor = InvokePreprocessImage(image, InputSize);
            var tensorShape = string.Join(",", tensor.Dimensions.ToArray());
            if (tensorShape != "1,3,640,640")
            {
                throw new InvalidOperationException($"Unexpected preprocess tensor shape: {tensorShape}");
            }

            var predictions = spec.GroundTruth
                .Select((box, index) => box.WithJitter(index % 2 == 0 ? 0.6f : -0.6f, index % 3 == 0 ? 0.5f : -0.5f, 0.97f - Math.Min(index, 10) * 0.01f))
                .ToList();
            var outputTensor = CreateYoloV8Tensor(spec.ClassCount, Math.Max(256, predictions.Count + 16));
            for (var i = 0; i < predictions.Count; i++)
            {
                WritePrediction(outputTensor, i, predictions[i], spec.Width, spec.Height);
            }

            var detections = InvokePostprocessYoloV8(
                outputTensor,
                ConfidenceThreshold,
                spec.Width,
                spec.Height,
                InputSize,
                enableNms: true,
                nmsIou: NmsIouThreshold);

            var evaluation = Evaluate(spec.GroundTruth, detections);
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            var passed = evaluation.FalsePositiveCount == 0 && evaluation.FalseNegativeCount == 0;

            return new CaseResult(
                spec.CaseId,
                spec.CategorySet,
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                spec.Width,
                spec.Height,
                spec.GroundTruth.Count,
                detections.Count,
                evaluation.TruePositiveCount,
                evaluation.FalsePositiveCount,
                evaluation.FalseNegativeCount,
                Math.Round(evaluation.BestMatchedIou, 6),
                evaluation.MatchedIous.Select(item => Math.Round(item, 6)).ToArray(),
                evaluation.ScoredPredictions,
                detections,
                passed ? null : evaluation.FailureReason);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                spec.CaseId,
                spec.CategorySet,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                spec.Width,
                spec.Height,
                spec.GroundTruth.Count,
                0,
                0,
                0,
                spec.GroundTruth.Count,
                0,
                [],
                [],
                [],
                ex.GetBaseException().Message);
        }
    }

    private static DenseTensor<float> CreateYoloV8Tensor(int classCount, int anchorCount)
    {
        return new DenseTensor<float>(new float[1 * (4 + classCount) * anchorCount], [1, 4 + classCount, anchorCount]);
    }

    private static void WritePrediction(DenseTensor<float> tensor, int anchor, Box box, int originalWidth, int originalHeight)
    {
        var scale = Math.Min((float)InputSize / originalWidth, (float)InputSize / originalHeight);
        var xPad = (InputSize - originalWidth * scale) / 2f;
        var yPad = (InputSize - originalHeight * scale) / 2f;
        tensor[0, 0, anchor] = (box.X + box.Width / 2f) * scale + xPad;
        tensor[0, 1, anchor] = (box.Y + box.Height / 2f) * scale + yPad;
        tensor[0, 2, anchor] = box.Width * scale;
        tensor[0, 3, anchor] = box.Height * scale;
        tensor[0, 4 + box.ClassId, anchor] = box.Confidence;
    }

    private static CaseEvaluation Evaluate(IReadOnlyList<Box> groundTruth, IReadOnlyList<DetectionRecord> detections)
    {
        var matched = new bool[groundTruth.Count];
        var truePositives = 0;
        var falsePositives = 0;
        var matchedIous = new List<double>();
        var scored = new List<ScoredPrediction>();

        foreach (var detection in detections.OrderByDescending(item => item.Confidence))
        {
            var bestIndex = -1;
            var bestIou = 0d;
            for (var i = 0; i < groundTruth.Count; i++)
            {
                if (matched[i] || groundTruth[i].ClassId != detection.ClassId)
                {
                    continue;
                }

                var iou = IoU(groundTruth[i], detection);
                if (iou > bestIou)
                {
                    bestIou = iou;
                    bestIndex = i;
                }
            }

            var isTruePositive = bestIndex >= 0 && bestIou >= MatchIouThreshold;
            if (isTruePositive)
            {
                matched[bestIndex] = true;
                truePositives++;
                matchedIous.Add(bestIou);
            }
            else
            {
                falsePositives++;
            }

            scored.Add(new ScoredPrediction(Math.Round(detection.Confidence, 6), isTruePositive));
        }

        var falseNegatives = matched.Count(item => !item);
        var failure = falsePositives == 0 && falseNegatives == 0
            ? null
            : $"FP={falsePositives}, FN={falseNegatives}, detections={detections.Count}, gt={groundTruth.Count}";
        return new CaseEvaluation(truePositives, falsePositives, falseNegatives, matchedIous.Count == 0 ? 0 : matchedIous.Max(), matchedIous, scored, failure);
    }

    private static double IoU(Box a, DetectionRecord b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = a.Width * a.Height + b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static double ComputeAP50(IReadOnlyList<ScoredPrediction> predictions, int totalGroundTruth)
    {
        if (totalGroundTruth == 0)
        {
            return 1d;
        }

        var ordered = predictions.OrderByDescending(item => item.Confidence).ToList();
        if (ordered.Count == 0)
        {
            return 0d;
        }

        var curve = new List<(double Recall, double Precision)>();
        var tp = 0;
        var fp = 0;
        foreach (var prediction in ordered)
        {
            if (prediction.IsTruePositive)
            {
                tp++;
            }
            else
            {
                fp++;
            }

            curve.Add((tp / (double)totalGroundTruth, tp / (double)(tp + fp)));
        }

        var ap = 0d;
        for (var threshold = 0; threshold <= 100; threshold++)
        {
            var recallThreshold = threshold / 100d;
            var precision = curve
                .Where(item => item.Recall >= recallThreshold)
                .Select(item => item.Precision)
                .DefaultIfEmpty(0)
                .Max();
            ap += precision;
        }

        return ap / 101d;
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

internal static class CocoDataset
{
    public static CocoDatasetSpec Load(string indexPath, int maxCases, int maxBoxesPerImage, int maxClasses)
    {
        var repoRoot = FindRepoRoot();
        var fullIndexPath = ResolveRepoPath(repoRoot, indexPath);
        using var indexDocument = JsonDocument.Parse(File.ReadAllText(fullIndexPath));
        var indexRoot = indexDocument.RootElement;
        var annotationPath = ResolveRepoPath(repoRoot, indexRoot.GetProperty("annotation_file").GetString() ?? "");
        var annotationsByImage = LoadAnnotations(annotationPath);
        var categoryMap = new Dictionary<int, int>();
        var cases = new List<CocoCaseSpec>();

        foreach (var record in indexRoot.GetProperty("records").EnumerateArray())
        {
            if (cases.Count >= maxCases)
            {
                break;
            }

            var imageIdText = record.GetProperty("id").GetString() ?? "";
            if (!int.TryParse(imageIdText, out var imageId) || !annotationsByImage.TryGetValue(imageId, out var annotations))
            {
                continue;
            }

            var boxes = new List<Box>();
            foreach (var annotation in annotations.Take(maxBoxesPerImage))
            {
                if (!categoryMap.TryGetValue(annotation.CategoryId, out var classId))
                {
                    if (categoryMap.Count >= maxClasses)
                    {
                        continue;
                    }

                    classId = categoryMap.Count;
                    categoryMap[annotation.CategoryId] = classId;
                }

                boxes.Add(new Box(annotation.X, annotation.Y, annotation.Width, annotation.Height, classId, 0.97f));
            }

            if (boxes.Count == 0)
            {
                continue;
            }

            var imagePath = ResolveRepoPath(repoRoot, record.GetProperty("image_path").GetString() ?? "");
            if (!File.Exists(imagePath))
            {
                continue;
            }

            var categorySet = string.Join("+", boxes.Select(item => item.ClassId).Distinct().OrderBy(item => item).Take(4).Select(item => $"c{item}"));
            cases.Add(new CocoCaseSpec(
                $"coco2017_val_{imageIdText}",
                categorySet,
                imagePath,
                record.GetProperty("width").GetInt32(),
                record.GetProperty("height").GetInt32(),
                boxes,
                maxClasses));
        }

        return new CocoDatasetSpec(RepoRelative(repoRoot, annotationPath), categoryMap.Count, cases);
    }

    private static Dictionary<int, List<CocoAnnotation>> LoadAnnotations(string annotationPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(annotationPath));
        var result = new Dictionary<int, List<CocoAnnotation>>();
        foreach (var annotation in document.RootElement.GetProperty("annotations").EnumerateArray())
        {
            if (annotation.TryGetProperty("iscrowd", out var isCrowd) && isCrowd.GetInt32() != 0)
            {
                continue;
            }

            var bbox = annotation.GetProperty("bbox");
            var x = bbox[0].GetSingle();
            var y = bbox[1].GetSingle();
            var width = bbox[2].GetSingle();
            var height = bbox[3].GetSingle();
            if (width < 2 || height < 2)
            {
                continue;
            }

            var imageId = annotation.GetProperty("image_id").GetInt32();
            var categoryId = annotation.GetProperty("category_id").GetInt32();
            if (!result.TryGetValue(imageId, out var list))
            {
                list = [];
                result[imageId] = list;
            }

            list.Add(new CocoAnnotation(categoryId, x, y, width, height));
        }

        foreach (var key in result.Keys.ToArray())
        {
            result[key] = result[key]
                .OrderByDescending(item => item.Width * item.Height)
                .ToList();
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveRepoPath(string repoRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Dataset path must not be empty.");
        }

        return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(repoRoot, value));
    }

    private static string RepoRelative(string repoRoot, string path)
    {
        return Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
    }
}

internal sealed record CocoDatasetSpec(string AnnotationPath, int CategoryCount, List<CocoCaseSpec> Cases);
internal sealed record CocoAnnotation(int CategoryId, float X, float Y, float Width, float Height);
internal sealed record CocoCaseSpec(string CaseId, string CategorySet, string ImagePath, int Width, int Height, IReadOnlyList<Box> GroundTruth, int ClassCount);

internal sealed record Box(float X, float Y, float Width, float Height, int ClassId, float Confidence)
{
    public Box WithJitter(float dx, float dy, float confidence)
    {
        return this with { X = X + dx, Y = Y + dy, Confidence = confidence };
    }
}

internal sealed record DetectionRecord(float X, float Y, float Width, float Height, float Confidence, int ClassId);
internal sealed record ScoredPrediction(double Confidence, bool IsTruePositive);
internal sealed record CaseEvaluation(int TruePositiveCount, int FalsePositiveCount, int FalseNegativeCount, double BestMatchedIou, IReadOnlyList<double> MatchedIous, IReadOnlyList<ScoredPrediction> ScoredPredictions, string? FailureReason);
internal sealed record BaselineResult(string EvidenceKind, DatasetSummary Summary, IReadOnlyList<OperatorSummary> Operators, IReadOnlyList<ScenarioSummary> Scenarios, IReadOnlyList<CaseResult> Cases);
internal sealed record DatasetSummary(DateTimeOffset GeneratedAtUtc, string DatasetName, string DatasetKind, string IndexPath, string AnnotationPath, int CaseCount, int Passed, int Failed, int CategoryCount, int GroundTruthCount, int TruePositiveCount, int FalsePositiveCount, int FalseNegativeCount, double PrecisionAt50, double RecallAt50, double AP50, double MeanMatchedIoU, double ConfidenceThreshold, double NmsIouThreshold, double MatchIouThreshold, double RuntimeMs, long MemoryAllocationBytes);
internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double RuntimeMsAvg, long MemoryAllocationBytesAvg, bool HasPublicDataset, string EvidenceKind, string DatasetName);
internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, int GroundTruthCount, int DetectionCount, int TruePositiveCount, int FalsePositiveCount, int FalseNegativeCount, double RuntimeMsAvg);
internal sealed record CaseResult(string CaseId, string CategorySet, bool Passed, double RuntimeMs, long MemoryAllocationBytes, int Width, int Height, int GroundTruthCount, int DetectionCount, int TruePositiveCount, int FalsePositiveCount, int FalseNegativeCount, double BestMatchedIou, double[] MatchedIous, IReadOnlyList<ScoredPrediction> ScoredPredictions, IReadOnlyList<DetectionRecord> Detections, string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning COCO Image Inference Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Categories seen | {result.Summary.CategoryCount} |",
            $"| Ground truth boxes | {result.Summary.GroundTruthCount} |",
            $"| True positives | {result.Summary.TruePositiveCount} |",
            $"| False positives | {result.Summary.FalsePositiveCount} |",
            $"| False negatives | {result.Summary.FalseNegativeCount} |",
            $"| Precision@0.50 | {result.Summary.PrecisionAt50:0.####} |",
            $"| Recall@0.50 | {result.Summary.RecallAt50:0.####} |",
            $"| AP50 | {result.Summary.AP50:0.####} |",
            $"| Mean matched IoU | {result.Summary.MeanMatchedIoU:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Evidence Boundary",
            "",
            "- This runner consumes real COCO 2017 validation images and annotations.",
            "- It verifies ClearVision DeepLearning image preprocessing, YOLO tensor postprocessing, NMS, coordinate unletterboxing, and COCO-style scoring on real image dimensions.",
            "- The candidate tensor is annotation-seeded because this repository does not bundle a trained COCO detector; this is not a production model accuracy claim.",
            "",
            "## Cases",
            "",
            "| Case | Categories | Passed | Size | GT | Detections | TP | FP | FN | Best IoU | Runtime ms | Failure |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        };

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.CategorySet} | {item.Passed} | {item.Width}x{item.Height} | {item.GroundTruthCount} | {item.DetectionCount} | {item.TruePositiveCount} | {item.FalsePositiveCount} | {item.FalseNegativeCount} | {item.BestMatchedIou:0.####} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(string IndexPath, string OutputPath, string ReportPath, int MaxCases, int MaxBoxesPerImage, int MaxClasses, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/datasets/coco2017_index.json",
            "quality/evals/reports/DeepLearning_coco_image_inference_baseline.json",
            "quality/evals/reports/DeepLearning_coco_image_inference_baseline.md",
            120,
            20,
            80,
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
                "--index" => options with { IndexPath = value },
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                "--max-cases" => int.TryParse(value, out var maxCases) && maxCases > 0
                    ? options with { MaxCases = maxCases }
                    : options with { ParseError = "--max-cases must be a positive integer." },
                "--max-boxes-per-image" => int.TryParse(value, out var maxBoxes) && maxBoxes > 0
                    ? options with { MaxBoxesPerImage = maxBoxes }
                    : options with { ParseError = "--max-boxes-per-image must be a positive integer." },
                "--max-classes" => int.TryParse(value, out var maxClasses) && maxClasses is > 0 and <= 256
                    ? options with { MaxClasses = maxClasses }
                    : options with { ParseError = "--max-classes must be between 1 and 256." },
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
        Console.WriteLine(
            "Usage: dotnet run --project quality/tools/DeepLearningCocoImageInferenceRunner/DeepLearningCocoImageInferenceRunner.csproj -- " +
            "--index quality/datasets/coco2017_index.json --output <json> --report <md> [--max-cases 120]");
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };
}
