// OperatorMetadataLocalization.cs
// 算子元数据本地化
// 提供算子元数据的本地化映射与文本转换
// 作者：蘅芜君
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Adds compatibility search aliases without changing authoritative operator identity metadata.
/// </summary>
internal static class OperatorMetadataLocalization
{
    private static readonly IReadOnlyDictionary<OperatorType, string[]> LegacySearchAliases =
        new Dictionary<OperatorType, string[]>
        {
            [OperatorType.ImageAcquisition] = new[] { "Image Acquisition" },
            [OperatorType.Filtering] = new[] { "Filtering", "滤波处理" },
            [OperatorType.RoiManager] = new[] { "ROI Manager", "ROI管理器", "ROI管理" },
            [OperatorType.TemplateMatching] = new[] { "Template Matching" },
            [OperatorType.BlobAnalysis] = new[] { "Blob Analysis", "斑点分析" },
            [OperatorType.Thresholding] = new[] { "Thresholding", "二值化", "阈值分割" },
            [OperatorType.EdgeDetection] = new[] { "Edge Detection" },
            [OperatorType.ShapeMatching] = new[] { "Shape Matching" },
            [OperatorType.DeepLearning] = new[] { "Deep Learning", "深度学习检测", "深度学习推理" },
            [OperatorType.SemanticSegmentation] = new[] { "Semantic Segmentation" },
            [OperatorType.SurfaceDefectDetection] = new[] { "Surface Defect Detection" },
            [OperatorType.CircleMeasurement] = new[] { "Circle Measurement" },
            [OperatorType.Measurement] = new[] { "Measurement", "几何测量" },
            [OperatorType.UnitConvert] = new[] { "Unit Convert" },
            [OperatorType.DetectionSequenceJudge] = new[] { "线序判定", "序列判定" },
            [OperatorType.ImageAdd] = new[] { "Image Add", "图像叠加" },
            [OperatorType.ImageCompose] = new[] { "图像合成" },
            [OperatorType.ResultJudgment] = new[] { "Result Judgment" },
            [OperatorType.ResultOutput] = new[] { "Result Output" },
            [OperatorType.ModbusCommunication] = new[] { "Modbus Communication", "Modbus通信" },
            [OperatorType.HttpRequest] = new[] { "HTTP Request", "HTTP请求" },
            [OperatorType.ScriptOperator] = new[] { "Script Operator" },
            [OperatorType.GeoMeasurement] = new[] { "几何距离测量" },
            [OperatorType.TcpCommunication] = new[] { "TCP通讯" },
            [OperatorType.ForEach] = new[] { "循环处理" },
            [OperatorType.ArrayIndexer] = new[] { "数组索引" },
            [OperatorType.JsonExtractor] = new[] { "JSON提取" },
            [OperatorType.MathOperation] = new[] { "数学运算" },
            [OperatorType.MqttPublish] = new[] { "MQTT Publish", "MQTT发布" },
            [OperatorType.SiemensS7Communication] = new[] { "西门子S7" }
        };

    public static void Apply(IEnumerable<OperatorMetadata> metadataItems)
    {
        foreach (var metadata in metadataItems)
        {
            if (LegacySearchAliases.TryGetValue(metadata.Type, out var aliases))
            {
                metadata.Keywords = (metadata.Keywords ?? Array.Empty<string>())
                    .Concat(aliases)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }
}
