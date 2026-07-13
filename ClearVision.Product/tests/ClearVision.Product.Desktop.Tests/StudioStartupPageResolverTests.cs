using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StudioStartupPageResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-startup-page-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WhenStudioUiIsDisabledAndLegacyIndexExists_ShouldUseLegacyIndex()
    {
        var legacyRoot = CreateLegacyRoot();

        var decision = StudioStartupPageResolver.Resolve(
            studioUiEnabled: false,
            legacyWebRoot: legacyRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Legacy);
        decision.PagePath.Should().Be(StudioStartupPageResolver.LegacyPagePath);
        decision.RequiredFilePath.Should().Be(Path.Combine(legacyRoot, "index.html"));
        decision.MissingAssetPaths.Should().BeEmpty();
        StudioStartupPageResolver.CreateInitialPageUri(5000, decision)
            .ToString()
            .Should()
            .Be("http://localhost:5000/index.html");
    }

    [Fact]
    public void Resolve_WhenStudioUiIsEnabledAndAssetsAreComplete_ShouldUseStudioUiIndex()
    {
        var studioUiRoot = CreateStudioUiRoot();

        var decision = StudioStartupPageResolver.Resolve(
            studioUiEnabled: true,
            studioUiWebRoot: studioUiRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.StudioUi);
        decision.PagePath.Should().Be(StudioStartupPageResolver.StudioUiPagePath);
        decision.RequiredFilePath.Should().Be(Path.Combine(studioUiRoot, "index.html"));
        decision.MissingAssetPaths.Should().BeEmpty();
        StudioStartupPageResolver.CreateInitialPageUri(5010, decision)
            .ToString()
            .Should()
            .Be("http://localhost:5010/studio/index.html");
    }

    [Fact]
    public void Resolve_WhenStudioUiRequiredAssetsAreMissing_ShouldFailClosedAndListEveryPath()
    {
        var legacyRoot = CreateLegacyRoot();
        var studioUiRoot = Path.Combine(_tempRoot, "incomplete-studio");
        Directory.CreateDirectory(studioUiRoot);
        var expectedMissingPaths = new[]
        {
            Path.Combine(studioUiRoot, "index.html"),
            Path.Combine(studioUiRoot, "assets"),
            Path.Combine(studioUiRoot, ".vite", "manifest.json")
        };

        var decision = StudioStartupPageResolver.Resolve(
            studioUiEnabled: true,
            legacyWebRoot: legacyRoot,
            studioUiWebRoot: studioUiRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Diagnostic);
        decision.IsNavigable.Should().BeFalse();
        decision.PagePath.Should().BeNull();
        decision.MissingAssetPaths.Should().Equal(expectedMissingPaths);
        decision.DiagnosticMessage.Should().Contain("不会回退 Legacy");
        foreach (var missingPath in expectedMissingPaths)
        {
            decision.DiagnosticMessage.Should().Contain(missingPath);
        }
    }

    [Fact]
    public void Resolve_WhenStudioUiAssetsDirectoryIsEmpty_ShouldTreatDirectoryAsInvalid()
    {
        var studioUiRoot = CreateStudioUiRoot(includeAssetFile: false);
        var assetsPath = Path.Combine(studioUiRoot, "assets");

        var decision = StudioStartupPageResolver.Resolve(
            studioUiEnabled: true,
            studioUiWebRoot: studioUiRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Diagnostic);
        decision.MissingAssetPaths.Should().Equal(assetsPath);
        decision.DiagnosticMessage.Should().Contain(assetsPath);
    }

    [Fact]
    public void Resolve_WhenLegacyIndexIsMissing_ShouldKeepWelcomeSemantics()
    {
        var legacyRoot = Path.Combine(_tempRoot, "missing-legacy-wwwroot");
        Directory.CreateDirectory(legacyRoot);

        var decision = StudioStartupPageResolver.Resolve(
            studioUiEnabled: false,
            legacyWebRoot: legacyRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Welcome);
        decision.IsNavigable.Should().BeFalse();
        decision.PagePath.Should().BeNull();
        decision.RequiredFilePath.Should().Be(Path.Combine(legacyRoot, "index.html"));
        decision.MissingAssetPaths.Should().Equal(Path.Combine(legacyRoot, "index.html"));
        decision.DiagnosticMessage.Should().Contain("未找到旧前端入口文件");
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

    private string CreateStudioUiRoot(bool includeAssetFile = true)
    {
        var root = Path.Combine(_tempRoot, "studio-wwwroot");
        var assetsRoot = Path.Combine(root, "assets");
        var manifestRoot = Path.Combine(root, ".vite");
        Directory.CreateDirectory(assetsRoot);
        Directory.CreateDirectory(manifestRoot);
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html>studio");
        File.WriteAllText(Path.Combine(manifestRoot, "manifest.json"), "{}");
        if (includeAssetFile)
        {
            File.WriteAllText(Path.Combine(assetsRoot, "app-12345678.js"), "asset");
        }

        return root;
    }
}
