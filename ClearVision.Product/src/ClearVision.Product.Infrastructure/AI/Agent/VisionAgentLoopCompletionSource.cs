using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentLoopCompletionSource
{
    Task<string> CompleteAsync(
        VisionAgentLoopCompletionRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentLoopCompletionSource : IVisionAgentLoopCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;
    private readonly JsonToolCallRepair _jsonRepair;
    private readonly AgentPlannerCompletionOptions _options;

    public VisionAgentLoopCompletionSource(
        AiGenerationOrchestrator orchestrator,
        JsonToolCallRepair jsonRepair,
        IOptions<AgentPlannerCompletionOptions>? options = null)
    {
        _orchestrator = orchestrator;
        _jsonRepair = jsonRepair;
        _options = (options?.Value ?? new AgentPlannerCompletionOptions()).Normalize();
    }

    public async Task<string> CompleteAsync(
        VisionAgentLoopCompletionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Vision Agent tool_loop completion is disabled by options.");
        }

        var systemPrompt = request.Messages
            .FirstOrDefault(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? string.Empty;
        var messages = request.Messages
            .Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            .TakeLast(_options.MaxMessages)
            .Select(message => new ChatMessage(
                NormalizeRole(message.Role),
                Bound(message.Content, _options.MaxMessageChars)))
            .ToList();
        if (messages.Count == 0)
        {
            messages.Add(new ChatMessage("user", Bound(request.GenerationRequest.Description, _options.MaxMessageChars)));
        }

        var model = _orchestrator.ResolveModelForRole(_options.ModelRole);
        var completion = await _orchestrator.CompleteAsync(
            systemPrompt,
            messages,
            model,
            cancellationToken);
        var bounded = Bound(completion.Content, _options.MaxCompletionChars);
        if (_jsonRepair.TryNormalizeProtocolJson(bounded, out var normalized, out var failureReason))
        {
            return normalized;
        }

        throw new InvalidOperationException($"Vision Agent tool_loop completion protocol invalid: {failureReason}");
    }

    private static string NormalizeRole(string? role)
    {
        return string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? "assistant"
            : "user";
    }

    private static string Bound(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxChars
            ? text
            : text[..maxChars] + "...[truncated]";
    }
}

public sealed record VisionAgentLoopCompletionRequest
{
    public AiFlowGenerationRequest GenerationRequest { get; init; } = new(string.Empty);
    public IReadOnlyList<VisionAgentLoopMessage> Messages { get; init; } = Array.Empty<VisionAgentLoopMessage>();
}
