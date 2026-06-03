using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowTemplateMatchTool : IVisionAgentTool
{
    private readonly IScenarioMatcher _scenarioMatcher;

    public FlowTemplateMatchTool(IScenarioMatcher scenarioMatcher)
    {
        _scenarioMatcher = scenarioMatcher;
    }

    public string Name => "match_flow_template";
    public string DisplayName => "匹配流程模板";
    public string Description => "基于用户需求描述和附件，匹配最适合的预置视觉工作流模板。匹配成功后返回模板ID和置信度，后续可拉取对应的模板骨架。";
    public string Category => "Templates";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""description"": { ""type"": ""string"", ""description"": ""用户对视觉任务的自然语言描述"" },
            ""additionalContext"": { ""type"": ""string"", ""description"": ""可选，额外的上下文或背景说明"" },
            ""attachmentNames"": {
                ""type"": ""array"",
                ""items"": { ""type"": ""string"" },
                ""description"": ""可选，关联附件名称列表""
            },
            ""topN"": { ""type"": ""integer"", ""description"": ""可选，返回候选模板的最大数量限制，默认为 3"" }
        },
        ""required"": [""description""]
    }").RootElement;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("description", out var descProp) ||
            descProp.ValueKind != JsonValueKind.String)
        {
            return VisionAgentToolResult.CreateFailure("Missing or invalid 'description' parameter.");
        }

        var description = descProp.GetString() ?? string.Empty;
        string? additionalContext = null;
        List<string>? attachmentNames = null;
        int topN = 3;

        if (arguments.TryGetProperty("additionalContext", out var contextProp) && contextProp.ValueKind == JsonValueKind.String)
        {
            additionalContext = contextProp.GetString();
        }

        if (arguments.TryGetProperty("attachmentNames", out var attachProp) && attachProp.ValueKind == JsonValueKind.Array)
        {
            attachmentNames = attachProp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .ToList();
        }

        if (arguments.TryGetProperty("topN", out var topProp) && topProp.ValueKind == JsonValueKind.Number)
        {
            topN = topProp.GetInt32();
        }

        var matches = await _scenarioMatcher.MatchAsync(
            description,
            additionalContext,
            attachmentNames,
            topN,
            cancellationToken);

        if (matches.Count == 0)
        {
            var noMatch = new
            {
                matched = false,
                templateId = (string?)null,
                templateName = (string?)null,
                confidence = 0.0,
                generationMode = "scratch",
                templateLockLevel = "relaxed"
            };
            return VisionAgentToolResult.CreateSuccess(noMatch, "No matching templates found.");
        }

        var bestMatch = matches[0];
        
        // 判定锁等级：如果是端子线序或一些高精度匹配，可能是 strict
        bool isStrict = bestMatch.Scenario.ScenarioKey.Contains("wire-sequence") || 
                        bestMatch.Scenario.ScenarioKey.Contains("aircon-indoor");
        
        var result = new
        {
            matched = true,
            templateId = bestMatch.Scenario.TemplateId ?? bestMatch.Scenario.ScenarioKey,
            templateName = bestMatch.Scenario.TemplateName,
            confidence = bestMatch.Confidence,
            generationMode = "template_fill",
            templateLockLevel = isStrict ? "strict" : "relaxed"
        };

        var summary = $"Matched template '{result.templateName}' (ID: {result.templateId}) with confidence {result.confidence:F2}.";
        return VisionAgentToolResult.CreateSuccess(result, summary);
    }
}
