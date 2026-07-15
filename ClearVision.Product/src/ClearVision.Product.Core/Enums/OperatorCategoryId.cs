namespace ClearVision.Product.Core.Enums;

/// <summary>
/// Stable product category identity for every built-in operator.
/// The enum name is the serialized/internal id; the Chinese label is presentation only.
/// </summary>
public enum OperatorCategoryId
{
    Acquisition,
    ImagePreprocessing,
    SegmentationAndRegion,
    FeatureExtraction,
    MatchingAndLocalization,
    DefectDetection,
    Measurement,
    CalibrationAndCoordinates,
    AiInference,
    PointCloud3D,
    DataProcessing,
    FlowControl,
    Communication,
    OutputAndAuxiliary
}

public sealed record OperatorCategoryDefinition(
    OperatorCategoryId Id,
    string DisplayName,
    int Order);

public static class OperatorCategoryCatalog
{
    public static IReadOnlyList<OperatorCategoryDefinition> All { get; } =
    [
        new(OperatorCategoryId.Acquisition, "采集", 1),
        new(OperatorCategoryId.ImagePreprocessing, "图像预处理", 2),
        new(OperatorCategoryId.SegmentationAndRegion, "分割与区域", 3),
        new(OperatorCategoryId.FeatureExtraction, "特征提取", 4),
        new(OperatorCategoryId.MatchingAndLocalization, "匹配与定位", 5),
        new(OperatorCategoryId.DefectDetection, "缺陷检测", 6),
        new(OperatorCategoryId.Measurement, "测量", 7),
        new(OperatorCategoryId.CalibrationAndCoordinates, "标定与坐标", 8),
        new(OperatorCategoryId.AiInference, "AI推理", 9),
        new(OperatorCategoryId.PointCloud3D, "3D点云", 10),
        new(OperatorCategoryId.DataProcessing, "数据处理", 11),
        new(OperatorCategoryId.FlowControl, "流程控制", 12),
        new(OperatorCategoryId.Communication, "通信", 13),
        new(OperatorCategoryId.OutputAndAuxiliary, "输出与辅助", 14)
    ];

    private static readonly IReadOnlyDictionary<OperatorCategoryId, OperatorCategoryDefinition> ById =
        All.ToDictionary(item => item.Id);

    public static string GetDisplayName(OperatorCategoryId id) =>
        ById.TryGetValue(id, out var definition)
            ? definition.DisplayName
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown operator category id.");

    public static int GetOrder(OperatorCategoryId id) =>
        ById.TryGetValue(id, out var definition)
            ? definition.Order
            : int.MaxValue;
}
