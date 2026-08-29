using System.Linq.Expressions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// User repository contract.
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Gets a user by username.
    /// </summary>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active users.
    /// </summary>
    Task<IEnumerable<User>> GetAllActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the username already exists.
    /// </summary>
    Task<bool> IsUsernameExistsAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the system already has any non-deleted user.
    /// </summary>
    Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the durable installation latch. Implementations may perform a one-way legacy
    /// backfill when users predate the latch.
    /// </summary>
    Task<bool> IsInstallationCompletedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically changes the installation latch from incomplete to complete and inserts the
    /// first active Admin. At most one concurrent caller may succeed.
    /// </summary>
    Task<UserAuthorityMutationResult> TryCreateInitialAdminAsync(
        User adminUser,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates a user while preserving at least one active, non-deleted Admin.
    /// </summary>
    Task<UserAuthorityMutationResult> TryUpdatePreservingActiveAdminAsync(
        Guid userId,
        string? displayName,
        UserRole role,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically deletes a user while preserving at least one active, non-deleted Admin.
    /// </summary>
    Task<UserAuthorityMutationResult> TryDeletePreservingActiveAdminAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public enum UserAuthorityMutationStatus
{
    Success,
    NotFound,
    LastActiveAdmin,
    InstallationAlreadyCompleted
}

public sealed record UserAuthorityMutationResult(
    UserAuthorityMutationStatus Status,
    User? User = null)
{
    public static UserAuthorityMutationResult Succeeded(User user) =>
        new(UserAuthorityMutationStatus.Success, user);

    public static UserAuthorityMutationResult Failed(UserAuthorityMutationStatus status) =>
        new(status);
}
