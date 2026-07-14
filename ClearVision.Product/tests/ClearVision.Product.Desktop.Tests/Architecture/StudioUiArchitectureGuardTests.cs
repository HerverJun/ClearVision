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
        project.Should().Contain("<StudioUiIntermediateDist Condition=");
        project.Should().Contain("System.IO.Path]::IsPathRooted('$(BaseIntermediateOutputPath)')");
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

            text.Should().NotMatchRegex(
                @"\bnew\s+FlowCanvas\s*\(",
                $"{relativePath} must not construct a second FlowCanvas directly");
            text.Should().NotMatchRegex(
                @"\bclass\s+FlowCanvas\b",
                $"{relativePath} must not define a second FlowCanvas kernel");
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
    public void StudioUiCanvasLab_ShouldUseOnlyTheApprovedCanonicalCanvasFacade()
    {
        var sourceFiles = GetStudioUiProductionFiles();
        var canonicalCanvasImports = sourceFiles
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                "\\bfrom\\s+['\"]@clearvision/canonical-flow-canvas['\"]",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();
        var canonicalInteractionImports = sourceFiles
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                "\\bfrom\\s+['\"]@clearvision/canonical-flow-interaction['\"]",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();

        canonicalCanvasImports.Should().Equal("src/labs/canvas/canonicalFlowCanvas.ts");
        canonicalInteractionImports.Should().Equal("src/labs/canvas/canonicalFlowCanvas.ts");

        var canvasIntegration = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "labs", "canvas", "canonicalFlowCanvas.ts"));
        canvasIntegration.Should().Contain("createHostedFlowCanvasAdapter");
        canvasIntegration.Should().Contain("FlowEditorInteraction");
        canvasIntegration.Should().NotContain("new FlowCanvas");
        canvasIntegration.Should().NotContain("class FlowCanvas");

        var canvasOwner = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "labs", "canvas", "canvasLabOwner.ts"));
        canvasOwner.Should().Contain("reportCanvasOwnerCountForDiagnostics(1)");
        canvasOwner.Should().Contain("reportCanvasOwnerCountForDiagnostics(0)");

        var vite = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "vite.config.ts"));
        vite.Should().Contain("'@clearvision/canonical-flow-canvas': canonicalFlowCanvasAdapter");
        vite.Should().Contain("'@clearvision/canonical-flow-interaction': canonicalFlowEditorInteraction");
        vite.Should().Contain("'flowCanvasAdapter.js'");
        vite.Should().Contain("'flowEditorInteraction.js'");

        var vitest = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "vitest.config.ts"));
        vitest.Should().Contain("'@clearvision/canonical-flow-canvas'");
        vitest.Should().Contain("'@clearvision/canonical-flow-interaction'");
        vitest.Should().Contain("'flowCanvasAdapter.js'");
        vitest.Should().Contain("'flowEditorInteraction.js'");

        var duplicateCanvasImplementations = Directory.EnumerateFiles(
                Path.Combine(RepoPath(StudioUiRoot), "src"),
                "*.*",
                SearchOption.AllDirectories)
            .Where(path =>
                Path.GetFileName(path).Equals("flowCanvas.js", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Equals("flowCanvasAdapter.js", StringComparison.OrdinalIgnoreCase))
            .Select(StudioUiRelativePath)
            .ToList();
        duplicateCanvasImplementations.Should().BeEmpty();
    }

    [Fact]
    public void DesktopWebView2EvidenceIsolation_ShouldReuseTheExistingHostAndRunner()
    {
        var program = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/Program.cs"));
        program.Should().Contain("CV_DESKTOP_HTTP_PORT");
        program.Should().Contain("CV_DESKTOP_LOG_PATH");
        program.Should().Contain("Requested Desktop HTTP port");
        program.Should().Contain("FindAvailablePort(MinWebPort, MaxWebPort)");

        var mainForm = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/MainForm.cs"));
        mainForm.Should().Contain("CV_WEBVIEW2_USER_DATA_FOLDER");
        mainForm.Should().Contain("InitializeAsync(userDataFolder)");

        var conversationStore = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/ConversationalFlowService.cs"));
        conversationStore.Should().Contain("CV_CONVERSATION_STORE_ROOT");

        var agentRunStore = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/AgentRun/AgentRunEventStore.cs"));
        agentRunStore.Should().Contain("CV_AGENT_RUN_EVENT_STORE");

        var runner = File.ReadAllText(RepoPath("scripts/run-ai-webview2-release-smoke.ps1"));
        foreach (var token in new[]
                 {
                     "DesktopExecutablePath",
                     "NodeSmokePath",
                     "NodeExecutablePath",
                     "SingleRun",
                     "SanitizeDesktopPath",
                     "CV_DESKTOP_HTTP_PORT",
                     "CV_WEBVIEW2_USER_DATA_FOLDER",
                     "CV_CONVERSATION_STORE_ROOT",
                     "CV_AGENT_RUN_EVENT_STORE",
                     "CV_DESKTOP_LOG_PATH",
                     "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                     "Remove-RepositoryTemporaryDirectory",
                     "-WindowStyle Hidden"
                 })
        {
            runner.Should().Contain(token);
        }

        runner.Should().Contain("tests/e2e/ai-webview2-release-smoke.cjs");
        runner.Should().NotContain("powershell.exe -File");

        var evidenceWrapper = File.ReadAllText(RepoPath(
            "scripts/studio-ui-next/Invoke-StudioUiWebView2Evidence.ps1"));
        evidenceWrapper.Should().Contain("scripts/run-ai-webview2-release-smoke.ps1");
        evidenceWrapper.Should().NotContain("Start-Process");

        var matrix = File.ReadAllText(RepoPath(
            "scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1"));
        matrix.Should().Contain("Invoke-StudioUiWebView2Evidence.ps1");
        matrix.Should().Contain("Invoke-StudioUiCanvasPerformanceEvidence.ps1");
        matrix.Should().Contain("scripts/dotnet.ps1");
        matrix.Should().Contain("--artifacts-path");
        matrix.Should().NotContain("Start-Process");
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
        program.Should().Contain("ApplicationConfiguration.Initialize();");
        program.Should().NotContain("SetHighDpiMode");
        program.Should().NotContain("Application.EnableVisualStyles");
        program.Should().NotContain("Application.SetCompatibleTextRenderingDefault");

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
