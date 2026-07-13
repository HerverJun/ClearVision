namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentReadOnlyCatalog
{
    public static IReadOnlyList<OperatorCatalogItem> Operators { get; } =
    [
        new("ImageAcquisition", "Image Acquisition", "image", "Provides a camera/file image input node; agent read-only tools never capture frames.", ["image", "camera", "input", "acquisition"]),
        new("Filtering", "Filtering", "image", "Applies Gaussian, mean/box, median, or bilateral spatial smoothing according to FilterMode.", ["filter", "gaussian", "mean", "median", "bilateral", "denoise"]),
        new("RoiManager", "ROI Manager", "roi", "Defines named regions of interest for downstream inspection.", ["roi", "region", "crop"]),
        new("TemplateMatching", "Template Matching", "matching", "Finds a known pattern by template and score.", ["template", "matching", "alignment", "position"]),
        new("BlobAnalysis", "Blob Analysis", "vision", "Describes connected-component inspection metadata without reading images.", ["blob", "area", "count"]),
        new("Thresholding", "Thresholding", "vision", "Defines threshold metadata for binary segmentation review.", ["threshold", "binary", "segmentation"]),
        new("EdgeDetection", "Edge Detection", "vision", "Defines edge extraction metadata for dry-run validation.", ["edge", "gradient", "contour"]),
        new("ShapeMatching", "Shape Matching", "matching", "Finds a shape by catalog template metadata.", ["shape", "matching", "template"]),
        new("DeepLearning", "Deep Learning", "ai", "Runs ONNX object detection, image classification, or semantic segmentation according to TaskType.", ["model", "onnx", "detection", "classification", "segmentation", "defect"]),
        new("SemanticSegmentation", "Semantic Segmentation", "ai", "Reviews segmentation model metadata without loading model files.", ["segmentation", "model", "mask"]),
        new("SurfaceDefectDetection", "Surface Defect Detection", "ai", "Traditional surface defect metadata without loading model files.", ["defect", "surface", "scratch"]),
        new("CircleMeasurement", "Circle Measurement", "measurement", "Measures circle center/radius features.", ["circle", "hole", "diameter"]),
        new("Measurement", "Measurement", "measurement", "Measures point-point, point-line, line-line distance, or a three-point angle according to MeasureType.", ["distance", "angle", "point", "line", "measurement"]),
        new("UnitConvert", "Unit Convert", "measurement", "Converts pixel measurement values to engineering units.", ["calibration", "pixel", "scale", "measurement"]),
        new("DetectionSequenceJudge", "检测顺序判定", "logic", "检查检测标签顺序，适用于端子线序检测。", ["wire", "sequence", "terminal", "order"]),
        new("ImageAdd", "Image Add", "image", "Combines images using the real ImageAdd operator.", ["compose", "multi-camera"]),
        new("ResultJudgment", "Result Judgment", "logic", "Evaluates pass/fail conditions using Value/Confidence inputs.", ["judgment", "pass", "fail", "tolerance"]),
        new("ResultOutput", "Result Output", "output", "Summarizes inspection result payloads.", ["output", "result", "mes", "plc"]),
        new("ModbusCommunication", "Modbus Communication", "communication", "Forbidden preview communication metadata; dry-run only.", ["modbus", "plc", "forbidden"]),
        new("HttpRequest", "HTTP Request", "communication", "Forbidden preview network metadata; dry-run only.", ["http", "network", "forbidden"]),
        new("ScriptOperator", "Script Operator", "logic", "Forbidden preview script metadata; dry-run only.", ["script", "command", "forbidden"])
    ];

    public static IReadOnlyDictionary<string, OperatorSchemaItem> Schemas { get; } =
        new Dictionary<string, OperatorSchemaItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["ImageAcquisition"] = new(
                "ImageAcquisition",
                ["Image"],
                [],
                [
                    new("SourceType", "string", false, "Camera/File/ProvidedFrame; read-only agent tools do not acquire images."),
                    new("CameraId", "cameraBinding", false, "Logical camera binding id supplied by engineer."),
                    new("FilePath", "string", false, "Optional offline file source; not read by read-only agent tools.")
                ]),
            ["Filtering"] = new(
                "Filtering",
                ["Image", "FilterMode", "FilterDiagnostics"],
                ["Image"],
                [
                    new("FilterMode", "enum", false, "Gaussian (default), Mean/Box, Median, or Bilateral."),
                    new("KernelSize", "int", false, "Used by Gaussian, Mean/Box, and Median modes."),
                    new("SigmaX", "double", false, "Gaussian horizontal sigma."),
                    new("SigmaY", "double", false, "Gaussian vertical sigma; zero follows OpenCV Gaussian semantics."),
                    new("BorderType", "enum", false, "Used by Gaussian, Mean/Box, and Bilateral modes."),
                    new("Diameter", "int", false, "Bilateral neighborhood diameter."),
                    new("SigmaColor", "double", false, "Bilateral color sigma."),
                    new("SigmaSpace", "double", false, "Bilateral spatial sigma.")
                ]),
            ["TemplateMatching"] = new(
                "TemplateMatching",
                ["Image", "Position", "Score", "NormalizedScore", "RawResponse", "SubpixelOffsetX", "SubpixelOffsetY", "PeakCurvature", "Angle", "Scale", "IsMatch", "Matches", "MatchCount"],
                ["Image", "Template", "Mask"],
                [
                    new("Method", "enum", false, "Template matching method."),
                    new("Domain", "enum", false, "Matching domain."),
                    new("Threshold", "double", false, "Minimum acceptable match score."),
                    new("MaxMatches", "int", false, "Maximum number of matches.")
                ]),
            ["BlobAnalysis"] = new(
                "BlobAnalysis",
                ["Blobs", "BlobCount", "AreaStatistics"],
                ["Image"],
                [
                    new("MinArea", "double", false, "Minimum blob area metadata."),
                    new("MaxArea", "double", false, "Maximum blob area metadata.")
                ]),
            ["Thresholding"] = new(
                "Thresholding",
                ["BinaryImage"],
                ["Image"],
                [
                    new("Threshold", "double", true, "Threshold metadata value."),
                    new("Mode", "string", false, "Threshold mode metadata.")
                ]),
            ["EdgeDetection"] = new(
                "EdgeDetection",
                ["Edges"],
                ["Image"],
                [
                    new("Method", "string", false, "Edge detector metadata."),
                    new("Polarity", "string", false, "Expected edge polarity metadata.")
                ]),
            ["ShapeMatching"] = new(
                "ShapeMatching",
                ["MatchResult", "Score", "Pose"],
                ["Image"],
                [
                    new("TemplateId", "string", true, "Catalog template metadata id; no template file is loaded."),
                    new("MinScore", "double", false, "Minimum acceptable shape score.")
                ]),
            ["DeepLearning"] = new(
                "DeepLearning",
                [
                    "Image", "OriginalImage", "DetectionList", "Defects", "DefectCount", "Objects", "ObjectCount",
                    "TaskType", "RequestedTaskType", "TaskResolutionSource", "TaskResolutionEvidence",
                    "TopClassLabel", "TopClassConfidence", "ClassificationTopK", "ClassificationResult",
                    "SegmentationMap", "ColoredMap", "ClassMasks", "ClassCount", "ClassMaskCount",
                    "OmittedClassMaskCount", "PresentClasses", "StatusCode", "StatusMessage",
                    "ResolvedModelPath", "ResolvedModelId", "ResolvedModelCatalogPath", "ModelSource",
                    "ModelProvenance", "PostprocessDiagnostics", "OutputFormat"
                ],
                ["Image"],
                [
                    new("TaskType", "enum", false, "ObjectDetection (legacy default), ImageClassification, SemanticSegmentation, or reliable Auto."),
                    new("ModelPath", "file", true, "Configured model artifact path; not loaded by read-only agent tools."),
                    new("Confidence", "double", false, "ObjectDetection confidence threshold only."),
                    new("ModelVersion", "enum", false, "ObjectDetection YOLO format selector only."),
                    new("InputSize", "int", false, "ObjectDetection square input size only."),
                    new("UseGpu", "bool", false, "Legacy GPU preference used when ExecutionProvider is Auto."),
                    new("GpuDeviceId", "int", false, "GPU device used by CUDA execution."),
                    new("TargetClasses", "string", false, "ObjectDetection class filter only."),
                    new("LabelsPath", "file", false, "Detection or classification label fallback."),
                    new("EnableInternalNms", "bool", false, "ObjectDetection platform-side NMS switch only."),
                    new("NmsIouThreshold", "double", false, "ObjectDetection NMS IoU threshold only."),
                    new("OutputFormat", "enum", false, "ObjectDetection output format only."),
                    new("DetectionMode", "enum", false, "ObjectDetection defect/object result semantics only."),
                    new("TopK", "int", false, "ImageClassification Top-K only."),
                    new("ClassificationInputSize", "string", false, "ImageClassification input size only."),
                    new("ClassificationScoreMode", "enum", false, "ImageClassification logits/probability handling only."),
                    new("ClassNames", "string", false, "Classification or segmentation labels when metadata/catalog names are unavailable."),
                    new("SegmentationInputSize", "string", false, "SemanticSegmentation input size only."),
                    new("NumClasses", "int", false, "SemanticSegmentation class count only."),
                    new("MaxClassMasks", "int", false, "SemanticSegmentation mask output limit only."),
                    new("ExecutionProvider", "enum", false, "Auto, CPU, or CUDA execution backend selection."),
                    new("ScaleToUnitRange", "bool", false, "Classification or segmentation input scaling."),
                    new("ChannelOrder", "enum", false, "Classification or segmentation RGB/BGR channel order."),
                    new("Mean", "string", false, "Classification or segmentation normalization mean."),
                    new("Std", "string", false, "Classification or segmentation normalization standard deviation."),
                    new("ModelId", "string", false, "Catalog model id metadata; use instead of ModelPath."),
                    new("ModelCatalogPath", "file", false, "Optional catalog path when ModelId is used.")
                ]),
            ["SemanticSegmentation"] = new(
                "SemanticSegmentation",
                ["Mask", "Classes", "Scores"],
                ["Image"],
                [
                    new("Method", "enum", false, "Surface defect method."),
                    new("Threshold", "double", false, "Defect response threshold."),
                    new("MinArea", "int", false, "Minimum defect area."),
                    new("MaxArea", "int", false, "Maximum defect area.")
                ]),
            ["SurfaceDefectDetection"] = new(
                "SurfaceDefectDetection",
                ["Defects", "Classes", "Scores"],
                ["Image"],
                [
                    new("ModelId", "string", true, "Catalog model metadata id; no model file is loaded."),
                    new("ModelKind", "string", true, "Expected model kind metadata.")
                ]),
            ["CircleMeasurement"] = new(
                "CircleMeasurement",
                ["Center", "Radius", "Diameter"],
                ["Image"],
                [
                    new("Roi", "string", false, "ROI name for the circle search."),
                    new("EdgePolarity", "string", false, "Expected edge polarity.")
                ]),
            ["Measurement"] = new(
                "Measurement",
                [
                    "Image", "Distance", "DeltaX", "DeltaY", "Angle", "Value", "Unit", "MeasurementType",
                    "StatusCode", "StatusMessage", "FootPoint", "Intersection", "HasIntersection", "IsParallel",
                    "Confidence", "UncertaintyPx", "UncertaintyDeg"
                ],
                ["Image", "PointA", "PointB", "PointC", "Line1", "Line2"],
                [
                    new("MeasureType", "enum", false, "PointToPoint (legacy default), Horizontal, Vertical, PointToLine, LineToLine, or ThreePointAngle."),
                    new("X1", "int", false, "Legacy start point X for point-distance modes without PointA input."),
                    new("Y1", "int", false, "Legacy start point Y for point-distance modes without PointA input."),
                    new("X2", "int", false, "Legacy end point X for point-distance modes without PointB input."),
                    new("Y2", "int", false, "Legacy end point Y for point-distance modes without PointB input."),
                    new("DistanceModel", "enum", false, "Segment or InfiniteLine for point-line and line-line distance."),
                    new("ParallelThreshold", "double", false, "LineToLine parallel-angle threshold in degrees."),
                    new("AngleUnit", "enum", false, "Degree or Radian for ThreePointAngle.")
                ]),
            ["UnitConvert"] = new(
                "UnitConvert",
                ["Result", "Unit"],
                ["Value", "PixelSize"],
                [
                    new("FromUnit", "enum", false, "Source unit."),
                    new("ToUnit", "enum", false, "Target unit."),
                    new("Scale", "double", false, "Pixel-to-world scale; kept pending when calibration is unknown."),
                    new("UseCalibration", "bool", false, "Whether to use PixelSize input.")
                ]),
            ["DetectionSequenceJudge"] = new(
                "DetectionSequenceJudge",
                ["IsMatch", "ActualOrder", "Count", "MissingLabels", "DuplicateLabels", "SortedDetections", "Assignment", "UnassignedDetections", "SlotDistances", "RowCount", "PerspectiveApplied", "Diagnostics", "Message"],
                ["Detections", "SlotPoints", "PerspectiveSrcPoints", "PerspectiveDstPoints"],
                [
                    new("ExpectedLabels", "string", true, "Comma-separated expected labels in order."),
                    new("SortBy", "enum", false, "Field used to sort detections."),
                    new("Direction", "enum", false, "Ordering direction."),
                    new("ExpectedCount", "int", false, "Expected detection count.")
                ]),
            ["ResultJudgment"] = new(
                "ResultJudgment",
                ["JudgmentResult", "IsOk", "ConditionResult", "JudgmentValue", "Details"],
                ["Value", "Confidence"],
                [
                    new("FieldName", "string", false, "Field to read from input payload."),
                    new("Condition", "enum", false, "Pass/fail condition."),
                    new("ExpectValue", "string", false, "Expected value."),
                    new("ExpectValueMin", "string", false, "Expected range minimum."),
                    new("ExpectValueMax", "string", false, "Expected range maximum."),
                    new("MinConfidence", "double", false, "Minimum confidence gate."),
                    new("NumericAbsTolerance", "double", false, "Numeric absolute tolerance."),
                    new("NumericRelTolerance", "double", false, "Numeric relative tolerance.")
                ]),
            ["ResultOutput"] = new(
                "ResultOutput",
                ["Output", "Image", "Result", "Text", "Data", "FilePath"],
                ["Image", "Result", "Text", "Data"],
                [
                    new("Format", "enum", false, "Output format."),
                    new("SaveToFile", "bool", false, "Whether to save output text to a file."),
                    new("MaxFormattedCollectionItems", "int", false, "Maximum formatted collection items.")
                ]),
            ["ModbusCommunication"] = new(
                "ModbusCommunication",
                ["MetadataBlocked"],
                ["Input"],
                [
                    new("WriteIntent", "bool", false, "Forbidden in RuntimePreview metadata-only review.")
                ]),
            ["HttpRequest"] = new(
                "HttpRequest",
                ["MetadataBlocked"],
                ["Input"],
                [
                    new("RequestIntent", "string", false, "Forbidden in RuntimePreview metadata-only review.")
                ]),
            ["ScriptOperator"] = new(
                "ScriptOperator",
                ["MetadataBlocked"],
                ["Input"],
                [
                    new("ScriptIntent", "string", false, "Forbidden in RuntimePreview metadata-only review.")
                ]),
            ["RoiManager"] = new(
                "RoiManager",
                ["RoiImage"],
                ["Image"],
                [
                    new("RoiName", "string", false, "Named ROI.")
                ]),
            ["ImageAdd"] = new(
                "ImageAdd",
                ["Image"],
                ["ImageA", "ImageB"],
                [
                    new("Mode", "string", false, "Composition mode.")
                ])
        };

    public static IReadOnlyList<TemplateItem> Templates { get; } =
    [
        new(
            "wire_sequence_inspection",
            "wire_sequence",
            "Wire sequence inspection",
            ["wire", "sequence", "terminal", "harness"],
            ["ImageAcquisition", "RoiManager", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_roi", "Image"),
                Link("op_roi", "Image", "op_detect", "Image"),
                Link("op_detect", "DetectionList", "op_sequence", "Detections"),
                Link("op_sequence", "IsMatch", "op_judge", "Value"),
                Link("op_judge", "JudgmentResult", "op_out", "Result")
            ]),
        new(
            "template_matching_alignment",
            "template_matching",
            "Template matching alignment",
            ["template", "matching", "alignment", "position"],
            ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_judge", "Value"),
                Link("op_judge", "JudgmentResult", "op_out", "Result")
            ]),
        new(
            "hole_distance_measurement",
            "measurement",
            "Hole distance measurement",
            ["hole", "distance", "spacing", "measurement"],
            ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "Measurement", "UnitConvert", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_circle_a", "Image"),
                Link("op_cam", "Image", "op_circle_b", "Image"),
                Link("op_circle_a", "Center", "op_distance", "PointA"),
                Link("op_circle_b", "Center", "op_distance", "PointB"),
                Link("op_distance", "Distance", "op_calibration", "Value"),
                Link("op_calibration", "Result", "op_judge", "Value"),
                Link("op_judge", "JudgmentResult", "op_out", "Result")
            ])
    ];

    private static TemplateConnection Link(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new TemplateConnection(sourceTempId, sourcePortName, targetTempId, targetPortName);
    }
}

internal sealed record OperatorCatalogItem(
    string OperatorType,
    string DisplayName,
    string Category,
    string Summary,
    IReadOnlyList<string> Keywords);

internal sealed record OperatorSchemaItem(
    string OperatorType,
    IReadOnlyList<string> OutputPorts,
    IReadOnlyList<string> InputPorts,
    IReadOnlyList<OperatorParameterItem> Parameters);

internal sealed record OperatorParameterItem(
    string Name,
    string DataType,
    bool Required,
    string Summary);

internal sealed record TemplateItem(
    string TemplateId,
    string ScenarioKey,
    string Name,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> OperatorTypes,
    IReadOnlyList<TemplateConnection> Connections);

internal sealed record TemplateConnection(
    string SourceTempId,
    string SourcePortName,
    string TargetTempId,
    string TargetPortName);
