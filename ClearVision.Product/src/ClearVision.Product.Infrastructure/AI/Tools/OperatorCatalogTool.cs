using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorCatalogTool : VisionAgentToolBase
{
    public override string Name => "list_operator_catalog";
    public override string DisplayName => "List operator catalog";
    public override string Description => "Returns a read-only slice of the ClearVision operator catalog.";
    public override string Category => "operator";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "keyword": { "type": "string" },
            "topN": { "type": "integer", "minimum": 1, "maximum": 50 }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keyword = ReadString(arguments, "keyword");
        var topN = Math.Clamp(ReadInt(arguments, "topN") ?? 20, 1, 50);
        var operators = VisionAgentReadOnlyCatalog.Operators.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            operators = operators.Where(item =>
                item.OperatorType.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Keywords.Any(value => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        var results = operators
            .Take(topN)
            .Select(item => new
            {
                operatorType = item.OperatorType,
                displayName = item.DisplayName,
                category = item.Category,
                summary = item.Summary,
                keywords = item.Keywords
            })
            .ToList();

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "readonly_static_catalog",
            keyword,
            count = results.Count,
            operators = results
        }));
    }
}
