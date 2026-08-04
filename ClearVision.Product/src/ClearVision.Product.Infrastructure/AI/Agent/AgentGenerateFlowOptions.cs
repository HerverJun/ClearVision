using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentGenerateFlowOptions
{
    public const string SectionName = "AI:VisionAgent:GenerateFlow";

    public bool Enabled { get; set; } = true;

    // Missing configuration must fail closed. Non-production callers must opt in
    // explicitly; this option only controls compatibility ingress policy.
    public string Policy { get; set; } = "production";

    public AiAgentGenerateFlowPolicyKind PolicyKind =>
        AiAgentGenerateFlowModePolicy.ParsePolicy(Policy);
}
