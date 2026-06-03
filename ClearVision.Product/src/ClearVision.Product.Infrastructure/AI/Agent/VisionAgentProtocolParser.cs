using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentProtocolParser
{
    public VisionAgentProtocolMessage Parse(string raw)
    {
        var json = StripJsonFences(raw ?? string.Empty);
        if (!TryParseJsonObject(json, out var root))
        {
            return VisionAgentProtocolMessage.Final(raw ?? string.Empty);
        }

        var kind = TryReadString(root, "kind");
        if (string.Equals(kind, "tool_call", StringComparison.OrdinalIgnoreCase) ||
            root.TryGetProperty("toolCalls", out _) ||
            root.TryGetProperty("tool_calls", out _))
        {
            var calls = ReadToolCalls(root);
            return calls.Count == 0
                ? VisionAgentProtocolMessage.Final(root.GetRawText())
                : VisionAgentProtocolMessage.ToolCall(calls);
        }

        return VisionAgentProtocolMessage.Final(root.GetRawText());
    }

    private static List<VisionAgentToolCall> ReadToolCalls(JsonElement root)
    {
        var property = root.TryGetProperty("toolCalls", out var camel)
            ? camel
            : root.TryGetProperty("tool_calls", out var snake)
                ? snake
                : default;
        if (property.ValueKind != JsonValueKind.Array)
        {
            return new List<VisionAgentToolCall>();
        }

        var calls = new List<VisionAgentToolCall>();
        var index = 0;
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = TryReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var args = item.TryGetProperty("arguments", out var arguments) &&
                       arguments.ValueKind == JsonValueKind.Object
                ? arguments.Clone()
                : EmptyArguments();
            calls.Add(new VisionAgentToolCall
            {
                Id = TryReadString(item, "id") ?? $"call_{++index}",
                Name = name,
                Arguments = args
            });
        }

        return calls;
    }

    private static JsonElement EmptyArguments()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static bool TryParseJsonObject(string json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            var candidate = ExtractBalancedObject(json);
            if (candidate == null)
            {
                return false;
            }

            using var doc = JsonDocument.Parse(candidate);
            root = doc.RootElement.Clone();
            return true;
        }
    }

    private static string StripJsonFences(string raw)
    {
        var json = raw.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            json = json[7..];
        }
        else if (json.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            json = json[3..];
        }

        if (json.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            json = json[..^3];
        }

        return json.Trim();
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

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

public sealed record VisionAgentProtocolMessage
{
    public bool IsToolCall { get; init; }
    public IReadOnlyList<VisionAgentToolCall> ToolCalls { get; init; } = Array.Empty<VisionAgentToolCall>();
    public string FinalContent { get; init; } = string.Empty;

    public static VisionAgentProtocolMessage ToolCall(IReadOnlyList<VisionAgentToolCall> calls) => new()
    {
        IsToolCall = true,
        ToolCalls = calls
    };

    public static VisionAgentProtocolMessage Final(string content) => new()
    {
        FinalContent = content
    };
}

public sealed record VisionAgentToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public JsonElement Arguments { get; init; }
}

