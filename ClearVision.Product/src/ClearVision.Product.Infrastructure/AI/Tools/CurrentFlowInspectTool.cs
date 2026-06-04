using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CurrentFlowInspectTool : VisionAgentToolBase
{
    public override string Name => "inspect_current_flow";
    public override string DisplayName => "Inspect current flow";
    public override string Description => "Summarizes existing flow JSON without executing it.";
    public override string Category => "flow";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "existingFlowJson": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flowJson = ReadString(arguments, "existingFlowJson") ?? context.ExistingFlowJson;
        if (string.IsNullOrWhiteSpace(flowJson))
        {
            return Task.FromResult(VisionAgentToolResult.Ok(new
            {
                source = "readonly_flow_inspection",
                hasExistingFlow = false,
                operatorCount = 0,
                connectionCount = 0,
                operatorTypes = Array.Empty<string>()
            }));
        }

        try
        {
            using var doc = JsonDocument.Parse(flowJson);
            var root = doc.RootElement;
            var operators = ReadArray(root, "operators")
                .Select(item => new
                {
                    tempId = ReadStringProperty(item, "tempId") ?? ReadStringProperty(item, "id") ?? string.Empty,
                    operatorType = ReadStringProperty(item, "operatorType") ?? ReadStringProperty(item, "type") ?? string.Empty,
                    displayName = ReadStringProperty(item, "displayName") ?? string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.operatorType))
                .ToList();
            var connections = ReadArray(root, "connections").ToList();

            return Task.FromResult(VisionAgentToolResult.Ok(new
            {
                source = "readonly_flow_inspection",
                hasExistingFlow = true,
                operatorCount = operators.Count,
                connectionCount = connections.Count,
                operatorTypes = operators.Select(item => item.operatorType).ToList(),
                operators
            }));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "invalid_existing_flow_json",
                ex.Message));
        }
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
