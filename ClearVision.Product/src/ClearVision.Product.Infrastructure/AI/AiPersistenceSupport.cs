using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI;

internal enum AiPersistenceStage
{
    ModelCandidateCleanupStarted,
    ModelCandidateStarted,
    ModelSecretCandidateWrite,
    ModelSecretsPrepared,
    ModelDocumentPrepared,
    ModelCommitStarted,
    ModelCommitCompleted,
    JsonCandidatePrepared,
    JsonCommitStarted,
    JsonCommitCompleted,
    CleanupStarted
}

internal interface IAiPersistenceFaultInjector
{
    void OnStage(AiPersistenceStage stage, string authority, string path);
}

internal sealed class NoOpAiPersistenceFaultInjector : IAiPersistenceFaultInjector
{
    public static NoOpAiPersistenceFaultInjector Instance { get; } = new();

    private NoOpAiPersistenceFaultInjector()
    {
    }

    public void OnStage(AiPersistenceStage stage, string authority, string path)
    {
    }
}

/// <summary>
/// Test-only signal that models an abrupt process stop. Production code never throws this type.
/// Recovery deliberately leaves candidates in place when this exception is observed.
/// </summary>
internal sealed class AiPersistenceInterruptionException : IOException
{
    public AiPersistenceInterruptionException(string stage)
        : base($"Simulated AI persistence interruption at {stage}.")
    {
    }
}

public sealed class AiConfigPersistenceException : Exception
{
    internal AiConfigPersistenceException(string errorCode, string stage, Exception innerException)
        : base("AI model configuration could not be durably committed.", innerException)
    {
        ErrorCode = errorCode;
        Stage = stage;
    }

    public string ErrorCode { get; }

    public string Stage { get; }

    public bool Retryable => true;

    public string PublicMessage =>
        "AI model configuration was not durably committed. No uncommitted in-memory model state was activated.";
}

internal sealed class AiAuxiliaryPersistenceException : Exception
{
    public AiAuxiliaryPersistenceException(string errorCode, string authority, string operation, Exception innerException)
        : base("Auxiliary AI evidence could not be durably persisted.", innerException)
    {
        ErrorCode = errorCode;
        Authority = authority;
        Operation = operation;
    }

    public string ErrorCode { get; }

    public string Authority { get; }

    public string Operation { get; }
}

public sealed record AiPersistenceRetryableEvent(
    string Authority,
    string Operation,
    string ErrorCode,
    DateTimeOffset OccurredAtUtc,
    bool Retryable = true);

public sealed record AiAuxiliaryPersistenceHealthSnapshot(
    bool Degraded,
    IReadOnlyList<AiPersistenceRetryableEvent> ActiveFailures,
    IReadOnlyList<AiPersistenceRetryableEvent> RecentRetryableEvents,
    DateTimeOffset? LastRecoveredAtUtc);

/// <summary>
/// Bounded, secret-free health evidence for fail-soft auxiliary AI persistence.
/// </summary>
public sealed class AiAuxiliaryPersistenceHealth
{
    private const int MaxRecentEvents = 32;
    private readonly object _gate = new();
    private readonly Dictionary<string, AiPersistenceRetryableEvent> _activeFailures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<AiPersistenceRetryableEvent> _recentEvents = new();
    private DateTimeOffset? _lastRecoveredAtUtc;

    internal void ReportDegraded(string authority, string operation, string errorCode)
    {
        var failure = new AiPersistenceRetryableEvent(
            authority,
            operation,
            errorCode,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            _activeFailures[authority] = failure;
            _recentEvents.Enqueue(failure);
            while (_recentEvents.Count > MaxRecentEvents)
            {
                _recentEvents.Dequeue();
            }
        }
    }

    internal void ReportRecovered(string authority)
    {
        lock (_gate)
        {
            if (_activeFailures.Remove(authority))
            {
                _lastRecoveredAtUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    public AiAuxiliaryPersistenceHealthSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new AiAuxiliaryPersistenceHealthSnapshot(
                _activeFailures.Count > 0,
                _activeFailures.Values.OrderBy(item => item.Authority, StringComparer.OrdinalIgnoreCase).ToArray(),
                _recentEvents.ToArray(),
                _lastRecoveredAtUtc);
        }
    }
}

internal readonly record struct AiJsonMutation<TResult>(bool Changed, TResult Result)
{
    public static AiJsonMutation<TResult> Persist(TResult result) => new(true, result);

    public static AiJsonMutation<TResult> NoChange(TResult result) => new(false, result);
}

/// <summary>
/// Serializes a complete load -> mutate -> candidate persist -> atomic commit cycle for one JSON document.
/// </summary>
internal sealed class AiJsonMutationAuthority<TData>
    where TData : class
{
    private readonly object _gate;
    private readonly string _authority;
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly Func<TData> _createEmpty;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IAiPersistenceFaultInjector _faultInjector;

    public AiJsonMutationAuthority(
        string authority,
        string filePath,
        Func<TData> createEmpty,
        JsonSerializerOptions jsonOptions,
        IAiPersistenceFaultInjector? faultInjector = null)
    {
        _authority = authority;
        _filePath = filePath;
        _backupPath = filePath + ".previous";
        _gate = AiPersistenceFileOperations.GetMutationGate(filePath);
        _createEmpty = createEmpty;
        _jsonOptions = jsonOptions;
        _faultInjector = faultInjector ?? NoOpAiPersistenceFaultInjector.Instance;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_gate)
        {
            CleanupCandidateResidue();
        }
    }

    public TResult Read<TResult>(Func<TData, TResult> reader)
    {
        lock (_gate)
        {
            return reader(LoadLocked());
        }
    }

    public TResult Mutate<TResult>(string operation, Func<TData, AiJsonMutation<TResult>> mutation)
    {
        lock (_gate)
        {
            var data = LoadLocked();
            var outcome = mutation(data);
            if (outcome.Changed)
            {
                PersistLocked(data, operation);
            }

            return outcome.Result;
        }
    }

    private TData LoadLocked()
    {
        if (!File.Exists(_filePath))
        {
            return _createEmpty();
        }

        try
        {
            return DeserializeFile(_filePath);
        }
        catch (Exception activeError) when (activeError is not AiPersistenceInterruptionException)
        {
            if (File.Exists(_backupPath))
            {
                try
                {
                    var recovered = DeserializeFile(_backupPath);
                    RestoreBackupLocked();
                    return recovered;
                }
                catch (Exception backupError) when (backupError is not AiPersistenceInterruptionException)
                {
                    throw new AiAuxiliaryPersistenceException(
                        "AI_AUXILIARY_RECOVERY_FAILED",
                        _authority,
                        "load",
                        new AggregateException(activeError, backupError));
                }
            }

            throw new AiAuxiliaryPersistenceException(
                "AI_AUXILIARY_READ_FAILED",
                _authority,
                "load",
                activeError);
        }
    }

    private TData DeserializeFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TData>(json, _jsonOptions)
            ?? throw new JsonException($"{_authority} document deserialized to null.");
    }

    private void PersistLocked(TData data, string operation)
    {
        var candidatePath = $"{_filePath}.{Guid.NewGuid():N}.candidate";
        var committed = false;
        var interrupted = false;
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            AiPersistenceFileOperations.WriteAllTextDurable(candidatePath, json);
            _faultInjector.OnStage(AiPersistenceStage.JsonCandidatePrepared, _authority, candidatePath);
            _faultInjector.OnStage(AiPersistenceStage.JsonCommitStarted, _authority, _filePath);
            AiPersistenceFileOperations.CommitCandidate(candidatePath, _filePath, _backupPath);
            committed = true;
            _faultInjector.OnStage(AiPersistenceStage.JsonCommitCompleted, _authority, _filePath);
        }
        catch (AiPersistenceInterruptionException)
        {
            interrupted = true;
            throw;
        }
        catch (Exception ex)
        {
            throw new AiAuxiliaryPersistenceException(
                "AI_AUXILIARY_PERSISTENCE_FAILED",
                _authority,
                operation,
                ex);
        }
        finally
        {
            if (!committed && !interrupted)
            {
                AiPersistenceFileOperations.TryDeleteFile(candidatePath);
            }
        }
    }

    private void RestoreBackupLocked()
    {
        var candidatePath = $"{_filePath}.{Guid.NewGuid():N}.recovery.candidate";
        try
        {
            AiPersistenceFileOperations.WriteAllTextDurable(candidatePath, File.ReadAllText(_backupPath));
            File.Move(candidatePath, _filePath, overwrite: true);
        }
        finally
        {
            AiPersistenceFileOperations.TryDeleteFile(candidatePath);
        }
    }

    private void CleanupCandidateResidue()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var prefix = Path.GetFileName(_filePath) + ".";
        foreach (var path in Directory.EnumerateFiles(directory, prefix + "*.candidate"))
        {
            AiPersistenceFileOperations.TryDeleteFile(path);
        }
    }
}

internal static class AiPersistenceFileOperations
{
    private static readonly ConcurrentDictionary<string, object> MutationGates =
        new(StringComparer.OrdinalIgnoreCase);

    public static object GetMutationGate(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return MutationGates.GetOrAdd(normalizedPath, static _ => new object());
    }

    public static void WriteAllTextDurable(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public static void CommitCandidate(string candidatePath, string activePath, string backupPath)
    {
        if (File.Exists(activePath))
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Replace(candidatePath, activePath, backupPath, ignoreMetadataErrors: true);
            return;
        }

        File.Move(candidatePath, activePath);
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Residue is uniquely named and non-authoritative. Startup cleanup retries it.
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Residue is uniquely named and non-authoritative. Startup cleanup retries it.
        }
    }
}
