namespace ClearVision.Product.Core.AI.Tools;

public sealed class VisionAgentToolResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
    public object? Summary { get; set; } // Short summary for logging/traces

    public static VisionAgentToolResult CreateSuccess(object data, object? summary = null) => new()
    {
        Success = true,
        Data = data,
        Summary = summary ?? data
    };

    public static VisionAgentToolResult CreateFailure(string errorMessage, object? data = null) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        Data = data
    };
}
