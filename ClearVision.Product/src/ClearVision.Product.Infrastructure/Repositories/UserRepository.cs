using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Infrastructure.Repositories;

/// <summary>
/// User repository implementation.
/// </summary>
public class UserRepository : RepositoryBase<User>, IUserRepository
{
    public UserRepository(Data.VisionDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted, cancellationToken);
    }

    public async Task<IEnumerable<User>> GetAllActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(u => u.IsActive && !u.IsDeleted)
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsUsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AnyAsync(u => u.Username == username && !u.IsDeleted, cancellationToken);
    }

    public async Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(u => !u.IsDeleted, cancellationToken);
    }

    public async Task<bool> IsInstallationCompletedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInstallationStateAsync(cancellationToken);

        return await _context.InstallationStates
            .AsNoTracking()
            .Where(state => state.Id == Data.InstallationStateEntity.SingletonId)
            .Select(state => state.IsCompleted)
            .SingleAsync(cancellationToken);
    }

    public async Task<UserAuthorityMutationResult> TryCreateInitialAdminAsync(
        User adminUser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adminUser);
        if (adminUser.Role != UserRole.Admin || !adminUser.IsActive || adminUser.IsDeleted)
        {
            throw new ArgumentException("The initial user must be an active Admin.", nameof(adminUser));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await EnsureInstallationStateAsync(cancellationToken);

        var affected = await _context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "InstallationStates"
            SET "IsCompleted" = 1,
                "CompletedAtUtc" = CURRENT_TIMESTAMP,
                "Revision" = "Revision" + 1
            WHERE "Id" = 1
              AND "IsCompleted" = 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM "Users"
                  WHERE "IsDeleted" = 0
              );
            """,
            cancellationToken);

        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return UserAuthorityMutationResult.Failed(
                UserAuthorityMutationStatus.InstallationAlreadyCompleted);
        }

        await _dbSet.AddAsync(adminUser, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserAuthorityMutationResult.Succeeded(adminUser);
    }

    public async Task<UserAuthorityMutationResult> TryUpdatePreservingActiveAdminAsync(
        Guid userId,
        string? displayName,
        UserRole role,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? null
            : displayName.Trim();
        var modifiedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Users"
            SET "DisplayName" = COALESCE({normalizedDisplayName}, "DisplayName"),
                "Role" = {(int)role},
                "IsActive" = {isActive},
                "ModifiedAt" = {modifiedAtUtc}
            WHERE "Id" = {userId}
              AND "IsDeleted" = 0
              AND (
                  "Role" <> {(int)UserRole.Admin}
                  OR "IsActive" = 0
                  OR ({(role == UserRole.Admin && isActive)} = 1)
                  OR EXISTS (
                      SELECT 1
                      FROM "Users" AS "other"
                      WHERE "other"."Id" <> {userId}
                        AND "other"."Role" = {(int)UserRole.Admin}
                        AND "other"."IsActive" = 1
                        AND "other"."IsDeleted" = 0
                  )
              );
            """,
            cancellationToken);

        if (affected != 1)
        {
            var exists = await _dbSet
                .AsNoTracking()
                .AnyAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return UserAuthorityMutationResult.Failed(
                exists
                    ? UserAuthorityMutationStatus.LastActiveAdmin
                    : UserAuthorityMutationStatus.NotFound);
        }

        var updatedUser = await _dbSet
            .AsNoTracking()
            .SingleAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserAuthorityMutationResult.Succeeded(updatedUser);
    }

    public async Task<UserAuthorityMutationResult> TryDeletePreservingActiveAdminAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM "Users"
            WHERE "Id" = {userId}
              AND "IsDeleted" = 0
              AND (
                  "Role" <> {(int)UserRole.Admin}
                  OR "IsActive" = 0
                  OR EXISTS (
                      SELECT 1
                      FROM "Users" AS "other"
                      WHERE "other"."Id" <> {userId}
                        AND "other"."Role" = {(int)UserRole.Admin}
                        AND "other"."IsActive" = 1
                        AND "other"."IsDeleted" = 0
                  )
              );
            """,
            cancellationToken);

        if (affected != 1)
        {
            var exists = await _dbSet
                .AsNoTracking()
                .AnyAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return UserAuthorityMutationResult.Failed(
                exists
                    ? UserAuthorityMutationStatus.LastActiveAdmin
                    : UserAuthorityMutationStatus.NotFound);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UserAuthorityMutationResult(UserAuthorityMutationStatus.Success);
    }

    private async Task EnsureInstallationStateAsync(CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT OR IGNORE INTO "InstallationStates"
                ("Id", "IsCompleted", "CompletedAtUtc", "Revision")
            SELECT 1,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
                   ) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
                   ) THEN CURRENT_TIMESTAMP ELSE NULL END,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
                   ) THEN 1 ELSE 0 END;
            """,
            cancellationToken);

        // One-way defensive backfill for databases that acquired users before the latch existed.
        await _context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "InstallationStates"
            SET "IsCompleted" = 1,
                "CompletedAtUtc" = COALESCE("CompletedAtUtc", CURRENT_TIMESTAMP),
                "Revision" = "Revision" + 1
            WHERE "Id" = 1
              AND "IsCompleted" = 0
              AND EXISTS (
                  SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
              );
            """,
            cancellationToken);
    }
}
