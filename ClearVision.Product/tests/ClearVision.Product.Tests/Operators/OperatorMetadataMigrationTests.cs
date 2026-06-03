using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Tests.Operators;

[Trait("Category", "Sprint7_AiEvolution")]
public class OperatorMetadataMigrationTests
{
    [Fact]
    public void AllOperatorTypes_ShouldHaveMetadataAfterMigration()
    {
        var factory = new OperatorFactory();
        var allTypes = Enum.GetValues<OperatorType>();
        var missing = new List<OperatorType>();
        var missingDisplayName = new List<OperatorType>();
        var missingPorts = new List<OperatorType>();

        foreach (var type in allTypes)
        {
            var metadata = factory.GetMetadata(type);
            if (metadata == null)
            {
                missing.Add(type);
                continue;
            }

            if (string.IsNullOrWhiteSpace(metadata.DisplayName))
            {
                missingDisplayName.Add(type);
            }

            if (metadata.OutputPorts.Count == 0 && metadata.InputPorts.Count == 0)
            {
                missingPorts.Add(type);
            }
        }

        Assert.True(
            missing.Count == 0,
            $"Missing metadata for: {string.Join(", ", missing)}");

        Assert.True(
            missingDisplayName.Count == 0,
            $"Metadata missing display name for: {string.Join(", ", missingDisplayName)}");

        Assert.True(
            missingPorts.Count == 0,
            $"Metadata missing input/output ports for: {string.Join(", ", missingPorts)}");
    }

    [Fact]
    public void GetAllMetadata_ShouldExcludeLegacyAliasTypes()
    {
        var factory = new OperatorFactory();
        var all = factory.GetAllMetadata().Select(m => m.Type).ToHashSet();

        Assert.DoesNotContain(OperatorType.Preprocessing, all);
        Assert.DoesNotContain(OperatorType.GaussianBlur, all);
        Assert.DoesNotContain(OperatorType.OnnxInference, all);
        Assert.DoesNotContain(OperatorType.ModbusRtuCommunication, all);
    }

    [Fact]
    public void MetadataCatalog_ShouldPreferChineseDisplayNameAndCategory()
    {
        var factory = new OperatorFactory();
        var metadataByType = factory.GetAllMetadata().ToDictionary(m => m.Type, m => m);

        Assert.Equal("滤波", metadataByType[OperatorType.Filtering].DisplayName);
        Assert.Equal("预处理", metadataByType[OperatorType.Filtering].Category);

        Assert.Equal("边缘检测", metadataByType[OperatorType.EdgeDetection].DisplayName);
        Assert.Equal("特征提取", metadataByType[OperatorType.EdgeDetection].Category);

        Assert.Equal("深度学习", metadataByType[OperatorType.DeepLearning].DisplayName);
        Assert.Equal("AI检测", metadataByType[OperatorType.DeepLearning].Category);
    }

    [Fact]
    public void MetadataCatalog_ShouldUseSupportedOperatorLibraryCategories()
    {
        var supportedCategories = new HashSet<string>
        {
            "3D",
            "AI检测",
            "变量",
            "标定",
            "采集",
            "测量",
            "拆分组合",
            "定位",
            "辅助",
            "检测",
            "控制",
            "流程控制",
            "逻辑工具",
            "匹配定位",
            "频域",
            "区域处理",
            "识别",
            "输出",
            "数据",
            "数据处理",
            "特征提取",
            "通信",
            "通用",
            "图像处理",
            "纹理",
            "颜色处理",
            "预处理"
        };

        var metadataByType = new OperatorFactory()
            .GetAllMetadata()
            .ToDictionary(m => m.Type, m => m);

        var unsupported = metadataByType.Values
            .Where(metadata => !supportedCategories.Contains(metadata.Category))
            .Select(metadata => $"{metadata.Type}:{metadata.Category}")
            .OrderBy(item => item)
            .ToList();

        Assert.True(
            unsupported.Count == 0,
            $"Unsupported operator library categories: {string.Join(", ", unsupported)}");

        Assert.Equal("逻辑工具", metadataByType[OperatorType.Comparator].Category);
        Assert.Equal("特征提取", metadataByType[OperatorType.SubpixelEdgeDetection].Category);
        Assert.Equal("标定", metadataByType[OperatorType.HandEyeCalibration].Category);
        Assert.Equal("手眼标定", metadataByType[OperatorType.HandEyeCalibration].DisplayName);
        Assert.Equal("标定", metadataByType[OperatorType.HandEyeCalibrationValidator].Category);
        Assert.Equal("手眼标定验证", metadataByType[OperatorType.HandEyeCalibrationValidator].DisplayName);
    }
}
