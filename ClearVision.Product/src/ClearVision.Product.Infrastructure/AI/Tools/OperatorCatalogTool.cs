using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorCatalogTool : VisionAgentToolBase
{
    private readonly IOperatorFactory _operatorFactory;

    public OperatorCatalogTool(IOperatorFactory operatorFactory)
    {
        _operatorFactory = operatorFactory;
    }

    public override string Name => "list_operator_catalog";
    public override string DisplayName => "List operator catalog";
    public override string Description => "Returns a compact list of registered ClearVision operator metadata. Use get_operator_schema for full ports and parameters.";
    public override string Category => "operator";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "category": { "type": "string" },
            "keyword": { "type": "string" },
            "topN": { "type": "integer", "minimum": 1, "maximum": 200 }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var category = ReadString(arguments, "category");
        var keyword = ReadString(arguments, "keyword");
        var topN = Math.Clamp(ReadInt(arguments, "topN") ?? 40, 1, 200);

        var metadata = _operatorFactory.GetAllMetadata()
            .Where(item => string.IsNullOrWhiteSpace(category) ||
                           string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesKeyword(item, keyword))
            .OrderBy(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .Select(item => new
            {
                operatorType = item.Type.ToString(),
                item.DisplayName,
                item.Category,
                item.Description,
                keywords = item.Keywords ?? Array.Empty<string>(),
                tags = item.Tags ?? Array.Empty<string>(),
                inputCount = item.InputPorts.Count,
                outputCount = item.OutputPorts.Count,
                parameterCount = item.Parameters.Count
            })
            .ToList();

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            operators = metadata,
            count = metadata.Count,
            filters = new { category, keyword, topN }
        }));
    }

    private static bool MatchesKeyword(OperatorMetadata metadata, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var needle = keyword.Trim();
        return metadata.Type.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               metadata.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               metadata.Description.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               (metadata.Keywords?.Any(item => item.Contains(needle, StringComparison.OrdinalIgnoreCase)) ?? false) ||
               (metadata.Tags?.Any(item => item.Contains(needle, StringComparison.OrdinalIgnoreCase)) ?? false);
    }
}

