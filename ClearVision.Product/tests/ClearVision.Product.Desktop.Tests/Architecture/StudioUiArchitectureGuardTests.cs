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
    public void StudioUiProductionSource_ShouldRespectApprovedAuthorityBoundaries()
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
                text.Should().NotContain(token, $"{relativePath} must stay inside the F02 authority boundary");
            }

            text.Should().NotMatchRegex(
                @"\bnew\s+FlowCanvas\s*\(",
                $"{relativePath} must not construct a second FlowCanvas directly");
            text.Should().NotMatchRegex(
                @"\bclass\s+FlowCanvas\b",
                $"{relativePath} must not define a second FlowCanvas kernel");
        }

        var imageCanvasConstructors = sourceFiles
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"\bnew\s+ImageCanvas\s*\(",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();
        imageCanvasConstructors.Should().Equal(
            new[] { "src/platform/canvas/canonicalImageCanvas.ts" },
            "the reviewed canonical facade must remain the sole ImageCanvas lifecycle owner");

        var imageCanvasDeclarations = sourceFiles
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"\bclass\s+ImageCanvas\b",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();
        imageCanvasDeclarations.Should().Equal(
            new[] { "src/platform/canvas/canonical-image-modules.d.ts" },
            "StudioUI may describe the canonical package type but must not define a second ImageCanvas kernel");

        var apiTransport = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "platform", "api", "apiTransport.ts"));
        Regex.Matches(apiTransport, @"\basync\s+get<").Count.Should().Be(1);
        Regex.Matches(apiTransport, @"\basync\s+post<").Count.Should().Be(1);
        Regex.Matches(apiTransport, @"\basync\s+put<").Count.Should().Be(1);
        Regex.Matches(apiTransport, @"\basync\s+patch<").Count.Should().Be(1);
        Regex.Matches(apiTransport, @"\basync\s+delete\s*\(").Count.Should().Be(1);
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
        abortControllerOwners.Should().BeEquivalentTo(new[]
        {
            "src/app/auth/authLifecycleOwner.ts",
            "src/capabilities/ai-workbench/agentRunStreamAdapter.ts",
            "src/capabilities/ai-workbench/aiSessionOwner.ts",
            "src/capabilities/inspection-run/inspectionRunOwner.ts",
            "src/capabilities/inspection-run/inspectionRunPageOwner.ts",
            "src/capabilities/project-lifecycle/projectLifecycleCommandOwner.ts",
            "src/capabilities/project-workspace/camera/cameraBindingEditorOwner.ts",
            "src/capabilities/project-workspace/final-decision/finalDecisionOwner.ts",
            "src/capabilities/project-workspace/handoff/handoffReceivePort.ts",
            "src/capabilities/project-workspace/preview/previewTransport.ts",
            "src/capabilities/project-workspace/run/runCommandOwner.ts",
            "src/capabilities/project-workspace/runtime-package/runtimePackageExportOwner.ts",
            "src/capabilities/results-read/resultEvidenceOwner.ts",
            "src/capabilities/settings/settingsOwner.ts",
            "src/capabilities/settings/settingsWriteCoordinator.ts",
            "src/capabilities/stations-read/stationAdminCommandOwner.ts",
            "src/capabilities/stations-read/stationLifecycleOwner.ts",
            "src/platform/diagnostics/runtimeDiagnostics.ts",
            "src/platform/query/readQuery.ts"
        });

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
    public void StudioUiCanvasConsumers_ShouldUseOnlyTheApprovedProductionCanonicalCanvasFacade()
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

        canonicalCanvasImports.Should().Equal("src/platform/canvas/canonicalFlowCanvas.ts");
        canonicalInteractionImports.Should().Equal("src/platform/canvas/canonicalFlowCanvas.ts");

        var canvasIntegration = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "platform", "canvas", "canonicalFlowCanvas.ts"));
        canvasIntegration.Should().Contain("createHostedFlowCanvasAdapter");
        canvasIntegration.Should().Contain("FlowEditorInteraction");
        canvasIntegration.Should().NotContain("new FlowCanvas");
        canvasIntegration.Should().NotContain("class FlowCanvas");
        canvasIntegration.Should().Contain("CanonicalFlowCanvasOwnerConflictError");

        var canvasOwner = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "labs", "canvas", "canvasLabOwner.ts"));
        canvasOwner.Should().Contain("from '@/platform/canvas'");
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
                     "KeepDatabase",
                     "ReuseDatabase",
                     "AllowInitialAdminSetup",
                     "DeferAuthToScenario",
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
        evidenceWrapper.Should().Contain("\"f01\", \"f02\", \"f03\"");
        evidenceWrapper.Should().Contain("WorkspaceCapabilityEnabled");
        evidenceWrapper.Should().Contain("Studio__WorkspaceCapabilityEnabled");
        evidenceWrapper.Should().Contain("CV_STUDIO_UI_PROFILE");
        evidenceWrapper.Should().Contain("[StudioStartup]");
        evidenceWrapper.Should().Contain("startupLog");
        evidenceWrapper.Should().Contain("KeepDatabase");
        evidenceWrapper.Should().Contain("ReuseDatabase");
        evidenceWrapper.Should().Contain("FinalJourneyPhase");
        evidenceWrapper.Should().Contain("DeferAuthToScenario");
        evidenceWrapper.Should().NotContain("Start-Process");

        var profileEvidence = File.ReadAllText(RepoPath(
            "scripts/studio-ui-next/Invoke-StudioUiProfileEvidence.ps1"));
        profileEvidence.Should().Contain("Invoke-StudioUiWebView2Evidence.ps1");
        profileEvidence.Should().Contain("LEGACY_DEFAULT");
        profileEvidence.Should().Contain("NEXT_PILOT");
        profileEvidence.Should().Contain("NEXT_FULL_CANDIDATE");
        profileEvidence.Should().Contain("ISOLATED_TRUTH_TABLE");
        profileEvidence.Should().Contain("missing-assets");
        profileEvidence.Should().NotContain("Start-Process");

        var rollbackEvidence = File.ReadAllText(RepoPath(
            "scripts/studio-ui-next/Invoke-StudioUiRollbackEvidence.ps1"));
        rollbackEvidence.Should().Contain("Invoke-StudioUiWebView2Evidence.ps1");
        rollbackEvidence.Should().Contain("NEXT_CREATE");
        rollbackEvidence.Should().Contain("LEGACY_VERIFY");
        rollbackEvidence.Should().Contain("NEXT_REOPEN");
        rollbackEvidence.Should().Contain("KeepDatabase");
        rollbackEvidence.Should().Contain("ReuseDatabase");
        rollbackEvidence.Should().NotContain("Start-Process");

        var finalEvidence = File.ReadAllText(RepoPath(
            "scripts/studio-ui-next/Invoke-StudioUiFinalEvidence.ps1"));
        finalEvidence.Should().Contain("Invoke-StudioUiWebView2Evidence.ps1");
        finalEvidence.Should().Contain("CREATE_RUN_LOGOUT");
        finalEvidence.Should().Contain("REOPEN_DELETE");
        finalEvidence.Should().Contain("SOAK");
        finalEvidence.Should().Contain("DeferAuthToScenario");
        finalEvidence.Should().Contain("KeepDatabase");
        finalEvidence.Should().Contain("ReuseDatabase");
        finalEvidence.Should().NotContain("Start-Process");

        var matrix = File.ReadAllText(RepoPath(
            "scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1"));
        matrix.Should().Contain("Invoke-StudioUiWebView2Evidence.ps1");
        matrix.Should().Contain("Invoke-StudioUiCanvasPerformanceEvidence.ps1");
        matrix.Should().Contain("scripts/dotnet.ps1");
        matrix.Should().Contain("--artifacts-path");
        matrix.Should().Contain("RestorePackagesWithLockFile=false");
        matrix.Should().Contain("NuGetLockFilePath");
        matrix.Should().Contain("restore-disabled.packages.lock.json");
        matrix.Should().Contain("\"f01\", \"f02\", \"f03\"");
        matrix.Should().NotContain("Start-Process");

        var browserFixtureServer = File.ReadAllText(RepoPath(
            "ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/support/studio-ui-next-server.cjs"));
        browserFixtureServer.Should().Contain("'f01', 'f02', 'f03'");

        var webView2Scenario = File.ReadAllText(RepoPath(
            "ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/studio-ui-webview2-smoke.cjs"));
        webView2Scenario.Should().Contain("f03-workspace-shell");
        webView2Scenario.Should().Contain("__STUDIO_UI_WORKSPACE_DIAGNOSTICS__");
        webView2Scenario.Should().Contain("CREATE_RUN_LOGOUT");
        webView2Scenario.Should().Contain("REOPEN_DELETE");
        webView2Scenario.Should().Contain("Memory.getDOMCounters");
        webView2Scenario.Should().Contain("HeapProfiler.collectGarbage");
        webView2Scenario.Should().Contain("WeakRef");
        webView2Scenario.Should().Contain("waitForSelectorWithoutHandle");
        webView2Scenario.Should().Contain("OWNER_RESOURCE_WEAKREF_AND_STABLE_LOGIN_DOM_COUNTERS");
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
        settings.RootElement
            .GetProperty("Studio")
            .GetProperty("WorkspaceCapabilityEnabled")
            .GetBoolean()
            .Should()
            .BeFalse();

        var options = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/Configuration/StudioOptions.cs"));
        options.Should().Contain("StudioUiEnabled { get; set; } = false");
        options.Should().Contain("WorkspaceCapabilityEnabled { get; set; } = false");

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
    public void StudioUiF02Composition_ShouldKeepOneProductShellAndSharedReadOwners()
    {
        var sourceFiles = GetStudioUiProductionFiles();

        sourceFiles.Count(file => Path.GetFileName(file) == "ProductLayout.vue").Should().Be(1);
        sourceFiles.Count(file => Path.GetFileName(file) == "InternalLabLayout.vue").Should().Be(1);
        sourceFiles.Count(file => Path.GetFileName(file) == "sessionProjectionOwner.ts").Should().Be(1);
        sourceFiles.Count(file => Path.GetFileName(file) == "authLifecycleOwner.ts").Should().Be(1);
        sourceFiles.Count(file => Path.GetFileName(file) == "systemStatusOwner.ts").Should().Be(1);
        sourceFiles.Count(file => Path.GetFileName(file) == "readQuery.ts").Should().Be(1);

        var productSource = sourceFiles
            .Where(file => !StudioUiRelativePath(file).StartsWith("src/labs/", StringComparison.Ordinal))
            .Where(file => StudioUiRelativePath(file) != "src/platform/api/apiTransport.ts")
            .Where(file => StudioUiRelativePath(file) != "src/capabilities/settings/contracts.ts")
            .Select(File.ReadAllText)
            .ToList();
        productSource.Should().OnlyContain(text =>
            !Regex.IsMatch(
                text,
                "method\\s*:\\s*['\\\"](?:POST|PUT|PATCH|DELETE)['\\\"]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        productSource.Should().OnlyContain(text =>
            !text.Contains("new EventSource", StringComparison.Ordinal));

        sourceFiles.Should().NotContain(file =>
            File.ReadAllText(file).Contains("createSessionProjectionOwner(", StringComparison.Ordinal));
        sourceFiles
            .Where(file => File.ReadAllText(file).Contains("cv_auth_token", StringComparison.Ordinal))
            .Select(StudioUiRelativePath)
            .Should()
            .Equal("src/platform/auth/tokenPort.ts");

        var authOwner = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "app", "auth", "authLifecycleOwner.ts"));
        authOwner.Should().Contain("setUnauthorizedHandler");
        authOwner.Should().Contain("unauthorizedFlights");
        authOwner.Should().NotContain("EventBus");
    }

    [Fact]
    public void StudioUiRouter_ShouldFreezeApprovedProductAndInternalLabRoutes()
    {
        var router = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "src", "app", "router.ts"));
        var navigation = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "src", "app", "navigation.ts"));

        router.Should().Contain("createWebHashHistory(import.meta.env.BASE_URL)");
        router.Should().Contain("component: ProductLayout");
        router.Should().Contain("component: InternalLabLayout");
        router.Should().Contain("redirect: '/projects'");
        router.Should().Contain("path: 'overview'");
        router.Should().Contain("path: 'projects'");
        router.Should().Contain("path: 'ai'");
        router.Should().Contain("path: 'projects/:id'");
        router.Should().Contain("path: 'projects/:id/ai'");
        router.Should().Contain("path: 'operators'");
        router.Should().Contain("path: 'operators/:operatorType'");
        router.Should().Contain("path: 'stations'");
        router.Should().Contain("path: 'stations/:stationId'");
        router.Should().Contain("path: 'inspection'");
        router.Should().Contain("path: 'projects/:id/inspection'");
        router.Should().Contain("path: 'results'");
        router.Should().Contain("path: 'settings'");
        router.Should().Contain("path: 'diagnostics'");
        router.Should().Contain("path: 'about'");
        router.Should().Contain("path: ':pathMatch(.*)*'");
        router.Should().Contain("path: '/labs'");
        router.Should().Contain("path: 'design'");
        router.Should().Contain("path: 'canvas'");
        router.Should().Contain("router.beforeEach");
        router.Should().Contain("resolveSafeReturnRoute");
        router.Should().Contain("path: '/login'");
        router.Should().Contain("path: '/setup'");
        router.Should().Contain("path: '/forbidden'");
        router.Should().Contain("path === '/settings'");

        navigation.Should().Contain("to: '/ai'");
        navigation.Should().Contain("to: '/projects'");
        navigation.Should().Contain("to: '/inspection'");
        navigation.Should().Contain("to: '/results'");
        navigation.Should().Contain("to: '/settings'");
        navigation.Should().Contain("to: '/stations'");
        navigation.Should().NotContain("to: '/overview'");
        navigation.Should().NotContain("to: '/operators'");
        navigation.Should().NotContain("to: '/diagnostics'");
        navigation.Should().NotContain("to: '/about'");
        navigation.Should().NotContain("/labs");
    }

    [Fact]
    public void StudioUiF03G5Workspace_ShouldKeepOnePersistenceOwnerAndExactProjectPutBoundary()
    {
        var workspaceRoot = Path.Combine(
            RepoPath(StudioUiRoot),
            "src",
            "capabilities",
            "project-workspace");
        Directory.Exists(workspaceRoot).Should().BeTrue();
        var workspaceFiles = Directory.EnumerateFiles(
                workspaceRoot,
                "*.*",
                SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
            .ToList();
        workspaceFiles.Count(path => Path.GetFileName(path) == "workspaceOwner.ts")
            .Should()
            .Be(1);

        foreach (var file in workspaceFiles)
        {
            var text = File.ReadAllText(file);
            var relativePath = StudioUiRelativePath(file);
            text.Should().NotMatchRegex(
                @"\b(?:globalThis\.)?fetch\s*\(",
                $"{relativePath} must consume only the narrow Workspace read port");
            if (!relativePath.EndsWith("workspaceRuntime.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("workspaceOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("workspaceNewDraftOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("handoff/handoffReceivePort.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("flow/flowCanvasOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("preview/previewOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("preview/previewWorkbenchOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("preview/previewTransport.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("persistence/projectPersistencePort.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("run/runContracts.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("camera/cameraBindingEditorOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("global-variables/workspaceGlobalVariablesOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("final-decision/finalDecisionOwner.ts", StringComparison.Ordinal) &&
                !relativePath.EndsWith("runtime-package/runtimePackageExportOwner.ts", StringComparison.Ordinal))
            {
                text.Should().NotContain("ApiTransport", $"{relativePath} must not hold the generic transport");
            }
            text.Should().NotMatchRegex(
                "method\\s*:\\s*['\\\"](?:POST|PUT|PATCH|DELETE)['\\\"]",
                $"{relativePath} must not create an ad-hoc HTTP method owner");
            text.Should().NotMatchRegex(
                "from\\s+['\\\"][^'\\\"]*(?:/labs/|FrontendV2)",
                $"{relativePath} must not import retired or Lab production code");
            text.Should().NotMatchRegex(@"\bnew\s+(?:FlowCanvas|ImageCanvas)\s*\(");
            text.Should().NotContain("window.flowCanvas");
            text.Should().NotContain("FlowCanvas.serialize()");
            text.Should().NotContain("new EventSource");
        }

        var query = File.ReadAllText(Path.Combine(workspaceRoot, "workspaceQueries.ts"));
        query.Should().Contain("return `projects/${projectId}`");
        query.Should().Contain("workspace-project:${client.sessionGeneration}:${projectId}");
        query.Should().NotContain("preview");
        query.Should().NotContain("artifact");
        query.Should().NotContain("admission");
        query.Should().NotContain("execute");

        var operatorQuery = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "capabilities", "operators-read", "operatorQueries.ts"));
        operatorQuery.Should().Contain("operators/library?includeCompatibility=true");
        operatorQuery.Should().Contain("operators/${encodeURIComponent(operatorType)}/metadata");

        var flowOwner = File.ReadAllText(Path.Combine(workspaceRoot, "flow", "flowCanvasOwner.ts"));
        flowOwner.Should().Contain("createCanonicalFlowCanvasHost");
        flowOwner.Should().Contain("openInspector");
        flowOwner.Should().Contain("patchNodeParameter");
        flowOwner.Should().Contain("patchNodeProperties");
        flowOwner.Should().NotContain(".raw");
        flowOwner.Should().NotContain("src/labs");

        var inspectorRoot = Path.Combine(workspaceRoot, "inspector");
        Directory.Exists(inspectorRoot).Should().BeTrue();
        Directory.EnumerateFiles(inspectorRoot, "inspectorOwner.ts", SearchOption.AllDirectories)
            .Should()
            .ContainSingle();
        var inspectorSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(inspectorRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        inspectorSource.Should().Contain("FlowCanvasOwner");
        inspectorSource.Should().Contain("patchNodeParameter");
        inspectorSource.Should().Contain("patchNodeProperties");
        inspectorSource.Should().Contain("disconnect");
        inspectorSource.Should().NotContain("FlowCanvas.serialize()");
        inspectorSource.Should().NotContain("FilePickedEvent");
        inspectorSource.Should().NotContain("HostBridge");
        Directory.Exists(Path.Combine(workspaceRoot, "preview")).Should().BeTrue();
        Directory.Exists(Path.Combine(workspaceRoot, "persistence")).Should().BeTrue();
        var runRoot = Path.Combine(workspaceRoot, "run");
        Directory.Exists(runRoot).Should().BeTrue();
        Directory.EnumerateFiles(runRoot, "runCommandOwner.ts", SearchOption.AllDirectories)
            .Should()
            .ContainSingle();

        var persistenceRoot = Path.Combine(workspaceRoot, "persistence");
        Directory.EnumerateFiles(persistenceRoot, "workspacePersistenceOwner.ts", SearchOption.AllDirectories)
            .Should()
            .ContainSingle();
        var persistenceSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(persistenceRoot, "*.ts", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        persistenceSource.Should().Contain("projects/${projectId}");
        persistenceSource.Should().Contain("PSV011");
        persistenceSource.Should().Contain("GV031");
        persistenceSource.Should().Contain("unknown-outcome");
        persistenceSource.Should().NotContain("projects/${projectId}/flow");
        persistenceSource.Should().NotContain("projects/${projectId}/global-variables");
        persistenceSource.Should().NotContain("inspection/execute");
        persistenceSource.Should().NotContain("inspection/admission");

        var runSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(runRoot, "*.ts", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        runSource.Should().Contain("'inspection/admission'");
        runSource.Should().Contain("'inspection/execute'");
        runSource.Should().Contain("expectedPersistenceRevision");
        runSource.Should().Contain("expectedCanonicalFlowHash");
        runSource.Should().Contain("expectedDecisionConfigurationHash");
        runSource.Should().Contain("unknown-outcome");
        runSource.Should().NotContain("FlowData");
        runSource.Should().NotContain("EventSource");
        runSource.Should().NotContain("WebMessage");

        var workspaceContracts = File.ReadAllText(Path.Combine(workspaceRoot, "workspaceContracts.ts"));
        workspaceContracts.Should().Contain("expectedPersistenceRevision: baseline.persistenceRevision");
        workspaceContracts.Should().Contain("globalVariables: encodeWorkspaceGlobalVariablesV1");

        var apiTransport = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "platform", "api", "apiTransport.ts"));
        Regex.Matches(apiTransport, @"\bsend\(\s*path,\s*'PUT'").Count.Should().Be(1);

        var canonical = File.ReadAllText(Path.Combine(
            RepoPath(StudioUiRoot), "src", "platform", "canvas", "canonicalFlowCanvas.ts"));
        canonical.Should().Contain("patchNodeParameter");
        canonical.Should().Contain("patchNodeProperties");
        canonical.Should().Contain("mergeFlowPersistence");

        var router = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "src", "app", "router.ts"));
        router.Should().Contain("path: 'projects/:id/workspace'");
        router.Should().Contain("workspaceMode: true");

        var host = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs"));
        host.Should().Contain("[\"Studio2.Workspace\"] = studioOptions.WorkspaceCapabilityEnabled");
        var mainForm = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/MainForm.cs"));
        mainForm.Should().Contain("__clearVisionFlushProjectWorkspace");
        mainForm.Should().Contain("Promise.all(flushers.map");
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
