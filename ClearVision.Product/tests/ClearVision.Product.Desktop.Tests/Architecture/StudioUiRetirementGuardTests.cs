using System.Runtime.CompilerServices;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests.Architecture;

public sealed class StudioUiRetirementGuardTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void RetiredFrontendSourceAndDedicatedPlaywrightScenario_ShouldBeAbsent()
    {
        Directory.Exists(RepoPath("ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2"))
            .Should().BeFalse();
        Directory.Exists(RepoPath("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/v2"))
            .Should().BeFalse();
        File.Exists(RepoPath("ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio2-flow-editor-port.spec.ts"))
            .Should().BeFalse();
    }

    [Fact]
    public void ActiveDesktopBuildHostConfigurationAndCi_ShouldNotReferenceRetiredFrontend()
    {
        var activeFiles = new[]
        {
            ".github/workflows/ci.yml",
            ".gitignore",
            "CLAUDE.md",
            "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Configuration/StudioOptions.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/DesktopWebRootResolver.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Program.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/StudioStartupPageResolver.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json"
        };

        var forbiddenTokens = new[]
        {
            "FrontendV2",
            "WorkspaceV2Enabled",
            "SkipFrontendV2",
            "frontendV2BasePath",
            "/v2/index.html",
            "wwwroot/v2",
            @"wwwroot\v2"
        };

        foreach (var relativePath in activeFiles)
        {
            var text = File.ReadAllText(RepoPath(relativePath));
            foreach (var token in forbiddenTokens)
            {
                text.Should().NotContain(token, $"{relativePath} must not retain retired frontend infrastructure");
            }
        }
    }

    private static string RepoPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var startPath in new[]
                 {
                     Path.GetDirectoryName(sourceFile),
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                continue;
            }

            var directory = new DirectoryInfo(startPath);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ClearVision.Product", "ClearVision.Product.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
