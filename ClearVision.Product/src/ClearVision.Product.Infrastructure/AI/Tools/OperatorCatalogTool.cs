using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorCatalogTool : VisionAgentToolBase
{
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public OperatorCatalogTool()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    public OperatorCatalogTool(IOperatorFactory operatorFactory)
        : this(new VisionAgentOperatorContractCatalog(operatorFactory))
    {
    }

    internal OperatorCatalogTool(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

    public override string Name => "list_operator_catalog";
    public override string DisplayName => "List operator catalog";
    public override string Description => "Returns a read-only slice of the ClearVision real operator contract catalog.";
    public override string Category => "operator";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
            "properties": {
              "keyword": { "type": "string" },
              "topN": { "type": "integer", "minimum": 1, "maximum": 50 },
              "includeCompatibility": { "type": "boolean", "default": false }
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
        var includeCompatibility = ReadBool(arguments, "includeCompatibility") ?? false;
        var operators = _contractCatalog.Operators
            .Where(item => includeCompatibility || !item.DefaultHidden)
            .OrderBy(item => item.CategoryOrder)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            operators = operators.Where(item =>
                item.OperatorType.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Keywords.Any(alias => alias.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        var results = operators
            .Take(topN)
            .Select(item => new
            {
                operatorType = item.OperatorType,
                displayName = item.DisplayName,
                categoryId = item.CategoryId.ToString(),
                categoryOrder = item.CategoryOrder,
                category = item.Category,
                lifecycle = item.Lifecycle.ToString(),
                lifecycleNote = item.LifecycleNote,
                defaultHidden = item.DefaultHidden,
                defaultAiRecommendation = item.DefaultAiRecommendation,
                requiresLifecycleDisclosure = item.RequiresLifecycleDisclosure,
                summary = item.Description,
                keywords = item.Keywords,
                inputPortCount = item.InputPorts.Count,
                outputPortCount = item.OutputPorts.Count,
                parameterCount = item.Parameters.Count
            })
            .ToList();

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "real_operator_contract_catalog",
            keyword,
            includeCompatibility,
            count = results.Count,
            operators = results
        }));
    }
}
