// AiFlowValidator.cs
// AI 娴佺▼鏍￠獙鍣?// 瀵?AI 鐢熸垚娴佺▼杩涜缁撴瀯涓庤鍒欐牎楠?// 浣滆€咃細铇呰姕鍚?using Acme.Product.Core.DTOs;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Services;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Acme.Product.Infrastructure.AI;

/// <summary>
/// 鏍￠獙 AI 鐢熸垚鐨勫伐浣滄祦鏄惁婊¤冻鎵€鏈夌害鏉?
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
        "\u7f3a\u5c11",      // 缺少
        "\u672a\u914d\u7f6e",// 未配置
        "\u672a\u63d0\u4f9b",// 未提供
        "\u65e0"             // 无
    ];
    private static readonly string[] AntiPatternConjunctionMarkers =
    [
        " and ",
        " + ",
        " together ",
        "\u540c\u65f6",      // 同时
        "\u5e76\u4e14"       // 并且
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
                "AI 鏈敓鎴愪换浣曠畻瀛?,
                code: "empty_workflow",
                category: "structure",
                relatedFields: ["operators"],
                repairHint: "璇疯嚦灏戠敓鎴愪竴涓湁鏁堢畻瀛愶紝骞惰ˉ榻?operators 鏁扮粍銆?);
            return result;
        }

        // 寤虹珛 tempId 鈫?绠楀瓙鍏冩暟鎹?鐨勬槧灏勶紝鐢ㄤ簬鍚庣画鏍￠獙
        var operatorMetaMap = new Dictionary<string, OperatorMetadata>();

        // 1. 鏍￠獙绠楀瓙绫诲瀷鍚堟硶鎬?
        ValidateOperatorTypes(generatedFlow, result, operatorMetaMap);

        // 濡傛灉绠楀瓙绫诲瀷鏍￠獙澶辫触锛屽悗缁牎楠屾剰涔変笉澶?
        if (!result.IsValid)
            return result;

        // 2. 鏍￠獙绔彛鍚嶅悎娉曟€у拰绫诲瀷鍏煎鎬?
        ValidateConnections(generatedFlow, result, operatorMetaMap);

        // 3. 鏍￠獙鏃犵幆璺?
        ValidateNoCycles(generatedFlow, result);

        // 4. 鏍￠獙杈撳叆绔彛涓嶉噸澶嶅崰鐢?
        ValidateNoDuplicateInputs(generatedFlow, result);

        // 5. 鏍￠獙鍙傛暟鍚堟硶鎬э紙鏁板€艰寖鍥淬€佹灇涓剧被鍨嬶級
        ValidateParameters(generatedFlow, result, operatorMetaMap);

        // 6. 鍩轰簬 operator_knowledge_graph.json 鐨勮祫婧愪笌鍙嶆ā寮忔牎楠屽叆鍙?
        ValidateKnowledgeGraphRules(generatedFlow, result);

        // 7. 璀﹀憡锛堜笉闃绘鐢熸垚锛屼絾璁板綍锛?
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

            // 妫€鏌?tempId 鏍煎紡
            if (string.IsNullOrWhiteSpace(op.TempId))
            {
                result.AddError(
                    "瀛樺湪绠楀瓙鐨?tempId 涓虹┖",
                    code: "missing_temp_id",
                    category: "structure",
                    relatedFields: [$"{operatorField}.tempId"],
                    repairHint: "涓烘瘡涓畻瀛愯ˉ鍏呭敮涓€ tempId锛屼緥濡?op_1銆乷p_2銆?);
                continue;
            }

            if (allTempIds.Contains(op.TempId))
            {
                result.AddError(
                    $"tempId 閲嶅锛歿op.TempId}",
                    code: "duplicate_temp_id",
                    category: "structure",
                    relatedFields: [$"{operatorField}.tempId"],
                    operatorId: op.TempId,
                    repairHint: "纭繚姣忎釜绠楀瓙閮戒娇鐢ㄥ敮涓€ tempId銆?);
                continue;
            }
            allTempIds.Add(op.TempId);

            // 妫€鏌ョ畻瀛愮被鍨嬫槸鍚﹀湪鏋氫妇涓?            if (!Enum.TryParse<OperatorType>(op.OperatorType, out var operatorType))
            {
                result.AddError(
                    $"绠楀瓙绫诲瀷涓嶅瓨鍦細{op.OperatorType}锛坱empId={op.TempId}锛夈€傝浣跨敤绠楀瓙鐩綍涓殑 operator_id 鍊笺€?,
                    code: "unknown_operator_type",
                    category: "operator",
                    relatedFields: [$"{operatorField}.operatorType"],
                    operatorId: op.TempId,
                    repairHint: "鎶?operatorType 鏀规垚宸叉敞鍐岀殑 OperatorType 鏋氫妇鍚嶃€?);
                continue;
            }

            // 妫€鏌ョ畻瀛愬厓鏁版嵁鏄惁宸叉敞鍐?            var metadata = _operatorFactory.GetMetadata(operatorType);
            if (metadata == null)
            {
                result.AddError(
                    $"绠楀瓙 {op.OperatorType} 鏈湪绠楀瓙宸ュ巶涓敞鍐?,
                    code: "operator_not_registered",
                    category: "operator",
                    relatedFields: [$"{operatorField}.operatorType"],
                    operatorId: op.TempId,
                    repairHint: "璇锋敼鐢ㄥ凡缁忔敞鍐岀殑绠楀瓙绫诲瀷锛屾垨绉婚櫎璇ユ棤鏁堢畻瀛愩€?);
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

            // 妫€鏌ユ簮绠楀瓙瀛樺湪
            if (!metaMap.TryGetValue(conn.SourceTempId, out var sourceMeta))
            {
                result.AddError(
                    $"杩炵嚎寮曠敤浜嗕笉瀛樺湪鐨勬簮绠楀瓙 tempId锛歿conn.SourceTempId}",
                    code: "missing_source_operator",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.sourceTempId"
                    ],
                    sourceTempId: conn.SourceTempId,
                    repairHint: "淇 sourceTempId锛岀‘淇濆畠寮曠敤宸插畾涔夌殑绠楀瓙 tempId銆?);
                continue;
            }

            // 妫€鏌ョ洰鏍囩畻瀛愬瓨鍦?            if (!metaMap.TryGetValue(conn.TargetTempId, out var targetMeta))
            {
                result.AddError(
                    $"杩炵嚎寮曠敤浜嗕笉瀛樺湪鐨勭洰鏍囩畻瀛?tempId锛歿conn.TargetTempId}",
                    code: "missing_target_operator",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.targetTempId"
                    ],
                    targetTempId: conn.TargetTempId,
                    repairHint: "淇 targetTempId锛岀‘淇濆畠寮曠敤宸插畾涔夌殑绠楀瓙 tempId銆?);
                continue;
            }

            // 妫€鏌ユ簮绔彛瀛樺湪
            var sourcePort = sourceMeta.OutputPorts.FirstOrDefault(p => p.Name == conn.SourcePortName);
            if (sourcePort == null)
            {
                result.AddError(
                    $"绠楀瓙 {conn.SourceTempId}({sourceMeta.DisplayName}) 娌℃湁鍚嶄负 '{conn.SourcePortName}' 鐨勮緭鍑虹鍙ｃ€? +
                    $"鍙敤杈撳嚭绔彛锛歿string.Join(", ", sourceMeta.OutputPorts.Select(p => p.Name))}",
                    code: "missing_output_port",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.sourceTempId",
                        $"{connectionField}.sourcePortName"
                    ],
                    sourceTempId: conn.SourceTempId,
                    sourcePortName: conn.SourcePortName,
                    repairHint: "鎶?sourcePortName 鏀规垚璇ユ簮绠楀瓙鐨勬湁鏁堣緭鍑虹鍙ｅ悕銆?);
                continue;
            }

            // 妫€鏌ョ洰鏍囩鍙ｅ瓨鍦?            var targetPort = targetMeta.InputPorts.FirstOrDefault(p => p.Name == conn.TargetPortName);
            if (targetPort == null)
            {
                result.AddError(
                    $"绠楀瓙 {conn.TargetTempId}({targetMeta.DisplayName}) 娌℃湁鍚嶄负 '{conn.TargetPortName}' 鐨勮緭鍏ョ鍙ｃ€? +
                    $"鍙敤杈撳叆绔彛锛歿string.Join(", ", targetMeta.InputPorts.Select(p => p.Name))}",
                    code: "missing_input_port",
                    category: "connection",
                    relatedFields:
                    [
                        $"{connectionField}.targetTempId",
                        $"{connectionField}.targetPortName"
                    ],
                    targetTempId: conn.TargetTempId,
                    targetPortName: conn.TargetPortName,
                    repairHint: "鎶?targetPortName 鏀规垚璇ョ洰鏍囩畻瀛愮殑鏈夋晥杈撳叆绔彛鍚嶃€?);
                continue;
            }

            // 妫€鏌ョ被鍨嬪吋瀹规€?            if (!AreTypesCompatible(sourcePort.DataType, targetPort.DataType))
            {
                result.AddError(
                    $"绔彛绫诲瀷涓嶅吋瀹癸細{conn.SourceTempId}.{conn.SourcePortName}({sourcePort.DataType}) 鈫?" +
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
                    repairHint: "璇锋敼鐢ㄧ被鍨嬪吋瀹圭殑绔彛杩炵嚎锛屾垨琛ュ厖涓棿杞崲绠楀瓙銆?);
            }
        }
    }

    private bool AreTypesCompatible(PortDataType source, PortDataType target)
    {
        // Any 绫诲瀷涓庝换浣曠被鍨嬪吋瀹?
        if (source == PortDataType.Any || target == PortDataType.Any)
            return true;
        // 鐩稿悓绫诲瀷鍏煎
        if (source == target)
            return true;

        // 鏁板€肩被鍨嬩簰閫氾紙Integer 鈫?Float锛?
        var numericTypes = new[] { PortDataType.Integer, PortDataType.Float };
        if (numericTypes.Contains(source) && numericTypes.Contains(target))
            return true;

        // 鍑犱綍绫诲瀷浜掗€氾紙Point 鈫?Rectangle锛?
        var geometryTypes = new[] { PortDataType.Point, PortDataType.Rectangle };
        if (geometryTypes.Contains(source) && geometryTypes.Contains(target))
            return true;

        // String 鍙互浣滀负鏁板€肩被鍨嬬殑杈撳叆锛堣繍琛屾椂杞崲锛?
        if (source == PortDataType.String && numericTypes.Contains(target))
            return true;

        // Boolean 鍙互浣滀负 Integer 鐨勮緭鍏ワ紙true=1, false=0锛?
        if (source == PortDataType.Boolean && target == PortDataType.Integer)
            return true;

        return false;
    }

    private void ValidateNoCycles(AiGeneratedFlowJson flow, AiValidationResult result)
    {
        if (flow.Connections == null || flow.Connections.Count == 0)
            return;

        // 鏋勫缓閭绘帴琛?
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var op in flow.Operators)
            adjacency[op.TempId] = new List<string>();

        foreach (var conn in flow.Connections)
        {
            if (adjacency.ContainsKey(conn.SourceTempId))
                adjacency[conn.SourceTempId].Add(conn.TargetTempId);
        }

        // DFS 妫€娴嬬幆璺?
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();

        foreach (var node in adjacency.Keys)
        {
            if (HasCycle(node, adjacency, visited, inStack))
            {
                result.AddError(
                    "宸ヤ綔娴佷腑瀛樺湪鐜矾锛堝惊鐜緷璧栵級锛岃閲嶆柊璁捐娴佺▼缁撴瀯",
                    code: "cycle_detected",
                    category: "graph",
                    relatedFields: ["connections"],
                    repairHint: "绉婚櫎褰㈡垚鍥炶矾鐨勮繛绾匡紝淇濇寔娴佺▼涓?DAG銆?);
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
                    $"杈撳叆绔彛琚噸澶嶈繛鎺ワ細绠楀瓙 {conn.TargetTempId} 鐨?{conn.TargetPortName} 绔彛鍙兘鎺ユ敹涓€鏉¤繛绾?,
                    code: "duplicate_input_connection",
                    category: "connection",
                    relatedFields:
                    [
                        $"connections[{index}].targetTempId",
                        $"connections[{index}].targetPortName"
                    ],
                    targetTempId: conn.TargetTempId,
                    targetPortName: conn.TargetPortName,
                    repairHint: "鍒犻櫎閲嶅杩炵嚎锛岀‘淇濇瘡涓緭鍏ョ鍙ｆ渶澶氭帴鏀朵竴鏉¤繛鎺ャ€?);
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

            op.Parameters ??= new Dictionary<string, string>();
            ApplyIntelligentDefaults(op, metadata, result, operatorField);

            foreach (var requiredParam in metadata.Parameters.Where(p => p.IsRequired))
            {
                if (!op.Parameters.ContainsKey(requiredParam.Name))
                {
                    result.AddWarning(
                        $"绠楀瓙 {op.TempId}({metadata.DisplayName}) 缂哄皯蹇呭～鍙傛暟 '{requiredParam.Name}'锛屼笖鏃犲彲鐢ㄩ粯璁ゅ€?,
                        code: "missing_required_parameter",
                        category: "parameter",
                        relatedFields: [$"{operatorField}.parameters.{requiredParam.Name}"],
                        operatorId: op.TempId,
                        parameterName: requiredParam.Name,
                        repairHint: $"璇蜂负绠楀瓙 {op.TempId} 琛ラ綈鍙傛暟 {requiredParam.Name}銆?);
                }
            }

            foreach (var kvp in op.Parameters.ToList())
            {
                var paramName = kvp.Key;
                var paramValueStr = kvp.Value?.ToString() ?? string.Empty;

                var paramDef = metadata.Parameters.FirstOrDefault(p => p.Name == paramName);
                if (paramDef == null)
                {
                    // 鍙傛暟涓嶅瓨鍦紝浠呬綔涓鸿鍛?                    result.AddWarning(
                        $"绠楀瓙 {op.TempId}({metadata.DisplayName}) 鐢熸垚浜嗘湭鐭ョ殑鍙傛暟 '{paramName}'",
                        code: "unknown_parameter",
                        category: "parameter",
                        relatedFields: [$"{operatorField}.parameters.{paramName}"],
                        operatorId: op.TempId,
                        parameterName: paramName,
                        repairHint: "璇风Щ闄ゆ湭鐭ュ弬鏁帮紝鎴栨敼鎴愯绠楀瓙瀹氫箟涓瓨鍦ㄧ殑鍙傛暟鍚嶃€?);
                    continue;
                }

                // 鏁板€艰寖鍥存牎楠?+ 鑷姩 Clamp
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
                            $"绠楀瓙 {op.TempId}({metadata.DisplayName}) 鐨勫弬鏁?'{paramName}' 鍊?{numValue} 瓒呭嚭鑼冨洿锛屽凡鑷姩璋冩暣涓?{clampedValue}",
                            code: "parameter_clamped",
                            category: "parameter",
                            relatedFields: [$"{operatorField}.parameters.{paramName}"],
                            operatorId: op.TempId,
                            parameterName: paramName,
                            repairHint: $"璇峰湪涓嬩竴杞洿鎺ョ敓鎴?{paramName} 鐨勫悎娉曡寖鍥村€笺€?);
                    }
                }

                // 鏋氫妇鍊兼牎楠?
                if (paramDef.DataType.Equals("enum", StringComparison.OrdinalIgnoreCase) && paramDef.Options != null && paramDef.Options.Count > 0)
                {
                    var validValues = paramDef.Options.Select(o => o.Value).ToList();
                    if (!validValues.Contains(paramValueStr))
                    {
                        result.AddWarning(
                            $"绠楀瓙 {op.TempId}({metadata.DisplayName}) 鐨勬灇涓惧弬鏁?'{paramName}' 鍊间负 '{paramValueStr}' 涓嶅悎娉曪紝鏈夋晥鍊间负: {string.Join(", ", validValues)}",
                            code: "invalid_enum_value",
                            category: "parameter",
                            relatedFields: [$"{operatorField}.parameters.{paramName}"],
                            operatorId: op.TempId,
                            parameterName: paramName,
                            repairHint: $"璇锋妸 {paramName} 鏀规垚鏈夋晥鏋氫妇鍊间箣涓€锛歿string.Join(", ", validValues)}銆?);
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
                $"绠楀瓙 {op.TempId}({metadata.DisplayName}) 鐨勫繀濉弬鏁?'{paramDef.Name}' 缂哄け锛屽凡鑷姩濉厖榛樿鍊?{defaultValue}",
                code: "default_parameter_applied",
                category: "parameter",
                relatedFields: [$"{operatorField}.parameters.{paramDef.Name}"],
                operatorId: op.TempId,
                parameterName: paramDef.Name,
                repairHint: $"濡傞粯璁ゅ€间笉绗﹀悎鍦烘櫙锛岃鍦ㄤ笅涓€杞槑纭粰鍑?{paramDef.Name}銆?);
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
                        $"绠楀瓙 {item.Operator.TempId}({item.Operator.OperatorType}) 缂哄皯鐭ヨ瘑鍥捐氨瑕佹眰璧勬簮 {requiredResource}銆?,
                        code: "knowledge_required_resource_missing",
                        category: "knowledge",
                        relatedFields:
                        [
                            $"operators[{item.Index}].parameters",
                            "missingResources"
                        ],
                        operatorId: item.Operator.TempId,
                        parameterName: requiredResource,
                        repairHint: $"璇疯ˉ榻愯祫婧?{requiredResource}锛屾垨鍦?missingResources/pendingParameters 涓槑纭０鏄庤璧勬簮寰呮彁渚涖€?);
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
                    $"绠楀瓙 {op.TempId}({op.OperatorType}) 鍛戒腑鐭ヨ瘑鍥捐氨鍙嶆ā寮忥細{antiPattern}",
                    code: "knowledge_anti_pattern_detected",
                    category: "knowledge",
                    relatedFields: relatedFields,
                    operatorId: op.TempId,
                    repairHint: "璇疯皟鏁寸畻瀛愭嫇鎵戞垨鍙傛暟锛岄伩鍏嶈Е鍙戝凡鐭ュ弽妯″紡銆?);
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
            {
                return true;
            }
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
        {
            return IsParameterValueMissing(op.Parameters, parameterKeys);
        }

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
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim();
        return normalized.Equals("todo", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("tbd", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("your_", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("to_be_filled", StringComparison.OrdinalIgnoreCase);
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
        // 璀﹀憡锛氭病鏈夋簮绠楀瓙
        var hasSource = flow.Operators.Any(op =>
            metaMap.TryGetValue(op.TempId, out var meta) &&
            meta.InputPorts.Count == 0);

        if (!hasSource)
        {
            result.AddWarning(
                "宸ヤ綔娴佹病鏈夊浘鍍忔簮绠楀瓙锛堟棤杈撳叆绔彛鐨勭畻瀛愶級锛屽缓璁坊鍔?ImageAcquisition",
                code: "missing_image_source",
                category: "completeness",
                relatedFields: ["operators"],
                repairHint: "璇疯ˉ鍏呭浘鍍忔簮绠楀瓙锛屼緥濡?ImageAcquisition銆?);
        }

        // 璀﹀憡锛氭病鏈?ResultOutput
        var hasOutput = flow.Operators.Any(op =>
            op.OperatorType == "ResultOutput" ||
            (metaMap.TryGetValue(op.TempId, out var meta) && meta.Category == "杈撳嚭"));

        if (!hasOutput)
        {
            result.AddWarning(
                "宸ヤ綔娴佹病鏈夌粨鏋滆緭鍑虹畻瀛愶紝寤鸿娣诲姞 ResultOutput",
                code: "missing_result_output",
                category: "completeness",
                relatedFields: ["operators"],
                repairHint: "璇疯ˉ鍏?ResultOutput 鎴栧叾浠栬緭鍑虹被绠楀瓙锛屼繚璇佺粨鏋滃彲娑堣垂銆?);
        }
    }
}
