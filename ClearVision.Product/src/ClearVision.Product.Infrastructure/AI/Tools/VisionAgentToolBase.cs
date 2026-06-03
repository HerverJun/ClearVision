using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public abstract class VisionAgentToolBase : IVisionAgentTool
{
    public abstract string Name { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract string Category { get; }
    public abstract VisionAgentToolPermission Permission { get; }
    public abstract JsonElement ParametersSchema { get; }

    public abstract Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken);

    protected static JsonElement Schema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    protected static string? ReadString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    protected static int? ReadInt(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    protected static bool ReadBool(JsonElement arguments, string name, bool defaultValue = false)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    protected static JsonElement ReadObjectOrSelf(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(propertyName, out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            return nested;
        }

        return arguments;
    }
}

