using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Infrastructure.Security;

/// <summary>
/// Explicit break-glass operation for the local console recovery tool. This service is deliberately
/// not registered in the Desktop HTTP service collection.
/// </summary>
public sealed class LocalAdminRecoveryService
{
    private readonly VisionDbContext _dbContext;

    public LocalAdminRecoveryService(VisionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LocalAdminRecoveryResult> RecoverAsync(
        string username,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        var normalizedUsername = username.Trim();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(
                candidate => candidate.Username == normalizedUsername,
                cancellationToken);
        var wasCreated = user == null;

        if (user == null)
        {
            user = User.Create(
                normalizedUsername,
                passwordHash,
                normalizedUsername,
                UserRole.Admin);
            await _dbContext.Users.AddAsync(user, cancellationToken);
        }
        else
        {
            user.Restore();
            user.Activate();
            user.ChangeRole(UserRole.Admin);
            user.ChangePassword(passwordHash);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT OR IGNORE INTO "InstallationStates"
                ("Id", "IsCompleted", "CompletedAtUtc", "Revision")
            VALUES (1, 1, CURRENT_TIMESTAMP, 1);

            UPDATE "InstallationStates"
            SET "IsCompleted" = 1,
                "CompletedAtUtc" = COALESCE("CompletedAtUtc", CURRENT_TIMESTAMP),
                "Revision" = "Revision" + 1
            WHERE "Id" = 1;
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LocalAdminRecoveryResult(user.Id, normalizedUsername, wasCreated);
    }
}

public sealed record LocalAdminRecoveryResult(Guid UserId, string Username, bool WasCreated);
