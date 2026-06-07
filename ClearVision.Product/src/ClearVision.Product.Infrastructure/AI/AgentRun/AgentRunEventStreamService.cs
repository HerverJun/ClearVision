using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public interface IAgentRunEventStreamService
{
    AgentRunCreateResult CreateRun(string description, object? payload = null, string? ownerHash = null);
    AgentRunEvent? Append(string? runId, AgentRunEventDraft draft);
    AgentRunEvent? Complete(string? runId, string summary, object? payload = null);
    AgentRunEvent? Fail(string? runId, string summary, string firstFixRecommendation, object? payload = null);
    AgentRunEvent? Cancel(string? runId, string summary = "Vision Agent run cancelled by user.");
    AgentRunEventSubscription? Subscribe(string runId, long afterSequence);
    AgentRunReplayResult? Replay(string runId);
    CancellationToken GetCancellationToken(string? runId);
    bool TryCancelToken(string runId);
    bool IsRunOwner(string runId, string? ownerHash);
    string? IssueStreamToken(string runId, string? ownerHash, TimeSpan? ttl = null);
    AgentRunStreamTokenValidationResult ValidateStreamToken(string runId, string? token, bool consume = true);
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
                chainOfThoughtVisible = false,
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

    public AgentRunEvent? Complete(string? runId, string summary, object? payload = null)
    {
        return AppendTerminal(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunCompleted,
            Stage = "run",
            Title = "Run completed",
            Summary = summary,
            Status = AgentRunEventStatuses.Completed,
            Payload = payload
        });
    }

    public AgentRunEvent? Fail(string? runId, string summary, string firstFixRecommendation, object? payload = null)
    {
        return AppendTerminal(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunFailed,
            Stage = "run",
            Title = "Run failed",
            Summary = summary,
            Status = AgentRunEventStatuses.Failed,
            Payload = new
            {
                diagnostic = payload,
                firstFixRecommendation
            }
        });
    }

    public AgentRunEvent? Cancel(string? runId, string summary = "Vision Agent run cancelled by user.")
    {
        if (!string.IsNullOrWhiteSpace(runId))
        {
            TryCancelToken(runId);
        }

        return AppendTerminal(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.RunCancelled,
            Stage = "run",
            Title = "Run cancelled",
            Summary = summary,
            Status = AgentRunEventStatuses.Cancelled,
            Payload = new
            {
                firstFixRecommendation = "Submit the request again when you are ready to continue."
            }
        });
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

    public AgentRunReplayResult? Replay(string runId)
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
            return new AgentRunReplayResult(
                state.ToSummary(),
                state.Events.OrderBy(evt => evt.Sequence).ToList());
        }
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

    private AgentRunEvent? AppendTerminal(string? runId, AgentRunEventDraft draft)
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

        AgentRunEvent evt;
        List<ChannelWriter<AgentRunEvent>> subscribers;
        lock (state.Gate)
        {
            if (state.IsTerminal)
            {
                return null;
            }

            evt = BuildSafeEvent(state.RunId, state.NextSequence(), draft);
            state.Events.Add(evt);
            UpdateSummaryFromEvent(state, evt);
            state.IsTerminal = true;
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

    private AgentRunEvent BuildSafeEvent(string runId, long sequence, AgentRunEventDraft draft)
    {
        var payload = _redactor.RedactObject(draft.Payload);
        var title = _redactor.RedactText(draft.Title);
        var summary = _redactor.RedactText(draft.Summary);
        var evt = new AgentRunEvent
        {
            RunId = runId,
            Sequence = sequence,
            Timestamp = DateTimeOffset.UtcNow,
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
            LastSequence = Math.Max(summary?.LastSequence ?? 0, events.Count == 0 ? 0 : events.Max(evt => evt.Sequence)),
            RedactionPass = summary?.RedactionPass ?? events.All(evt => evt.RedactionPass)
        };
        restored.Events.AddRange(events.OrderBy(evt => evt.Sequence));
        return _runs.GetOrAdd(runId, restored);
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

    private static string NormalizeOwnerHash(string? ownerHash)
    {
        return string.IsNullOrWhiteSpace(ownerHash)
            ? string.Empty
            : ownerHash.Trim();
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
        public bool RedactionPass { get; set; } = true;
        public bool IsTerminal { get; set; }
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
                OwnerHash = OwnerHash,
                MetadataOnly = true,
                RedactionPass = RedactionPass,
                Payload = new
                {
                    storageVersion = AgentRunEventStore.StorageVersion,
                    chainOfThoughtVisible = false
                }
            };
        }
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
