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

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
