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
            text.Should().NotContain(".raw", relativePath);
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
    public void FrontendV2_WorkspaceShell_ShouldUseOneHostedFlowCanvasAdapterRegistration()
    {
        var legacyModulesText = File.ReadAllText(Path.Combine(Root, LegacyModulesPath));
        legacyModulesText.Should().Contain("flowCanvasAdapter: '/src/core/canvas/flowCanvasAdapter.js'");
        legacyModulesText.Should().Contain("createHostedFlowCanvasAdapter");

        var workspaceRuntimeText = File.ReadAllText(Path.Combine(Root, WorkspaceShellRuntimePath));
        workspaceRuntimeText.Should().Contain("FLOW_CANVAS_ADAPTER_SERVICE_KEY");
        Regex.Matches(workspaceRuntimeText, @"serviceRegistry\.register\(\s*FLOW_CANVAS_ADAPTER_SERVICE_KEY")
            .Should().ContainSingle("one Workspace lifecycle must register exactly one FlowCanvas adapter.");
        workspaceRuntimeText.Should().NotContain("serviceRegistry.register('flowCanvas'", "V2 must not register a raw FlowCanvas instance.");
        workspaceRuntimeText.Should().NotContain(".raw", "V2 must not use the legacy raw escape hatch.");
    }

    [Fact]
    public void FrontendV2_ShouldNotCreateProjectFlowVariableOrAgentPersistenceAuthority()
    {
        foreach (var file in EnumerateFrontendV2SourceFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = ToRelativePath(file);

            text.Should().NotMatchRegex(@"\bfetch\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\bfetch\s*\(\s*['""`](?:https?:\/\/[^'""`]+)?(?:\/api)?\/projects(?:\/|\?|['""`])", relativePath);
            text.Should().NotMatchRegex(@"\bnew\s+HttpClient\s*\(", relativePath);
            text.Should().NotMatchRegex(@"\blocalStorage\s*\.\s*setItem\s*\(\s*['""`](?:cv[_-])?(?:project|flow|agent|agent-run|globalVariables|global-variables|variables|run)", relativePath);
            text.Should().NotMatchRegex(@"\bindexedDB\s*\.\s*open\s*\(\s*['""`](?:cv[_-])?(?:project|flow|agent|agent-run|globalVariables|global-variables|variables|run)", relativePath);
        }
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
