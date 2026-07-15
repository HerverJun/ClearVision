// AIPromptBuilder.cs
// AI 提示词构建器 - Sprint 5
// 构建包含最新算子库信息的提示词，供 LLM 生成流程
// 作者：蘅芜君

using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Infrastructure.AI;

/// <summary>
/// Legacy AI 提示词构建器。
/// 仅保留给 Sprint 5 兼容链与历史测试使用，不再代表当前主链提示词策略。
/// 当前正式支持的提示词构建器为 <see cref="PromptBuilder"/>。
/// </summary>
[Obsolete("Legacy compatibility prompt builder only. Use PromptBuilder for the supported AI generation path.")]
public class AIPromptBuilder
{
    private readonly StringBuilder _prompt = new();
    private readonly List<PromptOperatorInfo> _operators = new();

    public AIPromptBuilder()
    {
        InitializeOperatorLibrary();
    }

    /// <summary>
    /// 初始化算子库元数据
    /// </summary>
    private void InitializeOperatorLibrary()
    {
        foreach (var metadata in new OperatorFactory()
                     .GetAllMetadata()
                     .Where(item => !item.DefaultHidden)
                     .OrderByDescending(item => ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                         item.Lifecycle,
                         item.ImageInputContracts))
                     .ThenBy(item => OperatorCategoryCatalog.GetOrder(item.CategoryId))
                     .ThenBy(item => item.DisplayName, StringComparer.Ordinal))
        {
            var parameters = metadata.Parameters
                .Select(parameter => new PromptParamInfo(
                    parameter.Name,
                    parameter.DataType,
                    parameter.Description ?? parameter.DisplayName,
                    parameter.IsRequired,
                    parameter.DefaultValue?.ToString() ?? string.Empty,
                    parameter.MinValue?.ToString() ?? string.Empty,
                    parameter.MaxValue?.ToString() ?? string.Empty,
                    parameter.Options?
                        .Select(option => option.Value?.ToString() ?? string.Empty)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray()))
                .ToArray();
            var contractSummary = ImageContractPresentationBuilder.Summarize(metadata.ImageInputContracts);
            var lifecycleDisclosure = ImageContractPresentationBuilder.RequiresAiDisclosure(
                metadata.Lifecycle,
                metadata.ImageInputContracts)
                ? string.Join(" ", new[]
                {
                    $"生命周期={metadata.Lifecycle}。{metadata.LifecycleNote}".Trim(),
                    contractSummary.ContractCount > 0 ? contractSummary.EvidenceSummary : string.Empty
                }.Where(item => !string.IsNullOrWhiteSpace(item)))
                : string.Empty;
            var qualityNote = $"质量轴: Execution={metadata.QualityState.Execution}; AlgorithmQuality={metadata.QualityState.AlgorithmQuality}; ProductionReadiness={metadata.QualityState.ProductionReadiness}; FieldValidation={metadata.QualityState.FieldValidation}.";
            var lifecycleNote = string.Join(" ", new[] { lifecycleDisclosure, qualityNote }.Where(item => !string.IsNullOrWhiteSpace(item)));

            _operators.Add(new PromptOperatorInfo(
                metadata.Type,
                metadata.DisplayName,
                metadata.Description,
                string.Join(", ", metadata.InputPorts.Select(port => $"{port.Name}:{port.DataType}")),
                string.Join(", ", metadata.OutputPorts.Select(port => $"{port.Name}:{port.DataType}")),
                parameters,
                lifecycleNote,
                metadata.Category));
        }
    }

    /// <summary>
    /// 添加系统提示词头
    /// </summary>
    public AIPromptBuilder WithSystemPrompt(string? customSystemPrompt = null)
    {
        _prompt.AppendLine("# ClearVision 工业视觉检测平台 - 流程生成助手");
        _prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(customSystemPrompt))
        {
            _prompt.AppendLine(customSystemPrompt);
        }
        else
        {
            _prompt.AppendLine("你是一个专业的工业视觉检测流程设计助手。你的任务是将用户的自然语言需求转换为结构化的 ClearVision 流程定义。");
        }

        _prompt.AppendLine();
        _prompt.AppendLine("## 输出格式");
        _prompt.AppendLine("你必须输出一个标准的 JSON 对象，格式如下：");
        _prompt.AppendLine("```json");
        _prompt.AppendLine("{");
        _prompt.AppendLine("  \"flowName\": \"流程名称\",");
        _prompt.AppendLine("  \"operators\": [");
        _prompt.AppendLine("    {");
        _prompt.AppendLine("      \"id\": \"guid\",");
        _prompt.AppendLine("      \"name\": \"算子名称\",");
        _prompt.AppendLine("      \"type\": \"算子类型枚举\",");
        _prompt.AppendLine("      \"parameters\": [");
        _prompt.AppendLine("        {\"name\": \"参数名\", \"value\": \"参数值\"}");
        _prompt.AppendLine("      ],");
        _prompt.AppendLine("      \"inputPorts\": [...],");
        _prompt.AppendLine("      \"outputPorts\": [...]");
        _prompt.AppendLine("    }");
        _prompt.AppendLine("  ],");
        _prompt.AppendLine("  \"connections\": [");
        _prompt.AppendLine("    {");
        _prompt.AppendLine("      \"sourceOperatorId\": \"guid\",");
        _prompt.AppendLine("      \"sourcePortId\": \"guid\",");
        _prompt.AppendLine("      \"targetOperatorId\": \"guid\",");
        _prompt.AppendLine("      \"targetPortId\": \"guid\"");
        _prompt.AppendLine("    }");
        _prompt.AppendLine("  ]");
        _prompt.AppendLine("}");
        _prompt.AppendLine("```");
        _prompt.AppendLine();

        return this;
    }

    /// <summary>
    /// 添加可用算子库
    /// </summary>
    public AIPromptBuilder WithOperatorLibrary()
    {
        _prompt.AppendLine("## 可用算子库");
        _prompt.AppendLine();

        var categories = _operators.GroupBy(o => o.Category);

        foreach (var category in categories)
        {
            _prompt.AppendLine($"### {category.Key}");
            _prompt.AppendLine();

            foreach (var op in category)
            {
                _prompt.AppendLine($"- **{op.Name}** (`{op.Type}`): {op.Description}");

                if (!string.IsNullOrEmpty(op.InputType))
                    _prompt.AppendLine($"  - 输入: {op.InputType}");
                if (!string.IsNullOrEmpty(op.OutputType))
                    _prompt.AppendLine($"  - 输出: {op.OutputType}");

                if (op.Parameters.Any())
                {
                    _prompt.AppendLine($"  - 参数:");
                    foreach (var param in op.Parameters)
                    {
                        var required = param.IsRequired ? "(必需)" : "(可选)";
                        var range = !string.IsNullOrEmpty(param.MinValue) || !string.IsNullOrEmpty(param.MaxValue)
                            ? $" [{param.MinValue}~{param.MaxValue}]"
                            : "";
                        var options = param.Options?.Any() == true
                            ? $" 可选值: {string.Join(", ", param.Options)}"
                            : "";
                        _prompt.AppendLine($"    - `{param.Name}`: {param.Description} {required}{range}{options}");
                    }
                }

                if (!string.IsNullOrEmpty(op.SpecialNotes))
                {
                    _prompt.AppendLine($"  - ⚠️ 注意: {op.SpecialNotes}");
                }

                _prompt.AppendLine();
            }
        }

        return this;
    }

    /// <summary>
    /// 添加设计规则
    /// </summary>
    public AIPromptBuilder WithDesignRules()
    {
        _prompt.AppendLine("## 设计规则");
        _prompt.AppendLine();
        _prompt.AppendLine("1. **DAG 原则**: 流程必须是有向无环图，禁止循环依赖");
        _prompt.AppendLine("2. **通信算子保护**: 所有通信算子（HTTP、MQTT、Modbus、S7等）上游必须有 ConditionalBranch 或 ResultJudgment 保护，防止无条件触发外部设备");
        _prompt.AppendLine("3. **ForEach 模式选择**: ");
        _prompt.AppendLine("   - IoMode=Parallel: 用于纯计算子图（图像处理、数值计算）");
        _prompt.AppendLine("   - IoMode=Sequential: 用于含通信算子的子图，保护硬件连接");
        _prompt.AppendLine("4. **类型匹配**: 端口连接时确保数据类型兼容");
        _prompt.AppendLine("5. **参数校验**: 数值参数必须在有效范围内");
        _prompt.AppendLine("6. **分支覆盖**: 设计的流程应能处理正常和异常情况");
        _prompt.AppendLine();

        return this;
    }

    /// <summary>
    /// 添加用户需求
    /// </summary>
    public AIPromptBuilder WithUserRequirement(string requirement)
    {
        _prompt.AppendLine("## 用户需求");
        _prompt.AppendLine();
        _prompt.AppendLine(requirement);
        _prompt.AppendLine();

        return this;
    }

    /// <summary>
    /// 添加示例
    /// </summary>
    public AIPromptBuilder WithExamples()
    {
        _prompt.AppendLine("## 示例");
        _prompt.AppendLine();
        _prompt.AppendLine("### 示例 1：多目标检测 + 逐条 MES 上报");
        _prompt.AppendLine("```json");
        _prompt.AppendLine(@"{
  ""flowName"": ""多目标检测MES上报"",
  ""operators"": [
    { ""id"": ""op1"", ""name"": ""图像采集"", ""type"": ""ImageAcquisition"", ""outputPorts"": [{ ""id"": ""p1"", ""name"": ""Image"", ""dataType"": ""Image"" }] },
    { ""id"": ""op2"", ""name"": ""YOLO检测"", ""type"": ""DeepLearning"", ""parameters"": [{ ""name"": ""ModelPath"", ""value"": ""models/defect.onnx"" }, { ""name"": ""Confidence"", ""value"": ""0.5"" }], ""inputPorts"": [{ ""id"": ""p2"", ""name"": ""Image"" }], ""outputPorts"": [{ ""id"": ""p3"", ""name"": ""DetectionList"" }] },
    { ""id"": ""op3"", ""name"": ""ForEach 循环"", ""type"": ""ForEach"", ""parameters"": [{ ""name"": ""IoMode"", ""value"": ""Sequential"" }], ""inputPorts"": [{ ""id"": ""p4"", ""name"": ""Items"" }] }
  ],
  ""connections"": [
    { ""sourceOperatorId"": ""op1"", ""sourcePortId"": ""p1"", ""targetOperatorId"": ""op2"", ""targetPortId"": ""p2"" },
    { ""sourceOperatorId"": ""op2"", ""sourcePortId"": ""p3"", ""targetOperatorId"": ""op3"", ""targetPortId"": ""p4"" }
  ]
}");
        _prompt.AppendLine("```");
        _prompt.AppendLine();

        return this;
    }

    /// <summary>
    /// 添加输出要求
    /// </summary>
    public AIPromptBuilder WithOutputRequirements()
    {
        _prompt.AppendLine("## 输出要求");
        _prompt.AppendLine();
        _prompt.AppendLine("1. 只输出 JSON，不要输出任何其他文字说明");
        _prompt.AppendLine("2. 确保所有算子 ID 使用有效的 GUID 格式");
        _prompt.AppendLine("3. 确保端口 ID 唯一且在算子内保持一致");
        _prompt.AppendLine("4. 参数值类型必须与定义匹配");
        _prompt.AppendLine("5. 通信算子必须添加保护性上游节点");
        _prompt.AppendLine();
        _prompt.AppendLine("请根据以上信息生成流程 JSON：");
        _prompt.AppendLine();

        return this;
    }

    /// <summary>
    /// 构建完整提示词
    /// </summary>
    public string Build()
    {
        return _prompt.ToString();
    }

    /// <summary>
    /// 创建完整提示词（快捷方法）
    /// </summary>
    public static string CreateFullPrompt(string userRequirement)
    {
        return new AIPromptBuilder()
            .WithSystemPrompt()
            .WithOperatorLibrary()
            .WithDesignRules()
            .WithExamples()
            .WithUserRequirement(userRequirement)
            .WithOutputRequirements()
            .Build();
    }
}

/// <summary>
/// 算子元数据
/// </summary>
public class PromptOperatorInfo
{
    public OperatorType Type { get; }
    public string Name { get; }
    public string Description { get; }
    public string InputType { get; }
    public string OutputType { get; }
    public List<PromptParamInfo> Parameters { get; }
    public string SpecialNotes { get; }
    public string Category { get; }

    public PromptOperatorInfo(
        OperatorType type,
        string name,
        string description,
        string inputType = "",
        string outputType = "",
        PromptParamInfo[]? parameters = null,
        string specialNotes = "",
        string category = "其他")
    {
        Type = type;
        Name = name;
        Description = description;
        InputType = inputType;
        OutputType = outputType;
        Parameters = parameters?.ToList() ?? new List<PromptParamInfo>();
        SpecialNotes = specialNotes;
        Category = category;
    }
}

/// <summary>
/// 参数元数据
/// </summary>
public class PromptParamInfo
{
    public string Name { get; }
    public string DataType { get; }
    public string Description { get; }
    public bool IsRequired { get; }
    public string DefaultValue { get; }
    public string MinValue { get; }
    public string MaxValue { get; }
    public string[]? Options { get; }

    public PromptParamInfo(
        string name,
        string dataType,
        string description,
        bool isRequired = false,
        string defaultValue = "",
        string minValue = "",
        string maxValue = "",
        string[]? options = null)
    {
        Name = name;
        DataType = dataType;
        Description = description;
        IsRequired = isRequired;
        DefaultValue = defaultValue;
        MinValue = minValue;
        MaxValue = maxValue;
        Options = options;
    }
}
