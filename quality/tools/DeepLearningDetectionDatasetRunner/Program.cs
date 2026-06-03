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

var result = DeepLearningDetectionDatasetRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"DeepLearning detection dataset complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"AP50={result.Summary.AP50:F4}, recall={result.Summary.RecallAt50:F4}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class DeepLearningDetectionDatasetRunner
{
    private const string EvidenceKind = "dataset";
    private const string DatasetName = "COCO-style semi-synthetic detection protocol bridge";
    private const int InputSize = 640;
    private const int AnchorCount = 192;
    private const int ClassCount = 3;
    private const float ConfidenceThreshold = 0.45f;
    private const float NmsIouThreshold = 0.45f;
    private const float MatchIouThreshold = 0.50f;
    private static readonly string[] Labels = ["scratch", "missing_part", "extra_part"];
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);

    public static BaselineResult Run(RunnerOptions options)
    {
        var specs = BuildCases().ToList();
        var results = new List<CaseResult>(specs.Count);
        foreach (var spec in specs)
        {
            results.Add(RunCase(spec));
        }

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
        var runtimeMs = Math.Round(results.Sum(item => item.RuntimeMs), 3);
        var memoryBytes = results.Sum(item => item.MemoryAllocationBytes);

        return new BaselineResult(
            EvidenceKind,
            new DatasetSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                "Tier A protocol bridge for public/COCO-style object detection metrics; no external image pixels are stored.",
                specs.Count,
                results.Count - failed,
                failed,
                specs.Sum(item => item.GroundTruth.Count),
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
                runtimeMs,
                memoryBytes),
            [
                new OperatorSummary(
                    "DeepLearning",
                    specs.Count,
                    results.Count - failed,
                    failed,
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    (long)Math.Round(results.Average(item => item.MemoryAllocationBytes)),
                    true,
                    "dataset",
                    DatasetName)
            ],
            results
                .GroupBy(item => item.Scenario)
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

    private static CaseResult RunCase(DetectionCaseSpec spec)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            using var image = CreateImage(spec);
            var tensor = InvokePreprocessImage(image, InputSize);
            var tensorShape = string.Join(",", tensor.Dimensions.ToArray());
            if (tensorShape != "1,3,640,640")
            {
                throw new InvalidOperationException($"Unexpected preprocess tensor shape: {tensorShape}");
            }

            var outputTensor = CreateYoloV8Tensor();
            for (var i = 0; i < spec.Predictions.Count; i++)
            {
                WritePrediction(outputTensor, i, spec.Predictions[i], spec.Width, spec.Height);
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
                spec.Scenario,
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
                spec.Scenario,
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

    private static List<DetectionCaseSpec> BuildCases()
    {
        var cases = new List<DetectionCaseSpec>();
        var dimensions = new[] { (320, 240), (512, 384), (640, 480), (800, 600), (960, 540), (1280, 720) };
        for (var i = 0; i < dimensions.Length; i++)
        {
            var (width, height) = dimensions[i];
            var primary = Box.FromCenter(width * 0.32f, height * 0.40f, width * 0.16f, height * 0.18f, classId: 0, confidence: 0.94f);
            var secondary = Box.FromCenter(width * 0.66f, height * 0.55f, width * 0.14f, height * 0.20f, classId: 1, confidence: 0.91f);
            cases.Add(new DetectionCaseSpec(
                $"DeepLearning_single_object_{i:0000}",
                "single_object",
                width,
                height,
                [primary],
                [primary.WithJitter(1.0f, -1.0f, 0.98f)]));
            cases.Add(new DetectionCaseSpec(
                $"DeepLearning_multi_class_{i:0000}",
                "multi_class",
                width,
                height,
                [primary, secondary],
                [primary.WithJitter(0.5f, 0.5f, 0.96f), secondary.WithJitter(-0.5f, 1.0f, 0.92f)]));

            var edge = new Box(2, height * 0.18f, width * 0.18f, height * 0.18f, 2, 0.90f);
            cases.Add(new DetectionCaseSpec(
                $"DeepLearning_edge_clamp_{i:0000}",
                "edge_clamp",
                width,
                height,
                [edge],
                [edge.WithJitter(-3f, -1f, 0.93f)]));

            var overlap = Box.FromCenter(width * 0.50f, height * 0.48f, width * 0.22f, height * 0.24f, classId: 0, confidence: 0.95f);
            cases.Add(new DetectionCaseSpec(
                $"DeepLearning_same_class_nms_{i:0000}",
                "same_class_nms",
                width,
                height,
                [overlap],
                [overlap.WithJitter(0f, 0f, 0.95f), overlap.WithJitter(3f, 2f, 0.82f)]));

            var left = Box.FromCenter(width * 0.52f, height * 0.52f, width * 0.18f, height * 0.19f, classId: 0, confidence: 0.93f);
            var right = Box.FromCenter(width * 0.54f, height * 0.53f, width * 0.18f, height * 0.19f, classId: 1, confidence: 0.90f);
            cases.Add(new DetectionCaseSpec(
                $"DeepLearning_different_class_overlap_{i:0000}",
                "different_class_overlap",
                width,
                height,
                [left, right],
                [left.WithJitter(0.5f, 0f, 0.93f), right.WithJitter(-0.5f, 0f, 0.90f)]));

            var lowConfidenceDecoy = Box.FromCenter(width * 0.42f, height * 0.62f, width * 0.16f, height * 0.15f, classId: 2, confidence: 0.25f);
            cases.Add(new DetectionCaseSpec(
                $"DeepLearning_negative_low_confidence_{i:0000}",
                "negative_low_confidence",
                width,
                height,
                [],
                [lowConfidenceDecoy]));
        }

        return cases;
    }

    private static Mat CreateImage(DetectionCaseSpec spec)
    {
        var image = new Mat(spec.Height, spec.Width, MatType.CV_8UC3, new Scalar(36, 42, 52));
        for (var y = 0; y < spec.Height; y += Math.Max(16, spec.Height / 16))
        {
            Cv2.Line(image, new Point(0, y), new Point(spec.Width - 1, y), new Scalar(54, 72, 84), 1);
        }

        foreach (var box in spec.GroundTruth)
        {
            var color = box.ClassId switch
            {
                0 => new Scalar(40, 190, 230),
                1 => new Scalar(170, 220, 60),
                _ => new Scalar(210, 90, 190)
            };
            Cv2.Rectangle(image, ToRect(box, spec.Width, spec.Height), color, -1);
        }

        Cv2.GaussianBlur(image, image, new Size(3, 3), 0.2);
        return image;
    }

    private static Rect ToRect(Box box, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp((int)Math.Round(box.X), 0, imageWidth - 1);
        var y = Math.Clamp((int)Math.Round(box.Y), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Round(box.X + box.Width), x + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Round(box.Y + box.Height), y + 1, imageHeight);
        return new Rect(x, y, right - x, bottom - y);
    }

    private static DenseTensor<float> CreateYoloV8Tensor()
    {
        return new DenseTensor<float>(new float[1 * (4 + ClassCount) * AnchorCount], [1, 4 + ClassCount, AnchorCount]);
    }

    private static void WritePrediction(DenseTensor<float> tensor, int anchor, Box box, int originalWidth, int originalHeight)
    {
        var scale = Math.Min((float)InputSize / originalWidth, (float)InputSize / originalHeight);
        var xPad = (InputSize - originalWidth * scale) / 2f;
        var yPad = (InputSize - originalHeight * scale) / 2f;
        var centerX = (box.X + box.Width / 2f) * scale + xPad;
        var centerY = (box.Y + box.Height / 2f) * scale + yPad;
        tensor[0, 0, anchor] = centerX;
        tensor[0, 1, anchor] = centerY;
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
        return new CaseEvaluation(
            truePositives,
            falsePositives,
            falseNegatives,
            matchedIous.Count == 0 ? 0 : matchedIous.Max(),
            matchedIous,
            scored,
            failure);
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

internal sealed record DetectionCaseSpec(
    string CaseId,
    string Scenario,
    int Width,
    int Height,
    IReadOnlyList<Box> GroundTruth,
    IReadOnlyList<Box> Predictions);

internal sealed record Box(float X, float Y, float Width, float Height, int ClassId, float Confidence)
{
    public static Box FromCenter(float centerX, float centerY, float width, float height, int classId, float confidence)
    {
        return new Box(centerX - width / 2f, centerY - height / 2f, width, height, classId, confidence);
    }

    public Box WithJitter(float dx, float dy, float confidence)
    {
        return this with { X = X + dx, Y = Y + dy, Confidence = confidence };
    }
}

internal sealed record DetectionRecord(float X, float Y, float Width, float Height, float Confidence, int ClassId);

internal sealed record ScoredPrediction(double Confidence, bool IsTruePositive);

internal sealed record CaseEvaluation(
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double BestMatchedIou,
    IReadOnlyList<double> MatchedIous,
    IReadOnlyList<ScoredPrediction> ScoredPredictions,
    string? FailureReason);

internal sealed record BaselineResult(
    string EvidenceKind,
    DatasetSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

internal sealed record DatasetSummary(
    DateTimeOffset GeneratedAtUtc,
    string DatasetName,
    string DatasetKind,
    int CaseCount,
    int Passed,
    int Failed,
    int GroundTruthCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double PrecisionAt50,
    double RecallAt50,
    double AP50,
    double MeanMatchedIoU,
    double ConfidenceThreshold,
    double NmsIouThreshold,
    double MatchIouThreshold,
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
    string DatasetName);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    int GroundTruthCount,
    int DetectionCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    int Width,
    int Height,
    int GroundTruthCount,
    int DetectionCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double BestMatchedIou,
    double[] MatchedIous,
    IReadOnlyList<ScoredPrediction> ScoredPredictions,
    IReadOnlyList<DetectionRecord> Detections,
    string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning Detection Dataset Baseline",
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
            $"| Ground truth boxes | {result.Summary.GroundTruthCount} |",
            $"| True positives | {result.Summary.TruePositiveCount} |",
            $"| False positives | {result.Summary.FalsePositiveCount} |",
            $"| False negatives | {result.Summary.FalseNegativeCount} |",
            $"| Precision@0.50 | {result.Summary.PrecisionAt50:0.####} |",
            $"| Recall@0.50 | {result.Summary.RecallAt50:0.####} |",
            $"| AP50 | {result.Summary.AP50:0.####} |",
            $"| Mean matched IoU | {result.Summary.MeanMatchedIoU:0.####} |",
            $"| Confidence threshold | {result.Summary.ConfidenceThreshold:0.###} |",
            $"| NMS IoU threshold | {result.Summary.NmsIouThreshold:0.###} |",
            $"| Match IoU threshold | {result.Summary.MatchIouThreshold:0.###} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | GT | Detections | TP | FP | FN | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.GroundTruthCount} | {item.DetectionCount} | {item.TruePositiveCount} | {item.FalsePositiveCount} | {item.FalseNegativeCount} | {item.RuntimeMsAvg:0.###} |"));

        lines.AddRange(
        [
            "",
            "## Failure Boundaries",
            "",
            "- `edge_clamp` verifies detections near image borders remain matchable after coordinate clamp.",
            "- `same_class_nms` verifies duplicate same-class candidates are suppressed before dataset scoring.",
            "- `different_class_overlap` verifies overlapping boxes with different class ids are not suppressed across classes.",
            "- `negative_low_confidence` verifies below-threshold candidates do not become false positives.",
            "- This bridge records COCO-style detection metrics for DeepLearning post-processing; it is not a claim of production model accuracy.",
            "",
            "## Cases",
            "",
            "| Case | Scenario | Passed | Size | GT | Detections | TP | FP | FN | Best IoU | Runtime ms | Failure |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Scenario} | {item.Passed} | {item.Width}x{item.Height} | {item.GroundTruthCount} | {item.DetectionCount} | {item.TruePositiveCount} | {item.FalsePositiveCount} | {item.FalseNegativeCount} | {item.BestMatchedIou:0.####} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/DeepLearning_detection_dataset_baseline.json",
            "quality/evals/reports/DeepLearning_detection_dataset_baseline.md",
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
        Usage: dotnet run --project quality/tools/DeepLearningDetectionDatasetRunner/DeepLearningDetectionDatasetRunner.csproj -- [options]

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
