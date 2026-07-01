using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentPlannerCompletionOptions
{
    public const string SectionName = "AI:VisionAgent:PlannerCompletion";

    public bool Enabled { get; set; } = true;

    public string ModelRole { get; set; } = AiModelConfig.RolePlanner;

    public bool AllowRepair { get; set; } = true;

    public int MaxRepairAttempts { get; set; } = 1;

    public int MaxMessages { get; set; } = 12;

    public int MaxMessageChars { get; set; } = 4_000;

    public int MaxSummaryChars { get; set; } = 6_000;

    public int MaxCompletionChars { get; set; } = 64_000;

    public AgentPlannerCompletionOptions Normalize()
    {
        ModelRole = AiModelConfig.NormalizeRoleName(ModelRole);
        MaxRepairAttempts = Math.Clamp(MaxRepairAttempts, 0, 1);
        MaxMessages = Math.Clamp(MaxMessages, 2, 32);
        MaxMessageChars = Math.Clamp(MaxMessageChars, 512, 16_000);
        MaxSummaryChars = Math.Clamp(MaxSummaryChars, 512, 24_000);
        MaxCompletionChars = Math.Clamp(MaxCompletionChars, 1_024, 256_000);
        return this;
    }
}
