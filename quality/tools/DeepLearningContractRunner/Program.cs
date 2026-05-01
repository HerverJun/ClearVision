using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Acme.Product.Infrastructure.Operators;
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

var result = ContractRunner.Run();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"DeepLearning contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);
    private static readonly Type DetectionType = typeof(DeepLearningOperator)
        .GetNestedType("DetectionResult", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DeepLearningOperator.DetectionResult not found.");

    public static BaselineResult Run()
    {
        var cases = new List<ContractCase>
        {
            new("yolov8_standard_box_mapping", "YOLO output parsing", YoloV8StandardBoxMapping),
            new("yolov8_transposed_box_mapping", "YOLO output parsing", YoloV8TransposedBoxMapping),
            new("yolov8_coordinate_clamp", "YOLO output parsing", YoloV8CoordinateClamp),
            new("yolov5_standard_objectness_product", "YOLO output parsing", YoloV5StandardObjectnessProduct),
            new("yolov5_transposed_objectness_product", "YOLO output parsing", YoloV5TransposedObjectnessProduct),
            new("auto_detect_yolov8_custom_labels", "YOLO version detection", AutoDetectYoloV8),
            new("auto_detect_yolov5_custom_labels", "YOLO version detection", AutoDetectYoloV5),
            new("select_detection_output_known_label_count", "Output tensor selection", SelectOutputKnownLabels),
            new("select_detection_output_rank3_heuristic", "Output tensor selection", SelectOutputRank3Heuristic),
            new("select_detection_output_fail_closed", "Output tensor selection", SelectOutputFailClosed),
            new("nms_same_class_suppresses_overlap", "NMS contract", NmsSameClassSuppresses),
            new("nms_different_class_keeps_overlap", "NMS contract", NmsDifferentClassKeeps),
            new("nms_iou_threshold_low_suppresses", "NMS contract", NmsLowThresholdSuppresses),
            new("nms_iou_threshold_high_keeps", "NMS contract", NmsHighThresholdKeeps),
            new("nms_invalid_box_discarded", "NMS contract", NmsInvalidBoxDiscarded),
            new("target_classes_numeric_filter", "Target class contract", TargetClassesNumericFilter),
            new("target_classes_named_parse", "Target class contract", TargetClassesNamedParse),
            new("label_contract_match_valid", "Label contract", LabelContractMatchValid),
            new("label_contract_mismatch_fails", "Label contract", LabelContractMismatchFails),
            new("label_contract_missing_fails", "Label contract", LabelContractMissingFails),
            new("visualization_nms_when_internal_disabled", "Visualization contract", VisualizationNmsWhenInternalDisabled),
            new("statistics_label_object_mode", "Output contract", StatisticsLabelObjectMode),
            new("statistics_label_defect_mode", "Output contract", StatisticsLabelDefectMode),
            new("preprocess_grayscale_to_chw_rgb", "Preprocess contract", PreprocessGrayscale),
            new("preprocess_float_unit_range", "Preprocess contract", PreprocessFloatUnitRange),
            new("class_name_fallback_is_class_id", "Label contract", ClassNameFallback)
        };

        var results = new List<CaseResult>(cases.Count);
        foreach (var contractCase in cases)
        {
            results.Add(RunCase(contractCase));
        }

        var byScenario = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToList();

        var failed = results.Count(item => !item.Passed);
        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count - failed,
                failed,
                Math.Round(results.Sum(item => item.RuntimeMs), 3),
                results.Sum(item => item.MemoryAllocationBytes)),
            [
                new OperatorSummary(
                    "DeepLearning",
                    results.Count,
                    results.Count - failed,
                    failed,
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    (long)Math.Round(results.Average(item => item.MemoryAllocationBytes)))
            ],
            byScenario,
            results);
    }

    private static CaseResult RunCase(ContractCase contractCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            var metrics = contractCase.Execute();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();
            return new CaseResult(
                contractCase.Id,
                contractCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                null,
                metrics);
        }
        catch (Exception ex)
        {
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();
            return new CaseResult(
                contractCase.Id,
                contractCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                ex.GetBaseException().Message,
                new Dictionary<string, object?>());
        }
    }

    private static Dictionary<string, object?> YoloV8StandardBoxMapping()
    {
        var tensor = CreateYoloV8StandardTensor(anchorCount: 20, classCount: 2);
        WriteYoloV8Standard(tensor, 0, x: 160, y: 160, w: 80, h: 40, classScores: [0.95f, 0.05f]);

        var detections = InvokePostprocessYoloV8(tensor, threshold: 0.5f, originalWidth: 320, originalHeight: 240, inputSize: 320, enableNms: true, nmsIou: 0.45f);
        Require(detections.Count == 1, $"Expected 1 detection, got {detections.Count}.");
        var d = detections[0];
        RequireApproximately(d.X, 120f, 0.01f, "X");
        RequireApproximately(d.Y, 100f, 0.01f, "Y");
        RequireApproximately(d.Width, 80f, 0.01f, "Width");
        RequireApproximately(d.Height, 40f, 0.01f, "Height");
        Require(d.ClassId == 0, "Expected class 0.");

        return Metrics(("DetectionCount", detections.Count), ("X", d.X), ("Y", d.Y), ("Width", d.Width), ("Height", d.Height), ("Confidence", d.Confidence));
    }

    private static Dictionary<string, object?> YoloV8TransposedBoxMapping()
    {
        var tensor = CreateYoloV8TransposedTensor(anchorCount: 20, classCount: 2);
        WriteYoloV8Transposed(tensor, 0, x: 160, y: 160, w: 80, h: 40, classScores: [0.10f, 0.93f]);

        var detections = InvokePostprocessYoloV8(tensor, threshold: 0.5f, originalWidth: 320, originalHeight: 240, inputSize: 320, enableNms: true, nmsIou: 0.45f);
        Require(detections.Count == 1, $"Expected 1 detection, got {detections.Count}.");
        var d = detections[0];
        Require(d.ClassId == 1, "Expected class 1.");
        RequireApproximately(d.Y, 100f, 0.01f, "Y");

        return Metrics(("DetectionCount", detections.Count), ("ClassId", d.ClassId), ("Y", d.Y), ("Confidence", d.Confidence));
    }

    private static Dictionary<string, object?> YoloV8CoordinateClamp()
    {
        var tensor = CreateYoloV8StandardTensor(anchorCount: 20, classCount: 2);
        WriteYoloV8Standard(tensor, 0, x: 10, y: 45, w: 80, h: 80, classScores: [0.99f, 0.01f]);

        var detections = InvokePostprocessYoloV8(tensor, threshold: 0.5f, originalWidth: 320, originalHeight: 240, inputSize: 320, enableNms: true, nmsIou: 0.45f);
        Require(detections.Count == 1, $"Expected 1 detection, got {detections.Count}.");
        var d = detections[0];
        Require(d.X >= 0f && d.Y >= 0f, "Coordinates should be clamped to image bounds.");

        return Metrics(("X", d.X), ("Y", d.Y), ("Width", d.Width), ("Height", d.Height));
    }

    private static Dictionary<string, object?> YoloV5StandardObjectnessProduct()
    {
        var tensor = CreateYoloV5StandardTensor(anchorCount: 20, classCount: 2);
        WriteYoloV5Standard(tensor, 0, x: 160, y: 160, w: 80, h: 40, objectness: 0.8f, classScores: [0.20f, 0.75f]);

        var detections = InvokePostprocessYoloV5(tensor, threshold: 0.5f, originalWidth: 320, originalHeight: 240, inputSize: 320, enableNms: true, nmsIou: 0.45f);
        Require(detections.Count == 1, $"Expected 1 detection, got {detections.Count}.");
        var d = detections[0];
        Require(d.ClassId == 1, "Expected class 1.");
        RequireApproximately(d.Confidence, 0.6f, 0.0001f, "Confidence");

        return Metrics(("DetectionCount", detections.Count), ("ClassId", d.ClassId), ("Confidence", d.Confidence));
    }

    private static Dictionary<string, object?> YoloV5TransposedObjectnessProduct()
    {
        var tensor = CreateYoloV5TransposedTensor(anchorCount: 20, classCount: 2);
        WriteYoloV5Transposed(tensor, 0, x: 160, y: 160, w: 80, h: 40, objectness: 0.9f, classScores: [0.70f, 0.20f]);

        var detections = InvokePostprocessYoloV5(tensor, threshold: 0.5f, originalWidth: 320, originalHeight: 240, inputSize: 320, enableNms: true, nmsIou: 0.45f);
        Require(detections.Count == 1, $"Expected 1 detection, got {detections.Count}.");
        var d = detections[0];
        Require(d.ClassId == 0, "Expected class 0.");
        RequireApproximately(d.Confidence, 0.63f, 0.0001f, "Confidence");

        return Metrics(("DetectionCount", detections.Count), ("ClassId", d.ClassId), ("Confidence", d.Confidence));
    }

    private static Dictionary<string, object?> AutoDetectYoloV8()
    {
        var tensor = CreateYoloV8StandardTensor(anchorCount: 20, classCount: 2);
        var version = InvokeDetectYoloVersion(tensor, knownLabelCount: 2);
        Require(version == YoloVersion.YOLOv8, $"Expected YOLOv8, got {version}.");
        return Metrics(("DetectedVersion", version.ToString()));
    }

    private static Dictionary<string, object?> AutoDetectYoloV5()
    {
        var tensor = CreateYoloV5StandardTensor(anchorCount: 20, classCount: 2);
        var version = InvokeDetectYoloVersion(tensor, knownLabelCount: 2);
        Require(version == YoloVersion.YOLOv5, $"Expected YOLOv5, got {version}.");
        return Metrics(("DetectedVersion", version.ToString()));
    }

    private static Dictionary<string, object?> SelectOutputKnownLabels()
    {
        var (index, rule) = InvokeSelectDetectionOutputIndex(
            ["seg_output", "det_output"],
            [new[] { 1, 3, 64, 64 }, new[] { 1, 6, 20 }],
            knownLabelCount: 2);
        Require(index == 1, $"Expected output index 1, got {index}.");
        Require(rule.Contains("KnownLabelFeature", StringComparison.Ordinal), $"Unexpected rule {rule}.");
        return Metrics(("SelectedIndex", index), ("SelectionRule", rule));
    }

    private static Dictionary<string, object?> SelectOutputRank3Heuristic()
    {
        var (index, rule) = InvokeSelectDetectionOutputIndex(
            ["small_rank3", "large_rank3"],
            [new[] { 1, 32, 32 }, new[] { 1, 84, 8400 }],
            knownLabelCount: 0);
        Require(index == 1, $"Expected output index 1, got {index}.");
        Require(rule == "Rank3Heuristic", $"Unexpected rule {rule}.");
        return Metrics(("SelectedIndex", index), ("SelectionRule", rule));
    }

    private static Dictionary<string, object?> SelectOutputFailClosed()
    {
        var failed = false;
        try
        {
            _ = InvokeSelectDetectionOutputIndex(
                ["seg0", "seg1"],
                [new[] { 1, 3, 64, 64 }, new[] { 1, 2, 32, 32 }],
                knownLabelCount: 0);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            failed = true;
        }

        Require(failed, "Output selection should fail closed when no detection tensor exists.");
        return Metrics(("FailClosed", true));
    }

    private static Dictionary<string, object?> NmsSameClassSuppresses()
    {
        var detections = CreateDetectionList([
            CreateDetection(10, 10, 40, 40, 0.95f, 0),
            CreateDetection(12, 12, 40, 40, 0.90f, 0),
            CreateDetection(90, 90, 20, 20, 0.85f, 0)
        ]);

        var (kept, comparisons) = InvokeApplyNmsWithStats(detections, 0.45f);
        Require(kept.Count == 2, $"Expected 2 kept detections, got {kept.Count}.");
        return Metrics(("Kept", kept.Count), ("Comparisons", comparisons));
    }

    private static Dictionary<string, object?> NmsDifferentClassKeeps()
    {
        var detections = CreateDetectionList([
            CreateDetection(10, 10, 40, 40, 0.95f, 0),
            CreateDetection(12, 12, 40, 40, 0.90f, 1)
        ]);

        var (kept, comparisons) = InvokeApplyNmsWithStats(detections, 0.45f);
        Require(kept.Count == 2, $"Expected different classes to both survive, got {kept.Count}.");
        Require(comparisons == 0, "Different-class boxes should not be compared for suppression.");
        return Metrics(("Kept", kept.Count), ("Comparisons", comparisons));
    }

    private static Dictionary<string, object?> NmsLowThresholdSuppresses()
    {
        var detections = CreateDetectionList([
            CreateDetection(10, 10, 40, 40, 0.95f, 0),
            CreateDetection(30, 30, 40, 40, 0.90f, 0)
        ]);
        var (kept, _) = InvokeApplyNmsWithStats(detections, 0.10f);
        Require(kept.Count == 1, $"Expected low threshold to suppress, got {kept.Count}.");
        return Metrics(("Kept", kept.Count), ("IouThreshold", 0.10f));
    }

    private static Dictionary<string, object?> NmsHighThresholdKeeps()
    {
        var detections = CreateDetectionList([
            CreateDetection(10, 10, 40, 40, 0.95f, 0),
            CreateDetection(30, 30, 40, 40, 0.90f, 0)
        ]);
        var (kept, _) = InvokeApplyNmsWithStats(detections, 0.90f);
        Require(kept.Count == 2, $"Expected high threshold to keep both, got {kept.Count}.");
        return Metrics(("Kept", kept.Count), ("IouThreshold", 0.90f));
    }

    private static Dictionary<string, object?> NmsInvalidBoxDiscarded()
    {
        var detections = CreateDetectionList([
            CreateDetection(10, 10, 0, 40, 0.95f, 0),
            CreateDetection(12, 12, 20, 20, 0.90f, 0)
        ]);
        var (kept, _) = InvokeApplyNmsWithStats(detections, 0.45f);
        Require(kept.Count == 1, $"Expected one valid box, got {kept.Count}.");
        Require(ReadDetection(kept[0]).Width > 0, "Kept detection should be valid.");
        return Metrics(("Kept", kept.Count));
    }

    private static Dictionary<string, object?> TargetClassesNumericFilter()
    {
        var tensor = CreateYoloV8StandardTensor(anchorCount: 20, classCount: 2);
        WriteYoloV8Standard(tensor, 0, x: 80, y: 80, w: 40, h: 40, classScores: [0.95f, 0.05f]);
        WriteYoloV8Standard(tensor, 1, x: 180, y: 180, w: 40, h: 40, classScores: [0.05f, 0.92f]);

        var detections = InvokePostprocessResults(tensor, 0.5f, 320, 320, 320, YoloVersion.YOLOv8, [1], true, 0.45f);
        Require(detections.Count == 1, $"Expected only class 1, got {detections.Count}.");
        Require(detections[0].ClassId == 1, "Expected class 1.");
        return Metrics(("DetectionCount", detections.Count), ("ClassId", detections[0].ClassId));
    }

    private static Dictionary<string, object?> TargetClassesNamedParse()
    {
        var result = InvokeParseTargetClasses("Wire_Black,Wire_Blue", ["Wire_Brown", "Wire_Black", "Wire_Blue"]);
        Require(result.SetEquals([1, 2]), $"Unexpected target class set: {string.Join(",", result)}.");
        return Metrics(("TargetClasses", string.Join(",", result.OrderBy(x => x))));
    }

    private static Dictionary<string, object?> LabelContractMatchValid()
    {
        var sourceInfo = CreateLabelSourceInfo(["Wire_Blue", "Wire_Black"], "ExplicitFile", "labels.txt", isFileBacked: true);
        var contract = InvokeBuildLabelContract("model.onnx", ["Wire_Blue", "Wire_Black"], sourceInfo);
        Require(ReadProperty<bool>(contract, "IsValid"), "Expected valid label contract.");
        return Metrics(("ValidationStatus", ReadProperty<string>(contract, "ValidationStatus")), ("ResolvedLabelSource", ReadProperty<string>(contract, "ResolvedLabelSource")));
    }

    private static Dictionary<string, object?> LabelContractMismatchFails()
    {
        var sourceInfo = CreateLabelSourceInfo(["Wire_Black", "Wire_Blue"], "ExplicitFile", "labels.txt", isFileBacked: true);
        var contract = InvokeBuildLabelContract("model.onnx", ["Wire_Blue", "Wire_Black"], sourceInfo);
        Require(!ReadProperty<bool>(contract, "IsValid"), "Expected invalid label contract.");
        Require(ReadProperty<string>(contract, "ValidationStatus") == "Mismatch", "Expected mismatch status.");
        return Metrics(("ValidationStatus", ReadProperty<string>(contract, "ValidationStatus")));
    }

    private static Dictionary<string, object?> LabelContractMissingFails()
    {
        var sourceInfo = CreateLabelSourceInfo([], "Unavailable", string.Empty, isFileBacked: false);
        var contract = InvokeBuildLabelContract("model.onnx", [], sourceInfo);
        Require(!ReadProperty<bool>(contract, "IsValid"), "Expected invalid label contract.");
        Require(ReadProperty<string>(contract, "ValidationStatus") == "MissingLabelContract", "Expected MissingLabelContract status.");
        return Metrics(("ValidationStatus", ReadProperty<string>(contract, "ValidationStatus")));
    }

    private static Dictionary<string, object?> VisualizationNmsWhenInternalDisabled()
    {
        var detections = CreateDetectionList([
            CreateDetection(10, 10, 40, 40, 0.95f, 0),
            CreateDetection(12, 12, 40, 40, 0.85f, 0),
            CreateDetection(80, 80, 20, 20, 0.80f, 0)
        ]);

        var visual = InvokeBuildVisualizationDetections(detections, 0.05f, enableInternalNms: false, nmsIou: 0.45f);
        Require(visual.Count == 2, $"Expected visual NMS to keep 2 boxes, got {visual.Count}.");
        return Metrics(("VisualizationDetectionCount", visual.Count));
    }

    private static Dictionary<string, object?> StatisticsLabelObjectMode()
    {
        var label = InvokeBuildStatisticsLabel(3, "Object");
        Require(label == "Objects: 3", $"Unexpected label {label}.");
        return Metrics(("Label", label));
    }

    private static Dictionary<string, object?> StatisticsLabelDefectMode()
    {
        var label = InvokeBuildStatisticsLabel(2, "Defect");
        Require(label == "Defects: 2", $"Unexpected label {label}.");
        return Metrics(("Label", label));
    }

    private static Dictionary<string, object?> PreprocessGrayscale()
    {
        using var image = new Mat(32, 24, MatType.CV_8UC1, Scalar.All(128));
        var tensor = InvokePreprocessImage(image, 64);
        var dimensions = tensor.Dimensions.ToArray();
        Require(dimensions.SequenceEqual(new[] { 1, 3, 64, 64 }), $"Unexpected tensor shape {string.Join(",", dimensions)}.");
        return Metrics(("Shape", string.Join(",", dimensions)));
    }

    private static Dictionary<string, object?> PreprocessFloatUnitRange()
    {
        using var image = new Mat(64, 64, MatType.CV_32FC1, Scalar.All(0.5));
        var tensor = InvokePreprocessImage(image, 64);
        var values = tensor.ToArray();
        RequireApproximately(values[0], 0.5f, 0.02f, "FirstValue");
        return Metrics(("FirstValue", values[0]));
    }

    private static Dictionary<string, object?> ClassNameFallback()
    {
        var label = InvokeGetClassName(7, []);
        Require(label == "class_7", $"Unexpected fallback label {label}.");
        return Metrics(("Label", label));
    }

    private static DenseTensor<float> CreateYoloV8StandardTensor(int anchorCount, int classCount)
    {
        return new DenseTensor<float>(new float[1 * (4 + classCount) * anchorCount], [1, 4 + classCount, anchorCount]);
    }

    private static DenseTensor<float> CreateYoloV8TransposedTensor(int anchorCount, int classCount)
    {
        return new DenseTensor<float>(new float[1 * anchorCount * (4 + classCount)], [1, anchorCount, 4 + classCount]);
    }

    private static DenseTensor<float> CreateYoloV5StandardTensor(int anchorCount, int classCount)
    {
        return new DenseTensor<float>(new float[1 * anchorCount * (5 + classCount)], [1, anchorCount, 5 + classCount]);
    }

    private static DenseTensor<float> CreateYoloV5TransposedTensor(int anchorCount, int classCount)
    {
        return new DenseTensor<float>(new float[1 * (5 + classCount) * anchorCount], [1, 5 + classCount, anchorCount]);
    }

    private static void WriteYoloV8Standard(DenseTensor<float> tensor, int anchor, float x, float y, float w, float h, float[] classScores)
    {
        tensor[0, 0, anchor] = x;
        tensor[0, 1, anchor] = y;
        tensor[0, 2, anchor] = w;
        tensor[0, 3, anchor] = h;
        for (var i = 0; i < classScores.Length; i++)
        {
            tensor[0, 4 + i, anchor] = classScores[i];
        }
    }

    private static void WriteYoloV8Transposed(DenseTensor<float> tensor, int anchor, float x, float y, float w, float h, float[] classScores)
    {
        tensor[0, anchor, 0] = x;
        tensor[0, anchor, 1] = y;
        tensor[0, anchor, 2] = w;
        tensor[0, anchor, 3] = h;
        for (var i = 0; i < classScores.Length; i++)
        {
            tensor[0, anchor, 4 + i] = classScores[i];
        }
    }

    private static void WriteYoloV5Standard(DenseTensor<float> tensor, int anchor, float x, float y, float w, float h, float objectness, float[] classScores)
    {
        tensor[0, anchor, 0] = x;
        tensor[0, anchor, 1] = y;
        tensor[0, anchor, 2] = w;
        tensor[0, anchor, 3] = h;
        tensor[0, anchor, 4] = objectness;
        for (var i = 0; i < classScores.Length; i++)
        {
            tensor[0, anchor, 5 + i] = classScores[i];
        }
    }

    private static void WriteYoloV5Transposed(DenseTensor<float> tensor, int anchor, float x, float y, float w, float h, float objectness, float[] classScores)
    {
        tensor[0, 0, anchor] = x;
        tensor[0, 1, anchor] = y;
        tensor[0, 2, anchor] = w;
        tensor[0, 3, anchor] = h;
        tensor[0, 4, anchor] = objectness;
        for (var i = 0; i < classScores.Length; i++)
        {
            tensor[0, 5 + i, anchor] = classScores[i];
        }
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

    private static List<DetectionRecord> InvokePostprocessYoloV5(
        DenseTensor<float> tensor,
        float threshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        bool enableNms,
        float nmsIou)
    {
        return ToDetectionRecords(InvokeInstanceEnumerable(
            "PostprocessYoloV5V6",
            tensor,
            threshold,
            originalWidth,
            originalHeight,
            inputSize,
            enableNms,
            nmsIou));
    }

    private static List<DetectionRecord> InvokePostprocessResults(
        DenseTensor<float> tensor,
        float threshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        YoloVersion version,
        HashSet<int>? targetClasses,
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
            version,
            targetClasses,
            enableNms,
            nmsIou));
    }

    private static YoloVersion InvokeDetectYoloVersion(DenseTensor<float> tensor, int knownLabelCount)
    {
        return (YoloVersion)InvokeInstance("DetectYoloVersion", tensor, knownLabelCount)!;
    }

    private static (int SelectedIndex, string SelectionRule) InvokeSelectDetectionOutputIndex(
        IReadOnlyList<string> outputNames,
        IReadOnlyList<int[]> outputShapes,
        int knownLabelCount)
    {
        var tuple = InvokeStatic("SelectDetectionOutputIndex", outputNames, outputShapes, knownLabelCount)!;
        var tupleType = tuple.GetType();
        return (
            (int)tupleType.GetField("Item1")!.GetValue(tuple)!,
            (string)tupleType.GetField("Item2")!.GetValue(tuple)!);
    }

    private static (List<object> Kept, long Comparisons) InvokeApplyNmsWithStats(IList detections, float iouThreshold)
    {
        var tuple = InvokeInstance("ApplyNmsWithStats", detections, iouThreshold)!;
        var tupleType = tuple.GetType();
        var kept = ((IEnumerable)tupleType.GetField("Item1")!.GetValue(tuple)!).Cast<object>().ToList();
        var comparisons = (long)tupleType.GetField("Item2")!.GetValue(tuple)!;
        return (kept, comparisons);
    }

    private static HashSet<int> InvokeParseTargetClasses(string targetClasses, IReadOnlyList<string> labels)
    {
        return (HashSet<int>)InvokeInstance("ParseTargetClasses", targetClasses, labels)!;
    }

    private static object InvokeBuildLabelContract(string modelPath, string[] metadataLabels, object sourceInfo)
    {
        return InvokeInstance("BuildLabelContract", modelPath, metadataLabels, sourceInfo)!;
    }

    private static List<DetectionRecord> InvokeBuildVisualizationDetections(IList detections, float threshold, bool enableInternalNms, float nmsIou)
    {
        return ToDetectionRecords(InvokeInstanceEnumerable("BuildVisualizationDetections", detections, threshold, enableInternalNms, nmsIou));
    }

    private static string InvokeBuildStatisticsLabel(int count, string detectionMode)
    {
        return (string)InvokeStatic("BuildStatisticsLabel", count, detectionMode)!;
    }

    private static DenseTensor<float> InvokePreprocessImage(Mat image, int inputSize)
    {
        return (DenseTensor<float>)InvokeInstance("PreprocessImage", image, inputSize)!;
    }

    private static string InvokeGetClassName(int classId, IReadOnlyList<string> labels)
    {
        return (string)InvokeInstance("GetClassName", classId, labels)!;
    }

    private static object? InvokeInstance(string methodName, params object?[] args)
    {
        var method = typeof(DeepLearningOperator).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DeepLearningOperator), methodName);
        return method.Invoke(Operator, args);
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = typeof(DeepLearningOperator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DeepLearningOperator), methodName);
        return method.Invoke(null, args);
    }

    private static IEnumerable InvokeInstanceEnumerable(string methodName, params object?[] args)
    {
        return (IEnumerable)(InvokeInstance(methodName, args)
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static IList CreateDetectionList(IEnumerable<object> detections)
    {
        var listType = typeof(List<>).MakeGenericType(DetectionType);
        var list = (IList)(Activator.CreateInstance(listType)
            ?? throw new InvalidOperationException("Could not create detection list."));
        foreach (var detection in detections)
        {
            list.Add(detection);
        }

        return list;
    }

    private static object CreateDetection(float x, float y, float width, float height, float confidence, int classId)
    {
        var instance = Activator.CreateInstance(DetectionType)
            ?? throw new InvalidOperationException("Could not create detection.");
        DetectionType.GetProperty("X")!.SetValue(instance, x);
        DetectionType.GetProperty("Y")!.SetValue(instance, y);
        DetectionType.GetProperty("Width")!.SetValue(instance, width);
        DetectionType.GetProperty("Height")!.SetValue(instance, height);
        DetectionType.GetProperty("Confidence")!.SetValue(instance, confidence);
        DetectionType.GetProperty("ClassId")!.SetValue(instance, classId);
        return instance;
    }

    private static object CreateLabelSourceInfo(string[] labels, string source, string path, bool isFileBacked)
    {
        var type = typeof(DeepLearningOperator).GetNestedType("LabelSourceInfo", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LabelSourceInfo not found.");
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create LabelSourceInfo.");
        type.GetProperty("Labels")!.SetValue(instance, labels);
        type.GetProperty("Source")!.SetValue(instance, source);
        type.GetProperty("Path")!.SetValue(instance, path);
        type.GetProperty("IsFileBacked")!.SetValue(instance, isFileBacked);
        return instance;
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

    private static Dictionary<string, object?> Metrics(params (string Key, object? Value)[] values)
    {
        return values.ToDictionary(item => item.Key, item => item.Value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireApproximately(float actual, float expected, float tolerance, string name)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{name} expected {expected}, got {actual}.");
        }
    }

    private sealed record ContractCase(string Id, string Scenario, Func<Dictionary<string, object?>> Execute);
}

internal sealed record DetectionRecord(float X, float Y, float Width, float Height, float Confidence, int ClassId);

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            OutputPath: "quality/evals/reports/DeepLearning_contract_baseline.json",
            ReportPath: "quality/evals/reports/DeepLearning_contract_baseline.md",
            ShowHelp: false,
            ParseError: null);

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
        Usage: dotnet run --project quality/tools/DeepLearningContractRunner/DeepLearningContractRunner.csproj -- [options]

        Options:
          --output <path>   Baseline JSON output path.
          --report <path>   Baseline Markdown report path.
        """);
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning Contract Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:F3} |",
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        };

        foreach (var scenario in result.Scenarios)
        {
            lines.Add($"| {scenario.Scenario} | {scenario.CaseCount} | {scenario.Passed} | {scenario.Failed} | {scenario.RuntimeMsAvg:F3} |");
        }

        lines.AddRange([
            "",
            "## Cases",
            "",
            "| Case | Scenario | Passed | Runtime ms | Failure |",
            "| --- | --- | --- | ---: | --- |"
        ]);

        foreach (var item in result.Cases)
        {
            lines.Add($"| {item.CaseId} | {item.Scenario} | {item.Passed} | {item.RuntimeMs:F3} | {item.Failure ?? ""} |");
        }

        lines.AddRange([
            "",
            "## Notes",
            "",
            "- This is a contract baseline using controlled fake YOLO tensors and direct DeepLearningOperator post-processing paths.",
            "- It validates output tensor layout parsing, coordinate mapping, configurable NMS, same-class NMS isolation, TargetClasses parsing, label-contract failures, preprocessing, and output text contracts.",
            "- It does not claim model accuracy; real model quality should be evaluated separately with a public or field dataset."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record BaselineResult(
    BaselineSummary Summary,
    List<OperatorSummary> Operators,
    List<ScenarioSummary> Scenarios,
    List<CaseResult> Cases);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs,
    long MemoryAllocationBytes);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? Failure,
    Dictionary<string, object?> Metrics);

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
