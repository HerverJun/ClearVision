namespace ClearVision.Product.Core.AI.Tools;

public sealed record VisionAgentToolTrace
{
    public string ToolName { get; init; } = string.Empty;
    public object? Arguments { get; init; }
    public bool Success { get; init; }
    public object? ResultSummary { get; init; }
    public object? ValidationPreviewSummary { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }
    public string Permission { get; init; } = string.Empty;
    public string ToolCallingMode { get; init; } = string.Empty;
}

