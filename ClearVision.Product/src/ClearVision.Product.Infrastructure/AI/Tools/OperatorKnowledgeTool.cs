using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorKnowledgeTool : VisionAgentToolBase
{
    public override string Name => "retrieve_operator_knowledge";
    public override string DisplayName => "Retrieve operator knowledge";
    public override string Description => "Returns static engineering notes for known operators and scenarios.";
    public override string Category => "operator";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "operatorType": { "type": "string" },
            "keyword": { "type": "string" },
            "topN": { "type": "integer", "minimum": 1, "maximum": 10 }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operatorType = ReadString(arguments, "operatorType");
        var keyword = ReadString(arguments, "keyword") ?? context.UserDescription;
        var topN = Math.Clamp(ReadInt(arguments, "topN") ?? 5, 1, 10);
        var notes = BuildNotes(operatorType, keyword)
            .Take(topN)
            .ToList();

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "readonly_static_knowledge",
            operatorType,
            keyword,
            notes
        }));
    }

    private static IEnumerable<object> BuildNotes(string? operatorType, string? keyword)
    {
        var normalized = $"{operatorType} {keyword}".Trim();
        foreach (var item in VisionAgentReadOnlyCatalog.Operators)
        {
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !item.OperatorType.Contains(normalized, StringComparison.OrdinalIgnoreCase) &&
                !item.Summary.Contains(normalized, StringComparison.OrdinalIgnoreCase) &&
                !item.Keywords.Any(value => normalized.Contains(value, StringComparison.OrdinalIgnoreCase)) &&
                !normalized.Contains(item.OperatorType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new
            {
                operatorType = item.OperatorType,
                summary = item.Summary,
                guidance = item.OperatorType switch
                {
                    "ImageAcquisition" => "Treat camera binding as an engineer-provided resource; do not capture frames in read-only tools.",
                    "TemplateMatching" => "Surface missing TemplatePath as pending engineering input; do not load template files.",
                    "DeepLearning" => "Surface missing ModelPath as pending engineering input; do not load model files.",
                    "MeasureDistance" => "Ask for calibration and unit/tolerance confirmation before deployment.",
                    _ => "Use schema metadata and keep generated parameters reviewable."
                }
            };
        }

        if (string.IsNullOrWhiteSpace(operatorType))
        {
            yield return new
            {
                operatorType = "general",
                summary = "ReadOnly Tools v0.1 provide engineering context only.",
                guidance = "They must not access cameras, Station, files, networks, or runtime preview."
            };
        }
    }
}
