namespace ClearVision.Product.Core.AI.Tools;

public sealed record VisionAgentToolContext
{
    public string UserDescription { get; init; } = string.Empty;
    public string? AdditionalContext { get; init; }
    public string? SessionId { get; init; }
    public string? ExistingFlowJson { get; init; }
    public bool DebugTrace { get; init; }
    public int MaxToolResultChars { get; init; } = 12_000;

    public IReadOnlySet<VisionAgentToolPermission> AllowedPermissions { get; init; } =
        new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation
        };
}
