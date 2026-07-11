// AiFlowValidator.cs
// AI 流程校验器
// 对 AI 生成流程进行结构与规则校验
// 作者：蘅芜君
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI;

/// <summary>
/// 校验 AI 生成的工作流是否满足所有约束
/// </summary>
public class AiFlowValidator : IAiFlowValidator
{
    private readonly IOperatorFactory _operatorFactory;
    private readonly string _knowledgeGraphPath;
    private readonly object _knowledgeGraphLock = new();
    private IReadOnlyDictionary<string, OperatorKnowledgeCard>? _knowledgeCardsByType;
    private DateTime _knowledgeGraphLastWriteUtc = DateTime.MinValue;

    private const string DefaultKnowledgeGraphRelativePath = "docs/ai/operator-knowledge/operator_knowledge_graph.json";
    private static readonly JsonSerializerOptions KnowledgeGraphJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex AntiPatternConnectionRegex = new(
        @"(?<source>[A-Za-z][A-Za-z0-9_]*)\s*->\s*(?<target>[A-Za-z][A-Za-z0-9_]*)",
        RegexOptions.Compiled);
    private static readonly Regex AntiPatternParameterRegex = new(
        @"(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>[^,;\s]+)",
        RegexOptions.Compiled);
    private static readonly string[] AntiPatternNegationMarkers =
    [
        "without",
        "missing",
        "lack",
        "缺少",
        "未配置",
        "未提供",
        "无"
    ];
    private static readonly string[] AntiPatternConjunctionMarkers =
    [
        " and ",
        " + ",
        " together ",
        "同时",
        "并且"
    ];

    public AiFlowValidator(
        IOperatorFactory operatorFactory,
        string? operatorKnowledgeGraphPath = null)
    {
        _operatorFactory = operatorFactory;
        _knowledgeGraphPath = ResolveKnowledgeGraphPath(operatorKnowledgeGraphPath);
    }

    public AiValidationResult Validate(AiGeneratedFlowJson generatedFlow)
    {
        var result = new AiValidationResult();

        if (generatedFlow.Operators == null || generatedFlow.Operators.Count == 0)
        {
            result.AddError(
                "AI 未生成任何算子",
                code: "empty_workflow",
                category: "structure",
                relatedFields: ["operators"],
                repairHint: "请至少生成一个有效算子，并补齐 operators 数组。");
            return result;
        }

        // 建立 tempId → 算子元数据 的映射，用于后续校验
        var operatorMetaMap = new Dictionary<string, OperatorMetadata>();

        // 1. 校验算子类型合法性
        ValidateOperatorTypes(generatedFlow, result, operatorMetaMap);

        // 如果算子类型校验失败，后续校验意义不大
        if (!result.IsValid)
            return result;

        // 2. 校验端口名合法性和类型兼容性
        ValidateConnections(generatedFlow, result, operatorMetaMap);

        // 3. 校验无环路
        ValidateNoCycles(generatedFlow, result);

        // 4. 校验输入端口不重复占用
        ValidateNoDuplicateInputs(generatedFlow, result);

        // 5. 校验参数合法性（数值范围、枚举类型）
        ValidateParameters(generatedFlow, result, operatorMetaMap);

        // 6. 基于知识图谱的 requiredResources/antiPatterns 校验
        ValidateKnowledgeGraphRules(generatedFlow, result);

        // 7. 警告（不阻止生成，但记录）
        ValidateHasSourceAndOutput(generatedFlow, result, operatorMetaMap);

        return result;
    }

    private void ValidateOperatorTypes(
        AiGeneratedFlowJson flow,
        AiValidationResult result,
        Dictionary<string, OperatorMetadata> metaMap)
    {
        var allTempIds = new HashSet<string>();

        for (var index = 0; index < flow.Operators.Count; index++)
        {
            var op = flow.Operators[index];
            var operatorField = $"operators[{index}]";

            // 检查 tempId 格式
            if (string.IsNullOrWhiteSpace(op.TempId))
            {
                result.AddError(
                    "存在算子的 tempId 为空",
                    code: "missing_temp_id",
                    category: "structure",
                    relatedFields: [$"{operatorField}.tempId"],
                    repairHint: "为每个算子补充唯一 tempId，例如 op_1、op_2。");
                continue;
            }

            if (allTempIds.Contains(op.TempId))
            {
                result.AddError(
                    $"tempId 重复：{op.TempId}",
                    code: "duplicate_temp_id",
                    category: "structure",
                    relatedFields: [$"{operatorField}.tempId"],
                    operatorId: op.TempId,
                    repairHint: "确保每个算子都使用唯一 tempId。");
                continue;
            }
            allTempIds.Add(op.TempId);

            // 检查算子类型是否在枚举中
            if (!Enum.TryParse<OperatorType>(op.OperatorType, out var operatorType))
            {
                result.AddError(
                    $"算子类型不存在：{op.OperatorType}（tempId={op.TempId}）。请使用算子目录中的 operator_id 值。",
                    code: "unknown_operator_type",
                    category: "operator",
                    relatedFields: [$"{operatorField}.operatorType"],
                    operatorId: op.TempId,
                    repairHint: "把 operatorType 改成已注册的 OperatorType 枚举名。");
                continue;
            }

            // 检查算子元数据是否已注册
            var metadata = _operatorFactory.GetMetadata(operatorType);
            if (metadata == null)
            {
                result.AddError(
                    $"算子 {op.OperatorType} 未在算子工厂中注册",
                    code: "operator_not_registered",
                    category: "operator",
                    relatedFields: [$"{operatorField}.operatorType"],
                    operatorId: op.TempId,
                    repairHint: "请改用已经注册的算子类型，或移除该无效算子。");
                continue;
            }

            metaMap[op.TempId] = metadata;
        }
    }

    private void ValidateConnections(
        AiGeneratedFlowJson flow,
        AiValidationResult result,
        Dictionary<string, OperatorMetadata> metaMap)
    {
        if (flow.Connections == null)
            return;

        for (var index = 0; index < flow.Connections.Count; index++)
        {
            var conn = flow.Connections[index];
            var connectionField = $"connections[{index}]";

            // 检查源算子存在
            if (!metaMap.TryGetValue(conn.SourceTempId, out var sourceMeta))
            {
                result.AddError(
                    $"连线引用了不存在的源算子 tempId：{conn.SourceTempId}",
                    code: "missing_source_operator",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.sourceTempId"
                    ],
                    sourceTempId: conn.SourceTempId,
                    repairHint: "修正 sourceTempId，确保它引用已定义的算子 tempId。");
                continue;
            }

            // 检查目标算子存在
            if (!metaMap.TryGetValue(conn.TargetTempId, out var targetMeta))
            {
                result.AddError(
                    $"连线引用了不存在的目标算子 tempId：{conn.TargetTempId}",
                    code: "missing_target_operator",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.targetTempId"
                    ],
                    targetTempId: conn.TargetTempId,
                    repairHint: "修正 targetTempId，确保它引用已定义的算子 tempId。");
                continue;
            }

            // 检查源端口存在
            var sourcePort = sourceMeta.OutputPorts.FirstOrDefault(p => p.Name == conn.SourcePortName);
            if (sourcePort == null)
            {
                result.AddError(
                    $"算子 {conn.SourceTempId}({sourceMeta.DisplayName}) 没有名为 '{conn.SourcePortName}' 的输出端口。" +
                    $"可用输出端口：{string.Join(", ", sourceMeta.OutputPorts.Select(p => p.Name))}",
                    code: "missing_output_port",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.sourceTempId",
                        $"{connectionField}.sourcePortName"
                    ],
                    sourceTempId: conn.SourceTempId,
                    sourcePortName: conn.SourcePortName,
                    repairHint: "把 sourcePortName 改成该源算子的有效输出端口名。");
                continue;
            }

            // 检查目标端口存在
            var targetPort = targetMeta.InputPorts.FirstOrDefault(p => p.Name == conn.TargetPortName);
            if (targetPort == null)
            {
                result.AddError(
                    $"算子 {conn.TargetTempId}({targetMeta.DisplayName}) 没有名为 '{conn.TargetPortName}' 的输入端口。" +
                    $"可用输入端口：{string.Join(", ", targetMeta.InputPorts.Select(p => p.Name))}",
                    code: "missing_input_port",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.targetTempId",
                        $"{connectionField}.targetPortName"
                    ],
                    targetTempId: conn.TargetTempId,
                    targetPortName: conn.TargetPortName,
                    repairHint: "把 targetPortName 改成该目标算子的有效输入端口名。");
                continue;
            }

            // 检查类型兼容性
            if (!AreTypesCompatible(sourcePort.DataType, targetPort.DataType))
            {
                result.AddError(
                    $"端口类型不兼容：{conn.SourceTempId}.{conn.SourcePortName}({sourcePort.DataType}) → " +
                    $"{conn.TargetTempId}.{conn.TargetPortName}({targetPort.DataType})",
                    code: "incompatible_port_type",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.sourcePortName",
                        $"{connectionField}.targetPortName"
                    ],
                    sourceTempId: conn.SourceTempId,
                    sourcePortName: conn.SourcePortName,
                    targetTempId: conn.TargetTempId,
                    targetPortName: conn.TargetPortName,
                    repairHint: "请改用类型兼容的端口连线，或补充中间转换算子。");
            }
        }
    }

    private bool AreTypesCompatible(PortDataType source, PortDataType target)
    {
        return PortDataTypeCompatibility.AreCompatible(source, target);
    }

    private void ValidateNoCycles(AiGeneratedFlowJson flow, AiValidationResult result)
    {
        if (flow.Connections == null || flow.Connections.Count == 0)
            return;

        // 构建邻接表
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var op in flow.Operators)
            adjacency[op.TempId] = new List<string>();

        foreach (var conn in flow.Connections)
        {
            if (adjacency.ContainsKey(conn.SourceTempId))
                adjacency[conn.SourceTempId].Add(conn.TargetTempId);
        }

        // DFS 检测环路
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();

        foreach (var node in adjacency.Keys)
        {
            if (HasCycle(node, adjacency, visited, inStack))
            {
                result.AddError(
                    "工作流中存在环路（循环依赖），请重新设计流程结构",
                    code: "cycle_detected",
                    category: "graph",
                    relatedFields: ["connections"],
                    repairHint: "移除形成回路的连线，保持流程为 DAG。");
                return;
            }
        }
    }

    private bool HasCycle(
        string node,
        Dictionary<string, List<string>> adjacency,
        HashSet<string> visited,
        HashSet<string> inStack)
    {
        if (inStack.Contains(node))
            return true;
        if (visited.Contains(node))
            return false;

        visited.Add(node);
        inStack.Add(node);

        foreach (var neighbor in adjacency.GetValueOrDefault(node, new List<string>()))
        {
            if (HasCycle(neighbor, adjacency, visited, inStack))
                return true;
        }

        inStack.Remove(node);
        return false;
    }

    private void ValidateNoDuplicateInputs(AiGeneratedFlowJson flow, AiValidationResult result)
    {
        if (flow.Connections == null)
            return;

        var inputPortUsage = new HashSet<string>();
        for (var index = 0; index < flow.Connections.Count; index++)
        {
            var conn = flow.Connections[index];
            var key = $"{conn.TargetTempId}:{conn.TargetPortName}";
            if (!inputPortUsage.Add(key))
            {
                result.AddError(
                    $"输入端口被重复连接：算子 {conn.TargetTempId} 的 {conn.TargetPortName} 端口只能接收一条连线",
                    code: "duplicate_input_connection",
                    category: "connection",
                    relatedFields:
                    [
                        $"connections[{index}].targetTempId",
                        $"connections[{index}].targetPortName"
                    ],
                    targetTempId: conn.TargetTempId,
                    targetPortName: conn.TargetPortName,
                    repairHint: "删除重复连线，确保每个输入端口最多接收一条连接。");
            }
        }
    }

    private void ValidateParameters(
        AiGeneratedFlowJson flow,
        AiValidationResult result,
        Dictionary<string, OperatorMetadata> metaMap)
    {
        for (var index = 0; index < flow.Operators.Count; index++)
        {
            var op = flow.Operators[index];
            var operatorField = $"operators[{index}]";
            if (!metaMap.TryGetValue(op.TempId, out var metadata))
                continue;

            op.Parameters ??= new Dictionary<string, string>(StringComparer.Ordinal);
            var explicitParameterNames = op.Parameters.Keys.ToHashSet(StringComparer.Ordinal);
            ApplyIntelligentDefaults(op, metadata, result, operatorField);

            var constraintValues = op.Parameters.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.Ordinal);
            var canonicalization = OperatorParameterConstraintEvaluator.Canonicalize(
                metadata,
                constraintValues,
                explicitParameterNames);
            foreach (var diagnostic in canonicalization.Diagnostics)
            {
                result.AddWarning(
                    $"算子 {op.TempId}({metadata.DisplayName})：{diagnostic.Message}",
                    code: "parameter_alias_conflict",
                    category: "parameter",
                    relatedFields:
                    [
                        $"{operatorField}.parameters.{diagnostic.CanonicalParameter}",
                        $"{operatorField}.parameters.{diagnostic.AliasParameter}"
                    ],
                    operatorId: op.TempId,
                    parameterName: diagnostic.CanonicalParameter,
                    repairHint: $"保留 {diagnostic.CanonicalParameter}，移除冲突 alias {diagnostic.AliasParameter}。");
            }

            foreach (var alias in metadata.ParameterConstraints.Where(constraint =>
                         !string.IsNullOrWhiteSpace(constraint.AliasFor)))
            {
                op.Parameters.Remove(alias.Parameter);
            }

            foreach (var pair in canonicalization.ExplicitValues)
            {
                op.Parameters[pair.Key] = pair.Value?.ToString() ?? string.Empty;
            }

            constraintValues = op.Parameters.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.Ordinal);
            foreach (var violation in OperatorParameterConstraintEvaluator.Validate(
                         metadata,
                         constraintValues,
                         requireExplicitResourceConfiguration: true))
            {
                var fieldNames = string.Join(", ", violation.ParameterNames);
                var code = violation.Code switch
                {
                    "at-least-one" => "missing_conditional_parameter_group",
                    "mutually-exclusive" => "mutually_exclusive_parameters",
                    _ => "missing_conditional_parameter"
                };
                var message = violation.Code switch
                {
                    "at-least-one" => $"算子 {op.TempId}({metadata.DisplayName}) 需要在 {fieldNames} 中至少配置一项",
                    "mutually-exclusive" => $"算子 {op.TempId}({metadata.DisplayName}) 的参数 {fieldNames} 不能同时配置",
                    _ => $"算子 {op.TempId}({metadata.DisplayName}) 缺少条件必填参数 {fieldNames}"
                };
                result.AddWarning(
                    message,
                    code: code,
                    category: "parameter",
                    relatedFields: violation.ParameterNames
                        .Select(name => $"{operatorField}.parameters.{name}")
                        .ToArray(),
                    operatorId: op.TempId,
                    parameterName: violation.ParameterNames[0],
                    repairHint: $"请根据当前参数模式补齐或收敛 {fieldNames}。");
            }

            foreach (var requiredParam in metadata.Parameters.Where(p =>
                         p.IsRequired &&
                         metadata.ParameterConstraints.All(constraint =>
                             !constraint.Parameter.Equals(p.Name, StringComparison.OrdinalIgnoreCase))))
            {
                if (!op.Parameters.ContainsKey(requiredParam.Name))
                {
                    result.AddWarning(
                        $"算子 {op.TempId}({metadata.DisplayName}) 缺少必填参数 '{requiredParam.Name}'，且无可用默认值",
                        code: "missing_required_parameter",
                        category: "parameter",
                        relatedFields: [$"{operatorField}.parameters.{requiredParam.Name}"],
                        operatorId: op.TempId,
                        parameterName: requiredParam.Name,
                        repairHint: $"请为算子 {op.TempId} 补齐参数 {requiredParam.Name}。");
                }
            }

            foreach (var kvp in op.Parameters.ToList())
            {
                var paramName = kvp.Key;
                var paramValueStr = kvp.Value?.ToString() ?? string.Empty;

                var paramDef = metadata.Parameters.FirstOrDefault(p => p.Name == paramName);
                if (paramDef == null)
                {
                    if (metadata.ParameterConstraints.Any(constraint =>
                            constraint.Parameter.Equals(paramName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // 参数不存在，仅作为警告
                    result.AddWarning(
                        $"算子 {op.TempId}({metadata.DisplayName}) 生成了未知的参数 '{paramName}'",
                        code: "unknown_parameter",
                        category: "parameter",
                        relatedFields: [$"{operatorField}.parameters.{paramName}"],
                        operatorId: op.TempId,
                        parameterName: paramName,
                        repairHint: "请移除未知参数，或改成该算子定义中存在的参数名。");
                    continue;
                }

                // 数值范围校验 + 自动 Clamp
                if (TryParseDouble(paramValueStr, out var numValue))
                {
                    var hasMin = TryParseDouble(paramDef.MinValue, out var minValue);
                    var hasMax = TryParseDouble(paramDef.MaxValue, out var maxValue);

                    var clamped = numValue;
                    if (hasMin && clamped < minValue)
                        clamped = minValue;
                    if (hasMax && clamped > maxValue)
                        clamped = maxValue;

                    if (Math.Abs(clamped - numValue) > double.Epsilon)
                    {
                        var clampedValue = FormatNumericValue(clamped, paramDef.DataType);
                        op.Parameters[paramName] = clampedValue;
                        result.AddWarning(
                            $"算子 {op.TempId}({metadata.DisplayName}) 的参数 '{paramName}' 值 {numValue} 超出范围，已自动调整为 {clampedValue}",
                            code: "parameter_clamped",
                            category: "parameter",
                            relatedFields: [$"{operatorField}.parameters.{paramName}"],
                            operatorId: op.TempId,
                            parameterName: paramName,
                            repairHint: $"请在下一轮直接生成 {paramName} 的合法范围值。");
                    }
                }

                // 枚举值校验
                if (paramDef.DataType.Equals("enum", StringComparison.OrdinalIgnoreCase) && paramDef.Options != null && paramDef.Options.Count > 0)
                {
                    var validValues = paramDef.Options.Select(o => o.Value).ToList();
                    if (!validValues.Contains(paramValueStr))
                    {
                        result.AddWarning(
                            $"算子 {op.TempId}({metadata.DisplayName}) 的枚举参数 '{paramName}' 值为 '{paramValueStr}' 不合法，有效值为: {string.Join(", ", validValues)}",
                            code: "invalid_enum_value",
                            category: "parameter",
                            relatedFields: [$"{operatorField}.parameters.{paramName}"],
                            operatorId: op.TempId,
                            parameterName: paramName,
                            repairHint: $"请把 {paramName} 改成有效枚举值之一：{string.Join(", ", validValues)}。");
                    }
                }
            }
        }
    }

    private static void ApplyIntelligentDefaults(
        AiGeneratedOperator op,
        OperatorMetadata metadata,
        AiValidationResult result,
        string operatorField)
    {
        foreach (var paramDef in metadata.Parameters.Where(p => p.IsRequired))
        {
            if (op.Parameters.ContainsKey(paramDef.Name) &&
                !string.IsNullOrWhiteSpace(op.Parameters[paramDef.Name]))
            {
                continue;
            }

            var defaultValue = ConvertParameterValueToString(paramDef.DefaultValue);
            if (string.IsNullOrWhiteSpace(defaultValue))
                continue;

            op.Parameters[paramDef.Name] = defaultValue;
            result.AddWarning(
                $"算子 {op.TempId}({metadata.DisplayName}) 的必填参数 '{paramDef.Name}' 缺失，已自动填充默认值 {defaultValue}",
                code: "default_parameter_applied",
                category: "parameter",
                relatedFields: [$"{operatorField}.parameters.{paramDef.Name}"],
                operatorId: op.TempId,
                parameterName: paramDef.Name,
                repairHint: $"如默认值不符合场景，请在下一轮明确给出 {paramDef.Name}。");
        }
    }

    private static string ConvertParameterValueToString(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is bool boolean)
            return boolean ? "true" : "false";

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatNumericValue(double value, string dataType)
    {
        if (dataType.Equals("int", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("integer", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(Math.Round(value, MidpointRounding.AwayFromZero))
                .ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static bool TryParseDouble(object? value, out double parsed)
    {
        if (value == null)
        {
            parsed = 0;
            return false;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            parsed = 0;
            return false;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
    }

    private void ValidateKnowledgeGraphRules(AiGeneratedFlowJson flow, AiValidationResult result)
    {
        var cardsByType = ResolveKnowledgeCardsByType();
        if (cardsByType.Count == 0)
            return;

        ValidateRequiredResourcesFromKnowledgeGraph(flow, result, cardsByType);
        ValidateAntiPatternsFromKnowledgeGraph(flow, result, cardsByType);
    }

    private static void ValidateRequiredResourcesFromKnowledgeGraph(
        AiGeneratedFlowJson flow,
        AiValidationResult result,
        IReadOnlyDictionary<string, OperatorKnowledgeCard> cardsByType)
    {
        var seenWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var typedOperators = flow.Operators
            .Select((op, index) => new { Operator = op, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Operator.OperatorType))
            .GroupBy(item => item.Operator.OperatorType, StringComparer.OrdinalIgnoreCase);

        foreach (var group in typedOperators)
        {
            if (!cardsByType.TryGetValue(group.Key, out var card) || card.RequiredResources.Count == 0)
                continue;

            foreach (var requiredResource in card.RequiredResources
                         .Where(item => !string.IsNullOrWhiteSpace(item))
                         .Select(item => item.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var item in group)
                {
                    if (!IsKnownResourceMissing(flow, item.Operator, requiredResource))
                        continue;

                    var dedupKey = $"{item.Operator.TempId}|{requiredResource}";
                    if (!seenWarnings.Add(dedupKey))
                        continue;

                    result.AddWarning(
                        $"算子 {item.Operator.TempId}({item.Operator.OperatorType}) 缺少知识图谱要求资源 {requiredResource}。",
                        code: "knowledge_required_resource_missing",
                        category: "knowledge",
                        relatedFields:
                        [
                            $"operators[{item.Index}].parameters",
                            "missingResources"
                        ],
                        operatorId: item.Operator.TempId,
                        parameterName: requiredResource,
                        repairHint: $"请补齐资源 {requiredResource}，或在 missingResources/pendingParameters 中声明待提供。");
                }
            }
        }
    }

    private static void ValidateAntiPatternsFromKnowledgeGraph(
        AiGeneratedFlowJson flow,
        AiValidationResult result,
        IReadOnlyDictionary<string, OperatorKnowledgeCard> cardsByType)
    {
        var knownTypes = cardsByType.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var presentTypes = flow.Operators
            .Select(op => op.OperatorType)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connectionPairs = BuildTypeConnectionPairs(flow);
        var seenWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < flow.Operators.Count; index++)
        {
            var op = flow.Operators[index];
            if (string.IsNullOrWhiteSpace(op.OperatorType) ||
                !cardsByType.TryGetValue(op.OperatorType, out var card) ||
                card.AntiPatterns.Count == 0)
            {
                continue;
            }

            foreach (var antiPattern in card.AntiPatterns
                         .Where(item => !string.IsNullOrWhiteSpace(item))
                         .Select(item => item.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryMatchAntiPattern(op, antiPattern, knownTypes, presentTypes, connectionPairs, out var relatedField))
                    continue;

                var dedupKey = $"{op.TempId}|{antiPattern}";
                if (!seenWarnings.Add(dedupKey))
                    continue;

                var relatedFields = string.IsNullOrWhiteSpace(relatedField)
                    ? new[] { $"operators[{index}]" }
                    : new[] { $"operators[{index}].{relatedField}" };

                result.AddWarning(
                    $"算子 {op.TempId}({op.OperatorType}) 命中知识图谱反模式：{antiPattern}",
                    code: "knowledge_anti_pattern_detected",
                    category: "knowledge",
                    relatedFields: relatedFields,
                    operatorId: op.TempId,
                    repairHint: "请调整算子拓扑或参数，避免触发已知反模式。");
            }
        }
    }

    private static bool TryMatchAntiPattern(
        AiGeneratedOperator op,
        string antiPattern,
        IReadOnlyCollection<string> knownTypes,
        IReadOnlyCollection<string> presentTypes,
        IReadOnlyCollection<string> connectionPairs,
        out string? relatedField)
    {
        relatedField = null;

        var connectionMatch = AntiPatternConnectionRegex.Match(antiPattern);
        if (connectionMatch.Success)
        {
            var source = connectionMatch.Groups["source"].Value;
            var target = connectionMatch.Groups["target"].Value;
            if (connectionPairs.Contains($"{source}->{target}"))
                return true;
        }

        foreach (Match paramMatch in AntiPatternParameterRegex.Matches(antiPattern))
        {
            var key = paramMatch.Groups["key"].Value;
            var expectedValue = paramMatch.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (op.Parameters != null &&
                op.Parameters.TryGetValue(key, out var actualValue) &&
                string.Equals(actualValue?.Trim(), expectedValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                relatedField = $"parameters.{key}";
                return true;
            }
        }

        if (ContainsNegation(antiPattern))
        {
            var mentionedTypes = knownTypes
                .Where(type => antiPattern.Contains(type, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var missingMentionedType = mentionedTypes
                .FirstOrDefault(type => !presentTypes.Contains(type));
            if (!string.IsNullOrWhiteSpace(missingMentionedType))
                return true;
        }
        else if (AntiPatternConjunctionMarkers.Any(marker =>
                     antiPattern.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            var mentionedTypes = knownTypes
                .Where(type => antiPattern.Contains(type, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (mentionedTypes.Count >= 2 &&
                mentionedTypes.All(type => presentTypes.Contains(type)))
            {
                return true;
            }
        }

        if (antiPattern.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
            antiPattern.Contains("todo", StringComparison.OrdinalIgnoreCase))
        {
            var hasPlaceholder = (op.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                .Values
                .Any(value => IsParameterValueMissing(value));
            if (hasPlaceholder)
            {
                relatedField = "parameters";
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildTypeConnectionPairs(AiGeneratedFlowJson flow)
    {
        var typeByTempId = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId) && !string.IsNullOrWhiteSpace(op.OperatorType))
            .ToDictionary(op => op.TempId, op => op.OperatorType, StringComparer.OrdinalIgnoreCase);
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var connection in flow.Connections ?? new List<AiGeneratedConnection>())
        {
            if (!typeByTempId.TryGetValue(connection.SourceTempId, out var sourceType) ||
                !typeByTempId.TryGetValue(connection.TargetTempId, out var targetType))
            {
                continue;
            }

            pairs.Add($"{sourceType}->{targetType}");
        }

        return pairs;
    }

    private static bool IsKnownResourceMissing(
        AiGeneratedFlowJson flow,
        AiGeneratedOperator op,
        string resourceKey)
    {
        if (TryResolveResourceParameterKeys(op.OperatorType, resourceKey, out var parameterKeys))
            return IsParameterValueMissing(op.Parameters, parameterKeys);

        return (flow.MissingResources ?? new List<AiMissingResourceInfo>()).Any(item =>
            string.Equals(item.ResourceKey, resourceKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveResourceParameterKeys(
        string operatorType,
        string resourceKey,
        out string[] parameterKeys)
    {
        parameterKeys = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(resourceKey))
            return false;

        var normalized = resourceKey.Trim();
        string parameterName;
        if (normalized.Contains('.', StringComparison.Ordinal))
        {
            var separatorIndex = normalized.IndexOf('.');
            var resourceOperatorType = normalized[..separatorIndex].Trim();
            if (!resourceOperatorType.Equals(operatorType, StringComparison.OrdinalIgnoreCase))
                return false;

            parameterName = normalized[(separatorIndex + 1)..].Trim();
        }
        else
        {
            parameterName = normalized;
        }

        if (string.IsNullOrWhiteSpace(parameterName))
            return false;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { parameterName };
        if (parameterName.EndsWith("Path", StringComparison.OrdinalIgnoreCase))
            keys.Add(parameterName[..^4] + "Id");

        parameterKeys = keys.ToArray();
        return parameterKeys.Length > 0;
    }

    private static bool IsParameterValueMissing(
        IReadOnlyDictionary<string, string>? parameters,
        IEnumerable<string> candidateKeys)
    {
        if (parameters == null)
            return true;

        foreach (var key in candidateKeys)
        {
            if (!parameters.TryGetValue(key, out var value))
                continue;

            if (!IsParameterValueMissing(value))
                return false;
        }

        return true;
    }

    private static bool IsParameterValueMissing(string? value)
    {
        return OperatorParameterValueSemantics.IsMissing(value);
    }

    private static bool ContainsNegation(string text)
    {
        return AntiPatternNegationMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyDictionary<string, OperatorKnowledgeCard> ResolveKnowledgeCardsByType()
    {
        lock (_knowledgeGraphLock)
        {
            if (!File.Exists(_knowledgeGraphPath))
            {
                _knowledgeCardsByType ??= new Dictionary<string, OperatorKnowledgeCard>(StringComparer.OrdinalIgnoreCase);
                _knowledgeGraphLastWriteUtc = DateTime.MinValue;
                return _knowledgeCardsByType;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(_knowledgeGraphPath);
            if (_knowledgeCardsByType != null && lastWriteUtc == _knowledgeGraphLastWriteUtc)
                return _knowledgeCardsByType;

            try
            {
                var json = File.ReadAllText(_knowledgeGraphPath);
                var graph = JsonSerializer.Deserialize<OperatorKnowledgeGraph>(json, KnowledgeGraphJsonOptions);
                _knowledgeCardsByType = (graph?.Cards ?? new List<OperatorKnowledgeCard>())
                    .Where(card => !string.IsNullOrWhiteSpace(card.OperatorType))
                    .GroupBy(card => card.OperatorType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                _knowledgeGraphLastWriteUtc = lastWriteUtc;
            }
            catch
            {
                _knowledgeCardsByType = new Dictionary<string, OperatorKnowledgeCard>(StringComparer.OrdinalIgnoreCase);
                _knowledgeGraphLastWriteUtc = lastWriteUtc;
            }

            return _knowledgeCardsByType;
        }
    }

    private static string ResolveKnowledgeGraphPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
        }

        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; depth < 10 && current != null; depth++)
            {
                var candidate = Path.Combine(current.FullName, DefaultKnowledgeGraphRelativePath);
                if (File.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            DefaultKnowledgeGraphRelativePath));
    }

    private void ValidateHasSourceAndOutput(
        AiGeneratedFlowJson flow,
        AiValidationResult result,
        Dictionary<string, OperatorMetadata> metaMap)
    {
        // 警告：没有源算子
        var hasSource = flow.Operators.Any(op =>
            metaMap.TryGetValue(op.TempId, out var meta) &&
            meta.InputPorts.Count == 0);

        if (!hasSource)
        {
            result.AddWarning(
                "工作流没有图像源算子（无输入端口的算子），建议添加 ImageAcquisition",
                code: "missing_image_source",
                category: "completeness",
                relatedFields: ["operators"],
                repairHint: "请补充图像源算子，例如 ImageAcquisition。");
        }

        // 警告：没有 ResultOutput
        var hasOutput = flow.Operators.Any(op =>
            op.OperatorType == "ResultOutput" ||
            (metaMap.TryGetValue(op.TempId, out var meta) && meta.Category == "输出"));

        if (!hasOutput)
        {
            result.AddWarning(
                "工作流没有结果输出算子，建议添加 ResultOutput",
                code: "missing_result_output",
                category: "completeness",
                relatedFields: ["operators"],
                repairHint: "请补充 ResultOutput 或其他输出类算子，保证结果可消费。");
        }
    }
}

