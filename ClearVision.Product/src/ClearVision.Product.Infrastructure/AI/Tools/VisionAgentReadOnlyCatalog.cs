namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentReadOnlyCatalog
{
    public static IReadOnlyList<OperatorCatalogItem> Operators { get; } =
    [
        new("ImageAcquisition", "Image Acquisition", "image", "Provides a camera/file image input node; agent read-only tools never capture frames.", ["image", "camera", "input", "acquisition"]),
        new("RoiManager", "ROI Manager", "roi", "Defines named regions of interest for downstream inspection.", ["roi", "region", "crop"]),
        new("TemplateMatching", "Template Matching", "matching", "Finds a known pattern by template and score.", ["template", "matching", "alignment", "position"]),
        new("BlobAnalysis", "Blob Analysis", "vision", "Describes connected-component inspection metadata without reading images.", ["blob", "area", "count"]),
        new("Thresholding", "Thresholding", "vision", "Defines threshold metadata for binary segmentation review.", ["threshold", "binary", "segmentation"]),
        new("EdgeDetection", "Edge Detection", "vision", "Defines edge extraction metadata for dry-run validation.", ["edge", "gradient", "contour"]),
        new("ShapeMatching", "Shape Matching", "matching", "Finds a shape by catalog template metadata.", ["shape", "matching", "template"]),
        new("DeepLearning", "Deep Learning", "ai", "Runs model-based detection or classification when a model path is configured.", ["model", "detection", "wire", "defect"]),
        new("SemanticSegmentation", "Semantic Segmentation", "ai", "Reviews segmentation model metadata without loading model files.", ["segmentation", "model", "mask"]),
        new("SurfaceDefectDetection", "Surface Defect Detection", "ai", "Traditional surface defect metadata without loading model files.", ["defect", "surface", "scratch"]),
        new("CircleMeasurement", "Circle Measurement", "measurement", "Measures circle center/radius features.", ["circle", "hole", "diameter"]),
        new("Measurement", "Measurement", "measurement", "Measures distance between points or features.", ["distance", "spacing", "hole", "measurement"]),
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
                ["Detections", "Classes", "Scores", "TopClassLabel", "TopClassConfidence", "ClassificationResult"],
                ["Image"],
                [
                    new("ModelPath", "file", true, "Configured model artifact path; not loaded by read-only agent tools."),
                    new("Confidence", "double", false, "Detection confidence threshold."),
                    new("ModelId", "string", false, "Catalog model id metadata.")
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
                ["Distance"],
                ["Image", "PointA", "PointB"],
                [
                    new("X1", "int", false, "Start point X."),
                    new("Y1", "int", false, "Start point Y."),
                    new("X2", "int", false, "End point X."),
                    new("Y2", "int", false, "End point Y."),
                    new("MeasureType", "enum", false, "Measurement mode.")
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
