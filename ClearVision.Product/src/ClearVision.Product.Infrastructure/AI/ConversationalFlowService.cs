// ConversationalFlowService.cs
// 会话式流程服务
// 提供多轮对话下的流程生成、上下文维护与意图识别
// 作者：蘅芜君
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
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
    public string? CurrentFlowJson { get; set; }
    public string? CurrentCanvasFlowJson { get; set; }
    public VisionAgentWorkspaceSnapshot? WorkspaceSnapshot { get; set; }
    public List<ConversationTurn> History { get; set; } = new();
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
    public string RequirementMode { get; set; } = AiRequirementModes.Strict;
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
    public string? RequirementMode { get; set; }
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

public interface IConversationalFlowService
{
    ConversationSession GetOrCreateSession(string? sessionId);
    ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow);
    ConversationContext PrepareContext(AiFlowGenerationRequest request);
    void RecordAssistantResponse(
        string sessionId,
        string assistantMessage,
        string? latestFlowJson,
        string? latestCanvasFlowJson = null,
        ConversationTurnPayload? payload = null);
    IReadOnlyList<ConversationSessionSummary> ListSessions();
    ConversationSession? GetSession(string sessionId);
    bool TryBackfillCanvasFlowJson(string sessionId, string canvasFlowJson);
    ConversationSession UpdateWorkspaceSnapshot(string sessionId, VisionAgentWorkspaceSnapshotUpdate update);
    VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update);
    VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(VisionAgentTerminalProjectionRequest request);
    ConversationPersistenceStatus GetLastPersistenceStatus();
    bool DeleteSession(string sessionId);
}

internal sealed class ConversationStore
{
    public List<ConversationSession> Sessions { get; set; } = new();
}

public class ConversationalFlowService : IConversationalFlowService
{
    private const int MaxHistory = 20;
    private const int MaxPromptHistory = 5;
    private const int MaxPersistedSessions = 200;
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

    public ConversationalFlowService(
        string? storageRootPath = null,
        Microsoft.Extensions.Logging.ILogger<ConversationalFlowService>? logger = null)
    {
        _logger = logger;
        var rootPath = string.IsNullOrWhiteSpace(storageRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearVision")
            : storageRootPath;

        Directory.CreateDirectory(rootPath);
        _storagePath = Path.Combine(rootPath, "conversation_sessions.json");
        _lastGoodStoragePath = _storagePath + ".last-good";
        LoadSessionsFromStore();
    }

    public ConversationSession GetOrCreateSession(string? sessionId)
    {
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId.Trim();

        return _sessions.GetOrAdd(normalizedSessionId, id => new ConversationSession
        {
            SessionId = id,
            UpdatedAtUtc = DateTime.UtcNow
        });
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

    public ConversationContext PrepareContext(AiFlowGenerationRequest request)
    {
        var session = GetOrCreateSession(request.SessionId);
        ConversationIntent intent;
        GenerateFlowMode resolvedMode;
        string? existingFlowJson;
        string sessionSummary;

        lock (session)
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
        }

        PersistSessions();
        return new ConversationContext
        {
            SessionId = session.SessionId,
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

    public void RecordAssistantResponse(
        string sessionId,
        string assistantMessage,
        string? latestFlowJson,
        string? latestCanvasFlowJson = null,
        ConversationTurnPayload? payload = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;

        lock (session)
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
        }

        PersistSessions();
    }

    public IReadOnlyList<ConversationSessionSummary> ListSessions()
    {
        return _sessions.Values
            .Select(BuildSessionSummary)
            .OrderByDescending(summary => summary.UpdatedAtUtc)
            .ToList();
    }

    public ConversationSession? GetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var normalizedSessionId = sessionId.Trim();
        if (!_sessions.TryGetValue(normalizedSessionId, out var session))
            return null;

        return CloneSession(session);
    }

    public bool TryBackfillCanvasFlowJson(string sessionId, string canvasFlowJson)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(canvasFlowJson))
            return false;

        var normalizedSessionId = sessionId.Trim();
        if (!_sessions.TryGetValue(normalizedSessionId, out var session))
            return false;

        lock (session)
        {
            if (!string.IsNullOrWhiteSpace(session.CurrentCanvasFlowJson))
                return false;

            session.CurrentCanvasFlowJson = canvasFlowJson;
        }

        PersistSessions();
        return true;
    }

    public ConversationSession UpdateWorkspaceSnapshot(string sessionId, VisionAgentWorkspaceSnapshotUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var session = GetOrCreateSession(sessionId);
        lock (session)
        {
            ApplyWorkspaceUpdateLocked(session, update);
        }

        PersistSessions();
        return CloneSession(session);
    }

    public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var session = GetOrCreateSession(sessionId);

        lock (session)
        {
            var snapshot = session.WorkspaceSnapshot ??= new VisionAgentWorkspaceSnapshot();
            var expected = update.ExpectedRevision.GetValueOrDefault(-1);
            if (expected != snapshot.Revision)
            {
                return new VisionAgentWorkspaceSnapshotMutationResult
                {
                    Success = false,
                    Conflict = true,
                    ErrorCode = "workspace_revision_conflict",
                    PublicMessage = "工作台状态已更新，请刷新后重试本次修改。",
                    Snapshot = CloneWorkspaceSnapshot(snapshot),
                    PersistenceStatus = ClonePersistenceStatus(_lastPersistenceStatus)
                };
            }

            ApplyWorkspaceUpdateLocked(session, update);
        }

        var persistence = PersistSessions();
        var latest = GetSession(session.SessionId)?.WorkspaceSnapshot;
        return new VisionAgentWorkspaceSnapshotMutationResult
        {
            Success = persistence.PrimaryStoreSaved,
            Conflict = false,
            ErrorCode = persistence.PrimaryStoreSaved ? string.Empty : persistence.ErrorCode,
            PublicMessage = persistence.PublicMessage,
            Snapshot = latest,
            PersistenceStatus = persistence
        };
    }

    public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(VisionAgentTerminalProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = GetOrCreateSession(request.SessionId);
        var assistantTurnId = string.IsNullOrWhiteSpace(request.AssistantTurnId)
            ? Guid.NewGuid().ToString("N")
            : request.AssistantTurnId.Trim();

        lock (session)
        {
            if (!string.IsNullOrWhiteSpace(request.LatestFlowJson))
                session.CurrentFlowJson = request.LatestFlowJson;

            if (!string.IsNullOrWhiteSpace(request.LatestCanvasFlowJson))
                session.CurrentCanvasFlowJson = request.LatestCanvasFlowJson;
            else if (IsCanvasFlowJson(request.LatestFlowJson))
                session.CurrentCanvasFlowJson = request.LatestFlowJson;

            if (!session.History.Any(turn => string.Equals(turn.TurnId, assistantTurnId, StringComparison.OrdinalIgnoreCase)))
            {
                session.History.Add(new ConversationTurn
                {
                    TurnId = assistantTurnId,
                    Role = "assistant",
                    Message = request.AssistantMessage,
                    TimestampUtc = DateTime.UtcNow,
                    Payload = CloneTurnPayload(request.Payload)
                });
                TrimHistory(session);
            }

            ApplyWorkspaceUpdateLocked(session, request.WorkspaceUpdate);
        }

        var persistence = PersistSessions();
        var latest = GetSession(session.SessionId)?.WorkspaceSnapshot;
        return new VisionAgentWorkspaceSnapshotMutationResult
        {
            Success = persistence.PrimaryStoreSaved,
            Conflict = false,
            ErrorCode = persistence.PrimaryStoreSaved ? string.Empty : persistence.ErrorCode,
            PublicMessage = persistence.PublicMessage,
            Snapshot = latest,
            PersistenceStatus = persistence
        };
    }

    public ConversationPersistenceStatus GetLastPersistenceStatus() =>
        ClonePersistenceStatus(_lastPersistenceStatus);

    public bool DeleteSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        var normalizedSessionId = sessionId.Trim();
        if (!_sessions.TryRemove(normalizedSessionId, out _))
            return false;

        PersistSessions();
        return true;
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
            var status = new ConversationPersistenceStatus();
            string? tempPath = null;
            try
            {
                PruneInMemorySessions();

                var snapshot = _sessions.Values
                    .Select(CloneSession)
                    .OrderByDescending(session => session.UpdatedAtUtc)
                    .Take(MaxPersistedSessions)
                    .ToList();

                var json = JsonSerializer.Serialize(new ConversationStore
                {
                    Sessions = snapshot
                }, _jsonOptions);

                var directory = Path.GetDirectoryName(_storagePath) ?? AppContext.BaseDirectory;
                Directory.CreateDirectory(directory);
                tempPath = Path.Combine(directory, $"{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");
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
                    try { File.Delete(tempPath); } catch (IOException) { }
                }
            }

            _lastPersistenceStatus = ClonePersistenceStatus(status);
            return status;
        }
    }

    private void LoadSessionsFromJson(string json)
    {
        var store = JsonSerializer.Deserialize<ConversationStore>(json, _jsonOptions);
        if (store?.Sessions == null || store.Sessions.Count == 0)
            return;

        var cutoff = DateTime.UtcNow - SessionRetention;
        foreach (var session in store.Sessions)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
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
                    .ToList()
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

        if (!string.IsNullOrWhiteSpace(update.ProjectId))
            snapshot.ProjectId = update.ProjectId.Trim();
        if (!string.IsNullOrWhiteSpace(update.LifecycleState))
            snapshot.LifecycleState = update.LifecycleState.Trim();
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
        if (!string.IsNullOrWhiteSpace(update.RequirementMode))
            snapshot.RequirementMode = update.RequirementMode.Trim();
        if (update.PlanAcceptedRecommendedDefaults.HasValue)
            snapshot.PlanAcceptedRecommendedDefaults = update.PlanAcceptedRecommendedDefaults.Value;
        if (!string.IsNullOrWhiteSpace(update.PlanRunId))
            snapshot.PlanRunId = update.PlanRunId.Trim();
        if (!string.IsNullOrWhiteSpace(update.PlanRunStatus))
            snapshot.PlanRunStatus = update.PlanRunStatus.Trim();
        if (update.PlanTerminalSequence.HasValue)
            snapshot.PlanTerminalSequence = update.PlanTerminalSequence;
        if (!string.IsNullOrWhiteSpace(update.BuildRunId))
            snapshot.BuildRunId = update.BuildRunId.Trim();
        if (!string.IsNullOrWhiteSpace(update.BuildRunStatus))
            snapshot.BuildRunStatus = update.BuildRunStatus.Trim();
        if (update.BuildTerminalSequence.HasValue)
            snapshot.BuildTerminalSequence = update.BuildTerminalSequence;
        if (!string.IsNullOrWhiteSpace(update.SubmittedBuildFingerprint))
            snapshot.SubmittedBuildFingerprint = update.SubmittedBuildFingerprint.Trim();
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

        return new VisionAgentWorkspaceSnapshot
        {
            SchemaVersion = snapshot.SchemaVersion,
            Revision = snapshot.Revision,
            ProjectId = snapshot.ProjectId,
            LifecycleState = snapshot.LifecycleState,
            PendingPlanSnapshot = ClonePlanSnapshot(snapshot.PendingPlanSnapshot),
            PlanQuestionSelections = snapshot.PlanQuestionSelections
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            ConfirmedPlanAnswers = ClonePlanAnswers(snapshot.ConfirmedPlanAnswers),
            RequirementMode = snapshot.RequirementMode,
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
    }

    private static VisionAgentPlanModeResult? ClonePlanSnapshot(VisionAgentPlanModeResult? plan)
    {
        if (plan == null)
            return null;

        var json = JsonSerializer.Serialize(plan, _jsonOptions);
        return JsonSerializer.Deserialize<VisionAgentPlanModeResult>(json, _jsonOptions);
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

        session.WorkspaceSnapshot = CloneWorkspaceSnapshot(session.WorkspaceSnapshot);

        if (string.IsNullOrWhiteSpace(session.CurrentCanvasFlowJson) &&
            IsCanvasFlowJson(session.CurrentFlowJson))
        {
            session.CurrentCanvasFlowJson = session.CurrentFlowJson;
        }

        session.UpdatedAtUtc = session.UpdatedAtUtc == default ? DateTime.UtcNow : session.UpdatedAtUtc;
        session.SessionId = session.SessionId.Trim();
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
            if (kvp.Value.UpdatedAtUtc < cutoff)
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
