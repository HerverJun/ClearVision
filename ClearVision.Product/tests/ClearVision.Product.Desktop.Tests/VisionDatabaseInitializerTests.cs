using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class VisionDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_ShouldRoundTripProjectDecisionConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var projectId = Guid.Empty;
            var operatorId = Guid.NewGuid();

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
                var project = new Project("decision-roundtrip");
                projectId = project.Id;
                var op = new Operator(operatorId, "FinalJudge", OperatorType.ResultJudgment, 0, 0);
                op.AddOutputPort("IsOk", PortDataType.Boolean);
                project.Flow.AddOperator(op);
                project.Flow.DecisionConfiguration = new DecisionConfiguration
                {
                    FinalDecisionBinding = new FinalDecisionBinding
                    {
                        SourceOperatorId = operatorId,
                        SourceOutputPortId = op.OutputPorts.Single().Id,
                        SourceOutputName = "IsOk",
                        DataType = DecisionValueType.Boolean,
                        Rule = DecisionInterpretationRule.Boolean
                    },
                    MissingDecisionPolicy = MissingDecisionPolicy.Invalid
                };
                dbContext.Projects.Add(project);
                await dbContext.SaveChangesAsync();
            }

            await using (var dbContext = new VisionDbContext(options))
            {
                var reloaded = await dbContext.Projects
                    .Include(project => project.Flow)
                    .SingleAsync(project => project.Id == projectId);
                reloaded.Flow.DecisionConfiguration.Should().NotBeNull();
                reloaded.Flow.DecisionConfiguration!.FinalDecisionBinding!.SourceOperatorId.Should().Be(operatorId);
                reloaded.Flow.DecisionConfiguration.MissingDecisionPolicy.Should().Be(MissingDecisionPolicy.Invalid);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldCreateNewSqliteDatabase_WithCurrentSchemaVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeTrue();
            (await MigrationHistoryCountAsync(connection)).Should().Be(CurrentMigrationCount());
            (await ColumnExistsAsync(connection, "StationPackageRecords", "PackageKind")).Should().BeTrue();
            (await UserVersionAsync(connection)).Should().Be(VisionDatabaseMaintenance.CurrentSqliteSchemaVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldUpgradeInitialMigrationDatabase_AndPreserveStationData()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreateInitialMigrationDatabaseWithStationDataAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await MigrationHistoryCountAsync(connection)).Should().Be(CurrentMigrationCount());
            (await ColumnExistsAsync(connection, "StationPackageRecords", "PackageKind")).Should().BeTrue();
            (await ScalarStringAsync(connection, """SELECT "PackageKind" FROM "StationPackageRecords" WHERE "PackageId" = 'pkg-upgrade';"""))
                .Should()
                .Be("Production");
            (await ScalarLongAsync(connection, """SELECT COUNT(1) FROM "StationPackageRecords";""")).Should().Be(1);
            (await ScalarLongAsync(connection, """SELECT COUNT(1) FROM "StationResultSummaries";""")).Should().Be(1);
            (await ScalarLongAsync(connection, """SELECT COUNT(1) FROM "StationHealthSnapshots";""")).Should().Be(1);
            (await ScalarLongAsync(connection, """SELECT COUNT(1) FROM "StationCommandRecords";""")).Should().Be(1);
            (await ScalarLongAsync(connection, """SELECT COUNT(1) FROM "StationAuditRecords";""")).Should().Be(1);
            (await UserVersionAsync(connection)).Should().Be(VisionDatabaseMaintenance.CurrentSqliteSchemaVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldAdoptCompleteLegacySqliteSchema_WhenDatabaseAlreadyExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreateCompleteDatabaseWithoutMigrationHistoryAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeTrue();
            (await MigrationHistoryCountAsync(connection)).Should().Be(CurrentMigrationCount());
            (await ColumnExistsAsync(connection, "InspectionResults", "AnalysisDataJson")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "InspectionResults", "ExecutionOutcome")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "InspectionResults", "DecisionOutcome")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "InspectionResults", "DecisionSource")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "InspectionResults", "ReasonCode")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "InspectionResults", "HasJudgmentSignal")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "Projects", "Flow_DecisionConfiguration")).Should().BeTrue();
            (await TableExistsAsync(connection, "StationNodes")).Should().BeTrue();
            (await TableExistsAsync(connection, "StationResultSummaries")).Should().BeTrue();
            (await IndexExistsAsync(connection, "IX_StationNodes_StationId")).Should().BeTrue();
            (await IndexExistsAsync(connection, "IX_StationResultSummaries_StationId_SequenceId")).Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldUpgradePreviousDatabaseAndReadLegacyInspectionResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");
        var resultId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        try
        {
            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var oldContext = new VisionDbContext(options))
            {
                var migrator = oldContext.GetService<IMigrator>();
                await migrator.MigrateAsync("20260628000000_AddProjectPersistenceRevision");
                await oldContext.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO "InspectionResults"
                        ("Id", "ProjectId", "Status", "ProcessingTimeMs", "InspectionTime", "CreatedAt", "IsDeleted")
                    VALUES
                        ({0}, {1}, {2}, 7, {3}, {3}, 0);
                    """,
                    resultId,
                    projectId,
                    (int)InspectionStatus.OK,
                    DateTime.UtcNow);
            }

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
                var legacy = await dbContext.InspectionResults.SingleAsync(item => item.Id == resultId);

                legacy.ExecutionOutcome.Should().BeNull();
                legacy.DecisionOutcome.Should().BeNull();
                legacy.HasJudgmentSignal.Should().BeNull();
                legacy.GetOutcome().Execution.Should().Be(ExecutionOutcome.Succeeded);
                legacy.GetOutcome().Decision.Should().Be(DecisionOutcome.Ok);
                legacy.GetOutcome().HasJudgmentSignal.Should().BeTrue();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldRepairLegacyCoreSchemaMissingStationSyncTables()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreateCoreDatabaseWithoutStationSyncSchemaAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await MigrationHistoryCountAsync(connection)).Should().Be(CurrentMigrationCount());
            (await TableExistsAsync(connection, "StationAlarmEvents")).Should().BeTrue();
            (await IndexExistsAsync(connection, "IX_StationAlarmEvents_StationId_IsActive")).Should().BeTrue();
            (await ColumnExistsAsync(connection, "StationPackageRecords", "PackageKind")).Should().BeTrue();
            (await UserVersionAsync(connection)).Should().Be(VisionDatabaseMaintenance.CurrentSqliteSchemaVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldRepairLegacyDatabaseMissingInstallationLatch_AndBackfillCompleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreateCompleteDatabaseWithoutMigrationHistoryAsync(dbPath);
            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    DROP TABLE "InstallationStates";
                    INSERT INTO "Users"
                        ("Id", "Username", "PasswordHash", "DisplayName", "Role", "IsActive", "LastLoginAt", "CreatedAt", "ModifiedAt", "IsDeleted")
                    VALUES
                        ('00000000-0000-0000-0000-000000000028', 'legacy-operator', 'hash', 'Legacy Operator', 2, 1, NULL, '2026-01-01T00:00:00', NULL, 0);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
            }

            await using var verifyConnection = new SqliteConnection($"Data Source={dbPath}");
            await verifyConnection.OpenAsync();
            (await ScalarLongAsync(
                verifyConnection,
                "SELECT \"IsCompleted\" FROM \"InstallationStates\" WHERE \"Id\" = 1;"))
                .Should().Be(1);
            (await SchemaObjectExistsAsync(
                verifyConnection,
                "trigger",
                "TR_InstallationStates_PreventReopen")).Should().BeTrue();
            (await MigrationHistoryCountAsync(verifyConnection)).Should().Be(CurrentMigrationCount());
            (await UserVersionAsync(verifyConnection)).Should().Be(VisionDatabaseMaintenance.CurrentSqliteSchemaVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldRejectIncompleteLegacySqliteSchema_WhenDatabaseAlreadyExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreateIncompleteLegacyDatabaseAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                var act = () => VisionDatabaseInitializer.InitializeAsync(dbContext);

                await act.Should()
                    .ThrowAsync<OutdatedVisionDatabaseException>()
                    .WithMessage("*schema is not complete enough*");
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldDiscardOutdatedLegacySqliteSchema_WhenUserConfirms()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");
        var decisionRequests = new List<OutdatedVisionDatabase>();

        try
        {
            await CreateIncompleteLegacyDatabaseAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var initializationOptions = new VisionDatabaseInitializationOptions
            {
                OutdatedDatabaseDecisionProvider = (database, _) =>
                {
                    decisionRequests.Add(database);
                    return Task.FromResult(OutdatedVisionDatabaseDecision.Discard);
                }
            };

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext, initializationOptions);
            }

            decisionRequests.Should().ContainSingle();
            decisionRequests[0].DatabasePath.Should().Be(dbPath);
            decisionRequests[0].MissingSchemaItems.Should().NotBeEmpty();

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeTrue();
            (await MigrationHistoryCountAsync(connection)).Should().Be(CurrentMigrationCount());
            (await TableExistsAsync(connection, "StationNodes")).Should().BeTrue();
            (await TableExistsAsync(connection, "StationPackageRecords")).Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldRejectPartialLegacySqliteSchema_BeforeEfMigrationCreatesTables()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreatePartialLegacyDatabaseAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                var act = () => VisionDatabaseInitializer.InitializeAsync(dbContext);

                await act.Should()
                    .ThrowAsync<OutdatedVisionDatabaseException>()
                    .WithMessage("*schema is not complete enough*");
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_ShouldDiscardMigrationConflict_WhenUserConfirms()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");
        var decisionRequests = new List<OutdatedVisionDatabase>();

        try
        {
            await CreateConflictingDatabaseWithMigrationHistoryAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var initializationOptions = new VisionDatabaseInitializationOptions
            {
                OutdatedDatabaseDecisionProvider = (database, _) =>
                {
                    decisionRequests.Add(database);
                    return Task.FromResult(OutdatedVisionDatabaseDecision.Discard);
                }
            };

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext, initializationOptions);
            }

            decisionRequests.Should().ContainSingle();
            decisionRequests[0].MissingSchemaItems.Should().Contain(item => item.StartsWith("migration:", StringComparison.Ordinal));

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeTrue();
            (await MigrationHistoryCountAsync(connection)).Should().Be(CurrentMigrationCount());
            (await TableExistsAsync(connection, "Projects")).Should().BeTrue();
            (await TableExistsAsync(connection, "StationNodes")).Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(root);
        }
    }

    private static async Task CreateCompleteDatabaseWithoutMigrationHistoryAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var dbContext = new VisionDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static async Task CreateCoreDatabaseWithoutStationSyncSchemaAsync(string dbPath)
    {
        await CreateCompleteDatabaseWithoutMigrationHistoryAsync(dbPath);

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DROP TABLE "StationAlarmEvents";
            DROP TABLE "StationAuditRecords";
            DROP TABLE "StationCommandRecords";
            DROP TABLE "StationConnectionEvents";
            DROP TABLE "StationHealthSnapshots";
            DROP TABLE "StationLogSummaries";
            DROP TABLE "StationNodes";
            DROP TABLE "StationPackageRecords";
            DROP TABLE "StationResultSummaries";
            DROP TABLE "StationSyncCursors";
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateInitialMigrationDatabaseWithStationDataAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var dbContext = new VisionDbContext(options))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260509024011_InitialVisionSchema");
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "StationPackageRecords"
                ("PackageId", "PackageName", "PackageVersion", "FlowHash", "FileName", "FilePath", "SizeBytes", "Sha256", "CreatedBy", "CreatedAtUtc")
            VALUES
                ('pkg-upgrade', 'Upgrade Package', '1.0.0', 'sha256:pkg', 'pkg.cvpkg', 'pkg.cvpkg', 128, 'sha256', 'Studio', '2026-01-01T00:00:00+00:00');

            INSERT INTO "StationResultSummaries"
                ("StationId", "SequenceId", "MessageId", "RunId", "PackageId", "PackageName", "PackageVersion", "FlowHash", "ImageId", "Outcome", "InspectionStatus", "ExecutionTimeMs", "DiagnosticCode", "DiagnosticMessage", "PrimaryOutputsPreviewJson", "StartedAtUtc", "CompletedAtUtc", "CreatedAtUtc", "ReceivedAtUtc")
            VALUES
                ('station-upgrade', 1, 'result-1', 'run-1', 'pkg-upgrade', 'Upgrade Package', '1.0.0', 'sha256:pkg', 'image-1', 'Ok', 'Passed', 42, 'OK', NULL, '{}', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:01+00:00', '2026-01-01T00:00:01+00:00', '2026-01-01T00:00:02+00:00');

            INSERT INTO "StationHealthSnapshots"
                ("StationId", "SequenceId", "MessageId", "RuntimeState", "ProcessUptimeSeconds", "CpuUsagePercent", "WorkingSetMb", "PrivateMemoryMb", "DiskFreeMb", "DiskTotalMb", "SpoolPendingCount", "SpoolBytes", "CameraStatusSummary", "PlcStatusSummary", "CurrentPackageId", "CurrentPackageHealth", "LastErrorCode", "LastErrorMessage", "CreatedAtUtc", "ReceivedAtUtc")
            VALUES
                ('station-upgrade', 2, 'health-1', 'Running', 10, 1.5, 100, 120, 2048, 4096, 0, 0, 'Connected', 'Connected', 'pkg-upgrade', 'Loaded', NULL, NULL, '2026-01-01T00:00:03+00:00', '2026-01-01T00:00:04+00:00');

            INSERT INTO "StationCommandRecords"
                ("CommandId", "StationId", "CommandType", "PayloadJson", "Status", "ProgressPercent", "CreatedAtUtc", "ExpiresAtUtc", "DeliveredAtUtc", "AcceptedAtUtc", "StartedAtUtc", "CompletedAtUtc", "IssuedBy", "CorrelationId", "ResultMessage", "ErrorCode", "ErrorDetail")
            VALUES
                ('cmd-upgrade', 'station-upgrade', 'Ping', '{}', 'Created', 0, '2026-01-01T00:00:05+00:00', '2026-01-01T00:05:05+00:00', NULL, NULL, NULL, NULL, 'Studio', 'corr-upgrade', NULL, NULL, NULL);

            INSERT INTO "StationAuditRecords"
                ("AuditId", "UserId", "UserName", "Action", "TargetStationId", "CommandId", "PayloadSummary", "CreatedAtUtc", "Result", "ClientIp")
            VALUES
                ('audit-upgrade', 'user-1', 'admin', 'CreateCommand', 'station-upgrade', 'cmd-upgrade', '{}', '2026-01-01T00:00:06+00:00', 'Created', '127.0.0.1');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateIncompleteLegacyDatabaseAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE "Projects" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Projects" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Version" TEXT NOT NULL
            );
            CREATE TABLE "Operators" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Operators" PRIMARY KEY,
                "ProjectId" TEXT NOT NULL,
                "Name" TEXT NOT NULL
            );
            CREATE TABLE "InspectionResults" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_InspectionResults" PRIMARY KEY,
                "ProjectId" TEXT NOT NULL,
                "Status" INTEGER NOT NULL,
                "ProcessingTimeMs" INTEGER NOT NULL,
                "InspectionTime" TEXT NOT NULL
            );
            CREATE TABLE "Defects" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Defects" PRIMARY KEY,
                "InspectionResultId" TEXT NOT NULL
            );
            CREATE TABLE "Users" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                "Username" TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreatePartialLegacyDatabaseAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE "Projects" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Projects" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Version" TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateConflictingDatabaseWithMigrationHistoryAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            CREATE TABLE "Projects" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Projects" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Version" TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> MigrationHistoryCountAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(1) FROM "__EFMigrationsHistory";""";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static int CurrentMigrationCount()
    {
        var options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var dbContext = new VisionDbContext(options);
        return dbContext.Database.GetMigrations().Count();
    }

    private static async Task<int> UserVersionAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static async Task<string?> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        return await SchemaObjectExistsAsync(connection, "table", tableName);
    }

    private static async Task<bool> IndexExistsAsync(SqliteConnection connection, string indexName)
    {
        return await SchemaObjectExistsAsync(connection, "index", indexName);
    }

    private static async Task<bool> SchemaObjectExistsAsync(SqliteConnection connection, string type, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type = $type AND name = $name;
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
