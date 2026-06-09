namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentLoopOptions
{
    public int MaxToolRounds { get; set; } = 3;
    public int MaxToolCallsPerRound { get; set; } = 4;
    public int MaxToolResultChars { get; set; } = 12_000;
    public int ToolTimeoutMs { get; set; } = 10_000;

    public void Normalize()
    {
        MaxToolRounds = Math.Clamp(MaxToolRounds, 0, 16);
        MaxToolCallsPerRound = Math.Clamp(MaxToolCallsPerRound, 1, 16);
        MaxToolResultChars = Math.Clamp(MaxToolResultChars, 256, 128_000);
        ToolTimeoutMs = Math.Clamp(ToolTimeoutMs, 250, 120_000);
    }
}
