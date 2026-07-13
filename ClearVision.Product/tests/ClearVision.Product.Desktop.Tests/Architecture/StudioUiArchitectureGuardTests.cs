using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void StudioUiPlatform_ShouldHaveOneReviewedFetchAndWebView2Owner()
    {
        var sourceFiles = GetStudioUiProductionFiles();
        var directFetchFiles = sourceFiles
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"\b(?:globalThis\.)?fetch\s*\(",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();
        var directWebView2Files = sourceFiles
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"\bchrome\s*\?*\.\s*webview\b",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();

        directFetchFiles.Should().Equal("src/platform/api/apiTransport.ts");
        directWebView2Files.Should().Equal("src/platform/host/webView2HostAdapter.ts");
    }

    [Fact]
    public void StudioUiProductionSource_ShouldRespectPromptTwoAuthorityBoundaries()
    {
        var sourceFiles = GetStudioUiProductionFiles();
        var forbiddenTokens = new[]
        {
            "wwwroot/src",
            "legacy app.js",
            "FrontendV2",
            "workspaceV2Enabled",
            "frontendV2BasePath",
            "window.__API_BASE_URL__",
            "new FlowCanvas",
            "class FlowCanvas",
            "new ImageCanvas",
            "class ImageCanvas",
            "class EventBus",
            "createEventBus(",
            "class ServiceRegistry",
            "createServiceRegistry(",
            "ProjectSaveCoordinator",
            "new EventSource",
            "indexedDB"
        };

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            var relativePath = StudioUiRelativePath(file);
            foreach (var token in forbiddenTokens)
            {
                text.Should().NotContain(token, $"{relativePath} must stay inside the Prompt 2 boundary");
            }
        }

        var apiTransport = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "platform", "api", "apiTransport.ts"));
        apiTransport.Should().Contain("method: 'GET'");
        apiTransport.Should().NotContain("method: 'POST'");
        apiTransport.Should().NotContain("method: 'PUT'");
        apiTransport.Should().NotContain("method: 'PATCH'");
        apiTransport.Should().NotContain("method: 'DELETE'");
        apiTransport.Should().NotContain("EventSource");
        apiTransport.Should().NotContain("localStorage");

        var startupSource = Directory.EnumerateFiles(
                Path.Combine(RepoPath(StudioUiRoot), "src", "platform", "startup"),
                "*.ts",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();
        startupSource.Should().OnlyContain(text =>
            !text.Contains("localStorage", StringComparison.Ordinal) &&
            !text.Contains("sessionStorage", StringComparison.Ordinal) &&
            !text.Contains("location.search", StringComparison.Ordinal) &&
            !text.Contains("__API_BASE_URL__", StringComparison.Ordinal));

        var abortControllerOwners = sourceFiles
            .Where(file => File.ReadAllText(file).Contains("new AbortController", StringComparison.Ordinal))
            .Select(StudioUiRelativePath)
            .ToList();
        abortControllerOwners.Should().Equal("src/platform/diagnostics/runtimeDiagnostics.ts");

        var piniaStores = sourceFiles
            .Where(file => File.ReadAllText(file).Contains("defineStore(", StringComparison.Ordinal))
            .ToList();
        foreach (var store in piniaStores)
        {
            var text = File.ReadAllText(store);
            text.Should().NotContain("AbortController");
            text.Should().NotContain("EventSource");
            text.Should().NotContain("FlowCanvas");
            text.Should().NotContain("ImageCanvas");
            text.Should().NotContain("StudioHostAdapter");
            text.Should().NotContain("ApiTransport");
        }
    }

    [Fact]
    public void DesktopStartupDefaultsAndDpiAuthority_ShouldRemainExplicit()
    {
        var settingsPath = RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        settings.RootElement
            .GetProperty("Studio")
            .GetProperty("StudioUiEnabled")
            .GetBoolean()
            .Should()
            .BeFalse();

        var options = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/Configuration/StudioOptions.cs"));
        options.Should().Contain("StudioUiEnabled { get; set; } = false");

        var host = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs"));
        var studioV1Builder = SliceBetween(
            host,
            "BuildStudioUiStartupInjectionScript",
            "CreateStartupPlan");
        studioV1Builder.Should().Contain("schemaVersion = 1");
        studioV1Builder.Should().Contain("uiKind = \"studio-ui\"");
        studioV1Builder.Should().NotContain("window.__API_BASE_URL__");
        studioV1Builder.Should().NotContain("window.__CSS_VERSION__");

        var project = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj"));
        project.Should().Contain("<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>");

        var program = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/Program.cs"));
        program.Should().NotContain("SetHighDpiMode");

        var manifest = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/app.manifest"));
        manifest.Should().NotContain("dpiAware");
        manifest.Should().NotContain("dpiAwareness");
    }

    [Fact]
    public void StudioUiRouter_ShouldContainOnlyTheReservedF01TechnicalRoutes()
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

    private static IReadOnlyList<string> GetStudioUiProductionFiles()
    {
        var sourceRoot = Path.Combine(RepoPath(StudioUiRoot), "src");
        return Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string StudioUiRelativePath(string path) =>
        Path.GetRelativePath(RepoPath(StudioUiRoot), path).Replace('\\', '/');

    private static string SliceBetween(string text, string startToken, string endToken)
    {
        var start = text.IndexOf(startToken, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = text.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return text[start..end];
    }

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
