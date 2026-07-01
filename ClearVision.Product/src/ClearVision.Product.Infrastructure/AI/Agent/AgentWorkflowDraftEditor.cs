using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentWorkflowDraftEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public bool TryApplyFinalContent(
        string finalContent,
        JsonElement currentDraft,
        out JsonElement editedDraft)
    {
        editedDraft = default;
        if (!TryParseObject(finalContent, out var root))
        {
            return false;
        }

        if (TryGetProperty(root, "workflowDraft", out var workflowDraft) &&
            workflowDraft.ValueKind == JsonValueKind.Object)
        {
            editedDraft = workflowDraft.Clone();
            return true;
        }

        if (TryGetProperty(root, "draftEdits", out var draftEdits) &&
            draftEdits.ValueKind == JsonValueKind.Array)
        {
            editedDraft = ApplyEdits(currentDraft, draftEdits);
            return editedDraft.ValueKind == JsonValueKind.Object;
        }

        return false;
    }

    public JsonElement ApplyEdits(JsonElement currentDraft, JsonElement draftEdits)
    {
        var root = currentDraft.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(currentDraft.GetRawText()) as JsonObject
            : null;
        root ??= new JsonObject();
        var operators = EnsureArray(root, "operators");
        var connections = EnsureArray(root, "connections");

        foreach (var edit in draftEdits.EnumerateArray())
        {
            if (edit.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var operation = ReadString(edit, "op") ?? ReadString(edit, "operation");
            switch (NormalizeOperation(operation))
            {
                case "add_operator":
                    AddObject(operators, edit, "operator");
                    break;
                case "remove_operator":
                    RemoveOperator(operators, connections, ReadString(edit, "tempId"));
                    break;
                case "replace_operator_parameter":
                    ReplaceOperatorParameter(
                        operators,
                        ReadString(edit, "tempId"),
                        ReadString(edit, "parameterName"),
                        TryGetProperty(edit, "value", out var value) ? value : default);
                    break;
                case "add_connection":
                    AddObject(connections, edit, "connection");
                    break;
                case "remove_connection":
                    RemoveConnection(
                        connections,
                        ReadString(edit, "sourceTempId"),
                        ReadString(edit, "targetTempId"));
                    break;
            }
        }

        using var doc = JsonDocument.Parse(root.ToJsonString(JsonOptions));
        return doc.RootElement.Clone();
    }

    private static JsonArray EnsureArray(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonArray existing)
        {
            return existing;
        }

        var created = new JsonArray();
        root[propertyName] = created;
        return created;
    }

    private static void AddObject(JsonArray array, JsonElement edit, string propertyName)
    {
        if (!TryGetProperty(edit, propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        array.Add(JsonNode.Parse(value.GetRawText()));
    }

    private static void RemoveOperator(JsonArray operators, JsonArray connections, string? tempId)
    {
        if (string.IsNullOrWhiteSpace(tempId))
        {
            return;
        }

        for (var index = operators.Count - 1; index >= 0; index--)
        {
            if (string.Equals(ReadString(operators[index], "tempId"), tempId, StringComparison.OrdinalIgnoreCase))
            {
                operators.RemoveAt(index);
            }
        }

        for (var index = connections.Count - 1; index >= 0; index--)
        {
            if (string.Equals(ReadString(connections[index], "sourceTempId"), tempId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ReadString(connections[index], "targetTempId"), tempId, StringComparison.OrdinalIgnoreCase))
            {
                connections.RemoveAt(index);
            }
        }
    }

    private static void ReplaceOperatorParameter(
        JsonArray operators,
        string? tempId,
        string? parameterName,
        JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(tempId) ||
            string.IsNullOrWhiteSpace(parameterName) ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return;
        }

        foreach (var node in operators)
        {
            if (!string.Equals(ReadString(node, "tempId"), tempId, StringComparison.OrdinalIgnoreCase) ||
                node is not JsonObject op)
            {
                continue;
            }

            if (op["parameters"] is not JsonObject parameters)
            {
                parameters = new JsonObject();
                op["parameters"] = parameters;
            }

            parameters[parameterName] = JsonNode.Parse(value.GetRawText());
            return;
        }
    }

    private static void RemoveConnection(JsonArray connections, string? sourceTempId, string? targetTempId)
    {
        for (var index = connections.Count - 1; index >= 0; index--)
        {
            var sourceMatches = string.IsNullOrWhiteSpace(sourceTempId) ||
                                string.Equals(ReadString(connections[index], "sourceTempId"), sourceTempId, StringComparison.OrdinalIgnoreCase);
            var targetMatches = string.IsNullOrWhiteSpace(targetTempId) ||
                                string.Equals(ReadString(connections[index], "targetTempId"), targetTempId, StringComparison.OrdinalIgnoreCase);
            if (sourceMatches && targetMatches)
            {
                connections.RemoveAt(index);
            }
        }
    }

    private static string NormalizeOperation(string? operation)
    {
        return operation switch
        {
            "addOperator" => "add_operator",
            "removeOperator" => "remove_operator",
            "replaceOperatorParameter" => "replace_operator_parameter",
            "setParameter" => "replace_operator_parameter",
            "addConnection" => "add_connection",
            "removeConnection" => "remove_connection",
            _ => operation ?? string.Empty
        };
    }

    private static bool TryParseObject(string text, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
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

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadString(JsonNode? node, string propertyName)
    {
        return node is JsonObject obj &&
               obj[propertyName] is JsonValue value &&
               value.TryGetValue<string>(out var text)
            ? text
            : null;
    }
}
