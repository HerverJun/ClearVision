using System.Globalization;
using System.Text.Json;
using Acme.Product.Core.DTOs;

namespace Acme.Product.Infrastructure.AI;

public interface IAiFlowResponseParser
{
    AiFlowParseResult Parse(string rawResponse);

    string Summarize(string? rawResponse);
}

public sealed class AiFlowParseResult
{
    public bool Success => Flow != null;
    public AiGeneratedFlowJson? Flow { get; init; }
    public string? CandidateJson { get; init; }
    public int CandidateCount { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string RepairHint { get; init; } = string.Empty;

    public static AiFlowParseResult Parsed(
        AiGeneratedFlowJson flow,
        string candidateJson,
        int candidateCount) => new()
    {
        Flow = flow,
        CandidateJson = candidateJson,
        CandidateCount = candidateCount
    };

    public static AiFlowParseResult Failed(
        string code,
        string category,
        string message,
        string repairHint,
        int candidateCount = 0) => new()
    {
        Code = code,
        Category = category,
        Message = message,
        RepairHint = repairHint,
        CandidateCount = candidateCount
    };
}

public sealed class AiFlowResponseParser : IAiFlowResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new FlexibleStringDictionaryJsonConverter()
        }
    };

    private static readonly string[] FlowWrapperPropertyNames =
    [
        "workflow",
        "flow",
        "generatedFlow",
        "generated_flow",
        "result",
        "data",
        "output",
        "final",
        "answer",
        "payload"
    ];

    private static readonly string[] OperatorArrayPropertyNames =
    [
        "operators",
        "nodes",
        "steps",
        "modules"
    ];

    private static readonly string[] ConnectionArrayPropertyNames =
    [
        "connections",
        "edges",
        "links",
        "wires"
    ];

    private static readonly string[] ReviewPropertyNames =
    [
        "parametersNeedingReview",
        "parameters_needing_review",
        "parametersToReview",
        "parameters_to_review",
        "reviewParameters",
        "review_parameters",
        "pendingParameters",
        "pending_parameters"
    ];

    private static readonly string[] ExplanationPropertyNames =
    [
        "explanation",
        "summary",
        "description",
        "rationale"
    ];

    public AiFlowParseResult Parse(string rawResponse)
    {
        var json = StripJsonFences(rawResponse);
        var candidates = EnumerateBalancedJsonObjects(json);
        if (candidates.Count == 0)
        {
            return AiFlowParseResult.Failed(
                "invalid_json",
                "format",
                "AI 响应中找不到完整 JSON 对象",
                "请只返回一个完整 JSON 对象，不要附加 markdown、解释文本或多余前后缀。");
        }

        var orderedCandidates = candidates
            .Select((candidate, index) => new { Candidate = candidate, Index = index })
            .Where(item => LooksLikeGeneratedFlowJson(item.Candidate))
            .OrderByDescending(item => item.Index)
            .Select(item => item.Candidate)
            .Concat(candidates.AsEnumerable().Reverse())
            .Distinct(StringComparer.Ordinal);

        foreach (var candidate in orderedCandidates)
        {
            if (TryDeserializeGeneratedFlow(candidate, out var flow) && flow != null)
            {
                return AiFlowParseResult.Parsed(flow, candidate, candidates.Count);
            }
        }

        return AiFlowParseResult.Failed(
            "unsupported_workflow_json",
            "structure",
            "AI 返回的是合法 JSON 片段，但不是可识别的工作流结构",
            "请返回包含 operators/connections 的工作流 JSON；如使用 workflow、flow、result 等外壳，请确保内部对象包含完整工作流字段。",
            candidates.Count);
    }

    public string Summarize(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return "最近一次模型未返回可用正文。";

        var normalized = NormalizeJsonEnvelope(rawResponse);
        if (TryDeserializeGeneratedFlow(normalized, out var parsed) && parsed != null)
        {
            return $"最近一次输出包含 {parsed.Operators?.Count ?? 0} 个算子、" +
                   $"{parsed.Connections?.Count ?? 0} 条连线，说明文本长度 {parsed.Explanation?.Length ?? 0}。";
        }

        var trimmed = TrimRetryOutput(rawResponse)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        if (trimmed.Length > 160)
            trimmed = trimmed[..160] + "...";

        return $"最近一次输出未能解析为标准工作流 JSON，长度 {rawResponse.Trim().Length} 字符，片段：{trimmed}";
    }

    private static string NormalizeJsonEnvelope(string rawResponse)
    {
        var json = StripJsonFences(rawResponse);
        var candidate = EnumerateBalancedJsonObjects(json).FirstOrDefault();
        return candidate ?? string.Empty;
    }

    private static string StripJsonFences(string rawResponse)
    {
        var json = rawResponse.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            json = json[7..];
        else if (json.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            json = json[3..];

        if (json.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            json = json[..^3];

        return json.Trim();
    }

    private static List<string> EnumerateBalancedJsonObjects(string text)
    {
        var objects = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return objects;

        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
                continue;
            }

            if (ch == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    objects.Add(text[start..(i + 1)]);
                    start = -1;
                }
            }
        }

        return objects;
    }

    private static bool LooksLikeGeneratedFlowJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   (HasGeneratedFlowCoreProperty(root) ||
                    HasAnyProperty(root, FlowWrapperPropertyNames) ||
                    HasProperty(root, "explanation"));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.EnumerateObject().Any(property =>
                   property.NameEquals(propertyName) ||
                   string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAnyProperty(JsonElement element, IEnumerable<string> propertyNames)
    {
        return element.ValueKind == JsonValueKind.Object &&
               propertyNames.Any(name => HasProperty(element, name));
    }

    private static bool HasGeneratedFlowCoreProperty(JsonElement element)
    {
        return HasGeneratedFlowStructureProperty(element) ||
               HasAnyProperty(element, ReviewPropertyNames);
    }

    private static bool HasGeneratedFlowStructureProperty(JsonElement element)
    {
        return HasAnyProperty(element, OperatorArrayPropertyNames) ||
               HasAnyProperty(element, ConnectionArrayPropertyNames);
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        IEnumerable<string> propertyNames,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in propertyNames)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(name) ||
                        string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value) =>
        TryGetPropertyIgnoreCase(element, [propertyName], out value);

    private static bool TryDeserializeGeneratedFlow(string json, out AiGeneratedFlowJson? flow)
    {
        try
        {
            if (TryNormalizeGeneratedFlowPayload(json, out flow))
                return flow != null;

            flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(json, JsonOptions);
            return flow != null;
        }
        catch (JsonException)
        {
            flow = null;
            return false;
        }
    }

    private static bool TryNormalizeGeneratedFlowPayload(string json, out AiGeneratedFlowJson? flow)
    {
        flow = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        using var document = JsonDocument.Parse(json);
        return TryBuildGeneratedFlowFromElement(document.RootElement, null, 0, out flow);
    }

    private static bool TryBuildGeneratedFlowFromElement(
        JsonElement element,
        string? inheritedExplanation,
        int depth,
        out AiGeneratedFlowJson? flow)
    {
        flow = null;
        if (depth > 4)
            return false;

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var candidate in EnumerateBalancedJsonObjects(text))
            {
                if (TryNormalizeGeneratedFlowPayload(candidate, out flow))
                    return true;
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryBuildGeneratedFlowFromElement(item, inheritedExplanation, depth + 1, out flow))
                    return true;
            }

            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var localExplanation = ReadStringProperty(element, ExplanationPropertyNames) ?? inheritedExplanation;

        if (HasGeneratedFlowStructureProperty(element))
        {
            flow = BuildGeneratedFlow(element, localExplanation);
            return true;
        }

        foreach (var wrapperName in FlowWrapperPropertyNames)
        {
            if (TryGetPropertyIgnoreCase(element, wrapperName, out var wrapper) &&
                TryBuildGeneratedFlowFromElement(wrapper, localExplanation, depth + 1, out flow))
            {
                if (flow != null && TryGetPropertyIgnoreCase(element, ReviewPropertyNames, out var reviewElement))
                    MergeReviewParameters(flow.ParametersNeedingReview, ParseParametersNeedingReview(reviewElement));

                return true;
            }
        }

        if (HasGeneratedFlowCoreProperty(element))
        {
            flow = BuildGeneratedFlow(element, localExplanation);
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (TryBuildGeneratedFlowFromElement(property.Value, localExplanation, depth + 1, out flow))
                return true;
        }

        return false;
    }

    private static AiGeneratedFlowJson BuildGeneratedFlow(JsonElement element, string? inheritedExplanation)
    {
        var direct = TryDeserializeGeneratedFlowDirect(element);
        var flow = direct ?? new AiGeneratedFlowJson();

        flow.SchemaVersion = ReadStringProperty(element, ["schemaVersion", "schema_version", "version"]) ?? flow.SchemaVersion;
        flow.GenerationMode = ReadStringProperty(element, ["generationMode", "generation_mode"]) ?? flow.GenerationMode;
        flow.TemplateLockLevel = ReadStringProperty(element, ["templateLockLevel", "template_lock_level"]) ?? flow.TemplateLockLevel;
        flow.Explanation = ReadStringProperty(element, ExplanationPropertyNames) ?? inheritedExplanation ?? flow.Explanation;

        if (TryGetPropertyIgnoreCase(element, OperatorArrayPropertyNames, out var operatorsElement) &&
            operatorsElement.ValueKind == JsonValueKind.Array)
        {
            flow.Operators = ParseGeneratedOperators(operatorsElement);
        }

        if (TryGetPropertyIgnoreCase(element, ConnectionArrayPropertyNames, out var connectionsElement) &&
            connectionsElement.ValueKind == JsonValueKind.Array)
        {
            flow.Connections = ParseGeneratedConnections(connectionsElement);
        }

        if (TryGetPropertyIgnoreCase(element, ReviewPropertyNames, out var reviewElement))
        {
            flow.ParametersNeedingReview = ParseParametersNeedingReview(reviewElement);
        }

        return flow;
    }

    private static AiGeneratedFlowJson? TryDeserializeGeneratedFlowDirect(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<AiGeneratedFlowJson>(element.GetRawText(), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<AiGeneratedOperator> ParseGeneratedOperators(JsonElement operatorsElement)
    {
        var operators = new List<AiGeneratedOperator>();
        foreach (var item in operatorsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var operatorType = ReadStringProperty(item,
                ["operatorType", "operator_type", "operator_id", "type", "kind"]) ?? string.Empty;
            var tempId = ReadStringProperty(item,
                ["tempId", "temp_id", "id", "operatorId", "operatorIdTemp", "nodeId", "node_id"]) ?? string.Empty;
            var displayName = ReadStringProperty(item,
                ["displayName", "display_name", "name", "label", "title"]) ?? operatorType;

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryGetPropertyIgnoreCase(item, ["parameters", "params", "settings", "config"], out var parametersElement))
            {
                parameters = ReadStringDictionary(parametersElement);
            }

            operators.Add(new AiGeneratedOperator
            {
                TempId = tempId,
                OperatorType = operatorType,
                DisplayName = displayName,
                Parameters = parameters
            });
        }

        return operators;
    }

    private static List<AiGeneratedConnection> ParseGeneratedConnections(JsonElement connectionsElement)
    {
        var connections = new List<AiGeneratedConnection>();
        foreach (var item in connectionsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                if (TryParseConnectionString(item.GetString(), out var stringConnection))
                    connections.Add(stringConnection);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var sourceTempId = ReadStringProperty(item,
                ["sourceTempId", "source_temp_id", "sourceOperatorId", "source_operator_id", "sourceId", "source_id", "fromTempId", "fromId", "fromOperatorId"]) ?? string.Empty;
            var sourcePortName = ReadStringProperty(item,
                ["sourcePortName", "source_port_name", "sourcePort", "source_port", "sourceOutput", "outputPort", "fromPort", "from_port"]) ?? string.Empty;
            var targetTempId = ReadStringProperty(item,
                ["targetTempId", "target_temp_id", "targetOperatorId", "target_operator_id", "targetId", "target_id", "toTempId", "toId", "toOperatorId"]) ?? string.Empty;
            var targetPortName = ReadStringProperty(item,
                ["targetPortName", "target_port_name", "targetPort", "target_port", "targetInput", "inputPort", "toPort", "to_port"]) ?? string.Empty;

            if (TryGetPropertyIgnoreCase(item, ["source", "from"], out var sourceEndpoint))
                MergeEndpoint(sourceEndpoint, ref sourceTempId, ref sourcePortName);

            if (TryGetPropertyIgnoreCase(item, ["target", "to"], out var targetEndpoint))
                MergeEndpoint(targetEndpoint, ref targetTempId, ref targetPortName);

            connections.Add(new AiGeneratedConnection
            {
                SourceTempId = sourceTempId,
                SourcePortName = sourcePortName,
                TargetTempId = targetTempId,
                TargetPortName = targetPortName
            });
        }

        return connections;
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.IsNullOrWhiteSpace(property.Name))
                    result[property.Name] = ReadJsonValueAsString(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var name = ReadStringProperty(item, ["name", "key", "parameterName", "parameter_name", "paramName", "param_name"]);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (TryGetPropertyIgnoreCase(item, ["value", "defaultValue", "default_value"], out var valueElement))
                    result[name] = ReadJsonValueAsString(valueElement);
            }
        }

        return result;
    }

    private static Dictionary<string, List<string>> ParseParametersNeedingReview(JsonElement element)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var names = ReadStringList(property.Value);
                foreach (var name in names)
                    AddReviewParameter(result, property.Name, name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    AddReviewToken(result, item.GetString());
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var operatorId = ReadStringProperty(item,
                    ["operatorId", "operator_id", "tempId", "temp_id", "id", "nodeId", "node_id"]) ?? string.Empty;
                var names = ReadReviewNamesFromObject(item);
                foreach (var name in names)
                    AddReviewParameter(result, operatorId, name);
            }
        }

        return result;
    }

    private static List<string> ReadReviewNamesFromObject(JsonElement element)
    {
        if (TryGetPropertyIgnoreCase(
                element,
                ["parameterNames", "parameter_names", "parameters", "params", "names", "fields"],
                out var namesElement))
        {
            return ReadStringList(namesElement);
        }

        var single = ReadStringProperty(element, ["parameterName", "parameter_name", "parameter", "name", "field"]);
        return string.IsNullOrWhiteSpace(single) ? new List<string>() : [single];
    }

    private static List<string> ReadStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? new List<string>() : [value];
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var value = ReadStringProperty(item, ["name", "parameterName", "parameter_name", "field"]);
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }
            }

            return values;
        }

        if (element.ValueKind == JsonValueKind.Object)
            return ReadReviewNamesFromObject(element);

        return new List<string>();
    }

    private static void AddReviewToken(Dictionary<string, List<string>> result, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var trimmed = token.Trim();
        var splitAt = trimmed.LastIndexOfAny(['.', ':', '/']);
        if (splitAt > 0 && splitAt < trimmed.Length - 1)
            AddReviewParameter(result, trimmed[..splitAt], trimmed[(splitAt + 1)..]);
    }

    private static void AddReviewParameter(Dictionary<string, List<string>> result, string operatorId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(operatorId) || string.IsNullOrWhiteSpace(parameterName))
            return;

        if (!result.TryGetValue(operatorId, out var names))
        {
            names = new List<string>();
            result[operatorId] = names;
        }

        if (!names.Contains(parameterName, StringComparer.OrdinalIgnoreCase))
            names.Add(parameterName);
    }

    private static void MergeReviewParameters(
        Dictionary<string, List<string>> target,
        Dictionary<string, List<string>> source)
    {
        foreach (var pair in source)
        {
            foreach (var parameterName in pair.Value)
                AddReviewParameter(target, pair.Key, parameterName);
        }
    }

    private static string? ReadStringProperty(JsonElement element, IEnumerable<string> propertyNames)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyNames, out var value))
            return null;

        var text = ReadJsonValueAsString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string ReadJsonValueAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture)
                : element.GetDouble().ToString("0.############################", CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
            _ => string.Empty
        };
    }

    private static void MergeEndpoint(JsonElement endpoint, ref string tempId, ref string portName)
    {
        if (endpoint.ValueKind == JsonValueKind.String)
        {
            var (parsedTempId, parsedPortName) = ParseEndpointString(endpoint.GetString());
            if (string.IsNullOrWhiteSpace(tempId))
                tempId = parsedTempId;
            if (string.IsNullOrWhiteSpace(portName))
                portName = parsedPortName;
            return;
        }

        if (endpoint.ValueKind != JsonValueKind.Object)
            return;

        var endpointTempId = ReadStringProperty(endpoint,
            ["tempId", "temp_id", "operatorId", "operator_id", "id", "nodeId", "node_id"]);
        var endpointPortName = ReadStringProperty(endpoint,
            ["portName", "port_name", "port", "name", "outputPort", "inputPort"]);

        if (string.IsNullOrWhiteSpace(tempId) && !string.IsNullOrWhiteSpace(endpointTempId))
            tempId = endpointTempId;
        if (string.IsNullOrWhiteSpace(portName) && !string.IsNullOrWhiteSpace(endpointPortName))
            portName = endpointPortName;
    }

    private static bool TryParseConnectionString(string? value, out AiGeneratedConnection connection)
    {
        connection = new AiGeneratedConnection();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var separator = value.Contains("->", StringComparison.Ordinal)
            ? "->"
            : value.Contains("=>", StringComparison.Ordinal)
                ? "=>"
                : null;
        if (separator == null)
            return false;

        var parts = value.Split([separator], 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        var (sourceTempId, sourcePortName) = ParseEndpointString(parts[0]);
        var (targetTempId, targetPortName) = ParseEndpointString(parts[1]);
        connection = new AiGeneratedConnection
        {
            SourceTempId = sourceTempId,
            SourcePortName = sourcePortName,
            TargetTempId = targetTempId,
            TargetPortName = targetPortName
        };
        return true;
    }

    private static (string TempId, string PortName) ParseEndpointString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, string.Empty);

        var trimmed = value.Trim();
        var splitAt = trimmed.LastIndexOfAny(['.', ':', '/']);
        if (splitAt > 0 && splitAt < trimmed.Length - 1)
            return (trimmed[..splitAt].Trim(), trimmed[(splitAt + 1)..].Trim());

        return (trimmed, string.Empty);
    }

    private static string TrimRetryOutput(string rawResponse)
    {
        const int maxLength = 6000;
        if (string.IsNullOrWhiteSpace(rawResponse))
            return string.Empty;

        var trimmed = rawResponse.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength] + "\n...<truncated>";
    }
}
