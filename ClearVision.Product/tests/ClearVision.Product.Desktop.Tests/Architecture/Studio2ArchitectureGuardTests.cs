using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests.Architecture;

public sealed class Studio2ArchitectureGuardTests
{
    private static readonly string Root = FindRepositoryRoot();

    private const string FrontendV2SourceRoot =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src";
    private const string HostBridgeAdapterPath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/host/hostBridge.ts";
    private const string LegacyFlowCanvasAdapterPath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js";
    private const string WorkspaceShellRuntimePath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/workspace/workspaceShellRuntime.ts";
    private const string LegacyModulesPath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/adapters/legacyModules.ts";
    private const string FlowEditorPortPath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/flowEditor/studioFlowEditorPort.ts";
    private const string ProjectPersistencePortPath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/project/studioProjectPersistencePort.ts";
    private const string FlowEditorPortPanelPath =
        "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/components/FlowEditorPortPanel.vue";

    [Fact]
    public void FrontendV2_ShouldReuseExistingEventBusAndServiceRegistry()
    {
        foreach (var file in EnumerateFrontendV2SourceFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotMatchRegex(@"\bclass\s+EventBus\b", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+EventBus\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bclass\s+ServiceRegistry\b", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+ServiceRegistry\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bclass\s+HttpClient\b", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+HttpClient\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bfunction\s+createEventBus\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bfunction\s+createServiceRegistry\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bfunction\s+createHttpClient\s*\(", relativePath);
        }
    }

    [Fact]
    public void FrontendV2_ShouldUseFlowCanvasAdapterInsteadOfRawFlowCanvas()
    {
        foreach (var file in EnumerateFrontendV2SourceFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotMatchRegex(@"from\s+['""][^'""]*core/canvas/flowCanvas\.js['""]", relativePath);
            text.Should().NotMatchRegex(@"from\s+['""][^'""]*/flowCanvas\.js['""]", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+FlowCanvas\s*\(", relativePath);
            text.Should().NotContain("FlowCanvas.prototype", relativePath);
            text.Should().NotMatchRegex(@"\.raw\b", relativePath);
            text.Should().NotMatchRegex(@"\badapter\s*\.\s*raw\b", relativePath);
            text.Should().NotMatchRegex(@"\bwindow\s*\.\s*flowCanvas\b", relativePath);
            text.Should().NotMatchRegex(@"\bclass\s+\w*FlowCanvas\w*Adapter\b", relativePath);
        }
    }

    [Fact]
    public void LegacyFlowCanvasAdapter_ShouldBeTheOnlyPlaceThatCreatesHostedFlowCanvas()
    {
        var legacyAdapterText = File.ReadAllText(Path.Combine(Root, LegacyFlowCanvasAdapterPath));
        legacyAdapterText.Should().Contain("createHostedFlowCanvasAdapter");
        Regex.Matches(legacyAdapterText, @"\bnew\s+FlowCanvas\s*\(")
            .Should().ContainSingle("only the legacy FlowCanvasAdapter hosted factory may create FlowCanvas for V2.");

        foreach (var file in EnumerateFrontendV2SourceFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotContain("flowCanvas.js", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+FlowCanvas\s*\(", relativePath);
        }
    }

    [Fact]
    public void FrontendV2_WorkspaceShell_ShouldExposeOnlyOneFlowEditorPortRegistration()
    {
        var legacyModulesText = File.ReadAllText(Path.Combine(Root, LegacyModulesPath));
        legacyModulesText.Should().Contain("flowCanvasAdapter: '/src/core/canvas/flowCanvasAdapter.js'");
        legacyModulesText.Should().Contain("createHostedFlowCanvasAdapter");
        legacyModulesText.Should().Contain("LegacyFlowCanvasAdapter");

        var workspaceRuntimeText = File.ReadAllText(Path.Combine(Root, WorkspaceShellRuntimePath));
        workspaceRuntimeText.Should().Contain("FLOW_EDITOR_PORT_SERVICE_KEY");
        workspaceRuntimeText.Should().Contain("createStudioFlowEditorPort");
        workspaceRuntimeText.Should().NotContain("FLOW_CANVAS_ADAPTER_SERVICE_KEY");
        Regex.Matches(workspaceRuntimeText, @"serviceRegistry\.register\(\s*FLOW_EDITOR_PORT_SERVICE_KEY")
            .Should().ContainSingle("one Workspace lifecycle must register exactly one Flow Editor Port.");
        workspaceRuntimeText.Should().NotContain("serviceRegistry.register('flowCanvas'", "V2 must not register a raw FlowCanvas instance.");
        workspaceRuntimeText.Should().NotMatchRegex(@"\.raw\b", "V2 must not use the legacy raw escape hatch.");
    }

    [Fact]
    public void FrontendV2_FlowEditorPort_ShouldOwnRequestSequenceAuthority()
    {
        var flowEditorPortText = File.ReadAllText(Path.Combine(Root, FlowEditorPortPath));
        flowEditorPortText.Should().Contain("nextRequestSequence");
        flowEditorPortText.Should().Contain("maxObservedRequestSequenceByProject");

        var panelText = File.ReadAllText(Path.Combine(Root, FlowEditorPortPanelPath));
        panelText.Should().Contain("port.nextRequestSequence");
        panelText.Should().NotContain("Date.now", "Flow editor commands must use the shared port allocator.");

        foreach (var file in EnumerateFrontendV2SourceFiles())
        {
            var relativePath = ToRelativePath(file);
            if (string.Equals(relativePath, FlowEditorPortPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            text.Should().NotMatchRegex(@"\bDate\.now\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\blet\s+\w*requestSequence\w*\s*=", relativePath);
            text.Should().NotMatchRegex(@"\brequestSequence\s*(?:\+\+|\+=)", relativePath);
        }
    }

    [Fact]
    public void FrontendV2_ShouldNotCreateProjectFlowVariableOrAgentPersistenceAuthority()
    {
        foreach (var file in EnumerateFrontendV2ProductionSourceFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotMatchRegex(@"\bfetch\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bfetch\s*\(\s*['""`](?:https?:\/\/[^'""`]+)?(?:\/api)?\/projects(?:\/|\?|['""`])", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+HttpClient\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bsaveProject\s*\(", relativePath);
            text.Should().NotContain("ProjectSaveCoordinator", relativePath);
            text.Should().NotMatchRegex(@"\blocalStorage\s*\.\s*setItem\s*\(\s*['""`](?:cv[_-])?(?:project|flow|agent|agent-run|globalVariables|global-variables|variables|run)", relativePath);
            text.Should().NotMatchRegex(@"\bindexedDB\s*\.\s*open\s*\(\s*['""`](?:cv[_-])?(?:project|flow|agent|agent-run|globalVariables|global-variables|variables|run)", relativePath);
        }
    }

    [Fact]
    public void FrontendV2_ProjectPersistencePort_ShouldUseSingleProjectPutAndBackendRevisionContract()
    {
        var projectPortText = File.ReadAllText(Path.Combine(Root, ProjectPersistencePortPath));
        projectPortText.Should().Contain("httpClient.put");
        projectPortText.Should().Contain("expectedPersistenceRevision");
        projectPortText.Should().Contain("persistenceRevision");
        projectPortText.Should().NotContain("ProjectSaveCoordinator");
        projectPortText.Should().NotContain("projectManager");
        projectPortText.Should().NotMatchRegex(@"\bfetch\s*\(");
        projectPortText.Should().NotMatchRegex(@"['""`][^'""`]*projects[^'""`]*\/flow[^'""`]*['""`]");
        projectPortText.Should().NotMatchRegex(@"['""`][^'""`]*projects[^'""`]*\/global-variables[^'""`]*['""`]");
        Regex.Matches(projectPortText, @"httpClient\.put\(")
            .Should().ContainSingle("G04B V2 persistence must save through the existing single project update endpoint.");

        var workspaceRuntimeText = File.ReadAllText(Path.Combine(Root, WorkspaceShellRuntimePath));
        workspaceRuntimeText.Should().Contain("PROJECT_PERSISTENCE_PORT_SERVICE_KEY");
        Regex.Matches(workspaceRuntimeText, @"serviceRegistry\.register\(\s*PROJECT_PERSISTENCE_PORT_SERVICE_KEY")
            .Should().ContainSingle("one Workspace lifecycle must register exactly one Project Persistence Port.");
    }

    [Fact]
    public void FrontendV2_WebView2Access_ShouldStayBehindTheHostBridgeAdapter()
    {
        var violations = FindDirectWebView2AccessViolations(
            EnumerateFrontendV2SourceFiles()
                .Select(file => (RelativePath: ToRelativePath(file), Text: File.ReadAllText(file))));

        violations.Should().BeEmpty();
    }

    [Fact]
    public void FrontendV2_WebView2AccessGuard_ShouldRejectDirectAccessOutsideAdapter()
    {
        var violations = FindDirectWebView2AccessViolations(
        [
            (
                "ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/components/BadIsland.ts",
                "window.chrome.webview.postMessage({ type: 'bad' });"
            ),
            (
                HostBridgeAdapterPath,
                "window.chrome.webview.postMessage({ type: 'allowed-adapter' });"
            )
        ]);

        violations.Should().ContainSingle()
            .Which.Should().Contain("BadIsland.ts");
    }

    [Fact]
    public void FrontendV2_ShouldNotCreateSecondAgentRunAuthority()
    {
        foreach (var file in EnumerateFrontendV2SourceFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotMatchRegex(@"\bclass\s+AgentRunEventStore\b", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+AgentRunEventStore\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bfunction\s+createAgentRunEventStore\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bclass\s+AgentRunStateMachine\b", relativePath);
            text.Should().NotMatchRegex(@"\bfunction\s+createAgentRunStateMachine\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\b(?:resolve|determine|compute)AgentRunTerminal", relativePath);
            text.Should().NotMatchRegex(@"\b(?:complete|fail|cancel)AgentRun\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\blocalStorage\s*\.\s*setItem\s*\(\s*['""`](?:cv[_-])?(?:agent|agent-run|run)", relativePath);
            text.Should().NotMatchRegex(@"\bindexedDB\s*\.\s*open\s*\(\s*['""`](?:cv[_-])?(?:agent|agent-run|run)", relativePath);
        }
    }

    [Fact]
    public void Station_ShouldNotDependOnVueNodeOrStudioFrontend()
    {
        var stationRoot = Path.Combine(Root, "ClearVision.Product/src/ClearVision.Product.Station");
        var stationFiles = Directory.EnumerateFiles(stationRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        stationFiles.Should().NotBeEmpty();

        foreach (var file in stationFiles)
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotMatchRegex(@"\bVue\b", relativePath);
            text.Should().NotMatchRegex(@"\bVite\b", relativePath);
            text.Should().NotMatchRegex(@"\bPinia\b", relativePath);
            text.Should().NotContain("FrontendV2", relativePath);
            text.Should().NotContain("frontend-v2", relativePath);
            text.Should().NotContain("wwwroot/src", relativePath);
            text.Should().NotContain("node_modules", relativePath);
            text.Should().NotMatchRegex(@"\bnode\.exe\b", relativePath);
            text.Should().NotMatchRegex(@"\bProcessStartInfo\s*\(\s*['""]node['""]", relativePath);
        }
    }

    private static IReadOnlyList<string> EnumerateFrontendV2SourceFiles()
    {
        var root = Path.Combine(Root, FrontendV2SourceRoot);
        Directory.Exists(root).Should().BeTrue("G02A creates the real FrontendV2 source root.");

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsFrontendSourceFile)
            .ToList();

        files.Should().NotBeEmpty("G02A must make the Studio 2.0 guard scan real V2 source files.");

        return files;
    }

    private static IReadOnlyList<string> EnumerateFrontendV2ProductionSourceFiles() =>
        EnumerateFrontendV2SourceFiles()
            .Where(file => !ToRelativePath(file).Contains("/src/tests/", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool IsFrontendSourceFile(string path) =>
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> FindDirectWebView2AccessViolations(
        IEnumerable<(string RelativePath, string Text)> sourceFiles)
    {
        var violations = new List<string>();
        foreach (var (relativePath, text) in sourceFiles)
        {
            if (string.Equals(relativePath, HostBridgeAdapterPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (text.Contains("window.chrome.webview", StringComparison.Ordinal) ||
                text.Contains("chrome.webview", StringComparison.Ordinal))
            {
                violations.Add(relativePath);
            }
        }

        return violations;
    }

    private static string ToRelativePath(string path) =>
        Path.GetRelativePath(Root, path).Replace('\\', '/');

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
