// IAiToolCallingConnector.cs
// Optional runtime contract for providers that support native tool calls.
namespace ClearVision.Product.Infrastructure.AI.Runtime;

public interface IAiToolCallingConnector
{
    Task<AiCompletionResult> CompleteWithToolsAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        IReadOnlyList<AiNativeToolDefinition> tools,
        CancellationToken cancellationToken = default);
}
