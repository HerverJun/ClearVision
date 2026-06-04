namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentReadOnlyCatalog
{
    public static IReadOnlyList<OperatorCatalogItem> Operators { get; } =
    [
        new("ImageAcquisition", "Image Acquisition", "image", "Provides a camera/file image input node; agent read-only tools never capture frames.", ["image", "camera", "input", "acquisition"]),
        new("RoiManager", "ROI Manager", "roi", "Defines named regions of interest for downstream inspection.", ["roi", "region", "crop"]),
        new("TemplateMatching", "Template Matching", "matching", "Finds a known pattern by template and score.", ["template", "matching", "alignment", "position"]),
        new("DeepLearning", "Deep Learning", "ai", "Runs model-based detection or classification when a model path is configured.", ["model", "detection", "wire", "defect"]),
        new("CircleMeasurement", "Circle Measurement", "measurement", "Measures circle center/radius features.", ["circle", "hole", "diameter"]),
        new("MeasureDistance", "Measure Distance", "measurement", "Measures distance between points or features.", ["distance", "spacing", "hole", "measurement"]),
        new("ImageCompose", "Image Compose", "image", "Combines multiple images into one logical image.", ["compose", "multi-camera"]),
        new("ResultJudgment", "Result Judgment", "logic", "Evaluates pass/fail rules and tolerances.", ["judgment", "pass", "fail", "tolerance"]),
        new("ResultOutput", "Result Output", "output", "Publishes inspection result payloads to the configured output channel.", ["output", "result", "mes", "plc"])
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
                    new("CameraBindingId", "string", false, "Logical camera binding id supplied by engineer."),
                    new("FilePath", "string", false, "Optional offline file source; not read by read-only agent tools.")
                ]),
            ["TemplateMatching"] = new(
                "TemplateMatching",
                ["MatchResult", "Score", "Pose"],
                ["Image"],
                [
                    new("TemplatePath", "string", true, "Configured template artifact path; not loaded by read-only agent tools."),
                    new("MinScore", "double", false, "Minimum acceptable match score."),
                    new("MaxMatches", "int", false, "Maximum number of matches.")
                ]),
            ["DeepLearning"] = new(
                "DeepLearning",
                ["Detections", "Classes", "Scores"],
                ["Image"],
                [
                    new("ModelPath", "string", true, "Configured model artifact path; not loaded by read-only agent tools."),
                    new("ConfidenceThreshold", "double", false, "Detection confidence threshold.")
                ]),
            ["CircleMeasurement"] = new(
                "CircleMeasurement",
                ["Center", "Radius", "Diameter"],
                ["Image"],
                [
                    new("Roi", "string", false, "ROI name for the circle search."),
                    new("EdgePolarity", "string", false, "Expected edge polarity.")
                ]),
            ["MeasureDistance"] = new(
                "MeasureDistance",
                ["Distance"],
                ["PointA", "PointB"],
                [
                    new("Unit", "string", false, "Measurement unit."),
                    new("Tolerance", "string", false, "Allowed tolerance expression.")
                ]),
            ["ResultJudgment"] = new(
                "ResultJudgment",
                ["Result"],
                ["Input"],
                [
                    new("Rule", "string", false, "Pass/fail rule."),
                    new("Tolerance", "string", false, "Tolerance expression.")
                ]),
            ["ResultOutput"] = new(
                "ResultOutput",
                [],
                ["Input"],
                [
                    new("Channel", "string", false, "Output channel name.")
                ]),
            ["RoiManager"] = new(
                "RoiManager",
                ["RoiImage"],
                ["Image"],
                [
                    new("RoiName", "string", false, "Named ROI.")
                ]),
            ["ImageCompose"] = new(
                "ImageCompose",
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
            ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_roi", "Image"),
                Link("op_roi", "RoiImage", "op_detect", "Image"),
                Link("op_detect", "Detections", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ]),
        new(
            "template_matching_alignment",
            "template_matching",
            "Template matching alignment",
            ["template", "matching", "alignment", "position"],
            ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ]),
        new(
            "hole_distance_measurement",
            "measurement",
            "Hole distance measurement",
            ["hole", "distance", "spacing", "measurement"],
            ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_circle_a", "Image"),
                Link("op_cam", "Image", "op_circle_b", "Image"),
                Link("op_circle_a", "Center", "op_distance", "PointA"),
                Link("op_circle_b", "Center", "op_distance", "PointB"),
                Link("op_distance", "Distance", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
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
