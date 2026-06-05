using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class LlmVisionAgentPlannerCompletionSource : IVisionAgentPlannerCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;
    private readonly AgentPlannerPromptComposer _promptComposer;
    private readonly JsonToolCallRepair _jsonRepair;
    private readonly AgentPlannerCompletionOptions _options;
    private readonly Action<LlmVisionAgentPlannerCompletionDiagnostic>? _diagnosticObserver;

    public LlmVisionAgentPlannerCompletionSource(
        AiGenerationOrchestrator orchestrator,
        AgentPlannerPromptComposer promptComposer,
        JsonToolCallRepair jsonRepair,
        IOptions<AgentPlannerCompletionOptions>? options = null,
        Action<LlmVisionAgentPlannerCompletionDiagnostic>? diagnosticObserver = null)
    {
        _orchestrator = orchestrator;
        _promptComposer = promptComposer;
        _jsonRepair = jsonRepair;
        _options = (options?.Value ?? new AgentPlannerCompletionOptions()).Normalize();
        _diagnosticObserver = diagnosticObserver;
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
            Observe(model, initialParseSuccess: true, repairUsed: false, failureReason: null, repairFailureReason: null);
            return normalized;
        }

        if (!_options.AllowRepair || _options.MaxRepairAttempts == 0)
        {
            Observe(model, initialParseSuccess: false, repairUsed: false, failureReason, repairFailureReason: null);
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
            Observe(model, initialParseSuccess: false, repairUsed: true, failureReason, repairFailureReason: null);
            return normalized;
        }

        Observe(model, initialParseSuccess: false, repairUsed: true, failureReason, repairFailureReason);
        throw new InvalidOperationException(
            $"Vision Agent planner completion repair failed: {repairFailureReason}");
    }

    private void Observe(
        AiModelConfig model,
        bool initialParseSuccess,
        bool repairUsed,
        string? failureReason,
        string? repairFailureReason)
    {
        _diagnosticObserver?.Invoke(new LlmVisionAgentPlannerCompletionDiagnostic(
            string.IsNullOrWhiteSpace(model.Model) ? model.Id : model.Model,
            initialParseSuccess,
            repairUsed,
            failureReason,
            repairFailureReason));
    }

    private string BoundCompletion(string? content)
    {
        var text = content ?? string.Empty;
        return text.Length <= _options.MaxCompletionChars
            ? text
            : text[.._options.MaxCompletionChars];
    }
}

public sealed record LlmVisionAgentPlannerCompletionDiagnostic(
    string ModelName,
    bool InitialParseSuccess,
    bool RepairUsed,
    string? FailureReason,
    string? RepairFailureReason);
