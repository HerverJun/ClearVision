using ClearVision.Product.Desktop.Configuration;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StudioStartupPageResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-startup-page-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WhenFlagOff_ShouldUseLegacyIndexAndIgnoreMissingV2()
    {
        var legacyRoot = CreateLegacyRoot();
        var missingV2Root = Path.Combine(_tempRoot, "missing-v2");

        var decision = StudioStartupPageResolver.Resolve(
            new StudioOptions { WorkspaceV2Enabled = false },
            legacyWebRoot: legacyRoot,
            frontendV2WebRoot: missingV2Root);

        decision.Kind.Should().Be(StudioStartupPageKind.Legacy);
        decision.PagePath.Should().Be(StudioStartupPageResolver.LegacyPagePath);
        decision.WorkspaceV2Enabled.Should().BeFalse();
        decision.RequiredFilePath.Should().Be(Path.Combine(legacyRoot, "index.html"));
        StudioStartupPageResolver.CreateInitialPageUri(5000, decision)
            .ToString()
            .Should()
            .Be("http://localhost:5000/index.html");
    }

    [Fact]
    public void Resolve_WhenFlagOnAndAssetsExist_ShouldUseV2Index()
    {
        var legacyRoot = CreateLegacyRoot();
        var v2Root = CreateV2Root(withAssets: true);

        var decision = StudioStartupPageResolver.Resolve(
            new StudioOptions { WorkspaceV2Enabled = true },
            legacyWebRoot: legacyRoot,
            frontendV2WebRoot: v2Root);

        decision.Kind.Should().Be(StudioStartupPageKind.FrontendV2);
        decision.PagePath.Should().Be(StudioStartupPageResolver.FrontendV2PagePath);
        decision.WorkspaceV2Enabled.Should().BeTrue();
        StudioStartupPageResolver.CreateInitialPageUri(5000, decision)
            .ToString()
            .Should()
            .Be("http://localhost:5000/v2/index.html");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_WhenFlagOnAndV2AssetsMissing_ShouldFailClosedWithoutLegacyFallback(bool includeIndex)
    {
        var legacyRoot = CreateLegacyRoot();
        var v2Root = includeIndex
            ? CreateV2Root(withAssets: false)
            : Path.Combine(_tempRoot, "v2-missing-index");
        Directory.CreateDirectory(v2Root);

        var decision = StudioStartupPageResolver.Resolve(
            new StudioOptions { WorkspaceV2Enabled = true },
            legacyWebRoot: legacyRoot,
            frontendV2WebRoot: v2Root);

        decision.Kind.Should().Be(StudioStartupPageKind.Diagnostic);
        decision.IsNavigable.Should().BeFalse();
        decision.PagePath.Should().BeNull();
        decision.DiagnosticMessage.Should().Contain("禁止回退旧页面");
        decision.DiagnosticMessage.Should().Contain("wwwroot/v2");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateLegacyRoot()
    {
        var root = Path.Combine(_tempRoot, "legacy-wwwroot");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html>legacy");
        return root;
    }

    private string CreateV2Root(bool withAssets)
    {
        var root = Path.Combine(_tempRoot, withAssets ? "v2-ready" : "v2-no-assets");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html>v2");
        if (withAssets)
        {
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(assets);
            File.WriteAllText(Path.Combine(assets, "index.js"), "console.log('v2');");
        }

        return root;
    }
}
