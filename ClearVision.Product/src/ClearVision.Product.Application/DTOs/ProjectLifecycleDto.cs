namespace ClearVision.Product.Application.DTOs;

public sealed class DeleteProjectRequest
{
    public Guid ClientOperationId { get; set; }

    public long ExpectedPersistenceRevision { get; set; }
}

public sealed class ProjectOpenResponse
{
    public Guid ProjectId { get; set; }

    public DateTime LastOpenedAtUtc { get; set; }
}

public sealed class ProjectLifecycleOperationDto
{
    public Guid ClientOperationId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    public ProjectLifecycleOperationResultDto? Result { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }
}

public sealed class ProjectLifecycleOperationResultDto
{
    public ProjectDto? Project { get; set; }

    public bool ProjectDeleted { get; set; }

    public bool Deleted { get; set; }

    public bool AlreadyDeleted { get; set; }

    public string? CleanupStatus { get; set; }
}

public sealed record ProjectCreateCommandResult(
    ProjectDto Project,
    bool OperationReplayed,
    ProjectLifecycleOperationDto Operation);

public sealed record ProjectDeleteCommandResult(
    Guid ProjectId,
    bool OperationReplayed,
    ProjectLifecycleOperationDto Operation);
