using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public abstract class VisionAgentToolBase : IVisionAgentTool
{
    public abstract string Name { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract string Category { get; }
    public virtual VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
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

    protected static string? ReadString(JsonElement arguments, string propertyName)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               arguments.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    protected static int? ReadInt(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    protected static JsonElement? ReadObject(JsonElement arguments, string propertyName)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               arguments.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }
}
