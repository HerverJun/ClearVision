using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Data, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "data-platform", Suites = "DataProcessingPhase1;DataProcessingPhase2")]

public sealed class DatabaseWriteOperatorTests
{
    private readonly DatabaseWriteOperator _operator;

    public DatabaseWriteOperatorTests()
    {
        _operator = new DatabaseWriteOperator(Substitute.For<ILogger<DatabaseWriteOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeDatabaseWrite()
    {
        _operator.OperatorType.Should().Be(OperatorType.DatabaseWrite);
    }

    [Fact]
    public void ValidateParameters_DefaultConstructor_ShouldFailClosedWithoutServerResolver()
    {
        var op = CreateOperator(connectionString: "Data Source=client-supplied.db");

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.Contains("RESOURCE_CONFIGURATION_UNAVAILABLE", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateParameters_WithValidSqliteConfig_ShouldBeValid()
    {
        const string serverConnectionString = "Data Source=file:test_validate?mode=memory&cache=shared";
        var sut = CreateWithDatabaseProfile(serverConnectionString, "InspectionResults", "SQLite");
        var op = CreateOperator(connectionString: "Data Source=client-forged.db", dbType: "MySQL");

        var result = sut.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidDbType_ShouldBeInvalid()
    {
        var sut = CreateWithDatabaseProfile(
            "Data Source=file:test_invalid_dbtype?mode=memory&cache=shared",
            "InspectionResults",
            "Oracle");
        var op = CreateOperator(
            connectionString: "Data Source=client-forged.db",
            dbType: "SQLite");

        var result = sut.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("DbType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_RejectedProfile_ShouldNotResolveOrInvokeDatabaseProvider()
    {
        var resolver = Substitute.For<IExecutionResourceProfileResolver>();
        resolver.ResolveDatabase(Arg.Any<string>(), Arg.Any<string>()).Returns(
            ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource>.Reject(
                "RESOURCE_DATABASE_PROFILE_NOT_FOUND",
                "The requested server database profile does not exist."));
        var providerResolutionCalls = 0;
        var sut = new DatabaseWriteOperator(
            Substitute.For<ILogger<DatabaseWriteOperator>>(),
            resolver,
            _ =>
            {
                providerResolutionCalls++;
                return null;
            });
        var op = CreateOperator(connectionString: "Data Source=client-forged.db", dbType: "MySQL");
        var inputs = new Dictionary<string, object> { ["Data"] = new { Name = "item" } };

        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RESOURCE_DATABASE_PROFILE_NOT_FOUND");
        providerResolutionCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithForgedRawTarget_ShouldDispatchServerResolvedSqliteProfile()
    {
        var tableName = $"Inspection_{Guid.NewGuid():N}".Substring(0, 20);
        var serverConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(serverConnectionString);
        await keepAlive.OpenAsync();

        var sut = CreateWithDatabaseProfile(serverConnectionString, tableName, "SQLite");
        var op = CreateOperator("Data Source=client-forged.db", tableName, "MySQL");
        var inputs = new Dictionary<string, object> { ["Data"] = new { Code = "A01", Score = 99 } };

        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["DbType"].Should().Be("SQLite");
        result.OutputData["RecordId"].Should().BeOfType<string>();

        var generatedRecordId = result.OutputData["RecordId"].ToString();
        generatedRecordId.Should().NotBeNullOrWhiteSpace();

        await using var countCommand = keepAlive.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithProvidedRecordId_ShouldUpsertSingleRowInSqlite()
    {
        var tableName = $"Inspection_{Guid.NewGuid():N}".Substring(0, 20);
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var op = CreateOperator(connectionString, tableName);
        var sut = CreateWithDatabaseProfile(connectionString, tableName, "SQLite");
        var recordId = "record-001";

        var firstResult = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = new { Value = 1 },
            ["RecordId"] = recordId
        });

        var secondResult = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = new { Value = 2 },
            ["RecordId"] = recordId
        });

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.OutputData!["RecordId"].Should().Be(recordId);

        await using var countCommand = keepAlive.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE Id = @Id";
        countCommand.Parameters.AddWithValue("@Id", recordId);
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        count.Should().Be(1);

        await using var dataCommand = keepAlive.CreateCommand();
        dataCommand.CommandText = $"SELECT Data FROM {tableName} WHERE Id = @Id";
        dataCommand.Parameters.AddWithValue("@Id", recordId);
        var data = (await dataCommand.ExecuteScalarAsync())?.ToString();
        data.Should().NotBeNullOrWhiteSpace();
        using var json = JsonDocument.Parse(data!);
        json.RootElement.GetProperty("Value").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithSameTableNameAcrossDifferentConnections_ShouldCreateTablePerDatabase()
    {
        var sharedTableName = $"Inspection_{Guid.NewGuid():N}".Substring(0, 20);
        var connectionStringA = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connectionStringB = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";

        await using var keepAliveA = new SqliteConnection(connectionStringA);
        await using var keepAliveB = new SqliteConnection(connectionStringB);
        await keepAliveA.OpenAsync();
        await keepAliveB.OpenAsync();

        var opA = CreateOperator(connectionStringA, sharedTableName);
        var opB = CreateOperator(connectionStringB, sharedTableName);
        var sutA = CreateWithDatabaseProfile(connectionStringA, sharedTableName, "SQLite");
        var sutB = CreateWithDatabaseProfile(connectionStringB, sharedTableName, "SQLite");

        var resultA = await sutA.ExecuteAsync(opA, new Dictionary<string, object> { ["Data"] = new { Name = "A" } });
        var resultB = await sutB.ExecuteAsync(opB, new Dictionary<string, object> { ["Data"] = new { Name = "B" } });

        resultA.IsSuccess.Should().BeTrue();
        resultB.IsSuccess.Should().BeTrue();

        await using var countCommandA = keepAliveA.CreateCommand();
        countCommandA.CommandText = $"SELECT COUNT(*) FROM {sharedTableName}";
        var countA = Convert.ToInt32(await countCommandA.ExecuteScalarAsync());
        countA.Should().Be(1);

        await using var countCommandB = keepAliveB.CreateCommand();
        countCommandB.CommandText = $"SELECT COUNT(*) FROM {sharedTableName}";
        var countB = Convert.ToInt32(await countCommandB.ExecuteScalarAsync());
        countB.Should().Be(1);
    }

    [Fact]
    public void TableExistsCache_WhenManyDynamicKeysAreRecorded_ShouldStayBounded()
    {
        var maxEntries = GetPrivateStaticField<int>("MaxTableExistsCacheEntries");
        var method = typeof(DatabaseWriteOperator).GetMethod("MarkTableKnown", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        for (var index = 0; index < maxEntries + 25; index++)
        {
            method!.Invoke(null, new object[] { $"SQLite|test-{Guid.NewGuid():N}|Inspection_{index}" });
        }

        GetPrivateStaticCollectionCount("TableExistsCache").Should().BeLessThanOrEqualTo(maxEntries);
    }

    [Fact]
    public void TableEnsureLock_WhenReleased_ShouldBeRemovedFromStaticCache()
    {
        var cacheKey = $"SQLite|lock-{Guid.NewGuid():N}|Inspection";
        var acquire = typeof(DatabaseWriteOperator).GetMethod("AcquireTableEnsureLock", BindingFlags.NonPublic | BindingFlags.Static);
        var release = typeof(DatabaseWriteOperator).GetMethod("ReleaseTableEnsureLock", BindingFlags.NonPublic | BindingFlags.Static);
        acquire.Should().NotBeNull();
        release.Should().NotBeNull();

        var entry = acquire!.Invoke(null, new object[] { cacheKey });

        ContainsTableEnsureLock(cacheKey).Should().BeTrue();

        release!.Invoke(null, new[] { cacheKey, entry });

        ContainsTableEnsureLock(cacheKey).Should().BeFalse();
    }

    private static Operator CreateOperator(
        string connectionString,
        string tableName = "InspectionResults",
        string dbType = "SQLite")
    {
        var op = new Operator("DatabaseWriteTest", OperatorType.DatabaseWrite, 0, 0);

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "ProfileId",
            "Database Profile",
            "Server-owned database profile id",
            "string",
            "inspection-db",
            isRequired: true));

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "ConnectionString",
            "Connection String",
            "Database connection string",
            "string",
            connectionString,
            isRequired: true));

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "TableName",
            "Table Name",
            "Target table name",
            "string",
            tableName,
            isRequired: true));

        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "DbType",
            "Database Type",
            "Supported database type",
            "enum",
            dbType,
            isRequired: true,
            options: new List<ParameterOption>
            {
                new() { Label = "SQLite", Value = "SQLite" },
                new() { Label = "SQLServer", Value = "SQLServer" },
                new() { Label = "MySQL", Value = "MySQL" }
            }));

        return op;
    }

    private static DatabaseWriteOperator CreateWithDatabaseProfile(
        string connectionString,
        string tableName,
        string dbType)
    {
        var resolver = Substitute.For<IExecutionResourceProfileResolver>();
        resolver.ResolveDatabase("inspection-db", tableName).Returns(
            ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource>.Allow(
                new ResolvedDatabaseExecutionResource(
                    "inspection-db",
                    dbType,
                    connectionString,
                    tableName)));
        return new DatabaseWriteOperator(
            Substitute.For<ILogger<DatabaseWriteOperator>>(),
            resolver);
    }

    private static T GetPrivateStaticField<T>(string name)
    {
        var field = typeof(DatabaseWriteOperator).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        return (T)field!.GetValue(null)!;
    }

    private static int GetPrivateStaticCollectionCount(string name)
    {
        var collection = GetPrivateStaticField<object>(name);
        var countProperty = collection.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();
        return (int)countProperty!.GetValue(collection)!;
    }

    private static bool ContainsTableEnsureLock(string cacheKey)
    {
        var locks = GetPrivateStaticField<object>("TableEnsureLocks");
        var containsKey = locks.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
        containsKey.Should().NotBeNull();
        return (bool)containsKey!.Invoke(locks, new object[] { cacheKey })!;
    }
}
