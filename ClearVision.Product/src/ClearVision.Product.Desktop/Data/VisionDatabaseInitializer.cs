using System.Data;
using System.Data.Common;
using ClearVision.Product.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Desktop.Data;

internal static class VisionDatabaseInitializer
{
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static async Task InitializeAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(dbContext, options: null, cancellationToken);
    }

    public static async Task InitializeAsync(
        VisionDbContext dbContext,
        VisionDatabaseInitializationOptions? options,
        CancellationToken cancellationToken = default)
    {
        var handledOutdatedDatabase = false;

        while (true)
        {
            try
            {
                await InitializeCoreAsync(dbContext, cancellationToken);
                return;
            }
            catch (OutdatedVisionDatabaseException ex) when (
                options?.OutdatedDatabaseDecisionProvider != null &&
                !handledOutdatedDatabase)
            {
                var decision = await options.OutdatedDatabaseDecisionProvider(ex.Database, cancellationToken);
                if (decision != OutdatedVisionDatabaseDecision.Discard)
                {
                    throw;
                }

                await DiscardSqliteDatabaseAsync(dbContext, ex.Database.DatabasePath, cancellationToken);
                handledOutdatedDatabase = true;
            }
        }
    }

    private static async Task InitializeCoreAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var migrations = dbContext.Database.GetMigrations().ToList();
        if (migrations.Count > 0)
        {
            if (dbContext.Database.IsSqlite())
            {
                await AdoptCompleteLegacySqliteSchemaAsync(dbContext, migrations, cancellationToken);
            }

            try
            {
                await dbContext.Database.MigrateAsync(cancellationToken);
            }
            catch (SqliteException ex) when (IsSqliteSchemaConflict(ex))
            {
                throw CreateOutdatedDatabaseMigrationException(dbContext, ex);
            }
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (dbContext.Database.IsSqlite())
        {
            await VisionDatabaseMaintenance.ApplyPostMigrationMaintenanceAsync(dbContext, cancellationToken);
        }
    }

    private static OutdatedVisionDatabaseException CreateOutdatedDatabaseMigrationException(
        VisionDbContext dbContext,
        SqliteException exception)
    {
        return new OutdatedVisionDatabaseException(
            new OutdatedVisionDatabase(
                VisionDatabaseMaintenance.GetDatabasePath(dbContext.Database.GetDbConnection()),
                new[] { "migration:" + exception.Message }),
            "Existing SQLite database could not be migrated because its schema conflicts with the current EF baseline. " +
            exception.Message);
    }

    private static bool IsSqliteSchemaConflict(SqliteException exception)
    {
        if (exception.SqliteErrorCode != 1)
        {
            return false;
        }

        return exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AdoptCompleteLegacySqliteSchemaAsync(
        VisionDbContext dbContext,
        IReadOnlyList<string> migrationIds,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (await VisionDatabaseMaintenance.TableExistsAsync(connection, MigrationsHistoryTable, cancellationToken))
            {
                return;
            }

            var existingTables = await VisionDatabaseMaintenance.GetExistingUserTablesAsync(connection, cancellationToken);
            if (existingTables.Count == 0)
            {
                return;
            }

            var hasLegacyCoreTables =
                await VisionDatabaseMaintenance.TableExistsAsync(connection, "Projects", cancellationToken) &&
                await VisionDatabaseMaintenance.TableExistsAsync(connection, "Operators", cancellationToken) &&
                await VisionDatabaseMaintenance.TableExistsAsync(connection, "InspectionResults", cancellationToken) &&
                await VisionDatabaseMaintenance.TableExistsAsync(connection, "Defects", cancellationToken) &&
                await VisionDatabaseMaintenance.TableExistsAsync(connection, "Users", cancellationToken);

            var missingSchemaItems = await VisionDatabaseMaintenance.GetMissingBaselineSqliteSchemaItemsAsync(
                dbContext,
                connection,
                cancellationToken);

            if (missingSchemaItems.Count > 0 &&
                hasLegacyCoreTables &&
                VisionDatabaseMaintenance.IsRepairableLegacySqliteSchemaGap(missingSchemaItems))
            {
                await VisionDatabaseMaintenance.RepairLegacySqliteSchemaAsync(dbContext, cancellationToken);

                missingSchemaItems = await VisionDatabaseMaintenance.GetMissingBaselineSqliteSchemaItemsAsync(
                    dbContext,
                    connection,
                    cancellationToken);
            }

            if (missingSchemaItems.Count > 0)
            {
                var preview = string.Join(", ", missingSchemaItems.Take(8));
                var suffix = missingSchemaItems.Count > 8 ? $" and {missingSchemaItems.Count - 8} more" : string.Empty;
                throw new OutdatedVisionDatabaseException(new OutdatedVisionDatabase(
                    VisionDatabaseMaintenance.GetDatabasePath(connection),
                    missingSchemaItems),
                    "Existing SQLite database looks like a legacy ClearVision database, but its schema is not complete enough " +
                    $"to adopt the EF migration baseline. Missing schema items: {preview}{suffix}.");
            }

            await using var createHistoryCommand = (DbCommand)connection.CreateCommand();
            createHistoryCommand.CommandText = $"""
                CREATE TABLE "{MigrationsHistoryTable}" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """;
            await createHistoryCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var migrationId in migrationIds)
            {
                await using var insertHistoryCommand = (DbCommand)connection.CreateCommand();
                insertHistoryCommand.CommandText = $"""
                    INSERT INTO "{MigrationsHistoryTable}" ("MigrationId", "ProductVersion")
                    VALUES ($migrationId, $productVersion);
                    """;
                var migrationIdParameter = insertHistoryCommand.CreateParameter();
                migrationIdParameter.ParameterName = "$migrationId";
                migrationIdParameter.Value = migrationId;
                insertHistoryCommand.Parameters.Add(migrationIdParameter);

                var productVersionParameter = insertHistoryCommand.CreateParameter();
                productVersionParameter.ParameterName = "$productVersion";
                productVersionParameter.Value = "8.0.0";
                insertHistoryCommand.Parameters.Add(productVersionParameter);

                await insertHistoryCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task DiscardSqliteDatabaseAsync(
        VisionDbContext dbContext,
        string databasePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databasePath) ||
            string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The outdated SQLite database path is not a removable file path.");
        }

        await dbContext.Database.GetDbConnection().CloseAsync();
        SqliteConnection.ClearAllPools();

        foreach (var path in VisionDatabaseMaintenance.GetSqliteDatabaseFiles(databasePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

internal sealed class VisionDatabaseInitializationOptions
{
    public Func<OutdatedVisionDatabase, CancellationToken, Task<OutdatedVisionDatabaseDecision>>? OutdatedDatabaseDecisionProvider { get; init; }
}

internal enum OutdatedVisionDatabaseDecision
{
    Keep,
    Discard
}

internal sealed record OutdatedVisionDatabase(
    string DatabasePath,
    IReadOnlyList<string> MissingSchemaItems);

internal sealed class OutdatedVisionDatabaseException : InvalidOperationException
{
    public OutdatedVisionDatabaseException(OutdatedVisionDatabase database, string message)
        : base(message)
    {
        Database = database;
    }

    public OutdatedVisionDatabase Database { get; }
}
