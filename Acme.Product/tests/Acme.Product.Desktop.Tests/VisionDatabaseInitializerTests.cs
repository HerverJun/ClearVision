using Acme.Product.Desktop.Data;
using Acme.Product.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Acme.Product.Desktop.Tests;

public sealed class VisionDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_ShouldRepairLegacySqliteSchema_WhenDatabaseAlreadyExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.DatabaseInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");

        try
        {
            await CreateLegacyDatabaseAsync(dbPath);

            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var dbContext = new VisionDbContext(options))
            {
                await VisionDatabaseInitializer.InitializeAsync(dbContext);
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            (await ColumnExistsAsync(connection, "InspectionResults", "AnalysisDataJson")).Should().BeTrue();
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

    private static async Task CreateLegacyDatabaseAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE "InspectionResults" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_InspectionResults" PRIMARY KEY,
                "ProjectId" TEXT NOT NULL,
                "Status" INTEGER NOT NULL,
                "ProcessingTimeMs" INTEGER NOT NULL,
                "InspectionTime" TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
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
