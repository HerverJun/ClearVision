using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorKnowledgeTool : IVisionAgentTool
{
    private readonly IOperatorKnowledgeRetriever _knowledgeRetriever;

    public OperatorKnowledgeTool(IOperatorKnowledgeRetriever knowledgeRetriever)
    {
        _knowledgeRetriever = knowledgeRetriever;
    }

    public string Name => "retrieve_operator_knowledge";
    public string DisplayName => "查询算子知识";
    public string Description => "基于场景描述、上下文、附件及场景提示词，检索并返回相关的算子知识卡片（包含适用场景、限制、反模式等关键设计参考）。";
    public string Category => "Operators";
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
            ""scenarioHints"": {
                ""type"": ""array"",
                ""items"": { ""type"": ""string"" },
                ""description"": ""可选，显式的场景提示（如 wire-sequence 等）""
            },
            ""topN"": { ""type"": ""integer"", ""description"": ""可选，返回结果的最大数量限制，默认为 24"" }
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
        List<string>? scenarioHints = null;
        int topN = 24;

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

        if (arguments.TryGetProperty("scenarioHints", out var hintsProp) && hintsProp.ValueKind == JsonValueKind.Array)
        {
            scenarioHints = hintsProp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .ToList();
        }

        if (arguments.TryGetProperty("topN", out var topProp) && topProp.ValueKind == JsonValueKind.Number)
        {
            topN = topProp.GetInt32();
        }

        var query = new OperatorKnowledgeQuery
        {
            Description = description,
            AdditionalContext = additionalContext,
            AttachmentNames = attachmentNames,
            ScenarioHints = scenarioHints,
            TopN = topN
        };

        var slice = await _knowledgeRetriever.RetrieveAsync(query, cancellationToken);

        var summary = $"Retrieved {slice.Cards.Count} operator knowledge cards. Summary: {slice.RetrievalSummary}";
        return VisionAgentToolResult.CreateSuccess(slice, summary);
    }
}
