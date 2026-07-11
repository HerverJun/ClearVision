using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Data;

public sealed class VisionDatabaseMaintenanceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VisionDatabaseMaintenanceService> _logger;
    private readonly VisionDatabaseMaintenanceOptions _options;

    public VisionDatabaseMaintenanceService(
        IServiceScopeFactory scopeFactory,
        ILogger<VisionDatabaseMaintenanceService> logger)
        : this(scopeFactory, logger, VisionDatabaseMaintenanceOptions.CreateDefault())
    {
    }

    internal VisionDatabaseMaintenanceService(
        IServiceScopeFactory scopeFactory,
        ILogger<VisionDatabaseMaintenanceService> logger,
        VisionDatabaseMaintenanceOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    public async Task<VisionDatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();

        try
        {
            return await BuildStatusAsync(dbContext, runIntegrityCheck: true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Vision database status.");
            return new VisionDatabaseStatus
            {
                DatabasePath = VisionDatabaseMaintenance.GetDatabasePath(dbContext.Database.GetDbConnection()),
                Exists = false,
                State = VisionDatabaseState.Error,
                Issues = [ex.Message],
                CurrentSchemaVersion = VisionDatabaseMaintenance.CurrentSqliteSchemaVersion
            };
        }
    }

    public async Task<VisionDatabaseBackupResult> CreateBackupAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var databasePath = VisionDatabaseMaintenance.GetDatabasePath(dbContext.Database.GetDbConnection());
        ValidateFileBackedDatabase(databasePath);

        Directory.CreateDirectory(_options.BackupRootDirectory);
        var timestamp = DateTimeOffset.UtcNow;
        var backupPath = Path.Combine(
            _options.BackupRootDirectory,
            $"clearvision-db-{timestamp:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.cvdbbak");
        var tempRoot = Path.Combine(Path.GetTempPath(), "ClearVisionDatabaseBackups", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempRoot);
            var tempDatabaseDirectory = Path.Combine(tempRoot, "db");
            Directory.CreateDirectory(tempDatabaseDirectory);
            var tempDatabasePath = Path.Combine(tempDatabaseDirectory, "vision.db");

            await BackupSqliteDatabaseAsync(databasePath, tempDatabasePath, cancellationToken);

            var packageFileCount = 0;
            var packageBytes = 0L;
            if (Directory.Exists(_options.PackageRootDirectory))
            {
                var packageBackupDirectory = Path.Combine(tempRoot, "packages");
                CopyDirectory(_options.PackageRootDirectory, packageBackupDirectory);
                foreach (var file in Directory.EnumerateFiles(packageBackupDirectory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    packageFileCount++;
                    packageBytes += new FileInfo(file).Length;
                }
            }

            var manifest = new VisionDatabaseBackupManifest
            {
                CreatedAtUtc = timestamp,
                Reason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim(),
                SourceDatabasePath = databasePath,
                SchemaVersion = await VisionDatabaseMaintenance.GetUserVersionForPathAsync(tempDatabasePath, cancellationToken),
                CurrentSchemaVersion = VisionDatabaseMaintenance.CurrentSqliteSchemaVersion,
                PackageRootDirectory = _options.PackageRootDirectory,
                PackageFileCount = packageFileCount
            };
            await File.WriteAllTextAsync(
                Path.Combine(tempRoot, "manifest.json"),
                JsonSerializer.Serialize(manifest, VisionDatabaseMaintenance.JsonOptions),
                cancellationToken);

            ZipFile.CreateFromDirectory(tempRoot, backupPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            var backupFile = new FileInfo(backupPath);
            return new VisionDatabaseBackupResult
            {
                BackupPath = backupPath,
                CreatedAtUtc = timestamp,
                SizeBytes = backupFile.Length,
                DatabaseSizeBytes = new FileInfo(tempDatabasePath).Length,
                PackageFileCount = packageFileCount,
                PackageBytes = packageBytes
            };
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    public async Task<VisionDatabaseRestoreResult> RestoreBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("Backup path is required.", nameof(backupPath));
        }

        var fullBackupPath = Path.GetFullPath(backupPath.Trim());
        if (!File.Exists(fullBackupPath))
        {
            throw new FileNotFoundException("Database backup file was not found.", fullBackupPath);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var databasePath = VisionDatabaseMaintenance.GetDatabasePath(dbContext.Database.GetDbConnection());
        ValidateFileBackedDatabase(databasePath);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ClearVisionDatabaseRestore", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempRoot);
            var extension = Path.GetExtension(fullBackupPath);
            string restoredDatabasePath;
            string? restoredPackagesRoot = null;

            if (string.Equals(extension, ".db", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".sqlite", StringComparison.OrdinalIgnoreCase))
            {
                if (PathsReferToSameFile(fullBackupPath, databasePath))
                {
                    throw new InvalidOperationException("Cannot restore the live database file onto itself.");
                }

                var tempDatabaseDirectory = Path.Combine(tempRoot, "db");
                Directory.CreateDirectory(tempDatabaseDirectory);
                restoredDatabasePath = Path.Combine(tempDatabaseDirectory, "vision.db");
                await BackupSqliteDatabaseAsync(fullBackupPath, restoredDatabasePath, cancellationToken);
            }
            else
            {
                ExtractZipSafely(fullBackupPath, tempRoot);
                restoredDatabasePath = Path.Combine(tempRoot, "db", "vision.db");
                restoredPackagesRoot = Path.Combine(tempRoot, "packages");
            }

            if (!File.Exists(restoredDatabasePath))
            {
                throw new InvalidOperationException("Backup does not contain db/vision.db.");
            }

            var safetyBackup = await CreateBackupAsync("pre-restore", cancellationToken);
            await dbContext.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();

            foreach (var path in VisionDatabaseMaintenance.GetSqliteDatabaseFiles(databasePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            File.Copy(restoredDatabasePath, databasePath, overwrite: true);

            var restoredPackageFileCount = 0;
            if (!string.IsNullOrWhiteSpace(restoredPackagesRoot) && Directory.Exists(restoredPackagesRoot))
            {
                Directory.CreateDirectory(_options.PackageRootDirectory);
                CopyDirectory(restoredPackagesRoot, _options.PackageRootDirectory);
                restoredPackageFileCount = Directory
                    .EnumerateFiles(restoredPackagesRoot, "*", SearchOption.AllDirectories)
                    .Count();
            }

            SqliteConnection.ClearAllPools();
            await using (var verifyScope = _scopeFactory.CreateAsyncScope())
            {
                var verifyContext = verifyScope.ServiceProvider.GetRequiredService<VisionDbContext>();
                await VisionDatabaseInitializer.InitializeAsync(verifyContext, cancellationToken);
            }

            return new VisionDatabaseRestoreResult
            {
                RestoredDatabasePath = databasePath,
                BackupPath = fullBackupPath,
                SafetyBackupPath = safetyBackup.BackupPath,
                RestoredPackageFileCount = restoredPackageFileCount,
                Status = await GetStatusAsync(cancellationToken)
            };
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    public async Task<VisionDatabaseCleanupResult> CleanupHistoryAsync(
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var normalizedRetentionDays = Math.Clamp(retentionDays, 1, 3650);
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-normalizedRetentionDays);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var deletedRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            deletedRows["Defects"] = await ExecuteDeleteAsync(
                connection,
                """
                DELETE FROM "Defects"
                WHERE "InspectionResultId" IN (
                    SELECT "Id" FROM "InspectionResults" WHERE "InspectionTime" < $cutoffDateTime
                );
                """,
                cutoffUtc,
                cancellationToken);
            deletedRows["InspectionResults"] = await ExecuteDeleteAsync(
                connection,
                """DELETE FROM "InspectionResults" WHERE "InspectionTime" < $cutoffDateTime;""",
                cutoffUtc,
                cancellationToken);
            deletedRows["StationResultSummaries"] = await ExecuteDeleteAsync(
                connection,
                """DELETE FROM "StationResultSummaries" WHERE "CompletedAtUtc" < $cutoffDateTimeOffset;""",
                cutoffUtc,
                cancellationToken);
            deletedRows["StationHealthSnapshots"] = await ExecuteDeleteAsync(
                connection,
                """DELETE FROM "StationHealthSnapshots" WHERE "CreatedAtUtc" < $cutoffDateTimeOffset;""",
                cutoffUtc,
                cancellationToken);
            deletedRows["StationLogSummaries"] = await ExecuteDeleteAsync(
                connection,
                """DELETE FROM "StationLogSummaries" WHERE "TimestampUtc" < $cutoffDateTimeOffset;""",
                cutoffUtc,
                cancellationToken);
            deletedRows["StationConnectionEvents"] = await ExecuteDeleteAsync(
                connection,
                """DELETE FROM "StationConnectionEvents" WHERE "CreatedAtUtc" < $cutoffDateTimeOffset;""",
                cutoffUtc,
                cancellationToken);

            if (dbContext.Database.IsSqlite())
            {
                await dbContext.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
            }

            return new VisionDatabaseCleanupResult
            {
                RetentionDays = normalizedRetentionDays,
                CutoffUtc = cutoffUtc,
                DeletedRows = deletedRows
            };
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<VisionDatabaseStatus> RepairAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        await VisionDatabaseInitializer.InitializeAsync(dbContext, cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    private async Task<VisionDatabaseStatus> BuildStatusAsync(
        VisionDbContext dbContext,
        bool runIntegrityCheck,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var databasePath = VisionDatabaseMaintenance.GetDatabasePath(connection);
        var exists = !string.IsNullOrWhiteSpace(databasePath) &&
            !string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(databasePath);

        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var schemaVersion = dbContext.Database.IsSqlite()
                ? await VisionDatabaseMaintenance.GetUserVersionAsync(connection, cancellationToken)
                : 0;
            var missingSchemaItems = dbContext.Database.IsSqlite()
                ? await VisionDatabaseMaintenance.GetMissingBaselineSqliteSchemaItemsAsync(dbContext, connection, cancellationToken)
                : [];
            var appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            var integrity = runIntegrityCheck && dbContext.Database.IsSqlite()
                ? await VisionDatabaseMaintenance.RunIntegrityCheckAsync(connection, cancellationToken)
                : "not-run";
            var foreignKeyViolations = runIntegrityCheck && dbContext.Database.IsSqlite()
                ? await VisionDatabaseMaintenance.CountForeignKeyViolationsAsync(connection, cancellationToken)
                : 0;
            var rowCounts = await VisionDatabaseMaintenance.GetKnownTableCountsAsync(connection, cancellationToken);
            var issues = new List<string>();

            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(integrity, "not-run", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("integrity:" + integrity);
            }

            if (foreignKeyViolations > 0)
            {
                issues.Add($"foreign-key-violations:{foreignKeyViolations}");
            }

            issues.AddRange(missingSchemaItems.Select(item => "missing:" + item));
            issues.AddRange(pendingMigrations.Select(item => "pending-migration:" + item));

            var state = ResolveState(exists, integrity, missingSchemaItems, pendingMigrations, foreignKeyViolations);
            return new VisionDatabaseStatus
            {
                DatabasePath = databasePath,
                Exists = exists,
                State = state,
                SchemaVersion = schemaVersion,
                CurrentSchemaVersion = VisionDatabaseMaintenance.CurrentSqliteSchemaVersion,
                AppliedMigrations = appliedMigrations,
                PendingMigrations = pendingMigrations,
                MissingSchemaItems = missingSchemaItems,
                IntegrityCheck = integrity,
                ForeignKeyViolationCount = foreignKeyViolations,
                RowCounts = rowCounts,
                Issues = issues,
                DatabaseSizeBytes = exists ? new FileInfo(databasePath).Length : 0,
                WalSizeBytes = File.Exists(databasePath + "-wal") ? new FileInfo(databasePath + "-wal").Length : 0,
                BackupRootDirectory = _options.BackupRootDirectory,
                PackageRootDirectory = _options.PackageRootDirectory,
                PackageFileCount = Directory.Exists(_options.PackageRootDirectory)
                    ? Directory.EnumerateFiles(_options.PackageRootDirectory, "*", SearchOption.AllDirectories).Count()
                    : 0
            };
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string ResolveState(
        bool exists,
        string integrity,
        IReadOnlyCollection<string> missingSchemaItems,
        IReadOnlyCollection<string> pendingMigrations,
        int foreignKeyViolations)
    {
        if (!exists)
        {
            return VisionDatabaseState.Missing;
        }

        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(integrity, "not-run", StringComparison.OrdinalIgnoreCase))
        {
            return VisionDatabaseState.Corrupt;
        }

        if (foreignKeyViolations > 0 || missingSchemaItems.Count > 0)
        {
            return VisionDatabaseState.NeedsRepair;
        }

        if (pendingMigrations.Count > 0)
        {
            return VisionDatabaseState.PendingMigration;
        }

        return VisionDatabaseState.Healthy;
    }

    private static async Task BackupSqliteDatabaseAsync(
        string databasePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(
            VisionDatabaseMaintenance.CreateSqliteConnectionString(databasePath, pooling: false));
        await source.OpenAsync(cancellationToken);

        await using (var checkpoint = source.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(FULL);";
            await checkpoint.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var destination = new SqliteConnection(
            VisionDatabaseMaintenance.CreateSqliteConnectionString(destinationPath, pooling: false));
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task<int> ExecuteDeleteAsync(
        DbConnection connection,
        string sql,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(CreateParameter(command, "$cutoffDateTime", cutoffUtc.UtcDateTime));
        command.Parameters.Add(CreateParameter(command, "$cutoffDateTimeOffset", cutoffUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbParameter CreateParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    private static void ValidateFileBackedDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) ||
            string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The current SQLite database is not a file-backed database.");
        }
    }

    private static bool PathsReferToSameFile(string firstPath, string secondPath)
    {
        return string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static void ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        var destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(destinationDirectory));
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Backup contains an unsafe relative path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

internal sealed class VisionDatabaseMaintenanceOptions
{
    public string BackupRootDirectory { get; init; } = string.Empty;

    public string PackageRootDirectory { get; init; } = string.Empty;

    public static VisionDatabaseMaintenanceOptions CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new VisionDatabaseMaintenanceOptions
        {
            BackupRootDirectory = Path.Combine(localApplicationData, "ClearVision", "database-backups"),
            PackageRootDirectory = Path.Combine(localApplicationData, "ClearVisionStudio", "packages")
        };
    }
}

public static class VisionDatabaseState
{
    public const string Healthy = "Healthy";
    public const string Missing = "Missing";
    public const string PendingMigration = "PendingMigration";
    public const string NeedsRepair = "NeedsRepair";
    public const string Corrupt = "Corrupt";
    public const string Error = "Error";
}

public sealed class VisionDatabaseStatus
{
    public string DatabasePath { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public string State { get; init; } = VisionDatabaseState.Error;

    public int SchemaVersion { get; init; }

    public int CurrentSchemaVersion { get; init; }

    public IReadOnlyList<string> AppliedMigrations { get; init; } = [];

    public IReadOnlyList<string> PendingMigrations { get; init; } = [];

    public IReadOnlyList<string> MissingSchemaItems { get; init; } = [];

    public string IntegrityCheck { get; init; } = "not-run";

    public int ForeignKeyViolationCount { get; init; }

    public IReadOnlyDictionary<string, long> RowCounts { get; init; } = new Dictionary<string, long>();

    public IReadOnlyList<string> Issues { get; init; } = [];

    public long DatabaseSizeBytes { get; init; }

    public long WalSizeBytes { get; init; }

    public string BackupRootDirectory { get; init; } = string.Empty;

    public string PackageRootDirectory { get; init; } = string.Empty;

    public int PackageFileCount { get; init; }
}

public sealed class VisionDatabaseBackupResult
{
    public string BackupPath { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public long SizeBytes { get; init; }

    public long DatabaseSizeBytes { get; init; }

    public int PackageFileCount { get; init; }

    public long PackageBytes { get; init; }
}

public sealed class VisionDatabaseRestoreResult
{
    public string RestoredDatabasePath { get; init; } = string.Empty;

    public string BackupPath { get; init; } = string.Empty;

    public string SafetyBackupPath { get; init; } = string.Empty;

    public int RestoredPackageFileCount { get; init; }

    public VisionDatabaseStatus? Status { get; init; }
}

public sealed class VisionDatabaseCleanupResult
{
    public int RetentionDays { get; init; }

    public DateTimeOffset CutoffUtc { get; init; }

    public IReadOnlyDictionary<string, int> DeletedRows { get; init; } = new Dictionary<string, int>();
}

public sealed class VisionDatabaseBackupManifest
{
    public DateTimeOffset CreatedAtUtc { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string SourceDatabasePath { get; init; } = string.Empty;

    public int SchemaVersion { get; init; }

    public int CurrentSchemaVersion { get; init; }

    public string PackageRootDirectory { get; init; } = string.Empty;

    public int PackageFileCount { get; init; }
}

public sealed class VisionDatabaseCleanupRequest
{
    public int? RetentionDays { get; init; }
}

public sealed class VisionDatabaseRestoreRequest
{
    public string? BackupPath { get; init; }
}

internal static class VisionDatabaseMaintenance
{
    public const int CurrentSqliteSchemaVersion = 5;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task ApplyPostMigrationMaintenanceAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsSqlite())
        {
            return;
        }

        await RepairStationPackageKindColumnAsync(dbContext, cancellationToken);
        await EnsureStationCanonicalOutcomeSchemaAsync(dbContext, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        await SetUserVersionAsync(dbContext.Database.GetDbConnection(), CurrentSqliteSchemaVersion, cancellationToken);
    }

    public static bool IsRepairableLegacySqliteSchemaGap(IReadOnlyCollection<string> missingSchemaItems)
    {
        return missingSchemaItems.All(item => RepairableLegacySqliteSchemaItems.Contains(item));
    }

    public static async Task RepairLegacySqliteSchemaAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(
                dbContext.Database.GetDbConnection(),
                "InspectionResults",
                "AnalysisDataJson",
                cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "InspectionResults" ADD COLUMN "AnalysisDataJson" TEXT NULL;""",
                cancellationToken);
        }

        foreach (var column in new[]
                 {
                     (Name: "ExecutionOutcome", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ExecutionOutcome\" INTEGER NULL;"),
                     (Name: "DecisionOutcome", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"DecisionOutcome\" INTEGER NULL;"),
                     (Name: "DecisionSource", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"DecisionSource\" TEXT NULL;"),
                     (Name: "ReasonCode", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ReasonCode\" TEXT NULL;"),
                     (Name: "HasJudgmentSignal", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"HasJudgmentSignal\" INTEGER NULL;")
                 })
        {
            if (!await ColumnExistsAsync(
                    dbContext.Database.GetDbConnection(),
                    "InspectionResults",
                    column.Name,
                    cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(column.Statement, cancellationToken);
            }
        }

        if (!await ColumnExistsAsync(
                dbContext.Database.GetDbConnection(),
                "Projects",
                "Flow_DecisionConfiguration",
                cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Projects" ADD COLUMN "Flow_DecisionConfiguration" TEXT NULL;""",
                cancellationToken);
        }

        foreach (var statement in LegacyStationSyncSchemaStatements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await EnsureStationCanonicalOutcomeSchemaAsync(dbContext, cancellationToken);

        if (!await ColumnExistsAsync(
                dbContext.Database.GetDbConnection(),
                "StationPackageRecords",
                "PackageKind",
                cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "StationPackageRecords" ADD COLUMN "PackageKind" TEXT NOT NULL DEFAULT 'Production';""",
                cancellationToken);
        }
    }

    private static async Task EnsureStationCanonicalOutcomeSchemaAsync(
        VisionDbContext dbContext,
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
            if (!await TableExistsAsync(connection, "StationResultSummaries", cancellationToken))
            {
                return;
            }

            foreach (var column in new[]
                     {
                         (Name: "ExecutionOutcome", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"ExecutionOutcome\" TEXT NULL;"),
                         (Name: "DecisionOutcome", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"DecisionOutcome\" TEXT NULL;"),
                         (Name: "HasJudgmentSignal", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"HasJudgmentSignal\" INTEGER NULL;"),
                         (Name: "DecisionSource", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"DecisionSource\" TEXT NULL;"),
                         (Name: "ReasonCode", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"ReasonCode\" TEXT NULL;")
                     })
            {
                if (!await ColumnExistsAsync(connection, "StationResultSummaries", column.Name, cancellationToken))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(column.Statement, cancellationToken);
                }
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_ExecutionOutcome_DecisionOutcome"
                ON "StationResultSummaries" ("StationId", "ExecutionOutcome", "DecisionOutcome");
                """,
                cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static async Task<List<string>> GetMissingBaselineSqliteSchemaItemsAsync(
        VisionDbContext dbContext,
        IDbConnection connection,
        CancellationToken cancellationToken,
        bool stopAfterFirstMissing = false)
    {
        var missingItems = new List<string>();
        var requirements = GetBaselineSchemaRequirements(dbContext);

        foreach (var (tableName, tableRequirement) in requirements.Tables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!await TableExistsAsync(connection, tableName, cancellationToken))
            {
                missingItems.Add($"table:{tableName}");
                if (stopAfterFirstMissing)
                {
                    return missingItems;
                }

                continue;
            }

            foreach (var columnName in tableRequirement.Columns.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                if (!await ColumnExistsAsync(connection, tableName, columnName, cancellationToken))
                {
                    missingItems.Add($"column:{tableName}.{columnName}");
                    if (stopAfterFirstMissing)
                    {
                        return missingItems;
                    }
                }
            }
        }

        foreach (var indexName in requirements.Indexes.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (!await IndexExistsAsync(connection, indexName, cancellationToken))
            {
                missingItems.Add($"index:{indexName}");
                if (stopAfterFirstMissing)
                {
                    return missingItems;
                }
            }
        }

        return missingItems;
    }

    public static async Task<Dictionary<string, long>> GetKnownTableCountsAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableName in KnownHistoryAndAuditTables)
        {
            if (!await TableExistsAsync(connection, tableName, cancellationToken))
            {
                continue;
            }

            await using var command = (DbCommand)connection.CreateCommand();
            command.CommandText = $"""SELECT COUNT(1) FROM "{tableName}";""";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            counts[tableName] = Convert.ToInt64(value);
        }

        return counts;
    }

    public static async Task<string> RunIntegrityCheckAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value?.ToString() ?? "unknown";
    }

    public static async Task<int> CountForeignKeyViolationsAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        var count = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
        }

        return count;
    }

    public static async Task<int> GetUserVersionForPathAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(CreateSqliteConnectionString(databasePath, pooling: false));
        await connection.OpenAsync(cancellationToken);
        return await GetUserVersionAsync(connection, cancellationToken);
    }

    public static string CreateSqliteConnectionString(string databasePath, bool pooling)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = pooling
        }.ToString();
    }

    public static async Task<int> GetUserVersionAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    public static async Task<bool> TableExistsAsync(
        IDbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        return await SchemaObjectExistsAsync(connection, "table", tableName, cancellationToken);
    }

    public static async Task<bool> ColumnExistsAsync(
        IDbConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static Task<bool> IndexExistsAsync(
        IDbConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        return SchemaObjectExistsAsync(connection, "index", indexName, cancellationToken);
    }

    public static async Task<HashSet<string>> GetExistingUserTablesAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                tableNames.Add(name);
            }
        }

        return tableNames;
    }

    public static string GetDatabasePath(IDbConnection connection)
    {
        var databasePath = connection.Database;
        if (connection is DbConnection dbConnection &&
            !string.IsNullOrWhiteSpace(dbConnection.DataSource))
        {
            databasePath = dbConnection.DataSource;
        }

        return databasePath;
    }

    public static IEnumerable<string> GetSqliteDatabaseFiles(string databasePath)
    {
        yield return databasePath;
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
    }

    private static async Task RepairStationPackageKindColumnAsync(
        VisionDbContext dbContext,
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
            if (!await TableExistsAsync(connection, "StationPackageRecords", cancellationToken))
            {
                return;
            }

            if (await ColumnExistsAsync(connection, "StationPackageRecords", "PackageKind", cancellationToken))
            {
                return;
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "StationPackageRecords" ADD COLUMN "PackageKind" TEXT NOT NULL DEFAULT 'Production';""",
                cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task SetUserVersionAsync(
        IDbConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        try
        {
            await using var command = (DbCommand)connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {version};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                if (connection is DbConnection dbConnection)
                {
                    await dbConnection.CloseAsync();
                }
                else
                {
                    connection.Close();
                }
            }
        }
    }

    private static async Task<bool> SchemaObjectExistsAsync(
        IDbConnection connection,
        string type,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = $type AND name = $name
            LIMIT 1;
            """;

        var typeParameter = command.CreateParameter();
        typeParameter.ParameterName = "$type";
        typeParameter.Value = type;
        command.Parameters.Add(typeParameter);

        var nameParameter = command.CreateParameter();
        nameParameter.ParameterName = "$name";
        nameParameter.Value = name;
        command.Parameters.Add(nameParameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    private static BaselineSchemaRequirements GetBaselineSchemaRequirements(VisionDbContext dbContext)
    {
        var requirements = new BaselineSchemaRequirements();

        foreach (var entityType in dbContext.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var tableRequirement = requirements.GetOrAddTable(tableName);

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    tableRequirement.Columns.Add(columnName);
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName(storeObject);
                if (!string.IsNullOrWhiteSpace(indexName))
                {
                    requirements.Indexes.Add(indexName);
                }
            }
        }

        return requirements;
    }

    private static readonly string[] KnownHistoryAndAuditTables =
    [
        "Projects",
        "InspectionResults",
        "Defects",
        "StationNodes",
        "StationPackageRecords",
        "StationResultSummaries",
        "StationHealthSnapshots",
        "StationLogSummaries",
        "StationConnectionEvents",
        "StationCommandRecords",
        "StationAuditRecords",
        "StationSyncCursors"
    ];

    private static readonly HashSet<string> RepairableLegacySqliteSchemaItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "column:InspectionResults.AnalysisDataJson",
        "column:InspectionResults.ExecutionOutcome",
        "column:InspectionResults.DecisionOutcome",
        "column:InspectionResults.DecisionSource",
        "column:InspectionResults.ReasonCode",
        "column:InspectionResults.HasJudgmentSignal",
        "column:Projects.Flow_DecisionConfiguration",
        "table:StationAlarmEvents",
        "table:StationAuditRecords",
        "table:StationCommandRecords",
        "table:StationConnectionEvents",
        "table:StationHealthSnapshots",
        "table:StationLogSummaries",
        "table:StationNodes",
        "table:StationPackageRecords",
        "column:StationPackageRecords.PackageKind",
        "table:StationResultSummaries",
        "table:StationSyncCursors",
        "column:StationResultSummaries.ExecutionOutcome",
        "column:StationResultSummaries.DecisionOutcome",
        "column:StationResultSummaries.HasJudgmentSignal",
        "column:StationResultSummaries.DecisionSource",
        "column:StationResultSummaries.ReasonCode",
        "index:IX_StationAlarmEvents_AlarmId",
        "index:IX_StationAlarmEvents_StationId_IsActive",
        "index:IX_StationAuditRecords_AuditId",
        "index:IX_StationAuditRecords_TargetStationId_CreatedAtUtc",
        "index:IX_StationCommandRecords_CommandId",
        "index:IX_StationCommandRecords_StationId_CreatedAtUtc",
        "index:IX_StationCommandRecords_StationId_Status",
        "index:IX_StationConnectionEvents_StationId_CreatedAtUtc",
        "index:IX_StationHealthSnapshots_StationId_CreatedAtUtc",
        "index:IX_StationHealthSnapshots_StationId_SequenceId",
        "index:IX_StationLogSummaries_StationId_SequenceId",
        "index:IX_StationLogSummaries_StationId_TimestampUtc",
        "index:IX_StationNodes_LastSeenAtUtc",
        "index:IX_StationNodes_StationId",
        "index:IX_StationPackageRecords_CreatedAtUtc",
        "index:IX_StationPackageRecords_PackageId",
        "index:IX_StationResultSummaries_MessageId",
        "index:IX_StationResultSummaries_StationId_CompletedAtUtc",
        "index:IX_StationResultSummaries_StationId_ExecutionOutcome_DecisionOutcome",
        "index:IX_StationResultSummaries_StationId_SequenceId",
        "index:IX_StationSyncCursors_StationId"
    };

    private static readonly string[] LegacyStationSyncSchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS "StationNodes" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationNodes" PRIMARY KEY AUTOINCREMENT,
            "StationId" TEXT NOT NULL,
            "StationName" TEXT NOT NULL DEFAULT '',
            "LineName" TEXT NULL,
            "AreaName" TEXT NULL,
            "WorkcellName" TEXT NULL,
            "InspectionNodeName" TEXT NULL,
            "CameraAlias" TEXT NULL,
            "StationRole" TEXT NOT NULL DEFAULT '',
            "Owner" TEXT NULL,
            "MachineName" TEXT NOT NULL DEFAULT '',
            "IpAddressHint" TEXT NULL,
            "MacAddressHash" TEXT NULL,
            "FirstSeenAtUtc" TEXT NOT NULL,
            "LastSeenAtUtc" TEXT NOT NULL,
            "LastHeartbeatAtUtc" TEXT NULL,
            "OnlineState" TEXT NOT NULL DEFAULT 'Unknown',
            "RuntimeState" TEXT NOT NULL DEFAULT 'Unknown',
            "CurrentPackageId" TEXT NULL,
            "CurrentPackageName" TEXT NULL,
            "CurrentPackageVersion" TEXT NULL,
            "IsEnabled" INTEGER NOT NULL DEFAULT 1,
            "Remark" TEXT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationResultSummaries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationResultSummaries" PRIMARY KEY AUTOINCREMENT,
            "StationId" TEXT NOT NULL,
            "SequenceId" INTEGER NOT NULL,
            "MessageId" TEXT NOT NULL,
            "RunId" TEXT NOT NULL,
            "PackageId" TEXT NOT NULL,
            "PackageName" TEXT NOT NULL,
            "PackageVersion" TEXT NOT NULL,
            "FlowHash" TEXT NOT NULL,
            "ImageId" TEXT NOT NULL,
            "Outcome" TEXT NOT NULL,
            "InspectionStatus" TEXT NULL,
            "ExecutionTimeMs" INTEGER NOT NULL,
            "DiagnosticCode" TEXT NOT NULL,
            "DiagnosticMessage" TEXT NULL,
            "PrimaryOutputsPreviewJson" TEXT NOT NULL,
            "StartedAtUtc" TEXT NOT NULL,
            "CompletedAtUtc" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "ReceivedAtUtc" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationHealthSnapshots" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationHealthSnapshots" PRIMARY KEY AUTOINCREMENT,
            "StationId" TEXT NOT NULL,
            "SequenceId" INTEGER NOT NULL,
            "MessageId" TEXT NOT NULL,
            "RuntimeState" TEXT NOT NULL,
            "ProcessUptimeSeconds" INTEGER NOT NULL,
            "CpuUsagePercent" REAL NULL,
            "WorkingSetMb" INTEGER NOT NULL,
            "PrivateMemoryMb" INTEGER NOT NULL,
            "DiskFreeMb" INTEGER NOT NULL,
            "DiskTotalMb" INTEGER NOT NULL,
            "SpoolPendingCount" INTEGER NOT NULL,
            "SpoolBytes" INTEGER NOT NULL,
            "CameraStatusSummary" TEXT NULL,
            "PlcStatusSummary" TEXT NULL,
            "CurrentPackageId" TEXT NULL,
            "CurrentPackageHealth" TEXT NULL,
            "LastErrorCode" TEXT NULL,
            "LastErrorMessage" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "ReceivedAtUtc" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationConnectionEvents" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationConnectionEvents" PRIMARY KEY AUTOINCREMENT,
            "StationId" TEXT NOT NULL,
            "EventType" TEXT NOT NULL,
            "Message" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationAlarmEvents" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationAlarmEvents" PRIMARY KEY AUTOINCREMENT,
            "AlarmId" TEXT NOT NULL,
            "StationId" TEXT NOT NULL,
            "Severity" TEXT NOT NULL,
            "Code" TEXT NOT NULL,
            "Message" TEXT NOT NULL,
            "IsActive" INTEGER NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "UpdatedAtUtc" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationCommandRecords" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationCommandRecords" PRIMARY KEY AUTOINCREMENT,
            "CommandId" TEXT NOT NULL,
            "StationId" TEXT NOT NULL,
            "CommandType" TEXT NOT NULL,
            "PayloadJson" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "ProgressPercent" INTEGER NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "ExpiresAtUtc" TEXT NOT NULL,
            "DeliveredAtUtc" TEXT NULL,
            "AcceptedAtUtc" TEXT NULL,
            "StartedAtUtc" TEXT NULL,
            "CompletedAtUtc" TEXT NULL,
            "IssuedBy" TEXT NOT NULL,
            "CorrelationId" TEXT NOT NULL,
            "ResultMessage" TEXT NULL,
            "ErrorCode" TEXT NULL,
            "ErrorDetail" TEXT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationSyncCursors" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationSyncCursors" PRIMARY KEY AUTOINCREMENT,
            "StationId" TEXT NOT NULL,
            "LastPersistedSequenceId" INTEGER NOT NULL,
            "LastReceivedHealthSequenceId" INTEGER NOT NULL,
            "LastReceivedLogSequenceId" INTEGER NOT NULL,
            "UpdatedAtUtc" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationLogSummaries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationLogSummaries" PRIMARY KEY AUTOINCREMENT,
            "StationId" TEXT NOT NULL,
            "SequenceId" INTEGER NOT NULL,
            "MessageId" TEXT NOT NULL,
            "TimestampUtc" TEXT NOT NULL,
            "Level" TEXT NOT NULL,
            "Source" TEXT NOT NULL,
            "EventId" TEXT NULL,
            "MessageTemplate" TEXT NULL,
            "RenderedMessage" TEXT NOT NULL,
            "ExceptionType" TEXT NULL,
            "ExceptionMessage" TEXT NULL,
            "CorrelationId" TEXT NULL,
            "RunId" TEXT NULL,
            "PackageId" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "ReceivedAtUtc" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationAuditRecords" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationAuditRecords" PRIMARY KEY AUTOINCREMENT,
            "AuditId" TEXT NOT NULL,
            "UserId" TEXT NULL,
            "UserName" TEXT NULL,
            "Action" TEXT NOT NULL,
            "TargetStationId" TEXT NULL,
            "CommandId" TEXT NULL,
            "PayloadSummary" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "Result" TEXT NULL,
            "ClientIp" TEXT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "StationPackageRecords" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StationPackageRecords" PRIMARY KEY AUTOINCREMENT,
            "PackageId" TEXT NOT NULL,
            "PackageName" TEXT NOT NULL,
            "PackageVersion" TEXT NOT NULL,
            "PackageKind" TEXT NOT NULL DEFAULT 'Production',
            "FlowHash" TEXT NOT NULL,
            "FileName" TEXT NOT NULL,
            "FilePath" TEXT NOT NULL,
            "SizeBytes" INTEGER NOT NULL,
            "Sha256" TEXT NOT NULL,
            "CreatedBy" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL
        );
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationNodes_StationId" ON "StationNodes" ("StationId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationNodes_LastSeenAtUtc" ON "StationNodes" ("LastSeenAtUtc");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_SequenceId" ON "StationResultSummaries" ("StationId", "SequenceId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_CompletedAtUtc" ON "StationResultSummaries" ("StationId", "CompletedAtUtc");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationResultSummaries_MessageId" ON "StationResultSummaries" ("MessageId");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationHealthSnapshots_StationId_SequenceId" ON "StationHealthSnapshots" ("StationId", "SequenceId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationHealthSnapshots_StationId_CreatedAtUtc" ON "StationHealthSnapshots" ("StationId", "CreatedAtUtc");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationAlarmEvents_AlarmId" ON "StationAlarmEvents" ("AlarmId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationAlarmEvents_StationId_IsActive" ON "StationAlarmEvents" ("StationId", "IsActive");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationCommandRecords_CommandId" ON "StationCommandRecords" ("CommandId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationCommandRecords_StationId_CreatedAtUtc" ON "StationCommandRecords" ("StationId", "CreatedAtUtc");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationCommandRecords_StationId_Status" ON "StationCommandRecords" ("StationId", "Status");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationConnectionEvents_StationId_CreatedAtUtc" ON "StationConnectionEvents" ("StationId", "CreatedAtUtc");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationSyncCursors_StationId" ON "StationSyncCursors" ("StationId");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationLogSummaries_StationId_SequenceId" ON "StationLogSummaries" ("StationId", "SequenceId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationLogSummaries_StationId_TimestampUtc" ON "StationLogSummaries" ("StationId", "TimestampUtc");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationAuditRecords_AuditId" ON "StationAuditRecords" ("AuditId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationAuditRecords_TargetStationId_CreatedAtUtc" ON "StationAuditRecords" ("TargetStationId", "CreatedAtUtc");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationPackageRecords_PackageId" ON "StationPackageRecords" ("PackageId");""",
        """CREATE INDEX IF NOT EXISTS "IX_StationPackageRecords_CreatedAtUtc" ON "StationPackageRecords" ("CreatedAtUtc");"""
    ];

    private sealed class BaselineSchemaRequirements
    {
        public Dictionary<string, TableSchemaRequirement> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Indexes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TableSchemaRequirement GetOrAddTable(string tableName)
        {
            if (!Tables.TryGetValue(tableName, out var requirement))
            {
                requirement = new TableSchemaRequirement();
                Tables.Add(tableName, requirement);
            }

            return requirement;
        }
    }

    private sealed class TableSchemaRequirement
    {
        public HashSet<string> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
