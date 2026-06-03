namespace ClearVision.Product.Core.AI.Tools;

public sealed record VisionAgentToolContext
{
    public string UserDescription { get; init; } = string.Empty;
    public string? AdditionalContext { get; init; }
    public string? SessionId { get; init; }
    public string? ExistingFlowJson { get; init; }
    public string PromptMode { get; init; } = AiPromptModes.LegacyFullPrompt;
    public bool DebugPrompt { get; init; }
    public int MaxToolResultChars { get; init; } = 12_000;
    public string ToolCallingMode { get; init; } = "JSON fallback";

    public IReadOnlySet<VisionAgentToolPermission> AllowedPermissions { get; init; } =
        new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation
        };
}

public static class AiPromptModes
{
    public const string LegacyFullPrompt = "legacy_full_prompt";
    public const string Hybrid = "hybrid";
    public const string AgentTools = "agent_tools";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
        return normalized switch
        {
            Hybrid => Hybrid,
            AgentTools => AgentTools,
            _ => LegacyFullPrompt
        };
    }

    public static bool UsesAgentTools(string? value)
    {
        var normalized = Normalize(value);
        return normalized is Hybrid or AgentTools;
    }
}

