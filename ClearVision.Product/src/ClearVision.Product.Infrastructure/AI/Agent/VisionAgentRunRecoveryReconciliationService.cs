using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentRunRecoveryReconciliationService : IHostedService
{
    private readonly AgentRunEventStore _eventStore;
    private readonly IAgentRunEventStreamService _streamService;
    private readonly IConversationalFlowService _conversationService;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentRunRecoveryReconciliationService> _logger;

    public VisionAgentRunRecoveryReconciliationService(
        AgentRunEventStore eventStore,
        IAgentRunEventStreamService streamService,
        IConversationalFlowService conversationService,
        Microsoft.Extensions.Logging.ILogger<VisionAgentRunRecoveryReconciliationService> logger)
    {
        _eventStore = eventStore;
        _streamService = streamService;
        _conversationService = conversationService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var runIds = _eventStore.LoadSummaries()
            .Select(summary => summary.RunId)
            .Concat(_eventStore.LoadEvents().Select(evt => evt.RunId))
            .Where(runId => !string.IsNullOrWhiteSpace(runId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(runId => runId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var runId in runIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReconcileRun(runId);
        }

        return Task.CompletedTask;
    }

    private void ReconcileRun(string runId)
    {
        var replay = _streamService.ReplayRaw(runId);
        if (replay == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(replay.Summary.OwnerHash))
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                "Skipped ownerless AgentRun recovery. RunId={RunId}",
                runId);
            return;
        }

        var terminal = replay.Events
            .OrderBy(evt => evt.Sequence)
            .LastOrDefault(IsRunTerminalEvent);
        var intent = replay.Summary.TerminalIntent;
        var runKind = VisionAgentRunKindResolver.Resolve(replay);
        if (runKind == VisionAgentRunKind.Unknown)
        {
            return;
        }

        var runType = VisionAgentRunKindResolver.ToWireValue(runKind);
        var sessionId = FirstNonBlank(
            intent?.SessionId,
            ResolveSessionIdFromEvents(replay),
            ResolveSessionIdFromWorkspace(runId, runType, replay.Summary.OwnerHash));
        var recoverySession = string.IsNullOrWhiteSpace(sessionId)
            ? null
            : _conversationService.GetSessionForRecovery(sessionId);
        if (recoverySession != null &&
            !string.Equals(recoverySession.OwnerHash, replay.Summary.OwnerHash, StringComparison.Ordinal))
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                "Skipped AgentRun recovery with a mismatched conversation owner. RunId={RunId}, SessionId={SessionId}",
                runId,
                sessionId);
            return;
        }

        var session = string.IsNullOrWhiteSpace(sessionId)
            ? null
            : _conversationService.GetSession(replay.Summary.OwnerHash, sessionId);
        var workspace = session?.WorkspaceSnapshot;

        if (terminal != null)
        {
            var terminalStatus = NormalizeTerminalStatus(terminal.Status);
            if (HasWorkspaceConflict(runId, runType, workspace, terminalStatus))
            {
                MarkRecoveryConflict(sessionId, runId, runType, terminalStatus, workspace);
                return;
            }

            if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) &&
                !HasMatchingWorkspaceTerminal(runId, runType, workspace, terminalStatus))
            {
                ProjectPlanTerminal(replay, terminal, intent, terminalStatus, sessionId);
            }

            return;
        }

        if (intent != null)
        {
            if (TryCompletePreparedPlanTerminal(replay, intent, workspace))
            {
                return;
            }

            MarkRecoveryConflict(sessionId, runId, runType, intent.TargetStatus, workspace);
            return;
        }

        if (TryCompleteLegacyPlanTerminal(replay, runId, sessionId, workspace))
        {
            return;
        }

        var interruptedTerminal = _streamService.FailHostInterrupted(runId);
        EnsureRunTerminalCommitted(runId, AgentRunEventStatuses.Failed, interruptedTerminal);
        MarkHostInterruptedWorkspace(sessionId, runId, runType, workspace);
    }

    private bool TryCompletePreparedPlanTerminal(
        AgentRunReplayResult replay,
        AgentRunTerminalIntentRecord intent,
        VisionAgentWorkspaceSnapshot? workspace)
    {
        if (!string.Equals(intent.RunType, "plan", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasMatchingWorkspaceReceipt(intent.SessionId, intent.TerminalMutationId, intent.PayloadFingerprint))
        {
            CommitRunTerminalFromIntent(replay.Summary.RunId, intent);
            return true;
        }

        var planEvent = FindPlanEvidenceEvent(replay, intent.TargetStatus);
        if (planEvent == null)
        {
            return false;
        }

        var update = BuildPlanWorkspaceUpdate(replay.Summary.RunId, intent.TargetStatus, intent, planEvent, workspace);
        if (update == null)
        {
            return false;
        }

        var fingerprint = ConversationalFlowService.ComputeWorkspaceMutationFingerprint(update);
        if (!string.Equals(fingerprint, intent.PayloadFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        var projected = TryUpdateOwnedRecovery(intent.SessionId, update);
        if (!projected.Success)
        {
            ThrowIfPrimaryStoreFailed(projected, "plan terminal intent recovery", replay.Summary.RunId, intent.SessionId);
            return false;
        }

        ThrowIfPrimaryStoreFailed(projected, "plan terminal intent recovery", replay.Summary.RunId, intent.SessionId);
        CommitRunTerminalFromIntent(replay.Summary.RunId, intent);
        return true;
    }

    private bool TryCompleteLegacyPlanTerminal(
        AgentRunReplayResult replay,
        string runId,
        string sessionId,
        VisionAgentWorkspaceSnapshot? workspace)
    {
        if (workspace == null ||
            !string.Equals(workspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) ||
            !IsTerminalStatus(workspace.PlanRunStatus))
        {
            return false;
        }

        var planEvent = FindPlanEvidenceEvent(replay, workspace.PlanRunStatus);
        if (planEvent == null || workspace.PlanTerminalSequence != planEvent.Sequence)
        {
            MarkRecoveryConflict(
                sessionId,
                runId,
                "plan",
                workspace.PlanRunStatus ?? AgentRunEventStatuses.Failed,
                workspace);
            return true;
        }

        CommitRunTerminal(runId, workspace.PlanRunStatus!, BuildRecoveredPlanPayload(
            workspace.PlanRunStatus!,
            sessionId,
            runId,
            workspace,
            "legacy_plan_terminal_recovered"));
        return true;
    }

    private void ProjectPlanTerminal(
        AgentRunReplayResult replay,
        AgentRunEvent terminal,
        AgentRunTerminalIntentRecord? intent,
        string terminalStatus,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var planEvent = FindPlanEvidenceEvent(replay, terminalStatus);
        if (planEvent == null)
        {
            return;
        }

        var mutationId = FirstNonBlank(intent?.TerminalMutationId, BuildPlanTerminalMutationId(terminal.RunId, terminalStatus));
        var updateIntent = intent ?? new AgentRunTerminalIntentRecord
        {
            RunId = terminal.RunId,
            SessionId = sessionId,
            RunType = "plan",
            TargetStatus = terminalStatus,
            TerminalMutationId = mutationId,
            PayloadFingerprint = string.Empty,
            Phase = "WorkspaceProjected",
            CreatedAt = terminal.Timestamp,
            MetadataOnly = true
        };
        var recoverySession = _conversationService.GetSessionForRecovery(sessionId);
        if (recoverySession == null)
        {
            LogMissingRecoverySession(terminal.RunId, sessionId);
            return;
        }

        if (!string.Equals(recoverySession.OwnerHash, replay.Summary.OwnerHash, StringComparison.Ordinal))
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                "Skipped AgentRun recovery with a mismatched conversation owner. RunId={RunId}, SessionId={SessionId}",
                terminal.RunId,
                sessionId);
            return;
        }

        var workspace = recoverySession.WorkspaceSnapshot;
        var update = BuildPlanWorkspaceUpdate(terminal.RunId, terminalStatus, updateIntent, planEvent, workspace);
        if (update == null)
        {
            return;
        }

        var projected = _conversationService.TryUpdateWorkspaceSnapshotForRecovery(
            replay.Summary.OwnerHash,
            sessionId,
            update);
        if (string.Equals(projected.ErrorCode, "session_not_found", StringComparison.OrdinalIgnoreCase))
        {
            LogMissingRecoverySession(terminal.RunId, sessionId);
            return;
        }

        if (projected.Success)
        {
            ThrowIfPrimaryStoreFailed(projected, "plan terminal recovery", terminal.RunId, sessionId);
            return;
        }

        ThrowIfPrimaryStoreFailed(projected, "plan terminal recovery", terminal.RunId, sessionId);
        if (projected.Conflict)
        {
            MarkRecoveryConflict(sessionId, terminal.RunId, "plan", terminalStatus, workspace);
            return;
        }

        throw BuildRecoveryPersistenceException(
            "plan terminal recovery",
            terminal.RunId,
            sessionId,
            projected.ErrorCode,
            projected.PublicMessage);
    }

    private VisionAgentWorkspaceSnapshotUpdate? BuildPlanWorkspaceUpdate(
        string runId,
        string targetStatus,
        AgentRunTerminalIntentRecord intent,
        AgentRunEvent planEvent,
        VisionAgentWorkspaceSnapshot? workspace)
    {
        var status = NormalizeTerminalStatus(targetStatus);
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var mutationId = FirstNonBlank(intent.TerminalMutationId, BuildPlanTerminalMutationId(runId, status));
        var update = new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = intent.ExpectedWorkspaceRevision,
            ClientMutationId = mutationId,
            LifecycleState = status switch
            {
                AgentRunEventStatuses.Cancelled => "plan_cancelled",
                AgentRunEventStatuses.Failed => "plan_failed",
                _ => "plan_ready"
            },
            PlanRunId = runId,
            PlanRunStatus = status,
            PlanTerminalSequence = planEvent.Sequence,
            RequirementMode = FirstNonBlank(workspace?.RequirementMode, AiRequirementModes.Strict)
        };

        if (string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            var planResult = TryReadPlanResult(planEvent);
            if (planResult == null)
            {
                return null;
            }

            update.LifecycleState = planResult.CanBuild ? "plan_ready" : "plan_blocked";
            update.PendingPlanSnapshot = planResult;
            update.ConfirmedPlanAnswers = planResult.ConfirmedPlanAnswers;
        }

        return update;
    }

    private bool HasMatchingWorkspaceReceipt(string sessionId, string mutationId, string payloadFingerprint)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(mutationId) ||
            string.IsNullOrWhiteSpace(payloadFingerprint))
        {
            return false;
        }

        var session = _conversationService.GetSessionForRecovery(sessionId);
        return session?.MutationReceipts.Any(receipt =>
            string.Equals(receipt.MutationId, mutationId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(receipt.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal)) == true;
    }

    private void CommitRunTerminalFromIntent(string runId, AgentRunTerminalIntentRecord intent)
    {
        CommitRunTerminal(runId, intent.TargetStatus, BuildRecoveredPlanPayload(
            intent.TargetStatus,
            intent.SessionId,
            runId,
            _conversationService.GetSessionForRecovery(intent.SessionId)?.WorkspaceSnapshot,
            "durable_terminal_intent_recovered"));
    }

    private void CommitRunTerminal(string runId, string status, object payload)
    {
        AgentRunEvent? terminal;
        if (string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            terminal = _streamService.Complete(runId, "Plan terminal state recovered during startup.", payload);
            EnsureRunTerminalCommitted(runId, status, terminal);
            return;
        }

        if (string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            terminal = _streamService.Cancel(runId, "Plan was cancelled before startup recovery.", payload);
            EnsureRunTerminalCommitted(runId, status, terminal);
            return;
        }

        terminal = _streamService.Fail(
            runId,
            "Plan recovered as failed during startup.",
            "Create a new plan before continuing the build.",
            payload);
        EnsureRunTerminalCommitted(runId, AgentRunEventStatuses.Failed, terminal);
    }

    private void MarkRecoveryConflict(
        string sessionId,
        string runId,
        string runType,
        string status,
        VisionAgentWorkspaceSnapshot? workspace)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var latest = _conversationService.GetSessionForRecovery(sessionId);
        if (latest?.WorkspaceSnapshot == null)
        {
            return;
        }

        if (HasAppliedRecoveryConflict(latest, runId, runType, status))
        {
            LogRecoveryConflict(runId, sessionId, runType, status, "already_applied");
            return;
        }

        var update = new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = latest.WorkspaceSnapshot.Revision,
            ClientMutationId = $"recovery-conflict:{runId}",
            LifecycleState = "recovery_conflict",
            PlanRunId = string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) ? runId : null,
            PlanRunStatus = string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) ? status : null,
            BuildRunId = string.Equals(runType, "build", StringComparison.OrdinalIgnoreCase) ? runId : null,
            BuildRunStatus = string.Equals(runType, "build", StringComparison.OrdinalIgnoreCase) ? status : null
        };
        var result = TryUpdateOwnedRecovery(sessionId, update);
        if (result.Success)
        {
            ThrowIfPrimaryStoreFailed(result, "recovery conflict", runId, sessionId);
            LogRecoveryConflict(runId, sessionId, runType, status, "workspace_conflict");
            return;
        }

        ThrowIfPrimaryStoreFailed(result, "recovery conflict", runId, sessionId);
        var reread = _conversationService.GetSessionForRecovery(sessionId);
        if (HasAppliedRecoveryConflict(reread, runId, runType, status))
        {
            LogRecoveryConflict(runId, sessionId, runType, status, "already_applied");
            return;
        }

        throw BuildRecoveryPersistenceException(
            "recovery conflict",
            runId,
            sessionId,
            result.ErrorCode,
            result.PublicMessage);
    }

    private void MarkHostInterruptedWorkspace(
        string sessionId,
        string runId,
        string runType,
        VisionAgentWorkspaceSnapshot? workspace)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || workspace == null)
        {
            return;
        }

        if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(workspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) &&
            !IsTerminalStatus(workspace.PlanRunStatus))
        {
            var latest = _conversationService.GetSessionForRecovery(sessionId);
            var latestWorkspace = latest?.WorkspaceSnapshot;
            if (latestWorkspace == null ||
                !string.Equals(latestWorkspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) ||
                IsTerminalStatus(latestWorkspace.PlanRunStatus))
            {
                return;
            }

            var result = TryUpdateOwnedRecovery(sessionId, new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = latestWorkspace.Revision,
                ClientMutationId = $"plan-host-interrupted:{runId}",
                LifecycleState = "plan_failed",
                PlanRunId = runId,
                PlanRunStatus = AgentRunEventStatuses.Failed
            });
            EnsureHostInterruptedMutation(result, sessionId, runId, "plan");
        }

        if (string.Equals(runType, "build", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(workspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) &&
            !IsTerminalStatus(workspace.BuildRunStatus))
        {
            var latest = _conversationService.GetSessionForRecovery(sessionId);
            var latestWorkspace = latest?.WorkspaceSnapshot;
            if (latestWorkspace == null ||
                !string.Equals(latestWorkspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) ||
                IsTerminalStatus(latestWorkspace.BuildRunStatus))
            {
                return;
            }

            var result = TryUpdateOwnedRecovery(sessionId, new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = latestWorkspace.Revision,
                ClientMutationId = $"build-host-interrupted:{runId}",
                LifecycleState = "build_failed",
                BuildRunId = runId,
                BuildRunStatus = AgentRunEventStatuses.Failed
            });
            EnsureHostInterruptedMutation(result, sessionId, runId, "build");
        }
    }

    private void EnsureHostInterruptedMutation(
        VisionAgentWorkspaceSnapshotMutationResult result,
        string sessionId,
        string runId,
        string runType)
    {
        if (result.Success)
        {
            ThrowIfPrimaryStoreFailed(result, $"{runType} host interrupted recovery", runId, sessionId);
            return;
        }

        ThrowIfPrimaryStoreFailed(result, $"{runType} host interrupted recovery", runId, sessionId);
        var latest = _conversationService.GetSessionForRecovery(sessionId)?.WorkspaceSnapshot;
        if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) &&
            latest != null &&
            string.Equals(latest.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(latest.PlanRunStatus, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(runType, "build", StringComparison.OrdinalIgnoreCase) &&
            latest != null &&
            string.Equals(latest.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(latest.BuildRunStatus, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw BuildRecoveryPersistenceException(
            $"{runType} host interrupted recovery",
            runId,
            sessionId,
            result.ErrorCode,
            result.PublicMessage);
    }

    private void EnsureRunTerminalCommitted(string runId, string status, AgentRunEvent? terminal)
    {
        if (terminal != null)
        {
            return;
        }

        var replay = _streamService.ReplayRaw(runId);
        if (replay?.Events.Any(evt =>
                IsRunTerminalEvent(evt) &&
                string.Equals(NormalizeTerminalStatus(evt.Status), NormalizeTerminalStatus(status), StringComparison.OrdinalIgnoreCase)) == true)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Vision Agent startup recovery could not append terminal event. RunId={runId}, Status={status}");
    }

    private static void ThrowIfPrimaryStoreFailed(
        VisionAgentWorkspaceSnapshotMutationResult result,
        string operation,
        string runId,
        string sessionId)
    {
        if (result.PersistenceStatus.PrimaryStoreSaved)
        {
            return;
        }

        throw BuildRecoveryPersistenceException(
            operation,
            runId,
            sessionId,
            result.ErrorCode,
            result.PublicMessage);
    }

    private static InvalidOperationException BuildRecoveryPersistenceException(
        string operation,
        string runId,
        string sessionId,
        string errorCode,
        string publicMessage)
    {
        var code = string.IsNullOrWhiteSpace(errorCode)
            ? "unknown"
            : errorCode.Trim();
        var message = string.IsNullOrWhiteSpace(publicMessage)
            ? $"Vision Agent startup recovery failed to persist {operation}. RunId={runId}, SessionId={sessionId}, ErrorCode={code}"
            : publicMessage;
        return new InvalidOperationException(message);
    }

    private void LogRecoveryConflict(
        string runId,
        string sessionId,
        string runType,
        string status,
        string conflictCode)
    {
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
            _logger,
            "Vision Agent startup recovery conflict. RunId={RunId}, SessionId={SessionId}, RunType={RunType}, Status={Status}, ConflictCode={ConflictCode}",
            runId,
            sessionId,
            runType,
            status,
            conflictCode);
    }

    private void LogMissingRecoverySession(string runId, string sessionId)
    {
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
            _logger,
            "Skipped AgentRun workspace recovery because the conversation session no longer exists. RunId={RunId}, SessionId={SessionId}",
            runId,
            sessionId);
    }

    private static bool HasAppliedRecoveryConflict(
        ConversationSession? session,
        string runId,
        string runType,
        string status)
    {
        if (session?.WorkspaceSnapshot == null)
        {
            return false;
        }

        var workspace = session.WorkspaceSnapshot;
        var receiptApplied = session.MutationReceipts.Any(receipt =>
            string.Equals(receipt.MutationId, $"recovery-conflict:{runId}", StringComparison.OrdinalIgnoreCase));
        if (!receiptApplied ||
            !string.Equals(workspace.LifecycleState, "recovery_conflict", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(workspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(workspace.PlanRunStatus, status, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(workspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(workspace.BuildRunStatus, status, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasWorkspaceConflict(
        string runId,
        string runType,
        VisionAgentWorkspaceSnapshot? workspace,
        string terminalStatus)
    {
        if (workspace == null || string.IsNullOrWhiteSpace(terminalStatus))
        {
            return false;
        }

        if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(workspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) &&
            IsTerminalStatus(workspace.PlanRunStatus))
        {
            return !string.Equals(workspace.PlanRunStatus, terminalStatus, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(runType, "build", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(workspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) &&
            IsTerminalStatus(workspace.BuildRunStatus))
        {
            return !string.Equals(workspace.BuildRunStatus, terminalStatus, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool HasMatchingWorkspaceTerminal(
        string runId,
        string runType,
        VisionAgentWorkspaceSnapshot? workspace,
        string terminalStatus)
    {
        if (workspace == null)
        {
            return false;
        }

        if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(workspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(workspace.PlanRunStatus, terminalStatus, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(workspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(workspace.BuildRunStatus, terminalStatus, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveSessionIdFromWorkspace(string runId, string runType, string ownerHash)
    {
        foreach (var summary in _conversationService.ListSessionsForRecovery())
        {
            var session = _conversationService.GetSessionForRecovery(summary.SessionId);
            if (session == null ||
                !string.Equals(session.OwnerHash, ownerHash, StringComparison.Ordinal))
            {
                continue;
            }

            var workspace = session?.WorkspaceSnapshot;
            if (workspace == null)
            {
                continue;
            }

            if (string.Equals(runType, "plan", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(workspace.PlanRunId, runId, StringComparison.OrdinalIgnoreCase))
            {
                return summary.SessionId;
            }

            if (string.Equals(runType, "build", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(workspace.BuildRunId, runId, StringComparison.OrdinalIgnoreCase))
            {
                return summary.SessionId;
            }
        }

        return string.Empty;
    }

    private static string ResolveSessionIdFromEvents(AgentRunReplayResult replay)
    {
        foreach (var evt in replay.Events.OrderByDescending(evt => evt.Sequence))
        {
            var source = ToJsonElement(evt.Payload);
            var sessionId = TryReadString(source, "sessionId");
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                return sessionId;
            }

            if (TryGetProperty(source, "diagnostic", out var diagnostic))
            {
                sessionId = TryReadString(diagnostic, "sessionId");
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    return sessionId;
                }
            }
        }

        return string.Empty;
    }

    private static AgentRunEvent? FindPlanEvidenceEvent(AgentRunReplayResult replay, string? status)
    {
        var eventType = NormalizeTerminalStatus(status) switch
        {
            AgentRunEventStatuses.Completed => AgentRunEventTypes.PlanCompleted,
            AgentRunEventStatuses.Cancelled => AgentRunEventTypes.PlanCancelled,
            AgentRunEventStatuses.Failed => AgentRunEventTypes.PlanFailed,
            _ => string.Empty
        };

        return replay.Events
            .OrderBy(evt => evt.Sequence)
            .LastOrDefault(evt => string.Equals(evt.EventType, eventType, StringComparison.OrdinalIgnoreCase));
    }

    private static VisionAgentPlanModeResult? TryReadPlanResult(AgentRunEvent planEvent)
    {
        var payload = ToJsonElement(planEvent.Payload);
        if (payload == null)
        {
            return null;
        }

        if (TryGetProperty(payload.Value, "planResult", out var planResult))
        {
            return TryDeserialize<VisionAgentPlanModeResult>(planResult);
        }

        if (TryGetProperty(payload.Value, "planModeResult", out var planModeResult))
        {
            return TryDeserialize<VisionAgentPlanModeResult>(planModeResult);
        }

        return null;
    }

    private static object BuildRecoveredPlanPayload(
        string status,
        string sessionId,
        string runId,
        VisionAgentWorkspaceSnapshot? workspace,
        string recoveryCode)
    {
        return new
        {
            status = status switch
            {
                AgentRunEventStatuses.Cancelled => "plan_cancelled",
                AgentRunEventStatuses.Failed => "plan_failed",
                _ => "plan_completed"
            },
            generationMode = "plan",
            sessionId,
            planRunId = runId,
            workspaceSnapshot = workspace,
            recoveryCode,
            metadataOnly = true
        };
    }

    private static string BuildPlanTerminalMutationId(string runId, string status)
    {
        var normalizedStatus = NormalizeTerminalStatus(status);
        return $"plan-terminal:{runId}:{(string.IsNullOrWhiteSpace(normalizedStatus) ? AgentRunEventStatuses.Failed : normalizedStatus)}";
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

    private static T? TryDeserialize<T>(JsonElement element)
    {
        try
        {
            return element.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            });
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

    private static bool TryGetProperty(JsonElement? source, string propertyName, out JsonElement property)
    {
        if (source.HasValue)
        {
            return TryGetProperty(source.Value, propertyName, out property);
        }

        property = default;
        return false;
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

    private static bool IsRunTerminalEvent(AgentRunEvent evt)
    {
        return string.Equals(evt.EventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalStatus(string? status)
    {
        return string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTerminalStatus(string? status)
    {
        if (string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Completed;
        }

        if (string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Cancelled;
        }

        if (string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Failed;
        }

        return string.Empty;
    }

    private VisionAgentWorkspaceSnapshotMutationResult TryUpdateOwnedRecovery(
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        var session = _conversationService.GetSessionForRecovery(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.OwnerHash))
        {
            return new VisionAgentWorkspaceSnapshotMutationResult
            {
                Success = false,
                ErrorCode = "session_not_found",
                PublicMessage = "Conversation session was not found."
            };
        }

        return _conversationService.TryUpdateWorkspaceSnapshotForRecovery(
            session.OwnerHash,
            session.SessionId,
            update);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
