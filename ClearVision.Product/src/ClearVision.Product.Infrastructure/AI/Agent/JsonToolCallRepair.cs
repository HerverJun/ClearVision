using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class JsonToolCallRepair
{
    public bool TryNormalizeProtocolJson(
        string raw,
        out string normalized,
        out string failureReason)
    {
        normalized = string.Empty;
        failureReason = string.Empty;

        var candidate = StripJsonFences(raw ?? string.Empty);
        if (!TryParseObject(candidate, out var root))
        {
            failureReason = "Planner completion was not a valid JSON object.";
            return false;
        }

        if (IsToolCall(root))
        {
            if (!HasValidToolCalls(root, out failureReason))
            {
                return false;
            }

            normalized = root.GetRawText();
            return true;
        }

        if (!IsFinalContent(root))
        {
            failureReason = "Planner completion was not a tool_call or final workflowDraft/draftEdits JSON object.";
            return false;
        }

        normalized = root.GetRawText();
        return true;
    }

    private static bool IsToolCall(JsonElement root)
    {
        return ReadString(root, "kind").Equals("tool_call", StringComparison.OrdinalIgnoreCase) ||
               root.TryGetProperty("toolCalls", out _) ||
               root.TryGetProperty("tool_calls", out _);
    }

    private static bool IsFinalContent(JsonElement root)
    {
        var kind = ReadString(root, "kind");
        var hasFinalKind = kind.Equals("final", StringComparison.OrdinalIgnoreCase);
        var hasWorkflowDraft = root.TryGetProperty("workflowDraft", out var workflowDraft) &&
                               workflowDraft.ValueKind == JsonValueKind.Object;
        var hasDraftEdits = root.TryGetProperty("draftEdits", out var draftEdits) &&
                            draftEdits.ValueKind == JsonValueKind.Array;
        return (hasFinalKind || hasWorkflowDraft || hasDraftEdits) &&
               (hasWorkflowDraft || hasDraftEdits);
    }

    private static bool HasValidToolCalls(
        JsonElement root,
        out string failureReason)
    {
        failureReason = string.Empty;
        var calls = root.TryGetProperty("toolCalls", out var camel)
            ? camel
            : root.TryGetProperty("tool_calls", out var snake)
                ? snake
                : default;
        if (calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0)
        {
            failureReason = "Planner completion declared tool_call but did not include toolCalls.";
            return false;
        }

        var validNames = 0;
        foreach (var call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object)
            {
                failureReason = "Planner tool_call entry must be a JSON object.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ReadString(call, "name")))
            {
                failureReason = "Planner tool_call entry is missing a tool name.";
                return false;
            }

            if (call.TryGetProperty("arguments", out var arguments) &&
                arguments.ValueKind != JsonValueKind.Object)
            {
                failureReason = "Planner tool_call arguments must be a JSON object when present.";
                return false;
            }

            validNames++;
        }

        return validNames > 0;
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
            var extracted = ExtractBalancedObject(text);
            if (extracted == null)
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(extracted);
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
    }

    private static string StripJsonFences(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..];
        }
        else if (text.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..];
        }

        if (text.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^3];
        }

        return text.Trim();
    }

    private static string? ExtractBalancedObject(string text)
    {
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
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

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
                {
                    start = i;
                }

                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
