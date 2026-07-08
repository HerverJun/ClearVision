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
        Program.UseDesktopStaticAssets(app, legacyRoot, frontendV2WebRootPath: Path.Combine(_tempRoot, "missing-v2"));
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
    public async Task UseDesktopStaticAssets_ShouldServeLegacyFromLegacyRootAndV2FromOutputRoot()
    {
        var legacyRoot = Path.Combine(_tempRoot, "legacy-wwwroot");
        var legacyV2Root = Path.Combine(legacyRoot, "v2");
        var outputV2Root = Path.Combine(_tempRoot, "output", "wwwroot", "v2");
        Directory.CreateDirectory(legacyV2Root);
        Directory.CreateDirectory(outputV2Root);
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy-index");
        File.WriteAllText(Path.Combine(legacyV2Root, "index.html"), "legacy-v2-index");
        File.WriteAllText(Path.Combine(outputV2Root, "index.html"), "output-v2-index");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        Program.UseDesktopStaticAssets(app, legacyRoot, outputV2Root);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            var legacyIndex = await client.GetStringAsync("/index.html");
            var v2Index = await client.GetStringAsync("/v2/index.html");

            legacyIndex.Should().Be("legacy-index");
            v2Index.Should().Be("output-v2-index");
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
