using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorKnowledgeTool : VisionAgentToolBase
{
    private readonly IOperatorKnowledgeRetriever _knowledgeRetriever;

    public OperatorKnowledgeTool(IOperatorKnowledgeRetriever knowledgeRetriever)
    {
        _knowledgeRetriever = knowledgeRetriever;
    }

    public override string Name => "retrieve_operator_knowledge";
    public override string DisplayName => "Retrieve operator knowledge";
    public override string Description => "Retrieves a scenario-relevant operator knowledge slice instead of injecting the full knowledge catalog into the prompt.";
    public override string Category => "operator";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "description": { "type": "string" },
            "additionalContext": { "type": "string" },
            "scenarioKey": { "type": "string" },
            "topN": { "type": "integer", "minimum": 8, "maximum": 80 }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var scenarioKey = ReadString(arguments, "scenarioKey");
        var slice = await _knowledgeRetriever.RetrieveAsync(new OperatorKnowledgeQuery
        {
            Description = ReadString(arguments, "description") ?? context.UserDescription,
            AdditionalContext = ReadString(arguments, "additionalContext") ?? context.AdditionalContext,
            ScenarioHints = string.IsNullOrWhiteSpace(scenarioKey) ? null : [scenarioKey],
            TopN = Math.Clamp(ReadInt(arguments, "topN") ?? 24, 8, 80)
        }, cancellationToken);

        return VisionAgentToolResult.Ok(slice);
    }
}

