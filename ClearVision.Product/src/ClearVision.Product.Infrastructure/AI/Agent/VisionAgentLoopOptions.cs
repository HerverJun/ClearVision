namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentLoopOptions
{
    public int MaxToolRounds { get; set; } = 3;
    public int MaxToolCallsPerRound { get; set; } = 6;
    public int MaxToolResultChars { get; set; } = 12_000;

    public void Normalize()
    {
        MaxToolRounds = Math.Clamp(MaxToolRounds <= 0 ? 3 : MaxToolRounds, 1, 5);
        MaxToolCallsPerRound = Math.Clamp(MaxToolCallsPerRound <= 0 ? 6 : MaxToolCallsPerRound, 1, 12);
        MaxToolResultChars = Math.Clamp(MaxToolResultChars <= 0 ? 12_000 : MaxToolResultChars, 2_000, 64_000);
    }
}

