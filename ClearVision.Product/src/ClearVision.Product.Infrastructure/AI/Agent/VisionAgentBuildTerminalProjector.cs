using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Application.DTOs;
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
    AgentRunEvent TerminalEvent,
    bool Recovered = false);

public interface IVisionAgentBuildTerminalProjector
{
    bool Project(VisionAgentBuildTerminalProjection projection);

    bool ProjectRecovered(AgentRunReplayResult replay);
}

public sealed class VisionAgentBuildTerminalProjector : IVisionAgentBuildTerminalProjector
{
    private static readonly Regex SafeSessionIdRegex = new(
        "^[A-Za-z0-9_.:-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConversationalFlowService _conversationalFlowService;
    private readonly IVisionAgentBuildProjectionJournal _journal;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector> _logger;

    public VisionAgentBuildTerminalProjector(
        IConversationalFlowService conversationalFlowService,
        IVisionAgentBuildProjectionJournal journal,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector> logger)
    {
        _conversationalFlowService = conversationalFlowService;
        _journal = journal;
        _logger = logger;
    }

    public bool Project(VisionAgentBuildTerminalProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(projection.Request);
        ArgumentNullException.ThrowIfNull(projection.Result);
        ArgumentNullException.ThrowIfNull(projection.TerminalEvent);

        var runId = FirstNonBlank(projection.RunId, projection.TerminalEvent.RunId, projection.Request.AgentRunId);
        if (string.IsNullOrWhiteSpace(runId))
        {
            return false;
        }

        var terminalSource = TryGetTerminalSource(projection.TerminalEvent);
        var proposedSessionId = NormalizeProjectionSessionId(
            runId,
            FirstNonBlank(
                projection.Request.SessionId,
                projection.Result.SessionId,
                TryReadString(terminalSource, "sessionId")));
        var sessionId = _journal.ResolveSessionId(
            runId,
            projection.TerminalEvent.Sequence,
            projection.TerminalEvent.EventType,
            proposedSessionId);
        var session = projection.Recovered
            ? _conversationalFlowService.GetSession(sessionId)
            : _conversationalFlowService.GetOrCreateSession(sessionId);
        if (session == null)
        {
            return false;
        }

        projection.Result.SessionId = session.SessionId;
        var result = projection.Result;
        var terminal = projection.TerminalEvent;
        var basis = BuildProjectionBasis(terminalSource, result, projection.Request);
        if (projection.Recovered && !HasCompleteTerminalProjectionBasis(terminalSource))
        {
            MarkRecoveryConflict(
                session.SessionId,
                runId,
                NormalizeTerminalStatusFromEvent(terminal),
                "terminal_projection_basis_missing",
                session.WorkspaceSnapshot);
            return false;
        }

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
        var assistantTurnId = $"build:{runId}:terminal:{terminal.Sequence}:assistant";
        var workspaceUpdate = new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = basis.AssociationWorkspaceRevision ?? session.WorkspaceSnapshot?.Revision,
            LifecycleState = result.Success
                ? "build_completed"
                : (string.Equals(result.CompletionStatus, AiFlowGenerationResult.CompletionStatusCancelled, StringComparison.OrdinalIgnoreCase)
                    ? "build_cancelled"
                    : "build_failed"),
            PendingPlanSnapshot = projection.Request.BuildFromPlan?.PlanSnapshot,
            PlanQuestionSelections = projection.Request.BuildFromPlan?.UserSelections,
            ConfirmedPlanAnswers = projection.Request.BuildFromPlan?.ConfirmedAnswers,
            RequirementMode = projection.Request.RequirementMode,
            PlanAcceptedRecommendedDefaults = projection.Request.BuildFromPlan?.AcceptedRecommendedDefaults,
            BuildRunId = runId,
            BuildRunStatus = result.Success
                ? AgentRunEventStatuses.Completed
                : (string.Equals(result.CompletionStatus, AiFlowGenerationResult.CompletionStatusCancelled, StringComparison.OrdinalIgnoreCase)
                    ? AgentRunEventStatuses.Cancelled
                    : AgentRunEventStatuses.Failed),
            BuildTerminalSequence = terminal.Sequence,
            SubmittedBuildFingerprint = basis.SubmittedBuildFingerprint
        };
        var projectionRequest = new VisionAgentTerminalProjectionRequest
        {
            SessionId = session.SessionId,
            AssistantTurnId = assistantTurnId,
            AssistantMessage = assistantMessage,
            LatestFlowJson = flowJson,
            LatestCanvasFlowJson = flowJson,
            Payload = payload,
            WorkspaceUpdate = workspaceUpdate
        };
        var projectionFingerprint = ConversationalFlowService.ComputeTerminalProjectionFingerprint(
            projectionRequest,
            assistantTurnId);
        var projectionMutationId = ConversationalFlowService.BuildTerminalProjectionMutationId(
            assistantTurnId,
            projectionFingerprint);
        if (projection.Recovered &&
            workspaceUpdate.ExpectedRevision.HasValue &&
            session.WorkspaceSnapshot?.Revision != workspaceUpdate.ExpectedRevision.Value &&
            !HasMatchingWorkspaceReceipt(session, projectionMutationId, projectionFingerprint))
        {
            MarkRecoveryConflict(
                session.SessionId,
                runId,
                NormalizeTerminalStatusFromEvent(terminal),
                "workspace_revision_drift",
                session.WorkspaceSnapshot);
            return false;
        }

        var begin = _journal.Begin(
            runId,
            sessionId,
            projection.TerminalEvent.Sequence,
            projection.TerminalEvent.EventType,
            projectionMutationId,
            projectionFingerprint,
            workspaceUpdate.ExpectedRevision,
            basis.BuildIdentity,
            string.Empty);
        if (begin.Status == VisionAgentBuildProjectionBeginStatus.MetadataConflict)
        {
            MarkRecoveryConflict(
                session.SessionId,
                runId,
                NormalizeTerminalStatusFromEvent(terminal),
                "projection_checkpoint_metadata_conflict",
                session.WorkspaceSnapshot);
            return false;
        }

        if (begin.Status != VisionAgentBuildProjectionBeginStatus.Started)
        {
            return false;
        }

        try
        {
            var projectionResult = _conversationalFlowService.ProjectBuildTerminal(projectionRequest);
            if (!projectionResult.Success)
            {
                if (projection.Recovered && projectionResult.Conflict)
                {
                    MarkRecoveryConflict(
                        session.SessionId,
                        runId,
                        NormalizeTerminalStatusFromEvent(terminal),
                        projectionResult.ErrorCode,
                        session.WorkspaceSnapshot);
                    return false;
                }

                if (projection.Recovered && !projectionResult.PersistenceStatus.PrimaryStoreSaved)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(projectionResult.PublicMessage)
                        ? "Vision Agent Build recovery could not persist conversation state."
                        : projectionResult.PublicMessage);
                }

                _journal.MarkFailed(
                    runId,
                    session.SessionId,
                    terminal.Sequence,
                    terminal.EventType,
                    new IOException(string.IsNullOrWhiteSpace(projectionResult.PublicMessage)
                        ? projectionResult.ErrorCode
                        : projectionResult.PublicMessage));
                return false;
            }

            _journal.MarkProjected(
                runId,
                session.SessionId,
                terminal.Sequence,
                terminal.EventType);
            return true;
        }
        catch (Exception ex)
        {
            if (projection.Recovered)
            {
                throw;
            }

            _journal.MarkFailed(
                runId,
                sessionId,
                projection.TerminalEvent.Sequence,
                projection.TerminalEvent.EventType,
                ex);
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                ex,
                "Failed to project AgentRun terminal outcome. RunId={RunId}, SessionId={SessionId}",
                runId,
                sessionId);
            return false;
        }
    }

    public bool ProjectRecovered(AgentRunReplayResult replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (VisionAgentRunKindResolver.Resolve(replay) != VisionAgentRunKind.Build)
        {
            return false;
        }

        var terminal = replay.Events
            .OrderBy(evt => evt.Sequence)
            .LastOrDefault(IsTerminalEvent);
        if (terminal == null)
        {
            return false;
        }

        var source = TryGetTerminalSource(terminal);
        var startPayload = replay.Events
            .OrderBy(evt => evt.Sequence)
            .FirstOrDefault(evt => string.Equals(evt.EventType, AgentRunEventTypes.RunStarted, StringComparison.OrdinalIgnoreCase))
            ?.Payload;
        var sessionId = FirstNonBlank(
            TryReadString(source, "sessionId"),
            TryReadString(ToJsonElement(startPayload), "sessionId"));
        var request = BuildRecoveredRequest(replay, terminal, source, sessionId);
        var result = BuildRecoveredResult(terminal, source, request);
        return Project(new VisionAgentBuildTerminalProjection(
            terminal.RunId,
            BuildCommandTransports.AgentRun,
            request,
            result,
            terminal,
            Recovered: true));
    }

    private ProjectionBasis BuildProjectionBasis(
        JsonElement? terminalSource,
        AiFlowGenerationResult result,
        AiFlowGenerationRequest request)
    {
        var planId = FirstNonBlank(
            TryReadString(terminalSource, "planId"),
            result.PlanId,
            result.BuildResult?.PlanId,
            request.BuildFromPlan?.PlanId,
            request.BuildFromPlan?.PlanSnapshot?.PlanId);
        var planHash = FirstNonBlank(
            TryReadString(terminalSource, "planHash"),
            result.PlanHash,
            result.BuildResult?.PlanHash,
            request.BuildFromPlan?.PlanHash,
            request.BuildFromPlan?.PlanSnapshot?.PlanHash);
        var answerSetFingerprint = FirstNonBlank(
            TryReadString(terminalSource, "answerSetFingerprint"),
            result.AnswerSetFingerprint,
            result.BuildResult?.AnswerSetFingerprint);
        var submittedBuildFingerprint = FirstNonBlank(
            TryReadString(terminalSource, "submittedBuildFingerprint"),
            answerSetFingerprint,
            planHash);
        var buildIdentity = FirstNonBlank(
            TryReadString(terminalSource, "buildIdentity"),
            BuildBuildIdentity(planId, planHash, answerSetFingerprint, submittedBuildFingerprint));

        return new ProjectionBasis(
            TryReadLong(terminalSource, "associationWorkspaceRevision"),
            submittedBuildFingerprint,
            buildIdentity);
    }

    private static bool HasCompleteTerminalProjectionBasis(JsonElement? terminalSource)
    {
        return TryReadLong(terminalSource, "associationWorkspaceRevision").HasValue &&
               !string.IsNullOrWhiteSpace(TryReadString(terminalSource, "submittedBuildFingerprint")) &&
               !string.IsNullOrWhiteSpace(TryReadString(terminalSource, "planId")) &&
               !string.IsNullOrWhiteSpace(TryReadString(terminalSource, "planHash")) &&
               !string.IsNullOrWhiteSpace(TryReadString(terminalSource, "answerSetFingerprint")) &&
               !string.IsNullOrWhiteSpace(TryReadString(terminalSource, "buildIdentity"));
    }

    private static bool HasMatchingWorkspaceReceipt(
        ConversationSession session,
        string mutationId,
        string payloadFingerprint)
    {
        return !string.IsNullOrWhiteSpace(mutationId) &&
               !string.IsNullOrWhiteSpace(payloadFingerprint) &&
               session.MutationReceipts.Any(receipt =>
                   string.Equals(receipt.MutationId, mutationId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(receipt.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal));
    }

    private void MarkRecoveryConflict(
        string sessionId,
        string runId,
        string terminalStatus,
        string conflictCode,
        VisionAgentWorkspaceSnapshot? workspace)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || workspace == null)
        {
            return;
        }

        _conversationalFlowService.TryUpdateWorkspaceSnapshot(sessionId, new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = workspace.Revision,
            ClientMutationId = $"recovery-conflict:{runId}",
            LifecycleState = "recovery_conflict"
        });

        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
            _logger,
            "Vision Agent Build recovery conflict. RunId={RunId}, SessionId={SessionId}, TerminalStatus={TerminalStatus}, ConflictCode={ConflictCode}",
            runId,
            sessionId,
            terminalStatus,
            conflictCode);
    }

    private static string BuildBuildIdentity(
        string planId,
        string planHash,
        string answerSetFingerprint,
        string submittedBuildFingerprint)
    {
        return string.Join(
            ":",
            new[]
            {
                SanitizeIdentityToken(planId),
                SanitizeIdentityToken(planHash),
                SanitizeIdentityToken(answerSetFingerprint),
                SanitizeIdentityToken(submittedBuildFingerprint)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string SanitizeIdentityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            string.Empty,
            value.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is ':' or '_' or '-' or '.'));
    }

    private static AiFlowGenerationRequest BuildRecoveredRequest(
        AgentRunReplayResult replay,
        AgentRunEvent terminal,
        JsonElement? source,
        string? sessionId)
    {
        var buildFromPlan = TryDeserializeProperty<VisionAgentBuildFromPlanRequest>(source, "buildFromPlan");
        if (buildFromPlan == null)
        {
            var plan = TryDeserializeProperty<VisionAgentPlanModeResult>(source, "planSnapshot");
            var planId = FirstNonBlank(
                TryReadString(source, "planId"),
                plan?.PlanId);
            var planHash = FirstNonBlank(
                TryReadString(source, "planHash"),
                plan?.PlanHash);
            if (!string.IsNullOrWhiteSpace(planId) || plan != null)
            {
                buildFromPlan = new VisionAgentBuildFromPlanRequest
                {
                    PlanId = planId,
                    PlanHash = planHash,
                    PlanSnapshot = plan
                };
            }
        }

        return new AiFlowGenerationRequest(
            FirstNonBlank(replay.Summary.Summary, terminal.Summary, "Recovered Vision Agent BuildFromPlan terminal."),
            SessionId: sessionId,
            Mode: GenerateFlowModeExtensions.ParseOrAuto(TryReadString(source, "requestedMode")))
        {
            AgentRunId = terminal.RunId,
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Normalize(TryReadString(source, "requestedMode")),
            BuildFromPlan = buildFromPlan
        };
    }

    private static AiFlowGenerationResult BuildRecoveredResult(
        AgentRunEvent terminal,
        JsonElement? source,
        AiFlowGenerationRequest request)
    {
        var completionStatus = FirstNonBlank(
            TryReadString(source, "status"),
            terminal.EventType switch
            {
                AgentRunEventTypes.RunCompleted => AiFlowGenerationResult.CompletionStatusCompleted,
                AgentRunEventTypes.RunCancelled => AiFlowGenerationResult.CompletionStatusCancelled,
                _ => AiFlowGenerationResult.CompletionStatusFailed
            });
        var success = string.Equals(completionStatus, AiFlowGenerationResult.CompletionStatusCompleted, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(terminal.EventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase);
        var failureSummary = TryDeserializeProperty<AiFailureSummary>(source, "failureSummary");
        var buildResult = TryDeserializeProperty<VisionAgentBuildResult>(source, "buildResult");

        return new AiFlowGenerationResult
        {
            Success = success,
            CompletionStatus = completionStatus,
            FailureType = success
                ? string.Empty
                : FirstNonBlank(
                    TryReadString(source, "failureType"),
                    string.Equals(terminal.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase)
                        ? AiFlowGenerationResult.FailureTypeUserCancelled
                        : AiFlowGenerationResult.FailureTypeSystemError),
            ErrorMessage = success ? null : FirstNonBlank(terminal.Summary, failureSummary?.Message),
            FailureSummary = failureSummary,
            Flow = TryDeserializeProperty<OperatorFlowDto>(source, "flow"),
            AiExplanation = TryReadString(source, "aiExplanation"),
            BuildResult = buildResult,
            BuildReadiness = TryDeserializeProperty<VisionAgentBuildReadinessSnapshot>(source, "buildReadiness"),
            PendingParameters = TryDeserializeProperty<List<AiPendingParameterInfo>>(source, "pendingParameters") ?? [],
            MissingResources = TryDeserializeProperty<List<AiMissingResourceInfo>>(source, "missingResources") ?? [],
            PlanId = FirstNonBlank(TryReadString(source, "planId"), buildResult?.PlanId, request.BuildFromPlan?.PlanId),
            PlanHash = FirstNonBlank(TryReadString(source, "planHash"), buildResult?.PlanHash, request.BuildFromPlan?.PlanHash),
            ContractVersion = FirstNonBlank(TryReadString(source, "contractVersion"), buildResult?.ContractVersion, request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion),
            AnswerSetFingerprint = FirstNonBlank(TryReadString(source, "answerSetFingerprint"), buildResult?.AnswerSetFingerprint),
            RequestedMode = AiAgentGenerateFlowModes.Normalize(TryReadString(source, "requestedMode")),
            EffectiveMode = AiAgentGenerateFlowModes.Normalize(TryReadString(source, "effectiveMode")),
            ToolLoopEntered = TryReadBool(source, "toolLoopEntered"),
            FallbackReason = TryReadString(source, "fallbackReason") ?? string.Empty,
            InteractionState = FirstNonBlank(
                TryReadString(source, "interactionState"),
                success ? AiInteractionStates.Completed : AiInteractionStates.Failed),
            TurnIntent = FirstNonBlank(TryReadString(source, "turnIntent"), AiTurnIntents.NewFlow),
            RouterConfidence = FirstNonBlank(TryReadString(source, "routerConfidence"), AiRouterConfidence.High),
            RequirementMaturity = TryDeserializeProperty<AiRequirementMaturityResult>(source, "requirementMaturity"),
            DecisionTrace = TryDeserializeProperty<AiDecisionTrace>(source, "decisionTrace")
        };
    }

    private static JsonElement? TryGetTerminalSource(AgentRunEvent terminal)
    {
        var payload = ToJsonElement(terminal.Payload);
        if (payload == null)
        {
            return null;
        }

        if (string.Equals(terminal.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) &&
            TryGetProperty(payload.Value, "diagnostic", out var diagnostic))
        {
            return diagnostic;
        }

        return payload;
    }

    private static JsonElement? ToJsonElement(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is JsonElement element)
        {
            return element;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(payload, AgentRunEventJson.Options),
                AgentRunEventJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T? TryDeserializeProperty<T>(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return default;
        }

        try
        {
            return property.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? TryReadString(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static bool TryReadBool(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static long? TryReadLong(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string NormalizeTerminalStatusFromEvent(AgentRunEvent terminal)
    {
        if (string.Equals(terminal.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(terminal.Status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Cancelled;
        }

        if (string.Equals(terminal.EventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(terminal.Status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Completed;
        }

        return AgentRunEventStatuses.Failed;
    }

    private static bool TryGetProperty(JsonElement source, string propertyName, out JsonElement property)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in source.EnumerateObject())
            {
                if (string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = item.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string NormalizeProjectionSessionId(string runId, string? sessionId)
    {
        var normalized = sessionId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && SafeSessionIdRegex.IsMatch(normalized))
        {
            return normalized;
        }

        var safeRunId = string.Join(
            string.Empty,
            (runId ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.'));
        return $"agent-run-{(string.IsNullOrWhiteSpace(safeRunId) ? "unknown" : safeRunId)}";
    }

    private static bool IsTerminalEvent(AgentRunEvent evt)
    {
        return string.Equals(evt.EventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private sealed record ProjectionBasis(
        long? AssociationWorkspaceRevision,
        string SubmittedBuildFingerprint,
        string BuildIdentity);
}
