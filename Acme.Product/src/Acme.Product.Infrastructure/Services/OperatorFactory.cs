using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Services;
using Acme.Product.Core.ValueObjects;

namespace Acme.Product.Infrastructure.Services;

/// <summary>
/// 算子工厂实现
/// </summary>
public class OperatorFactory : IOperatorFactory
{
    private readonly Dictionary<OperatorType, OperatorMetadata> _metadata = new();

    public OperatorFactory()
    {
        InitializeDefaultOperators();
    }

    public Operator CreateOperator(OperatorType type, string name, double x, double y)
    {
        var op = new Operator(name, type, x, y);

        // 根据算子类型添加默认端口和参数
        switch (type)
        {
            case OperatorType.ImageAcquisition:
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "ImagePath",
                    "图像路径",
                    "要加载的图像文件路径",
                    "string",
                    "",
                    null,
                    null,
                    false));
                break;

            case OperatorType.Filtering:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "KernelSize",
                    "核大小",
                    "高斯核大小（奇数）",
                    "int",
                    5,
                    1,
                    31,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "SigmaX",
                    "X方向标准差",
                    "高斯核X方向标准差",
                    "double",
                    1.0,
                    0.1,
                    10.0,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "SigmaY",
                    "Y方向标准差",
                    "高斯核Y方向标准差(0表示与X相同)",
                    "double",
                    0.0,
                    0.0,
                    10.0,
                    false));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "BorderType",
                    "边界填充方式",
                    "0=Constant,1=Replicate,2=Reflect,3=Wrap,4=Default",
                    "int",
                    4,
                    0,
                    4,
                    false));
                break;

            case OperatorType.EdgeDetection:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddOutputPort("Edges", PortDataType.Image);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Threshold1",
                    "低阈值",
                    "Canny边缘检测低阈值",
                    "double",
                    50.0,
                    0.0,
                    255.0,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Threshold2",
                    "高阈值",
                    "Canny边缘检测高阈值",
                    "double",
                    150.0,
                    0.0,
                    255.0,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "EnableGaussianBlur",
                    "启用高斯模糊",
                    "是否在Canny前进行高斯模糊降噪",
                    "bool",
                    true,
                    null,
                    null,
                    false));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "GaussianKernelSize",
                    "高斯核大小",
                    "预处理高斯模糊的核大小",
                    "int",
                    5,
                    3,
                    15,
                    false));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "ApertureSize",
                    "Sobel孔径大小",
                    "Sobel算子的孔径大小",
                    "int",
                    3,
                    3,
                    7,
                    false));
                break;

            case OperatorType.Thresholding:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Threshold",
                    "阈值",
                    "二值化阈值",
                    "double",
                    127.0,
                    0.0,
                    255.0,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "MaxValue",
                    "最大值",
                    "二值化最大值",
                    "double",
                    255.0,
                    0.0,
                    255.0,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Type",
                    "阈值类型",
                    "0=Binary, 1=BinaryInv, 2=Trunc, 3=ToZero, 4=ToZeroInv",
                    "int",
                    0,
                    0,
                    4,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "UseOtsu",
                    "使用Otsu自动阈值",
                    "是否使用Otsu算法自动计算最优阈值",
                    "bool",
                    false,
                    null,
                    null,
                    false));
                break;

            case OperatorType.Morphology:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Operation",
                    "操作类型",
                    "Erode/Dilate/Open/Close",
                    "enum",
                    "Erode",
                    null,
                    null,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "KernelSize",
                    "核大小",
                    "结构元素大小",
                    "int",
                    3,
                    1,
                    21,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Iterations",
                    "迭代次数",
                    "操作重复次数",
                    "int",
                    1,
                    1,
                    10,
                    true));
                break;

            case OperatorType.BlobAnalysis:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddOutputPort("Blobs", PortDataType.Contour);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "MinArea",
                    "最小面积",
                    "Blob最小面积",
                    "int",
                    100,
                    0,
                    null,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "MaxArea",
                    "最大面积",
                    "Blob最大面积",
                    "int",
                    100000,
                    0,
                    null,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Color",
                    "目标颜色",
                    "White/Black",
                    "enum",
                    "White",
                    null,
                    null,
                    true));
                break;

            case OperatorType.TemplateMatching:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddInputPort("Template", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddOutputPort("Position", PortDataType.Point);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Method",
                    "匹配方法",
                    "NCC/SQDiff",
                    "enum",
                    "NCC",
                    null,
                    null,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Threshold",
                    "匹配阈值",
                    "匹配分数阈值",
                    "double",
                    0.8,
                    0.0,
                    1.0,
                    true));
                break;

            case OperatorType.Measurement:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddOutputPort("Distance", PortDataType.Float);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "MeasureType",
                    "测量类型",
                    "PointToPoint/Horizontal/Vertical",
                    "enum",
                    "PointToPoint",
                    null,
                    null,
                    true));
                break;

            case OperatorType.ContourDetection:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "MinArea",
                    "最小面积",
                    "轮廓最小面积",
                    "int",
                    100,
                    0,
                    null,
                    true));
                break;

            case OperatorType.CodeRecognition:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddOutputPort("Text", PortDataType.String);
                break;

            case OperatorType.DeepLearning:
                op.AddInputPort("Image", PortDataType.Image, true);
                op.AddOutputPort("Image", PortDataType.Image);
                op.AddOutputPort("Defects", PortDataType.Contour);
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "ModelPath",
                    "模型路径",
                    "模型文件路径",
                    "file",
                    "",
                    null,
                    null,
                    true));
                op.AddParameter(new Parameter(
                    Guid.NewGuid(),
                    "Confidence",
                    "置信度",
                    "置信度阈值",
                    "double",
                    0.5,
                    0.0,
                    1.0,
                    true));
                break;

            case OperatorType.ResultOutput:
                op.AddInputPort("Image", PortDataType.Image, false);
                op.AddInputPort("Result", PortDataType.Any, false);
                op.AddOutputPort("Output", PortDataType.Any);
                break;

            default:
                // 为其他类型添加通用输入输出端口
                op.AddInputPort("Input", PortDataType.Any, false);
                op.AddOutputPort("Output", PortDataType.Any);
                break;
        }

        return op;
    }

    public OperatorMetadata? GetMetadata(OperatorType type)
    {
        return _metadata.TryGetValue(type, out var metadata) ? metadata : null;
    }

    public IEnumerable<OperatorMetadata> GetAllMetadata()
    {
        return _metadata.Values;
    }

    public IEnumerable<OperatorType> GetSupportedOperatorTypes()
    {
        return _metadata.Keys;
    }

    public void RegisterOperator(OperatorMetadata metadata)
    {
        _metadata[metadata.Type] = metadata;
    }

    private void InitializeDefaultOperators()
    {
        // 图像采集
        _metadata[OperatorType.ImageAcquisition] = new OperatorMetadata
        {
            Type = OperatorType.ImageAcquisition,
            DisplayName = "图像采集",
            Description = "从文件或相机采集图像",
            Category = "输入",
            IconName = "camera",
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "sourceType", DisplayName = "采集源", DataType = "enum", DefaultValue = "camera", Options = new List<ParameterOption> { new() { Label = "相机", Value = "camera" }, new() { Label = "文件", Value = "file" } } },
                new() { Name = "filePath", DisplayName = "文件路径", DataType = "file", DefaultValue = "" },
                new() { Name = "exposureTime", DisplayName = "曝光时间", DataType = "int", DefaultValue = 5000, MinValue = 100, MaxValue = 1000000 },
                new() { Name = "gain", DisplayName = "增益", DataType = "float", DefaultValue = 1.0, MinValue = 0.0, MaxValue = 24.0 }
            }
        };

        // 预处理
        _metadata[OperatorType.Preprocessing] = new OperatorMetadata
        {
            Type = OperatorType.Preprocessing,
            DisplayName = "预处理",
            Description = "图像预处理操作",
            Category = "预处理",
            IconName = "preprocess"
        };

        // 滤波
        _metadata[OperatorType.Filtering] = new OperatorMetadata
        {
            Type = OperatorType.Filtering,
            DisplayName = "滤波",
            Description = "图像滤波降噪",
            Category = "预处理",
            IconName = "filter",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "KernelSize", DisplayName = "核大小", DataType = "int", DefaultValue = 5, MinValue = 1, MaxValue = 31 },
                new() { Name = "SigmaX", DisplayName = "Sigma X", DataType = "double", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0 },
                new() { Name = "SigmaY", DisplayName = "Sigma Y", DataType = "double", DefaultValue = 0.0, MinValue = 0.0, MaxValue = 10.0 },
                new() { Name = "BorderType", DisplayName = "边界填充", DataType = "int", DefaultValue = 4, MinValue = 0, MaxValue = 4 }
            }
        };

        // 边缘检测
        _metadata[OperatorType.EdgeDetection] = new OperatorMetadata
        {
            Type = OperatorType.EdgeDetection,
            DisplayName = "边缘检测",
            Description = "检测图像边缘",
            Category = "特征提取",
            IconName = "edge",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image },
                new() { Name = "Edges", DisplayName = "边缘", DataType = PortDataType.Image }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Threshold1", DisplayName = "低阈值", DataType = "double", DefaultValue = 50.0, MinValue = 0.0, MaxValue = 255.0 },
                new() { Name = "Threshold2", DisplayName = "高阈值", DataType = "double", DefaultValue = 150.0, MinValue = 0.0, MaxValue = 255.0 },
                new() { Name = "EnableGaussianBlur", DisplayName = "启用高斯模糊", DataType = "bool", DefaultValue = true },
                new() { Name = "GaussianKernelSize", DisplayName = "高斯核大小", DataType = "int", DefaultValue = 5, MinValue = 3, MaxValue = 15 },
                new() { Name = "ApertureSize", DisplayName = "Sobel孔径", DataType = "int", DefaultValue = 3, MinValue = 3, MaxValue = 7 }
            }
        };

        // 二值化
        _metadata[OperatorType.Thresholding] = new OperatorMetadata
        {
            Type = OperatorType.Thresholding,
            DisplayName = "二值化",
            Description = "图像阈值分割",
            Category = "预处理",
            IconName = "threshold",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Threshold", DisplayName = "阈值", DataType = "double", DefaultValue = 127.0, MinValue = 0.0, MaxValue = 255.0 },
                new() { Name = "MaxValue", DisplayName = "最大值", DataType = "double", DefaultValue = 255.0, MinValue = 0.0, MaxValue = 255.0 },
                new() { Name = "Type", DisplayName = "类型", DataType = "int", DefaultValue = 0, MinValue = 0, MaxValue = 4 },
                new() { Name = "UseOtsu", DisplayName = "使用Otsu", DataType = "bool", DefaultValue = false }
            }
        };

        // 形态学
        _metadata[OperatorType.Morphology] = new OperatorMetadata
        {
            Type = OperatorType.Morphology,
            DisplayName = "形态学",
            Description = "形态学操作（腐蚀、膨胀、开闭运算）",
            Category = "预处理",
            IconName = "morphology",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Operation", DisplayName = "操作类型", DataType = "string", DefaultValue = "Erode" },
                new() { Name = "KernelSize", DisplayName = "核大小", DataType = "int", DefaultValue = 3, MinValue = 1, MaxValue = 21 },
                new() { Name = "KernelShape", DisplayName = "核形状", DataType = "string", DefaultValue = "Rect" },
                new() { Name = "Iterations", DisplayName = "迭代次数", DataType = "int", DefaultValue = 1, MinValue = 1, MaxValue = 10 },
                new() { Name = "AnchorX", DisplayName = "锚点X", DataType = "int", DefaultValue = -1 },
                new() { Name = "AnchorY", DisplayName = "锚点Y", DataType = "int", DefaultValue = -1 }
            }
        };

        // Blob分析

        // Blob分析
        _metadata[OperatorType.BlobAnalysis] = new OperatorMetadata
        {
            Type = OperatorType.BlobAnalysis,
            DisplayName = "Blob分析",
            Description = "连通区域分析",
            Category = "特征提取",
            IconName = "blob",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "标记图像", DataType = PortDataType.Image },
                new() { Name = "Blobs", DisplayName = "Blob数据", DataType = PortDataType.Contour }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "MinArea", DisplayName = "最小面积", DataType = "int", DefaultValue = 100, MinValue = 0 },
                new() { Name = "MaxArea", DisplayName = "最大面积", DataType = "int", DefaultValue = 100000, MinValue = 0 },
                new() { Name = "Color", DisplayName = "目标颜色", DataType = "enum", DefaultValue = "White", Options = new List<ParameterOption> { new() { Label = "白色", Value = "White" }, new() { Label = "黑色", Value = "Black" } } }
            }
        };

        // 模板匹配
        _metadata[OperatorType.TemplateMatching] = new OperatorMetadata
        {
            Type = OperatorType.TemplateMatching,
            DisplayName = "模板匹配",
            Description = "图像模板匹配",
            Category = "匹配定位",
            IconName = "template",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true },
                new() { Name = "Template", DisplayName = "模板图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "结果图像", DataType = PortDataType.Image },
                new() { Name = "Position", DisplayName = "匹配位置", DataType = PortDataType.Point }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Method", DisplayName = "匹配方法", DataType = "enum", DefaultValue = "NCC", Options = new List<ParameterOption> { new() { Label = "归一化相关 (NCC)", Value = "NCC" }, new() { Label = "平方差 (SQDiff)", Value = "SQDiff" } } },
                new() { Name = "Threshold", DisplayName = "匹配分数阈值", DataType = "double", DefaultValue = 0.8, MinValue = 0.1, MaxValue = 1.0 },
                new() { Name = "MaxMatches", DisplayName = "最大匹配数", DataType = "int", DefaultValue = 1, MinValue = 1, MaxValue = 100 }
            }
        };

        // 测量
        _metadata[OperatorType.Measurement] = new OperatorMetadata
        {
            Type = OperatorType.Measurement,
            DisplayName = "测量",
            Description = "几何测量",
            Category = "检测",
            IconName = "measure",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "结果图像", DataType = PortDataType.Image },
                new() { Name = "Distance", DisplayName = "测量距离", DataType = PortDataType.Float }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "X1", DisplayName = "起点X", DataType = "int", DefaultValue = 0 },
                new() { Name = "Y1", DisplayName = "起点Y", DataType = "int", DefaultValue = 0 },
                new() { Name = "X2", DisplayName = "终点X", DataType = "int", DefaultValue = 100 },
                new() { Name = "Y2", DisplayName = "终点Y", DataType = "int", DefaultValue = 100 },
                new() { Name = "MeasureType", DisplayName = "测量类型", DataType = "enum", DefaultValue = "PointToPoint", Options = new List<ParameterOption> { new() { Label = "点到点", Value = "PointToPoint" }, new() { Label = "水平", Value = "Horizontal" }, new() { Label = "垂直", Value = "Vertical" } } }
            }
        };

        // 轮廓检测
        _metadata[OperatorType.ContourDetection] = new OperatorMetadata
        {
            Type = OperatorType.ContourDetection,
            DisplayName = "轮廓检测",
            Description = "查找图像中的轮廓",
            Category = "特征提取",
            IconName = "contour",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "结果图像", DataType = PortDataType.Image }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Mode", DisplayName = "检索模式", DataType = "enum", DefaultValue = "External", Options = new List<ParameterOption> { new() { Label = "外部", Value = "External" }, new() { Label = "列表", Value = "List" }, new() { Label = "树", Value = "Tree" } } },
                new() { Name = "Method", DisplayName = "近似方法", DataType = "enum", DefaultValue = "Simple", Options = new List<ParameterOption> { new() { Label = "简单", Value = "Simple" }, new() { Label = "无", Value = "None" } } },
                new() { Name = "MinArea", DisplayName = "最小面积", DataType = "int", DefaultValue = 100 },
                new() { Name = "MaxArea", DisplayName = "最大面积", DataType = "int", DefaultValue = 100000 },
                new() { Name = "Threshold", DisplayName = "二值化阈值", DataType = "double", DefaultValue = 127.0 }
            }
        };

        // 条码识别
        _metadata[OperatorType.CodeRecognition] = new OperatorMetadata
        {
            Type = OperatorType.CodeRecognition,
            DisplayName = "条码识别",
            Description = "一维码/二维码识别",
            Category = "识别",
            IconName = "barcode",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "结果图像", DataType = PortDataType.Image },
                new() { Name = "Text", DisplayName = "识别内容", DataType = PortDataType.String }
            }
        };

        // 深度学习
        _metadata[OperatorType.DeepLearning] = new OperatorMetadata
        {
            Type = OperatorType.DeepLearning,
            DisplayName = "深度学习",
            Description = "AI缺陷检测",
            Category = "AI检测",
            IconName = "ai",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "结果图像", DataType = PortDataType.Image },
                new() { Name = "Defects", DisplayName = "缺陷列表", DataType = PortDataType.Contour }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "ModelPath", DisplayName = "模型路径", DataType = "file", DefaultValue = "" },
                new() { Name = "Confidence", DisplayName = "置信度阈值", DataType = "double", DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        // 结果输出
        _metadata[OperatorType.ResultOutput] = new OperatorMetadata
        {
            Type = OperatorType.ResultOutput,
            DisplayName = "结果输出",
            Description = "输出检测结果",
            Category = "输出",
            IconName = "output",
            InputPorts = new List<PortDefinition>
            {
                new() { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = false },
                new() { Name = "Result", DisplayName = "结果", DataType = PortDataType.Any, IsRequired = false }
            },
            OutputPorts = new List<PortDefinition>
            {
                new() { Name = "Output", DisplayName = "输出", DataType = PortDataType.Any }
            },
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Format", DisplayName = "输出格式", DataType = "enum", DefaultValue = "JSON", Options = new List<ParameterOption> { new() { Label = "JSON", Value = "JSON" }, new() { Label = "CSV", Value = "CSV" }, new() { Label = "Text", Value = "Text" } } },
                new() { Name = "SaveToFile", DisplayName = "保存到文件", DataType = "bool", DefaultValue = true }
            }
        };
    }
}
