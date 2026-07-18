using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Interfaces;

public interface IProjectLifecycleOperationRepository
{
    Task<ProjectLifecycleOperation?> GetAsync(
        string userId,
        ProjectLifecycleOperationKind kind,
        Guid clientOperationId,
        CancellationToken cancellationToken = default);

    Task<ProjectLifecycleOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<ProjectLifecycleOperation?> GetDeleteAuthorityAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        ProjectLifecycleOperation operation,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ProjectLifecycleOperation operation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectLifecycleOperation>> GetRecoverableAsync(
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
