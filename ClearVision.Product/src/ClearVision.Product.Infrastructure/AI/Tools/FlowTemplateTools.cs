using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowTemplateMatchTool : VisionAgentToolBase
{
    private readonly IFlowTemplateService _templateService;

    public FlowTemplateMatchTool(IFlowTemplateService templateService)
    {
        _templateService = templateService;
    }

    public override string Name => "match_flow_template";
    public override string DisplayName => "Match flow template";
    public override string Description => "Finds existing ClearVision flow templates that match the request.";
    public override string Category => "template";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "description": { "type": "string" },
            "industry": { "type": "string" },
            "topN": { "type": "integer", "minimum": 1, "maximum": 10 }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var description = ReadString(arguments, "description") ?? context.UserDescription;
        var industry = ReadString(arguments, "industry");
        var topN = Math.Clamp(ReadInt(arguments, "topN") ?? 3, 1, 10);
        var templates = await _templateService.GetTemplatesAsync(industry, cancellationToken);
        var scored = templates
            .Select(template => new
            {
                template,
                score = Score(template, description)
            })
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.template.Name, StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .Select(item => new
            {
                templateId = item.template.Id,
                templateName = item.template.Name,
                templateVersion = item.template.TemplateVersion,
                item.template.ScenarioKey,
                item.template.Industry,
                item.template.Description,
                tags = item.template.Tags,
                confidence = Math.Min(1.0, item.score / 8.0),
                matchReason = item.score <= 0 ? "metadata listed" : "matched by template metadata keywords"
            })
            .ToList();

        return VisionAgentToolResult.Ok(new
        {
            templates = scored,
            count = scored.Count
        });
    }

    private static double Score(FlowTemplate template, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return 0;
        }

        var text = description.ToLowerInvariant();
        var score = 0.0;
        foreach (var token in Tokenize(template.Name)
                     .Concat(Tokenize(template.Description))
                     .Concat(Tokenize(template.Industry))
                     .Concat(template.Tags.SelectMany(Tokenize))
                     .Concat(Tokenize(template.ScenarioKey)))
        {
            if (token.Length >= 2 && text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 1.0;
            }
        }

        return score;
    }

    private static IEnumerable<string> Tokenize(string? text)
    {
        return (text ?? string.Empty)
            .Split([' ', ',', ';', '/', '\\', '-', '_', '|', '，', '；', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant());
    }
}

public sealed class FlowTemplateSkeletonTool : VisionAgentToolBase
{
    private readonly IFlowTemplateService _templateService;

    public FlowTemplateSkeletonTool(IFlowTemplateService templateService)
    {
        _templateService = templateService;
    }

    public override string Name => "get_flow_template_skeleton";
    public override string DisplayName => "Get flow template skeleton";
    public override string Description => "Returns the stored JSON skeleton for a matched ClearVision flow template.";
    public override string Category => "template";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "templateId": { "type": "string" },
            "scenarioKey": { "type": "string" }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var templateId = ReadString(arguments, "templateId");
        var scenarioKey = ReadString(arguments, "scenarioKey");
        FlowTemplate? template = null;
        if (Guid.TryParse(templateId, out var id))
        {
            template = await _templateService.GetTemplateAsync(id, cancellationToken);
        }

        if (template == null && !string.IsNullOrWhiteSpace(scenarioKey))
        {
            var templates = await _templateService.GetTemplatesAsync(cancellationToken: cancellationToken);
            template = templates.FirstOrDefault(item =>
                string.Equals(item.ScenarioKey, scenarioKey, StringComparison.OrdinalIgnoreCase));
        }

        if (template == null)
        {
            return VisionAgentToolResult.Fail(
                "template_not_found",
                "No matching flow template was found. Call match_flow_template first.");
        }

        object? skeleton = template.FlowJson;
        if (!string.IsNullOrWhiteSpace(template.FlowJson))
        {
            try
            {
                skeleton = JsonSerializer.Deserialize<object>(template.FlowJson);
            }
            catch (JsonException)
            {
                skeleton = template.FlowJson;
            }
        }

        return VisionAgentToolResult.Ok(new
        {
            templateId = template.Id,
            templateName = template.Name,
            templateVersion = template.TemplateVersion,
            template.ScenarioKey,
            template.Industry,
            skeleton
        });
    }
}

