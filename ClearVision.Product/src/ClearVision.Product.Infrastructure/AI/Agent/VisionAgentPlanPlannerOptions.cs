using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentPlanPlannerOptions
{
    public const string SectionName = "AI:VisionAgent:PlanPlanner";

    public bool Enabled { get; set; } = true;

    public string ModelRole { get; set; } = AiModelConfig.RolePlanner;

    public bool AllowRuleFallback { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxContextChars { get; set; } = 12_000;

    public int MaxCompletionChars { get; set; } = 64_000;

    public VisionAgentPlanPlannerOptions Normalize()
    {
        ModelRole = AiModelConfig.NormalizeRoleName(ModelRole);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 3, 180);
        MaxContextChars = Math.Clamp(MaxContextChars, 2_000, 48_000);
        MaxCompletionChars = Math.Clamp(MaxCompletionChars, 2_000, 256_000);
        return this;
    }
}
