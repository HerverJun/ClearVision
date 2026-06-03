using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowValidationTool : IVisionAgentTool
{
    private readonly IAiFlowValidator _validator;
    private readonly ITemplateConstraintValidator _templateConstraintValidator;
    private readonly IFlowTemplateService _templateService;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public FlowValidationTool(
        IAiFlowValidator validator,
        ITemplateConstraintValidator templateConstraintValidator,
        IFlowTemplateService templateService)
    {
        _validator = validator;
        _templateConstraintValidator = templateConstraintValidator;
        _templateService = templateService;
    }

    public string Name => "validate_flow";
    public string DisplayName => "校验工作流";
    public string Description => "对生成或修改后的工作流结构和连线数据进行自检校验，返回详细的错误清单（如端口类型不匹配、必填参数缺失等）及修复建议。";
    public string Category => "Validation";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""flow"": {
                ""type"": ""object"",
                ""description"": ""包含 operators、connections 等结构的工作流 JSON 对象""
            },
            ""templateId"": { ""type"": ""string"", ""description"": ""可选，流程模板ID，用于进行模板契约/连线校验"" },
            ""templateLockLevel"": { ""type"": ""string"", ""description"": ""可选，'strict' 或 'relaxed'"" }
        },
        ""required"": [""flow""]
    }").RootElement;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object || 
            !arguments.TryGetProperty("flow", out var flowProp))
        {
            return VisionAgentToolResult.CreateFailure("Missing or invalid 'flow' parameter.");
        }

        AiGeneratedFlowJson? flow;
        try
        {
            var flowRaw = flowProp.GetRawText();
            flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowRaw, _jsonOptions);
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.CreateFailure($"Failed to parse 'flow' argument: {ex.Message}");
        }

        if (flow == null)
        {
            return VisionAgentToolResult.CreateFailure("Flow argument deserialized to null.");
        }

        // 1. Run general flow validation
        var validationResult = _validator.Validate(flow);

        // 2. Run template constraint validation if templateId is specified
        string? templateId = null;
        if (arguments.TryGetProperty("templateId", out var tempIdProp) && tempIdProp.ValueKind == JsonValueKind.String)
        {
            templateId = tempIdProp.GetString();
        }

        if (!string.IsNullOrWhiteSpace(templateId) && Guid.TryParse(templateId, out var templateGuid))
        {
            var template = await _templateService.GetTemplateAsync(templateGuid, cancellationToken);
            if (template != null)
            {
                string? lockLevel = null;
                if (arguments.TryGetProperty("templateLockLevel", out var lockProp) && lockProp.ValueKind == JsonValueKind.String)
                {
                    lockLevel = lockProp.GetString();
                }

                bool isStrict = string.Equals(lockLevel, "strict", StringComparison.OrdinalIgnoreCase);
                var templateGateResult = _templateConstraintValidator.Validate(flow, template, isStrict);

                // Merge template diagnostics
                foreach (var diag in templateGateResult.Diagnostics)
                {
                    if (diag.Severity == AiValidationSeverity.Error)
                    {
                        validationResult.AddError(
                            diag.Message, diag.Code, diag.Category, diag.RelatedFields, 
                            diag.OperatorId, diag.ParameterName, diag.SourceTempId, diag.SourcePortName, 
                            diag.TargetTempId, diag.TargetPortName, diag.RepairHint);
                    }
                    else
                    {
                        validationResult.AddWarning(
                            diag.Message, diag.Code, diag.Category, diag.RelatedFields, 
                            diag.OperatorId, diag.ParameterName, diag.SourceTempId, diag.SourcePortName, 
                            diag.TargetTempId, diag.TargetPortName, diag.RepairHint);
                    }
                }
            }
        }

        var responseData = new
        {
            isValid = validationResult.IsValid,
            errors = validationResult.Diagnostics
                .Where(d => d.Severity == AiValidationSeverity.Error)
                .Select(d => new
                {
                    code = d.Code,
                    operatorTempId = d.OperatorId,
                    message = d.Message,
                    repairHint = d.RepairHint
                }).ToList(),
            warnings = validationResult.Diagnostics
                .Where(d => d.Severity == AiValidationSeverity.Warning)
                .Select(d => new
                {
                    code = d.Code,
                    operatorTempId = d.OperatorId,
                    message = d.Message,
                    repairHint = d.RepairHint
                }).ToList()
        };

        var summary = validationResult.IsValid 
            ? "Validation passed." 
            : $"Validation failed with {responseData.errors.Count} errors and {responseData.warnings.Count} warnings.";

        return VisionAgentToolResult.CreateSuccess(responseData, summary);
    }
}
