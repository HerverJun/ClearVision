using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorCatalogTool : IVisionAgentTool
{
    private readonly IOperatorFactory _operatorFactory;

    public OperatorCatalogTool(IOperatorFactory operatorFactory)
    {
        _operatorFactory = operatorFactory;
    }

    public string Name => "list_operator_catalog";
    public string DisplayName => "算子目录列表";
    public string Description => "获取可用算子的紧凑列表，支持按类别、关键词过滤，以获取算子类型名称和基础元信息。";
    public string Category => "Operators";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""category"": { ""type"": ""string"", ""description"": ""可选，按类别（如 Acquisition, Detection, Measurement 等）过滤"" },
            ""keyword"": { ""type"": ""string"", ""description"": ""可选，按名称、描述或关键字搜索"" },
            ""topN"": { ""type"": ""integer"", ""description"": ""可选，限制返回的最大算子个数"" }
        }
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        string? categoryFilter = null;
        string? keywordFilter = null;
        int? topN = null;

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("category", out var catProp) && catProp.ValueKind == JsonValueKind.String)
                categoryFilter = catProp.GetString();
            if (arguments.TryGetProperty("keyword", out var keyProp) && keyProp.ValueKind == JsonValueKind.String)
                keywordFilter = keyProp.GetString();
            if (arguments.TryGetProperty("topN", out var topProp) && topProp.ValueKind == JsonValueKind.Number)
                topN = topProp.GetInt32();
        }

        var list = _operatorFactory.GetAllMetadata();

        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            list = list.Where(m => string.Equals(m.Category, categoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(keywordFilter))
        {
            list = list.Where(m => 
                m.DisplayName.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase) ||
                (m.Keywords != null && m.Keywords.Any(k => k.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase))));
        }

        var operators = list.Select(m => new
        {
            operatorType = m.Type.ToString(),
            displayName = m.DisplayName,
            category = m.Category,
            description = m.Description,
            keywords = m.Keywords ?? Array.Empty<string>(),
            inputCount = m.InputPorts.Count,
            outputCount = m.OutputPorts.Count,
            parameterCount = m.Parameters.Count
        });

        if (topN.HasValue && topN.Value > 0)
        {
            operators = operators.Take(topN.Value);
        }

        var resultData = new { operators = operators.ToList() };
        var summary = $"Found {resultData.operators.Count} operators";

        return Task.FromResult(VisionAgentToolResult.CreateSuccess(resultData, summary));
    }
}
