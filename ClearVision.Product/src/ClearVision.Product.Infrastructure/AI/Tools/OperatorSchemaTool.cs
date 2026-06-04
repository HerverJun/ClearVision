using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorSchemaTool : VisionAgentToolBase
{
    public override string Name => "get_operator_schema";
    public override string DisplayName => "Get operator schema";
    public override string Description => "Returns read-only operator ports and parameter metadata.";
    public override string Category => "operator";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "required": ["operatorType"],
          "properties": {
            "operatorType": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operatorType = ReadString(arguments, "operatorType");
        if (string.IsNullOrWhiteSpace(operatorType))
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "operator_type_required",
                "operatorType is required."));
        }

        if (!VisionAgentReadOnlyCatalog.Schemas.TryGetValue(operatorType, out var schema))
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "unknown_operator_type",
                $"Operator type '{operatorType}' is not in the read-only schema catalog.",
                new { operatorType }));
        }

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "readonly_static_schema",
            operatorType = schema.OperatorType,
            inputPorts = schema.InputPorts,
            outputPorts = schema.OutputPorts,
            parameters = schema.Parameters.Select(parameter => new
            {
                name = parameter.Name,
                dataType = parameter.DataType,
                required = parameter.Required,
                summary = parameter.Summary
            }).ToList()
        }));
    }
}
