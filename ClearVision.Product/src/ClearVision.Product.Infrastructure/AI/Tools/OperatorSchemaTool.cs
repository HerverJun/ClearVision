using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorSchemaTool : VisionAgentToolBase
{
    private readonly IOperatorFactory _operatorFactory;

    public OperatorSchemaTool(IOperatorFactory operatorFactory)
    {
        _operatorFactory = operatorFactory;
    }

    public override string Name => "get_operator_schema";
    public override string DisplayName => "Get operator schema";
    public override string Description => "Returns the authoritative ClearVision ports and parameters for one operator type.";
    public override string Category => "operator";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
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
        var operatorTypeRaw = ReadString(arguments, "operatorType");
        if (string.IsNullOrWhiteSpace(operatorTypeRaw) ||
            !Enum.TryParse<OperatorType>(operatorTypeRaw, ignoreCase: true, out var operatorType))
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "invalid_operator_type",
                $"Unknown operatorType '{operatorTypeRaw}'. Call list_operator_catalog first."));
        }

        var metadata = _operatorFactory.GetMetadata(operatorType);
        if (metadata == null)
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "operator_schema_not_registered",
                $"Operator '{operatorType}' is not registered in the ClearVision operator factory."));
        }

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            operatorType = metadata.Type.ToString(),
            metadata.DisplayName,
            metadata.Category,
            metadata.Description,
            inputs = metadata.InputPorts.Select(port => new
            {
                portName = port.Name,
                port.DisplayName,
                dataType = port.DataType.ToString(),
                required = port.IsRequired,
                port.Description
            }),
            outputs = metadata.OutputPorts.Select(port => new
            {
                portName = port.Name,
                port.DisplayName,
                dataType = port.DataType.ToString(),
                required = port.IsRequired,
                port.Description
            }),
            parameters = metadata.Parameters.Select(parameter => new
            {
                paramName = parameter.Name,
                parameter.DisplayName,
                parameter.Description,
                type = parameter.DataType,
                required = parameter.IsRequired,
                parameter.DefaultValue,
                parameter.MinValue,
                parameter.MaxValue,
                options = parameter.Options?.Select(option => new
                {
                    option.Label,
                    option.Value
                }).ToList()
            })
        }));
    }
}

