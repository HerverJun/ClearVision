using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowTemplateSkeletonTool : IVisionAgentTool
{
    private readonly IFlowTemplateService _templateService;

    public FlowTemplateSkeletonTool(IFlowTemplateService templateService)
    {
        _templateService = templateService;
    }

    public string Name => "get_flow_template_skeleton";
    public string DisplayName => "获取模板骨架";
    public string Description => "基于模板ID，获取该模板定义的工作流骨架（包含默认算子节点和推荐的数据流连线），作为生成该场景流程的基础架构。";
    public string Category => "Templates";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""templateId"": { ""type"": ""string"", ""description"": ""模板ID或ScenarioKey（如 classic-template-matching-inspection 或 Guid）"" }
        },
        ""required"": [""templateId""]
    }").RootElement;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("templateId", out var tempProp) ||
            tempProp.ValueKind != JsonValueKind.String)
        {
            return VisionAgentToolResult.CreateFailure("Missing or invalid 'templateId' parameter.");
        }

        var templateId = tempProp.GetString() ?? string.Empty;
        FlowTemplate? template = null;

        if (Guid.TryParse(templateId, out var guid))
        {
            template = await _templateService.GetTemplateAsync(guid, cancellationToken);
        }

        if (template == null)
        {
            var templates = await _templateService.GetTemplatesAsync(cancellationToken: cancellationToken);
            template = templates.FirstOrDefault(t =>
                string.Equals(t.ScenarioKey, templateId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, templateId, StringComparison.OrdinalIgnoreCase) ||
                (t.Id != Guid.Empty && string.Equals(t.Id.ToString(), templateId, StringComparison.OrdinalIgnoreCase)));
        }

        if (template == null)
        {
            return VisionAgentToolResult.CreateFailure($"Template with ID or key '{templateId}' not found.");
        }

        try
        {
            var flowJson = template.FlowJson ?? "{}";
            using var doc = JsonDocument.Parse(flowJson);
            
            var result = new
            {
                templateId = template.Id == Guid.Empty ? template.ScenarioKey : template.Id.ToString(),
                flowSkeleton = doc.RootElement.Clone()
            };

            return VisionAgentToolResult.CreateSuccess(result, $"Successfully retrieved skeleton for template '{template.Name}'");
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.CreateFailure($"Failed to parse template flow JSON: {ex.Message}");
        }
    }
}
