using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

internal enum VisionDatabaseMaintenanceStage
{
    OperationEntered,
    StatusRead,
    BackupStarted,
    BackupDatabaseCandidate,
    BackupPackageCandidate,
    BackupCommit,
    RestoreExtract,
    RestoreCandidateValidated,
    SafetyBackupCreated,
    RecoveryMarkerWrite,
    RecoveryMarkerPrepared,
    DatabaseReplace,
    DatabaseReplaced,
    PackageReplace,
    PackagePreviousMoved,
    PackageReplaced,
    RestoreVerified,
    RecoveryMarkerCompleted,
    RollbackStarted,
    RollbackDatabase,
    RollbackPackage,
    RollbackCompleted,
    RepairStarted,
    CleanupStarted
}

internal interface IVisionDatabaseMaintenanceFaultInjector
{
    void OnStage(VisionDatabaseMaintenanceStage stage, string databasePath, string operationId);
}

internal sealed class NoOpVisionDatabaseMaintenanceFaultInjector : IVisionDatabaseMaintenanceFaultInjector
{
    public static NoOpVisionDatabaseMaintenanceFaultInjector Instance { get; } = new();

    private NoOpVisionDatabaseMaintenanceFaultInjector()
    {
    }

    public void OnStage(VisionDatabaseMaintenanceStage stage, string databasePath, string operationId)
    {
    }
}

internal sealed class VisionDatabaseMaintenanceInterruptionException : IOException
{
    public VisionDatabaseMaintenanceInterruptionException(string stage)
        : base($"Simulated database maintenance interruption at {stage}.")
    {
    }
}

public sealed class VisionDatabaseMaintenanceException : Exception
{
    internal VisionDatabaseMaintenanceException(
        string errorCode,
        string stage,
        bool retryable,
        bool recoveryRequired,
        bool interruption,
        Exception innerException)
        : base("Vision database maintenance could not complete safely.", innerException)
    {
        ErrorCode = errorCode;
        Stage = stage;
        Retryable = retryable;
        RecoveryRequired = recoveryRequired;
        Interruption = interruption;
    }

    public string ErrorCode { get; }

    public string Stage { get; }

    public bool Retryable { get; }

    public bool RecoveryRequired { get; }

    public string PublicMessage => RecoveryRequired
        ? "Database maintenance recovery is required. No further maintenance operation will access the uncertain database until recovery succeeds."
        : "Database maintenance did not complete. The previous durable database and package set remains authoritative unless recovery is reported as required.";

    internal bool Interruption { get; }
}

public sealed class VisionDatabaseMaintenanceService
{
    private const int RecoveryMarkerSchemaVersion = 1;
    private const string RecoveryStatePrepared = "Prepared";
    private const string RecoveryStateDatabaseReplaced = "DatabaseReplaced";
    private const string RecoveryStatePackageReplaced = "PackageReplaced";
    private const string RecoveryStateCompleted = "Completed";
    private const string RecoveryStateRecoveryRequired = "RecoveryRequired";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MaintenanceGates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VisionDatabaseMaintenanceService> _logger;
    private readonly VisionDatabaseMaintenanceOptions _options;
    private readonly IVisionDatabaseMaintenanceFaultInjector _faultInjector;
    private readonly ConcurrentDictionary<string, byte> _locallyFencedDatabasePaths =
        new(StringComparer.OrdinalIgnoreCase);

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
        : this(scopeFactory, logger, options, NoOpVisionDatabaseMaintenanceFaultInjector.Instance)
    {
    }

    internal VisionDatabaseMaintenanceService(
        IServiceScopeFactory scopeFactory,
        ILogger<VisionDatabaseMaintenanceService> logger,
        VisionDatabaseMaintenanceOptions options,
        IVisionDatabaseMaintenanceFaultInjector faultInjector)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
        _faultInjector = faultInjector;
    }

    public async Task<VisionDatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteUnderMaintenanceGateAsync(
            (databasePath, token) => GetStatusCoreAsync(databasePath, token),
            cancellationToken);
    }

    public async Task<VisionDatabaseBackupResult> CreateBackupAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteUnderMaintenanceGateAsync(
            (databasePath, token) => CreateBackupCoreAsync(databasePath, reason, token),
            cancellationToken);
    }

    private async Task<VisionDatabaseStatus> GetStatusCoreAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        try
        {
            return await RunStageAsync(
                VisionDatabaseMaintenanceStage.StatusRead,
                databasePath,
                string.Empty,
                () => BuildStatusAsync(dbContext, runIntegrityCheck: true, cancellationToken));
        }
        catch (VisionDatabaseMaintenanceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Vision database status.");
            throw CreateMaintenanceException(VisionDatabaseMaintenanceStage.StatusRead, ex);
        }
    }

    private async Task<VisionDatabaseBackupResult> CreateBackupCoreAsync(
        string databasePath,
        string? reason,
        CancellationToken cancellationToken)
    {
        ValidateFileBackedDatabase(databasePath);
        var operationId = Guid.NewGuid().ToString("N");

        RunStage(
            VisionDatabaseMaintenanceStage.BackupStarted,
            databasePath,
            operationId,
            () =>
            {
                Directory.CreateDirectory(_options.BackupRootDirectory);
            });
        var timestamp = DateTimeOffset.UtcNow;
        var backupPath = Path.Combine(
            _options.BackupRootDirectory,
            $"clearvision-db-{timestamp:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.cvdbbak");
        var backupCandidatePath = backupPath + "." + Guid.NewGuid().ToString("N") + ".candidate";
        var tempRoot = Path.Combine(Path.GetTempPath(), "ClearVisionDatabaseBackups", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempRoot);
            var tempDatabaseDirectory = Path.Combine(tempRoot, "db");
            Directory.CreateDirectory(tempDatabaseDirectory);
            var tempDatabasePath = Path.Combine(tempDatabaseDirectory, "vision.db");

            await RunVoidStageAsync(
                VisionDatabaseMaintenanceStage.BackupDatabaseCandidate,
                databasePath,
                operationId,
                () => BackupSqliteDatabaseAsync(databasePath, tempDatabasePath, cancellationToken));

            var packageFileCount = 0;
            var packageBytes = 0L;
            if (Directory.Exists(_options.PackageRootDirectory))
            {
                var packageBackupDirectory = Path.Combine(tempRoot, "packages");
                RunStage(
                    VisionDatabaseMaintenanceStage.BackupPackageCandidate,
                    databasePath,
                    operationId,
                    () => CopyDirectory(_options.PackageRootDirectory, packageBackupDirectory));
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
                PackageRootExisted = Directory.Exists(_options.PackageRootDirectory),
                PackageFileCount = packageFileCount
            };
            RunStage(
                VisionDatabaseMaintenanceStage.BackupPackageCandidate,
                databasePath,
                operationId,
                () => WriteAllBytesDurable(
                    Path.Combine(tempRoot, "manifest.json"),
                    JsonSerializer.SerializeToUtf8Bytes(manifest, VisionDatabaseMaintenance.JsonOptions)));

            RunStage(
                VisionDatabaseMaintenanceStage.BackupCommit,
                databasePath,
                operationId,
                () =>
                {
                    ZipFile.CreateFromDirectory(
                        tempRoot,
                        backupCandidatePath,
                        CompressionLevel.Optimal,
                        includeBaseDirectory: false);
                    FlushExistingFile(backupCandidatePath);
                    File.Move(backupCandidatePath, backupPath);
                    FlushExistingFile(backupPath);
                });
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisionDatabaseMaintenanceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateMaintenanceException(VisionDatabaseMaintenanceStage.BackupStarted, ex);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
            TryDeleteFile(backupCandidatePath);
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

        return await ExecuteUnderMaintenanceGateAsync(
            (databasePath, token) => RestoreBackupCoreAsync(databasePath, fullBackupPath, token),
            cancellationToken);
    }

    private async Task<VisionDatabaseRestoreResult> RestoreBackupCoreAsync(
        string databasePath,
        string fullBackupPath,
        CancellationToken cancellationToken)
    {
        ValidateFileBackedDatabase(databasePath);
        if (!File.Exists(fullBackupPath))
        {
            throw new FileNotFoundException("Database backup file was not found.", fullBackupPath);
        }

        if (PathsReferToSameFile(fullBackupPath, databasePath))
        {
            throw new InvalidOperationException("Cannot restore the live database file onto itself.");
        }

        var operationId = Guid.NewGuid().ToString("N");
        var recoveryDirectory = GetRecoveryDirectory(operationId);
        VisionDatabaseRecoveryMarker? marker = null;
        var markerPersisted = false;
        var commitCompleted = false;
        var completed = false;

        try
        {
            Directory.CreateDirectory(recoveryDirectory);
            var extension = Path.GetExtension(fullBackupPath);
            string restoredDatabasePath;
            string? restoredPackagesRoot = null;
            var replacePackages = false;

            if (string.Equals(extension, ".db", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var tempDatabaseDirectory = Path.Combine(recoveryDirectory, "db");
                Directory.CreateDirectory(tempDatabaseDirectory);
                restoredDatabasePath = Path.Combine(tempDatabaseDirectory, "vision.db");
                Checkpoint(VisionDatabaseMaintenanceStage.RestoreExtract, databasePath, operationId);
                try
                {
                    await BackupSqliteDatabaseAsync(fullBackupPath, restoredDatabasePath, cancellationToken);
                }
                catch (SqliteException ex)
                {
                    throw new InvalidDataException("The database backup is not a readable SQLite database.", ex);
                }
            }
            else
            {
                Checkpoint(VisionDatabaseMaintenanceStage.RestoreExtract, databasePath, operationId);
                ExtractZipSafely(fullBackupPath, recoveryDirectory);
                restoredDatabasePath = Path.Combine(recoveryDirectory, "db", "vision.db");
                restoredPackagesRoot = Path.Combine(recoveryDirectory, "packages");
                replacePackages = true;
                if (!Directory.Exists(restoredPackagesRoot))
                {
                    Directory.CreateDirectory(restoredPackagesRoot);
                }
            }

            if (!File.Exists(restoredDatabasePath))
            {
                throw new InvalidOperationException("Backup does not contain db/vision.db.");
            }

            await ValidateRestoreCandidateAsync(restoredDatabasePath, cancellationToken);
            Checkpoint(VisionDatabaseMaintenanceStage.RestoreCandidateValidated, databasePath, operationId);

            var packageRootExisted = Directory.Exists(_options.PackageRootDirectory);
            var safetyBackup = await CreateBackupCoreAsync(databasePath, "pre-restore", cancellationToken);
            Checkpoint(VisionDatabaseMaintenanceStage.SafetyBackupCreated, databasePath, operationId);

            marker = new VisionDatabaseRecoveryMarker
            {
                SchemaVersion = RecoveryMarkerSchemaVersion,
                OperationId = operationId,
                State = RecoveryStatePrepared,
                DatabasePath = Path.GetFullPath(databasePath),
                PackageRootDirectory = Path.GetFullPath(_options.PackageRootDirectory),
                BackupRootDirectory = Path.GetFullPath(_options.BackupRootDirectory),
                SourceBackupPath = fullBackupPath,
                SourceBackupSha256 = ComputeFileSha256(fullBackupPath),
                SafetyBackupPath = Path.GetFullPath(safetyBackup.BackupPath),
                SafetyBackupSha256 = ComputeFileSha256(safetyBackup.BackupPath),
                CandidateDatabaseSha256 = ComputeFileSha256(restoredDatabasePath),
                CandidatePackagesSha256 = ComputeDirectorySha256(restoredPackagesRoot),
                ReplacePackages = replacePackages,
                PreviousPackageRootExisted = packageRootExisted,
                PreparedAtUtc = DateTimeOffset.UtcNow
            };
            PersistRecoveryMarkerNoLock(marker, databasePath, VisionDatabaseMaintenanceStage.RecoveryMarkerWrite);
            markerPersisted = true;
            Checkpoint(VisionDatabaseMaintenanceStage.RecoveryMarkerPrepared, databasePath, operationId);

            RunStage(
                VisionDatabaseMaintenanceStage.DatabaseReplace,
                databasePath,
                operationId,
                () => EnsureFileHash(restoredDatabasePath, marker.CandidateDatabaseSha256),
                injectFault: false);
            PublishDatabaseCandidateNoLock(
                restoredDatabasePath,
                databasePath,
                operationId,
                marker.CandidateDatabaseSha256,
                VisionDatabaseMaintenanceStage.DatabaseReplace,
                injectFault: true);
            marker.State = RecoveryStateDatabaseReplaced;
            PersistRecoveryMarkerNoLock(marker, databasePath, VisionDatabaseMaintenanceStage.RecoveryMarkerWrite);
            Checkpoint(VisionDatabaseMaintenanceStage.DatabaseReplaced, databasePath, operationId);

            var restoredPackageFileCount = 0;
            if (replacePackages)
            {
                RunStage(
                    VisionDatabaseMaintenanceStage.PackageReplace,
                    databasePath,
                    operationId,
                    () => EnsureDirectoryHash(restoredPackagesRoot!, marker.CandidatePackagesSha256),
                    injectFault: false);
                ReplacePackageRootNoLock(
                    restoredPackagesRoot!,
                    _options.PackageRootDirectory,
                    databasePath,
                    operationId,
                    marker.CandidatePackagesSha256,
                    VisionDatabaseMaintenanceStage.PackageReplace,
                    injectFault: true);
                restoredPackageFileCount = Directory
                    .EnumerateFiles(restoredPackagesRoot, "*", SearchOption.AllDirectories)
                    .Count();
            }

            marker.State = RecoveryStatePackageReplaced;
            PersistRecoveryMarkerNoLock(marker, databasePath, VisionDatabaseMaintenanceStage.RecoveryMarkerWrite);
            Checkpoint(VisionDatabaseMaintenanceStage.PackageReplaced, databasePath, operationId);

            var status = await VerifyPublishedDatabaseAsync(databasePath, cancellationToken);
            Checkpoint(VisionDatabaseMaintenanceStage.RestoreVerified, databasePath, operationId);

            marker.State = RecoveryStateCompleted;
            marker.CompletedAtUtc = DateTimeOffset.UtcNow;
            PersistRecoveryMarkerNoLock(marker, databasePath, VisionDatabaseMaintenanceStage.RecoveryMarkerWrite);
            commitCompleted = true;
            Checkpoint(VisionDatabaseMaintenanceStage.RecoveryMarkerCompleted, databasePath, operationId);
            completed = true;

            var result = new VisionDatabaseRestoreResult
            {
                RestoredDatabasePath = databasePath,
                BackupPath = fullBackupPath,
                SafetyBackupPath = safetyBackup.BackupPath,
                RestoredPackageFileCount = restoredPackageFileCount,
                Status = status
            };

            CleanupCompletedRecoveryNoThrow(marker);
            return result;
        }
        catch (Exception ex) when (!markerPersisted && IsDatabaseMaintenanceClientError(ex))
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = ToMaintenanceException(VisionDatabaseMaintenanceStage.RestoreVerified, ex);
            if (!markerPersisted || marker == null || failure.Interruption || commitCompleted)
            {
                if (commitCompleted && !failure.Interruption)
                {
                    CleanupCompletedRecoveryNoThrow(marker!);
                }

                throw failure;
            }

            try
            {
                await RollbackFromSafetyBackupNoLock(marker, injectFault: true, CancellationToken.None);
            }
            catch (Exception rollbackFailure)
            {
                TryFenceRecoveryNoThrow(marker, databasePath);
                _locallyFencedDatabasePaths.TryAdd(databasePath, 0);
                throw CreateRecoveryRequiredException(rollbackFailure);
            }

            throw failure;
        }
        finally
        {
            if (!markerPersisted || completed)
            {
                TryDeleteDirectory(recoveryDirectory);
            }
        }
    }

    public async Task<VisionDatabaseCleanupResult> CleanupHistoryAsync(
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteUnderMaintenanceGateAsync(
            (databasePath, token) => CleanupHistoryCoreAsync(databasePath, retentionDays, token),
            cancellationToken);
    }

    private async Task<VisionDatabaseCleanupResult> CleanupHistoryCoreAsync(
        string databasePath,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        Checkpoint(VisionDatabaseMaintenanceStage.CleanupStarted, databasePath, string.Empty);
        try
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisionDatabaseMaintenanceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateMaintenanceException(VisionDatabaseMaintenanceStage.CleanupStarted, ex);
        }
    }

    public async Task<VisionDatabaseStatus> RepairAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteUnderMaintenanceGateAsync(
            (databasePath, token) => RepairCoreAsync(databasePath, token),
            cancellationToken);
    }

    private async Task<VisionDatabaseStatus> RepairCoreAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        Checkpoint(VisionDatabaseMaintenanceStage.RepairStarted, databasePath, string.Empty);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        await RunVoidStageAsync(
            VisionDatabaseMaintenanceStage.RepairStarted,
            databasePath,
            string.Empty,
            () => VisionDatabaseInitializer.InitializeAsync(dbContext, cancellationToken));
        return await GetStatusCoreAsync(databasePath, cancellationToken);
    }

    private async Task<T> ExecuteUnderMaintenanceGateAsync<T>(
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var databasePath = Path.GetFullPath(ResolveDatabasePath());
        ValidateFileBackedDatabase(databasePath);
        var gate = MaintenanceGates.GetOrAdd(databasePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_locallyFencedDatabasePaths.ContainsKey(databasePath))
            {
                throw CreateRecoveryRequiredException(
                    new InvalidOperationException("This maintenance service instance is fenced pending restart recovery."));
            }

            Checkpoint(VisionDatabaseMaintenanceStage.OperationEntered, databasePath, string.Empty);
            await RecoverPendingMaintenanceNoLock(databasePath, CancellationToken.None);
            return await operation(databasePath, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private string ResolveDatabasePath()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return VisionDatabaseMaintenance.GetDatabasePath(dbContext.Database.GetDbConnection());
    }

    private async Task RecoverPendingMaintenanceNoLock(
        string databasePath,
        CancellationToken cancellationToken)
    {
        VisionDatabaseRecoveryMarker? marker;
        try
        {
            marker = ReadRecoveryMarkerNoLock(databasePath);
        }
        catch
        {
            _locallyFencedDatabasePaths.TryAdd(databasePath, 0);
            throw;
        }

        if (marker == null)
        {
            return;
        }

        if (string.Equals(marker.State, RecoveryStateCompleted, StringComparison.Ordinal))
        {
            CleanupCompletedRecoveryNoThrow(marker);
            return;
        }

        try
        {
            await RollbackFromSafetyBackupNoLock(marker, injectFault: true, cancellationToken);
        }
        catch (Exception ex)
        {
            TryFenceRecoveryNoThrow(marker, databasePath);
            _locallyFencedDatabasePaths.TryAdd(databasePath, 0);
            throw CreateRecoveryRequiredException(ex);
        }
    }

    private async Task RollbackFromSafetyBackupNoLock(
        VisionDatabaseRecoveryMarker marker,
        bool injectFault,
        CancellationToken cancellationToken)
    {
        Checkpoint(
            VisionDatabaseMaintenanceStage.RollbackStarted,
            marker.DatabasePath,
            marker.OperationId,
            injectFault);
        EnsureFileHash(marker.SafetyBackupPath, marker.SafetyBackupSha256);

        var rollbackDirectory = Path.Combine(
            GetRecoveryDirectory(marker.OperationId),
            "rollback-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(rollbackDirectory);
            RunStage(
                VisionDatabaseMaintenanceStage.RollbackStarted,
                marker.DatabasePath,
                marker.OperationId,
                () => ExtractZipSafely(marker.SafetyBackupPath, rollbackDirectory),
                injectFault);
            var rollbackDatabasePath = Path.Combine(rollbackDirectory, "db", "vision.db");
            if (!File.Exists(rollbackDatabasePath))
            {
                throw new InvalidDataException("Safety backup does not contain a database candidate.");
            }

            await ValidateRestoreCandidateAsync(rollbackDatabasePath, cancellationToken);
            var rollbackDatabaseSha256 = ComputeFileSha256(rollbackDatabasePath);
            PublishDatabaseCandidateNoLock(
                rollbackDatabasePath,
                marker.DatabasePath,
                marker.OperationId,
                rollbackDatabaseSha256,
                VisionDatabaseMaintenanceStage.RollbackDatabase,
                injectFault);

            var rollbackPackagesPath = Path.Combine(rollbackDirectory, "packages");
            if (marker.PreviousPackageRootExisted)
            {
                if (!Directory.Exists(rollbackPackagesPath))
                {
                    Directory.CreateDirectory(rollbackPackagesPath);
                }

                ReplacePackageRootNoLock(
                    rollbackPackagesPath,
                    marker.PackageRootDirectory,
                    marker.DatabasePath,
                    marker.OperationId,
                    ComputeDirectorySha256(rollbackPackagesPath),
                    VisionDatabaseMaintenanceStage.RollbackPackage,
                    injectFault);
            }
            else
            {
                RemovePackageRootNoLock(
                    marker.PackageRootDirectory,
                    marker.DatabasePath,
                    marker.OperationId,
                    injectFault);
            }

            await VerifyPublishedDatabaseAsync(marker.DatabasePath, cancellationToken);
            Checkpoint(
                VisionDatabaseMaintenanceStage.RollbackCompleted,
                marker.DatabasePath,
                marker.OperationId,
                injectFault);
            RunStage(
                VisionDatabaseMaintenanceStage.RollbackCompleted,
                marker.DatabasePath,
                marker.OperationId,
                () =>
                {
                    var markerPath = GetRecoveryMarkerPath(marker.DatabasePath);
                    if (File.Exists(markerPath))
                    {
                        File.Delete(markerPath);
                    }
                },
                injectFault);
            TryDeleteDirectory(GetRecoveryDirectory(marker.OperationId));
        }
        finally
        {
            TryDeleteDirectory(rollbackDirectory);
        }
    }

    private async Task ValidateRestoreCandidateAsync(
        string candidateDatabasePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(
                VisionDatabaseMaintenance.CreateSqliteConnectionString(candidateDatabasePath, pooling: false));
            await connection.OpenAsync(cancellationToken);
            var integrity = await VisionDatabaseMaintenance.RunIntegrityCheckAsync(connection, cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The backup database failed its integrity check.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("The backup database is not a valid SQLite database.", ex);
        }
    }

    private async Task<VisionDatabaseStatus> VerifyPublishedDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            await RunVoidStageAsync(
                VisionDatabaseMaintenanceStage.RestoreVerified,
                databasePath,
                string.Empty,
                () => VisionDatabaseInitializer.InitializeAsync(context, cancellationToken),
                injectFault: false);
        }

        var status = await GetStatusCoreAsync(databasePath, cancellationToken);
        if (!string.Equals(status.State, VisionDatabaseState.Healthy, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The published database did not verify as healthy.");
        }

        return status;
    }

    private void PublishDatabaseCandidateNoLock(
        string candidateDatabasePath,
        string databasePath,
        string operationId,
        string expectedCandidateSha256,
        VisionDatabaseMaintenanceStage stage,
        bool injectFault)
    {
        var bytes = File.ReadAllBytes(candidateDatabasePath);
        var tempPath = databasePath + ".maintenance-" + operationId + "." + Guid.NewGuid().ToString("N") + ".candidate";
        try
        {
            RunStage(
                stage,
                databasePath,
                operationId,
                () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
                    WriteAllBytesDurable(tempPath, bytes);
                    EnsureFileHash(tempPath, expectedCandidateSha256);
                    SqliteConnection.ClearAllPools();
                    foreach (var sidecarPath in VisionDatabaseMaintenance
                        .GetSqliteDatabaseFiles(databasePath)
                        .Where(path => !string.Equals(path, databasePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (File.Exists(sidecarPath))
                        {
                            File.Delete(sidecarPath);
                        }
                    }

                    File.Move(tempPath, databasePath, overwrite: true);
                    FlushExistingFile(databasePath);
                },
                injectFault);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private void ReplacePackageRootNoLock(
        string candidateRoot,
        string packageRoot,
        string databasePath,
        string operationId,
        string expectedCandidateSha256,
        VisionDatabaseMaintenanceStage stage,
        bool injectFault)
    {
        var normalizedPackageRoot = Path.GetFullPath(packageRoot);
        var stagedRoot = normalizedPackageRoot + ".maintenance-" + operationId + ".candidate";
        var previousRoot = normalizedPackageRoot + ".maintenance-" + operationId + ".previous";
        var movedPrevious = false;
        try
        {
            RunStage(
                stage,
                databasePath,
                operationId,
                () =>
                {
                    DeleteDirectoryIfExists(stagedRoot);
                    if (Directory.Exists(previousRoot))
                    {
                        if (!Directory.Exists(normalizedPackageRoot))
                        {
                            Directory.Move(previousRoot, normalizedPackageRoot);
                        }
                        else
                        {
                            Directory.Delete(previousRoot, recursive: true);
                        }
                    }

                    CopyDirectory(candidateRoot, stagedRoot);
                    EnsureDirectoryHash(stagedRoot, expectedCandidateSha256);
                    if (Directory.Exists(normalizedPackageRoot))
                    {
                        Directory.Move(normalizedPackageRoot, previousRoot);
                        movedPrevious = true;
                    }

                    Checkpoint(
                        VisionDatabaseMaintenanceStage.PackagePreviousMoved,
                        databasePath,
                        operationId,
                        injectFault);
                    Directory.Move(stagedRoot, normalizedPackageRoot);
                },
                injectFault);
            DeleteDirectoryIfExists(previousRoot);
        }
        catch (Exception ex)
        {
            var failure = ToMaintenanceException(stage, ex);
            if (!failure.Interruption &&
                movedPrevious &&
                !Directory.Exists(normalizedPackageRoot) &&
                Directory.Exists(previousRoot))
            {
                Directory.Move(previousRoot, normalizedPackageRoot);
            }

            throw failure;
        }
        finally
        {
            TryDeleteDirectory(stagedRoot);
        }
    }

    private void RemovePackageRootNoLock(
        string packageRoot,
        string databasePath,
        string operationId,
        bool injectFault)
    {
        var normalizedPackageRoot = Path.GetFullPath(packageRoot);
        var previousRoot = normalizedPackageRoot + ".maintenance-" + operationId + ".previous";
        RunStage(
            VisionDatabaseMaintenanceStage.RollbackPackage,
            databasePath,
            operationId,
            () =>
            {
                if (Directory.Exists(normalizedPackageRoot))
                {
                    DeleteDirectoryIfExists(previousRoot);
                    Directory.Move(normalizedPackageRoot, previousRoot);
                    Checkpoint(
                        VisionDatabaseMaintenanceStage.PackagePreviousMoved,
                        databasePath,
                        operationId,
                        injectFault);
                    Directory.Delete(previousRoot, recursive: true);
                }
                else
                {
                    DeleteDirectoryIfExists(previousRoot);
                }
            },
            injectFault);
    }

    private VisionDatabaseRecoveryMarker? ReadRecoveryMarkerNoLock(string databasePath)
    {
        var markerPath = GetRecoveryMarkerPath(databasePath);
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<VisionDatabaseRecoveryMarker>(
                File.ReadAllBytes(markerPath),
                VisionDatabaseMaintenance.JsonOptions)
                ?? throw new InvalidDataException("Database maintenance recovery marker is null.");
            ValidateRecoveryMarker(marker, databasePath);
            return marker;
        }
        catch (Exception ex)
        {
            throw CreateRecoveryRequiredException(ex);
        }
    }

    private void PersistRecoveryMarkerNoLock(
        VisionDatabaseRecoveryMarker marker,
        string databasePath,
        VisionDatabaseMaintenanceStage stage,
        bool injectFault = true)
    {
        ValidateRecoveryMarker(marker, databasePath);
        var markerPath = GetRecoveryMarkerPath(databasePath);
        var tempPath = markerPath + "." + marker.OperationId + "." + Guid.NewGuid().ToString("N") + ".candidate";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(marker, VisionDatabaseMaintenance.JsonOptions);
        try
        {
            RunStage(
                stage,
                databasePath,
                marker.OperationId,
                () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                    WriteAllBytesDurable(tempPath, bytes);
                    File.Move(tempPath, markerPath, overwrite: true);
                    FlushExistingFile(markerPath);
                },
                injectFault);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private void ValidateRecoveryMarker(VisionDatabaseRecoveryMarker marker, string databasePath)
    {
        var validState = marker.State is RecoveryStatePrepared or
            RecoveryStateDatabaseReplaced or
            RecoveryStatePackageReplaced or
            RecoveryStateCompleted or
            RecoveryStateRecoveryRequired;
        if (marker.SchemaVersion != RecoveryMarkerSchemaVersion ||
            !Guid.TryParseExact(marker.OperationId, "N", out _) ||
            !validState ||
            !PathsReferToSameFile(marker.DatabasePath, databasePath) ||
            !PathsReferToSameFile(marker.PackageRootDirectory, _options.PackageRootDirectory) ||
            !PathsReferToSameFile(marker.BackupRootDirectory, _options.BackupRootDirectory) ||
            !IsSha256(marker.SourceBackupSha256) ||
            !IsSha256(marker.SafetyBackupSha256) ||
            !IsSha256(marker.CandidateDatabaseSha256) ||
            (marker.ReplacePackages && !IsSha256(marker.CandidatePackagesSha256)))
        {
            throw new InvalidDataException("Database maintenance recovery marker is invalid.");
        }

        var recoveryDirectory = Path.GetFullPath(GetRecoveryDirectory(marker.OperationId));
        var recoveryRoot = EnsureTrailingSeparator(Path.GetFullPath(GetRecoveryRootDirectory()));
        if (!EnsureTrailingSeparator(recoveryDirectory).StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Database maintenance recovery directory is invalid.");
        }

        var safetyBackupPath = Path.GetFullPath(marker.SafetyBackupPath);
        var backupRoot = Path.GetFullPath(_options.BackupRootDirectory);
        if (!IsPathWithinDirectory(safetyBackupPath, backupRoot) ||
            !string.Equals(Path.GetExtension(safetyBackupPath), ".cvdbbak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Database maintenance safety backup path is invalid.");
        }
    }

    private void TryFenceRecoveryNoThrow(VisionDatabaseRecoveryMarker marker, string databasePath)
    {
        try
        {
            marker.State = RecoveryStateRecoveryRequired;
            marker.LastFailureAtUtc = DateTimeOffset.UtcNow;
            PersistRecoveryMarkerNoLock(
                marker,
                databasePath,
                VisionDatabaseMaintenanceStage.RecoveryMarkerWrite,
                injectFault: false);
        }
        catch
        {
            // The existing non-completed marker remains a fail-closed recovery signal.
        }
    }

    private void CleanupCompletedRecoveryNoThrow(VisionDatabaseRecoveryMarker marker)
    {
        try
        {
            var markerPath = GetRecoveryMarkerPath(marker.DatabasePath);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
        catch
        {
            // Completed-state residue is non-authoritative; a later status/operation retries cleanup.
            return;
        }

        TryDeleteDirectory(GetRecoveryDirectory(marker.OperationId));
        TryDeleteDirectory(marker.PackageRootDirectory + ".maintenance-" + marker.OperationId + ".candidate");
        TryDeleteDirectory(marker.PackageRootDirectory + ".maintenance-" + marker.OperationId + ".previous");
    }

    private string GetRecoveryRootDirectory()
    {
        return Path.Combine(Path.GetFullPath(_options.BackupRootDirectory), ".maintenance-recovery");
    }

    private string GetRecoveryDirectory(string operationId)
    {
        if (!Guid.TryParseExact(operationId, "N", out _))
        {
            throw new InvalidDataException("Database maintenance operation identifier is invalid.");
        }

        return Path.Combine(GetRecoveryRootDirectory(), operationId);
    }

    private static string GetRecoveryMarkerPath(string databasePath)
    {
        return Path.GetFullPath(databasePath) + ".maintenance-recovery.json";
    }

    private static void WriteAllBytesDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void FlushExistingFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.WriteThrough);
        stream.Flush(flushToDisk: true);
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeDirectorySha256(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
        }

        var builder = new StringBuilder();
        foreach (var directory in Directory
            .EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(directoryPath, path), StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("D:");
            builder.Append(Path.GetRelativePath(directoryPath, directory).Replace('\\', '/'));
            builder.Append('\n');
        }

        foreach (var file in Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(directoryPath, path), StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("F:");
            builder.Append(Path.GetRelativePath(directoryPath, file).Replace('\\', '/'));
            builder.Append('\n');
            builder.Append(ComputeFileSha256(file));
            builder.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void EnsureFileHash(string path, string expectedSha256)
    {
        if (!File.Exists(path) ||
            !string.Equals(ComputeFileSha256(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Database maintenance durable artifact hash mismatch.");
        }
    }

    private static void EnsureDirectoryHash(string path, string expectedSha256)
    {
        if (!Directory.Exists(path) ||
            !string.Equals(ComputeDirectorySha256(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Database maintenance durable artifact hash mismatch.");
        }
    }

    private static bool IsPathWithinDirectory(string path, string directoryPath)
    {
        var fullPath = Path.GetFullPath(path);
        var directoryRoot = EnsureTrailingSeparator(Path.GetFullPath(directoryPath));
        return fullPath.StartsWith(directoryRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256(string? value)
    {
        return value?.Length == 64 && value.All(Uri.IsHexDigit);
    }

    private void Checkpoint(
        VisionDatabaseMaintenanceStage stage,
        string databasePath,
        string operationId,
        bool injectFault = true)
    {
        RunStage(stage, databasePath, operationId, static () => { }, injectFault);
    }

    private T RunStage<T>(
        VisionDatabaseMaintenanceStage stage,
        string databasePath,
        string operationId,
        Func<T> operation,
        bool injectFault = true)
    {
        try
        {
            if (injectFault)
            {
                _faultInjector.OnStage(stage, databasePath, operationId);
            }

            return operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisionDatabaseMaintenanceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateMaintenanceException(stage, ex);
        }
    }

    private void RunStage(
        VisionDatabaseMaintenanceStage stage,
        string databasePath,
        string operationId,
        Action operation,
        bool injectFault = true)
    {
        RunStage(
            stage,
            databasePath,
            operationId,
            () =>
            {
                operation();
                return true;
            },
            injectFault);
    }

    private async Task<T> RunStageAsync<T>(
        VisionDatabaseMaintenanceStage stage,
        string databasePath,
        string operationId,
        Func<Task<T>> operation,
        bool injectFault = true)
    {
        try
        {
            if (injectFault)
            {
                _faultInjector.OnStage(stage, databasePath, operationId);
            }

            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisionDatabaseMaintenanceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateMaintenanceException(stage, ex);
        }
    }

    private async Task RunVoidStageAsync(
        VisionDatabaseMaintenanceStage stage,
        string databasePath,
        string operationId,
        Func<Task> operation,
        bool injectFault = true)
    {
        await RunStageAsync(
            stage,
            databasePath,
            operationId,
            async () =>
            {
                await operation();
                return true;
            },
            injectFault);
    }

    private static VisionDatabaseMaintenanceException ToMaintenanceException(
        VisionDatabaseMaintenanceStage stage,
        Exception exception)
    {
        return exception as VisionDatabaseMaintenanceException
            ?? CreateMaintenanceException(stage, exception);
    }

    private static VisionDatabaseMaintenanceException CreateMaintenanceException(
        VisionDatabaseMaintenanceStage stage,
        Exception exception)
    {
        var interruption = ContainsException<VisionDatabaseMaintenanceInterruptionException>(exception);
        var errorCode = interruption
            ? "DB_MAINTENANCE_INTERRUPTED"
            : ContainsException<UnauthorizedAccessException>(exception)
                ? "DB_MAINTENANCE_PERMISSION_DENIED"
                : ContainsException<IOException>(exception) || ContainsException<SqliteException>(exception)
                    ? "DB_MAINTENANCE_IO_FAILED"
                    : "DB_MAINTENANCE_PERSISTENCE_FAILED";
        return new VisionDatabaseMaintenanceException(
            errorCode,
            GetPublicStage(stage),
            retryable: true,
            recoveryRequired: false,
            interruption: interruption,
            innerException: exception);
    }

    private static VisionDatabaseMaintenanceException CreateRecoveryRequiredException(Exception exception)
    {
        return new VisionDatabaseMaintenanceException(
            "DB_MAINTENANCE_RECOVERY_REQUIRED",
            "recovery",
            retryable: true,
            recoveryRequired: true,
            interruption: false,
            innerException: exception);
    }

    private static bool ContainsException<T>(Exception exception)
        where T : Exception
    {
        if (exception is T)
        {
            return true;
        }

        if (exception is AggregateException aggregate &&
            aggregate.InnerExceptions.Any(inner => ContainsException<T>(inner)))
        {
            return true;
        }

        return exception.InnerException != null && ContainsException<T>(exception.InnerException);
    }

    private static string GetPublicStage(VisionDatabaseMaintenanceStage stage)
    {
        return stage switch
        {
            VisionDatabaseMaintenanceStage.StatusRead => "status",
            VisionDatabaseMaintenanceStage.BackupStarted or
            VisionDatabaseMaintenanceStage.BackupDatabaseCandidate or
            VisionDatabaseMaintenanceStage.BackupPackageCandidate or
            VisionDatabaseMaintenanceStage.BackupCommit => "backup",
            VisionDatabaseMaintenanceStage.RestoreExtract or
            VisionDatabaseMaintenanceStage.RestoreCandidateValidated => "restore-candidate",
            VisionDatabaseMaintenanceStage.SafetyBackupCreated => "safety-backup",
            VisionDatabaseMaintenanceStage.RecoveryMarkerWrite or
            VisionDatabaseMaintenanceStage.RecoveryMarkerPrepared or
            VisionDatabaseMaintenanceStage.RecoveryMarkerCompleted => "recovery-marker",
            VisionDatabaseMaintenanceStage.DatabaseReplace or
            VisionDatabaseMaintenanceStage.DatabaseReplaced => "database-replace",
            VisionDatabaseMaintenanceStage.PackageReplace or
            VisionDatabaseMaintenanceStage.PackagePreviousMoved or
            VisionDatabaseMaintenanceStage.PackageReplaced => "package-replace",
            VisionDatabaseMaintenanceStage.RollbackStarted or
            VisionDatabaseMaintenanceStage.RollbackDatabase or
            VisionDatabaseMaintenanceStage.RollbackPackage or
            VisionDatabaseMaintenanceStage.RollbackCompleted => "rollback",
            VisionDatabaseMaintenanceStage.RepairStarted => "repair",
            VisionDatabaseMaintenanceStage.CleanupStarted => "cleanup",
            _ => "maintenance"
        };
    }

    private static bool IsDatabaseMaintenanceClientError(Exception ex)
    {
        return ex is ArgumentException
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or InvalidDataException
            or OperationCanceledException;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Unique candidates are non-authoritative residue.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectoryIfExists(path);
        }
        catch
        {
            // Unique candidates and completed-operation residue are non-authoritative.
        }
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

internal sealed class VisionDatabaseRecoveryMarker
{
    public VisionDatabaseRecoveryMarker()
    {
    }

    public int SchemaVersion { get; set; }

    public string OperationId { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string DatabasePath { get; set; } = string.Empty;

    public string PackageRootDirectory { get; set; } = string.Empty;

    public string BackupRootDirectory { get; set; } = string.Empty;

    public string SourceBackupPath { get; set; } = string.Empty;

    public string SourceBackupSha256 { get; set; } = string.Empty;

    public string SafetyBackupPath { get; set; } = string.Empty;

    public string SafetyBackupSha256 { get; set; } = string.Empty;

    public string CandidateDatabaseSha256 { get; set; } = string.Empty;

    public string CandidatePackagesSha256 { get; set; } = string.Empty;

    public bool ReplacePackages { get; set; }

    public bool PreviousPackageRootExisted { get; set; }

    public DateTimeOffset PreparedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? LastFailureAtUtc { get; set; }
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

    public bool PackageRootExisted { get; init; }

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
    public const int CurrentSqliteSchemaVersion = 6;

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
        await EnsureStationExecutionIdentitySchemaAsync(dbContext, cancellationToken);
        await EnsureInstallationAuthoritySchemaAsync(dbContext, cancellationToken);
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
                      (Name: "HasJudgmentSignal", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"HasJudgmentSignal\" INTEGER NULL;"),
                      (Name: "ExecutionSnapshotId", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ExecutionSnapshotId\" TEXT NULL;"),
                      (Name: "ProjectPersistenceRevision", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ProjectPersistenceRevision\" INTEGER NULL;"),
                      (Name: "DecisionConfigurationHash", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"DecisionConfigurationHash\" TEXT NULL;"),
                      (Name: "RuntimePackageId", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"RuntimePackageId\" TEXT NULL;"),
                      (Name: "ExecutionSource", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ExecutionSource\" TEXT NULL;"),
                      (Name: "ExecutionRunMode", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ExecutionRunMode\" TEXT NULL;"),
                      (Name: "ShadowRole", Statement: "ALTER TABLE \"InspectionResults\" ADD COLUMN \"ShadowRole\" TEXT NULL;")
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
        await EnsureStationExecutionIdentitySchemaAsync(dbContext, cancellationToken);
        await EnsureInstallationAuthoritySchemaAsync(dbContext, cancellationToken);

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

    private static async Task EnsureInstallationAuthoritySchemaAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "InstallationStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InstallationStates" PRIMARY KEY,
                "IsCompleted" INTEGER NOT NULL,
                "CompletedAtUtc" TEXT NULL,
                "Revision" INTEGER NOT NULL,
                CONSTRAINT "CK_InstallationStates_Singleton" CHECK ("Id" = 1)
            );

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

            UPDATE "InstallationStates"
            SET "IsCompleted" = 1,
                "CompletedAtUtc" = COALESCE("CompletedAtUtc", CURRENT_TIMESTAMP),
                "Revision" = "Revision" + 1
            WHERE "Id" = 1
              AND "IsCompleted" = 0
              AND EXISTS (
                  SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
              );

            CREATE TRIGGER IF NOT EXISTS "TR_InstallationStates_PreventReopen"
            BEFORE UPDATE OF "IsCompleted" ON "InstallationStates"
            FOR EACH ROW
            WHEN OLD."IsCompleted" = 1 AND NEW."IsCompleted" = 0
            BEGIN
                SELECT RAISE(ABORT, 'installation completion latch cannot be reopened');
            END;
            """,
            cancellationToken);
    }

    private static async Task EnsureStationExecutionIdentitySchemaAsync(
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
            foreach (var column in new[]
                     {
                         (Table: "StationNodes", Name: "PackageFlowHash", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"PackageFlowHash\" TEXT NULL;"),
                         (Table: "StationNodes", Name: "ExecutionFlowHash", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"ExecutionFlowHash\" TEXT NULL;"),
                         (Table: "StationNodes", Name: "FlowHash", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"FlowHash\" TEXT NULL;"),
                         (Table: "StationNodes", Name: "ExecutionSnapshotId", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"ExecutionSnapshotId\" TEXT NULL;"),
                         (Table: "StationNodes", Name: "ProjectRevision", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"ProjectRevision\" INTEGER NULL;"),
                         (Table: "StationNodes", Name: "DecisionConfigurationHash", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"DecisionConfigurationHash\" TEXT NULL;"),
                         (Table: "StationNodes", Name: "ExecutionRunMode", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"ExecutionRunMode\" TEXT NULL;"),
                         (Table: "StationNodes", Name: "CurrentRunId", Statement: "ALTER TABLE \"StationNodes\" ADD COLUMN \"CurrentRunId\" TEXT NULL;"),
                         (Table: "StationResultSummaries", Name: "PackageFlowHash", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"PackageFlowHash\" TEXT NULL;"),
                         (Table: "StationResultSummaries", Name: "ExecutionFlowHash", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"ExecutionFlowHash\" TEXT NULL;"),
                         (Table: "StationResultSummaries", Name: "ExecutionSnapshotId", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"ExecutionSnapshotId\" TEXT NULL;"),
                         (Table: "StationResultSummaries", Name: "ProjectRevision", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"ProjectRevision\" INTEGER NULL;"),
                         (Table: "StationResultSummaries", Name: "DecisionConfigurationHash", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"DecisionConfigurationHash\" TEXT NULL;"),
                         (Table: "StationResultSummaries", Name: "ExecutionRunMode", Statement: "ALTER TABLE \"StationResultSummaries\" ADD COLUMN \"ExecutionRunMode\" TEXT NULL;")
                     })
            {
                if (await TableExistsAsync(connection, column.Table, cancellationToken) &&
                    !await ColumnExistsAsync(connection, column.Table, column.Name, cancellationToken))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(column.Statement, cancellationToken);
                }
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
        "column:InspectionResults.ExecutionSnapshotId",
        "column:InspectionResults.ProjectPersistenceRevision",
        "column:InspectionResults.DecisionConfigurationHash",
        "column:InspectionResults.RuntimePackageId",
        "column:InspectionResults.ExecutionSource",
        "column:InspectionResults.ExecutionRunMode",
        "column:InspectionResults.ShadowRole",
        "column:Projects.Flow_DecisionConfiguration",
        "table:InstallationStates",
        "table:StationAlarmEvents",
        "table:StationAuditRecords",
        "table:StationCommandRecords",
        "table:StationConnectionEvents",
        "table:StationHealthSnapshots",
        "table:StationLogSummaries",
        "table:StationNodes",
        "column:StationNodes.PackageFlowHash",
        "column:StationNodes.ExecutionFlowHash",
        "column:StationNodes.FlowHash",
        "column:StationNodes.ExecutionSnapshotId",
        "column:StationNodes.ProjectRevision",
        "column:StationNodes.DecisionConfigurationHash",
        "column:StationNodes.ExecutionRunMode",
        "column:StationNodes.CurrentRunId",
        "table:StationPackageRecords",
        "column:StationPackageRecords.PackageKind",
        "table:StationResultSummaries",
        "table:StationSyncCursors",
        "column:StationResultSummaries.ExecutionOutcome",
        "column:StationResultSummaries.DecisionOutcome",
        "column:StationResultSummaries.HasJudgmentSignal",
        "column:StationResultSummaries.DecisionSource",
        "column:StationResultSummaries.ReasonCode",
        "column:StationResultSummaries.PackageFlowHash",
        "column:StationResultSummaries.ExecutionFlowHash",
        "column:StationResultSummaries.ExecutionSnapshotId",
        "column:StationResultSummaries.ProjectRevision",
        "column:StationResultSummaries.DecisionConfigurationHash",
        "column:StationResultSummaries.ExecutionRunMode",
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
