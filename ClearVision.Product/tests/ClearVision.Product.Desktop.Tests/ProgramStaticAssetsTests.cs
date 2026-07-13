using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Desktop.Tests;

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
        Program.UseDesktopStaticAssets(app, legacyRoot);
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
        var nestedAsset = Path.Combine(legacyRoot, "src", "app.js");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedAsset)!);
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy-index");
        File.WriteAllText(nestedAsset, "legacy-app");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(app, legacyRoot);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            var legacyIndex = await client.GetStringAsync("/index.html");
            var legacyApp = await client.GetStringAsync("/src/app.js");

            legacyIndex.Should().Be("legacy-index");
            legacyApp.Should().Be("legacy-app");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

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
