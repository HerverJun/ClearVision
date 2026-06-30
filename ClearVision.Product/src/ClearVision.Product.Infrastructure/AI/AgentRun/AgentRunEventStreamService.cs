using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public interface IAgentRunEventStreamService
{
    AgentRunCreateResult CreateRun(string description, object? payload = null, string? ownerHash = null);
    AgentRunEvent? Append(string? runId, AgentRunEventDraft draft);
    AgentRunTerminalReservationResult TryReserveTerminal(string? runId, string terminalStatus);
    AgentRunTerminalIntentRecord? PrepareTerminalIntent(
        string? runId,
        AgentRunTerminalIntentDraft intent,
        AgentRunTerminalReservationResult? reservation = null);
    AgentRunEvent? Complete(string? runId, string summary, object? payload = null, AgentRunTerminalReservationResult? reservation = null);
    AgentRunEvent? Fail(string? runId, string summary, string firstFixRecommendation, object? payload = null, AgentRunTerminalReservationResult? reservation = null);
    AgentRunEvent? FailHostInterrupted(string? runId);
    AgentRunEvent? Cancel(string? runId, string summary = "Vision Agent run cancelled by user.", object? payload = null, AgentRunTerminalReservationResult? reservation = null);
    AgentRunEventSubscription? Subscribe(string runId, long afterSequence);
    AgentRunReplayResult? ReplayRaw(string runId);
    AgentRunReplayResult? Replay(string runId);
    AgentRunReplayResult? ReplayLatest(string? ownerHash = null);
    CancellationToken GetCancellationToken(string? runId);
    bool TryCancelToken(string runId);
    bool IsRunOwner(string runId, string? ownerHash);
    string? IssueStreamToken(string runId, string? ownerHash, TimeSpan? ttl = null);
    AgentRunStreamTokenValidationResult ValidateStreamToken(string runId, string? token, bool consume = true);
}

public enum AgentRunTerminalReservationOutcome
{
    Acquired,
    AlreadyReservedBySameStatus,
    AlreadyTerminal,
    RejectedByOtherTerminalOwner,
    RunNotFound,
    InvalidTerminalStatus
}

public sealed record AgentRunTerminalReservationResult(
    AgentRunTerminalReservationOutcome Outcome,
    string TargetStatus,
    string CurrentStatus,
    string? ReservationId = null)
{
    public bool Acquired => Outcome == AgentRunTerminalReservationOutcome.Acquired;
}

public sealed class AgentRunEventStreamService : IAgentRunEventStreamService
{
    private const int MaxRecentEvents = 4096;

    private readonly ConcurrentDictionary<string, AgentRunState> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AgentRunStreamToken> _streamTokens = new(StringComparer.Ordinal);
    private readonly AgentRunEventStore _store;
    private readonly AgentRunEventRedactor _redactor;

    public AgentRunEventStreamService()
        : this(new AgentRunEventStore(), new AgentRunEventRedactor())
    {
    }

    public AgentRunEventStreamService(AgentRunEventStore store, AgentRunEventRedactor redactor)
    {
        _store = store;
        _redactor = redactor;
    }

    public Func<DateTimeOffset> UtcNowProvider { get; set; } = static () => DateTimeOffset.UtcNow;

    public string HostInstanceId { get; } = Guid.NewGuid().ToString("N");

    public AgentRunCreateResult CreateRun(string description, object? payload = null, string? ownerHash = null)
    {
        var runId = $"ar_{Guid.NewGuid():N}";
        var state = new AgentRunState(runId, UtcNowProvider(), ownerHash);
        _runs[runId] = state;

        var brief = BuildBrief(description);
        var events = new List<AgentRunEvent>();
        events.Add(Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunStarted,
            Stage = "run",
            Title = "Vision Agent run started",
            Summary = "Received the request and opened a metadata-only event stream.",
            Status = AgentRunEventStatuses.Running,
            Payload = payload
        })!);
        events.Add(Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.AssistantBrief,
            Stage = "brief",
            Title = "Task summary",
            Summary = brief,
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                brief,
                publicDiagnosticsOnly = true,
                metadataOnly = true
            }
        })!);

        return new AgentRunCreateResult(runId, brief, events);
    }

    public AgentRunEvent? Append(string? runId, AgentRunEventDraft draft)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(draft.EventType))
        {
            return null;
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return null;
        }

        AgentRunEvent evt;
        List<ChannelWriter<AgentRunEvent>> subscribers;
        lock (state.Gate)
        {
            if (state.IsTerminal)
            {
                state.DroppedEventCount++;
                _store.AppendSummary(state.ToSummary());
                return null;
            }

            evt = BuildSafeEvent(state.RunId, state.NextSequence(), draft);
            state.Events.Add(evt);
            if (state.Events.Count > MaxRecentEvents)
            {
                state.Events.RemoveRange(0, state.Events.Count - MaxRecentEvents);
            }

            UpdateSummaryFromEvent(state, evt);
            _store.AppendEvent(evt);
            _store.AppendSummary(state.ToSummary());
            subscribers = state.Subscribers.ToList();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.TryWrite(evt);
        }

        return evt;
    }

    public AgentRunTerminalReservationResult TryReserveTerminal(string? runId, string terminalStatus)
    {
        var targetStatus = NormalizeTerminalStatus(terminalStatus);
        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            return new AgentRunTerminalReservationResult(
                AgentRunTerminalReservationOutcome.InvalidTerminalStatus,
                terminalStatus?.Trim() ?? string.Empty,
                AgentRunTerminalIntents.None);
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return new AgentRunTerminalReservationResult(
                AgentRunTerminalReservationOutcome.RunNotFound,
                targetStatus,
                AgentRunTerminalIntents.None);
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return new AgentRunTerminalReservationResult(
                AgentRunTerminalReservationOutcome.RunNotFound,
                targetStatus,
                AgentRunTerminalIntents.None);
        }

        AgentRunTerminalReservationResult result;
        var cancelToken = false;
        lock (state.Gate)
        {
            if (state.IsTerminal)
            {
                result = new AgentRunTerminalReservationResult(
                    AgentRunTerminalReservationOutcome.AlreadyTerminal,
                    targetStatus,
                    NormalizeTerminalStatus(state.Status) ?? state.Status);
            }
            else if (string.IsNullOrWhiteSpace(state.TerminalIntentStatus))
            {
                state.TerminalIntentStatus = targetStatus;
                state.TerminalIntentState = ToPendingTerminalIntent(targetStatus);
                state.TerminalReservationId = Guid.NewGuid().ToString("N");
                cancelToken = string.Equals(targetStatus, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
                result = new AgentRunTerminalReservationResult(
                    AgentRunTerminalReservationOutcome.Acquired,
                    targetStatus,
                    state.TerminalIntentState,
                    state.TerminalReservationId);
            }
            else if (string.Equals(state.TerminalIntentStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                cancelToken = string.Equals(targetStatus, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
                result = new AgentRunTerminalReservationResult(
                    AgentRunTerminalReservationOutcome.AlreadyReservedBySameStatus,
                    targetStatus,
                    state.TerminalIntentState);
            }
            else
            {
                result = new AgentRunTerminalReservationResult(
                    AgentRunTerminalReservationOutcome.RejectedByOtherTerminalOwner,
                    targetStatus,
                    state.TerminalIntentState);
            }
        }

        if (cancelToken)
        {
            TryCancelStateToken(state);
        }

        return result;
    }

    public AgentRunTerminalIntentRecord? PrepareTerminalIntent(
        string? runId,
        AgentRunTerminalIntentDraft intent,
        AgentRunTerminalReservationResult? reservation = null)
    {
        if (string.IsNullOrWhiteSpace(runId) || intent == null)
        {
            return null;
        }

        var targetStatus = NormalizeTerminalStatus(intent.TargetStatus);
        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            return null;
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return null;
        }

        lock (state.Gate)
        {
            if (state.IsTerminal)
            {
                return state.TerminalIntent;
            }

            if (reservation != null)
            {
                if (reservation.Outcome != AgentRunTerminalReservationOutcome.Acquired ||
                    string.IsNullOrWhiteSpace(reservation.ReservationId) ||
                    !string.Equals(reservation.TargetStatus, targetStatus, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(state.TerminalIntentStatus, targetStatus, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(state.TerminalReservationId, reservation.ReservationId, StringComparison.Ordinal))
                {
                    return null;
                }
            }
            else if (string.IsNullOrWhiteSpace(state.TerminalIntentStatus))
            {
                state.TerminalIntentStatus = targetStatus;
                state.TerminalIntentState = ToPendingTerminalIntent(targetStatus);
                state.TerminalReservationId = Guid.NewGuid().ToString("N");
            }
            else if (!string.Equals(state.TerminalIntentStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var existing = state.TerminalIntent;
            if (existing != null)
            {
                return string.Equals(existing.TargetStatus, targetStatus, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(existing.TerminalMutationId, intent.TerminalMutationId?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(existing.PayloadFingerprint, intent.PayloadFingerprint?.Trim() ?? string.Empty, StringComparison.Ordinal)
                    ? existing
                    : null;
            }

            var record = new AgentRunTerminalIntentRecord
            {
                RunId = state.RunId,
                SessionId = intent.SessionId?.Trim() ?? string.Empty,
                RunType = intent.RunType?.Trim() ?? string.Empty,
                TargetStatus = targetStatus,
                TerminalMutationId = intent.TerminalMutationId?.Trim() ?? string.Empty,
                PayloadFingerprint = intent.PayloadFingerprint?.Trim() ?? string.Empty,
                ExpectedWorkspaceRevision = intent.ExpectedWorkspaceRevision,
                Identity = intent.Identity?.Trim() ?? string.Empty,
                Phase = string.IsNullOrWhiteSpace(intent.Phase) ? "TerminalPrepared" : intent.Phase.Trim(),
                CreatedAt = UtcNowProvider(),
                HostInstanceId = HostInstanceId,
                MetadataOnly = true
            };

            state.TerminalIntent = record;
            state.UpdatedAt = UtcNowProvider();
            _store.AppendSummary(state.ToSummary());
            return record;
        }
    }

    public AgentRunEvent? Complete(
        string? runId,
        string summary,
        object? payload = null,
        AgentRunTerminalReservationResult? reservation = null)
    {
        return AppendTerminal(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunCompleted,
            Stage = "run",
            Title = "Run completed",
            Summary = summary,
            Status = AgentRunEventStatuses.Completed,
            Payload = payload
        }, reservation);
    }

    public AgentRunEvent? Fail(
        string? runId,
        string summary,
        string firstFixRecommendation,
        object? payload = null,
        AgentRunTerminalReservationResult? reservation = null)
    {
        return AppendTerminal(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunFailed,
            Stage = "run",
            Title = "Run failed",
            Summary = summary,
            Status = AgentRunEventStatuses.Failed,
            Payload = BuildTerminalDiagnosticPayload(payload, firstFixRecommendation)
        }, reservation);
    }

    public AgentRunEvent? FailHostInterrupted(string? runId)
    {
        return Fail(
            runId,
            "上一次主机进程在该 AgentRun 到达终态前结束，本次已将它恢复为失败状态。",
            "请重新提交请求，由当前正在运行的主机从头执行。",
            new
            {
                failureCode = "host_instance_interrupted",
                metadataOnly = true
            });
    }

    public AgentRunEvent? Cancel(
        string? runId,
        string summary = "Vision Agent run cancelled by user.",
        object? payload = null,
        AgentRunTerminalReservationResult? reservation = null)
    {
        var terminal = AppendTerminal(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunCancelled,
            Stage = "run",
            Title = "Run cancelled",
            Summary = summary,
            Status = AgentRunEventStatuses.Cancelled,
            Payload = BuildTerminalDiagnosticPayload(
                payload,
                "Submit the request again when you are ready to continue.")
        }, reservation);

        if (!string.IsNullOrWhiteSpace(runId))
        {
            TryCancelToken(runId);
        }

        return terminal;
    }

    public AgentRunEventSubscription? Subscribe(string runId, long afterSequence)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return null;
        }

        var channel = Channel.CreateUnbounded<AgentRunEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        IReadOnlyList<AgentRunEvent> replay;
        lock (state.Gate)
        {
            replay = state.Events
                .Where(evt => evt.Sequence > afterSequence)
                .OrderBy(evt => evt.Sequence)
                .ToList();

            if (state.IsTerminal)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                state.Subscribers.Add(channel.Writer);
            }
        }

        return new AgentRunEventSubscription(
            runId,
            replay,
            channel.Reader,
            () =>
            {
                lock (state.Gate)
                {
                    state.Subscribers.Remove(channel.Writer);
                }

                channel.Writer.TryComplete();
            });
    }

    public AgentRunReplayResult? ReplayRaw(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return null;
        }

        lock (state.Gate)
        {
            var events = state.Events.OrderBy(evt => evt.Sequence).ToList();
            return BuildReplayResult(state, events);
        }
    }

    public AgentRunReplayResult? Replay(string runId)
    {
        return ReplayRaw(runId);
    }

    public AgentRunReplayResult? ReplayLatest(string? ownerHash = null)
    {
        var normalizedOwner = NormalizeOwnerHash(ownerHash);
        var summaries = _store.LoadSummaries().ToList();
        foreach (var state in _runs.Values)
        {
            lock (state.Gate)
            {
                summaries.Add(state.ToSummary());
            }
        }

        foreach (var summary in summaries
            .GroupBy(item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .Where(summary => IsSummaryOwner(summary, normalizedOwner))
            .OrderByDescending(summary => summary.UpdatedAt))
        {
            var replay = Replay(summary.RunId);
            if (replay != null)
            {
                return replay;
            }
        }

        return null;
    }

    public CancellationToken GetCancellationToken(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return CancellationToken.None;
        }

        return GetOrRestoreState(runId.Trim())?.Cancellation.Token ?? CancellationToken.None;
    }

    public bool TryCancelToken(string runId)
    {
        var state = GetOrRestoreState(runId);
        if (state == null)
        {
            return false;
        }

        return TryCancelStateToken(state);
    }

    private static bool TryCancelStateToken(AgentRunState state)
    {
        try
        {
            state.Cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public bool IsRunOwner(string runId, string? ownerHash)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return false;
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return false;
        }

        var normalizedOwner = NormalizeOwnerHash(ownerHash);
        lock (state.Gate)
        {
            if (string.IsNullOrWhiteSpace(state.OwnerHash))
            {
                return string.IsNullOrWhiteSpace(normalizedOwner);
            }

            return string.Equals(state.OwnerHash, normalizedOwner, StringComparison.Ordinal);
        }
    }

    public string? IssueStreamToken(string runId, string? ownerHash, TimeSpan? ttl = null)
    {
        if (!IsRunOwner(runId, ownerHash))
        {
            return null;
        }

        var effectiveTtl = ttl.GetValueOrDefault(TimeSpan.FromSeconds(45));
        if (effectiveTtl <= TimeSpan.Zero || effectiveTtl > TimeSpan.FromSeconds(60))
        {
            effectiveTtl = TimeSpan.FromSeconds(45);
        }

        var token = CreateOpaqueToken();
        _streamTokens[token] = new AgentRunStreamToken(
            runId.Trim(),
            NormalizeOwnerHash(ownerHash),
            UtcNowProvider().Add(effectiveTtl));
        return token;
    }

    public AgentRunStreamTokenValidationResult ValidateStreamToken(string runId, string? token, bool consume = true)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(token))
        {
            return new AgentRunStreamTokenValidationResult(false, FailureReason: "missing_token");
        }

        var normalizedRunId = runId.Trim();
        var normalizedToken = token.Trim();
        if (!_streamTokens.TryGetValue(normalizedToken, out var record))
        {
            return new AgentRunStreamTokenValidationResult(false, FailureReason: "unknown_token");
        }

        if (record.ExpiresAt <= UtcNowProvider())
        {
            _streamTokens.TryRemove(normalizedToken, out _);
            return new AgentRunStreamTokenValidationResult(false, FailureReason: "expired_token");
        }

        if (!string.Equals(record.RunId, normalizedRunId, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentRunStreamTokenValidationResult(false, FailureReason: "run_mismatch");
        }

        if (!IsRunOwner(normalizedRunId, record.OwnerHash))
        {
            return new AgentRunStreamTokenValidationResult(false, FailureReason: "owner_mismatch");
        }

        if (consume && !_streamTokens.TryRemove(normalizedToken, out _))
        {
            return new AgentRunStreamTokenValidationResult(false, FailureReason: "token_consumed");
        }

        return new AgentRunStreamTokenValidationResult(true, record.OwnerHash);
    }

    private AgentRunEvent? AppendTerminal(
        string? runId,
        AgentRunEventDraft draft,
        AgentRunTerminalReservationResult? reservation = null)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var targetStatus = NormalizeTerminalStatus(draft.Status);
        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            return null;
        }

        var state = GetOrRestoreState(runId.Trim());
        if (state == null)
        {
            return null;
        }

        AgentRunEvent evt;
        List<ChannelWriter<AgentRunEvent>> subscribers;
        lock (state.Gate)
        {
            if (state.IsTerminal)
            {
                return null;
            }

            if (!CanCommitTerminalLocked(state, targetStatus, reservation))
            {
                return null;
            }

            evt = BuildSafeEvent(state.RunId, state.NextSequence(), draft);
            state.Events.Add(evt);
            UpdateSummaryFromEvent(state, evt);
            state.IsTerminal = true;
            state.TerminalIntentStatus = targetStatus;
            state.TerminalIntentState = targetStatus;
            if (state.TerminalIntent != null)
            {
                state.TerminalIntent = state.TerminalIntent with
                {
                    Phase = "TerminalCommitted"
                };
            }
            _store.AppendEvent(evt);
            _store.AppendSummary(state.ToSummary());
            subscribers = state.Subscribers.ToList();
            state.Subscribers.Clear();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.TryWrite(evt);
            subscriber.TryComplete();
        }

        return evt;
    }

    private static bool CanCommitTerminalLocked(
        AgentRunState state,
        string targetStatus,
        AgentRunTerminalReservationResult? reservation)
    {
        if (reservation != null)
        {
            return reservation.Outcome == AgentRunTerminalReservationOutcome.Acquired &&
                   !string.IsNullOrWhiteSpace(reservation.ReservationId) &&
                   string.Equals(reservation.TargetStatus, targetStatus, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(state.TerminalIntentStatus, targetStatus, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(state.TerminalReservationId, reservation.ReservationId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(state.TerminalIntentStatus))
        {
            return state.TerminalIntent != null &&
                   string.Equals(state.TerminalIntentStatus, targetStatus, StringComparison.OrdinalIgnoreCase);
        }

        state.TerminalIntentStatus = targetStatus;
        state.TerminalIntentState = ToPendingTerminalIntent(targetStatus);
        state.TerminalReservationId = Guid.NewGuid().ToString("N");
        return true;
    }

    private AgentRunEvent BuildSafeEvent(string runId, long sequence, AgentRunEventDraft draft)
    {
        var payload = _redactor.RedactObject(draft.Payload);
        var title = _redactor.RedactText(draft.Title);
        var summary = _redactor.RedactText(draft.Summary);
        var evt = new AgentRunEvent
        {
            RunId = runId,
            Sequence = sequence,
            Timestamp = UtcNowProvider(),
            EventType = _redactor.RedactText(draft.EventType),
            Stage = _redactor.RedactText(draft.Stage),
            Title = title,
            Summary = summary,
            Status = _redactor.RedactText(draft.Status),
            Payload = payload,
            MetadataOnly = true,
            RedactionPass = true
        };

        if (_redactor.IsRedactionSafe(evt))
        {
            return evt;
        }

        var fallback = evt with
        {
            Title = "Redacted event",
            Summary = "Unsafe metadata was removed before publishing this AgentRun event.",
            Payload = new
            {
                redacted = true,
                eventTypeRedacted = true,
                metadataOnly = true
            },
            RedactionPass = true
        };

        return _redactor.IsRedactionSafe(fallback)
            ? fallback
            : fallback with
            {
                Payload = null
            };
    }

    private AgentRunReplayResult BuildReplayResult(AgentRunState state, IReadOnlyList<AgentRunEvent> events)
    {
        var summary = state.ToSummary();
        var snapshot = new AgentRunReplaySnapshot
        {
            StorageVersion = AgentRunEventStore.StorageVersion,
            RunId = state.RunId,
            GeneratedAt = UtcNowProvider(),
            FirstSequence = events.Count == 0 ? 0 : events.Min(evt => evt.Sequence),
            LastSequence = events.Count == 0 ? 0 : events.Max(evt => evt.Sequence),
            EventCount = events.Count,
            MetadataOnly = true,
            RedactionPass = summary.RedactionPass && events.All(evt => evt.RedactionPass),
            Events = events
        };
        var diagnostics = new AgentRunReplayDiagnostics
        {
            RunId = state.RunId,
            EventCount = events.Count,
            DuplicateEventCount = state.DuplicateEventCount,
            DroppedEventCount = state.DroppedEventCount,
            StaleEventCount = state.StaleEventCount,
            MetadataOnly = true,
            RedactionPass = snapshot.RedactionPass
        };

        return new AgentRunReplayResult(summary, events)
        {
            Snapshot = snapshot,
            Diagnostics = diagnostics
        };
    }

    private AgentRunState? GetOrRestoreState(string runId)
    {
        if (_runs.TryGetValue(runId, out var existing))
        {
            return existing;
        }

        var summary = _store.LoadSummary(runId);
        var events = _store.LoadEvents(runId);
        if (summary == null && events.Count == 0)
        {
            return null;
        }

        var restored = new AgentRunState(runId, summary?.CreatedAt ?? events.Min(evt => evt.Timestamp), summary?.OwnerHash)
        {
            UpdatedAt = summary?.UpdatedAt ?? events.Max(evt => evt.Timestamp),
            Status = summary?.Status ?? events.LastOrDefault()?.Status ?? AgentRunEventStatuses.Completed,
            Title = summary?.Title ?? events.LastOrDefault()?.Title ?? string.Empty,
            Summary = summary?.Summary ?? events.LastOrDefault()?.Summary ?? string.Empty,
            FirstFixRecommendation = summary?.FirstFixRecommendation ?? string.Empty,
            IsTerminal = IsTerminalStatus(summary?.Status) || events.Any(evt => IsTerminalEvent(evt.EventType)),
            EventCount = summary?.EventCount ?? events.Count,
            DuplicateEventCount = summary?.DuplicateEventCount ?? 0,
            DroppedEventCount = summary?.DroppedEventCount ?? 0,
            StaleEventCount = summary?.StaleEventCount ?? 0,
            LastSequence = Math.Max(summary?.LastSequence ?? 0, events.Count == 0 ? 0 : events.Max(evt => evt.Sequence)),
            RedactionPass = summary?.RedactionPass ?? events.All(evt => evt.RedactionPass)
        };
        if (summary?.TerminalIntent != null)
        {
            restored.TerminalIntent = summary.TerminalIntent;
            restored.TerminalIntentStatus = NormalizeTerminalStatus(summary.TerminalIntent.TargetStatus);
            restored.TerminalIntentState = restored.TerminalIntentStatus == null
                ? AgentRunTerminalIntents.None
                : ToPendingTerminalIntent(restored.TerminalIntentStatus);
        }

        if (restored.IsTerminal)
        {
            var restoredTerminalStatus = NormalizeTerminalStatus(restored.Status);
            restored.TerminalIntentStatus = restoredTerminalStatus;
            restored.TerminalIntentState = restoredTerminalStatus ?? AgentRunTerminalIntents.None;
            if (restored.TerminalIntent != null)
            {
                restored.TerminalIntent = restored.TerminalIntent with
                {
                    Phase = "TerminalCommitted"
                };
            }
        }

        restored.Events.AddRange(events.OrderBy(evt => evt.Sequence));
        restored = _runs.GetOrAdd(runId, restored);
        return restored;
    }

    private static void UpdateSummaryFromEvent(AgentRunState state, AgentRunEvent evt)
    {
        state.UpdatedAt = evt.Timestamp;
        state.LastSequence = evt.Sequence;
        state.EventCount++;
        state.RedactionPass &= evt.RedactionPass;

        if (!string.IsNullOrWhiteSpace(evt.Title))
        {
            state.Title = evt.Title;
        }

        if (!string.IsNullOrWhiteSpace(evt.Summary))
        {
            state.Summary = evt.Summary;
        }

        if (IsTerminalEvent(evt.EventType))
        {
            state.Status = evt.Status;
        }
        else if (string.Equals(evt.EventType, AgentRunEventTypes.RunStarted, StringComparison.OrdinalIgnoreCase))
        {
            state.Status = AgentRunEventStatuses.Running;
        }

        var firstFix = TryExtractFirstFixRecommendation(evt.Payload);
        if (!string.IsNullOrWhiteSpace(firstFix))
        {
            state.FirstFixRecommendation = firstFix;
        }
    }

    private static bool IsTerminalEvent(string? eventType)
    {
        return string.Equals(eventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalStatus(string? status)
    {
        return string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeTerminalStatus(string? status)
    {
        if (string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Completed;
        }

        if (string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Failed;
        }

        if (string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Cancelled;
        }

        return null;
    }

    private static string ToPendingTerminalIntent(string terminalStatus)
    {
        if (string.Equals(terminalStatus, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunTerminalIntents.Completing;
        }

        if (string.Equals(terminalStatus, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunTerminalIntents.Failing;
        }

        return AgentRunTerminalIntents.Cancelling;
    }

    private static string NormalizeOwnerHash(string? ownerHash)
    {
        return string.IsNullOrWhiteSpace(ownerHash)
            ? string.Empty
            : ownerHash.Trim();
    }

    private static bool IsSummaryOwner(AgentRunSummary summary, string normalizedOwner)
    {
        return string.Equals(summary.OwnerHash ?? string.Empty, normalizedOwner, StringComparison.Ordinal);
    }

    private static string CreateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string BuildBrief(string? description)
    {
        var normalized = string.Join(
            " ",
            (description ?? string.Empty)
            .Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "I will create a metadata-only Vision Agent run and report progress as public events.";
        }

        var clipped = normalized.Length <= 140
            ? normalized
            : normalized[..140] + "...";
        return $"I will turn this request into a safe Vision Agent workflow draft and stream each public progress step: {clipped}";
    }

    private static string TryExtractFirstFixRecommendation(object? payload)
    {
        if (payload == null)
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, AgentRunEventJson.Options));
            return TryFindString(doc.RootElement, "firstFixRecommendation") ??
                   TryFindString(doc.RootElement, "repairTarget") ??
                   string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static object BuildTerminalDiagnosticPayload(object? payload, string firstFixRecommendation)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["firstFixRecommendation"] = firstFixRecommendation
        };

        if (payload == null)
        {
            return result;
        }

        result["diagnostic"] = payload;
        try
        {
            if (JsonSerializer.SerializeToNode(payload, AgentRunEventJson.Options) is JsonObject node)
            {
                foreach (var property in node)
                {
                    if (result.ContainsKey(property.Key))
                    {
                        continue;
                    }

                    result[property.Key] = property.Value?.Deserialize<object>(AgentRunEventJson.Options);
                }
            }
        }
        catch (JsonException)
        {
            // Keep the compatibility diagnostic payload even if it cannot be flattened.
        }

        return result;
    }

    private static string? TryFindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = TryFindString(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private sealed class AgentRunState
    {
        public AgentRunState(string runId, DateTimeOffset createdAt, string? ownerHash)
        {
            RunId = runId;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
            OwnerHash = NormalizeOwnerHash(ownerHash);
        }

        public object Gate { get; } = new();
        public string RunId { get; }
        public string OwnerHash { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string Status { get; set; } = AgentRunEventStatuses.Running;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string FirstFixRecommendation { get; set; } = string.Empty;
        public long LastSequence { get; set; }
        public int EventCount { get; set; }
        public int DuplicateEventCount { get; set; }
        public int DroppedEventCount { get; set; }
        public int StaleEventCount { get; set; }
        public bool RedactionPass { get; set; } = true;
        public bool IsTerminal { get; set; }
        public string? TerminalIntentStatus { get; set; }
        public string TerminalIntentState { get; set; } = AgentRunTerminalIntents.None;
        public string? TerminalReservationId { get; set; }
        public AgentRunTerminalIntentRecord? TerminalIntent { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public List<AgentRunEvent> Events { get; } = new();
        public List<ChannelWriter<AgentRunEvent>> Subscribers { get; } = new();

        public long NextSequence()
        {
            LastSequence++;
            return LastSequence;
        }

        public AgentRunSummary ToSummary()
        {
            return new AgentRunSummary
            {
                RunId = RunId,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Status = Status,
                Title = Title,
                Summary = Summary,
                FirstFixRecommendation = FirstFixRecommendation,
                LastSequence = LastSequence,
                EventCount = EventCount,
                DuplicateEventCount = DuplicateEventCount,
                DroppedEventCount = DroppedEventCount,
                StaleEventCount = StaleEventCount,
                OwnerHash = OwnerHash,
                TerminalIntent = TerminalIntent,
                MetadataOnly = true,
                RedactionPass = RedactionPass,
                Payload = new
                {
                    storageVersion = AgentRunEventStore.StorageVersion,
                    publicDiagnosticsOnly = true
                }
            };
        }
    }

    private static class AgentRunTerminalIntents
    {
        public const string None = "none";
        public const string Completing = "completing";
        public const string Failing = "failing";
        public const string Cancelling = "cancelling";
    }

    private sealed record AgentRunStreamToken(
        string RunId,
        string OwnerHash,
        DateTimeOffset ExpiresAt);
}

public sealed class AgentRunEventSubscription : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    public AgentRunEventSubscription(
        string runId,
        IReadOnlyList<AgentRunEvent> replayEvents,
        ChannelReader<AgentRunEvent> liveEvents,
        Action dispose)
    {
        RunId = runId;
        ReplayEvents = replayEvents;
        LiveEvents = liveEvents;
        _dispose = dispose;
    }

    public string RunId { get; }
    public IReadOnlyList<AgentRunEvent> ReplayEvents { get; }
    public ChannelReader<AgentRunEvent> LiveEvents { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }
}
