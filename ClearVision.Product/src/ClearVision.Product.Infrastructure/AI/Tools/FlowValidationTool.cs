using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowValidationTool : VisionAgentToolBase
{
    private readonly IAiFlowValidator _validator;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public FlowValidationTool(IAiFlowValidator validator)
    {
        _validator = validator;
    }

    public override string Name => "validate_flow";
    public override string DisplayName => "Validate flow";
    public override string Description => "Validates an AI-generated ClearVision flow against registered operators, ports, parameters, and knowledge rules.";
    public override string Category => "flow";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "required": ["flow"],
          "properties": {
            "flow": { "type": "object" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flowElement = ReadObjectOrSelf(arguments, "flow");
        AiGeneratedFlowJson? flow;
        try
        {
            flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowElement.GetRawText(), JsonOptions);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(VisionAgentToolResult.Fail("invalid_flow_json", ex.Message));
        }

        if (flow == null)
        {
            return Task.FromResult(VisionAgentToolResult.Fail("invalid_flow_json", "Flow payload is empty."));
        }

        var validation = _validator.Validate(flow);
        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            valid = validation.IsValid,
            errors = validation.Errors,
            warnings = validation.Warnings,
            diagnostics = validation.Diagnostics
        }));
    }
}

