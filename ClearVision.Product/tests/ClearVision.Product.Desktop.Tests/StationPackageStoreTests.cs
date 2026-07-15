using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationPackageStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Fact]
    public async Task CreateTestPackageAsync_ShouldCreateDeployableRuntimePackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationPackageStoreTests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "vision.db");
        Directory.CreateDirectory(root);

        try
        {
            await using (var provider = new ServiceCollection()
                .AddLogging()
                .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
                .AddSingleton<StationPackageStore>()
                .AddSingleton<RuntimePackageValidator>()
                .AddSingleton<RuntimePackageLoader>()
                .BuildServiceProvider())
            {
                await using (var scope = provider.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
                }

                var store = provider.GetRequiredService<StationPackageStore>();
                var manifest = await store.CreateTestPackageAsync(CancellationToken.None);
                var packagePath = store.GetPackagePath(manifest.PackageId);

                manifest.PackageKind.Should().Be(StationPackageKind.Test);
                packagePath.Should().NotBeNullOrWhiteSpace();
                File.Exists(packagePath).Should().BeTrue();

                var extractRoot = Path.Combine(root, "extract");
                ZipFile.ExtractToDirectory(packagePath!, extractRoot);
                File.Exists(Path.Combine(extractRoot, "manifest.json")).Should().BeTrue();

                var runtimeRoot = Path.Combine(extractRoot, "package");
                File.Exists(Path.Combine(runtimeRoot, "package.json")).Should().BeTrue();
                File.Exists(Path.Combine(runtimeRoot, "flow.json")).Should().BeTrue();
                File.Exists(Path.Combine(runtimeRoot, "runtime-profile.json")).Should().BeTrue();
                File.Exists(Path.Combine(runtimeRoot, "quality", "validation-report.json")).Should().BeTrue();

                var loaded = await provider.GetRequiredService<RuntimePackageLoader>().LoadAsync(runtimeRoot);
                loaded.Manifest.PackageId.Should().Be(manifest.PackageId);
                loaded.Flow.Operators.Should().ContainSingle();
                loaded.ValidationReport.IsValid.Should().BeTrue();

                store.GetPackages().Should().ContainSingle(item =>
                    item.PackageId == manifest.PackageId &&
                    item.PackageKind == StationPackageKind.Test);
                store.GetProductionPackages().Should().BeEmpty();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    [Fact]
    public async Task ImportRuntimePackageAsync_ShouldCreateStationDeployablePackageRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationPackageImportTests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "vision.db");
        Directory.CreateDirectory(root);

        try
        {
            await using (var provider = new ServiceCollection()
                .AddLogging()
                .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
                .AddSingleton<StationPackageStore>()
                .AddSingleton<RuntimePackageValidator>()
                .AddSingleton<RuntimePackageLoader>()
                .BuildServiceProvider())
            {
                await using (var scope = provider.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
                }

                var runtimeRoot = Path.Combine(root, "runtime-package");
                await CreateRuntimePackageRootAsync(runtimeRoot, "cvpkg-import-1");

                var store = provider.GetRequiredService<StationPackageStore>();
                var manifest = await store.ImportRuntimePackageAsync(runtimeRoot, "unit-test", CancellationToken.None);
                var packagePath = store.GetPackagePath(manifest.PackageId);

                manifest.PackageId.Should().Be("cvpkg-import-1");
                manifest.PackageKind.Should().Be(StationPackageKind.Production);
                manifest.CreatedBy.Should().Be("unit-test");
                packagePath.Should().NotBeNullOrWhiteSpace();
                File.Exists(packagePath).Should().BeTrue();

                var extractRoot = Path.Combine(root, "extract-imported");
                ZipFile.ExtractToDirectory(packagePath!, extractRoot);
                File.Exists(Path.Combine(extractRoot, "manifest.json")).Should().BeTrue();

                var runtimePackageRoot = Path.Combine(extractRoot, "package");
                var loaded = await provider.GetRequiredService<RuntimePackageLoader>().LoadAsync(runtimePackageRoot);
                loaded.Manifest.PackageId.Should().Be("cvpkg-import-1");
                loaded.ValidationReport.IsValid.Should().BeTrue();

                store.GetPackages().Should().Contain(item =>
                    item.PackageId == manifest.PackageId &&
                    item.PackageKind == StationPackageKind.Production);
                store.GetProductionPackages().Should().ContainSingle(item => item.PackageId == manifest.PackageId);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    [Fact]
    public async Task GetPackages_ShouldReadLegacyPackageKindColumn_AfterDatabaseMaintenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationPackageLegacyKindTests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "vision.db");
        Directory.CreateDirectory(root);

        try
        {
            await CreateLegacyPackageRecordDatabaseAsync(dbPath);
            await using (var provider = new ServiceCollection()
                .AddLogging()
                .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
                .AddSingleton<StationPackageStore>()
                .BuildServiceProvider())
            {
                await using (var scope = provider.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
                    await VisionDatabaseMaintenance.ApplyPostMigrationMaintenanceAsync(db, CancellationToken.None);
                }

                var store = provider.GetRequiredService<StationPackageStore>();

                var packages = store.GetPackages();

                packages.Should().ContainSingle(item =>
                    item.PackageId == "legacy-package" &&
                    item.PackageKind == StationPackageKind.Production);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static async Task CreateRuntimePackageRootAsync(string runtimeRoot, string packageId)
    {
        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "quality"));
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "field"));

        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "Import test runtime flow",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "Result",
                    Type = OperatorType.ResultOutput,
                    X = 0,
                    Y = 0,
                    InputPorts = [],
                    OutputPorts = [],
                    Parameters = []
                }
            ],
            Connections = []
        }.WithStringDecisionBinding();
        var flowBytes = JsonSerializer.SerializeToUtf8Bytes(flow, JsonOptions);
        var flowEntity = flow.ToEntity();
        var flowHash = ExecutionFlowIdentity.ComputeFlowHash(flowEntity);
        var manifest = new RuntimePackageManifest
        {
            PackageId = packageId,
            PackageName = "Import Test Package",
            RuntimeApiVersion = "1.0",
            MinStationVersion = "0.1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "ClearVision Studio",
            EntryFlow = "flow.json",
            FlowHash = flowHash,
            DecisionConfigurationHash = ExecutionFlowIdentity.ComputeDecisionConfigurationHash(
                flowEntity.DecisionConfiguration),
            OperatorCatalogVersion = "unit-test",
            ExportAllowed = true,
            FieldExtensions = new RuntimeFieldExtensions
            {
                RuntimeParameters = "field/runtime-parameters.json",
                DefaultSiteProfile = "field/station-profile.default.json"
            }
        };
        var validationReport = new RuntimeValidationReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            IsValid = true,
            FlowHash = flowHash
        };

        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "flow.json"), flowBytes);
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "package.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "runtime-profile.json"), JsonSerializer.Serialize(new RuntimeProfile(), JsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(runtimeRoot, "quality", "validation-report.json"),
            JsonSerializer.Serialize(validationReport, JsonOptions));
    }

    private static async Task CreateLegacyPackageRecordDatabaseAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = """
            CREATE TABLE "StationPackageRecords" (
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
            INSERT INTO "StationPackageRecords"
                ("PackageId", "PackageName", "PackageVersion", "FlowHash", "FileName", "FilePath", "SizeBytes", "Sha256", "CreatedBy", "CreatedAtUtc")
            VALUES
                ('legacy-package', 'Legacy Package', '1.0.0', 'sha256:legacy', 'legacy.cvpkg', 'legacy.cvpkg', 10, 'legacy', 'Studio', '2026-01-01T00:00:00Z');
            """;
        await createCommand.ExecuteNonQueryAsync();
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(100);
            }
        }

        throw lastError ?? new IOException($"Failed to delete temporary directory: {path}");
    }
}
