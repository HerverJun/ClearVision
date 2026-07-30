using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Handoff;

public static class AiWorkspaceHandoffStatuses
{
    public const string Available = "available";
    public const string Consuming = "consuming";
    public const string Consumed = "consumed";
    public const string Expired = "expired";
    public const string Rejected = "rejected";

    public static bool IsKnown(string? value) => value is
        Available or Consuming or Consumed or Expired or Rejected;
}

public sealed record AiWorkspaceHandoffConsumeReceiptV1
{
    public Guid ClientOperationId { get; init; }
    public Guid? TargetProjectId { get; init; }
    public string Result { get; init; } = "workspace_staged";
    public DateTimeOffset AcknowledgedAtUtc { get; init; }
    public bool ProjectSaved { get; init; }
}

public sealed record AiWorkspaceHandoffArtifactV1
{
    public int SchemaVersion { get; init; } = 1;
    public string ArtifactId { get; init; } = string.Empty;
    public string OwnerHash { get; init; } = string.Empty;
    public Guid ClientOperationId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public long SessionRevision { get; init; }
    public string PlanRunId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string BuildRunId { get; init; } = string.Empty;
    public Guid BuildClientOperationId { get; init; }
    public string BuildIdentity { get; init; } = string.Empty;
    public string SubmittedBuildFingerprint { get; init; } = string.Empty;
    public int AnswerRevision { get; init; }
    public int ResourceRevision { get; init; }
    public string TargetKind { get; init; } = string.Empty;
    public AiProjectBaselineIdentity? ProjectBaseline { get; init; }
    public string CandidateFlowJson { get; init; } = string.Empty;
    public string CandidateFlowFingerprint { get; init; } = string.Empty;
    public VisionAgentPublicBuildResultV1 PublicBuild { get; init; } = new();
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string Status { get; init; } = AiWorkspaceHandoffStatuses.Available;
    public Guid? ConsumeClientOperationId { get; init; }
    public Guid? ReservedTargetProjectId { get; init; }
    public AiWorkspaceHandoffConsumeReceiptV1? ConsumeReceipt { get; init; }
    public string RejectionCode { get; init; } = string.Empty;
}

public sealed record AiWorkspaceHandoffCreateCommand
{
    public string OwnerHash { get; init; } = string.Empty;
    public Guid ClientOperationId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public long SessionRevision { get; init; }
    public string PlanRunId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string BuildRunId { get; init; } = string.Empty;
    public Guid BuildClientOperationId { get; init; }
    public string BuildIdentity { get; init; } = string.Empty;
    public string SubmittedBuildFingerprint { get; init; } = string.Empty;
    public int AnswerRevision { get; init; }
    public int ResourceRevision { get; init; }
    public string TargetKind { get; init; } = string.Empty;
    public AiProjectBaselineIdentity? ProjectBaseline { get; init; }
    public string CandidateFlowJson { get; init; } = string.Empty;
    public string CandidateFlowFingerprint { get; init; } = string.Empty;
    public VisionAgentPublicBuildResultV1 PublicBuild { get; init; } = new();
}

public enum AiWorkspaceHandoffStoreOutcome
{
    Created,
    Existing,
    Updated,
    NotFound,
    IdentityConflict,
    InvalidState,
    Expired,
    CapacityExceeded,
    PayloadTooLarge,
    PersistenceFailed
}

public sealed record AiWorkspaceHandoffStoreResult(
    AiWorkspaceHandoffStoreOutcome Outcome,
    AiWorkspaceHandoffArtifactV1? Artifact,
    string ErrorCode = "",
    string PublicMessage = "");

public interface IAiWorkspaceHandoffArtifactStore
{
    AiWorkspaceHandoffStoreResult Create(AiWorkspaceHandoffCreateCommand command);
    AiWorkspaceHandoffArtifactV1? Get(string ownerHash, string artifactId);
    AiWorkspaceHandoffArtifactV1? FindByCreateOperation(string ownerHash, Guid clientOperationId);
    AiWorkspaceHandoffArtifactV1? FindByBuildRun(string ownerHash, string buildRunId);
    AiWorkspaceHandoffStoreResult ReserveConsume(
        string ownerHash,
        string artifactId,
        Guid clientOperationId,
        Guid? targetProjectId);
    AiWorkspaceHandoffStoreResult Acknowledge(
        string ownerHash,
        string artifactId,
        Guid clientOperationId,
        Guid? targetProjectId);
    AiWorkspaceHandoffStoreResult Reject(
        string ownerHash,
        string artifactId,
        Guid clientOperationId,
        string rejectionCode);
}

internal sealed class AiWorkspaceHandoffArtifactDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<AiWorkspaceHandoffArtifactV1> Artifacts { get; set; } = [];
}

public sealed class AiWorkspaceHandoffArtifactStore : IAiWorkspaceHandoffArtifactStore
{
    public const string StorageRootEnvironmentVariable = "CV_AI_HANDOFF_STORE_ROOT";
    public static readonly TimeSpan ArtifactTimeToLive = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan TerminalAuditRetention = TimeSpan.FromHours(24);
    public const int MaxActiveArtifactsPerOwner = 16;
    public const int MaxActiveArtifactsGlobal = 256;
    public const int MaxStoredArtifacts = 512;
    public const int MaxCandidateFlowBytes = 2 * 1024 * 1024;

    private const int MaxPersistenceAttempts = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly object _gate = new();
    private readonly string _storagePath;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<string, AiWorkspaceHandoffArtifactV1> _artifacts =
        new(StringComparer.Ordinal);

    public AiWorkspaceHandoffArtifactStore()
        : this(null, null)
    {
    }

    public AiWorkspaceHandoffArtifactStore(string? storageRootPath, Func<DateTimeOffset>? utcNow = null)
    {
        var root = ResolveStorageRootPath(storageRootPath);
        Directory.CreateDirectory(root);
        _storagePath = Path.Combine(root, "ai_workspace_handoff_artifacts.json");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public static string ResolveStorageRootPath(string? storageRootPath = null)
    {
        storageRootPath ??= Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(storageRootPath)
            ? ConversationalFlowService.ResolveStorageRootPath()
            : storageRootPath.Trim());
    }

    public AiWorkspaceHandoffStoreResult Create(AiWorkspaceHandoffCreateCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var ownerHash = Required(command.OwnerHash, nameof(command.OwnerHash));
        if (command.ClientOperationId == Guid.Empty)
        {
            throw new ArgumentException("clientOperationId cannot be empty.", nameof(command));
        }
        var candidateFingerprint = Required(command.CandidateFlowFingerprint, nameof(command.CandidateFlowFingerprint));
        var candidateFlowJson = command.CandidateFlowJson ?? string.Empty;
        var candidateBytes = Encoding.UTF8.GetByteCount(candidateFlowJson);
        if (candidateBytes == 0 || candidateBytes > MaxCandidateFlowBytes)
        {
            return Failure(
                AiWorkspaceHandoffStoreOutcome.PayloadTooLarge,
                "handoff_candidate_size_invalid",
                $"候选流程必须小于 {MaxCandidateFlowBytes / 1024 / 1024} MiB。");
        }

        lock (_gate)
        {
            var now = _utcNow();
            var candidate = SnapshotAndMaintain(now);
            var existing = candidate.Values.FirstOrDefault(item =>
                item.OwnerHash == ownerHash && item.ClientOperationId == command.ClientOperationId);
            if (existing != null)
            {
                return string.Equals(
                    NormalizeHash(existing.CandidateFlowFingerprint),
                    NormalizeHash(candidateFingerprint),
                    StringComparison.Ordinal)
                    ? new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Existing, existing)
                    : Failure(
                        AiWorkspaceHandoffStoreOutcome.IdentityConflict,
                        "handoff_create_identity_conflict",
                        "同一交接操作标识已用于不同候选，请重新确认当前 Build。");
            }

            var active = candidate.Values.Where(IsActive).ToArray();
            if (active.Count(item => item.OwnerHash == ownerHash) >= MaxActiveArtifactsPerOwner ||
                active.Length >= MaxActiveArtifactsGlobal)
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.CapacityExceeded,
                    "handoff_capacity_exceeded",
                    "当前待审核候选数量已达上限，请先处理已有候选或等待其过期。");
            }

            var artifact = new AiWorkspaceHandoffArtifactV1
            {
                ArtifactId = Guid.NewGuid().ToString("N"),
                OwnerHash = ownerHash,
                ClientOperationId = command.ClientOperationId,
                SessionId = Required(command.SessionId, nameof(command.SessionId)),
                SessionRevision = command.SessionRevision,
                PlanRunId = Required(command.PlanRunId, nameof(command.PlanRunId)),
                PlanId = Required(command.PlanId, nameof(command.PlanId)),
                PlanHash = Required(command.PlanHash, nameof(command.PlanHash)),
                BuildRunId = Required(command.BuildRunId, nameof(command.BuildRunId)),
                BuildClientOperationId = command.BuildClientOperationId,
                BuildIdentity = Required(command.BuildIdentity, nameof(command.BuildIdentity)),
                SubmittedBuildFingerprint = Required(
                    command.SubmittedBuildFingerprint,
                    nameof(command.SubmittedBuildFingerprint)),
                AnswerRevision = command.AnswerRevision,
                ResourceRevision = command.ResourceRevision,
                TargetKind = Required(command.TargetKind, nameof(command.TargetKind)).ToLowerInvariant(),
                ProjectBaseline = command.ProjectBaseline is null ? null : command.ProjectBaseline with { },
                CandidateFlowJson = candidateFlowJson,
                CandidateFlowFingerprint = candidateFingerprint,
                PublicBuild = CloneBuild(command.PublicBuild),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(ArtifactTimeToLive),
                UpdatedAtUtc = now,
                Status = AiWorkspaceHandoffStatuses.Available
            };
            candidate[artifact.ArtifactId] = artifact;
            TrimTerminalArtifacts(candidate, now);
            if (!Persist(candidate))
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.PersistenceFailed,
                    "handoff_persistence_failed",
                    "交接工件未能安全保存，本次交接没有创建。");
            }
            Replace(candidate);
            return new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Created, artifact);
        }
    }

    public AiWorkspaceHandoffArtifactV1? Get(string ownerHash, string artifactId)
    {
        var owner = Required(ownerHash, nameof(ownerHash));
        var id = Required(artifactId, nameof(artifactId));
        lock (_gate)
        {
            MaintainAndPersistIfChanged(_utcNow());
            return _artifacts.TryGetValue(id, out var artifact) && artifact.OwnerHash == owner
                ? artifact
                : null;
        }
    }

    public AiWorkspaceHandoffArtifactV1? FindByCreateOperation(string ownerHash, Guid clientOperationId)
    {
        var owner = Required(ownerHash, nameof(ownerHash));
        lock (_gate)
        {
            MaintainAndPersistIfChanged(_utcNow());
            return _artifacts.Values
                .Where(item => item.OwnerHash == owner && item.ClientOperationId == clientOperationId)
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefault();
        }
    }

    public AiWorkspaceHandoffArtifactV1? FindByBuildRun(string ownerHash, string buildRunId)
    {
        var owner = Required(ownerHash, nameof(ownerHash));
        var runId = Required(buildRunId, nameof(buildRunId));
        lock (_gate)
        {
            MaintainAndPersistIfChanged(_utcNow());
            return _artifacts.Values
                .Where(item => item.OwnerHash == owner &&
                    string.Equals(item.BuildRunId, runId, StringComparison.Ordinal))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefault();
        }
    }

    public AiWorkspaceHandoffStoreResult ReserveConsume(
        string ownerHash,
        string artifactId,
        Guid clientOperationId,
        Guid? targetProjectId)
    {
        if (clientOperationId == Guid.Empty)
        {
            throw new ArgumentException("clientOperationId cannot be empty.", nameof(clientOperationId));
        }
        return Mutate(ownerHash, artifactId, current =>
        {
            if (current.Status == AiWorkspaceHandoffStatuses.Expired)
            {
                return Failure(AiWorkspaceHandoffStoreOutcome.Expired, "handoff_expired", "交接工件已过期，请返回 AI 重新构建。");
            }
            if (current.Status == AiWorkspaceHandoffStatuses.Consumed ||
                current.Status == AiWorkspaceHandoffStatuses.Rejected)
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.InvalidState,
                    $"handoff_{current.Status}",
                    current.Status == AiWorkspaceHandoffStatuses.Consumed
                        ? "交接工件已由工作区接收。"
                        : "交接工件已放弃，不能再次接收。");
            }
            if (current.Status == AiWorkspaceHandoffStatuses.Consuming)
            {
                return current.ConsumeClientOperationId == clientOperationId &&
                    current.ReservedTargetProjectId == targetProjectId
                    ? new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Existing, current)
                    : Failure(
                        AiWorkspaceHandoffStoreOutcome.IdentityConflict,
                        "handoff_consume_identity_conflict",
                        "该候选正在由另一个工作区接收，请协调现有接收状态。");
            }
            var next = current with
            {
                Status = AiWorkspaceHandoffStatuses.Consuming,
                ConsumeClientOperationId = clientOperationId,
                ReservedTargetProjectId = targetProjectId,
                UpdatedAtUtc = _utcNow()
            };
            return new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Updated, next);
        });
    }

    public AiWorkspaceHandoffStoreResult Acknowledge(
        string ownerHash,
        string artifactId,
        Guid clientOperationId,
        Guid? targetProjectId)
    {
        if (clientOperationId == Guid.Empty)
        {
            throw new ArgumentException("clientOperationId cannot be empty.", nameof(clientOperationId));
        }
        return Mutate(ownerHash, artifactId, current =>
        {
            if (current.Status == AiWorkspaceHandoffStatuses.Consumed)
            {
                return current.ConsumeReceipt?.ClientOperationId == clientOperationId &&
                    current.ConsumeReceipt.TargetProjectId == targetProjectId
                    ? new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Existing, current)
                    : Failure(
                        AiWorkspaceHandoffStoreOutcome.IdentityConflict,
                        "handoff_acknowledge_identity_conflict",
                        "交接工件已由另一个接收操作确认。");
            }
            if (current.Status != AiWorkspaceHandoffStatuses.Consuming ||
                current.ConsumeClientOperationId != clientOperationId ||
                current.ReservedTargetProjectId != targetProjectId)
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.InvalidState,
                    "handoff_not_reserved",
                    "工作区尚未为当前操作保留该候选，不能确认接收。");
            }
            var now = _utcNow();
            var receipt = new AiWorkspaceHandoffConsumeReceiptV1
            {
                ClientOperationId = clientOperationId,
                TargetProjectId = targetProjectId,
                AcknowledgedAtUtc = now,
                ProjectSaved = false
            };
            var next = current with
            {
                Status = AiWorkspaceHandoffStatuses.Consumed,
                ConsumeReceipt = receipt,
                UpdatedAtUtc = now
            };
            return new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Updated, next);
        });
    }

    public AiWorkspaceHandoffStoreResult Reject(
        string ownerHash,
        string artifactId,
        Guid clientOperationId,
        string rejectionCode)
    {
        if (clientOperationId == Guid.Empty)
        {
            throw new ArgumentException("clientOperationId cannot be empty.", nameof(clientOperationId));
        }
        var code = Required(rejectionCode, nameof(rejectionCode));
        return Mutate(ownerHash, artifactId, current =>
        {
            if (current.Status == AiWorkspaceHandoffStatuses.Consumed)
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.InvalidState,
                    "handoff_consumed",
                    "工作区已接收该候选，不能从 artifact store 撤销本地草稿。");
            }
            if (current.Status == AiWorkspaceHandoffStatuses.Rejected)
            {
                return new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Existing, current);
            }
            if (current.Status == AiWorkspaceHandoffStatuses.Expired)
            {
                return Failure(AiWorkspaceHandoffStoreOutcome.Expired, "handoff_expired", "交接工件已过期。");
            }
            if (current.Status == AiWorkspaceHandoffStatuses.Consuming &&
                current.ConsumeClientOperationId != clientOperationId)
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.IdentityConflict,
                    "handoff_reject_identity_conflict",
                    "当前接收操作与放弃操作不一致，请先协调交接状态。");
            }
            var next = current with
            {
                Status = AiWorkspaceHandoffStatuses.Rejected,
                ConsumeClientOperationId = current.ConsumeClientOperationId ?? clientOperationId,
                RejectionCode = code,
                UpdatedAtUtc = _utcNow()
            };
            return new AiWorkspaceHandoffStoreResult(AiWorkspaceHandoffStoreOutcome.Updated, next);
        });
    }

    private AiWorkspaceHandoffStoreResult Mutate(
        string ownerHash,
        string artifactId,
        Func<AiWorkspaceHandoffArtifactV1, AiWorkspaceHandoffStoreResult> mutation)
    {
        var owner = Required(ownerHash, nameof(ownerHash));
        var id = Required(artifactId, nameof(artifactId));
        lock (_gate)
        {
            var now = _utcNow();
            var candidate = SnapshotAndMaintain(now);
            if (!candidate.TryGetValue(id, out var current) || current.OwnerHash != owner)
            {
                return Failure(AiWorkspaceHandoffStoreOutcome.NotFound, "handoff_not_found", "交接工件不存在或当前用户无权访问。");
            }
            var result = mutation(current);
            if (result.Outcome is not (AiWorkspaceHandoffStoreOutcome.Updated or AiWorkspaceHandoffStoreOutcome.Created))
            {
                if (!SameSnapshot(candidate, _artifacts)) PersistAndReplaceMaintenance(candidate);
                return result;
            }
            if (result.Artifact == null)
            {
                return Failure(AiWorkspaceHandoffStoreOutcome.PersistenceFailed, "handoff_mutation_invalid", "交接状态更新失败。");
            }
            candidate[id] = result.Artifact;
            TrimTerminalArtifacts(candidate, now);
            if (!Persist(candidate))
            {
                return Failure(
                    AiWorkspaceHandoffStoreOutcome.PersistenceFailed,
                    "handoff_persistence_failed",
                    "交接状态未能安全保存，请协调后重试。");
            }
            Replace(candidate);
            return result;
        }
    }

    private Dictionary<string, AiWorkspaceHandoffArtifactV1> SnapshotAndMaintain(DateTimeOffset now)
    {
        var candidate = _artifacts.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var entry in candidate.ToArray())
        {
            if (IsActive(entry.Value) && entry.Value.ExpiresAtUtc <= now)
            {
                candidate[entry.Key] = entry.Value with
                {
                    Status = AiWorkspaceHandoffStatuses.Expired,
                    UpdatedAtUtc = now
                };
            }
        }
        TrimTerminalArtifacts(candidate, now);
        return candidate;
    }

    private void MaintainAndPersistIfChanged(DateTimeOffset now)
    {
        var candidate = SnapshotAndMaintain(now);
        if (!SameSnapshot(candidate, _artifacts)) PersistAndReplaceMaintenance(candidate);
    }

    private void PersistAndReplaceMaintenance(Dictionary<string, AiWorkspaceHandoffArtifactV1> candidate)
    {
        if (Persist(candidate)) Replace(candidate);
    }

    private static void TrimTerminalArtifacts(
        IDictionary<string, AiWorkspaceHandoffArtifactV1> candidate,
        DateTimeOffset now)
    {
        foreach (var key in candidate
            .Where(entry => !IsActive(entry.Value) && entry.Value.UpdatedAtUtc.Add(TerminalAuditRetention) <= now)
            .Select(entry => entry.Key)
            .ToArray())
        {
            candidate.Remove(key);
        }
        foreach (var key in candidate.Values
            .OrderByDescending(item => IsActive(item))
            .ThenByDescending(item => item.UpdatedAtUtc)
            .Skip(MaxStoredArtifacts)
            .Select(item => item.ArtifactId)
            .ToArray())
        {
            candidate.Remove(key);
        }
    }

    private bool Persist(IReadOnlyDictionary<string, AiWorkspaceHandoffArtifactV1> candidate)
    {
        for (var attempt = 1; attempt <= MaxPersistenceAttempts; attempt++)
        {
            string? tempPath = null;
            try
            {
                var document = new AiWorkspaceHandoffArtifactDocument
                {
                    Artifacts = candidate.Values.OrderByDescending(item => item.UpdatedAtUtc).ToList()
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
                    try { File.Delete(tempPath); }
                    catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
                }
                if (attempt == MaxPersistenceAttempts || error is UnauthorizedAccessException or JsonException)
                {
                    return false;
                }
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * (1 << (attempt - 1))));
            }
        }
        return false;
    }

    private void Load()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var document = JsonSerializer.Deserialize<AiWorkspaceHandoffArtifactDocument>(
                File.ReadAllText(_storagePath), JsonOptions);
            if (document?.Artifacts == null) return;
            foreach (var artifact in document.Artifacts)
            {
                if (!ValidLoadedArtifact(artifact)) continue;
                _artifacts[artifact.ArtifactId] = artifact;
            }
            MaintainAndPersistIfChanged(_utcNow());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _artifacts.Clear();
        }
    }

    private static bool ValidLoadedArtifact(AiWorkspaceHandoffArtifactV1 artifact) =>
        artifact.SchemaVersion == 1 &&
        !string.IsNullOrWhiteSpace(artifact.ArtifactId) &&
        !string.IsNullOrWhiteSpace(artifact.OwnerHash) &&
        artifact.ClientOperationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(artifact.CandidateFlowJson) &&
        !string.IsNullOrWhiteSpace(artifact.CandidateFlowFingerprint) &&
        AiWorkspaceHandoffStatuses.IsKnown(artifact.Status);

    private void Replace(IReadOnlyDictionary<string, AiWorkspaceHandoffArtifactV1> candidate)
    {
        _artifacts.Clear();
        foreach (var entry in candidate) _artifacts[entry.Key] = entry.Value;
    }

    private static bool IsActive(AiWorkspaceHandoffArtifactV1 artifact) =>
        artifact.Status is AiWorkspaceHandoffStatuses.Available or AiWorkspaceHandoffStatuses.Consuming;

    private static bool SameSnapshot(
        IReadOnlyDictionary<string, AiWorkspaceHandoffArtifactV1> left,
        IReadOnlyDictionary<string, AiWorkspaceHandoffArtifactV1> right)
    {
        if (left.Count != right.Count) return false;
        return left.All(entry => right.TryGetValue(entry.Key, out var value) && value == entry.Value);
    }

    private static VisionAgentPublicBuildResultV1 CloneBuild(VisionAgentPublicBuildResultV1 build) =>
        JsonSerializer.Deserialize<VisionAgentPublicBuildResultV1>(
            JsonSerializer.Serialize(build, JsonOptions), JsonOptions) ?? new VisionAgentPublicBuildResultV1();

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", name)
            : value.Trim();

    private static string NormalizeHash(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? normalized["sha256:".Length..].ToUpperInvariant()
            : normalized.ToUpperInvariant();
    }

    private static AiWorkspaceHandoffStoreResult Failure(
        AiWorkspaceHandoffStoreOutcome outcome,
        string code,
        string message) => new(outcome, null, code, message);

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
}
