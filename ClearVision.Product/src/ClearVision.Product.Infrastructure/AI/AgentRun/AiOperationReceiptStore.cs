using System.Text.Json;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public static class AiOperationKinds
{
    public const string SessionCreate = "session_create";
    public const string SessionDelete = "session_delete";
    public const string PlanRun = "plan_run";
    public const string BuildRun = "build_run";

    public static bool IsSupported(string? value) => value?.Trim().ToLowerInvariant() is
        SessionCreate or SessionDelete or PlanRun or BuildRun;

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public static class AiOperationStatuses
{
    public const string Pending = "pending";
    public const string Created = "created";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
}

public sealed record AiOperationReceipt
{
    public string OwnerHash { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public Guid ClientOperationId { get; init; }
    public string PayloadFingerprint { get; init; } = string.Empty;
    public string Status { get; init; } = AiOperationStatuses.Pending;
    public string SessionId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public AiProjectBaselineIdentity? ProjectBaseline { get; init; }
    public string PublicErrorCode { get; init; } = string.Empty;
    public string PublicMessage { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public enum AiOperationReservationOutcome
{
    Reserved,
    Existing,
    IdentityConflict,
    PersistenceFailed
}

public sealed record AiOperationReservationResult(
    AiOperationReservationOutcome Outcome,
    AiOperationReceipt? Receipt,
    string ErrorCode = "",
    string PublicMessage = "");

public interface IAiOperationReceiptStore
{
    AiOperationReservationResult Reserve(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        string payloadFingerprint,
        string? sessionId = null,
        AiProjectBaselineIdentity? projectBaseline = null);

    AiOperationReceipt? Get(string ownerHash, string kind, Guid clientOperationId);
    IReadOnlyList<AiOperationReceipt> Find(string ownerHash, Guid clientOperationId);

    AiOperationReceipt? MarkCreated(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        string? sessionId = null,
        string? runId = null,
        AiProjectBaselineIdentity? projectBaseline = null);

    AiOperationReceipt? MarkFailed(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        string publicErrorCode,
        string publicMessage,
        bool rejected = false,
        string? sessionId = null,
        string? runId = null,
        AiProjectBaselineIdentity? projectBaseline = null);
}

internal sealed class AiOperationReceiptDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<AiOperationReceipt> Receipts { get; set; } = [];
}

public sealed class AiOperationReceiptStore : IAiOperationReceiptStore
{
    public const string StorageRootEnvironmentVariable = "CV_AI_OPERATION_STORE_ROOT";
    private const int MaxReceipts = 1000;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _storagePath;
    private readonly Dictionary<OperationKey, AiOperationReceipt> _receipts = [];

    public AiOperationReceiptStore(string? storageRootPath = null)
    {
        var root = ResolveStorageRootPath(storageRootPath);
        Directory.CreateDirectory(root);
        _storagePath = Path.Combine(root, "ai_operation_receipts.json");
        Load();
    }

    public static string ResolveStorageRootPath(string? storageRootPath = null)
    {
        storageRootPath ??= Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(storageRootPath)
            ? ConversationalFlowService.ResolveStorageRootPath()
            : storageRootPath.Trim());
    }

    public AiOperationReservationResult Reserve(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        string payloadFingerprint,
        string? sessionId = null,
        AiProjectBaselineIdentity? projectBaseline = null)
    {
        var normalizedOwner = NormalizeRequired(ownerHash, nameof(ownerHash));
        var normalizedKind = NormalizeKind(kind);
        if (clientOperationId == Guid.Empty)
        {
            throw new ArgumentException("clientOperationId cannot be empty.", nameof(clientOperationId));
        }

        var normalizedFingerprint = NormalizeRequired(payloadFingerprint, nameof(payloadFingerprint));
        var key = new OperationKey(normalizedOwner, normalizedKind, clientOperationId);
        lock (_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            if (_receipts.TryGetValue(key, out var existing))
            {
                return string.Equals(existing.PayloadFingerprint, normalizedFingerprint, StringComparison.Ordinal)
                    ? new AiOperationReservationResult(AiOperationReservationOutcome.Existing, existing)
                    : new AiOperationReservationResult(
                        AiOperationReservationOutcome.IdentityConflict,
                        existing,
                        "operation_identity_conflict",
                        "clientOperationId 已用于不同请求，请生成新的操作标识。");
            }

            var now = DateTimeOffset.UtcNow;
            var receipt = new AiOperationReceipt
            {
                OwnerHash = normalizedOwner,
                Kind = normalizedKind,
                ClientOperationId = clientOperationId,
                PayloadFingerprint = normalizedFingerprint,
                Status = AiOperationStatuses.Pending,
                SessionId = NormalizeOptional(sessionId),
                ProjectBaseline = CloneBaseline(projectBaseline),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now.Add(Retention)
            };
            var candidate = SnapshotWith(key, receipt);
            if (!Persist(candidate))
            {
                return new AiOperationReservationResult(
                    AiOperationReservationOutcome.PersistenceFailed,
                    null,
                    "operation_receipt_persistence_failed",
                    "操作身份未能安全保存，本次操作没有启动。");
            }

            ReplaceUnderLock(candidate);
            return new AiOperationReservationResult(AiOperationReservationOutcome.Reserved, receipt);
        }
    }

    public AiOperationReceipt? Get(string ownerHash, string kind, Guid clientOperationId)
    {
        var key = new OperationKey(NormalizeRequired(ownerHash, nameof(ownerHash)), NormalizeKind(kind), clientOperationId);
        lock (_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            return _receipts.TryGetValue(key, out var receipt) ? receipt : null;
        }
    }

    public IReadOnlyList<AiOperationReceipt> Find(string ownerHash, Guid clientOperationId)
    {
        var normalizedOwner = NormalizeRequired(ownerHash, nameof(ownerHash));
        lock (_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            return _receipts.Values
                .Where(receipt => receipt.OwnerHash == normalizedOwner && receipt.ClientOperationId == clientOperationId)
                .OrderBy(receipt => receipt.Kind, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public AiOperationReceipt? MarkCreated(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        string? sessionId = null,
        string? runId = null,
        AiProjectBaselineIdentity? projectBaseline = null)
    {
        return Update(ownerHash, kind, clientOperationId, receipt => receipt with
        {
            Status = AiOperationStatuses.Created,
            SessionId = FirstNonBlank(sessionId, receipt.SessionId),
            RunId = FirstNonBlank(runId, receipt.RunId),
            ProjectBaseline = CloneBaseline(projectBaseline) ?? receipt.ProjectBaseline,
            PublicErrorCode = string.Empty,
            PublicMessage = string.Empty,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public AiOperationReceipt? MarkFailed(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        string publicErrorCode,
        string publicMessage,
        bool rejected = false,
        string? sessionId = null,
        string? runId = null,
        AiProjectBaselineIdentity? projectBaseline = null)
    {
        return Update(ownerHash, kind, clientOperationId, receipt => receipt with
        {
            Status = rejected ? AiOperationStatuses.Rejected : AiOperationStatuses.Failed,
            SessionId = FirstNonBlank(sessionId, receipt.SessionId),
            RunId = FirstNonBlank(runId, receipt.RunId),
            ProjectBaseline = CloneBaseline(projectBaseline) ?? receipt.ProjectBaseline,
            PublicErrorCode = NormalizeOptional(publicErrorCode),
            PublicMessage = NormalizeOptional(publicMessage),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private AiOperationReceipt? Update(
        string ownerHash,
        string kind,
        Guid clientOperationId,
        Func<AiOperationReceipt, AiOperationReceipt> update)
    {
        var key = new OperationKey(NormalizeRequired(ownerHash, nameof(ownerHash)), NormalizeKind(kind), clientOperationId);
        lock (_gate)
        {
            if (!_receipts.TryGetValue(key, out var current))
            {
                return null;
            }

            var next = update(current);
            var candidate = SnapshotWith(key, next);
            if (!Persist(candidate))
            {
                return null;
            }

            ReplaceUnderLock(candidate);
            return next;
        }
    }

    private Dictionary<OperationKey, AiOperationReceipt> SnapshotWith(OperationKey key, AiOperationReceipt receipt)
    {
        var candidate = _receipts.ToDictionary(entry => entry.Key, entry => entry.Value);
        candidate[key] = receipt;
        return candidate;
    }

    private bool Persist(IReadOnlyDictionary<OperationKey, AiOperationReceipt> candidate)
    {
        string? tempPath = null;
        try
        {
            var document = new AiOperationReceiptDocument
            {
                Receipts = candidate.Values
                    .OrderByDescending(receipt => receipt.UpdatedAtUtc)
                    .Take(MaxReceipts)
                    .ToList()
            };
            var directory = Path.GetDirectoryName(_storagePath) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $"{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");
            WriteAllTextDurably(tempPath, JsonSerializer.Serialize(document, JsonOptions));
            if (File.Exists(_storagePath))
            {
                File.Replace(tempPath, _storagePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _storagePath);
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
            return false;
        }
    }

    private void Load()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var document = JsonSerializer.Deserialize<AiOperationReceiptDocument>(File.ReadAllText(_storagePath), JsonOptions);
            if (document?.Receipts == null) return;
            var now = DateTimeOffset.UtcNow;
            foreach (var receipt in document.Receipts.Where(receipt => receipt.ExpiresAtUtc > now))
            {
                if (string.IsNullOrWhiteSpace(receipt.OwnerHash) || !AiOperationKinds.IsSupported(receipt.Kind) ||
                    receipt.ClientOperationId == Guid.Empty || string.IsNullOrWhiteSpace(receipt.PayloadFingerprint))
                {
                    continue;
                }
                _receipts[new OperationKey(receipt.OwnerHash, AiOperationKinds.Normalize(receipt.Kind), receipt.ClientOperationId)] = receipt;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _receipts.Clear();
        }
    }

    private static void WriteAllTextDurably(string path, string contents)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
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

    private void PruneExpiredUnderLock(DateTimeOffset now)
    {
        foreach (var key in _receipts.Where(entry => entry.Value.ExpiresAtUtc <= now).Select(entry => entry.Key).ToArray())
        {
            _receipts.Remove(key);
        }
    }

    private void ReplaceUnderLock(IReadOnlyDictionary<OperationKey, AiOperationReceipt> candidate)
    {
        _receipts.Clear();
        foreach (var entry in candidate.OrderByDescending(entry => entry.Value.UpdatedAtUtc).Take(MaxReceipts))
        {
            _receipts[entry.Key] = entry.Value;
        }
    }

    private static string NormalizeKind(string kind)
    {
        var normalized = NormalizeRequired(kind, nameof(kind)).ToLowerInvariant();
        return AiOperationKinds.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported AI operation kind.");
    }

    private static string NormalizeRequired(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();

    private static string NormalizeOptional(string? value) => value?.Trim() ?? string.Empty;

    private static string FirstNonBlank(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();

    private static AiProjectBaselineIdentity? CloneBaseline(AiProjectBaselineIdentity? baseline) =>
        baseline == null ? null : baseline with { };

    private readonly record struct OperationKey(string OwnerHash, string Kind, Guid ClientOperationId);
}
