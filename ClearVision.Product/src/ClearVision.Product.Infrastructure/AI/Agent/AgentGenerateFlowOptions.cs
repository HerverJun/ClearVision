namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentGenerateFlowOptions
{
    public const string SectionName = "AI:VisionAgent:GenerateFlow";

    public bool Enabled { get; set; }

    public bool FallbackToLegacyOnFailure { get; set; } = true;
}
