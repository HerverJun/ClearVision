using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentFlowDraftNormalizer
{
    public static VisionAgentFlowDraftNormalizeResult Normalize(
        JsonElement arguments,
        VisionAgentToolContext context)
    {
        try
        {
            var entryOperatorTempId = ReadString(arguments, "entryOperatorTempId");
            if (TryReadFlowElement(arguments, out var flowElement))
            {
                return VisionAgentFlowDraftNormalizeResult.Ok(ReadDraft(flowElement, entryOperatorTempId));
            }

            var flowJson =
                ReadString(arguments, "flowJson") ??
                ReadString(arguments, "existingFlowJson") ??
                context.ExistingFlowJson;
            if (!string.IsNullOrWhiteSpace(flowJson))
            {
                using var doc = JsonDocument.Parse(flowJson);
                return VisionAgentFlowDraftNormalizeResult.Ok(ReadDraft(doc.RootElement, entryOperatorTempId));
            }

            if (LooksLikeFlow(arguments))
            {
                return VisionAgentFlowDraftNormalizeResult.Ok(ReadDraft(arguments, entryOperatorTempId));
            }

            return VisionAgentFlowDraftNormalizeResult.Ok(new VisionAgentFlowDraft(
                [],
                [],
                entryOperatorTempId));
        }
        catch (JsonException ex)
        {
            return VisionAgentFlowDraftNormalizeResult.Fail(
                "invalid_flow_json",
                ex.Message);
        }
    }

    private static bool TryReadFlowElement(JsonElement arguments, out JsonElement flowElement)
    {
        flowElement = default;
        if (arguments.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(arguments, "flow", out var flow))
        {
            return false;
        }

        if (flow.ValueKind == JsonValueKind.Object)
        {
            flowElement = flow;
            return true;
        }

        if (flow.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(flow.GetString()))
        {
            using var doc = JsonDocument.Parse(flow.GetString()!);
            flowElement = doc.RootElement.Clone();
            return true;
        }

        return false;
    }

    private static VisionAgentFlowDraft ReadDraft(
        JsonElement root,
        string? entryOperatorTempId)
    {
        var entry = entryOperatorTempId ?? ReadString(root, "entryOperatorTempId");
        var operators = ReadArray(root, "operators")
            .Select(ReadOperator)
            .ToList();
        var connections = ReadArray(root, "connections")
            .Select(ReadConnection)
            .ToList();

        return new VisionAgentFlowDraft(operators, connections, entry);
    }

    private static VisionAgentFlowOperator ReadOperator(JsonElement element)
    {
        var parameters = ReadParameters(element);
        foreach (var name in new[] { "CameraBindingId", "ModelPath", "TemplatePath" })
        {
            if (!parameters.ContainsKey(name) &&
                TryGetProperty(element, name, out var directValue))
            {
                parameters[name] = ReadScalar(directValue);
            }
        }

        return new VisionAgentFlowOperator(
            ReadString(element, "tempId") ??
            ReadString(element, "operatorTempId") ??
            ReadString(element, "id") ??
            string.Empty,
            ReadString(element, "operatorType") ??
            ReadString(element, "type") ??
            string.Empty,
            parameters);
    }

    private static VisionAgentFlowConnection ReadConnection(JsonElement element)
    {
        return new VisionAgentFlowConnection(
            ReadEndpointTempId(element, "source", "sourceTempId", "sourceOperatorTempId", "sourceId"),
            ReadEndpointPortName(element, "source", "sourcePortName", "sourcePort", "sourceOutput", "outputPortName"),
            ReadEndpointTempId(element, "target", "targetTempId", "targetOperatorTempId", "targetId"),
            ReadEndpointPortName(element, "target", "targetPortName", "targetPort", "targetInput", "inputPortName"));
    }

    private static Dictionary<string, string?> ReadParameters(JsonElement element)
    {
        foreach (var propertyName in new[] { "parameters", "config", "settings" })
        {
            if (TryGetProperty(element, propertyName, out var parameters) &&
                parameters.ValueKind == JsonValueKind.Object)
            {
                return parameters.EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => ReadScalar(property.Value),
                        StringComparer.Ordinal);
            }
        }

        return new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    private static string ReadEndpointTempId(
        JsonElement element,
        string endpointPropertyName,
        params string[] directPropertyNames)
    {
        foreach (var propertyName in directPropertyNames)
        {
            var value = ReadString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (TryGetProperty(element, endpointPropertyName, out var endpoint))
        {
            if (endpoint.ValueKind == JsonValueKind.String)
            {
                return endpoint.GetString() ?? string.Empty;
            }

            if (endpoint.ValueKind == JsonValueKind.Object)
            {
                return ReadString(endpoint, "tempId") ??
                       ReadString(endpoint, "operatorTempId") ??
                       ReadString(endpoint, "id") ??
                       string.Empty;
            }
        }

        return string.Empty;
    }

    private static string ReadEndpointPortName(
        JsonElement element,
        string endpointPropertyName,
        params string[] directPropertyNames)
    {
        foreach (var propertyName in directPropertyNames)
        {
            var value = ReadString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (TryGetProperty(element, endpointPropertyName, out var endpoint) &&
            endpoint.ValueKind == JsonValueKind.Object)
        {
            return ReadString(endpoint, "portName") ??
                   ReadString(endpoint, "port") ??
                   string.Empty;
        }

        return string.Empty;
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               TryGetProperty(root, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];
    }

    private static bool LooksLikeFlow(JsonElement arguments)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               (TryGetProperty(arguments, "operators", out _) ||
                TryGetProperty(arguments, "connections", out _));
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText()
        };
    }
}

internal sealed record VisionAgentFlowDraftNormalizeResult
{
    public bool Success { get; init; }
    public VisionAgentFlowDraft Flow { get; init; } = new([], [], null);
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static VisionAgentFlowDraftNormalizeResult Ok(VisionAgentFlowDraft flow)
    {
        return new VisionAgentFlowDraftNormalizeResult
        {
            Success = true,
            Flow = flow
        };
    }

    public static VisionAgentFlowDraftNormalizeResult Fail(
        string errorCode,
        string errorMessage)
    {
        return new VisionAgentFlowDraftNormalizeResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}

internal sealed record VisionAgentFlowDraft(
    IReadOnlyList<VisionAgentFlowOperator> Operators,
    IReadOnlyList<VisionAgentFlowConnection> Connections,
    string? EntryOperatorTempId);

internal sealed record VisionAgentFlowOperator(
    string TempId,
    string OperatorType,
    IReadOnlyDictionary<string, string?> Parameters);

internal sealed record VisionAgentFlowConnection(
    string SourceTempId,
    string SourcePortName,
    string TargetTempId,
    string TargetPortName);
