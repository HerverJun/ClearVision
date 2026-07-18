namespace ClearVision.Product.Core.Entities;

public enum ProjectLifecycleOperationKind
{
    Create = 0,
    Delete = 1
}

public enum ProjectLifecycleOperationStatus
{
    Pending = 0,
    Completed = 1,
    FailedRetryable = 2,
    FailedTerminal = 3
}

public enum ProjectLifecycleCleanupStatus
{
    NotApplicable = 0,
    CleanupPending = 1,
    CleanupCompleted = 2,
    CleanupFailedRetryable = 3
}

/// <summary>
/// Durable, user-scoped identity and outcome journal for Project create/delete commands.
/// Project data remains authoritative in <see cref="Project"/>; this record only owns
/// command idempotency, response-loss reconciliation, and delete cleanup progress.
/// </summary>
public sealed class ProjectLifecycleOperation
{
    private ProjectLifecycleOperation()
    {
        UserId = string.Empty;
        PayloadFingerprint = string.Empty;
    }

    private ProjectLifecycleOperation(
        string userId,
        ProjectLifecycleOperationKind kind,
        Guid clientOperationId,
        string payloadFingerprint,
        Guid projectId,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("Authenticated user id is required.", nameof(userId));
        }

        if (clientOperationId == Guid.Empty)
        {
            throw new ArgumentException("Client operation id cannot be empty.", nameof(clientOperationId));
        }

        if (string.IsNullOrWhiteSpace(payloadFingerprint))
        {
            throw new ArgumentException("Payload fingerprint is required.", nameof(payloadFingerprint));
        }

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        Id = Guid.NewGuid();
        UserId = userId.Trim();
        Kind = kind;
        ClientOperationId = clientOperationId;
        PayloadFingerprintVersion = 1;
        PayloadFingerprint = payloadFingerprint;
        Status = ProjectLifecycleOperationStatus.Pending;
        ProjectId = projectId;
        CleanupStatus = ProjectLifecycleCleanupStatus.NotApplicable;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string UserId { get; private set; }

    public ProjectLifecycleOperationKind Kind { get; private set; }

    public Guid ClientOperationId { get; private set; }

    public int PayloadFingerprintVersion { get; private set; }

    public string PayloadFingerprint { get; private set; }

    public ProjectLifecycleOperationStatus Status { get; private set; }

    public Guid ProjectId { get; private set; }

    public string? ProjectName { get; private set; }

    public string? ProjectDescription { get; private set; }

    public long? ExpectedPersistenceRevision { get; private set; }

    public string? ResultJson { get; private set; }

    public string? ErrorCode { get; private set; }

    public ProjectLifecycleCleanupStatus CleanupStatus { get; private set; }

    public Guid? CleanupAuthorityOperationId { get; private set; }

    public int CleanupAttemptCount { get; private set; }

    public string? LastCleanupErrorCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? CleanupNextAttemptAtUtc { get; private set; }

    public static ProjectLifecycleOperation ReserveCreate(
        string userId,
        Guid clientOperationId,
        string payloadFingerprint,
        Guid projectId,
        string name,
        string? description,
        DateTimeOffset nowUtc)
    {
        var operation = new ProjectLifecycleOperation(
            userId,
            ProjectLifecycleOperationKind.Create,
            clientOperationId,
            payloadFingerprint,
            projectId,
            nowUtc)
        {
            ProjectName = name,
            ProjectDescription = description
        };
        return operation;
    }

    public static ProjectLifecycleOperation ReserveDelete(
        string userId,
        Guid clientOperationId,
        string payloadFingerprint,
        Guid projectId,
        long expectedPersistenceRevision,
        DateTimeOffset nowUtc)
    {
        var operation = new ProjectLifecycleOperation(
            userId,
            ProjectLifecycleOperationKind.Delete,
            clientOperationId,
            payloadFingerprint,
            projectId,
            nowUtc)
        {
            ExpectedPersistenceRevision = expectedPersistenceRevision,
            CleanupStatus = ProjectLifecycleCleanupStatus.CleanupPending
        };
        return operation;
    }

    public bool MatchesFingerprint(string fingerprint) =>
        string.Equals(PayloadFingerprint, fingerprint, StringComparison.Ordinal);

    public void CompleteCreate(string resultJson, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        Status = ProjectLifecycleOperationStatus.Completed;
        ResultJson = resultJson;
        ErrorCode = null;
        CleanupStatus = ProjectLifecycleCleanupStatus.NotApplicable;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void CompleteDelete(string resultJson, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        Status = ProjectLifecycleOperationStatus.Completed;
        ResultJson = resultJson;
        ErrorCode = null;
        CleanupStatus = ProjectLifecycleCleanupStatus.CleanupPending;
        CleanupNextAttemptAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void CompleteDeleteReplay(
        string resultJson,
        Guid cleanupAuthorityOperationId,
        ProjectLifecycleCleanupStatus cleanupStatus,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        Status = ProjectLifecycleOperationStatus.Completed;
        ResultJson = resultJson;
        ErrorCode = null;
        CleanupAuthorityOperationId = cleanupAuthorityOperationId;
        CleanupStatus = cleanupStatus;
        CleanupNextAttemptAtUtc = null;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void MarkFailedRetryable(string errorCode, DateTimeOffset nowUtc, DateTimeOffset retryAtUtc)
    {
        Status = ProjectLifecycleOperationStatus.FailedRetryable;
        ErrorCode = errorCode;
        UpdatedAtUtc = nowUtc;
        CleanupNextAttemptAtUtc = retryAtUtc;
        ExpiresAtUtc = null;
    }

    public void MarkFailedTerminal(string errorCode, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        Status = ProjectLifecycleOperationStatus.FailedTerminal;
        ErrorCode = errorCode;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void MarkCleanupCompleted(DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        CleanupStatus = ProjectLifecycleCleanupStatus.CleanupCompleted;
        CleanupAttemptCount += 1;
        LastCleanupErrorCode = null;
        CleanupNextAttemptAtUtc = null;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void MarkCleanupFailedRetryable(
        string errorCode,
        DateTimeOffset nowUtc,
        DateTimeOffset retryAtUtc)
    {
        CleanupStatus = ProjectLifecycleCleanupStatus.CleanupFailedRetryable;
        CleanupAttemptCount += 1;
        LastCleanupErrorCode = errorCode;
        CleanupNextAttemptAtUtc = retryAtUtc;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = null;
    }
}
