using System.Collections.Concurrent;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentBuildTerminalProjection(
    string RunId,
    string Transport,
    AiFlowGenerationRequest Request,
    AiFlowGenerationResult Result,
    AgentRunEvent TerminalEvent);

public interface IVisionAgentBuildTerminalProjector
{
    bool Project(VisionAgentBuildTerminalProjection projection);
}

public sealed class VisionAgentBuildTerminalProjector : IVisionAgentBuildTerminalProjector
{
    private static readonly ConcurrentDictionary<string, byte> ProjectedTerminals =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConversationalFlowService _conversationalFlowService;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector> _logger;

    public VisionAgentBuildTerminalProjector(
        IConversationalFlowService conversationalFlowService,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector> logger)
    {
        _conversationalFlowService = conversationalFlowService;
        _logger = logger;
    }

    public bool Project(VisionAgentBuildTerminalProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(projection.Request);
        ArgumentNullException.ThrowIfNull(projection.Result);
        ArgumentNullException.ThrowIfNull(projection.TerminalEvent);

        var session = _conversationalFlowService.GetOrCreateSession(projection.Request.SessionId);
        projection.Result.SessionId = session.SessionId;
        var runId = FirstNonBlank(projection.RunId, projection.TerminalEvent.RunId, projection.Request.AgentRunId);
        var projectionKey = $"{runId}:{session.SessionId}:terminal";
        if (!ProjectedTerminals.TryAdd(projectionKey, 0))
        {
            return false;
        }

        try
        {
            var result = projection.Result;
            var terminal = projection.TerminalEvent;
            var assistantMessage = FirstNonBlank(
                terminal.Summary,
                result.Success ? result.AiExplanation : result.ErrorMessage,
                result.FailureSummary?.Message,
                result.Success
                    ? "Vision Agent BuildFromPlan completed."
                    : "Vision Agent BuildFromPlan failed.");
            var flowJson = result.Flow == null ? null : JsonSerializer.Serialize(result.Flow, JsonOptions);
            var payload = new ConversationTurnPayload
            {
                Kind = result.Success ? "assistant_agent_result" : "assistant_agent_failure",
                Status = result.CompletionStatus,
                InteractionState = result.InteractionState,
                TurnIntent = result.TurnIntent,
                RouterConfidence = result.RouterConfidence,
                Reply = result.Success ? assistantMessage : null,
                Progress =
                [
                    projection.Transport,
                    $"agent_run:{runId}",
                    $"terminal:{terminal.Sequence}"
                ],
                ClarificationRequired = result.ClarificationRequired,
                RequirementBrief = result.RequirementBrief,
                BuildResult = result.BuildResult,
                WorkflowDiff = result.BuildResult?.WorkflowDiff,
                ApplyGate = result.BuildResult?.ApplyGate,
                ToolEvidenceTimeline = result.BuildResult?.ToolEvidenceTimeline,
                FirstFixRecommendation = result.BuildResult?.FirstFixRecommendation ??
                                         result.FailureSummary?.RepairTarget,
                BlockingClarificationFields = result.BlockingClarificationFields.ToList(),
                NonBlockingMissingFields = result.NonBlockingMissingFields.ToList(),
                Failure = result.Success || result.FailureSummary == null
                    ? null
                    : new ConversationTurnFailurePayload
                    {
                        Summary = assistantMessage,
                        FailureSummary = result.FailureSummary,
                        Diagnostics = result.LastAttemptDiagnostics.ToList()
                    }
            };
            _conversationalFlowService.RecordAssistantResponse(
                session.SessionId,
                assistantMessage,
                flowJson,
                flowJson,
                payload);
            return true;
        }
        catch (Exception ex)
        {
            ProjectedTerminals.TryRemove(projectionKey, out _);
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                ex,
                "Failed to project AgentRun terminal outcome. RunId={RunId}, SessionId={SessionId}",
                runId,
                session.SessionId);
            return false;
        }
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
