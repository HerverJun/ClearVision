// ConversationalFlowService.cs
// 会话式流程服务
// 提供多轮对话下的流程生成、上下文维护与意图识别
// 作者：蘅芜君
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Security;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI;

public enum ConversationIntent
{
    New,
    Modify,
    Explain
}

public sealed class ConversationTurn
{
    public string TurnId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public ConversationTurnPayload? Payload { get; set; }
}

public sealed class ConversationTurnPayload
{
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string InteractionState { get; set; } = string.Empty;
    public string TurnIntent { get; set; } = string.Empty;
    public string RouterConfidence { get; set; } = string.Empty;
    public List<string> BlockingClarificationFields { get; set; } = new();
    public List<string> NonBlockingMissingFields { get; set; } = new();
    public int ClarificationRound { get; set; }
    public List<string> AskedQuestionFingerprints { get; set; } = new();
    public List<string> AnsweredClarificationFields { get; set; } = new();
    public string? Reply { get; set; }
    public string? Reasoning { get; set; }
    public List<string> Progress { get; set; } = new();
    public ConversationTurnFailurePayload? Failure { get; set; }
    public AiManualRetryInfo? ManualRetry { get; set; }
    public bool ClarificationRequired { get; set; }
    public AiRequirementBrief? RequirementBrief { get; set; }
    public object? BuildResult { get; set; }
    public object? WorkflowDiff { get; set; }
    public object? ApplyGate { get; set; }
    public object? ToolEvidenceTimeline { get; set; }
    public string? FirstFixRecommendation { get; set; }
}

public sealed class ConversationTurnFailurePayload
{
    public string Summary { get; set; } = string.Empty;
    public AiFailureSummary? FailureSummary { get; set; }
    public List<AiAttemptDiagnostic> Diagnostics { get; set; } = new();
}

public sealed class ConversationSession
{
    public string SessionId { get; set; } = string.Empty;
    public string OwnerHash { get; set; } = string.Empty;
    public string? CurrentFlowJson { get; set; }
    public string? CurrentCanvasFlowJson { get; set; }
    public VisionAgentWorkspaceSnapshot? WorkspaceSnapshot { get; set; }
    public List<ConversationTurn> History { get; set; } = new();
    public List<ConversationMutationReceipt> MutationReceipts { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ConversationMutationReceipt
{
    public string MutationId { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;
    public long AppliedRevision { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class VisionAgentWorkspaceSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public string? ProjectId { get; set; }
    public string LifecycleState { get; set; } = "idle";
    public VisionAgentPlanModeResult? PendingPlanSnapshot { get; set; }
    public Dictionary<string, string> PlanQuestionSelections { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<VisionAgentPlanAnswer> ConfirmedPlanAnswers { get; set; } = new();
    public List<VisionAgentPlanAnswer> OptimisticPlanAnswers { get; set; } = new();
    public int AnswerRevision { get; set; }
    public VisionAgentBuildReadinessPreviewResult? ReadinessPreview { get; set; }
    public List<VisionAgentResourceRequirement> MissingResources { get; set; } = new();
    public Dictionary<string, JsonElement> ResourceDecisions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int ResourceRevision { get; set; }
    public string RequirementMode { get; set; } = AiRequirementModes.Strict;
    public string WorkspaceViewMode { get; set; } = "plan";
    public bool PlanAcceptedRecommendedDefaults { get; set; }
    public string? PlanRunId { get; set; }
    public string? PlanRunStatus { get; set; }
    public long? PlanTerminalSequence { get; set; }
    public string? BuildRunId { get; set; }
    public string? BuildRunStatus { get; set; }
    public long? BuildTerminalSequence { get; set; }
    public string? SubmittedBuildFingerprint { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class VisionAgentWorkspaceSnapshotUpdate
{
    public long? ExpectedRevision { get; set; }
    public string? ClientMutationId { get; set; }
    public string? ProjectId { get; set; }
    public string? LifecycleState { get; set; }
    public VisionAgentPlanModeResult? PendingPlanSnapshot { get; set; }
    public Dictionary<string, string>? PlanQuestionSelections { get; set; }
    public List<VisionAgentPlanAnswer>? ConfirmedPlanAnswers { get; set; }
    public List<VisionAgentPlanAnswer>? OptimisticPlanAnswers { get; set; }
    public int? AnswerRevision { get; set; }
    public VisionAgentBuildReadinessPreviewResult? ReadinessPreview { get; set; }
    public List<VisionAgentResourceRequirement>? MissingResources { get; set; }
    public Dictionary<string, JsonElement>? ResourceDecisions { get; set; }
    public int? ResourceRevision { get; set; }
    public string? RequirementMode { get; set; }
    public string? WorkspaceViewMode { get; set; }
    public bool? PlanAcceptedRecommendedDefaults { get; set; }
    public string? PlanRunId { get; set; }
    public string? PlanRunStatus { get; set; }
    public long? PlanTerminalSequence { get; set; }
    public string? BuildRunId { get; set; }
    public string? BuildRunStatus { get; set; }
    public long? BuildTerminalSequence { get; set; }
    public string? SubmittedBuildFingerprint { get; set; }
    public string? UserTurnId { get; set; }
    public string? UserMessage { get; set; }
    public bool RequireExpectedRevisionWhenWorkspaceExists { get; set; }
}

public sealed class ConversationPersistenceStatus
{
    public bool PrimaryStoreSaved { get; set; } = true;
    public bool RecoveryBackupSaved { get; set; } = true;
    public string ErrorCode { get; set; } = string.Empty;
    public string PublicMessage { get; set; } = string.Empty;
}

public sealed class VisionAgentWorkspaceSnapshotMutationResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string PublicMessage { get; set; } = string.Empty;
    public VisionAgentWorkspaceSnapshot? Snapshot { get; set; }
    public long Revision => Snapshot?.Revision ?? 0;
    public long AppliedRevision { get; set; }
    public ConversationPersistenceStatus PersistenceStatus { get; set; } = new();
    public bool IdempotentReplay { get; set; }
}

public sealed class ConversationSessionAccessException : Exception
{
    public ConversationSessionAccessException()
        : base("Conversation session was not found.")
    {
    }
}

public enum ConversationSessionDeleteStatus
{
    Deleted,
    NotFound,
    PersistenceFailed
}

public enum ConversationBackfillStatus
{
    Applied,
    AlreadyPresent,
    NotFound,
    PersistenceFailed
}

public sealed class ConversationSessionWriteResult
{
    public bool Success { get; set; }
    public ConversationSession? Session { get; set; }
    public ConversationPersistenceStatus PersistenceStatus { get; set; } = new();
}

public sealed class ConversationSessionDeleteResult
{
    public ConversationSessionDeleteStatus Status { get; set; }
    public ConversationPersistenceStatus PersistenceStatus { get; set; } = new();
}

public sealed class ConversationBackfillResult
{
    public ConversationBackfillStatus Status { get; set; }
    public ConversationPersistenceStatus PersistenceStatus { get; set; } = new();
}

public sealed class VisionAgentTerminalProjectionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string AssistantTurnId { get; set; } = string.Empty;
    public string AssistantMessage { get; set; } = string.Empty;
    public string? LatestFlowJson { get; set; }
    public string? LatestCanvasFlowJson { get; set; }
    public ConversationTurnPayload? Payload { get; set; }
    public VisionAgentWorkspaceSnapshotUpdate WorkspaceUpdate { get; set; } = new();
}

public sealed class ConversationSessionSummary
{
    public string SessionId { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public int TurnCount { get; set; }
}

public sealed class ConversationContext
{
    public required string SessionId { get; init; }
    public required ConversationIntent Intent { get; init; }
    public required GenerateFlowMode Mode { get; init; }
    public string? ExistingFlowJson { get; init; }
    public string SessionSummary { get; init; } = string.Empty;
    public string PromptContext { get; init; } = string.Empty;
}

internal static class ConversationOwnerAuthority
{
    public static string Require(string? ownerHash)
    {
        if (string.IsNullOrWhiteSpace(ownerHash))
        {
            throw new ArgumentException(
                "Authenticated owner authority is required.",
                nameof(ownerHash));
        }

        return ownerHash.Trim();
    }
}

public interface IConversationalFlowService
{
    ConversationSession GetOrCreateSession(string ownerHash, string? sessionId);
    ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow);
    ConversationContext PrepareContext(string ownerHash, AiFlowGenerationRequest request);
    ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
        string ownerHash,
        string sessionId,
        string assistantMessage,
        string? latestFlowJson,
        string? latestCanvasFlowJson = null,
        ConversationTurnPayload? payload = null);
    IReadOnlyList<ConversationSessionSummary> ListSessions(string ownerHash);
    ConversationSession? GetSession(string ownerHash, string sessionId);
    IReadOnlyList<ConversationSessionSummary> ListSessionsForRecovery();
    ConversationSession? GetSessionForRecovery(string sessionId);
    bool TryBackfillCanvasFlowJson(string ownerHash, string sessionId, string canvasFlowJson);
    ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(
        string ownerHash,
        string sessionId,
        string canvasFlowJson);
    VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
        string ownerHash,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update);
    VisionAgentWorkspaceSnapshotMutationResult TryInitializeWorkspaceSnapshot(
        string ownerHash,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update);
    VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshotForRecovery(
        string ownerHash,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update);
    VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
        string ownerHash,
        string sessionId,
        string runId,
        string kind,
        string? clientMutationId = null);
    VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(
        string ownerHash,
        VisionAgentTerminalProjectionRequest request);
    ConversationPersistenceStatus GetLastPersistenceStatus();
    ConversationSessionDeleteResult DeleteSessionWithResult(string ownerHash, string sessionId);
}

internal sealed class ConversationStore
{
    public int SchemaVersion { get; set; }
    public List<ConversationSession> Sessions { get; set; } = new();
}

public class ConversationalFlowService : IConversationalFlowService
{
    public const int CurrentStoreSchemaVersion = 2;
    private const int MaxHistory = 20;
    private const int MaxPromptHistory = 5;
    private const int MaxPersistedSessions = 200;
    private const int MaxMutationReceipts = 32;
    private const int MaxLastMessagePreviewLength = 80;
    private const int MaxPromptTurnLength = 220;
    private static readonly TimeSpan SessionRetention = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] _modifyKeywords =
    [
        "改", "修改", "调整", "优化", "调优", "增加", "新增", "补充", "删除", "删掉", "替换", "调大", "调小",
        "change", "update", "adjust", "add", "remove", "replace", "refine"
    ];

    private static readonly string[] _explainKeywords =
    [
        "解释", "为什么", "什么意思", "含义", "讲解", "说明", "原理", "思路",
        "explain", "why", "reason", "meaning"
    ];

    private static readonly string[] _newKeywords =
    [
        "新建", "重新", "从头", "重做", "另一个", "新的流程", "新流程",
        "new flow", "start over", "from scratch", "rebuild"
    ];

    private readonly ConcurrentDictionary<string, ConversationSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _persistLock = new();
    private readonly string _storagePath;
    private readonly string _lastGoodStoragePath;
    private readonly Microsoft.Extensions.Logging.ILogger<ConversationalFlowService>? _logger;
    private ConversationPersistenceStatus _lastPersistenceStatus = new();

    internal Action? PrimaryStoreWriteFaultInjector { get; set; }
    internal Action? RecoveryBackupWriteFaultInjector { get; set; }

    public ConversationalFlowService(
        string? storageRootPath = null,
        Microsoft.Extensions.Logging.ILogger<ConversationalFlowService>? logger = null)
    {
        _logger = logger;
        var rootPath = ResolveStorageRootPath(storageRootPath);

        Directory.CreateDirectory(rootPath);
        _storagePath = ResolveStoragePath(storageRootPath);
        _lastGoodStoragePath = _storagePath + ".last-good";
        LoadSessionsFromStore();
    }

    public static string ResolveStorageRootPath(string? storageRootPath = null) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(storageRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearVision")
            : storageRootPath.Trim());

    public static string ResolveStoragePath(string? storageRootPath = null) =>
        Path.Combine(ResolveStorageRootPath(storageRootPath), "conversation_sessions.json");

    public ConversationSession GetOrCreateSession(string ownerHash, string? sessionId)
    {
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId.Trim();

        if (_sessions.TryGetValue(normalizedSessionId, out var existing))
        {
            if (!IsOwnedBy(existing, normalizedOwnerHash))
            {
                throw new ConversationSessionAccessException();
            }

            return CloneSession(existing);
        }

        return new ConversationSession
        {
            SessionId = normalizedSessionId,
            OwnerHash = normalizedOwnerHash,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow)
    {
        var content = userDescription ?? string.Empty;

        if (!hasExistingFlow)
            return ConversationIntent.New;

        if (ContainsAny(content, _newKeywords))
            return ConversationIntent.New;

        if (ContainsAny(content, _explainKeywords))
            return ConversationIntent.Explain;

        if (ContainsAny(content, _modifyKeywords))
            return ConversationIntent.Modify;

        // 有上下文但未出现明确动词时，按增量修改处理。
        return ConversationIntent.Modify;
    }

    public ConversationContext PrepareContext(string ownerHash, AiFlowGenerationRequest request)
    {
        ConversationIntent intent = ConversationIntent.New;
        GenerateFlowMode resolvedMode = GenerateFlowMode.New;
        string? existingFlowJson = null;
        string sessionSummary = string.Empty;
        var normalizedSessionId = NormalizeSessionId(request.SessionId);
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);

        var commit = CommitSessionStateMutation(normalizedOwnerHash, normalizedSessionId, allowCreate: true, session =>
        {
            if (HasMeaningfulFlow(request.ExistingFlowJson))
            {
                session.CurrentFlowJson = request.ExistingFlowJson;
                if (IsCanvasFlowJson(request.ExistingFlowJson))
                {
                    session.CurrentCanvasFlowJson = request.ExistingFlowJson;
                }
            }
            else if (request.ExistingFlowJson != null)
            {
                session.CurrentFlowJson = null;
                session.CurrentCanvasFlowJson = null;
            }

            session.History.Add(new ConversationTurn
            {
                TurnId = Guid.NewGuid().ToString("N"),
                Role = "user",
                Message = request.Description,
                TimestampUtc = DateTime.UtcNow
            });

            TrimHistory(session);
            session.UpdatedAtUtc = DateTime.UtcNow;

            var hasExistingFlow = HasMeaningfulFlow(session.CurrentFlowJson);
            resolvedMode = ResolveMode(request.Mode, request.Description, hasExistingFlow);
            intent = ToIntent(resolvedMode, request.Description, hasExistingFlow);
            if (request.Mode == GenerateFlowMode.Auto &&
                (intent == ConversationIntent.Modify || intent == ConversationIntent.Explain) &&
                !hasExistingFlow)
            {
                intent = ConversationIntent.New;
                resolvedMode = GenerateFlowMode.New;
            }

            existingFlowJson = HasMeaningfulFlow(session.CurrentFlowJson) ? session.CurrentFlowJson : null;
            sessionSummary = BuildPromptSessionSummary(session);
        });

        if (!commit.Success || commit.Session == null)
        {
            if (string.Equals(
                    commit.PersistenceStatus.ErrorCode,
                    "session_not_found",
                    StringComparison.Ordinal))
            {
                throw new ConversationSessionAccessException();
            }

            throw new IOException(commit.PersistenceStatus.PublicMessage);
        }

        return new ConversationContext
        {
            SessionId = commit.Session.SessionId,
            Intent = intent,
            Mode = resolvedMode,
            ExistingFlowJson = existingFlowJson,
            SessionSummary = sessionSummary,
            PromptContext = BuildLegacyPromptContext(intent, sessionSummary)
        };
    }

    private static GenerateFlowMode ResolveMode(
        GenerateFlowMode requestedMode,
        string userDescription,
        bool hasExistingFlow)
    {
        if (requestedMode != GenerateFlowMode.Auto)
            return requestedMode;

        return DetectIntentStatic(userDescription, hasExistingFlow) switch
        {
            ConversationIntent.Explain => GenerateFlowMode.Explain,
            ConversationIntent.Modify => GenerateFlowMode.Modify,
            _ => GenerateFlowMode.New
        };
    }

    private static ConversationIntent ToIntent(
        GenerateFlowMode mode,
        string userDescription,
        bool hasExistingFlow)
    {
        return mode switch
        {
            GenerateFlowMode.New => ConversationIntent.New,
            GenerateFlowMode.Explain => ConversationIntent.Explain,
            GenerateFlowMode.Modify => ConversationIntent.Modify,
            GenerateFlowMode.ReviewPendingParameters => ConversationIntent.Modify,
            _ => DetectIntentStatic(userDescription, hasExistingFlow)
        };
    }

    private static ConversationIntent DetectIntentStatic(string userDescription, bool hasExistingFlow)
    {
        var content = userDescription ?? string.Empty;

        if (!hasExistingFlow)
            return ConversationIntent.New;

        if (ContainsAny(content, _newKeywords))
            return ConversationIntent.New;

        if (ContainsAny(content, _explainKeywords))
            return ConversationIntent.Explain;

        if (ContainsAny(content, _modifyKeywords))
            return ConversationIntent.Modify;

        return ConversationIntent.Modify;
    }

    private static string BuildPromptSessionSummary(ConversationSession session)
    {
        var sb = new StringBuilder();
        var historyToInject = session.History
            .OrderByDescending(turn => turn.TimestampUtc)
            .Take(MaxPromptHistory)
            .OrderBy(turn => turn.TimestampUtc)
            .ToList();

        foreach (var turn in historyToInject)
        {
            var sanitizedMessage = SanitizePromptTurn(turn.Message);
            if (string.IsNullOrWhiteSpace(sanitizedMessage))
                continue;

            sb.AppendLine($"- {turn.Role}: {sanitizedMessage}");
        }

        return sb.ToString().Trim();
    }

    private static string SanitizePromptTurn(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var trimmed = message.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
            return "[workflow json omitted]";

        var fenceIndex = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceIndex >= 0)
        {
            trimmed = trimmed[..fenceIndex].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return "[structured content omitted]";
        }

        trimmed = trimmed
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        while (trimmed.Contains("  ", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (trimmed.Length > MaxPromptTurnLength)
            trimmed = trimmed[..MaxPromptTurnLength] + "...";

        return trimmed;
    }

    private static string BuildLegacyPromptContext(ConversationIntent intent, string sessionSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"会话意图：{ToIntentLabel(intent)}");
        if (!string.IsNullOrWhiteSpace(sessionSummary))
        {
            sb.AppendLine();
            sb.AppendLine(sessionSummary);
        }

        return sb.ToString().Trim();
    }

    public ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
        string ownerHash,
        string sessionId,
        string assistantMessage,
        string? latestFlowJson,
        string? latestCanvasFlowJson = null,
        ConversationTurnPayload? payload = null) =>
        CommitSessionStateMutation(ownerHash, sessionId, allowCreate: false, session =>
        {
            if (!string.IsNullOrWhiteSpace(latestFlowJson))
                session.CurrentFlowJson = latestFlowJson;

            if (!string.IsNullOrWhiteSpace(latestCanvasFlowJson))
                session.CurrentCanvasFlowJson = latestCanvasFlowJson;
            else if (IsCanvasFlowJson(latestFlowJson))
                session.CurrentCanvasFlowJson = latestFlowJson;

            session.History.Add(new ConversationTurn
            {
                TurnId = Guid.NewGuid().ToString("N"),
                Role = "assistant",
                Message = assistantMessage,
                TimestampUtc = DateTime.UtcNow,
                Payload = CloneTurnPayload(payload)
            });

            TrimHistory(session);
            session.UpdatedAtUtc = DateTime.UtcNow;
        });

    public IReadOnlyList<ConversationSessionSummary> ListSessions(string ownerHash)
    {
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        return _sessions.Values
            .Where(session => IsOwnedBy(session, normalizedOwnerHash))
            .Select(BuildSessionSummary)
            .OrderByDescending(summary => summary.UpdatedAtUtc)
            .ToList();
    }

    public IReadOnlyList<ConversationSessionSummary> ListSessionsForRecovery()
    {
        return _sessions.Values
            .Where(HasValidOwnerAssociation)
            .Select(BuildSessionSummary)
            .OrderByDescending(summary => summary.UpdatedAtUtc)
            .ToList();
    }

    public ConversationSession? GetSession(string ownerHash, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        var normalizedSessionId = sessionId.Trim();
        if (!_sessions.TryGetValue(normalizedSessionId, out var session))
            return null;

        return IsOwnedBy(session, normalizedOwnerHash) ? CloneSession(session) : null;
    }

    public ConversationSession? GetSessionForRecovery(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            !_sessions.TryGetValue(sessionId.Trim(), out var session) ||
            !HasValidOwnerAssociation(session))
        {
            return null;
        }

        return CloneSession(session);
    }

    public bool TryBackfillCanvasFlowJson(
        string ownerHash,
        string sessionId,
        string canvasFlowJson) =>
        TryBackfillCanvasFlowJsonWithResult(ownerHash, sessionId, canvasFlowJson).Status ==
        ConversationBackfillStatus.Applied;

    public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(
        string ownerHash,
        string sessionId,
        string canvasFlowJson)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(canvasFlowJson))
            return new ConversationBackfillResult { Status = ConversationBackfillStatus.NotFound };

        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        var normalizedSessionId = sessionId.Trim();
        lock (_persistLock)
        {
            if (!_sessions.TryGetValue(normalizedSessionId, out var currentSession) ||
                !IsOwnedBy(currentSession, normalizedOwnerHash))
                return new ConversationBackfillResult { Status = ConversationBackfillStatus.NotFound };

            var current = CloneSession(currentSession);
            if (!string.IsNullOrWhiteSpace(current.CurrentCanvasFlowJson))
                return new ConversationBackfillResult { Status = ConversationBackfillStatus.AlreadyPresent };

            var candidate = CloneSession(current);
            candidate.CurrentCanvasFlowJson = canvasFlowJson;
            candidate.UpdatedAtUtc = DateTime.UtcNow;
            NormalizeSession(candidate);
            var persistence = PersistSessionsSnapshotUnderLock(BuildPersistedSnapshotWithCandidate(candidate));
            if (!persistence.PrimaryStoreSaved)
            {
                return new ConversationBackfillResult
                {
                    Status = ConversationBackfillStatus.PersistenceFailed,
                    PersistenceStatus = ClonePersistenceStatus(persistence)
                };
            }

            _sessions[normalizedSessionId] = candidate;
            return new ConversationBackfillResult
            {
                Status = ConversationBackfillStatus.Applied,
                PersistenceStatus = ClonePersistenceStatus(persistence)
            };
        }
    }

    public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
        string ownerHash,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return CommitSessionMutation(
            ownerHash,
            sessionId,
            update.ExpectedRevision,
            update.ClientMutationId,
            ComputeWorkspaceMutationFingerprint(update),
            candidate => ApplyWorkspaceUpdateLocked(candidate, update),
            update.RequireExpectedRevisionWhenWorkspaceExists,
            allowCreate: false)
            .ToWorkspaceMutationResult();
    }

    public VisionAgentWorkspaceSnapshotMutationResult TryInitializeWorkspaceSnapshot(
        string ownerHash,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return CommitSessionMutation(
            ownerHash,
            sessionId,
            update.ExpectedRevision,
            update.ClientMutationId,
            ComputeWorkspaceMutationFingerprint(update),
            candidate => ApplyWorkspaceUpdateLocked(candidate, update),
            update.RequireExpectedRevisionWhenWorkspaceExists,
            allowCreate: true)
            .ToWorkspaceMutationResult();
    }

    public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshotForRecovery(
        string ownerHash,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update) =>
        TryUpdateWorkspaceSnapshot(ownerHash, sessionId, update);

    public VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
        string ownerHash,
        string sessionId,
        string runId,
        string kind,
        string? clientMutationId = null)
    {
        return TryBeginAgentRunCore(ownerHash, sessionId, runId, kind, clientMutationId, allowCreate: true);
    }

    private VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRunCore(
        string ownerHash,
        string sessionId,
        string runId,
        string kind,
        string? clientMutationId,
        bool allowCreate)
    {
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        var normalizedSessionId = NormalizeSessionId(sessionId);
        var normalizedRunId = runId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRunId))
        {
            return new VisionAgentWorkspaceSnapshotMutationResult
            {
                Success = false,
                ErrorCode = "agent_run_invalid_request",
                PublicMessage = "Agent run id is required.",
                PersistenceStatus = ClonePersistenceStatus(_lastPersistenceStatus)
            };
        }

        var normalizedKind = string.IsNullOrWhiteSpace(kind)
            ? "agent_run"
            : kind.Trim();
        var normalizedMutationId = string.IsNullOrWhiteSpace(clientMutationId)
            ? $"agent-run-begin:{normalizedRunId}"
            : clientMutationId.Trim();
        var payloadFingerprint = ComputeJsonFingerprint(new
        {
            SessionId = normalizedSessionId,
            RunId = normalizedRunId,
            Kind = normalizedKind
        });

        lock (_persistLock)
        {
            if (!TryGetCurrentSessionSnapshot(
                    normalizedOwnerHash,
                    normalizedSessionId,
                    allowCreate,
                    out var current))
            {
                return SessionMutationCommitResult.NotFound(_lastPersistenceStatus)
                    .ToWorkspaceMutationResult();
            }
            NormalizeSession(current);

            var receipt = current.MutationReceipts.FirstOrDefault(item =>
                string.Equals(item.MutationId, normalizedMutationId, StringComparison.OrdinalIgnoreCase));
            if (receipt != null)
            {
                if (!string.Equals(receipt.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
                {
                    return SessionMutationCommitResult.Conflicted(
                            "workspace_mutation_id_conflict",
                            "This agent run begin request conflicts with an already processed mutation id.",
                            current.WorkspaceSnapshot,
                            _lastPersistenceStatus)
                        .ToWorkspaceMutationResult();
                }

                return SessionMutationCommitResult.Succeeded(
                        current,
                        current.WorkspaceSnapshot,
                        _lastPersistenceStatus,
                        idempotentReplay: true,
                        appliedRevision: receipt.AppliedRevision)
                    .ToWorkspaceMutationResult();
            }

            if (IsSameAgentRunInProgress(current.WorkspaceSnapshot, normalizedRunId))
            {
                return SessionMutationCommitResult.Succeeded(
                        current,
                        current.WorkspaceSnapshot,
                        _lastPersistenceStatus,
                        idempotentReplay: true)
                    .ToWorkspaceMutationResult();
            }

            if (HasRunningAgentRun(current.WorkspaceSnapshot))
            {
                return SessionMutationCommitResult.Conflicted(
                        "agent_run_already_running",
                        "The same conversation already has an Agent run in progress.",
                        current.WorkspaceSnapshot,
                        _lastPersistenceStatus)
                    .ToWorkspaceMutationResult();
            }

            var candidate = CloneSession(current);
            ApplyWorkspaceUpdateLocked(candidate, new VisionAgentWorkspaceSnapshotUpdate
            {
                LifecycleState = "building",
                BuildRunId = normalizedRunId,
                BuildRunStatus = AgentRunEventStatuses.Running
            });
            NormalizeSession(candidate);
            AddMutationReceipt(candidate, normalizedMutationId, payloadFingerprint);

            var persistence = PersistSessionsSnapshotUnderLock(BuildPersistedSnapshotWithCandidate(candidate));
            if (!persistence.PrimaryStoreSaved)
            {
                return SessionMutationCommitResult.Failed(
                        current.WorkspaceSnapshot,
                        persistence)
                    .ToWorkspaceMutationResult();
            }

            _sessions[normalizedSessionId] = candidate;
            return SessionMutationCommitResult.Succeeded(
                    candidate,
                    candidate.WorkspaceSnapshot,
                    persistence,
                    idempotentReplay: false)
                .ToWorkspaceMutationResult();
        }
    }

    public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(
        string ownerHash,
        VisionAgentTerminalProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        {
            var committedAssistantTurnId = string.IsNullOrWhiteSpace(request.AssistantTurnId)
                ? Guid.NewGuid().ToString("N")
                : request.AssistantTurnId.Trim();
            var terminalProjectionFingerprint = ComputeTerminalProjectionFingerprint(request, committedAssistantTurnId);
            return CommitSessionMutation(
                ownerHash,
                request.SessionId,
                request.WorkspaceUpdate.ExpectedRevision,
                BuildTerminalProjectionMutationId(committedAssistantTurnId, terminalProjectionFingerprint),
                terminalProjectionFingerprint,
                candidate =>
                {
                    if (!string.IsNullOrWhiteSpace(request.LatestFlowJson))
                        candidate.CurrentFlowJson = request.LatestFlowJson;

                    if (!string.IsNullOrWhiteSpace(request.LatestCanvasFlowJson))
                        candidate.CurrentCanvasFlowJson = request.LatestCanvasFlowJson;
                    else if (IsCanvasFlowJson(request.LatestFlowJson))
                        candidate.CurrentCanvasFlowJson = request.LatestFlowJson;

                    if (!candidate.History.Any(turn => string.Equals(turn.TurnId, committedAssistantTurnId, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidate.History.Add(new ConversationTurn
                        {
                            TurnId = committedAssistantTurnId,
                            Role = "assistant",
                            Message = request.AssistantMessage,
                            TimestampUtc = DateTime.UtcNow,
                            Payload = CloneTurnPayload(request.Payload)
                        });
                        TrimHistory(candidate);
                    }

                    ApplyWorkspaceUpdateLocked(candidate, request.WorkspaceUpdate);
                },
                allowCreate: false)
                .ToWorkspaceMutationResult();
        }
    }

    public ConversationPersistenceStatus GetLastPersistenceStatus() =>
        ClonePersistenceStatus(_lastPersistenceStatus);

    private ConversationSessionWriteResult CommitSessionStateMutation(
        string ownerHash,
        string sessionId,
        bool allowCreate,
        Action<ConversationSession> mutator)
    {
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        var normalizedSessionId = NormalizeSessionId(sessionId);
        lock (_persistLock)
        {
            if (!TryGetCurrentSessionSnapshot(
                    normalizedOwnerHash,
                    normalizedSessionId,
                    allowCreate,
                    out var current))
            {
                return new ConversationSessionWriteResult
                {
                    Success = false,
                    Session = null,
                    PersistenceStatus = NotFoundPersistenceStatus()
                };
            }
            NormalizeSession(current);
            var candidate = CloneSession(current);
            mutator(candidate);
            NormalizeSession(candidate);

            var persistence = PersistSessionsSnapshotUnderLock(BuildPersistedSnapshotWithCandidate(candidate));
            if (!persistence.PrimaryStoreSaved)
            {
                return new ConversationSessionWriteResult
                {
                    Success = false,
                    Session = CloneSession(current),
                    PersistenceStatus = ClonePersistenceStatus(persistence)
                };
            }

            _sessions[normalizedSessionId] = candidate;
            return new ConversationSessionWriteResult
            {
                Success = true,
                Session = CloneSession(candidate),
                PersistenceStatus = ClonePersistenceStatus(persistence)
            };
        }
    }

    private SessionMutationCommitResult CommitSessionMutation(
        string ownerHash,
        string sessionId,
        long? expectedRevision,
        string? clientMutationId,
        string payloadFingerprint,
        Action<ConversationSession> mutator,
        bool requireExpectedRevisionWhenWorkspaceExists = false,
        bool allowCreate = false)
    {
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        var normalizedSessionId = NormalizeSessionId(sessionId);
        var normalizedMutationId = clientMutationId?.Trim() ?? string.Empty;
        var normalizedFingerprint = string.IsNullOrWhiteSpace(payloadFingerprint)
            ? string.Empty
            : payloadFingerprint.Trim();

        lock (_persistLock)
        {
            if (!TryGetCurrentSessionSnapshot(
                    normalizedOwnerHash,
                    normalizedSessionId,
                    allowCreate,
                    out var current))
            {
                return SessionMutationCommitResult.NotFound(_lastPersistenceStatus);
            }
            NormalizeSession(current);

            if (!string.IsNullOrWhiteSpace(normalizedMutationId))
            {
                var receipt = current.MutationReceipts.FirstOrDefault(item =>
                    string.Equals(item.MutationId, normalizedMutationId, StringComparison.OrdinalIgnoreCase));
                if (receipt != null)
                {
                    if (!string.Equals(receipt.PayloadFingerprint, normalizedFingerprint, StringComparison.Ordinal))
                    {
                        return SessionMutationCommitResult.Conflicted(
                            "workspace_mutation_id_conflict",
                            "本次保存请求与已处理的请求编号冲突，请刷新后重试。",
                            current.WorkspaceSnapshot,
                            _lastPersistenceStatus);
                    }

                    return SessionMutationCommitResult.Succeeded(
                        current,
                        current.WorkspaceSnapshot,
                        _lastPersistenceStatus,
                        idempotentReplay: true,
                        appliedRevision: receipt.AppliedRevision);
                }
            }

            var currentRevision = current.WorkspaceSnapshot?.Revision ?? 0;
            if (requireExpectedRevisionWhenWorkspaceExists &&
                current.WorkspaceSnapshot != null &&
                !expectedRevision.HasValue)
            {
                return SessionMutationCommitResult.Conflicted(
                    "workspace_revision_required",
                    "Plan 状态缺少版本号，请刷新确认后重新构建。",
                    current.WorkspaceSnapshot,
                    _lastPersistenceStatus);
            }

            if (expectedRevision.HasValue && expectedRevision.Value != currentRevision)
            {
                return SessionMutationCommitResult.Conflicted(
                    "workspace_revision_conflict",
                    "工作台状态已更新，请确认最新内容后重试本次修改。",
                    current.WorkspaceSnapshot,
                    _lastPersistenceStatus);
            }

            var candidate = CloneSession(current);
            mutator(candidate);
            NormalizeSession(candidate);
            if (!string.IsNullOrWhiteSpace(normalizedMutationId))
            {
                AddMutationReceipt(candidate, normalizedMutationId, normalizedFingerprint);
            }

            var persistence = PersistSessionsSnapshotUnderLock(BuildPersistedSnapshotWithCandidate(candidate));
            if (!persistence.PrimaryStoreSaved)
            {
                return SessionMutationCommitResult.Failed(
                    current.WorkspaceSnapshot,
                    persistence);
            }

            _sessions[normalizedSessionId] = candidate;
            return SessionMutationCommitResult.Succeeded(
                candidate,
                candidate.WorkspaceSnapshot,
                persistence,
                idempotentReplay: false);
        }
    }

    private sealed class SessionMutationCommitResult
    {
        public bool Success { get; private init; }
        public bool Conflict { get; private init; }
        public string ErrorCode { get; private init; } = string.Empty;
        public string PublicMessage { get; private init; } = string.Empty;
        public ConversationSession? Session { get; private init; }
        public VisionAgentWorkspaceSnapshot? Snapshot { get; private init; }
        public long AppliedRevision { get; private init; }
        public ConversationPersistenceStatus PersistenceStatus { get; private init; } = new();
        public bool IdempotentReplay { get; private init; }

        public static SessionMutationCommitResult Succeeded(
            ConversationSession session,
            VisionAgentWorkspaceSnapshot? snapshot,
            ConversationPersistenceStatus persistenceStatus,
            bool idempotentReplay,
            long? appliedRevision = null) =>
            new()
            {
                Success = true,
                Session = CloneSession(session),
                Snapshot = CloneWorkspaceSnapshot(snapshot),
                AppliedRevision = appliedRevision ?? snapshot?.Revision ?? 0,
                PersistenceStatus = ClonePersistenceStatus(persistenceStatus),
                IdempotentReplay = idempotentReplay
            };

        public static SessionMutationCommitResult Conflicted(
            string errorCode,
            string publicMessage,
            VisionAgentWorkspaceSnapshot? snapshot,
            ConversationPersistenceStatus persistenceStatus) =>
            new()
            {
                Success = false,
                Conflict = true,
                ErrorCode = errorCode,
                PublicMessage = publicMessage,
                Snapshot = CloneWorkspaceSnapshot(snapshot),
                PersistenceStatus = ClonePersistenceStatus(persistenceStatus)
            };

        public static SessionMutationCommitResult Failed(
            VisionAgentWorkspaceSnapshot? snapshot,
            ConversationPersistenceStatus persistenceStatus) =>
            new()
            {
                Success = false,
                ErrorCode = persistenceStatus.ErrorCode,
                PublicMessage = persistenceStatus.PublicMessage,
                Snapshot = CloneWorkspaceSnapshot(snapshot),
                PersistenceStatus = ClonePersistenceStatus(persistenceStatus)
            };

        public static SessionMutationCommitResult NotFound(
            ConversationPersistenceStatus persistenceStatus) =>
            new()
            {
                Success = false,
                Conflict = false,
                ErrorCode = "session_not_found",
                PublicMessage = "Conversation session was not found.",
                PersistenceStatus = ClonePersistenceStatus(persistenceStatus)
            };

        public VisionAgentWorkspaceSnapshotMutationResult ToWorkspaceMutationResult() =>
            new()
            {
                Success = Success,
                Conflict = Conflict,
                ErrorCode = ErrorCode,
                PublicMessage = PublicMessage,
                Snapshot = CloneWorkspaceSnapshot(Snapshot),
                AppliedRevision = AppliedRevision,
                PersistenceStatus = ClonePersistenceStatus(PersistenceStatus),
                IdempotentReplay = IdempotentReplay
            };
    }

    private static string NormalizeSessionId(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId.Trim();

    private static bool HasRunningAgentRun(VisionAgentWorkspaceSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        if (IsActiveRunStatus(snapshot.PlanRunStatus) || IsActiveRunStatus(snapshot.BuildRunStatus))
        {
            return true;
        }

        return IsActiveLifecycleState(snapshot.LifecycleState);
    }

    private static bool IsSameAgentRunInProgress(VisionAgentWorkspaceSnapshot? snapshot, string runId)
    {
        return snapshot != null &&
               !string.IsNullOrWhiteSpace(runId) &&
               string.Equals(snapshot.BuildRunId, runId, StringComparison.OrdinalIgnoreCase) &&
               (IsActiveRunStatus(snapshot.BuildRunStatus) ||
                IsActiveLifecycleState(snapshot.LifecycleState));
    }

    private static bool IsActiveRunStatus(string? status)
    {
        return string.Equals(status, AgentRunEventStatuses.Running, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Pending, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveLifecycleState(string? lifecycleState)
    {
        return string.Equals(lifecycleState, "planning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(lifecycleState, "building", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetCurrentSessionSnapshot(
        string ownerHash,
        string sessionId,
        bool allowCreate,
        out ConversationSession session)
    {
        if (_sessions.TryGetValue(sessionId, out var current))
        {
            if (!IsOwnedBy(current, ownerHash))
            {
                session = null!;
                return false;
            }

            session = CloneSession(current);
            return true;
        }

        if (!allowCreate)
        {
            session = null!;
            return false;
        }

        session = new ConversationSession
        {
            SessionId = sessionId,
            OwnerHash = ownerHash,
            UpdatedAtUtc = DateTime.UtcNow
        };
        return true;
    }

    private static string NormalizeOwnerHash(string? ownerHash)
        => ConversationOwnerAuthority.Require(ownerHash);

    private static bool IsOwnedBy(ConversationSession session, string ownerHash) =>
        HasValidOwnerAssociation(session) &&
        string.Equals(session.OwnerHash, ownerHash, StringComparison.Ordinal);

    private static bool HasValidOwnerAssociation(ConversationSession session) =>
        !string.IsNullOrWhiteSpace(session.OwnerHash);

    private static ConversationPersistenceStatus NotFoundPersistenceStatus() => new()
    {
        PrimaryStoreSaved = true,
        RecoveryBackupSaved = true,
        ErrorCode = "session_not_found",
        PublicMessage = "Conversation session was not found."
    };

    private IReadOnlyCollection<ConversationSession> BuildPersistedSnapshotWithCandidate(ConversationSession candidate)
    {
        var candidateSessionId = candidate.SessionId.Trim();
        var sessions = _sessions.Values
            .Where(session => !string.Equals(session.SessionId, candidateSessionId, StringComparison.OrdinalIgnoreCase))
            .Select(CloneSession)
            .ToList();
        sessions.Add(CloneSession(candidate));
        return sessions;
    }

    private static void AddMutationReceipt(
        ConversationSession session,
        string mutationId,
        string payloadFingerprint)
    {
        session.MutationReceipts ??= new List<ConversationMutationReceipt>();
        session.MutationReceipts.RemoveAll(item =>
            string.Equals(item.MutationId, mutationId, StringComparison.OrdinalIgnoreCase));
        session.MutationReceipts.Add(new ConversationMutationReceipt
        {
            MutationId = mutationId,
            PayloadFingerprint = payloadFingerprint,
            AppliedRevision = session.WorkspaceSnapshot?.Revision ?? 0,
            UpdatedAtUtc = DateTime.UtcNow
        });
        session.MutationReceipts = session.MutationReceipts
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(MaxMutationReceipts)
            .OrderBy(item => item.UpdatedAtUtc)
            .ToList();
    }

    public static string ComputeWorkspaceMutationFingerprint(VisionAgentWorkspaceSnapshotUpdate update)
    {
        var normalizedSelections = update.PlanQuestionSelections?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new { Key = pair.Key.Trim(), Value = pair.Value?.Trim() ?? string.Empty })
            .ToList();
        var hasV2WorkspaceFields = update.OptimisticPlanAnswers != null ||
            update.AnswerRevision.HasValue ||
            update.ReadinessPreview != null ||
            update.MissingResources != null ||
            update.ResourceDecisions != null ||
            update.ResourceRevision.HasValue ||
            update.WorkspaceViewMode != null;

        if (!hasV2WorkspaceFields)
        {
            return ComputeJsonFingerprint(new
            {
                update.ProjectId,
                update.LifecycleState,
                update.PendingPlanSnapshot,
                PlanQuestionSelections = normalizedSelections,
                update.ConfirmedPlanAnswers,
                update.RequirementMode,
                update.PlanAcceptedRecommendedDefaults,
                update.PlanRunId,
                update.PlanRunStatus,
                update.PlanTerminalSequence,
                update.BuildRunId,
                update.BuildRunStatus,
                update.BuildTerminalSequence,
                update.SubmittedBuildFingerprint,
                update.UserTurnId,
                update.UserMessage
            });
        }

        return ComputeJsonFingerprint(new
        {
            SchemaVersion = 2,
            update.ProjectId,
            update.LifecycleState,
            update.PendingPlanSnapshot,
            PlanQuestionSelections = normalizedSelections,
            update.ConfirmedPlanAnswers,
            update.OptimisticPlanAnswers,
            update.AnswerRevision,
            update.ReadinessPreview,
            update.MissingResources,
            ResourceDecisions = update.ResourceDecisions?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { Key = pair.Key.Trim(), Value = pair.Value })
                .ToList(),
            update.ResourceRevision,
            update.RequirementMode,
            update.WorkspaceViewMode,
            update.PlanAcceptedRecommendedDefaults,
            update.PlanRunId,
            update.PlanRunStatus,
            update.PlanTerminalSequence,
            update.BuildRunId,
            update.BuildRunStatus,
            update.BuildTerminalSequence,
            update.SubmittedBuildFingerprint,
            update.UserTurnId,
            update.UserMessage
        });
    }

    public static string ComputeTerminalProjectionFingerprint(
        VisionAgentTerminalProjectionRequest request,
        string assistantTurnId) =>
        ComputeJsonFingerprint(new
        {
            assistantTurnId,
            request.AssistantMessage,
            request.LatestFlowJson,
            request.LatestCanvasFlowJson,
            request.Payload,
            WorkspaceFingerprint = ComputeWorkspaceMutationFingerprint(request.WorkspaceUpdate)
        });

    public static string BuildTerminalProjectionMutationId(string assistantTurnId, string fingerprint) =>
        $"build-terminal:{assistantTurnId}:{BuildMutationIdFingerprintSuffix(fingerprint)}";

    private static string BuildMutationIdFingerprintSuffix(string fingerprint)
    {
        var normalized = string.IsNullOrWhiteSpace(fingerprint)
            ? string.Empty
            : fingerprint.Trim();
        var separator = normalized.IndexOf(':', StringComparison.Ordinal);
        var value = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return value.Length <= 16 ? value : value[..16];
    }

    private static string ComputeJsonFingerprint(object value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public ConversationSessionDeleteResult DeleteSessionWithResult(
        string ownerHash,
        string sessionId)
    {
        var normalizedOwnerHash = NormalizeOwnerHash(ownerHash);
        return DeleteSessionWithResultCore(normalizedOwnerHash, sessionId);
    }

    private ConversationSessionDeleteResult DeleteSessionWithResultCore(
        string ownerHash,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return new ConversationSessionDeleteResult { Status = ConversationSessionDeleteStatus.NotFound };

        var normalizedSessionId = sessionId.Trim();
        lock (_persistLock)
        {
            if (!_sessions.TryGetValue(normalizedSessionId, out var current) ||
                !IsOwnedBy(current, ownerHash))
                return new ConversationSessionDeleteResult { Status = ConversationSessionDeleteStatus.NotFound };

            var snapshot = _sessions.Values
                .Where(session => !string.Equals(session.SessionId, normalizedSessionId, StringComparison.OrdinalIgnoreCase))
                .Select(CloneSession)
                .ToList();
            var persistence = PersistSessionsSnapshotUnderLock(snapshot);
            if (!persistence.PrimaryStoreSaved)
            {
                return new ConversationSessionDeleteResult
                {
                    Status = ConversationSessionDeleteStatus.PersistenceFailed,
                    PersistenceStatus = ClonePersistenceStatus(persistence)
                };
            }

            _sessions.TryRemove(normalizedSessionId, out _);
            return new ConversationSessionDeleteResult
            {
                Status = ConversationSessionDeleteStatus.Deleted,
                PersistenceStatus = ClonePersistenceStatus(persistence)
            };
        }
    }

    private static string BuildPromptContext(ConversationSession session, ConversationIntent intent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"会话意图：{ToIntentLabel(intent)}");

        switch (intent)
        {
            case ConversationIntent.New:
                sb.AppendLine("请创建一个新的完整工作流。");
                break;
            case ConversationIntent.Modify:
                sb.AppendLine("请在当前工作流基础上做增量修改，优先保留未被明确要求修改的节点和连线。");
                break;
            case ConversationIntent.Explain:
                sb.AppendLine("用户希望理解当前工作流，请在不改变算子结构的前提下给出清晰 explanation。");
                break;
        }

        var historyToInject = session.History
            .OrderByDescending(turn => turn.TimestampUtc)
            .Take(MaxPromptHistory)
            .OrderBy(turn => turn.TimestampUtc)
            .ToList();

        if (historyToInject.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("最近对话历史：");
            foreach (var turn in historyToInject)
            {
                sb.AppendLine($"- {turn.Role}: {turn.Message}");
            }
        }

        return sb.ToString();
    }

    private static string ToIntentLabel(ConversationIntent intent) => intent switch
    {
        ConversationIntent.New => "NEW",
        ConversationIntent.Modify => "MODIFY",
        ConversationIntent.Explain => "EXPLAIN",
        _ => "NEW"
    };

    private static bool ContainsAny(string source, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => source.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static void TrimHistory(ConversationSession session)
    {
        if (session.History.Count <= MaxHistory)
            return;

        session.History.RemoveRange(0, session.History.Count - MaxHistory);
    }

    private static ConversationSessionSummary BuildSessionSummary(ConversationSession session)
    {
        lock (session)
        {
            var latestTurn = session.History
                .OrderByDescending(turn => turn.TimestampUtc)
                .FirstOrDefault();
            var latestMessage = latestTurn?.Message ?? string.Empty;
            if (latestMessage.Length > MaxLastMessagePreviewLength)
                latestMessage = latestMessage[..MaxLastMessagePreviewLength] + "...";

            return new ConversationSessionSummary
            {
                SessionId = session.SessionId,
                LastMessage = latestMessage,
                UpdatedAtUtc = session.UpdatedAtUtc,
                TurnCount = session.History.Count
            };
        }
    }

    private void LoadSessionsFromStore()
    {
        if (!File.Exists(_storagePath))
            return;

        try
        {
            var json = File.ReadAllText(_storagePath);
            LoadSessionsFromJson(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (_logger != null)
                LoggerExtensions.LogWarning(_logger, ex, "Failed to load conversation session store. Path={StoragePath}", _storagePath);
            QuarantineCorruptStore(ex);
            TryLoadLastGoodStore();
        }
    }

    private ConversationPersistenceStatus PersistSessions()
    {
        lock (_persistLock)
        {
            PruneInMemorySessions();
            var snapshot = _sessions.Values
                .Select(CloneSession)
                .OrderByDescending(session => session.UpdatedAtUtc)
                .Take(MaxPersistedSessions)
                .ToList();
            return PersistSessionsSnapshotUnderLock(snapshot);
        }
    }

    private ConversationPersistenceStatus PersistSessionsSnapshotUnderLock(IReadOnlyCollection<ConversationSession> sessions)
    {
        var status = new ConversationPersistenceStatus();
        string? tempPath = null;
        try
        {
            var snapshot = sessions
                .Select(CloneSession)
                .OrderByDescending(session => session.UpdatedAtUtc)
                .Take(MaxPersistedSessions)
                .ToList();

            var json = JsonSerializer.Serialize(new ConversationStore
            {
                SchemaVersion = CurrentStoreSchemaVersion,
                Sessions = snapshot
            }, _jsonOptions);

            var directory = Path.GetDirectoryName(_storagePath) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $"{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");
            PrimaryStoreWriteFaultInjector?.Invoke();
            WriteAllTextDurably(tempPath, json);

            if (File.Exists(_storagePath))
            {
                var replaceBackupPath = tempPath + ".backup";
                if (File.Exists(replaceBackupPath))
                    File.Delete(replaceBackupPath);
                File.Replace(tempPath, _storagePath, replaceBackupPath, ignoreMetadataErrors: true);
                if (File.Exists(replaceBackupPath))
                    File.Delete(replaceBackupPath);
            }
            else
            {
                File.Move(tempPath, _storagePath);
            }

            status.PrimaryStoreSaved = true;
            try
            {
                RecoveryBackupWriteFaultInjector?.Invoke();
                File.Copy(_storagePath, _lastGoodStoragePath, overwrite: true);
                status.RecoveryBackupSaved = true;
            }
            catch (Exception backupEx) when (backupEx is IOException or UnauthorizedAccessException)
            {
                status.RecoveryBackupSaved = false;
                status.ErrorCode = "recovery_backup_save_failed";
                status.PublicMessage = "会话已保存，但恢复备份未更新；下次保存会继续重试。";
                if (_logger != null)
                {
                    LoggerExtensions.LogWarning(
                        _logger,
                        backupEx,
                        "Conversation session primary store saved but last-good backup failed. Path={StoragePath}, LastGoodPath={LastGoodPath}",
                        _storagePath,
                        _lastGoodStoragePath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            status.PrimaryStoreSaved = false;
            status.RecoveryBackupSaved = File.Exists(_lastGoodStoragePath);
            status.ErrorCode = "primary_store_save_failed";
            status.PublicMessage = "结果已生成，但本次会话尚未成功保存。";
            if (_logger != null)
                LoggerExtensions.LogError(_logger, ex, "Failed to persist conversation session store. Path={StoragePath}", _storagePath);
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                try
                { File.Delete(tempPath); }
                catch (IOException) { }
            }
        }

        _lastPersistenceStatus = ClonePersistenceStatus(status);
        return status;
    }

    private void LoadSessionsFromJson(string json)
    {
        var store = JsonSerializer.Deserialize<ConversationStore>(json, _jsonOptions);
        if (store?.Sessions == null || store.Sessions.Count == 0)
            return;

        if (store.SchemaVersion < CurrentStoreSchemaVersion)
        {
            if (_logger != null)
            {
                LoggerExtensions.LogWarning(
                    _logger,
                    "Ignored ownerless conversation session store. SchemaVersion={SchemaVersion}, RequiredSchemaVersion={RequiredSchemaVersion}",
                    store.SchemaVersion,
                    CurrentStoreSchemaVersion);
            }

            return;
        }

        var cutoff = DateTime.UtcNow - SessionRetention;
        foreach (var session in store.Sessions)
        {
            if (session == null ||
                string.IsNullOrWhiteSpace(session.SessionId) ||
                !HasValidOwnerAssociation(session))
                continue;

            NormalizeSession(session);
            if (session.UpdatedAtUtc < cutoff)
                continue;

            _sessions[session.SessionId.Trim()] = session;
        }

        PruneInMemorySessions();
    }

    private void TryLoadLastGoodStore()
    {
        if (!File.Exists(_lastGoodStoragePath))
            return;

        try
        {
            LoadSessionsFromJson(File.ReadAllText(_lastGoodStoragePath));
            if (_logger != null)
            {
                LoggerExtensions.LogInformation(
                    _logger,
                    "Recovered conversation sessions from last-good store. Path={LastGoodPath}",
                    _lastGoodStoragePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (_logger != null)
                LoggerExtensions.LogError(_logger, ex, "Failed to recover conversation sessions from last-good store. Path={LastGoodPath}", _lastGoodStoragePath);
        }
    }

    private void QuarantineCorruptStore(Exception ex)
    {
        try
        {
            if (!File.Exists(_storagePath))
                return;

            var corruptPath = $"{_storagePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(_storagePath, corruptPath);
            if (_logger != null)
            {
                LoggerExtensions.LogWarning(
                    _logger,
                    ex,
                    "Quarantined corrupt conversation session store. Path={StoragePath}, CorruptPath={CorruptPath}",
                    _storagePath,
                    corruptPath);
            }
        }
        catch (Exception quarantineEx) when (quarantineEx is IOException or UnauthorizedAccessException)
        {
            if (_logger != null)
                LoggerExtensions.LogError(_logger, quarantineEx, "Failed to quarantine corrupt conversation session store. Path={StoragePath}", _storagePath);
        }
    }

    private static void WriteAllTextDurably(string path, string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static ConversationSession CloneSession(ConversationSession session)
    {
        lock (session)
        {
            return new ConversationSession
            {
                SessionId = session.SessionId,
                OwnerHash = session.OwnerHash,
                CurrentFlowJson = session.CurrentFlowJson,
                CurrentCanvasFlowJson = session.CurrentCanvasFlowJson,
                WorkspaceSnapshot = CloneWorkspaceSnapshot(session.WorkspaceSnapshot),
                UpdatedAtUtc = session.UpdatedAtUtc,
                History = session.History
                    .Select(turn => new ConversationTurn
                    {
                        TurnId = turn.TurnId,
                        Role = turn.Role,
                        Message = turn.Message,
                        TimestampUtc = turn.TimestampUtc,
                        Payload = CloneTurnPayload(turn.Payload)
                    })
                    .ToList(),
                MutationReceipts = session.MutationReceipts?
                    .Select(receipt => new ConversationMutationReceipt
                    {
                        MutationId = receipt.MutationId,
                        PayloadFingerprint = receipt.PayloadFingerprint,
                        AppliedRevision = receipt.AppliedRevision,
                        UpdatedAtUtc = receipt.UpdatedAtUtc
                    })
                    .ToList() ?? new List<ConversationMutationReceipt>()
            };
        }
    }

    private static void ApplyWorkspaceUpdate(
        VisionAgentWorkspaceSnapshot snapshot,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        snapshot.SchemaVersion = Math.Max(1, snapshot.SchemaVersion);
        snapshot.Revision++;
        snapshot.UpdatedAtUtc = DateTime.UtcNow;

        if (update.ProjectId != null)
            snapshot.ProjectId = NormalizeOptionalSnapshotString(update.ProjectId);
        if (update.LifecycleState != null)
            snapshot.LifecycleState = string.IsNullOrWhiteSpace(update.LifecycleState)
                ? "idle"
                : update.LifecycleState.Trim();
        if (update.PendingPlanSnapshot != null)
            snapshot.PendingPlanSnapshot = ClonePlanSnapshot(update.PendingPlanSnapshot);
        if (update.PlanQuestionSelections != null)
        {
            snapshot.PlanQuestionSelections = update.PlanQuestionSelections
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }
        if (update.ConfirmedPlanAnswers != null)
            snapshot.ConfirmedPlanAnswers = ClonePlanAnswers(update.ConfirmedPlanAnswers);
        if (update.OptimisticPlanAnswers != null)
            snapshot.OptimisticPlanAnswers = ClonePlanAnswers(update.OptimisticPlanAnswers);
        if (update.AnswerRevision.HasValue)
            snapshot.AnswerRevision = Math.Max(0, update.AnswerRevision.Value);
        if (update.ReadinessPreview != null)
            snapshot.ReadinessPreview = CloneReadinessPreview(update.ReadinessPreview);
        if (update.MissingResources != null)
            snapshot.MissingResources = update.MissingResources.Select(resource => resource with { Aliases = resource.Aliases.ToList() }).ToList();
        if (update.ResourceDecisions != null)
        {
            snapshot.ResourceDecisions = update.ResourceDecisions
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase);
        }
        if (update.ResourceRevision.HasValue)
            snapshot.ResourceRevision = Math.Max(0, update.ResourceRevision.Value);
        if (update.RequirementMode != null)
            snapshot.RequirementMode = string.IsNullOrWhiteSpace(update.RequirementMode)
                ? AiRequirementModes.Strict
                : update.RequirementMode.Trim();
        if (update.WorkspaceViewMode != null)
            snapshot.WorkspaceViewMode = string.Equals(update.WorkspaceViewMode, "build", StringComparison.OrdinalIgnoreCase)
                ? "build"
                : "plan";
        if (update.PlanAcceptedRecommendedDefaults.HasValue)
            snapshot.PlanAcceptedRecommendedDefaults = update.PlanAcceptedRecommendedDefaults.Value;
        if (update.PlanRunId != null)
            snapshot.PlanRunId = NormalizeOptionalSnapshotString(update.PlanRunId);
        if (update.PlanRunStatus != null)
            snapshot.PlanRunStatus = NormalizeOptionalSnapshotString(update.PlanRunStatus);
        if (update.PlanTerminalSequence.HasValue)
            snapshot.PlanTerminalSequence = update.PlanTerminalSequence;
        if (update.BuildRunId != null)
            snapshot.BuildRunId = NormalizeOptionalSnapshotString(update.BuildRunId);
        if (update.BuildRunStatus != null)
            snapshot.BuildRunStatus = NormalizeOptionalSnapshotString(update.BuildRunStatus);
        if (update.BuildTerminalSequence.HasValue)
            snapshot.BuildTerminalSequence = update.BuildTerminalSequence;
        if (update.SubmittedBuildFingerprint != null)
            snapshot.SubmittedBuildFingerprint = NormalizeOptionalSnapshotString(update.SubmittedBuildFingerprint);

        if (!IsCurrentReadinessPreview(snapshot, snapshot.ReadinessPreview))
            snapshot.ReadinessPreview = null;
    }

    private static string? NormalizeOptionalSnapshotString(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static void ApplyWorkspaceUpdateLocked(
        ConversationSession session,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        var snapshot = session.WorkspaceSnapshot ?? new VisionAgentWorkspaceSnapshot();
        ApplyWorkspaceUpdate(snapshot, update);
        session.WorkspaceSnapshot = snapshot;

        var userMessage = update.UserMessage?.Trim();
        var userTurnId = update.UserTurnId?.Trim();
        if (!string.IsNullOrWhiteSpace(userMessage) &&
            !string.IsNullOrWhiteSpace(userTurnId) &&
            !session.History.Any(turn => string.Equals(turn.TurnId, userTurnId, StringComparison.OrdinalIgnoreCase)))
        {
            session.History.Add(new ConversationTurn
            {
                TurnId = userTurnId,
                Role = "user",
                Message = userMessage,
                TimestampUtc = DateTime.UtcNow
            });
            TrimHistory(session);
        }

        session.UpdatedAtUtc = snapshot.UpdatedAtUtc;
    }

    private static ConversationPersistenceStatus ClonePersistenceStatus(ConversationPersistenceStatus status)
    {
        return new ConversationPersistenceStatus
        {
            PrimaryStoreSaved = status.PrimaryStoreSaved,
            RecoveryBackupSaved = status.RecoveryBackupSaved,
            ErrorCode = status.ErrorCode,
            PublicMessage = status.PublicMessage
        };
    }

    private static VisionAgentWorkspaceSnapshot? CloneWorkspaceSnapshot(VisionAgentWorkspaceSnapshot? snapshot)
    {
        if (snapshot == null)
            return null;

        var clone = new VisionAgentWorkspaceSnapshot
        {
            SchemaVersion = snapshot.SchemaVersion,
            Revision = snapshot.Revision,
            ProjectId = snapshot.ProjectId,
            LifecycleState = snapshot.LifecycleState,
            PendingPlanSnapshot = ClonePlanSnapshot(snapshot.PendingPlanSnapshot),
            PlanQuestionSelections = snapshot.PlanQuestionSelections
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            ConfirmedPlanAnswers = ClonePlanAnswers(snapshot.ConfirmedPlanAnswers),
            OptimisticPlanAnswers = ClonePlanAnswers(snapshot.OptimisticPlanAnswers),
            AnswerRevision = snapshot.AnswerRevision,
            ReadinessPreview = CloneReadinessPreview(snapshot.ReadinessPreview),
            MissingResources = snapshot.MissingResources.Select(resource => resource with { Aliases = resource.Aliases.ToList() }).ToList(),
            ResourceDecisions = snapshot.ResourceDecisions
                .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            ResourceRevision = snapshot.ResourceRevision,
            RequirementMode = snapshot.RequirementMode,
            WorkspaceViewMode = snapshot.WorkspaceViewMode,
            PlanAcceptedRecommendedDefaults = snapshot.PlanAcceptedRecommendedDefaults,
            PlanRunId = snapshot.PlanRunId,
            PlanRunStatus = snapshot.PlanRunStatus,
            PlanTerminalSequence = snapshot.PlanTerminalSequence,
            BuildRunId = snapshot.BuildRunId,
            BuildRunStatus = snapshot.BuildRunStatus,
            BuildTerminalSequence = snapshot.BuildTerminalSequence,
            SubmittedBuildFingerprint = snapshot.SubmittedBuildFingerprint,
            UpdatedAtUtc = snapshot.UpdatedAtUtc
        };

        if (!IsCurrentReadinessPreview(clone, clone.ReadinessPreview))
            clone.ReadinessPreview = null;

        return clone;
    }

    private static bool IsCurrentReadinessPreview(
        VisionAgentWorkspaceSnapshot snapshot,
        VisionAgentBuildReadinessPreviewResult? preview)
    {
        var plan = snapshot.PendingPlanSnapshot;
        if (preview == null || plan == null)
            return false;

        return IdentityEquals(preview.PlanId, plan.PlanId) &&
               IdentityEquals(preview.PlanHash, plan.PlanHash) &&
               IdentityEquals(preview.RequirementMode, snapshot.RequirementMode) &&
               preview.AnswerRevision == snapshot.AnswerRevision &&
               preview.ResourceRevision == snapshot.ResourceRevision &&
               !HasDoubleEncodedDerivedResource(preview);
    }

    private static bool HasDoubleEncodedDerivedResource(VisionAgentBuildReadinessPreviewResult preview)
    {
        return preview.BuildReadiness.MissingResources
            .Concat(preview.BuildReadiness.Blockers
                .Where(blocker => blocker.Resource != null)
                .Select(blocker => blocker.Resource!))
            .Any(resource =>
            {
                if (!VisionAgentResourceIdentity.TryParseCanonicalId(
                        resource.CanonicalId,
                        out var resourceType,
                        out var operatorKey,
                        out var parameterName) ||
                    !resourceType.Equals("resource", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var encodedIdentity = VisionAgentResourceIdentity.NormalizeToken($"{operatorKey}{parameterName}");
                return encodedIdentity.Contains("resourcev1resource", StringComparison.Ordinal);
            });
    }

    private static bool IdentityEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static VisionAgentPlanModeResult? ClonePlanSnapshot(VisionAgentPlanModeResult? plan)
    {
        if (plan == null)
            return null;

        var json = JsonSerializer.Serialize(plan, _jsonOptions);
        return JsonSerializer.Deserialize<VisionAgentPlanModeResult>(json, _jsonOptions);
    }

    private static VisionAgentBuildReadinessPreviewResult? CloneReadinessPreview(
        VisionAgentBuildReadinessPreviewResult? preview)
    {
        if (preview == null)
            return null;

        var json = JsonSerializer.Serialize(preview, _jsonOptions);
        return JsonSerializer.Deserialize<VisionAgentBuildReadinessPreviewResult>(json, _jsonOptions);
    }

    private static List<VisionAgentPlanAnswer> ClonePlanAnswers(IEnumerable<VisionAgentPlanAnswer>? answers)
    {
        if (answers == null)
            return [];

        var json = JsonSerializer.Serialize(answers, _jsonOptions);
        return JsonSerializer.Deserialize<List<VisionAgentPlanAnswer>>(json, _jsonOptions) ?? [];
    }

    private static ConversationTurnPayload? CloneTurnPayload(ConversationTurnPayload? payload)
    {
        if (payload == null)
            return null;

        return new ConversationTurnPayload
        {
            Kind = payload.Kind,
            Status = payload.Status,
            InteractionState = payload.InteractionState,
            TurnIntent = payload.TurnIntent,
            RouterConfidence = payload.RouterConfidence,
            BlockingClarificationFields = payload.BlockingClarificationFields?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            NonBlockingMissingFields = payload.NonBlockingMissingFields?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            ClarificationRound = payload.ClarificationRound,
            AskedQuestionFingerprints = payload.AskedQuestionFingerprints?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            AnsweredClarificationFields = payload.AnsweredClarificationFields?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            Reply = payload.Reply,
            Reasoning = payload.Reasoning,
            Progress = payload.Progress?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? new List<string>(),
            Failure = CloneTurnFailurePayload(payload.Failure),
            ManualRetry = CloneManualRetry(payload.ManualRetry),
            ClarificationRequired = payload.ClarificationRequired,
            RequirementBrief = CloneRequirementBrief(payload.RequirementBrief),
            BuildResult = CloneJsonObject(payload.BuildResult),
            WorkflowDiff = CloneJsonObject(payload.WorkflowDiff),
            ApplyGate = CloneJsonObject(payload.ApplyGate),
            ToolEvidenceTimeline = CloneJsonObject(payload.ToolEvidenceTimeline),
            FirstFixRecommendation = payload.FirstFixRecommendation
        };
    }

    private static object? CloneJsonObject(object? value)
    {
        if (value == null)
            return null;

        var json = JsonSerializer.Serialize(value, _jsonOptions);
        return JsonSerializer.Deserialize<JsonElement>(json, _jsonOptions);
    }

    private static ConversationTurnFailurePayload? CloneTurnFailurePayload(ConversationTurnFailurePayload? failure)
    {
        if (failure == null)
            return null;

        return new ConversationTurnFailurePayload
        {
            Summary = failure.Summary ?? string.Empty,
            FailureSummary = CloneFailureSummary(failure.FailureSummary),
            Diagnostics = failure.Diagnostics?.Select(CloneAttemptDiagnostic).ToList() ?? new List<AiAttemptDiagnostic>()
        };
    }

    private static AiManualRetryInfo? CloneManualRetry(AiManualRetryInfo? manualRetry)
    {
        if (manualRetry == null)
            return null;

        return new AiManualRetryInfo
        {
            Required = manualRetry.Required,
            Stage = manualRetry.Stage,
            Draft = manualRetry.Draft,
            Summary = manualRetry.Summary,
            RepairTarget = manualRetry.RepairTarget,
            LastOutputSummary = manualRetry.LastOutputSummary,
            Diagnostics = manualRetry.Diagnostics?.Select(CloneAttemptDiagnostic).ToList() ?? new List<AiAttemptDiagnostic>()
        };
    }

    private static AiFailureSummary? CloneFailureSummary(AiFailureSummary? summary)
    {
        if (summary == null)
            return null;

        return new AiFailureSummary
        {
            Category = summary.Category,
            Code = summary.Code,
            Message = summary.Message,
            RepairTarget = summary.RepairTarget,
            RetryCount = summary.RetryCount,
            LastOutputSummary = summary.LastOutputSummary
        };
    }

    private static AiRequirementBrief? CloneRequirementBrief(AiRequirementBrief? brief)
    {
        if (brief == null)
            return null;

        var json = JsonSerializer.Serialize(brief, _jsonOptions);
        return JsonSerializer.Deserialize<AiRequirementBrief>(json, _jsonOptions);
    }

    private static AiAttemptDiagnostic CloneAttemptDiagnostic(AiAttemptDiagnostic diagnostic)
    {
        return new AiAttemptDiagnostic
        {
            AttemptNumber = diagnostic.AttemptNumber,
            Stage = diagnostic.Stage,
            Summary = diagnostic.Summary,
            OutputSummary = diagnostic.OutputSummary,
            Issues = diagnostic.Issues?.Select(CloneValidationDiagnostic).ToList() ?? new List<AiValidationDiagnostic>()
        };
    }

    private static AiValidationDiagnostic CloneValidationDiagnostic(AiValidationDiagnostic diagnostic)
    {
        return new AiValidationDiagnostic
        {
            Severity = diagnostic.Severity,
            Code = diagnostic.Code,
            Category = diagnostic.Category,
            Message = diagnostic.Message,
            RelatedFields = diagnostic.RelatedFields?.ToList() ?? new List<string>(),
            OperatorId = diagnostic.OperatorId,
            ParameterName = diagnostic.ParameterName,
            SourceTempId = diagnostic.SourceTempId,
            SourcePortName = diagnostic.SourcePortName,
            TargetTempId = diagnostic.TargetTempId,
            TargetPortName = diagnostic.TargetPortName,
            RepairHint = diagnostic.RepairHint
        };
    }

    private static void NormalizeSession(ConversationSession session)
    {
        session.History ??= new List<ConversationTurn>();
        session.History = session.History
            .Where(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Role))
            .OrderBy(turn => turn.TimestampUtc)
            .TakeLast(MaxHistory)
            .Select(turn => new ConversationTurn
            {
                TurnId = string.IsNullOrWhiteSpace(turn.TurnId) ? Guid.NewGuid().ToString("N") : turn.TurnId.Trim(),
                Role = turn.Role,
                Message = turn.Message ?? string.Empty,
                TimestampUtc = turn.TimestampUtc == default ? DateTime.UtcNow : turn.TimestampUtc,
                Payload = CloneTurnPayload(turn.Payload)
            })
            .ToList();

        session.MutationReceipts ??= new List<ConversationMutationReceipt>();
        session.MutationReceipts = session.MutationReceipts
            .Where(receipt =>
                receipt != null &&
                !string.IsNullOrWhiteSpace(receipt.MutationId) &&
                !string.IsNullOrWhiteSpace(receipt.PayloadFingerprint))
            .GroupBy(receipt => receipt.MutationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(receipt => receipt.UpdatedAtUtc == default ? DateTime.MinValue : receipt.UpdatedAtUtc)
                .First())
            .OrderByDescending(receipt => receipt.UpdatedAtUtc == default ? DateTime.MinValue : receipt.UpdatedAtUtc)
            .Take(MaxMutationReceipts)
            .OrderBy(receipt => receipt.UpdatedAtUtc == default ? DateTime.MinValue : receipt.UpdatedAtUtc)
            .Select(receipt => new ConversationMutationReceipt
            {
                MutationId = receipt.MutationId.Trim(),
                PayloadFingerprint = receipt.PayloadFingerprint.Trim(),
                AppliedRevision = receipt.AppliedRevision,
                UpdatedAtUtc = receipt.UpdatedAtUtc == default ? DateTime.UtcNow : receipt.UpdatedAtUtc
            })
            .ToList();

        session.WorkspaceSnapshot = CloneWorkspaceSnapshot(session.WorkspaceSnapshot);

        if (string.IsNullOrWhiteSpace(session.CurrentCanvasFlowJson) &&
            IsCanvasFlowJson(session.CurrentFlowJson))
        {
            session.CurrentCanvasFlowJson = session.CurrentFlowJson;
        }

        session.UpdatedAtUtc = session.UpdatedAtUtc == default ? DateTime.UtcNow : session.UpdatedAtUtc;
        session.SessionId = session.SessionId.Trim();
        session.OwnerHash = session.OwnerHash?.Trim() ?? string.Empty;
    }

    private static bool IsCanvasFlowJson(string? flowJson)
    {
        if (string.IsNullOrWhiteSpace(flowJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(flowJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var operators = TryGetArray(root, "operators", "Operators");
            var connections = TryGetArray(root, "connections", "Connections");
            if (operators == null || connections == null)
                return false;

            if (operators.Value.GetArrayLength() == 0)
                return true;

            var first = operators.Value.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                return false;

            if (first.TryGetProperty("tempId", out _) || first.TryGetProperty("TempId", out _))
                return false;

            if (first.TryGetProperty("operatorType", out _) || first.TryGetProperty("OperatorType", out _))
                return false;

            return first.TryGetProperty("id", out _) || first.TryGetProperty("Id", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasMeaningfulFlow(string? flowJson)
    {
        if (string.IsNullOrWhiteSpace(flowJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(flowJson);
            var root = doc.RootElement;
            var operators = TryGetArray(root, "operators", "Operators");
            if (operators == null)
                return false;

            if (operators.Value.GetArrayLength() > 0)
                return true;

            var connections = TryGetArray(root, "connections", "Connections");
            return connections != null && connections.Value.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement? TryGetArray(JsonElement root, string camelName, string pascalName)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty(camelName, out var camel) && camel.ValueKind == JsonValueKind.Array)
            return camel;

        if (root.TryGetProperty(pascalName, out var pascal) && pascal.ValueKind == JsonValueKind.Array)
            return pascal;

        return null;
    }

    private void PruneInMemorySessions()
    {
        var cutoff = DateTime.UtcNow - SessionRetention;
        foreach (var kvp in _sessions.ToArray())
        {
            if (!HasValidOwnerAssociation(kvp.Value) || kvp.Value.UpdatedAtUtc < cutoff)
                _sessions.TryRemove(kvp.Key, out _);
        }

        if (_sessions.Count <= MaxPersistedSessions)
            return;

        var keepSessionIds = _sessions.Values
            .OrderByDescending(session => session.UpdatedAtUtc)
            .Take(MaxPersistedSessions)
            .Select(session => session.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _sessions.Keys)
        {
            if (!keepSessionIds.Contains(key))
                _sessions.TryRemove(key, out _);
        }
    }
}
