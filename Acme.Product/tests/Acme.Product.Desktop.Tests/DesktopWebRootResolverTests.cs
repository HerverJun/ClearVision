using Acme.Product.Desktop;
using FluentAssertions;

namespace Acme.Product.Desktop.Tests;

public sealed class DesktopWebRootResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-webroot-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WithRidOutputUnderProject_ShouldPreferProjectWwwRoot()
    {
        var projectRoot = CreateDesktopProject();
        var baseDirectory = Path.Combine(projectRoot, "bin", "Debug", "net8.0-windows", "win-x64");
        CreateOutputWwwRoot(baseDirectory);

        var resolved = DesktopWebRootResolver.Resolve(baseDirectory, preferProjectSource: true);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(projectRoot, "wwwroot")));
    }

    [Fact]
    public void Resolve_WithNonRidOutputUnderProject_ShouldPreferProjectWwwRoot()
    {
        var projectRoot = CreateDesktopProject();
        var baseDirectory = Path.Combine(projectRoot, "bin", "Debug", "net8.0-windows");
        CreateOutputWwwRoot(baseDirectory);

        var resolved = DesktopWebRootResolver.Resolve(baseDirectory, preferProjectSource: true);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(projectRoot, "wwwroot")));
    }

    [Fact]
    public void Resolve_WhenProjectSourceIsNotPreferred_ShouldUseOutputWwwRoot()
    {
        var projectRoot = CreateDesktopProject();
        var baseDirectory = Path.Combine(projectRoot, "bin", "Release", "net8.0-windows", "win-x64");
        CreateOutputWwwRoot(baseDirectory);

        var resolved = DesktopWebRootResolver.Resolve(baseDirectory, preferProjectSource: false);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(baseDirectory, "wwwroot")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateDesktopProject()
    {
        var projectRoot = Path.Combine(_tempRoot, "Acme.Product", "src", "Acme.Product.Desktop");
        var wwwRoot = Path.Combine(projectRoot, "wwwroot");
        Directory.CreateDirectory(wwwRoot);
        File.WriteAllText(Path.Combine(projectRoot, "Acme.Product.Desktop.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(wwwRoot, "index.html"), "<!doctype html>");
        return projectRoot;
    }

    private static void CreateOutputWwwRoot(string baseDirectory)
    {
        var outputWwwRoot = Path.Combine(baseDirectory, "wwwroot");
        Directory.CreateDirectory(outputWwwRoot);
        File.WriteAllText(Path.Combine(outputWwwRoot, "index.html"), "<!doctype html>");
    }
}
