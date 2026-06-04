using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class LlmVisionAgentPlannerCompletionSource : IVisionAgentPlannerCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;
    private readonly AgentPlannerPromptComposer _promptComposer;
    private readonly JsonToolCallRepair _jsonRepair;
    private readonly AgentPlannerCompletionOptions _options;

    public LlmVisionAgentPlannerCompletionSource(
        AiGenerationOrchestrator orchestrator,
        AgentPlannerPromptComposer promptComposer,
        JsonToolCallRepair jsonRepair,
        IOptions<AgentPlannerCompletionOptions>? options = null)
    {
        _orchestrator = orchestrator;
        _promptComposer = promptComposer;
        _jsonRepair = jsonRepair;
        _options = (options?.Value ?? new AgentPlannerCompletionOptions()).Normalize();
    }

    public async Task<string> CompleteAsync(
        AgentPlannerCompletionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Vision Agent planner LLM completion is disabled by options.");
        }

        var prompt = _promptComposer.Compose(request, _options);
        var model = _orchestrator.ResolveModelForRole(_options.ModelRole);
        var completion = await _orchestrator.CompleteAsync(
            prompt.SystemPrompt,
            prompt.Messages,
            model,
            cancellationToken);
        return await NormalizeOrRepairAsync(
            request,
            completion.Content,
            model,
            cancellationToken);
    }

    private async Task<string> NormalizeOrRepairAsync(
        AgentPlannerCompletionRequest request,
        string raw,
        AiModelConfig model,
        CancellationToken cancellationToken)
    {
        var boundedRaw = BoundCompletion(raw);
        if (_jsonRepair.TryNormalizeProtocolJson(boundedRaw, out var normalized, out var failureReason))
        {
            return normalized;
        }

        if (!_options.AllowRepair || _options.MaxRepairAttempts == 0)
        {
            throw new InvalidOperationException($"Vision Agent planner completion protocol invalid: {failureReason}");
        }

        var repairPrompt = _promptComposer.ComposeRepair(request, boundedRaw, failureReason, _options);
        var repaired = await _orchestrator.CompleteAsync(
            repairPrompt.SystemPrompt,
            repairPrompt.Messages,
            model,
            cancellationToken);
        var boundedRepair = BoundCompletion(repaired.Content);
        if (_jsonRepair.TryNormalizeProtocolJson(boundedRepair, out normalized, out var repairFailureReason))
        {
            return normalized;
        }

        throw new InvalidOperationException(
            $"Vision Agent planner completion repair failed: {repairFailureReason}");
    }

    private string BoundCompletion(string? content)
    {
        var text = content ?? string.Empty;
        return text.Length <= _options.MaxCompletionChars
            ? text
            : text[.._options.MaxCompletionChars];
    }
}
