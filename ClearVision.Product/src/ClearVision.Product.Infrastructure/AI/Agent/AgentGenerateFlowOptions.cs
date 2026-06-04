using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentGenerateFlowOptions
{
    public const string SectionName = "AI:VisionAgent:GenerateFlow";

    public bool Enabled { get; set; }

    public string Mode { get; set; } = AiAgentGenerateFlowModes.Scripted;

    public bool FallbackToScriptedOnPlannerFailure { get; set; } = true;

    public bool FallbackToLegacyOnFailure { get; set; } = true;
}
