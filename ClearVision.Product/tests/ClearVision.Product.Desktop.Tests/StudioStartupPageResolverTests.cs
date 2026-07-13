using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StudioStartupPageResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-startup-page-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WhenLegacyIndexExists_ShouldUseLegacyIndex()
    {
        var legacyRoot = CreateLegacyRoot();

        var decision = StudioStartupPageResolver.Resolve(legacyWebRoot: legacyRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Legacy);
        decision.PagePath.Should().Be(StudioStartupPageResolver.LegacyPagePath);
        decision.RequiredFilePath.Should().Be(Path.Combine(legacyRoot, "index.html"));
        StudioStartupPageResolver.CreateInitialPageUri(5000, decision)
            .ToString()
            .Should()
            .Be("http://localhost:5000/index.html");
    }

    [Fact]
    public void Resolve_WhenLegacyIndexIsMissing_ShouldReturnWelcomeWithoutNavigation()
    {
        var legacyRoot = Path.Combine(_tempRoot, "missing-legacy-wwwroot");
        Directory.CreateDirectory(legacyRoot);

        var decision = StudioStartupPageResolver.Resolve(legacyWebRoot: legacyRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Welcome);
        decision.IsNavigable.Should().BeFalse();
        decision.PagePath.Should().BeNull();
        decision.RequiredFilePath.Should().Be(Path.Combine(legacyRoot, "index.html"));
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
}
