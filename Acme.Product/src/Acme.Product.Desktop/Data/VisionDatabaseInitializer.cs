using System.Data;
using System.Data.Common;
using Acme.Product.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.Product.Desktop.Data;

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
                await AdoptCompleteLegacySqliteSchemaAsync(dbContext, migrations[0], cancellationToken);
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
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        }
    }

    private static OutdatedVisionDatabaseException CreateOutdatedDatabaseMigrationException(
        VisionDbContext dbContext,
        SqliteException exception)
    {
        return new OutdatedVisionDatabaseException(
            new OutdatedVisionDatabase(
                GetDatabasePath(dbContext.Database.GetDbConnection()),
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
        string baselineMigrationId,
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
            if (await TableExistsAsync(connection, MigrationsHistoryTable, cancellationToken))
            {
                return;
            }

            var existingTables = await GetExistingUserTablesAsync(connection, cancellationToken);
            if (existingTables.Count == 0)
            {
                return;
            }

            var hasLegacyCoreTables =
                await TableExistsAsync(connection, "Projects", cancellationToken) &&
                await TableExistsAsync(connection, "Operators", cancellationToken) &&
                await TableExistsAsync(connection, "InspectionResults", cancellationToken) &&
                await TableExistsAsync(connection, "Defects", cancellationToken) &&
                await TableExistsAsync(connection, "Users", cancellationToken);

            var missingSchemaItems = await GetMissingBaselineSqliteSchemaItemsAsync(
                dbContext,
                connection,
                cancellationToken);

            if (missingSchemaItems.Count > 0 &&
                hasLegacyCoreTables &&
                IsRepairableLegacySqliteSchemaGap(missingSchemaItems))
            {
                await RepairLegacySqliteSchemaAsync(dbContext, cancellationToken);

                missingSchemaItems = await GetMissingBaselineSqliteSchemaItemsAsync(
                    dbContext,
                    connection,
                    cancellationToken);
            }

            if (missingSchemaItems.Count > 0)
            {
                var preview = string.Join(", ", missingSchemaItems.Take(8));
                var suffix = missingSchemaItems.Count > 8 ? $" and {missingSchemaItems.Count - 8} more" : string.Empty;
                throw new OutdatedVisionDatabaseException(new OutdatedVisionDatabase(
                    GetDatabasePath(connection),
                    missingSchemaItems),
                    "Existing SQLite database looks like a legacy ClearVision database, but its schema is not complete enough " +
                    $"to adopt the EF migration baseline. Missing schema items: {preview}{suffix}.");
            }

            await using var historyCommand = (DbCommand)connection.CreateCommand();
            historyCommand.CommandText = $"""
                CREATE TABLE "{MigrationsHistoryTable}" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                INSERT INTO "{MigrationsHistoryTable}" ("MigrationId", "ProductVersion")
                VALUES ($migrationId, $productVersion);
                """;
            var migrationIdParameter = historyCommand.CreateParameter();
            migrationIdParameter.ParameterName = "$migrationId";
            migrationIdParameter.Value = baselineMigrationId;
            historyCommand.Parameters.Add(migrationIdParameter);

            var productVersionParameter = historyCommand.CreateParameter();
            productVersionParameter.ParameterName = "$productVersion";
            productVersionParameter.Value = "8.0.0";
            historyCommand.Parameters.Add(productVersionParameter);

            await historyCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool IsRepairableLegacySqliteSchemaGap(IReadOnlyCollection<string> missingSchemaItems)
    {
        return missingSchemaItems.All(item => RepairableLegacySqliteSchemaItems.Contains(item));
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

        foreach (var path in GetSqliteDatabaseFiles(databasePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task<bool> IsBaselineSqliteSchemaCompleteAsync(
        VisionDbContext dbContext,
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        var missingItems = await GetMissingBaselineSqliteSchemaItemsAsync(
            dbContext,
            connection,
            cancellationToken,
            stopAfterFirstMissing: true);

        return missingItems.Count == 0;
    }

    private static async Task<List<string>> GetMissingBaselineSqliteSchemaItemsAsync(
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

    private static async Task RepairLegacySqliteSchemaAsync(
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

        foreach (var statement in LegacyStationSyncSchemaStatements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }

    private static readonly HashSet<string> RepairableLegacySqliteSchemaItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "column:InspectionResults.AnalysisDataJson",
        "table:StationAlarmEvents",
        "table:StationAuditRecords",
        "table:StationCommandRecords",
        "table:StationConnectionEvents",
        "table:StationHealthSnapshots",
        "table:StationLogSummaries",
        "table:StationNodes",
        "table:StationPackageRecords",
        "table:StationResultSummaries",
        "table:StationSyncCursors",
        "index:IX_StationAlarmEvents_AlarmId",
        "index:IX_StationAuditRecords_AuditId",
        "index:IX_StationCommandRecords_CommandId",
        "index:IX_StationCommandRecords_StationId_Status",
        "index:IX_StationHealthSnapshots_StationId_SequenceId",
        "index:IX_StationLogSummaries_StationId_SequenceId",
        "index:IX_StationNodes_StationId",
        "index:IX_StationPackageRecords_PackageId",
        "index:IX_StationResultSummaries_StationId_CompletedAtUtc",
        "index:IX_StationResultSummaries_StationId_SequenceId",
        "index:IX_StationSyncCursors_StationId"
    };

    private static readonly string[] LegacyStationSyncSchemaStatements =
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

    private static async Task<bool> ColumnExistsAsync(
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

    private static Task<bool> TableExistsAsync(
        IDbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        return SchemaObjectExistsAsync(connection, "table", tableName, cancellationToken);
    }

    private static Task<bool> IndexExistsAsync(
        IDbConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        return SchemaObjectExistsAsync(connection, "index", indexName, cancellationToken);
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

    private static async Task<HashSet<string>> GetExistingUserTablesAsync(
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

    private static string GetDatabasePath(IDbConnection connection)
    {
        var databasePath = connection.Database;
        if (connection is DbConnection dbConnection &&
            !string.IsNullOrWhiteSpace(dbConnection.DataSource))
        {
            databasePath = dbConnection.DataSource;
        }

        return databasePath;
    }

    private static IEnumerable<string> GetSqliteDatabaseFiles(string databasePath)
    {
        yield return databasePath;
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
    }

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
