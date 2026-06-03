using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.DryRun;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class DryRunFlowTool : VisionAgentToolBase
{
    private readonly IAiFlowValidator _validator;
    private readonly IOperatorFactory _operatorFactory;
    private readonly DryRunService _dryRunService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public DryRunFlowTool(
        IAiFlowValidator validator,
        IOperatorFactory operatorFactory,
        DryRunService dryRunService)
    {
        _validator = validator;
        _operatorFactory = operatorFactory;
        _dryRunService = dryRunService;
    }

    public override string Name => "dryrun_flow";
    public override string DisplayName => "Dry-run flow";
    public override string Description => "Runs a structure-only ClearVision DryRun simulation using stubs. This does not verify real camera image effects.";
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

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var flowElement = ReadObjectOrSelf(arguments, "flow");
        AiGeneratedFlowJson? flow;
        try
        {
            flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowElement.GetRawText(), JsonOptions);
        }
        catch (JsonException ex)
        {
            return VisionAgentToolResult.Fail("invalid_flow_json", ex.Message);
        }

        if (flow == null)
        {
            return VisionAgentToolResult.Fail("invalid_flow_json", "Flow payload is empty.");
        }

        var validation = _validator.Validate(flow);
        if (!validation.IsValid)
        {
            return VisionAgentToolResult.Ok(new
            {
                dryRunExecuted = false,
                valid = false,
                blockingIssues = validation.Errors,
                warnings = validation.Warnings,
                diagnostics = validation.Diagnostics
            });
        }

        try
        {
            var entity = VisionAgentFlowConverter.ToEntity(flow, _operatorFactory);
            var result = await _dryRunService.RunAsync(
                entity,
                new Dictionary<string, object>(),
                new DryRunStubRegistry(),
                cancellationToken);

            return VisionAgentToolResult.Ok(new
            {
                dryRunExecuted = true,
                result.IsSuccess,
                result.CoveragePercentage,
                result.CoveredBranches,
                result.TotalBranches,
                result.DurationMs,
                validationWarnings = validation.Warnings
            });
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.Fail("dryrun_failed", ex.Message, new
            {
                validationWarnings = validation.Warnings
            });
        }
    }
}

