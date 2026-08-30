using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class VisionDatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task BackupAndRestore_ShouldRoundTripDatabaseAndStationPackageFiles()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var packagePath = Path.Combine(packageRoot, "files", "pkg-roundtrip", "pkg-roundtrip.cvpkg");

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot);
            await InitializeDatabaseAsync(provider);
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            await File.WriteAllTextAsync(packagePath, "package-content");
            await InsertStationPackageRecordAsync(provider, "pkg-roundtrip", packagePath);

            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();
            var backup = await service.CreateBackupAsync("unit-test");

            await DeleteStationPackageRecordsAsync(provider);
            File.Delete(packagePath);

            var restore = await service.RestoreBackupAsync(backup.BackupPath);

            restore.SafetyBackupPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(restore.SafetyBackupPath).Should().BeTrue();
            File.Exists(packagePath).Should().BeTrue();
            File.ReadAllText(packagePath).Should().Be("package-content");
            (await CountAsync(provider, "StationPackageRecords")).Should().Be(1);
            restore.Status!.State.Should().Be(VisionDatabaseState.Healthy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_ShouldRejectLiveDatabasePath()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot);
            await InitializeDatabaseAsync(provider);

            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();
            var act = () => service.RestoreBackupAsync(dbPath);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*live database file*");
            File.Exists(dbPath).Should().BeTrue();
            (await service.GetStatusAsync()).State.Should().Be(VisionDatabaseState.Healthy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task CleanupHistoryAsync_ShouldPruneHistoryButKeepPackagesCommandsAndAudits()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot);
            await InitializeDatabaseAsync(provider);
            await SeedHistoryForCleanupAsync(provider);

            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();
            var result = await service.CleanupHistoryAsync(30);

            result.DeletedRows.Values.Sum().Should().BeGreaterThan(0);
            (await CountAsync(provider, "InspectionResults")).Should().Be(1);
            (await CountAsync(provider, "Defects")).Should().Be(0);
            (await CountAsync(provider, "StationResultSummaries")).Should().Be(1);
            (await CountAsync(provider, "StationHealthSnapshots")).Should().Be(1);
            (await CountAsync(provider, "StationLogSummaries")).Should().Be(1);
            (await CountAsync(provider, "StationConnectionEvents")).Should().Be(1);
            (await CountAsync(provider, "StationPackageRecords")).Should().Be(1);
            (await CountAsync(provider, "StationCommandRecords")).Should().Be(1);
            (await CountAsync(provider, "StationAuditRecords")).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("cleanup")]
    [InlineData("repair")]
    [InlineData("status")]
    public async Task MaintenanceGate_ShouldSerializeRestoreAgainstEveryCrossInstanceOperation(string operation)
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        using var restoreEntered = new ManualResetEventSlim();
        using var releaseRestore = new ManualResetEventSlim();
        using var contenderEntered = new ManualResetEventSlim();
        var restoreFaults = new ArmableMaintenanceFaultInjector();
        var contenderFaults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var restoreProvider = CreateProvider(dbPath, packageRoot, backupRoot, restoreFaults);
            await using var contenderProvider = CreateProvider(dbPath, packageRoot, backupRoot, contenderFaults);
            await InitializeDatabaseAsync(restoreProvider);
            await WriteAuthoritativeStateAsync(restoreProvider, packageRoot, "new-state", "new-package");
            var restoreService = restoreProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            var backup = await restoreService.CreateBackupAsync("serialization-source");
            await WriteAuthoritativeStateAsync(restoreProvider, packageRoot, "old-state", "old-package");

            restoreFaults.Enqueue(
                VisionDatabaseMaintenanceStage.DatabaseReplace,
                (_, _) =>
                {
                    restoreEntered.Set();
                    if (!releaseRestore.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Restore barrier was not released.");
                    }
                });
            restoreFaults.Arm();
            contenderFaults.Enqueue(
                VisionDatabaseMaintenanceStage.OperationEntered,
                (_, _) => contenderEntered.Set());
            contenderFaults.Arm();

            var restoreTask = Task.Run(() => restoreService.RestoreBackupAsync(backup.BackupPath));
            restoreEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

            var contenderService = contenderProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            Task contenderTask = operation switch
            {
                "backup" => Task.Run(async () => { await contenderService.CreateBackupAsync("contender"); }),
                "cleanup" => Task.Run(async () => { await contenderService.CleanupHistoryAsync(30); }),
                "repair" => Task.Run(async () => { await contenderService.RepairAsync(); }),
                "status" => Task.Run(async () => { await contenderService.GetStatusAsync(); }),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };

            try
            {
                contenderEntered.Wait(TimeSpan.FromMilliseconds(250)).Should().BeFalse();
                contenderTask.IsCompleted.Should().BeFalse();
            }
            finally
            {
                releaseRestore.Set();
            }

            await Task.WhenAll(restoreTask, contenderTask);
            contenderEntered.IsSet.Should().BeTrue();
        }
        finally
        {
            releaseRestore.Set();
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData((int)VisionDatabaseMaintenanceStage.BackupDatabaseCandidate)]
    [InlineData((int)VisionDatabaseMaintenanceStage.BackupPackageCandidate)]
    [InlineData((int)VisionDatabaseMaintenanceStage.BackupCommit)]
    [InlineData((int)VisionDatabaseMaintenanceStage.RestoreExtract)]
    [InlineData((int)VisionDatabaseMaintenanceStage.RestoreCandidateValidated)]
    [InlineData((int)VisionDatabaseMaintenanceStage.SafetyBackupCreated)]
    [InlineData((int)VisionDatabaseMaintenanceStage.DatabaseReplace)]
    [InlineData((int)VisionDatabaseMaintenanceStage.PackageReplace)]
    public async Task MaintenanceStages_ShouldMapPermissionFailuresToStableErrors(
        int stageValue)
    {
        await AssertInjectedStageFailureAsync(
            (VisionDatabaseMaintenanceStage)stageValue,
            new UnauthorizedAccessException("sensitive permission failure"),
            "DB_MAINTENANCE_PERMISSION_DENIED");
    }

    [Theory]
    [InlineData((int)VisionDatabaseMaintenanceStage.BackupDatabaseCandidate)]
    [InlineData((int)VisionDatabaseMaintenanceStage.BackupPackageCandidate)]
    [InlineData((int)VisionDatabaseMaintenanceStage.BackupCommit)]
    [InlineData((int)VisionDatabaseMaintenanceStage.RestoreExtract)]
    [InlineData((int)VisionDatabaseMaintenanceStage.RestoreCandidateValidated)]
    [InlineData((int)VisionDatabaseMaintenanceStage.SafetyBackupCreated)]
    [InlineData((int)VisionDatabaseMaintenanceStage.DatabaseReplace)]
    [InlineData((int)VisionDatabaseMaintenanceStage.PackageReplace)]
    public async Task MaintenanceStages_ShouldMapIoFailuresToStableErrors(
        int stageValue)
    {
        await AssertInjectedStageFailureAsync(
            (VisionDatabaseMaintenanceStage)stageValue,
            new IOException("sensitive I/O failure"),
            "DB_MAINTENANCE_IO_FAILED");
    }

    [Theory]
    [InlineData((int)VisionDatabaseMaintenanceStage.DatabaseReplaced)]
    [InlineData((int)VisionDatabaseMaintenanceStage.PackageReplaced)]
    public async Task OrdinaryPublishFailure_ShouldRestoreExactPriorDatabaseAndPackageSet(
        int stageValue)
    {
        var stage = (VisionDatabaseMaintenanceStage)stageValue;
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(provider);
            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(provider, packageRoot, "new-state", "new-package");
            var backup = await service.CreateBackupAsync("publish-failure-source");
            await WriteAuthoritativeStateAsync(provider, packageRoot, "old-state", "old-package");
            var expected = await ReadAuthoritativeStateAsync(provider, packageRoot);
            faults.Enqueue(stage, (_, _) => throw new IOException("publish failed after replacement"));
            faults.Arm();

            var act = () => service.RestoreBackupAsync(backup.BackupPath);
            var failure = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;

            failure.RecoveryRequired.Should().BeFalse();
            (await ReadAuthoritativeStateAsync(provider, packageRoot)).Should().BeEquivalentTo(expected);
            File.Exists(dbPath + ".maintenance-recovery.json").Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InterruptedAfterDatabaseReplacement_NewInstanceShouldRollbackPriorCompleteSet()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var interruptedProvider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(interruptedProvider);
            var interruptedService = interruptedProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "new-state", "new-package");
            var backup = await interruptedService.CreateBackupAsync("interrupted-source");
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "old-state", "old-package");
            var expected = await ReadAuthoritativeStateAsync(interruptedProvider, packageRoot);
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.DatabaseReplaced,
                (_, _) => throw new VisionDatabaseMaintenanceInterruptionException("database-replaced"));
            faults.Arm();

            var act = () => interruptedService.RestoreBackupAsync(backup.BackupPath);
            var interrupted = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;
            interrupted.ErrorCode.Should().Be("DB_MAINTENANCE_INTERRUPTED");
            File.Exists(dbPath + ".maintenance-recovery.json").Should().BeTrue();

            await using var recoveryProvider = CreateProvider(dbPath, packageRoot, backupRoot);
            var recovered = await recoveryProvider
                .GetRequiredService<VisionDatabaseMaintenanceService>()
                .GetStatusAsync();

            recovered.State.Should().Be(VisionDatabaseState.Healthy);
            (await ReadAuthoritativeStateAsync(recoveryProvider, packageRoot)).Should().BeEquivalentTo(expected);
            File.Exists(dbPath + ".maintenance-recovery.json").Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InterruptedAfterCompletedMarker_NewInstanceShouldKeepNewCompleteSet()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var interruptedProvider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(interruptedProvider);
            var interruptedService = interruptedProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "new-state", "new-package");
            var expected = await ReadAuthoritativeStateAsync(interruptedProvider, packageRoot);
            var backup = await interruptedService.CreateBackupAsync("completed-source");
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "old-state", "old-package");
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.RecoveryMarkerCompleted,
                (_, _) => throw new VisionDatabaseMaintenanceInterruptionException("completed-marker"));
            faults.Arm();

            var act = () => interruptedService.RestoreBackupAsync(backup.BackupPath);
            var interrupted = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;
            interrupted.ErrorCode.Should().Be("DB_MAINTENANCE_INTERRUPTED");
            File.Exists(dbPath + ".maintenance-recovery.json").Should().BeTrue();

            await using var recoveryProvider = CreateProvider(dbPath, packageRoot, backupRoot);
            var recovered = await recoveryProvider
                .GetRequiredService<VisionDatabaseMaintenanceService>()
                .GetStatusAsync();

            recovered.State.Should().Be(VisionDatabaseState.Healthy);
            (await ReadAuthoritativeStateAsync(recoveryProvider, packageRoot)).Should().BeEquivalentTo(expected);
            File.Exists(dbPath + ".maintenance-recovery.json").Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RollbackInterruption_ShouldFenceSameInstanceUntilCleanInstanceRecovers()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var fencedProvider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(fencedProvider);
            var fencedService = fencedProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(fencedProvider, packageRoot, "new-state", "new-package");
            var backup = await fencedService.CreateBackupAsync("rollback-interruption-source");
            await WriteAuthoritativeStateAsync(fencedProvider, packageRoot, "old-state", "old-package");
            var expected = await ReadAuthoritativeStateAsync(fencedProvider, packageRoot);
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.PackageReplaced,
                (_, _) => throw new IOException("force ordinary restore failure"));
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.RollbackPackage,
                (_, _) => throw new VisionDatabaseMaintenanceInterruptionException("rollback-package"));
            faults.Arm();

            var restoreAct = () => fencedService.RestoreBackupAsync(backup.BackupPath);
            var restoreFailure = (await restoreAct.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;
            restoreFailure.RecoveryRequired.Should().BeTrue();
            restoreFailure.ErrorCode.Should().Be("DB_MAINTENANCE_RECOVERY_REQUIRED");

            var sameInstanceAct = () => fencedService.GetStatusAsync();
            var fencedFailure = (await sameInstanceAct.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;
            fencedFailure.RecoveryRequired.Should().BeTrue();

            await using var recoveryProvider = CreateProvider(dbPath, packageRoot, backupRoot);
            var recovered = await recoveryProvider
                .GetRequiredService<VisionDatabaseMaintenanceService>()
                .GetStatusAsync();

            recovered.State.Should().Be(VisionDatabaseState.Healthy);
            (await ReadAuthoritativeStateAsync(recoveryProvider, packageRoot)).Should().BeEquivalentTo(expected);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptSafetyBackup_ShouldRemainRecoveryRequired(bool corruptInsteadOfDelete)
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var interruptedProvider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(interruptedProvider);
            var interruptedService = interruptedProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "new-state", "new-package");
            var backup = await interruptedService.CreateBackupAsync("missing-safety-source");
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "old-state", "old-package");
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.DatabaseReplaced,
                (_, _) => throw new VisionDatabaseMaintenanceInterruptionException("leave-pending-marker"));
            faults.Arm();
            Func<Task> interruptedAct = () => interruptedService.RestoreBackupAsync(backup.BackupPath);
            await interruptedAct.Should().ThrowAsync<VisionDatabaseMaintenanceException>();

            var marker = await ReadRecoveryMarkerAsync(dbPath);
            if (corruptInsteadOfDelete)
            {
                await File.AppendAllTextAsync(marker.SafetyBackupPath, "corrupt");
            }
            else
            {
                File.Delete(marker.SafetyBackupPath);
            }

            await using var recoveryProvider = CreateProvider(dbPath, packageRoot, backupRoot);
            var recoveryService = recoveryProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            var recoveryAct = () => recoveryService.GetStatusAsync();
            var recoveryFailure = (await recoveryAct.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;
            recoveryFailure.RecoveryRequired.Should().BeTrue();
            recoveryFailure.ErrorCode.Should().Be("DB_MAINTENANCE_RECOVERY_REQUIRED");

            var repeatedAct = () => recoveryService.GetStatusAsync();
            (await repeatedAct.Should().ThrowAsync<VisionDatabaseMaintenanceException>())
                .Which.RecoveryRequired.Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData("database")]
    [InlineData("packages")]
    public async Task Restore_ShouldRevalidateStagedArtifactsBeforeDestructivePublish(string artifact)
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(provider);
            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(provider, packageRoot, "new-state", "new-package");
            var backup = await service.CreateBackupAsync("tamper-source");
            await WriteAuthoritativeStateAsync(provider, packageRoot, "old-state", "old-package");
            var expected = await ReadAuthoritativeStateAsync(provider, packageRoot);
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.RecoveryMarkerPrepared,
                (_, operationId) =>
                {
                    var recoveryDirectory = Path.Combine(backupRoot, ".maintenance-recovery", operationId);
                    var path = artifact == "database"
                        ? Path.Combine(recoveryDirectory, "db", "vision.db")
                        : Path.Combine(recoveryDirectory, "packages", "tampered.txt");
                    File.AppendAllText(path, "tampered");
                });
            faults.Arm();

            var act = () => service.RestoreBackupAsync(backup.BackupPath);
            var failure = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;

            failure.RecoveryRequired.Should().BeFalse();
            (await ReadAuthoritativeStateAsync(provider, packageRoot)).Should().BeEquivalentTo(expected);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RecoveryMarkerWithSafetyBackupOutsideCanonicalRoot_ShouldFailClosed()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var interruptedProvider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(interruptedProvider);
            var interruptedService = interruptedProvider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "new-state", "new-package");
            var backup = await interruptedService.CreateBackupAsync("outside-safety-source");
            await WriteAuthoritativeStateAsync(interruptedProvider, packageRoot, "old-state", "old-package");
            faults.Enqueue(
                VisionDatabaseMaintenanceStage.DatabaseReplaced,
                (_, _) => throw new VisionDatabaseMaintenanceInterruptionException("leave-marker"));
            faults.Arm();
            Func<Task> interruptedAct = () => interruptedService.RestoreBackupAsync(backup.BackupPath);
            await interruptedAct.Should().ThrowAsync<VisionDatabaseMaintenanceException>();

            var markerPath = dbPath + ".maintenance-recovery.json";
            var marker = await ReadRecoveryMarkerAsync(dbPath);
            var outsideSafetyPath = Path.Combine(root, "outside-safety.cvdbbak");
            File.Copy(marker.SafetyBackupPath, outsideSafetyPath);
            marker.SafetyBackupPath = outsideSafetyPath;
            await File.WriteAllTextAsync(
                markerPath,
                JsonSerializer.Serialize(marker, VisionDatabaseMaintenance.JsonOptions));

            await using var recoveryProvider = CreateProvider(dbPath, packageRoot, backupRoot);
            var act = () => recoveryProvider
                .GetRequiredService<VisionDatabaseMaintenanceService>()
                .GetStatusAsync();
            var failure = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;

            failure.RecoveryRequired.Should().BeTrue();
            failure.ErrorCode.Should().Be("DB_MAINTENANCE_RECOVERY_REQUIRED");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task MalformedRecoveryMarker_ShouldFailClosedWithoutOpeningDatabase()
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot);
            await InitializeDatabaseAsync(provider);
            await File.WriteAllTextAsync(dbPath + ".maintenance-recovery.json", "{not-json");
            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();

            var act = () => service.GetStatusAsync();
            var failure = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;

            failure.RecoveryRequired.Should().BeTrue();
            failure.ErrorCode.Should().Be("DB_MAINTENANCE_RECOVERY_REQUIRED");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    private static async Task AssertInjectedStageFailureAsync(
        VisionDatabaseMaintenanceStage stage,
        Exception injectedException,
        string expectedErrorCode)
    {
        var root = CreateTempRoot();
        var dbPath = Path.Combine(root, "vision.db");
        var packageRoot = Path.Combine(root, "packages");
        var backupRoot = Path.Combine(root, "backups");
        var faults = new ArmableMaintenanceFaultInjector();

        try
        {
            await using var provider = CreateProvider(dbPath, packageRoot, backupRoot, faults);
            await InitializeDatabaseAsync(provider);
            var service = provider.GetRequiredService<VisionDatabaseMaintenanceService>();
            await WriteAuthoritativeStateAsync(provider, packageRoot, "source-state", "source-package");
            VisionDatabaseBackupResult? restoreSource = null;
            if (!IsBackupStage(stage))
            {
                restoreSource = await service.CreateBackupAsync("fault-source");
                await WriteAuthoritativeStateAsync(provider, packageRoot, "prior-state", "prior-package");
            }

            faults.Enqueue(stage, (_, _) => throw injectedException);
            faults.Arm();
            Func<Task> act = IsBackupStage(stage)
                ? async () => { await service.CreateBackupAsync("fault-target"); }
                : async () => { await service.RestoreBackupAsync(restoreSource!.BackupPath); };

            var failure = (await act.Should().ThrowAsync<VisionDatabaseMaintenanceException>()).Which;

            failure.ErrorCode.Should().Be(expectedErrorCode);
            failure.Retryable.Should().BeTrue();
            failure.RecoveryRequired.Should().BeFalse();
            failure.Message.Should().NotContain(injectedException.Message);
            failure.PublicMessage.Should().NotContain(injectedException.Message);
            failure.Stage.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    private static bool IsBackupStage(VisionDatabaseMaintenanceStage stage)
    {
        return stage is VisionDatabaseMaintenanceStage.BackupStarted or
            VisionDatabaseMaintenanceStage.BackupDatabaseCandidate or
            VisionDatabaseMaintenanceStage.BackupPackageCandidate or
            VisionDatabaseMaintenanceStage.BackupCommit;
    }

    private static ServiceProvider CreateProvider(
        string dbPath,
        string packageRoot,
        string backupRoot,
        IVisionDatabaseMaintenanceFaultInjector? faultInjector = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton(sp => new VisionDatabaseMaintenanceService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VisionDatabaseMaintenanceService>.Instance,
            new VisionDatabaseMaintenanceOptions
            {
                PackageRootDirectory = packageRoot,
                BackupRootDirectory = backupRoot
            },
            faultInjector ?? NoOpVisionDatabaseMaintenanceFaultInjector.Instance));
        return services.BuildServiceProvider();
    }

    private static async Task InitializeDatabaseAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        await VisionDatabaseInitializer.InitializeAsync(db);
    }

    private static async Task InsertStationPackageRecordAsync(
        ServiceProvider provider,
        string packageId,
        string packagePath)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        db.StationPackageRecords.Add(new StationPackageRecordEntity
        {
            PackageId = packageId,
            PackageName = "Roundtrip Package",
            PackageVersion = "1.0.0",
            PackageKind = "Production",
            FlowHash = "sha256:roundtrip",
            FileName = Path.GetFileName(packagePath),
            FilePath = packagePath,
            SizeBytes = new FileInfo(packagePath).Length,
            Sha256 = "sha256",
            CreatedBy = "unit-test",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task WriteAuthoritativeStateAsync(
        ServiceProvider provider,
        string packageRoot,
        string packageId,
        string packageContent)
    {
        await DeleteStationPackageRecordsAsync(provider);
        if (Directory.Exists(packageRoot))
        {
            Directory.Delete(packageRoot, recursive: true);
        }

        var packagePath = Path.Combine(packageRoot, "files", packageId, packageId + ".cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, packageContent);
        Directory.CreateDirectory(Path.Combine(packageRoot, "empty", packageId));
        await InsertStationPackageRecordAsync(provider, packageId, packagePath);
    }

    private static async Task<MaintenanceSnapshot> ReadAuthoritativeStateAsync(
        ServiceProvider provider,
        string packageRoot)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var packageIds = await db.StationPackageRecords
            .AsNoTracking()
            .OrderBy(record => record.PackageId)
            .Select(record => record.PackageId)
            .ToListAsync();
        var packageFiles = Directory.Exists(packageRoot)
            ? Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(packageRoot, path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'),
                    File.ReadAllText,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packageDirectories = Directory.Exists(packageRoot)
            ? Directory.EnumerateDirectories(packageRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        return new MaintenanceSnapshot(packageIds, packageFiles, packageDirectories);
    }

    private static async Task<VisionDatabaseRecoveryMarker> ReadRecoveryMarkerAsync(string databasePath)
    {
        var marker = JsonSerializer.Deserialize<VisionDatabaseRecoveryMarker>(
            await File.ReadAllTextAsync(databasePath + ".maintenance-recovery.json"),
            VisionDatabaseMaintenance.JsonOptions);
        marker.Should().NotBeNull();
        return marker!;
    }

    private static async Task DeleteStationPackageRecordsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        db.StationPackageRecords.RemoveRange(db.StationPackageRecords);
        await db.SaveChangesAsync();
    }

    private static async Task SeedHistoryForCleanupAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            INSERT INTO "InspectionResults"
                ("Id", "ProjectId", "Status", "ProcessingTimeMs", "ImageId", "ConfidenceScore", "ErrorMessage", "OutputImage", "InspectionTime", "OutputDataJson", "AnalysisDataJson", "FlowVersionHash", "CalibrationBundleId", "SessionId", "CreatedAt", "ModifiedAt", "IsDeleted")
            VALUES
                ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 1, 10, NULL, NULL, NULL, NULL, '2020-01-01T00:00:00', NULL, NULL, NULL, NULL, NULL, '2020-01-01T00:00:00', NULL, 0),
                ('33333333-3333-3333-3333-333333333333', '22222222-2222-2222-2222-222222222222', 1, 10, NULL, NULL, NULL, NULL, '{{DateTime.UtcNow:O}}', NULL, NULL, NULL, NULL, NULL, '{{DateTime.UtcNow:O}}', NULL, 0);

            INSERT INTO "Defects"
                ("Id", "InspectionResultId", "Type", "X", "Y", "Width", "Height", "ConfidenceScore", "Description", "AnnotationData", "CreatedAt", "ModifiedAt", "IsDeleted")
            VALUES
                ('44444444-4444-4444-4444-444444444444', '11111111-1111-1111-1111-111111111111', 1, 0, 0, 1, 1, 0.9, NULL, NULL, '2020-01-01T00:00:00', NULL, 0);

            INSERT INTO "StationResultSummaries"
                ("StationId", "SequenceId", "MessageId", "RunId", "PackageId", "PackageName", "PackageVersion", "FlowHash", "ImageId", "Outcome", "InspectionStatus", "ExecutionTimeMs", "DiagnosticCode", "DiagnosticMessage", "PrimaryOutputsPreviewJson", "StartedAtUtc", "CompletedAtUtc", "CreatedAtUtc", "ReceivedAtUtc")
            VALUES
                ('station-cleanup', 1, 'old-result', 'run-old', 'pkg-cleanup', 'Package', '1.0.0', 'sha256:cleanup', 'image-old', 'Ok', 'Passed', 1, 'OK', NULL, '{}', '2020-01-01T00:00:00+00:00', '2020-01-01T00:00:01+00:00', '2020-01-01T00:00:01+00:00', '2020-01-01T00:00:02+00:00'),
                ('station-cleanup', 2, 'new-result', 'run-new', 'pkg-cleanup', 'Package', '1.0.0', 'sha256:cleanup', 'image-new', 'Ok', 'Passed', 1, 'OK', NULL, '{}', '{{DateTimeOffset.UtcNow:O}}', '{{DateTimeOffset.UtcNow:O}}', '{{DateTimeOffset.UtcNow:O}}', '{{DateTimeOffset.UtcNow:O}}');

            INSERT INTO "StationHealthSnapshots"
                ("StationId", "SequenceId", "MessageId", "RuntimeState", "ProcessUptimeSeconds", "CpuUsagePercent", "WorkingSetMb", "PrivateMemoryMb", "DiskFreeMb", "DiskTotalMb", "SpoolPendingCount", "SpoolBytes", "CameraStatusSummary", "PlcStatusSummary", "CurrentPackageId", "CurrentPackageHealth", "LastErrorCode", "LastErrorMessage", "CreatedAtUtc", "ReceivedAtUtc")
            VALUES
                ('station-cleanup', 1, 'old-health', 'Running', 1, 1, 1, 1, 1, 1, 0, 0, NULL, NULL, 'pkg-cleanup', 'Loaded', NULL, NULL, '2020-01-01T00:00:00+00:00', '2020-01-01T00:00:01+00:00'),
                ('station-cleanup', 2, 'new-health', 'Running', 1, 1, 1, 1, 1, 1, 0, 0, NULL, NULL, 'pkg-cleanup', 'Loaded', NULL, NULL, '{{DateTimeOffset.UtcNow:O}}', '{{DateTimeOffset.UtcNow:O}}');

            INSERT INTO "StationLogSummaries"
                ("StationId", "SequenceId", "MessageId", "TimestampUtc", "Level", "Source", "EventId", "MessageTemplate", "RenderedMessage", "ExceptionType", "ExceptionMessage", "CorrelationId", "RunId", "PackageId", "CreatedAtUtc", "ReceivedAtUtc")
            VALUES
                ('station-cleanup', 1, 'old-log', '2020-01-01T00:00:00+00:00', 'Info', 'Station', NULL, NULL, 'old', NULL, NULL, NULL, NULL, 'pkg-cleanup', '2020-01-01T00:00:00+00:00', '2020-01-01T00:00:01+00:00'),
                ('station-cleanup', 2, 'new-log', '{{DateTimeOffset.UtcNow:O}}', 'Info', 'Station', NULL, NULL, 'new', NULL, NULL, NULL, NULL, 'pkg-cleanup', '{{DateTimeOffset.UtcNow:O}}', '{{DateTimeOffset.UtcNow:O}}');

            INSERT INTO "StationConnectionEvents"
                ("StationId", "EventType", "Message", "CreatedAtUtc")
            VALUES
                ('station-cleanup', 'Old', 'old', '2020-01-01T00:00:00+00:00'),
                ('station-cleanup', 'New', 'new', '{{DateTimeOffset.UtcNow:O}}');

            INSERT INTO "StationPackageRecords"
                ("PackageId", "PackageName", "PackageVersion", "PackageKind", "FlowHash", "FileName", "FilePath", "SizeBytes", "Sha256", "CreatedBy", "CreatedAtUtc")
            VALUES
                ('pkg-cleanup', 'Package', '1.0.0', 'Production', 'sha256:cleanup', 'pkg.cvpkg', 'pkg.cvpkg', 1, 'sha256', 'Studio', '2020-01-01T00:00:00+00:00');

            INSERT INTO "StationCommandRecords"
                ("CommandId", "StationId", "CommandType", "PayloadJson", "Status", "ProgressPercent", "CreatedAtUtc", "ExpiresAtUtc", "DeliveredAtUtc", "AcceptedAtUtc", "StartedAtUtc", "CompletedAtUtc", "IssuedBy", "CorrelationId", "ResultMessage", "ErrorCode", "ErrorDetail")
            VALUES
                ('cmd-cleanup', 'station-cleanup', 'Ping', '{}', 'Created', 0, '2020-01-01T00:00:00+00:00', '2020-01-01T00:05:00+00:00', NULL, NULL, NULL, NULL, 'Studio', 'corr-cleanup', NULL, NULL, NULL);

            INSERT INTO "StationAuditRecords"
                ("AuditId", "UserId", "UserName", "Action", "TargetStationId", "CommandId", "PayloadSummary", "CreatedAtUtc", "Result", "ClientIp")
            VALUES
                ('audit-cleanup', 'user', 'admin', 'CreateCommand', 'station-cleanup', 'cmd-cleanup', '{}', '2020-01-01T00:00:00+00:00', 'Created', '127.0.0.1');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(ServiceProvider provider, string tableName)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(1) FROM "{tableName}";""";
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseMaintenance.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record MaintenanceSnapshot(
        IReadOnlyList<string> PackageIds,
        IReadOnlyDictionary<string, string> PackageFiles,
        IReadOnlyList<string> PackageDirectories);

    private sealed class ArmableMaintenanceFaultInjector : IVisionDatabaseMaintenanceFaultInjector
    {
        private readonly object _sync = new();
        private readonly Dictionary<VisionDatabaseMaintenanceStage, Queue<Action<string, string>>> _actions = [];
        private bool _armed;

        public void Enqueue(
            VisionDatabaseMaintenanceStage stage,
            Action<string, string> action)
        {
            lock (_sync)
            {
                if (!_actions.TryGetValue(stage, out var queue))
                {
                    queue = new Queue<Action<string, string>>();
                    _actions.Add(stage, queue);
                }

                queue.Enqueue(action);
            }
        }

        public void Arm()
        {
            lock (_sync)
            {
                _armed = true;
            }
        }

        public void OnStage(
            VisionDatabaseMaintenanceStage stage,
            string databasePath,
            string operationId)
        {
            Action<string, string>? action = null;
            lock (_sync)
            {
                if (_armed && _actions.TryGetValue(stage, out var queue) && queue.Count > 0)
                {
                    action = queue.Dequeue();
                }
            }

            action?.Invoke(databasePath, operationId);
        }
    }
}
