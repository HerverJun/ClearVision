using System.Collections;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using VisionCircleData = ClearVision.Product.Core.ValueObjects.CircleData;
using VisionDetectionList = ClearVision.Product.Core.ValueObjects.DetectionList;
using VisionDetectionResult = ClearVision.Product.Core.ValueObjects.DetectionResult;
using VisionLineData = ClearVision.Product.Core.ValueObjects.LineData;
using VisionPosition = ClearVision.Product.Core.ValueObjects.Position;
using VisionRegionPoint2f = ClearVision.Product.Core.ValueObjects.RegionPoint2f;

namespace ClearVision.Product.Application.Analysis;

internal static class AnalysisMapperHelpers
{
    public static bool TryGetValueIgnoreCase(
        IReadOnlyDictionary<string, object>? outputData,
        string key,
        out object? value)
    {
        value = null;
        if (outputData == null)
        {
            return false;
        }

        foreach (var pair in outputData)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    public static string ResolveStatus(OperatorExecutionResult result)
    {
        return result.IsSuccess ? "OK" : "Error";
    }

    public static double? TryReadDouble(IReadOnlyDictionary<string, object>? outputData, string key)
    {
        if (!TryGetValueIgnoreCase(outputData, key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            byte number => number,
            short number => number,
            int number => number,
            long number => number,
            float number => number,
            double number => number,
            decimal number => (double)number,
            _ when double.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    public static string? TryReadString(IReadOnlyDictionary<string, object>? outputData, string key)
    {
        if (!TryGetValueIgnoreCase(outputData, key, out var value) || value == null)
        {
            return null;
        }

        return value.ToString();
    }

    public static bool? TryReadBool(IReadOnlyDictionary<string, object>? outputData, string key)
    {
        if (!TryGetValueIgnoreCase(outputData, key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            bool booleanValue => booleanValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    public static object? TryReadObject(IReadOnlyDictionary<string, object>? outputData, string key)
    {
        return TryGetValueIgnoreCase(outputData, key, out var value) ? value : null;
    }

    public static Dictionary<string, object?> BuildMeta(params (string Key, object? Value)[] values)
    {
        return values
            .Where(item => item.Value != null)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsImageLikeKey(string key)
    {
        return key.Contains("Image", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Base64", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Bitmap", StringComparison.OrdinalIgnoreCase);
    }

    public static string InferDataType(string key, object? value)
    {
        if (value is VisionDetectionList)
        {
            return "DetectionList";
        }

        if (value is VisionDetectionResult)
        {
            return "DetectionResult";
        }

        if (value is VisionCircleData || HasShapeProperties(value, "CenterX", "CenterY", "Radius"))
        {
            return "CircleData";
        }

        if (value is VisionLineData
            || HasShapeProperties(value, "StartX", "StartY", "EndX", "EndY")
            || HasShapeProperties(value, "X1", "Y1", "X2", "Y2"))
        {
            return "LineData";
        }

        if (value is VisionPosition or VisionRegionPoint2f)
        {
            return "Point";
        }

        if (HasShapeProperties(value, "X", "Y", "Width", "Height"))
        {
            return "Rectangle";
        }

        if (HasShapeProperties(value, "X", "Y"))
        {
            return "Point";
        }

        if (value is bool)
        {
            return "Boolean";
        }

        if (value is byte or short or int or long)
        {
            return "Integer";
        }

        if (value is float or double or decimal)
        {
            return "Float";
        }

        if (key.Contains("Detection", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Object", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Defect", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Box", StringComparison.OrdinalIgnoreCase))
        {
            return value is IEnumerable and not string ? "DetectionList" : "DetectionResult";
        }

        if (value is string)
        {
            return "String";
        }

        return "Any";
    }

    private static bool HasShapeProperties(object? value, params string[] propertyNames)
    {
        if (value == null)
        {
            return false;
        }

        if (value is IDictionary dictionary)
        {
            var keys = dictionary.Keys
                .Cast<object?>()
                .Where(item => item != null)
                .Select(item => item!.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return propertyNames.All(name => keys.Contains(name));
        }

        var properties = value.GetType()
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return propertyNames.All(name => properties.Contains(name));
    }

    public static bool IsUsefulField(string key, object? value)
    {
        return value != null
            && !IsImageLikeKey(key)
            && !key.Equals("Diagnostics", StringComparison.OrdinalIgnoreCase)
            && !key.Equals("Output", StringComparison.OrdinalIgnoreCase);
    }

    public static AnalysisFieldDto ToField(
        string key,
        object? value,
        string? label = null,
        string? displayHint = null,
        string? dataType = null,
        string? variant = null,
        string? status = null)
    {
        return new AnalysisFieldDto
        {
            Key = key,
            Label = label ?? key,
            Value = value,
            DisplayHint = displayHint,
            Variant = variant,
            DataType = dataType ?? InferDataType(key, value),
            Status = status
        };
    }
}

public class OcrRecognitionAnalysisCardMapper : IAnalysisCardMapper
{
    public bool CanMap(OperatorType operatorType) => operatorType == OperatorType.OcrRecognition;

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        var text = AnalysisMapperHelpers.TryReadString(result.OutputData, "Text");
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var confidence = AnalysisMapperHelpers.TryReadObject(result.OutputData, "Confidence");
        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-recognition",
            Category = "recognition",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = "OCR 文本识别",
            Status = AnalysisMapperHelpers.ResolveStatus(result),
            Priority = 90,
            Fields =
            [
                new AnalysisFieldDto
                {
                    Key = "text",
                    Label = "识别文本",
                    Value = text,
                    DisplayHint = "code-text"
                }
            ],
            Meta = AnalysisMapperHelpers.BuildMeta(("confidence", confidence))
        };
    }
}

public class CodeRecognitionAnalysisCardMapper : IAnalysisCardMapper
{
    public bool CanMap(OperatorType operatorType) => operatorType == OperatorType.CodeRecognition;

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        var text = AnalysisMapperHelpers.TryReadString(result.OutputData, "Text");
        var codeType = AnalysisMapperHelpers.TryReadString(result.OutputData, "CodeType");
        var codeCount = AnalysisMapperHelpers.TryReadDouble(result.OutputData, "CodeCount")
            ?? AnalysisMapperHelpers.TryReadDouble(result.OutputData, "ResultCount");

        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(codeType) && codeCount is null)
        {
            yield break;
        }

        var fields = new List<AnalysisFieldDto>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            fields.Add(new AnalysisFieldDto
            {
                Key = "text",
                Label = "识别内容",
                Value = text,
                DisplayHint = "code-text"
            });
        }

        if (!string.IsNullOrWhiteSpace(codeType))
        {
            fields.Add(new AnalysisFieldDto
            {
                Key = "codeType",
                Label = "码制类型",
                Value = codeType,
                DisplayHint = "tag"
            });
        }

        if (codeCount is not null)
        {
            fields.Add(new AnalysisFieldDto
            {
                Key = "codeCount",
                Label = "识别数量",
                Value = codeCount
            });
        }

        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-recognition",
            Category = "recognition",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = "条码识别",
            Status = AnalysisMapperHelpers.ResolveStatus(result),
            Priority = 95,
            Fields = fields,
            Meta = AnalysisMapperHelpers.BuildMeta(("codes", AnalysisMapperHelpers.TryReadObject(result.OutputData, "Codes")))
        };
    }
}

public class CommunicationAnalysisCardMapper : IAnalysisCardMapper
{
    private static readonly HashSet<OperatorType> SupportedTypes =
    [
        OperatorType.ModbusCommunication,
        OperatorType.ModbusRtuCommunication,
        OperatorType.TcpCommunication,
        OperatorType.SerialCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.OmronFinsCommunication,
        OperatorType.HttpRequest,
        OperatorType.MqttPublish
    ];

    public bool CanMap(OperatorType operatorType) => SupportedTypes.Contains(operatorType);

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        if (result.OutputData == null || result.OutputData.Count == 0)
        {
            yield break;
        }

        var preferredKeys = new[]
        {
            "Success",
            "IsConnected",
            "StatusCode",
            "Response",
            "Value",
            "Topic",
            "Address",
            "LatencyMs",
            "RoundtripMs",
            "ErrorMessage"
        };

        var fields = preferredKeys
            .Where(key => AnalysisMapperHelpers.TryGetValueIgnoreCase(result.OutputData, key, out var value)
                && AnalysisMapperHelpers.IsUsefulField(key, value))
            .Select(key =>
            {
                AnalysisMapperHelpers.TryGetValueIgnoreCase(result.OutputData, key, out var value);
                return AnalysisMapperHelpers.ToField(key, value);
            })
            .ToList();

        if (fields.Count == 0)
        {
            fields = result.OutputData
                .Where(pair => AnalysisMapperHelpers.IsUsefulField(pair.Key, pair.Value))
                .Take(6)
                .Select(pair => AnalysisMapperHelpers.ToField(pair.Key, pair.Value))
                .ToList();
        }

        if (fields.Count == 0)
        {
            yield break;
        }

        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-communication",
            Category = "communication",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = $"{@operator.Type} Communication",
            Status = AnalysisMapperHelpers.ResolveStatus(result),
            Priority = 85,
            Fields = fields
        };
    }
}

public class DetectionSequenceJudgeAnalysisCardMapper : IAnalysisCardMapper
{
    public bool CanMap(OperatorType operatorType) => operatorType == OperatorType.DetectionSequenceJudge;

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        if (result.OutputData == null || result.OutputData.Count == 0)
        {
            yield break;
        }

        var isMatch = AnalysisMapperHelpers.TryReadBool(result.OutputData, "IsMatch");
        var status = !result.IsSuccess
            ? "Error"
            : isMatch == true
                ? "OK"
                : "NG";
        var message = AnalysisMapperHelpers.TryReadString(result.OutputData, "Message")
            ?? (isMatch == true ? "Sequence matched." : "Sequence did not match.");

        var fields = new List<AnalysisFieldDto>
        {
            AnalysisMapperHelpers.ToField(
                "IsMatch",
                isMatch == true ? "匹配" : "不匹配",
                label: "判定结果",
                dataType: "String",
                variant: "status",
                status: status),
            AnalysisMapperHelpers.ToField(
                "ExpectedLabels",
                ReadSequenceValue(result.OutputData, "ExpectedLabels"),
                label: "预期顺序",
                dataType: "Any",
                variant: "sequence"),
            AnalysisMapperHelpers.ToField(
                "ActualOrder",
                ReadSequenceValue(result.OutputData, "ActualOrder"),
                label: "实际顺序",
                dataType: "Any",
                variant: "sequence"),
            AnalysisMapperHelpers.ToField(
                "MissingLabels",
                ReadSequenceValue(result.OutputData, "MissingLabels"),
                label: "缺失标签",
                dataType: "Any",
                variant: "labels"),
            AnalysisMapperHelpers.ToField(
                "DuplicateLabels",
                ReadSequenceValue(result.OutputData, "DuplicateLabels"),
                label: "重复标签",
                dataType: "Any",
                variant: "labels")
        };

        foreach (var key in new[] { "ReceivedCount", "FilteredCount", "DetectionCount", "ExpectedCount", "RequiredMinConfidence", "RowCount" })
        {
            if (AnalysisMapperHelpers.TryGetValueIgnoreCase(result.OutputData, key, out var value)
                && AnalysisMapperHelpers.IsUsefulField(key, value))
            {
                fields.Add(AnalysisMapperHelpers.ToField(key, value, label: ToSequenceFieldLabel(key)));
            }
        }

        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-sequence-judgment",
            Category = "diagnostic",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = "线序判定",
            Status = status,
            Priority = status == "OK" ? 140 : 170,
            Message = message,
            Fields = fields,
            Meta = AnalysisMapperHelpers.BuildMeta(
                ("Message", message),
                ("Source", "DetectionSequenceJudge"))
        };
    }

    private static object ReadSequenceValue(IReadOnlyDictionary<string, object> outputData, string key)
    {
        return AnalysisMapperHelpers.TryReadObject(outputData, key) ?? Array.Empty<string>();
    }

    private static string ToSequenceFieldLabel(string key)
    {
        return key switch
        {
            "ReceivedCount" => "接收数量",
            "FilteredCount" => "过滤后数量",
            "DetectionCount" => "最终数量",
            "ExpectedCount" => "预期数量",
            "RequiredMinConfidence" => "最小置信度",
            "RowCount" => "行数",
            _ => key
        };
    }
}

public class DetectionAnalysisCardMapper : IAnalysisCardMapper
{
    private static readonly HashSet<OperatorType> SupportedTypes =
    [
        OperatorType.BlobAnalysis,
        OperatorType.ContourDetection,
        OperatorType.DeepLearning,
        OperatorType.OnnxInference,
        OperatorType.ColorDetection,
        OperatorType.RectangleDetection,
        OperatorType.SurfaceDefectDetection,
        OperatorType.BlobLabeling,
        OperatorType.AnomalyDetection,
        OperatorType.BoxNms,
        OperatorType.BoxFilter
    ];

    public bool CanMap(OperatorType operatorType) => SupportedTypes.Contains(operatorType);

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        if (result.OutputData == null || result.OutputData.Count == 0)
        {
            yield break;
        }

        var fields = new List<AnalysisFieldDto>();
        foreach (var key in new[] { "DetectionCount", "ObjectCount", "DefectCount", "BlobCount", "ContourCount", "RectangleCount" })
        {
            if (AnalysisMapperHelpers.TryGetValueIgnoreCase(result.OutputData, key, out var value)
                && AnalysisMapperHelpers.IsUsefulField(key, value))
            {
                fields.Add(AnalysisMapperHelpers.ToField(
                    key,
                    value,
                    label: ToDetectionFieldLabel(@operator.Type, key),
                    dataType: "Integer"));
            }
        }

        foreach (var key in new[] { "Detections", "Objects", "Defects", "Boxes", "Candidates", "SuppressedDetections" })
        {
            if (AnalysisMapperHelpers.TryGetValueIgnoreCase(result.OutputData, key, out var value)
                && AnalysisMapperHelpers.IsUsefulField(key, value))
            {
                fields.Add(AnalysisMapperHelpers.ToField(
                    key,
                    value,
                    label: ToDetectionFieldLabel(@operator.Type, key),
                    dataType: "DetectionList"));
            }
        }

        if (fields.Count == 0)
        {
            yield break;
        }

        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-detections",
            Category = "detection",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = ToDetectionCardTitle(@operator.Type),
            Status = result.IsSuccess ? "Info" : "Error",
            Priority = ToDetectionCardPriority(@operator.Type),
            Fields = fields
        };
    }

    private static string ToDetectionCardTitle(OperatorType operatorType)
    {
        return operatorType switch
        {
            OperatorType.DeepLearning or OperatorType.OnnxInference => $"{operatorType} Raw Detections",
            OperatorType.BoxFilter => "BoxFilter Candidates",
            OperatorType.BoxNms => "BoxNms Kept Detections",
            _ => $"{operatorType} Detections"
        };
    }

    private static int ToDetectionCardPriority(OperatorType operatorType)
    {
        return operatorType switch
        {
            OperatorType.BoxNms => 90,
            OperatorType.BoxFilter => 80,
            OperatorType.DeepLearning or OperatorType.OnnxInference => 70,
            _ => 75
        };
    }

    private static string ToDetectionFieldLabel(OperatorType operatorType, string key)
    {
        if (operatorType == OperatorType.BoxFilter && key.Equals("Detections", StringComparison.OrdinalIgnoreCase))
        {
            return "Candidates before NMS";
        }

        if (operatorType == OperatorType.BoxNms && key.Equals("Detections", StringComparison.OrdinalIgnoreCase))
        {
            return "Kept detections";
        }

        if (operatorType == OperatorType.BoxNms && key.Equals("SuppressedDetections", StringComparison.OrdinalIgnoreCase))
        {
            return "Suppressed detections";
        }

        if ((operatorType == OperatorType.DeepLearning || operatorType == OperatorType.OnnxInference)
            && (key.Equals("Objects", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Detections", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Defects", StringComparison.OrdinalIgnoreCase)))
        {
            return "Raw model candidates";
        }

        return key;
    }
}

public class GenericMeasurementAnalysisCardMapper : IAnalysisCardMapper
{
    private static readonly HashSet<OperatorType> SupportedTypes =
    [
        OperatorType.Measurement,
        OperatorType.CircleMeasurement,
        OperatorType.LineMeasurement,
        OperatorType.ContourMeasurement,
        OperatorType.AngleMeasurement,
        OperatorType.CaliperTool,
        OperatorType.PointLineDistance,
        OperatorType.LineLineDistance,
        OperatorType.GapMeasurement,
        OperatorType.GeoMeasurement,
        OperatorType.ColorMeasurement,
        OperatorType.Statistics
    ];

    public bool CanMap(OperatorType operatorType) => SupportedTypes.Contains(operatorType);

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        if (result.OutputData == null || result.OutputData.Count == 0)
        {
            yield break;
        }

        var fields = result.OutputData
            .Where(pair => AnalysisMapperHelpers.IsUsefulField(pair.Key, pair.Value))
            .Where(pair => AnalysisMapperHelpers.InferDataType(pair.Key, pair.Value) is "Integer" or "Float" or "Point" or "PointList" or "Rectangle" or "CircleData" or "LineData")
            .Take(8)
            .Select(pair => AnalysisMapperHelpers.ToField(pair.Key, pair.Value, displayHint: "measurement"))
            .ToList();

        if (fields.Count == 0)
        {
            yield break;
        }

        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-generic-measurement",
            Category = "measurement",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = $"{@operator.Type} Measurements",
            Status = AnalysisMapperHelpers.ResolveStatus(result),
            Priority = 70,
            Fields = fields
        };
    }
}

public class WidthMeasurementAnalysisCardMapper : IAnalysisCardMapper
{
    public bool CanMap(OperatorType operatorType) => operatorType == OperatorType.WidthMeasurement;

    public IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        var width = AnalysisMapperHelpers.TryReadDouble(result.OutputData, "Width");
        if (width is null)
        {
            yield break;
        }

        var fields = new List<AnalysisFieldDto>
        {
            new()
            {
                Key = "width",
                Label = "宽度",
                Value = width,
                Unit = "px",
                DisplayHint = "big-number"
            }
        };

        var minWidth = AnalysisMapperHelpers.TryReadDouble(result.OutputData, "MinWidth");
        if (minWidth is not null)
        {
            fields.Add(new AnalysisFieldDto
            {
                Key = "minWidth",
                Label = "最小宽度",
                Value = minWidth,
                Unit = "px"
            });
        }

        var maxWidth = AnalysisMapperHelpers.TryReadDouble(result.OutputData, "MaxWidth");
        if (maxWidth is not null)
        {
            fields.Add(new AnalysisFieldDto
            {
                Key = "maxWidth",
                Label = "最大宽度",
                Value = maxWidth,
                Unit = "px"
            });
        }

        yield return new AnalysisCardDto
        {
            Id = $"{@operator.Id:N}-measurement",
            Category = "measurement",
            SourceOperatorId = @operator.Id,
            SourceOperatorType = @operator.Type.ToString(),
            Title = "宽度测量",
            Status = AnalysisMapperHelpers.ResolveStatus(result),
            Priority = 100,
            Fields = fields,
            Meta = AnalysisMapperHelpers.BuildMeta(
                ("sampleCount", AnalysisMapperHelpers.TryReadObject(result.OutputData, "SampleCount")),
                ("refinedSampleCount", AnalysisMapperHelpers.TryReadObject(result.OutputData, "RefinedSampleCount")),
                ("direction", AnalysisMapperHelpers.TryReadObject(result.OutputData, "Direction")))
        };
    }
}
