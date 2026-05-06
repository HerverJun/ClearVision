using System.IO.Compression;
using Acme.Product.Desktop.Station;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Runtime;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Acme.Product.Desktop.Tests;

public sealed class StationPackageStoreTests
{
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

                store.GetPackages().Should().ContainSingle(item => item.PackageId == manifest.PackageId);
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
