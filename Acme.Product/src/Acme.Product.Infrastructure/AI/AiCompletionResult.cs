// AiCompletionResult.cs
// AI 完成结果模型
// 定义 AI 请求返回的文本、状态与统计信息
// 作者：蘅芜君
namespace Acme.Product.Infrastructure.AI;

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
