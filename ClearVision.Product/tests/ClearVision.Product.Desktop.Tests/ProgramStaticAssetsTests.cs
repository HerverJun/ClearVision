using ClearVision.Product.Desktop.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class ProgramStaticAssetsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-static-assets-tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("index.html")]
    [InlineData("src/features/flow-editor/propertyPanel.js")]
    [InlineData("src/features/flow-editor/propertyPanelCapabilityOwner.mjs")]
    [InlineData("src/shared/styles/property-panel-enhancements.css")]
    public async Task UseDesktopStaticAssets_ShouldDisableCachingForDesktopFrontendAssets(string relativePath)
    {
        var legacyRoot = Path.Combine(_tempRoot, "cache-wwwroot");
        var targetPath = Path.Combine(legacyRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "asset");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(
            app,
            Profile(StudioStartupProfileCatalog.LegacyFallback),
            legacyRoot,
            Path.Combine(_tempRoot, "missing-studio-root"));
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            using var response = await client.GetAsync($"/{relativePath}");

            response.EnsureSuccessStatusCode();
            GetHeader(response, "Cache-Control").Should().Contain("no-store");
            GetHeader(response, "Cache-Control").Should().Contain("no-cache");
            GetHeader(response, "Cache-Control").Should().Contain("max-age=0");
            GetHeader(response, "Pragma").Should().Contain("no-cache");
            GetHeader(response, "Expires").Should().Be("0");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseDesktopStaticAssets_ShouldServeLegacyIndexAndNestedAssetsFromLegacyRoot()
    {
        var legacyRoot = Path.Combine(_tempRoot, "legacy-wwwroot");
        var studioUiRoot = Path.Combine(_tempRoot, "studio-disabled-root");
        var nestedAsset = Path.Combine(legacyRoot, "src", "app.js");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedAsset)!);
        Directory.CreateDirectory(studioUiRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy-index");
        File.WriteAllText(nestedAsset, "legacy-app");
        File.WriteAllText(Path.Combine(studioUiRoot, "index.html"), "studio-index");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(
            app,
            Profile(StudioStartupProfileCatalog.LegacyFallback),
            legacyRoot,
            studioUiRoot);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            var legacyIndex = await client.GetStringAsync("/index.html");
            var legacyApp = await client.GetStringAsync("/src/app.js");
            using var studioIndex = await client.GetAsync("/studio/index.html");

            legacyIndex.Should().Be("legacy-index");
            legacyApp.Should().Be("legacy-app");
            studioIndex.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData(".vite/manifest.json")]
    public async Task UseDesktopStaticAssets_ShouldDisableCachingForStudioEntryAndManifest(
        string relativePath)
    {
        var legacyRoot = Path.Combine(_tempRoot, "legacy-cache-root");
        var studioUiRoot = Path.Combine(_tempRoot, "studio-cache-root");
        Directory.CreateDirectory(legacyRoot);
        var targetPath = Path.Combine(
            studioUiRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "studio-asset");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(
            app,
            Profile(StudioStartupProfileCatalog.NextDefault),
            legacyRoot,
            studioUiRoot);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            using var response = await client.GetAsync($"/studio/{relativePath}");

            response.EnsureSuccessStatusCode();
            GetHeader(response, "Cache-Control").Should().Contain("no-store");
            GetHeader(response, "Cache-Control").Should().Contain("no-cache");
            GetHeader(response, "Cache-Control").Should().Contain("max-age=0");
            GetHeader(response, "Pragma").Should().Contain("no-cache");
            GetHeader(response, "Expires").Should().Be("0");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseDesktopStaticAssets_ShouldCacheHashedStudioAssetsAsImmutable()
    {
        var legacyRoot = Path.Combine(_tempRoot, "legacy-immutable-root");
        var studioUiRoot = Path.Combine(_tempRoot, "studio-immutable-root");
        var hashedAsset = Path.Combine(studioUiRoot, "assets", "app-Clear1234.js");
        Directory.CreateDirectory(legacyRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(hashedAsset)!);
        File.WriteAllText(hashedAsset, "studio-hash");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(
            app,
            Profile(StudioStartupProfileCatalog.NextDefault),
            legacyRoot,
            studioUiRoot);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            using var response = await client.GetAsync("/studio/assets/app-Clear1234.js");

            response.EnsureSuccessStatusCode();
            GetHeader(response, "Cache-Control").Should().Contain("public");
            GetHeader(response, "Cache-Control").Should().Contain("max-age=31536000");
            GetHeader(response, "Cache-Control").Should().Contain("immutable");
            GetHeader(response, "Cache-Control").Should().NotContain("no-store");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseDesktopStaticAssets_NextProfile_ShouldMountOnlyStudioProvider()
    {
        var legacyRoot = Path.Combine(_tempRoot, "legacy-scope-root");
        var studioUiRoot = Path.Combine(_tempRoot, "studio-scope-root");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "studio"));
        Directory.CreateDirectory(Path.Combine(legacyRoot, "src"));
        Directory.CreateDirectory(Path.Combine(studioUiRoot, "assets"));
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy-index");
        File.WriteAllText(Path.Combine(legacyRoot, "src", "app.js"), "legacy-app");
        File.WriteAllText(Path.Combine(legacyRoot, "studio", "index.html"), "legacy-shadow");
        File.WriteAllText(Path.Combine(studioUiRoot, "index.html"), "studio-index");
        File.WriteAllText(Path.Combine(studioUiRoot, "assets", "app-Clear1234.js"), "studio-asset");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(
            app,
            Profile(StudioStartupProfileCatalog.NextDefault),
            legacyRoot,
            studioUiRoot);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            (await client.GetStringAsync("/studio/index.html")).Should().Be("studio-index");
            using var legacyIndex = await client.GetAsync("/index.html");
            using var legacyApp = await client.GetAsync("/src/app.js");
            using var unscopedStudioAsset = await client.GetAsync("/assets/app-Clear1234.js");
            legacyIndex.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
            legacyApp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
            unscopedStudioAsset.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseDesktopStaticAssets_NextProfileWithMissingAssets_ShouldNotFallBackToLegacy()
    {
        var legacyRoot = Path.Combine(_tempRoot, "legacy-fail-closed-root");
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy-index");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(
            app,
            Profile(StudioStartupProfileCatalog.NextDefault),
            legacyRoot,
            Path.Combine(_tempRoot, "missing-next-root"));
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            using var legacyIndex = await client.GetAsync("/index.html");

            legacyIndex.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static StudioOptions Profile(string startupProfile) => new()
    {
        StartupProfile = startupProfile
    };

    private static string GetHeader(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return string.Join(", ", values);
        }

        return response.Content.Headers.TryGetValues(headerName, out var contentValues)
            ? string.Join(", ", contentValues)
            : string.Empty;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
