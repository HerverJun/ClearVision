using System.Text.Json;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentPlannerService
{
    Task<string> CompleteAsync(
        AgentPlannerCompletionRequest request,
        CancellationToken cancellationToken);
}

public interface IVisionAgentPlannerCompletionSource
{
    Task<string> CompleteAsync(
        AgentPlannerCompletionRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentPlannerService : IVisionAgentPlannerService
{
    private readonly IVisionAgentPlannerCompletionSource _completionSource;
    private readonly VisionAgentProtocolParser _protocolParser;
    private readonly AgentToolCallPolicy _toolCallPolicy;
    private readonly AgentPlannerPromptBuilder _promptBuilder;

    public VisionAgentPlannerService(
        IVisionAgentPlannerCompletionSource completionSource,
        VisionAgentProtocolParser protocolParser,
        AgentToolCallPolicy toolCallPolicy,
        AgentPlannerPromptBuilder promptBuilder)
    {
        _completionSource = completionSource;
        _protocolParser = protocolParser;
        _toolCallPolicy = toolCallPolicy;
        _promptBuilder = promptBuilder;
    }

    public async Task<string> CompleteAsync(
        AgentPlannerCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var allowedToolNames = _toolCallPolicy.ListAllowedToolNames();
        var enriched = request with
        {
            PlannerPrompt = _promptBuilder.Build(
                request.GenerationRequest,
                allowedToolNames),
            AllowedToolNames = allowedToolNames
        };
        var completion = await _completionSource.CompleteAsync(enriched, cancellationToken);
        var parsed = _protocolParser.Parse(completion);
        var policy = _toolCallPolicy.Validate(parsed);
        if (!policy.Allowed)
        {
            throw new AgentToolCallPolicyViolationException(
                policy.ErrorCode ?? "tool_policy_denied",
                policy.ErrorMessage ?? "Planner tool call was denied by policy.");
        }

        return completion;
    }
}

public sealed class NoOpVisionAgentPlannerCompletionSource : IVisionAgentPlannerCompletionSource
{
    public Task<string> CompleteAsync(
        AgentPlannerCompletionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("Vision Agent planner completion source is not configured.");
    }
}

public sealed record AgentPlannerCompletionRequest
{
    public AiFlowGenerationRequest GenerationRequest { get; init; } = new(string.Empty);
    public IReadOnlyList<VisionAgentLoopMessage> Messages { get; init; } = Array.Empty<VisionAgentLoopMessage>();
    public string PlannerPrompt { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedToolNames { get; init; } = Array.Empty<string>();
    public JsonElement FlowDraft { get; init; }
    public JsonElement ValidationSummary { get; init; }
    public JsonElement DryRunSummary { get; init; }
    public JsonElement DeploymentPrecheck { get; init; }
}
