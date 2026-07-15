using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private static ServiceProvider CreateProvider(string dbPath, string packageRoot, string backupRoot)
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
            }));
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
}
