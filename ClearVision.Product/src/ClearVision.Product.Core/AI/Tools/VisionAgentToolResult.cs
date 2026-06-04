namespace ClearVision.Product.Core.AI.Tools;

public sealed record VisionAgentToolResult
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresUserConfirmation { get; init; }
    public List<VisionAgentPendingAction> PendingActions { get; init; } = new();

    public static VisionAgentToolResult Ok(
        object? data = null,
        bool requiresUserConfirmation = false,
        IEnumerable<VisionAgentPendingAction>? pendingActions = null)
    {
        return new VisionAgentToolResult
        {
            Success = true,
            Data = data,
            RequiresUserConfirmation = requiresUserConfirmation,
            PendingActions = pendingActions?.ToList() ?? new List<VisionAgentPendingAction>()
        };
    }

    public static VisionAgentToolResult Fail(
        string errorCode,
        string errorMessage,
        object? data = null)
    {
        return new VisionAgentToolResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Data = data
        };
    }
}

public sealed record VisionAgentPendingAction
{
    public string ActionType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public bool RequiresUserConfirmation { get; init; } = true;
}
