using System.Data;
using Acme.Product.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Product.Desktop.Data;

internal static class VisionDatabaseInitializer
{
    public static async Task InitializeAsync(VisionDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.GetMigrations().Any())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (dbContext.Database.IsSqlite())
        {
            await EnsureInspectionResultAnalysisDataColumnAsync(dbContext, cancellationToken);
            await EnsureStationSyncSchemaAsync(dbContext, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        }
    }

    private static async Task EnsureInspectionResultAnalysisDataColumnAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await EnsureTextColumnExistsAsync(dbContext, "InspectionResults", "AnalysisDataJson", cancellationToken);
    }

    private static async Task EnsureStationSyncSchemaAsync(
        VisionDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var statements = new[]
        {
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
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_SequenceId" ON "StationResultSummaries" ("StationId", "SequenceId");""",
            """CREATE INDEX IF NOT EXISTS "IX_StationResultSummaries_StationId_CompletedAtUtc" ON "StationResultSummaries" ("StationId", "CompletedAtUtc");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationHealthSnapshots_StationId_SequenceId" ON "StationHealthSnapshots" ("StationId", "SequenceId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationAlarmEvents_AlarmId" ON "StationAlarmEvents" ("AlarmId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationCommandRecords_CommandId" ON "StationCommandRecords" ("CommandId");""",
            """CREATE INDEX IF NOT EXISTS "IX_StationCommandRecords_StationId_Status" ON "StationCommandRecords" ("StationId", "Status");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationSyncCursors_StationId" ON "StationSyncCursors" ("StationId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationLogSummaries_StationId_SequenceId" ON "StationLogSummaries" ("StationId", "SequenceId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationAuditRecords_AuditId" ON "StationAuditRecords" ("AuditId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_StationPackageRecords_PackageId" ON "StationPackageRecords" ("PackageId");"""
        };

        foreach (var statement in statements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }

    private static async Task EnsureTextColumnExistsAsync(
        VisionDbContext dbContext,
        string tableName,
        string columnName,
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
            await using var pragmaCommand = connection.CreateCommand();
            pragmaCommand.CommandText = $"PRAGMA table_info(\"{tableName}\");";

            await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var existingColumn = reader["name"]?.ToString();
                if (string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await reader.CloseAsync();
            var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" TEXT NULL;";
            await dbContext.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
