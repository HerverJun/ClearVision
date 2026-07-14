using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorSchemaTool : VisionAgentToolBase
{
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public OperatorSchemaTool()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    public OperatorSchemaTool(IOperatorFactory operatorFactory)
        : this(new VisionAgentOperatorContractCatalog(operatorFactory))
    {
    }

    internal OperatorSchemaTool(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

    public override string Name => "get_operator_schema";
    public override string DisplayName => "Get operator schema";
    public override string Description => "Returns read-only operator ports and parameter metadata from the ClearVision operator contract catalog.";
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

        if (!_contractCatalog.TryGet(operatorType, out var schema))
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "unknown_operator_type",
                $"Operator type '{operatorType}' is not in the ClearVision operator contract catalog.",
                new { operatorType }));
        }

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "real_operator_contract_catalog",
            operatorType = schema.OperatorType,
            displayName = schema.DisplayName,
            categoryId = schema.CategoryId.ToString(),
            categoryOrder = schema.CategoryOrder,
            category = schema.Category,
            description = schema.Description,
            lifecycle = schema.Lifecycle.ToString(),
            lifecycleNote = schema.LifecycleNote,
            defaultHidden = schema.DefaultHidden,
            defaultAiRecommendation = schema.DefaultAiRecommendation,
            requiresLifecycleDisclosure = schema.RequiresLifecycleDisclosure,
            inputPorts = schema.InputPorts.Select(port => new
            {
                name = port.Name,
                displayName = port.DisplayName,
                dataType = port.DataType.ToString(),
                required = port.IsRequired,
                description = port.Description
            }).ToList(),
            outputPorts = schema.OutputPorts.Select(port => new
            {
                name = port.Name,
                displayName = port.DisplayName,
                dataType = port.DataType.ToString(),
                required = port.IsRequired,
                description = port.Description
            }).ToList(),
            parameters = schema.Parameters.Select(parameter => new
            {
                name = parameter.Name,
                displayName = parameter.DisplayName,
                dataType = parameter.DataType,
                required = parameter.IsRequired,
                defaultValue = parameter.DefaultValue,
                minValue = parameter.MinValue,
                maxValue = parameter.MaxValue,
                summary = parameter.Description,
                options = parameter.Options?.Select(option => new
                {
                    value = option.Value,
                    label = option.Label
                }).ToList()
            }).ToList(),
            parameterConditions = schema.ParameterConstraints,
            outputConditions = schema.OutputAvailabilityRules
        }));
    }
}
