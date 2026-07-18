using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Infrastructure.Repositories;

public sealed class ProjectLifecycleOperationRepository : IProjectLifecycleOperationRepository
{
    private readonly VisionDbContext _context;
    private readonly DbSet<ProjectLifecycleOperation> _operations;

    public ProjectLifecycleOperationRepository(VisionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _operations = context.Set<ProjectLifecycleOperation>();
    }

    public Task<ProjectLifecycleOperation?> GetAsync(
        string userId,
        ProjectLifecycleOperationKind kind,
        Guid clientOperationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _operations.FirstOrDefaultAsync(
            operation => operation.UserId == userId &&
                         operation.Kind == kind &&
                         operation.ClientOperationId == clientOperationId,
            cancellationToken);
    }

    public Task<ProjectLifecycleOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        return _operations.FirstOrDefaultAsync(
            operation => operation.Id == operationId,
            cancellationToken);
    }

    public async Task<ProjectLifecycleOperation?> GetDeleteAuthorityAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _operations
            .Where(operation =>
                operation.Kind == ProjectLifecycleOperationKind.Delete &&
                operation.ProjectId == projectId &&
                operation.Status == ProjectLifecycleOperationStatus.Completed)
            .ToListAsync(cancellationToken);
        return candidates.OrderByDescending(operation => operation.UpdatedAtUtc).FirstOrDefault();
    }

    public async Task AddAsync(
        ProjectLifecycleOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _operations.AddAsync(operation, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _context.Entry(operation).State = EntityState.Detached;
            throw;
        }
    }

    public async Task UpdateAsync(
        ProjectLifecycleOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Update(operation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectLifecycleOperation>> GetRecoverableAsync(
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var candidates = await _operations
            .Where(operation =>
                operation.Status == ProjectLifecycleOperationStatus.Pending ||
                operation.Status == ProjectLifecycleOperationStatus.FailedRetryable ||
                (operation.Kind == ProjectLifecycleOperationKind.Delete &&
                 operation.Status == ProjectLifecycleOperationStatus.Completed &&
                 operation.CleanupAuthorityOperationId == null &&
                 (operation.CleanupStatus == ProjectLifecycleCleanupStatus.CleanupPending ||
                  operation.CleanupStatus == ProjectLifecycleCleanupStatus.CleanupFailedRetryable)))
            .ToListAsync(cancellationToken);
        return candidates
            .Where(operation => operation.CleanupNextAttemptAtUtc == null || operation.CleanupNextAttemptAtUtc <= nowUtc)
            .OrderBy(operation => operation.UpdatedAtUtc)
            .Take(take)
            .ToList();
    }

    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _operations
            .Where(operation => operation.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken);
        var expired = candidates
            .Where(operation => operation.ExpiresAtUtc <= nowUtc)
            .ToList();
        if (expired.Count == 0)
        {
            return 0;
        }

        _operations.RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
