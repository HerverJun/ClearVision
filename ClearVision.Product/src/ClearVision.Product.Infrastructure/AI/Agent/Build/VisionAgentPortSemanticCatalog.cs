using System.Text.RegularExpressions;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal static class VisionPortSemantics
{
    public const string SourceImage = "source_image";
    public const string ProcessedImage = "processed_image";
    public const string PreviewImage = "preview_image";
    public const string BinaryMask = "binary_mask";
    public const string EdgeMask = "edge_mask";
    public const string DefectMask = "defect_mask";
    public const string PresenceCount = "presence_count";
    public const string DefectCount = "defect_count";
    public const string DefectArea = "defect_area";
    public const string DefectFeatures = "defect_features";
    public const string Label = "classification_label";
    public const string ClassificationDetails = "classification_details";
    public const string Confidence = "confidence";
    public const string Detections = "detections";
    public const string ObjectCount = "object_count";
    public const string IsMatch = "is_match";
    public const string TemplatePose = "template_pose";
    public const string TemplateMatches = "template_matches";
    public const string MeasurementValue = "measurement_value";
    public const string MeasurementDetails = "measurement_details";
    public const string MeasurementUnit = "measurement_unit";
    public const string MeasurementBundle = "measurement_bundle";
    public const string GeometryElement = "geometry_element";
    public const string SequenceDetails = "sequence_details";
    public const string DecodedText = "decoded_text";
    public const string CodeType = "code_type";
    public const string CodeCount = "code_count";
    public const string JudgmentResult = "judgment_result";
    public const string BooleanResult = "boolean_result";
    public const string StructuredData = "structured_data";
}

internal sealed class VisionAgentPortSemanticCatalog
{
    private static readonly IReadOnlySet<string> CriticalTargetPorts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ResultJudgment.Value",
            "ResultJudgment.Confidence",
            "ResultOutput.Result",
            "ResultOutput.Text",
            "ResultOutput.Data",
            "BlobAnalysis.Image",
            "DetectionSequenceJudge.Detections",
            "UnitConvert.Value",
            "Measurement.PointA",
            "Measurement.PointB"
        };

    public IReadOnlyList<string> OutputSemantics(string operatorType, string portName)
    {
        var key = $"{operatorType.Trim()}.{portName.Trim()}";
        return key switch
        {
            "ImageAcquisition.Image" => [VisionPortSemantics.SourceImage],
            "Thresholding.Image" or "AdaptiveThreshold.Image" => [VisionPortSemantics.BinaryMask],
            "EdgeDetection.Edges" => [VisionPortSemantics.EdgeMask],
            "EdgeDetection.Image" => [VisionPortSemantics.PreviewImage],
            "SurfaceDefectDetection.DefectMask" => [VisionPortSemantics.DefectMask],
            "SurfaceDefectDetection.DefectCount" => [VisionPortSemantics.DefectCount, VisionPortSemantics.PresenceCount],
            "SurfaceDefectDetection.DefectArea" => [VisionPortSemantics.DefectArea, VisionPortSemantics.MeasurementValue],
            "SurfaceDefectDetection.Diagnostics" => [VisionPortSemantics.DefectFeatures, VisionPortSemantics.StructuredData],
            "BlobAnalysis.BlobCount" => [VisionPortSemantics.PresenceCount],
            "BlobAnalysis.Blobs" or "BlobAnalysis.BlobFeatures" => [VisionPortSemantics.DefectFeatures, VisionPortSemantics.StructuredData],
            "DeepLearning.TopClassLabel" => [VisionPortSemantics.Label],
            "DeepLearning.TopClassConfidence" => [VisionPortSemantics.Confidence],
            "DeepLearning.ClassificationResult" or "DeepLearning.ClassificationTopK" => [VisionPortSemantics.ClassificationDetails, VisionPortSemantics.StructuredData],
            "DeepLearning.DetectionList" or "DeepLearning.Objects" or "DeepLearning.Defects" => [VisionPortSemantics.Detections, VisionPortSemantics.StructuredData],
            "DeepLearning.ObjectCount" => [VisionPortSemantics.ObjectCount, VisionPortSemantics.PresenceCount],
            "DeepLearning.DefectCount" => [VisionPortSemantics.DefectCount, VisionPortSemantics.PresenceCount],
            "TemplateMatching.IsMatch" => [VisionPortSemantics.IsMatch, VisionPortSemantics.BooleanResult],
            "TemplateMatching.Score" or "TemplateMatching.NormalizedScore" => [VisionPortSemantics.Confidence],
            "TemplateMatching.Position" => [VisionPortSemantics.TemplatePose],
            "TemplateMatching.Matches" => [VisionPortSemantics.TemplateMatches, VisionPortSemantics.TemplatePose, VisionPortSemantics.StructuredData],
            "TemplateMatching.MatchCount" => [VisionPortSemantics.PresenceCount],
            "DetectionSequenceJudge.IsMatch" => [VisionPortSemantics.IsMatch, VisionPortSemantics.BooleanResult],
            "DetectionSequenceJudge.ActualOrder" or "DetectionSequenceJudge.Assignment" or "DetectionSequenceJudge.Diagnostics" => [VisionPortSemantics.SequenceDetails, VisionPortSemantics.StructuredData],
            "DetectionSequenceJudge.Count" => [VisionPortSemantics.PresenceCount],
            "CodeRecognition.Text" => [VisionPortSemantics.DecodedText],
            "CodeRecognition.CodeType" => [VisionPortSemantics.CodeType, VisionPortSemantics.StructuredData],
            "CodeRecognition.CodeCount" => [VisionPortSemantics.CodeCount, VisionPortSemantics.PresenceCount],
            "Measurement.Distance" or
            "Measurement.Angle" or
            "Measurement.Value" or
            "CircleMeasurement.Radius" or
            "CircleMeasurement.Circularity" or
            "LineMeasurement.Length" or
            "LineMeasurement.Angle" or
            "ContourMeasurement.Area" or
            "ContourMeasurement.Perimeter" or
            "AngleMeasurement.Angle" or
            "WidthMeasurement.Width" or
            "WidthMeasurement.MeanWidth" or
            "GapMeasurement.MeanGap" or
            "ColorMeasurement.DeltaE" or
            "UnitConvert.Result" => [VisionPortSemantics.MeasurementValue],
            "CircleMeasurement.Center" or
            "CircleMeasurement.Circle" or
            "LineMeasurement.Line" or
            "Measurement.Intersection1" or
            "Measurement.Intersection2" => [VisionPortSemantics.GeometryElement],
            "Measurement.MeasurementEvidence" or
            "CircleMeasurement.MeasurementEvidence" or
            "LineMeasurement.MeasurementEvidence" => [VisionPortSemantics.MeasurementDetails, VisionPortSemantics.StructuredData],
            "UnitConvert.Unit" => [VisionPortSemantics.MeasurementUnit],
            "Measurement.Unit" => [VisionPortSemantics.MeasurementUnit],
            "Aggregator.Result" or "Aggregator.MergedList" =>
                [VisionPortSemantics.MeasurementBundle, VisionPortSemantics.StructuredData, VisionPortSemantics.MeasurementDetails],
            "ResultJudgment.JudgmentResult" => [VisionPortSemantics.JudgmentResult],
            "ResultJudgment.IsOk" or "ResultJudgment.ConditionResult" => [VisionPortSemantics.BooleanResult],
            "ResultJudgment.Details" => [VisionPortSemantics.StructuredData],
            _ when portName.Equals("Image", StringComparison.OrdinalIgnoreCase) =>
                [IsImagePreprocessor(operatorType) ? VisionPortSemantics.ProcessedImage : VisionPortSemantics.PreviewImage],
            _ => []
        };
    }

    public IReadOnlyList<string> AcceptedInputSemantics(
        string taskType,
        string operatorType,
        string portName,
        IReadOnlyCollection<string>? requiredOutputs = null,
        string? measurementTarget = null,
        string? acceptanceCriteria = null)
    {
        var routeKey = VisionTaskRouteContractRegistry.NormalizeTaskType(taskType);
        var key = $"{operatorType.Trim()}.{portName.Trim()}";
        if (key.Equals("BlobAnalysis.Image", StringComparison.OrdinalIgnoreCase))
        {
            return [
                VisionPortSemantics.DefectMask,
                VisionPortSemantics.BinaryMask,
                VisionPortSemantics.EdgeMask,
                VisionPortSemantics.ProcessedImage,
                VisionPortSemantics.SourceImage
            ];
        }

        if (key.Equals("DetectionSequenceJudge.Detections", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.Detections];
        }

        if (key.Equals("UnitConvert.Value", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.MeasurementValue, VisionPortSemantics.DefectArea];
        }

        if (key.Equals("Measurement.PointA", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Measurement.PointB", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.GeometryElement];
        }

        if (key.Equals("ResultJudgment.Confidence", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.Confidence];
        }

        if (key.Equals("ResultJudgment.Value", StringComparison.OrdinalIgnoreCase))
        {
            return JudgmentValueSemantics(routeKey, requiredOutputs, measurementTarget, acceptanceCriteria);
        }

        if (key.Equals("ResultOutput.Result", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.JudgmentResult, VisionPortSemantics.BooleanResult];
        }

        if (key.Equals("ResultOutput.Text", StringComparison.OrdinalIgnoreCase))
        {
            return routeKey.Equals("code_recognition", StringComparison.OrdinalIgnoreCase)
                ? [VisionPortSemantics.DecodedText]
                : [];
        }

        if (key.Equals("ResultOutput.Data", StringComparison.OrdinalIgnoreCase))
        {
            return ResultDataSemantics(routeKey, requiredOutputs, measurementTarget);
        }

        if (key.Equals("ResultOutput.Image", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.PreviewImage, VisionPortSemantics.ProcessedImage, VisionPortSemantics.SourceImage];
        }

        if (portName.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            return [
                VisionPortSemantics.ProcessedImage,
                VisionPortSemantics.BinaryMask,
                VisionPortSemantics.EdgeMask,
                VisionPortSemantics.DefectMask,
                VisionPortSemantics.SourceImage
            ];
        }

        if (operatorType.Equals("Aggregator", StringComparison.OrdinalIgnoreCase) &&
            portName.Equals("Value1", StringComparison.OrdinalIgnoreCase))
        {
            return [
                VisionPortSemantics.MeasurementValue,
                VisionPortSemantics.DefectArea,
                VisionPortSemantics.DefectCount,
                VisionPortSemantics.StructuredData
            ];
        }

        if (operatorType.Equals("Aggregator", StringComparison.OrdinalIgnoreCase) &&
            portName.Equals("Value2", StringComparison.OrdinalIgnoreCase))
        {
            return [VisionPortSemantics.MeasurementUnit, VisionPortSemantics.StructuredData];
        }

        return [];
    }

    public bool IsCriticalBusinessInput(string operatorType, string portName) =>
        CriticalTargetPorts.Contains($"{operatorType.Trim()}.{portName.Trim()}") ||
        operatorType.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase);

    public bool MatchesAny(
        string sourceOperatorType,
        string sourcePortName,
        IReadOnlyCollection<string> acceptedSemantics) =>
        OutputSemantics(sourceOperatorType, sourcePortName)
            .Any(source => acceptedSemantics.Contains(source, StringComparer.OrdinalIgnoreCase));

    public string? FirstMatchingSemantic(
        string sourceOperatorType,
        string sourcePortName,
        IReadOnlyCollection<string> acceptedSemantics) =>
        OutputSemantics(sourceOperatorType, sourcePortName)
            .FirstOrDefault(source => acceptedSemantics.Contains(source, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<string> JudgmentValueSemantics(
        string routeKey,
        IReadOnlyCollection<string>? requiredOutputs,
        string? measurementTarget,
        string? acceptanceCriteria)
    {
        return routeKey switch
        {
            "presence_detection" => [VisionPortSemantics.PresenceCount, VisionPortSemantics.BooleanResult, VisionPortSemantics.IsMatch],
            "attribute_classification" => [VisionPortSemantics.Label],
            "object_detection" => [VisionPortSemantics.ObjectCount, VisionPortSemantics.PresenceCount],
            "template_matching" => [VisionPortSemantics.IsMatch],
            "surface_defect_detection" when requiredOutputs?.Contains("defect_area", StringComparer.OrdinalIgnoreCase) == true ||
                                                  ContainsArea(measurementTarget) || ContainsArea(acceptanceCriteria) =>
                [VisionPortSemantics.DefectArea],
            "surface_defect_detection" => [VisionPortSemantics.DefectCount],
            "measurement" => [VisionPortSemantics.MeasurementValue],
            "sequence_judgment" => [VisionPortSemantics.IsMatch],
            "code_recognition" when MentionsExpectedCode(acceptanceCriteria) => [VisionPortSemantics.DecodedText],
            "code_recognition" => [VisionPortSemantics.CodeCount],
            _ => []
        };
    }

    private static IReadOnlyList<string> ResultDataSemantics(
        string routeKey,
        IReadOnlyCollection<string>? requiredOutputs,
        string? measurementTarget)
    {
        return routeKey switch
        {
            "presence_detection" => [VisionPortSemantics.PresenceCount, VisionPortSemantics.StructuredData],
            "attribute_classification" => [VisionPortSemantics.ClassificationDetails, VisionPortSemantics.Label],
            "object_detection" => [VisionPortSemantics.Detections, VisionPortSemantics.StructuredData],
            "template_matching" => [VisionPortSemantics.TemplateMatches, VisionPortSemantics.TemplatePose],
            "surface_defect_detection" when requiredOutputs?.Contains("defect_area", StringComparer.OrdinalIgnoreCase) == true || ContainsArea(measurementTarget) =>
                [VisionPortSemantics.DefectArea],
            "surface_defect_detection" => [VisionPortSemantics.DefectFeatures, VisionPortSemantics.DefectCount],
            "measurement" =>
                [VisionPortSemantics.MeasurementBundle, VisionPortSemantics.MeasurementValue, VisionPortSemantics.MeasurementDetails, VisionPortSemantics.StructuredData],
            "sequence_judgment" => [VisionPortSemantics.SequenceDetails],
            "code_recognition" => [VisionPortSemantics.CodeType, VisionPortSemantics.StructuredData],
            _ => []
        };
    }

    private static bool IsImagePreprocessor(string operatorType) => operatorType is
        "Preprocessing" or "Filtering" or "ColorConversion" or "MedianBlur" or
        "BilateralFilter" or "ImageResize" or "ImageCrop" or "ImageRotate" or
        "PerspectiveTransform" or "ClaheEnhancement" or "GaussianBlur" or
        "LaplacianSharpen" or "HistogramEqualization" or "RoiManager";

    private static bool ContainsArea(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("area", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("面积", StringComparison.OrdinalIgnoreCase));

    private static bool MentionsExpectedCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("码值", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("等于", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("equals", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(
                   value,
                   "(?:code|barcode|qr|条码|二维码|内容)\\s*(?:==|=|is|应为|必须为|为)\\s*(?:[\\\"“'][^\\\"”']+[\\\"”']|[A-Za-z0-9_.:/-]+)",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
