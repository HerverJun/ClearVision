using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
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

try
{
    var result = CocoRealModelRunner.Run(options);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
    File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

    if (!string.IsNullOrWhiteSpace(options.ReportPath))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
        File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
    }

    Console.WriteLine(
        $"DeepLearning COCO real-model inference complete: accepted={result.Accepted}, " +
        $"{result.Summary.ProcessedCaseCount}/{result.Summary.CaseCount} processed, " +
        $"AP50={result.Summary.AP50:F4}, recall={result.Summary.RecallAt50:F4}, output={options.OutputPath}");

    return result.Accepted ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(Sanitizer.Message(ex.GetBaseException().Message));
    return 2;
}

internal static class CocoRealModelRunner
{
    private const string EvidenceKind = "public-benchmark-real-model";
    private const string DatasetName = "COCO 2017 real validation images";
    private const string DatasetKind = "COCO real-image inference with ONNX Runtime model outputs; annotation-seeded tensors are not used.";
    private const float DefaultConfidenceThreshold = 0.25f;
    private const float DefaultNmsIouThreshold = 0.45f;
    private const float DefaultMatchIouThreshold = 0.50f;
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);

    public static RealModelResult Run(RunnerOptions options)
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var manifest = options.GenerateSmokeModel
            ? ModelManifest.CreateSmoke()
            : ModelManifest.Load(RepoPaths.ResolveRepoPath(repoRoot, options.ModelManifestPath));
        var modelPath = ResolveModelPath(repoRoot, options, manifest);
        var modelSha256 = ComputeSha256(modelPath);
        var expectedSha256 = options.ModelSha256Override
            ?? NormalizeSha256(manifest.ModelSha256);
        var modelSha256Matched = !string.IsNullOrWhiteSpace(expectedSha256)
            && string.Equals(modelSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);

        var inputSize = manifest.ResolveSquareInputSize(options.InputSize);
        var classNames = manifest.ResolveClasses(options.MaxClasses);
        if (classNames.Length == 0)
        {
            throw new InvalidOperationException("Model manifest must define classes for COCO scoring.");
        }

        var dataset = CocoDataset.Load(
            options.IndexPath,
            options.MaxCases,
            options.MaxBoxesPerImage,
            classNames,
            options.CaseIds);

        using var sessionOptions = new SessionOptions();
        var provider = ResolveProvider(options.ExecutionProvider);
        var sessionStopwatch = Stopwatch.StartNew();
        using var session = new InferenceSession(modelPath, sessionOptions);
        sessionStopwatch.Stop();

        var yoloVersion = ResolveYoloVersion(options.ModelVersion, manifest.Postprocess?.YoloVersion);
        var confidence = options.ConfidenceThreshold ?? manifest.Postprocess?.ConfidenceThreshold ?? DefaultConfidenceThreshold;
        var nmsIou = options.NmsIouThreshold ?? manifest.Postprocess?.NmsIouThreshold ?? DefaultNmsIouThreshold;
        var matchIou = options.MatchIouThreshold ?? DefaultMatchIouThreshold;

        var results = dataset.Cases
            .Select(spec => RunCase(
                session,
                spec,
                classNames.Length,
                inputSize,
                yoloVersion,
                confidence,
                nmsIou,
                matchIou))
            .ToList();

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
        var processingFailed = results.Count(item => item.ProcessingError);
        var matchedCases = results.Count(item => item.Passed);
        var failedCases = results.Count - matchedCases;
        var inferenceSmokeOnly = options.GenerateSmokeModel ||
                                 manifest.Source.Equals("generated-smoke-fixture", StringComparison.OrdinalIgnoreCase);
        var thresholdsApprovedForPrecision = options.MinPrecisionAt50 > 0 &&
                                             options.MinRecallAt50 > 0 &&
                                             options.MinAP50 > 0;
        var nonZeroPrecisionEvidence = precision > 0 || recall > 0 || ap50 > 0;
        var accepted = !inferenceSmokeOnly
            && modelSha256Matched
            && thresholdsApprovedForPrecision
            && nonZeroPrecisionEvidence
            && processingFailed == 0
            && precision >= options.MinPrecisionAt50
            && recall >= options.MinRecallAt50
            && ap50 >= options.MinAP50;

        var summary = new RealModelSummary(
            DateTimeOffset.UtcNow,
            DatasetName,
            DatasetKind,
            RepoPaths.Sanitize(repoRoot, options.IndexPath),
            dataset.AnnotationPath,
            dataset.Cases.Count,
            results.Count - processingFailed,
            processingFailed,
            matchedCases,
            failedCases,
            classNames.Length,
            totalGroundTruth,
            totalTruePositives,
            totalFalsePositives,
            totalFalseNegatives,
            Math.Round(precision, 6),
            Math.Round(recall, 6),
            Math.Round(ap50, 6),
            options.MinPrecisionAt50,
            options.MinRecallAt50,
            options.MinAP50,
            Math.Round(matchedIous.Length == 0 ? 0 : matchedIous.Average(), 6),
            Math.Round(confidence, 6),
            Math.Round(nmsIou, 6),
            Math.Round(matchIou, 6),
            Math.Round(results.Sum(item => item.RuntimeMs), 3),
            results.Sum(item => item.MemoryAllocationBytes),
            Math.Round(sessionStopwatch.Elapsed.TotalMilliseconds, 3),
            provider,
            true,
            false,
            manifest.ModelId,
            modelSha256,
            expectedSha256,
            modelSha256Matched,
            RepoPaths.SanitizeModelArtifact(repoRoot, modelPath),
            RepoPaths.Sanitize(repoRoot, options.ModelManifestPath),
            options.CandidateVersion,
            options.Profile);

        return new RealModelResult(
            "2026-04-30.deep-learning-coco-real-model.v1",
            EvidenceKind,
            inferenceSmokeOnly ? "InferenceSmokeOnly" : "DeliveryPrecisionCandidate",
            accepted,
            EvidenceIdentity.Capture(
                repoRoot,
                modelSha256,
                ComputeSha256(RepoPaths.ResolveRepoPath(repoRoot, options.IndexPath)),
                provider,
                string.Empty),
            summary,
            manifest.ToProvenancePayload(expectedSha256, modelSha256, modelSha256Matched),
            [
                new OperatorSummary(
                    "DeepLearning",
                    dataset.Cases.Count,
                    matchedCases,
                    failedCases,
                    Math.Round(results.Count == 0 ? 0 : results.Average(item => item.RuntimeMs), 3),
                    (long)Math.Round(results.Count == 0 ? 0 : results.Average(item => item.MemoryAllocationBytes)),
                    true,
                    EvidenceKind,
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
            results,
            new ClaimBoundary(
                "This report uses real ONNX Runtime model outputs. Annotation-seeded tensors are not used.",
                "COCO public benchmark evidence is not real production-site validation or sign-off.",
                "AP50/precision/recall are frozen from the supplied model artifact and must not be compared to annotation-seeded proof as model accuracy."));
    }

    private static CaseResult RunCase(
        InferenceSession session,
        CocoCaseSpec spec,
        int knownLabelCount,
        int inputSize,
        YoloVersion yoloVersion,
        float confidenceThreshold,
        float nmsIouThreshold,
        float matchIouThreshold)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            using var image = Cv2.ImRead(spec.ImagePath, ImreadModes.Color);
            if (image.Empty())
            {
                throw new FileNotFoundException("Unable to read COCO image.");
            }

            var inputTensor = InvokePreprocessImage(image, inputSize);
            var inference = RunInference(session, inputTensor, knownLabelCount);
            var effectiveVersion = yoloVersion == YoloVersion.Auto
                ? InvokeDetectYoloVersion(inference.Tensor, knownLabelCount)
                : yoloVersion;
            var detections = InvokePostprocessResults(
                inference.Tensor,
                confidenceThreshold,
                spec.Width,
                spec.Height,
                inputSize,
                effectiveVersion,
                enableNms: true,
                nmsIouThreshold);

            var evaluation = Evaluate(spec.GroundTruth, detections, matchIouThreshold);
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            var passed = evaluation.FalsePositiveCount == 0 && evaluation.FalseNegativeCount == 0;

            return new CaseResult(
                spec.CaseId,
                spec.CategorySet,
                passed,
                false,
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
                inference.OutputName,
                inference.OutputShape,
                inference.SelectionRule,
                effectiveVersion.ToString(),
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
                true,
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
                string.Empty,
                [],
                string.Empty,
                yoloVersion.ToString(),
                Sanitizer.Message(ex.GetBaseException().Message));
        }
    }

    private static InferenceOutput RunInference(InferenceSession session, DenseTensor<float> inputTensor, int knownLabelCount)
    {
        var inputName = session.InputMetadata.Keys.First();
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, inputTensor)]);
        var outputNames = new List<string>();
        var outputShapes = new List<int[]>();
        var outputTensors = new List<Tensor<float>>();

        foreach (var output in results)
        {
            try
            {
                var tensor = output.AsTensor<float>();
                outputNames.Add(output.Name);
                outputShapes.Add(tensor.Dimensions.ToArray());
                outputTensors.Add(tensor);
            }
            catch
            {
                // Non-float tensors cannot be DeepLearning detection outputs.
            }
        }

        if (outputTensors.Count == 0)
        {
            throw new InvalidOperationException("No float output tensor was produced by the model.");
        }

        var (selectedIndex, selectionRule) = SelectDetectionOutputIndex(outputNames, outputShapes, knownLabelCount);
        var selectedShape = outputShapes[selectedIndex];
        var selectedTensor = CopyTensorToDense(outputTensors[selectedIndex], selectedShape);
        return new InferenceOutput(selectedTensor, outputNames[selectedIndex], selectedShape, selectionRule);
    }

    private static DenseTensor<float> CopyTensorToDense(Tensor<float> source, int[] shape)
    {
        var values = new float[source.Length];
        var index = 0;
        foreach (var value in source)
        {
            values[index++] = value;
        }

        return new DenseTensor<float>(values, shape);
    }

    private static (int SelectedIndex, string SelectionRule) SelectDetectionOutputIndex(
        IReadOnlyList<string> outputNames,
        IReadOnlyList<int[]> outputShapes,
        int knownLabelCount)
    {
        if (knownLabelCount > 0)
        {
            var bestIndex = -1;
            var bestAnchor = -1;
            var bestRule = string.Empty;

            for (var i = 0; i < outputShapes.Count; i++)
            {
                if (!TryMatchKnownLabelShape(outputShapes[i], knownLabelCount, out var anchorDim, out var rule))
                {
                    continue;
                }

                if (anchorDim > bestAnchor)
                {
                    bestAnchor = anchorDim;
                    bestIndex = i;
                    bestRule = rule;
                }
            }

            if (bestIndex >= 0)
            {
                return (bestIndex, bestRule);
            }
        }

        var heuristicIndex = -1;
        var heuristicScore = int.MinValue;
        for (var i = 0; i < outputShapes.Count; i++)
        {
            if (!TryGetRank3DetectionScore(outputShapes[i], out var score))
            {
                continue;
            }

            if (score > heuristicScore)
            {
                heuristicScore = score;
                heuristicIndex = i;
            }
        }

        if (heuristicIndex >= 0)
        {
            return (heuristicIndex, knownLabelCount > 0 ? "Rank3HeuristicAfterKnownLabelMiss" : "Rank3Heuristic");
        }

        throw new InvalidOperationException("Could not identify a rank-3 detection output tensor.");
    }

    private static bool TryMatchKnownLabelShape(int[] shape, int knownLabelCount, out int anchorDim, out string rule)
    {
        anchorDim = 0;
        rule = string.Empty;
        if (shape.Length != 3)
        {
            return false;
        }

        if (TryMatchFeatureDimension(shape[1], shape[2], knownLabelCount, out rule))
        {
            anchorDim = shape[2];
            return true;
        }

        if (TryMatchFeatureDimension(shape[2], shape[1], knownLabelCount, out rule))
        {
            anchorDim = shape[1];
            return true;
        }

        return false;
    }

    private static bool TryMatchFeatureDimension(int featureDim, int anchorDim, int knownLabelCount, out string rule)
    {
        rule = string.Empty;
        if (anchorDim <= featureDim)
        {
            return false;
        }

        if (featureDim == knownLabelCount + 4)
        {
            rule = "KnownLabelFeature+4";
            return true;
        }

        if (featureDim == knownLabelCount + 5)
        {
            rule = "KnownLabelFeature+5";
            return true;
        }

        return false;
    }

    private static bool TryGetRank3DetectionScore(int[] shape, out int score)
    {
        score = int.MinValue;
        if (shape.Length != 3)
        {
            return false;
        }

        var dimA = shape[1];
        var dimB = shape[2];
        var anchorDim = Math.Max(dimA, dimB);
        var featureDim = Math.Min(dimA, dimB);
        if (anchorDim < 16 || featureDim < 4 || featureDim > 512)
        {
            return false;
        }

        score = (anchorDim * 1024) - featureDim;
        return true;
    }

    private static DenseTensor<float> InvokePreprocessImage(Mat image, int inputSize)
    {
        return (DenseTensor<float>)InvokeInstance("PreprocessImage", image, inputSize)!;
    }

    private static YoloVersion InvokeDetectYoloVersion(DenseTensor<float> tensor, int knownLabelCount)
    {
        return (YoloVersion)InvokeInstance("DetectYoloVersion", tensor, knownLabelCount)!;
    }

    private static List<DetectionRecord> InvokePostprocessResults(
        DenseTensor<float> tensor,
        float threshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        YoloVersion yoloVersion,
        bool enableNms,
        float nmsIou)
    {
        return ToDetectionRecords(InvokeInstanceEnumerable(
            "PostprocessResults",
            tensor,
            threshold,
            originalWidth,
            originalHeight,
            inputSize,
            yoloVersion,
            null,
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

    private static CaseEvaluation Evaluate(
        IReadOnlyList<Box> groundTruth,
        IReadOnlyList<DetectionRecord> detections,
        float matchIouThreshold)
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

            var isTruePositive = bestIndex >= 0 && bestIou >= matchIouThreshold;
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

    private static string ResolveModelPath(string repoRoot, RunnerOptions options, ModelManifest manifest)
    {
        if (options.GenerateSmokeModel)
        {
            var path = Path.Combine(repoRoot, ".tmp", "publish-check", "deep-learning-real-model-smoke.onnx");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, SmokeYoloOnnxModel.Create(manifest.Classes.Length));
            return path;
        }

        var configured = options.ModelPath ?? manifest.ArtifactPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("Model path is required. Use --model or artifactPath in the model manifest.");
        }

        var resolved = RepoPaths.ResolveRepoPath(repoRoot, configured);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException("Model artifact not found. Model files are intentionally not committed; provide --model or install the artifact referenced by the manifest.");
        }

        return resolved;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["sha256:".Length..];
        }

        return trimmed.Contains('<', StringComparison.Ordinal) || trimmed.Contains("required", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : trimmed;
    }

    private static string ResolveProvider(string provider)
    {
        if (!provider.Equals("CPUExecutionProvider", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("cpu", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only CPUExecutionProvider is supported by the automated real-model runner. GPU/TensorRT remain manual release evidence.");
        }

        return "CPUExecutionProvider";
    }

    private static YoloVersion ResolveYoloVersion(string? optionValue, string? manifestValue)
    {
        var value = string.IsNullOrWhiteSpace(optionValue) || optionValue.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            ? manifestValue
            : optionValue;
        return value?.ToLowerInvariant() switch
        {
            "v5" or "yolov5" or "5" => YoloVersion.YOLOv5,
            "v6" or "yolov6" or "6" => YoloVersion.YOLOv6,
            "v8" or "yolov8" or "8" => YoloVersion.YOLOv8,
            "v11" or "yolo11" or "yolov11" or "11" => YoloVersion.YOLOv11,
            _ => YoloVersion.Auto
        };
    }
}

internal static class CocoDataset
{
    public static CocoDatasetSpec Load(
        string indexPath,
        int maxCases,
        int maxBoxesPerImage,
        IReadOnlyList<string> classNames,
        IReadOnlySet<string> requestedCaseIds)
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var fullIndexPath = RepoPaths.ResolveRepoPath(repoRoot, indexPath);
        using var indexDocument = JsonDocument.Parse(File.ReadAllText(fullIndexPath));
        var indexRoot = indexDocument.RootElement;
        var annotationPath = RepoPaths.ResolveRepoPath(repoRoot, indexRoot.GetProperty("annotation_file").GetString() ?? "");
        var annotations = CocoAnnotationStore.Load(annotationPath, classNames);
        var cases = new List<CocoCaseSpec>();

        foreach (var record in indexRoot.GetProperty("records").EnumerateArray())
        {
            if (cases.Count >= maxCases)
            {
                break;
            }

            var imageIdText = record.GetProperty("id").GetString() ?? "";
            var caseId = $"coco2017_val_{imageIdText}";
            if (requestedCaseIds.Count > 0
                && !requestedCaseIds.Contains(caseId)
                && !requestedCaseIds.Contains(imageIdText))
            {
                continue;
            }

            if (!int.TryParse(imageIdText, out var imageId) || !annotations.ByImage.TryGetValue(imageId, out var imageAnnotations))
            {
                continue;
            }

            var boxes = imageAnnotations
                .Take(maxBoxesPerImage)
                .Select(item => new Box(item.X, item.Y, item.Width, item.Height, item.ClassId, 1f))
                .ToList();
            if (boxes.Count == 0)
            {
                continue;
            }

            var imagePath = RepoPaths.ResolveRepoPath(repoRoot, record.GetProperty("image_path").GetString() ?? "");
            if (!File.Exists(imagePath))
            {
                continue;
            }

            var categorySet = string.Join(
                "+",
                boxes
                    .Select(item => item.ClassId)
                    .Distinct()
                    .OrderBy(item => item)
                    .Take(4)
                    .Select(item => classNames[item].Replace(' ', '_')));

            cases.Add(new CocoCaseSpec(
                caseId,
                categorySet,
                imagePath,
                record.GetProperty("width").GetInt32(),
                record.GetProperty("height").GetInt32(),
                boxes));
        }

        if (cases.Count == 0)
        {
            throw new InvalidOperationException("No COCO cases matched the requested model classes/case ids.");
        }

        return new CocoDatasetSpec(RepoPaths.Sanitize(repoRoot, annotationPath), annotations.CategoryCount, cases);
    }
}

internal sealed class CocoAnnotationStore
{
    private CocoAnnotationStore(Dictionary<int, List<CocoAnnotation>> byImage, int categoryCount)
    {
        ByImage = byImage;
        CategoryCount = categoryCount;
    }

    public Dictionary<int, List<CocoAnnotation>> ByImage { get; }

    public int CategoryCount { get; }

    public static CocoAnnotationStore Load(string annotationPath, IReadOnlyList<string> classNames)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(annotationPath));
        var categoryNameById = document.RootElement
            .GetProperty("categories")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("id").GetInt32(),
                item => item.GetProperty("name").GetString() ?? string.Empty);
        var classByName = classNames
            .Select((name, index) => new { Name = NormalizeClassName(name), Index = index })
            .Where(item => item.Name.Length > 0)
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<int, List<CocoAnnotation>>();

        foreach (var annotation in document.RootElement.GetProperty("annotations").EnumerateArray())
        {
            if (annotation.TryGetProperty("iscrowd", out var isCrowd) && isCrowd.GetInt32() != 0)
            {
                continue;
            }

            var categoryId = annotation.GetProperty("category_id").GetInt32();
            if (!categoryNameById.TryGetValue(categoryId, out var categoryName)
                || !classByName.TryGetValue(NormalizeClassName(categoryName), out var classId))
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
            if (!result.TryGetValue(imageId, out var list))
            {
                list = [];
                result[imageId] = list;
            }

            list.Add(new CocoAnnotation(classId, x, y, width, height));
        }

        foreach (var key in result.Keys.ToArray())
        {
            result[key] = result[key]
                .OrderByDescending(item => item.Width * item.Height)
                .ToList();
        }

        return new CocoAnnotationStore(result, categoryNameById.Count);
    }

    private static string NormalizeClassName(string value)
    {
        return value.Trim().Replace('_', ' ').ToLowerInvariant();
    }
}

internal sealed record ModelManifest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("modelId")] string ModelId,
    [property: JsonPropertyName("modelSha256")] string ModelSha256,
    [property: JsonPropertyName("artifactPath")] string ArtifactPath,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("classes")] string[] Classes,
    [property: JsonPropertyName("inputShape")] int[] InputShape,
    [property: JsonPropertyName("preprocess")] PreprocessSpec? Preprocess,
    [property: JsonPropertyName("postprocess")] PostprocessSpec? Postprocess)
{
    public static ModelManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<ModelManifest>(json, JsonSettings.CaseInsensitive)
            ?? throw new InvalidOperationException("Model manifest is empty.");
        if (string.IsNullOrWhiteSpace(manifest.ModelId))
        {
            throw new InvalidOperationException("Model manifest must define modelId.");
        }

        return manifest;
    }

    public static ModelManifest CreateSmoke()
    {
        return new ModelManifest(
            "2026-04-30.deep-learning-model-manifest.v1",
            "clearvision_yolov8_constant_smoke",
            string.Empty,
            ".tmp/publish-check/deep-learning-real-model-smoke.onnx",
            "generated-smoke-fixture",
            "internal-test-fixture",
            ["person", "bicycle", "car"],
            [1, 3, 640, 640],
            new PreprocessSpec("letterbox", "RGB", "float32_0_1", 114),
            new PostprocessSpec("yolov8", 0.25f, 0.45f, true));
    }

    public int ResolveSquareInputSize(int? overrideSize)
    {
        if (overrideSize is > 0)
        {
            return overrideSize.Value;
        }

        if (InputShape.Length >= 4 && InputShape[^1] == InputShape[^2] && InputShape[^1] > 0)
        {
            return InputShape[^1];
        }

        return 640;
    }

    public string[] ResolveClasses(int maxClasses)
    {
        if (maxClasses <= 0 || maxClasses >= Classes.Length)
        {
            return Classes;
        }

        return Classes.Take(maxClasses).ToArray();
    }

    public Dictionary<string, object> ToProvenancePayload(string expectedSha256, string actualSha256, bool matched)
    {
        return new Dictionary<string, object>
        {
            ["ModelId"] = ModelId,
            ["ExpectedModelSha256"] = expectedSha256,
            ["ActualModelSha256"] = actualSha256,
            ["ModelSha256Matched"] = matched,
            ["Source"] = Source,
            ["License"] = License,
            ["ClassCount"] = Classes.Length,
            ["InputShape"] = InputShape,
            ["Preprocess"] = Preprocess?.ToPayload() ?? new Dictionary<string, object>(),
            ["Postprocess"] = Postprocess?.ToPayload() ?? new Dictionary<string, object>()
        };
    }
}

internal sealed record PreprocessSpec(
    [property: JsonPropertyName("resize")] string Resize,
    [property: JsonPropertyName("colorOrder")] string ColorOrder,
    [property: JsonPropertyName("normalization")] string Normalization,
    [property: JsonPropertyName("padValue")] int PadValue)
{
    public Dictionary<string, object> ToPayload() => new()
    {
        ["Resize"] = Resize,
        ["ColorOrder"] = ColorOrder,
        ["Normalization"] = Normalization,
        ["PadValue"] = PadValue
    };
}

internal sealed record PostprocessSpec(
    [property: JsonPropertyName("yoloVersion")] string YoloVersion,
    [property: JsonPropertyName("confidenceThreshold")] float ConfidenceThreshold,
    [property: JsonPropertyName("nmsIouThreshold")] float NmsIouThreshold,
    [property: JsonPropertyName("classAwareNms")] bool ClassAwareNms)
{
    public Dictionary<string, object> ToPayload() => new()
    {
        ["YoloVersion"] = YoloVersion,
        ["ConfidenceThreshold"] = ConfidenceThreshold,
        ["NmsIouThreshold"] = NmsIouThreshold,
        ["ClassAwareNms"] = ClassAwareNms
    };
}

internal static class SmokeYoloOnnxModel
{
    public static byte[] Create(int classCount)
    {
        var anchorCount = 32;
        var featureCount = 4 + classCount;
        var values = new float[1 * featureCount * anchorCount];
        values[0 * anchorCount + 0] = 320f;
        values[1 * anchorCount + 0] = 320f;
        values[2 * anchorCount + 0] = 180f;
        values[3 * anchorCount + 0] = 160f;
        values[4 * anchorCount + 0] = 0.90f;

        var input = ValueInfo("images", [1, 3, 640, 640]);
        var output = ValueInfo("output0", [1, featureCount, anchorCount]);
        var tensor = Tensor("constant_yolo_output", [1, featureCount, anchorCount], values);
        var attribute = Message(L(1, String("value")), L(5, tensor), V(20, 4));
        var node = Message(L(2, String("output0")), L(4, String("Constant")), L(5, attribute));
        var graph = Message(L(1, node), L(2, String("clearvision_yolov8_constant_smoke")), L(11, input), L(12, output));
        var opset = Message(L(1, String("")), V(2, 13));
        return Message(V(1, 7), L(2, String("ClearVision")), L(7, graph), L(8, opset));
    }

    private static byte[] ValueInfo(string name, IReadOnlyList<long> dimensions)
    {
        var shape = Message(dimensions.Select(dim => L(1, Message(V64(1, (ulong)dim)))).ToArray());
        var tensorType = Message(V(1, 1), L(2, shape));
        var type = Message(L(1, tensorType));
        return Message(L(1, String(name)), L(2, type));
    }

    private static byte[] Tensor(string name, IReadOnlyList<long> dimensions, IReadOnlyList<float> values)
    {
        var fields = new List<Field>();
        fields.AddRange(dimensions.Select(dim => V64(1, (ulong)dim)));
        fields.Add(V(2, 1));
        fields.Add(L(8, String(name)));
        fields.Add(L(9, RawFloats(values)));
        return Message(fields.ToArray());
    }

    private static byte[] RawFloats(IReadOnlyList<float> values)
    {
        var bytes = new byte[values.Count * sizeof(float)];
        for (var i = 0; i < values.Count; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(bytes, i * sizeof(float));
        }

        return bytes;
    }

    private static Field L(int number, byte[] value) => new(number, 2, value);
    private static Field V(int number, uint value) => new(number, 0, Varint(value));
    private static Field V64(int number, ulong value) => new(number, 0, Varint(value));

    private static byte[] Message(params Field[] fields)
    {
        using var stream = new MemoryStream();
        foreach (var field in fields)
        {
            WriteVarint(stream, (ulong)((field.Number << 3) | field.WireType));
            if (field.WireType == 2)
            {
                WriteVarint(stream, (ulong)field.Value.Length);
            }

            stream.Write(field.Value);
        }

        return stream.ToArray();
    }

    private static byte[] String(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] Varint(ulong value)
    {
        using var stream = new MemoryStream();
        WriteVarint(stream, value);
        return stream.ToArray();
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private readonly record struct Field(int Number, int WireType, byte[] Value);
}

internal static class RepoPaths
{
    public static string FindRepoRoot()
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

    public static string ResolveRepoPath(string repoRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Path value must not be empty.");
        }

        return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(repoRoot, value));
    }

    public static string Sanitize(string repoRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var full = Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(repoRoot, value));
        if (full.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(repoRoot, full).Replace('\\', '/');
        }

        return value.Contains(':', StringComparison.Ordinal) || value.StartsWith(@"\\", StringComparison.Ordinal)
            ? "external-artifact"
            : value.Replace('\\', '/');
    }

    public static string SanitizeModelArtifact(string repoRoot, string modelPath)
    {
        var full = Path.GetFullPath(modelPath);
        if (full.Contains($"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}publish-check{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return "generated-smoke-fixture";
        }

        if (full.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(repoRoot, full).Replace('\\', '/');
        }

        return "external-model-artifact";
    }
}

internal static class Sanitizer
{
    public static string Message(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var repoRoot = RepoPaths.FindRepoRoot();
        var sanitized = message.Replace(repoRoot, "<repo>", StringComparison.OrdinalIgnoreCase);
        return sanitized
            .Replace('\\', '/')
            .Replace("C:/", "<drive>/", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record CocoDatasetSpec(string AnnotationPath, int CategoryCount, List<CocoCaseSpec> Cases);
internal sealed record CocoAnnotation(int ClassId, float X, float Y, float Width, float Height);
internal sealed record CocoCaseSpec(string CaseId, string CategorySet, string ImagePath, int Width, int Height, IReadOnlyList<Box> GroundTruth);
internal sealed record Box(float X, float Y, float Width, float Height, int ClassId, float Confidence);
internal sealed record DetectionRecord(float X, float Y, float Width, float Height, float Confidence, int ClassId);
internal sealed record ScoredPrediction(double Confidence, bool IsTruePositive);
internal sealed record CaseEvaluation(int TruePositiveCount, int FalsePositiveCount, int FalseNegativeCount, double BestMatchedIou, IReadOnlyList<double> MatchedIous, IReadOnlyList<ScoredPrediction> ScoredPredictions, string? FailureReason);
internal sealed record InferenceOutput(DenseTensor<float> Tensor, string OutputName, int[] OutputShape, string SelectionRule);

internal sealed record RealModelResult(
    string SchemaVersion,
    string EvidenceKind,
    string EvidencePurpose,
    bool Accepted,
    EvidenceIdentity EvidenceIdentity,
    RealModelSummary Summary,
    Dictionary<string, object> ModelProvenance,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases,
    ClaimBoundary ClaimBoundary);

internal sealed record RealModelSummary(
    DateTimeOffset GeneratedAtUtc,
    string DatasetName,
    string DatasetKind,
    string IndexPath,
    string AnnotationPath,
    int CaseCount,
    int ProcessedCaseCount,
    int ProcessingFailedCaseCount,
    int Passed,
    int Failed,
    int CategoryCount,
    int GroundTruthCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double PrecisionAt50,
    double RecallAt50,
    double AP50,
    double MinPrecisionAt50,
    double MinRecallAt50,
    double MinAP50,
    double MeanMatchedIoU,
    double ConfidenceThreshold,
    double NmsIouThreshold,
    double MatchIouThreshold,
    double RuntimeMs,
    long MemoryAllocationBytes,
    double SessionCreateMs,
    string InferenceProvider,
    bool RealOnnxInference,
    bool AnnotationSeeded,
    string ModelId,
    string ModelSha256,
    string ExpectedModelSha256,
    bool ModelSha256Matched,
    string ModelArtifactRef,
    string ModelManifestPath,
    string CandidateVersion,
    string Profile);

internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double RuntimeMsAvg, long MemoryAllocationBytesAvg, bool HasPublicDataset, string EvidenceKind, string DatasetName);
internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, int GroundTruthCount, int DetectionCount, int TruePositiveCount, int FalsePositiveCount, int FalseNegativeCount, double RuntimeMsAvg);
internal sealed record CaseResult(
    string CaseId,
    string CategorySet,
    bool Passed,
    bool ProcessingError,
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
    string OutputTensorName,
    int[] OutputTensorShape,
    string OutputSelectionRule,
    string YoloVersion,
    string? Failure);
internal sealed record ClaimBoundary(string RealModelRule, string FieldSignoffRule, string AccuracyComparisonRule);

internal sealed record EvidenceIdentity(
    string GitSha,
    bool RepositoryDirty,
    string ToolVersion,
    string Environment,
    string ModelContentSha256,
    string DatasetChecksumSha256,
    string ActualProvider,
    string FallbackReason)
{
    public static EvidenceIdentity Capture(
        string repoRoot,
        string modelContentSha256,
        string datasetChecksumSha256,
        string actualProvider,
        string fallbackReason)
    {
        return new EvidenceIdentity(
            RunGit(repoRoot, "rev-parse HEAD"),
            !string.IsNullOrWhiteSpace(RunGit(repoRoot, "status --porcelain")),
            "DeepLearningCocoRealModelRunner/2026-09-01.wave3b",
            $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; .NET {System.Environment.Version}",
            modelContentSha256,
            datasetChecksumSha256,
            actualProvider,
            fallbackReason);
    }

    private static string RunGit(string repoRoot, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

internal static class MarkdownReport
{
    public static string Create(RealModelResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning COCO Real Model Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"EvidencePurpose: `{result.EvidencePurpose}`",
            $"Accepted: `{result.Accepted}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Processed cases | {result.Summary.ProcessedCaseCount} |",
            $"| Processing failed cases | {result.Summary.ProcessingFailedCaseCount} |",
            $"| Matched cases | {result.Summary.Passed} |",
            $"| Unmatched cases | {result.Summary.Failed} |",
            $"| Ground truth boxes | {result.Summary.GroundTruthCount} |",
            $"| True positives | {result.Summary.TruePositiveCount} |",
            $"| False positives | {result.Summary.FalsePositiveCount} |",
            $"| False negatives | {result.Summary.FalseNegativeCount} |",
            $"| Precision@0.50 | {result.Summary.PrecisionAt50:0.####} |",
            $"| Recall@0.50 | {result.Summary.RecallAt50:0.####} |",
            $"| AP50 | {result.Summary.AP50:0.####} |",
            $"| Min Precision@0.50 | {result.Summary.MinPrecisionAt50:0.####} |",
            $"| Min Recall@0.50 | {result.Summary.MinRecallAt50:0.####} |",
            $"| Min AP50 | {result.Summary.MinAP50:0.####} |",
            $"| Mean matched IoU | {result.Summary.MeanMatchedIoU:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            $"| Session create ms | {result.Summary.SessionCreateMs:0.###} |",
            $"| Real ONNX inference | {result.Summary.RealOnnxInference} |",
            $"| Annotation seeded | {result.Summary.AnnotationSeeded} |",
            "",
            "## Model",
            "",
            "| Field | Value |",
            "| --- | --- |",
            $"| ModelId | `{result.Summary.ModelId}` |",
            $"| Model artifact | `{result.Summary.ModelArtifactRef}` |",
            $"| Model SHA256 | `{result.Summary.ModelSha256}` |",
            $"| Expected SHA256 | `{result.Summary.ExpectedModelSha256}` |",
            $"| SHA256 matched | `{result.Summary.ModelSha256Matched}` |",
            $"| Provider | `{result.Summary.InferenceProvider}` |",
            $"| CandidateVersion | `{result.Summary.CandidateVersion}` |",
            $"| Profile | `{result.Summary.Profile}` |",
            $"| Git SHA / dirty | `{result.EvidenceIdentity.GitSha}` / `{result.EvidenceIdentity.RepositoryDirty}` |",
            $"| Dataset checksum | `{result.EvidenceIdentity.DatasetChecksumSha256}` |",
            $"| Tool / environment | `{result.EvidenceIdentity.ToolVersion}` / `{result.EvidenceIdentity.Environment}` |",
            "",
            "## Claim Boundary",
            "",
            $"- {result.ClaimBoundary.RealModelRule}",
            $"- {result.ClaimBoundary.FieldSignoffRule}",
            $"- {result.ClaimBoundary.AccuracyComparisonRule}",
            "",
            "## Cases",
            "",
            "| Case | Categories | Passed | ProcessingError | Size | GT | Detections | TP | FP | FN | Best IoU | Runtime ms | Output shape | Failure |",
            "| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        };

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.CategorySet} | {item.Passed} | {item.ProcessingError} | {item.Width}x{item.Height} | {item.GroundTruthCount} | {item.DetectionCount} | {item.TruePositiveCount} | {item.FalsePositiveCount} | {item.FalseNegativeCount} | {item.BestMatchedIou:0.####} | {item.RuntimeMs:0.###} | {string.Join("x", item.OutputTensorShape)} | {item.Failure ?? "-"} |"));

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string IndexPath,
    string OutputPath,
    string ReportPath,
    string ModelManifestPath,
    string? ModelPath,
    string? ModelSha256Override,
    string ExecutionProvider,
    int MaxCases,
    int MaxBoxesPerImage,
    int MaxClasses,
    int? InputSize,
    float? ConfidenceThreshold,
    float? NmsIouThreshold,
    float? MatchIouThreshold,
    double MinPrecisionAt50,
    double MinRecallAt50,
    double MinAP50,
    string ModelVersion,
    string CandidateVersion,
    string Profile,
    IReadOnlySet<string> CaseIds,
    bool GenerateSmokeModel,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/datasets/coco2017_index.json",
            "quality/evals/reports/DeepLearning_coco_real_model_baseline.json",
            "quality/evals/reports/DeepLearning_coco_real_model_baseline.md",
            "models/object_detection/coco_yolo_real_model_manifest.template.json",
            null,
            null,
            "CPUExecutionProvider",
            120,
            20,
            80,
            null,
            null,
            null,
            null,
            0.45,
            0.35,
            0.45,
            "Auto",
            "baseline",
            "hard_nms_045",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            false,
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (arg == "--generate-smoke-model")
            {
                options = options with { GenerateSmokeModel = true, ModelManifestPath = "generated-smoke-fixture" };
                continue;
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
                "--model-manifest" => options with { ModelManifestPath = value },
                "--model" => options with { ModelPath = value },
                "--model-sha256" => options with { ModelSha256Override = value },
                "--execution-provider" => options with { ExecutionProvider = value },
                "--max-cases" => int.TryParse(value, out var maxCases) && maxCases > 0
                    ? options with { MaxCases = maxCases }
                    : options with { ParseError = "--max-cases must be a positive integer." },
                "--max-boxes-per-image" => int.TryParse(value, out var maxBoxes) && maxBoxes > 0
                    ? options with { MaxBoxesPerImage = maxBoxes }
                    : options with { ParseError = "--max-boxes-per-image must be a positive integer." },
                "--max-classes" => int.TryParse(value, out var maxClasses) && maxClasses is > 0 and <= 512
                    ? options with { MaxClasses = maxClasses }
                    : options with { ParseError = "--max-classes must be between 1 and 512." },
                "--input-size" => int.TryParse(value, out var inputSize) && inputSize > 0
                    ? options with { InputSize = inputSize }
                    : options with { ParseError = "--input-size must be a positive integer." },
                "--confidence" => float.TryParse(value, out var confidence) && confidence is >= 0 and <= 1
                    ? options with { ConfidenceThreshold = confidence }
                    : options with { ParseError = "--confidence must be between 0 and 1." },
                "--nms-iou" => float.TryParse(value, out var nmsIou) && nmsIou is >= 0 and <= 1
                    ? options with { NmsIouThreshold = nmsIou }
                    : options with { ParseError = "--nms-iou must be between 0 and 1." },
                "--match-iou" => float.TryParse(value, out var matchIou) && matchIou is >= 0 and <= 1
                    ? options with { MatchIouThreshold = matchIou }
                    : options with { ParseError = "--match-iou must be between 0 and 1." },
                "--min-precision-at-50" => double.TryParse(value, out var minPrecisionAt50) && minPrecisionAt50 is >= 0 and <= 1
                    ? options with { MinPrecisionAt50 = minPrecisionAt50 }
                    : options with { ParseError = "--min-precision-at-50 must be between 0 and 1." },
                "--min-recall-at-50" => double.TryParse(value, out var minRecallAt50) && minRecallAt50 is >= 0 and <= 1
                    ? options with { MinRecallAt50 = minRecallAt50 }
                    : options with { ParseError = "--min-recall-at-50 must be between 0 and 1." },
                "--min-ap50" => double.TryParse(value, out var minAp50) && minAp50 is >= 0 and <= 1
                    ? options with { MinAP50 = minAp50 }
                    : options with { ParseError = "--min-ap50 must be between 0 and 1." },
                "--model-version" => options with { ModelVersion = value },
                "--candidate-version" => options with { CandidateVersion = value },
                "--profile" => options with { Profile = value },
                "--case-ids" => options with { CaseIds = ParseCaseIds(value) },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };

            if (options.ParseError is not null)
            {
                return options;
            }
        }

        return options;
    }

    private static HashSet<string> ParseCaseIds(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            "Usage: dotnet run --project quality/tools/DeepLearningCocoRealModelRunner/DeepLearningCocoRealModelRunner.csproj -- " +
            "--index quality/datasets/coco2017_index.json --model-manifest models/object_detection/coco_yolo_real_model_manifest.template.json --model <onnx> --output <json> --report <md> " +
            "[--max-cases 120|500|5000] [--min-ap50 0.45 --min-precision-at-50 0.45 --min-recall-at-50 0.35]");
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
