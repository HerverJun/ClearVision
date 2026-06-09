using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

internal interface IVisionAgentOperatorContractCatalog
{
    IReadOnlyCollection<string> OperatorTypes { get; }

    IReadOnlyCollection<VisionAgentOperatorContract> Operators { get; }

    bool TryGet(string operatorType, out VisionAgentOperatorContract contract);

    string CanonicalizeOperatorType(string operatorType);
}

internal sealed class VisionAgentOperatorContractCatalog : IVisionAgentOperatorContractCatalog
{
    private static readonly IReadOnlyDictionary<string, string> LegacyAgentAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MeasureDistance"] = nameof(OperatorType.Measurement),
            ["TemplateMatch"] = nameof(OperatorType.TemplateMatching)
        };

    private readonly IReadOnlyDictionary<string, VisionAgentOperatorContract> _contracts;

    public VisionAgentOperatorContractCatalog()
        : this(new OperatorFactory())
    {
    }

    public VisionAgentOperatorContractCatalog(IOperatorFactory operatorFactory)
    {
        var contracts = operatorFactory.GetAllMetadata()
            .Select(ToContract)
            .GroupBy(item => item.OperatorType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var supplemental in SupplementalContracts())
        {
            contracts.TryAdd(supplemental.OperatorType, supplemental);
        }

        _contracts = contracts;
    }

    public IReadOnlyCollection<string> OperatorTypes => _contracts.Keys.ToList();

    public IReadOnlyCollection<VisionAgentOperatorContract> Operators => _contracts.Values.ToList();

    public bool TryGet(string operatorType, out VisionAgentOperatorContract contract)
    {
        var canonical = CanonicalizeOperatorType(operatorType);
        return _contracts.TryGetValue(canonical, out contract!);
    }

    public string CanonicalizeOperatorType(string operatorType)
    {
        var cleaned = string.IsNullOrWhiteSpace(operatorType) ? string.Empty : operatorType.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        if (LegacyAgentAliases.TryGetValue(cleaned, out var alias))
        {
            return alias;
        }

        if (Enum.TryParse<OperatorType>(cleaned, ignoreCase: true, out var parsed))
        {
            return OperatorTypeAliasResolver.Resolve(parsed).ToString();
        }

        return cleaned;
    }

    private static VisionAgentOperatorContract ToContract(OperatorMetadata metadata)
    {
        return new VisionAgentOperatorContract(
            metadata.Type.ToString(),
            metadata.DisplayName,
            metadata.Category,
            metadata.Description,
            metadata.InputPorts.Select(ToPort).ToList(),
            metadata.OutputPorts.Select(ToPort).ToList(),
            metadata.Parameters.Select(ToParameter).ToList());
    }

    private static VisionAgentPortContract ToPort(PortDefinition port)
    {
        return new VisionAgentPortContract(
            port.Name,
            port.DisplayName,
            port.DataType,
            port.IsRequired,
            port.Description ?? string.Empty);
    }

    private static VisionAgentParameterContract ToParameter(ParameterDefinition parameter)
    {
        return new VisionAgentParameterContract(
            parameter.Name,
            parameter.DisplayName,
            parameter.DataType,
            parameter.IsRequired,
            parameter.DefaultValue,
            parameter.MinValue,
            parameter.MaxValue,
            parameter.Description ?? string.Empty,
            parameter.Options?
                .Select(option => new ParameterOption
                {
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList());
    }

    private static IEnumerable<VisionAgentOperatorContract> SupplementalContracts()
    {
        yield return new VisionAgentOperatorContract(
            "DeepLearning",
            "深度学习",
            "AI检测",
            "AI 深度学习推理，支持 YOLO 系列模型，用于缺陷检测和目标分类。",
            [
                Port("Image", "输入图像", PortDataType.Image, true)
            ],
            [
                Port("Image", "结果图像", PortDataType.Image, false),
                Port("OriginalImage", "原始图像", PortDataType.Image, false),
                Port("DetectionList", "检测列表", PortDataType.DetectionList, false),
                Port("Defects", "缺陷列表", PortDataType.DetectionList, false),
                Port("DefectCount", "缺陷数量", PortDataType.Integer, false),
                Port("Objects", "目标列表", PortDataType.DetectionList, false),
                Port("ObjectCount", "目标数量", PortDataType.Integer, false)
            ],
            [
                Param("ModelPath", "模型路径", "file", true, ""),
                Param("Confidence", "置信度阈值", "double", false, 0.5, 0.0, 1.0),
                Param("ModelVersion", "YOLO版本", "enum", false, "Auto"),
                Param("InputSize", "输入尺寸", "int", false, 640, 320, 1280),
                Param("UseGpu", "使用GPU", "bool", false, true),
                Param("GpuDeviceId", "GPU设备ID", "int", false, 0, 0, 15),
                Param("TargetClasses", "目标类别", "string", false, ""),
                Param("ModelId", "Model Id", "string", false, ""),
                Param("ModelCatalogPath", "Model Catalog Path", "file", false, "")
            ]);

        yield return new VisionAgentOperatorContract(
            "DetectionSequenceJudge",
            "检测顺序判定",
            "AI 检测",
            "对检测结果排序，并与期望标签序列进行比对。",
            [
                Port("Detections", "检测结果", PortDataType.DetectionList, true),
                Port("SlotPoints", "槽位点", PortDataType.PointList, false),
                Port("PerspectiveSrcPoints", "透视源点", PortDataType.PointList, false),
                Port("PerspectiveDstPoints", "透视目标点", PortDataType.PointList, false)
            ],
            [
                Port("IsMatch", "是否匹配", PortDataType.Boolean, false),
                Port("ActualOrder", "实际顺序", PortDataType.Any, false),
                Port("Count", "数量", PortDataType.Integer, false),
                Port("MissingLabels", "缺失标签", PortDataType.Any, false),
                Port("DuplicateLabels", "重复标签", PortDataType.Any, false),
                Port("SortedDetections", "排序后检测", PortDataType.DetectionList, false),
                Port("Assignment", "分配结果", PortDataType.Any, false),
                Port("UnassignedDetections", "未分配检测", PortDataType.DetectionList, false),
                Port("Diagnostics", "诊断信息", PortDataType.Any, false),
                Port("Message", "消息", PortDataType.String, false)
            ],
            [
                Param("ExpectedLabels", "期望标签序列", "string", true, ""),
                Param("SortBy", "排序字段", "enum", false, "CenterX"),
                Param("Direction", "排序方向", "enum", false, "Ascending"),
                Param("ExpectedCount", "期望数量", "int", false, 0, 0, 256),
                Param("MinConfidence", "最低置信度", "double", false, 0.0, 0.0, 1.0)
            ]);
    }

    private static VisionAgentPortContract Port(
        string name,
        string displayName,
        PortDataType dataType,
        bool required)
    {
        return new VisionAgentPortContract(name, displayName, dataType, required, string.Empty);
    }

    private static VisionAgentParameterContract Param(
        string name,
        string displayName,
        string dataType,
        bool required,
        object? defaultValue,
        object? minValue = null,
        object? maxValue = null)
    {
        return new VisionAgentParameterContract(
            name,
            displayName,
            dataType,
            required,
            defaultValue,
            minValue,
            maxValue,
            string.Empty,
            null);
    }
}

internal sealed record VisionAgentOperatorContract(
    string OperatorType,
    string DisplayName,
    string Category,
    string Description,
    IReadOnlyList<VisionAgentPortContract> InputPorts,
    IReadOnlyList<VisionAgentPortContract> OutputPorts,
    IReadOnlyList<VisionAgentParameterContract> Parameters);

internal sealed record VisionAgentPortContract(
    string Name,
    string DisplayName,
    PortDataType DataType,
    bool IsRequired,
    string Description);

internal sealed record VisionAgentParameterContract(
    string Name,
    string DisplayName,
    string DataType,
    bool IsRequired,
    object? DefaultValue,
    object? MinValue,
    object? MaxValue,
    string Description,
    IReadOnlyList<ParameterOption>? Options);
