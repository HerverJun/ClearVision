using System.Text;
using System.Text.Json;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public static class VisionAgentBuildProjectionStatuses
{
    public const string Pending = "pending";
    public const string Projected = "projected";
    public const string Failed = "failed";
}

public enum VisionAgentBuildProjectionBeginStatus
{
    Started,
    AlreadyProjected,
    InProgress
}

public sealed record VisionAgentBuildProjectionBeginResult(
    VisionAgentBuildProjectionBeginStatus Status,
    VisionAgentBuildProjectionCheckpoint? Checkpoint);

public sealed record VisionAgentBuildProjectionCheckpoint
{
    public const string StorageVersion = "agent-run-build-projections.jsonl.v1";

    public string Version { get; init; } = StorageVersion;
    public string RunId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public long TerminalSequence { get; init; }
    public string TerminalType { get; init; } = string.Empty;
    public string TerminalMutationId { get; init; } = string.Empty;
    public string PayloadFingerprint { get; init; } = string.Empty;
    public long? ExpectedWorkspaceRevision { get; init; }
    public string Identity { get; init; } = string.Empty;
    public string Status { get; init; } = VisionAgentBuildProjectionStatuses.Pending;
    public int Attempts { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string HostInstanceId { get; init; } = string.Empty;
    public string PublicErrorCode { get; init; } = string.Empty;
    public string PublicErrorMessage { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
    public bool RedactionPass { get; init; } = true;
}

public interface IVisionAgentBuildProjectionJournal
{
    string JournalPath { get; }

    string ResolveSessionId(
        string runId,
        long terminalSequence,
        string terminalType,
        string proposedSessionId);

    VisionAgentBuildProjectionBeginResult Begin(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType,
        string terminalMutationId = "",
        string payloadFingerprint = "",
        long? expectedWorkspaceRevision = null,
        string identity = "",
        string hostInstanceId = "");

    void MarkProjected(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType);

    void MarkFailed(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType,
        Exception exception);

    IReadOnlyList<VisionAgentBuildProjectionCheckpoint> LoadCheckpoints();

    void Cleanup(DateTimeOffset now, TimeSpan projectedRetention);
}

public sealed class VisionAgentBuildProjectionJournal : IVisionAgentBuildProjectionJournal
{
    private readonly object _gate = new();
    private readonly AgentRunEventRedactor _redactor;
    private readonly Dictionary<string, VisionAgentBuildProjectionCheckpoint> _latestByProjectionKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sessionByTerminalKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlightTerminals =
        new(StringComparer.OrdinalIgnoreCase);

    public VisionAgentBuildProjectionJournal(
        AgentRunEventStore eventStore,
        AgentRunEventRedactor redactor)
    {
        _redactor = redactor;
        JournalPath = Path.Combine(eventStore.DirectoryPath, "agent_run_build_projections.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        ReloadUnsafe();
        Cleanup(DateTimeOffset.UtcNow, TimeSpan.FromDays(30));
    }

    public string JournalPath { get; }

    public string ResolveSessionId(
        string runId,
        long terminalSequence,
        string terminalType,
        string proposedSessionId)
    {
        var terminalKey = TerminalKey(runId, terminalSequence, terminalType);
        lock (_gate)
        {
            return _sessionByTerminalKey.TryGetValue(terminalKey, out var existing)
                ? existing
                : proposedSessionId;
        }
    }

    public VisionAgentBuildProjectionBeginResult Begin(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType,
        string terminalMutationId = "",
        string payloadFingerprint = "",
        long? expectedWorkspaceRevision = null,
        string identity = "",
        string hostInstanceId = "")
    {
        var terminalKey = TerminalKey(runId, terminalSequence, terminalType);
        var projectionKey = ProjectionKey(runId, sessionId, terminalSequence, terminalType);

        lock (_gate)
        {
            if (_sessionByTerminalKey.TryGetValue(terminalKey, out var boundSessionId) &&
                !string.Equals(boundSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                projectionKey = ProjectionKey(runId, boundSessionId, terminalSequence, terminalType);
                sessionId = boundSessionId;
            }

            if (_latestByProjectionKey.TryGetValue(projectionKey, out var latest) &&
                string.Equals(latest.Status, VisionAgentBuildProjectionStatuses.Projected, StringComparison.OrdinalIgnoreCase))
            {
                return new VisionAgentBuildProjectionBeginResult(
                    VisionAgentBuildProjectionBeginStatus.AlreadyProjected,
                    latest);
            }

            if (_inFlightTerminals.Contains(terminalKey))
            {
                return new VisionAgentBuildProjectionBeginResult(
                    VisionAgentBuildProjectionBeginStatus.InProgress,
                    latest);
            }

            var checkpoint = new VisionAgentBuildProjectionCheckpoint
            {
                RunId = runId,
                SessionId = sessionId,
                TerminalSequence = terminalSequence,
                TerminalType = terminalType,
                TerminalMutationId = terminalMutationId?.Trim() ?? string.Empty,
                PayloadFingerprint = payloadFingerprint?.Trim() ?? string.Empty,
                ExpectedWorkspaceRevision = expectedWorkspaceRevision,
                Identity = identity?.Trim() ?? string.Empty,
                Status = VisionAgentBuildProjectionStatuses.Pending,
                Attempts = (latest?.Attempts ?? 0) + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
                HostInstanceId = hostInstanceId?.Trim() ?? string.Empty
            };

            AppendUnsafe(checkpoint);
            _latestByProjectionKey[projectionKey] = checkpoint;
            _sessionByTerminalKey[terminalKey] = sessionId;
            _inFlightTerminals.Add(terminalKey);
            return new VisionAgentBuildProjectionBeginResult(
                VisionAgentBuildProjectionBeginStatus.Started,
                checkpoint);
        }
    }

    public void MarkProjected(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType)
    {
        Complete(
            runId,
            sessionId,
            terminalSequence,
            terminalType,
            VisionAgentBuildProjectionStatuses.Projected,
            publicErrorCode: string.Empty,
            publicErrorMessage: string.Empty);
    }

    public void MarkFailed(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType,
        Exception exception)
    {
        Complete(
            runId,
            sessionId,
            terminalSequence,
            terminalType,
            VisionAgentBuildProjectionStatuses.Failed,
            exception.GetType().Name,
            exception.Message);
    }

    public IReadOnlyList<VisionAgentBuildProjectionCheckpoint> LoadCheckpoints()
    {
        lock (_gate)
        {
            return _latestByProjectionKey.Values
                .OrderBy(item => item.RunId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TerminalSequence)
                .ThenBy(item => item.TerminalType, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Cleanup(DateTimeOffset now, TimeSpan projectedRetention)
    {
        lock (_gate)
        {
            var cutoff = now - projectedRetention;
            var retained = _latestByProjectionKey.Values
                .Where(item =>
                    !string.Equals(item.Status, VisionAgentBuildProjectionStatuses.Projected, StringComparison.OrdinalIgnoreCase) ||
                    item.UpdatedAt >= cutoff)
                .OrderBy(item => item.UpdatedAt)
                .ToList();

            RewriteUnsafe(retained);
            ReloadUnsafe();
        }
    }

    private void Complete(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType,
        string status,
        string publicErrorCode,
        string publicErrorMessage)
    {
        var terminalKey = TerminalKey(runId, terminalSequence, terminalType);
        var projectionKey = ProjectionKey(runId, sessionId, terminalSequence, terminalType);

        lock (_gate)
        {
            _latestByProjectionKey.TryGetValue(projectionKey, out var latest);
            var checkpoint = new VisionAgentBuildProjectionCheckpoint
            {
                RunId = runId,
                SessionId = sessionId,
                TerminalSequence = terminalSequence,
                TerminalType = terminalType,
                TerminalMutationId = latest?.TerminalMutationId ?? string.Empty,
                PayloadFingerprint = latest?.PayloadFingerprint ?? string.Empty,
                ExpectedWorkspaceRevision = latest?.ExpectedWorkspaceRevision,
                Identity = latest?.Identity ?? string.Empty,
                Status = status,
                Attempts = latest?.Attempts ?? 1,
                UpdatedAt = DateTimeOffset.UtcNow,
                HostInstanceId = latest?.HostInstanceId ?? string.Empty,
                PublicErrorCode = _redactor.RedactText(publicErrorCode),
                PublicErrorMessage = _redactor.RedactText(publicErrorMessage)
            };

            AppendUnsafe(checkpoint);
            _latestByProjectionKey[projectionKey] = checkpoint;
            _sessionByTerminalKey[terminalKey] = sessionId;
            _inFlightTerminals.Remove(terminalKey);
        }
    }

    private void ReloadUnsafe()
    {
        _latestByProjectionKey.Clear();
        _sessionByTerminalKey.Clear();
        if (!File.Exists(JournalPath))
        {
            return;
        }

        foreach (var line in File.ReadLines(JournalPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var checkpoint = JsonSerializer.Deserialize<VisionAgentBuildProjectionCheckpoint>(
                    line,
                    AgentRunEventJson.Options);
                if (checkpoint == null ||
                    string.IsNullOrWhiteSpace(checkpoint.RunId) ||
                    string.IsNullOrWhiteSpace(checkpoint.SessionId) ||
                    checkpoint.TerminalSequence <= 0 ||
                    string.IsNullOrWhiteSpace(checkpoint.TerminalType))
                {
                    continue;
                }

                var projectionKey = ProjectionKey(
                    checkpoint.RunId,
                    checkpoint.SessionId,
                    checkpoint.TerminalSequence,
                    checkpoint.TerminalType);
                _latestByProjectionKey[projectionKey] = checkpoint;
                _sessionByTerminalKey[TerminalKey(
                    checkpoint.RunId,
                    checkpoint.TerminalSequence,
                    checkpoint.TerminalType)] = checkpoint.SessionId;
            }
            catch (JsonException)
            {
                // Keep append-only storage usable if a previous process wrote a corrupt line.
            }
        }
    }

    private void AppendUnsafe(VisionAgentBuildProjectionCheckpoint checkpoint)
    {
        if (!_redactor.IsRedactionSafe(checkpoint))
        {
            throw new InvalidOperationException("Build terminal projection journal rejected unsafe metadata.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.AppendAllText(
            JournalPath,
            JsonSerializer.Serialize(checkpoint, AgentRunEventJson.Options) + Environment.NewLine,
            Encoding.UTF8);
    }

    private void RewriteUnsafe(IReadOnlyList<VisionAgentBuildProjectionCheckpoint> retained)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        var lines = retained
            .Where(item => _redactor.IsRedactionSafe(item))
            .Select(item => JsonSerializer.Serialize(item, AgentRunEventJson.Options));
        File.WriteAllLines(JournalPath, lines, Encoding.UTF8);
    }

    private static string ProjectionKey(
        string runId,
        string sessionId,
        long terminalSequence,
        string terminalType)
    {
        return $"{runId.Trim()}:{sessionId.Trim()}:{terminalSequence}:{terminalType.Trim()}";
    }

    private static string TerminalKey(
        string runId,
        long terminalSequence,
        string terminalType)
    {
        return $"{runId.Trim()}:{terminalSequence}:{terminalType.Trim()}";
    }
}
