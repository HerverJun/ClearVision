using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CurrentFlowInspectTool : VisionAgentToolBase
{
    public override string Name => "inspect_current_flow";
    public override string DisplayName => "Inspect current flow";
    public override string Description => "Summarizes the current canvas flow from the GenerateFlow existingFlowJson payload.";
    public override string Category => "flow";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {}
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.ExistingFlowJson))
        {
            return Task.FromResult(VisionAgentToolResult.Ok(new
            {
                hasFlow = false,
                operatorCount = 0,
                connectionCount = 0,
                operators = Array.Empty<object>(),
                connections = Array.Empty<string>(),
                warnings = new[] { "No existingFlowJson was provided." }
            }));
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ExistingFlowJson);
            var root = doc.RootElement;
            var operatorsElement = TryGetArray(root, "operators");
            var connectionsElement = TryGetArray(root, "connections");
            var operators = operatorsElement.HasValue
                ? operatorsElement.Value.EnumerateArray().Select(ReadOperatorSummary).ToList()
                : new List<object>();
            var connections = connectionsElement.HasValue
                ? connectionsElement.Value.EnumerateArray().Select(ReadConnectionSummary).ToList()
                : new List<string>();

            return Task.FromResult(VisionAgentToolResult.Ok(new
            {
                hasFlow = true,
                operatorCount = operators.Count,
                connectionCount = connections.Count,
                operators,
                connections,
                warnings = operators.Count == 0 ? new[] { "Existing flow JSON contains no operators array." } : Array.Empty<string>()
            }));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "invalid_existing_flow_json",
                ex.Message));
        }
    }

    private static JsonElement? TryGetArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("flow", out var flow) &&
            flow.ValueKind == JsonValueKind.Object &&
            flow.TryGetProperty(propertyName, out var nested) &&
            nested.ValueKind == JsonValueKind.Array)
        {
            return nested;
        }

        return null;
    }

    private static object ReadOperatorSummary(JsonElement op)
    {
        return new
        {
            id = ReadAnyString(op, "id") ?? ReadAnyString(op, "tempId") ?? string.Empty,
            operatorType = ReadAnyString(op, "operatorType") ?? ReadAnyString(op, "type") ?? string.Empty,
            displayName = ReadAnyString(op, "displayName") ?? ReadAnyString(op, "name") ?? string.Empty,
            parameterCount = TryGetArrayLength(op, "parameters")
        };
    }

    private static string ReadConnectionSummary(JsonElement connection)
    {
        var source = ReadAnyString(connection, "sourceTempId")
            ?? ReadAnyString(connection, "sourceOperatorId")
            ?? string.Empty;
        var sourcePort = ReadAnyString(connection, "sourcePortName")
            ?? ReadAnyString(connection, "sourcePortId")
            ?? string.Empty;
        var target = ReadAnyString(connection, "targetTempId")
            ?? ReadAnyString(connection, "targetOperatorId")
            ?? string.Empty;
        var targetPort = ReadAnyString(connection, "targetPortName")
            ?? ReadAnyString(connection, "targetPortId")
            ?? string.Empty;
        return $"{source}.{sourcePort} -> {target}.{targetPort}";
    }

    private static string? ReadAnyString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText().Trim('"');
    }

    private static int TryGetArrayLength(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value.GetArrayLength();
        }

        return 0;
    }
}

