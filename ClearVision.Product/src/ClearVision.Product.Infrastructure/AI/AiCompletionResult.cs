// AiCompletionResult.cs
// AI 完成结果模型
// 定义 AI 请求返回的文本、状态与统计信息
// 作者：蘅芜君
namespace ClearVision.Product.Infrastructure.AI;

/// <summary>
/// AI API 调用结果，包含内容和思维链推理过程
/// </summary>
public class AiCompletionResult
{
    /// <summary>
    /// AI 生成的主要内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// AI 的推理/思维链内容（DeepSeek reasoning_content / Anthropic thinking）
    /// 可能为空，取决于模型是否支持
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// Token usage reported by the API (null if not available).
    /// </summary>
    public AiTokenUsage? TokenUsage { get; set; }

    /// <summary>
    /// Native provider tool calls requested by the model.
    /// </summary>
    public List<AiNativeToolCall> ToolCalls { get; set; } = new();
}

/// <summary>
/// Token usage statistics from an API response.
/// </summary>
public class AiTokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int? CacheReadTokens { get; set; }
    public int? CacheWriteTokens { get; set; }
}

public sealed record AiNativeToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public System.Text.Json.JsonElement ParametersSchema { get; init; }
}

public sealed record AiNativeToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public System.Text.Json.JsonElement Arguments { get; init; }
    public string? ResponseItemId { get; init; }
}

public sealed record AiNativeToolResult
{
    public string ToolCallId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public bool IsError { get; init; }
}
