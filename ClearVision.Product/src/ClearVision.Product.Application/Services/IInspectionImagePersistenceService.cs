using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Application.Services;

public sealed record InspectionImageStorageHealth(
    int ManagedRootCount,
    int FileCount,
    long TotalBytes,
    DateTimeOffset? OldestImageAtUtc,
    long TrimmedFileCount,
    bool GapDetected,
    bool Degraded,
    long? AvailableFreeBytes,
    DateTimeOffset? LastSuccessfulCleanupAtUtc);

/// <summary>
/// Supplies free-space information without forcing product code or tests to query an actual drive.
/// </summary>
public interface IInspectionStorageFreeSpaceProvider
{
    long? GetAvailableFreeBytes(string path);
}

public interface IInspectionImagePersistenceService
{
    Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before a formal production run starts. Implementations that own disk-backed
    /// image storage must fail closed when the configured free-space floor cannot be met.
    /// </summary>
    void EnsureProductionStartAllowed()
    {
    }

    InspectionImageStorageHealth GetStorageHealth() => new(
        ManagedRootCount: 0,
        FileCount: 0,
        TotalBytes: 0,
        OldestImageAtUtc: null,
        TrimmedFileCount: 0,
        GapDetected: false,
        Degraded: false,
        AvailableFreeBytes: null,
        LastSuccessfulCleanupAtUtc: null);
}

public sealed class NullInspectionImagePersistenceService : IInspectionImagePersistenceService
{
    public static NullInspectionImagePersistenceService Instance { get; } = new();

    private NullInspectionImagePersistenceService()
    {
    }

    public Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
