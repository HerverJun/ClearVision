using System.Runtime.CompilerServices;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests.Architecture;

public sealed class StudioUiArchitectureGuardTests
{
    private static readonly string Root = FindRepositoryRoot();
    private const string StudioUiRoot =
        "ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI";

    [Fact]
    public void StudioUiBuildFoundation_ShouldUseTheApprovedSourceAndAssetBoundaries()
    {
        var studioUiRoot = RepoPath(StudioUiRoot);
        Directory.Exists(studioUiRoot).Should().BeTrue();
        File.Exists(Path.Combine(studioUiRoot, "package-lock.json")).Should().BeTrue();
        Directory.Exists(RepoPath("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/studio"))
            .Should().BeFalse("generated StudioUI assets must never be written into source wwwroot");

        var vite = File.ReadAllText(Path.Combine(studioUiRoot, "vite.config.ts"));
        vite.Should().Contain("const studioUiBasePath = '/studio/'");
        vite.Should().Contain("manifest: true");
        vite.Should().Contain("sourcemap: false");
        vite.Should().Contain("process.env.VITE_OUT_DIR");
        vite.Should().Contain("'StudioUI', 'dist'");

        var project = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj"));
        project.Should().Contain("<StudioUiRoot>");
        project.Should().Contain("<StudioUiIntermediateDist>");
        project.Should().Contain("<SkipStudioUiBuild");
        project.Should().Contain("<SkipStudioUiInstall");
        project.Should().Contain("Name=\"BuildStudioUi\"");
        project.Should().Contain("Name=\"CopyStudioUiAssetsToOutput\"");
        project.Should().Contain("Name=\"CopyStudioUiAssetsToPublish\"");
        project.Should().Contain("Name=\"CleanStudioUiAssets\"");
    }

    [Fact]
    public void StudioUiProductionSource_ShouldNotCreateBusinessOrImperativeInfrastructure()
    {
        var sourceRoot = Path.Combine(RepoPath(StudioUiRoot), "src");
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
            .ToList();

        sourceFiles.Should().NotBeEmpty();
        var forbiddenTokens = new[]
        {
            "wwwroot/src",
            "legacy app.js",
            "window.chrome.webview",
            "chrome.webview",
            "fetch(",
            "new FlowCanvas",
            "class FlowCanvas",
            "new ImageCanvas",
            "class ImageCanvas",
            "class EventBus",
            "createEventBus(",
            "class ServiceRegistry",
            "createServiceRegistry(",
            "ProjectSaveCoordinator",
            "defineStore(",
            "new EventSource",
            "new AbortController",
            "localStorage",
            "indexedDB"
        };

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(Root, file);
            foreach (var token in forbiddenTokens)
            {
                text.Should().NotContain(token, $"{relativePath} must stay inside the Prompt 1 boundary");
            }
        }
    }

    [Fact]
    public void StudioUiRouter_ShouldContainOnlyThePromptOneSkeleton()
    {
        var router = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "src", "app", "router.ts"));
        router.Should().Contain("createWebHashHistory(import.meta.env.BASE_URL)");
        router.Should().Contain("path: '/diagnostics'");
        router.Should().Contain("path: '/labs/design'");
        router.Should().Contain("path: '/labs/canvas'");
        router.Should().NotContain("/projects");
        router.Should().NotContain("/inspection");
        router.Should().NotContain("/results");
        router.Should().NotContain("/settings");
        router.Should().NotContain("/ai");
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
