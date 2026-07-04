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
    public void ExecutionObservation_ShouldRemainDesktopProjectionOnly()
    {
        var sourceRoot = Path.Combine(Root, "ClearVision.Product/src");
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var allowedObservationPaths = new[]
        {
            "ClearVision.Product/src/ClearVision.Product.Desktop/Observation/",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/CalibrationDraftEndpoints.cs"
        };

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("ExecutionObservation", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = ToRelativePath(file);
            allowedObservationPaths.Any(path => relativePath.StartsWith(path, StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(relativePath, path, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"{relativePath} must not make Observation a Core, EF, RuntimeHost, or Station contract.");
        }

        var forbiddenRoots = new[]
        {
            "ClearVision.Product/src/ClearVision.Product.Core/",
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/Data/",
            "ClearVision.Product/src/ClearVision.Product.Runtime/",
            "ClearVision.Product/src/ClearVision.Product.Station/"
        };

        foreach (var relativeRoot in forbiddenRoots)
        {
            var absoluteRoot = Path.Combine(Root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                File.ReadAllText(file).Should().NotContain("ExecutionObservation", ToRelativePath(file));
            }
        }

        var previewEndpointFiles = sourceFiles
            .Select(file => (RelativePath: ToRelativePath(file), Text: File.ReadAllText(file)))
            .Where(file => file.Text.Contains("/api/flows/preview-node", StringComparison.Ordinal))
            .ToList();
        previewEndpointFiles.Should().ContainSingle("G05A must adapt the existing preview endpoint, not add a second one.")
            .Which.RelativePath.Should().Be("ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs");
        Regex.Matches(previewEndpointFiles[0].Text, "MapPost\\(\\\"/api/flows/preview-node\\\"")
            .Should().ContainSingle("there must be exactly one preview-node POST endpoint.");

        File.ReadAllText(Path.Combine(Root, "ClearVision.Product/src/ClearVision.Product.Desktop/Observation/ExecutionObservationEnvelopeV1.cs"
                .Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .Contain("PreviewArtifactReferenceV1?", "G05B may attach optional artifact refs to resource detail nodes without making Observation persistent authority.");
    }

    [Fact]
    public void ExecutionObservation_ShouldAvoidGenericReflectionAndUnboundedMetricsEnumeration()
    {
        var projectorPath = Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Desktop/Observation/ExecutionObservationProjector.cs"
                .Replace('/', Path.DirectorySeparatorChar));
        var endpointPath = Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs"
                .Replace('/', Path.DirectorySeparatorChar));

        var projector = File.ReadAllText(projectorPath);
        projector.Should().NotContain("System.Reflection", "G05A observation projection must not enumerate arbitrary public getters.");
        projector.Should().NotContain("BindingFlags", "G05A observation projection must not enumerate arbitrary public getters.");
        projector.Should().NotContain("GetProperties(", "G05A observation projection must use explicit known adapters, not generic reflection.");

        var endpoint = File.ReadAllText(endpointPath);
        endpoint.Should().NotContain("Cast<object?>().Count()", "G05A metrics must not count unknown custom IEnumerable values.");
        endpoint.Should().Contain("ArtifactMode", "G05B keeps artifact transport additive and opt-in for first-party preview calls.");
    }

    [Fact]
    public void PreviewArtifact_ShouldRemainDesktopScopedAndAvoidLiveResourceStorage()
    {
        var sourceRoot = Path.Combine(Root, "ClearVision.Product/src");
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var allowedArtifactPaths = new[]
        {
            "ClearVision.Product/src/ClearVision.Product.Desktop/PreviewArtifacts/",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewArtifactEndpoints.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/CalibrationDraftEndpoints.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/ApiEndpoints.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Observation/",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Program.cs"
        };

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"\bPreviewArtifact\w*\b") &&
                !text.Contains("preview-artifacts", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = ToRelativePath(file);
            allowedArtifactPaths.Any(path => relativePath.StartsWith(path, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(relativePath, path, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"{relativePath} must not move Preview Artifact authority outside Desktop.");
        }

        var storeText = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Desktop/PreviewArtifacts/PreviewArtifactStore.cs"
                .Replace('/', Path.DirectorySeparatorChar)));
        storeText.Should().NotContain("OpenCvSharp", "PreviewArtifactStore stores immutable bytes and metadata only.");
        storeText.Should().NotContain("ImageWrapper", "PreviewArtifactStore must not hold live ImageWrapper instances.");
        storeText.Should().NotMatchRegex(@"\bMat\b", "PreviewArtifactStore must not hold live Mat instances.");

        var artifactEndpointText = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewArtifactEndpoints.cs"
                .Replace('/', Path.DirectorySeparatorChar)));
        artifactEndpointText.Should().Contain("MapGet(\"/api/preview-artifacts/{artifactId}\"");
        artifactEndpointText.Should().Contain("MapDelete(\"/api/preview-artifacts/{artifactId}\"");
        artifactEndpointText.Should().NotContain("MapGet(\"/api/preview-artifacts/stream");
        artifactEndpointText.Should().NotContain("text/event-stream");

        var previewCoordinatorText = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js"
                .Replace('/', Path.DirectorySeparatorChar)));
        Regex.Matches(previewCoordinatorText, @"class\s+NodePreviewCoordinator\b")
            .Should().ContainSingle("G05B must reuse the existing preview coordinator.");
        previewCoordinatorText.Should().Contain("artifactMode: 'references'");
        previewCoordinatorText.Should().NotContain("new EventSource('/api/preview-artifacts");
    }

    [Fact]
    public void NodePreviewInspector_ShouldStayBehindSingleCoordinatorFlagAndAvoidNewAuthorities()
    {
        var appText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/app.js");
        var coordinatorText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js");
        var inspectorText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js");
        var selectionStoreText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewSelectionStore.js");
        var studioOptionsText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/Configuration/StudioOptions.cs");
        var webViewHostText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs");
        var appSettingsText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json");
        var adrText = ReadRepoText("docs/进行中/Studio2/architecture/Studio2-架构边界-ADR.md");

        Regex.Matches(coordinatorText, @"class\s+NodePreviewCoordinator\b")
            .Should().ContainSingle("G06 must reuse the existing preview coordinator owner.");
        Regex.Matches(appText, @"new\s+NodePreviewCoordinator\s*\(")
            .Should().ContainSingle("legacy app composition root should create one preview coordinator.");
        Regex.Matches(appText, @"new\s+NodePreviewOverlay\s*\(")
            .Should().ContainSingle("flag off keeps the legacy overlay as the only old owner.");
        Regex.Matches(appText, @"new\s+NodePreviewInspector\s*\(")
            .Should().ContainSingle("flag on creates one inspector owner.");
        appText.Should().Contain("if (inspectorEnabled)");
        appText.Should().Contain("serviceRegistry.register('nodePreviewCoordinator'");
        appText.Should().Contain("serviceRegistry.register('nodePreviewOverlay'");
        appText.Should().Contain("serviceRegistry.register('nodePreviewInspector'");
        appText.Should().Contain("const NODE_PREVIEW_INSPECTOR_ENABLED = readNodePreviewInspectorFlagOnce()");
        appText.Should().Contain("featureFlags?.[NODE_PREVIEW_INSPECTOR_FLAG_KEY] === true");
        appText.Should().NotContain("startup.nodePreviewInspectorEnabled === true", "owner decision must use only the canonical featureFlags key.");
        Regex.Matches(appText, @"createNodePreviewSelectionStore\(\)")
            .Should().ContainSingle("selectionStore must only be created in the inspector-enabled branch.");
        appText.IndexOf("createNodePreviewSelectionStore()", StringComparison.Ordinal)
            .Should().BeGreaterThan(appText.IndexOf("if (inspectorEnabled)", StringComparison.Ordinal));

        studioOptionsText.Should().Contain("NodePreviewInspectorEnabled");
        studioOptionsText.Should().Contain("CircleSearchV2ToolEnabled");
        studioOptionsText.Should().Contain("NPointCalibrationWorkbenchEnabled");
        webViewHostText.Should().Contain("const featureFlags = Object.freeze");
        webViewHostText.Should().Contain("Studio:CircleSearchV2ToolEnabled");
        webViewHostText.Should().Contain("Studio:NPointCalibrationWorkbenchEnabled");
        webViewHostText.Should().Contain("Object.defineProperty(startup, 'featureFlags'");
        webViewHostText.Should().Contain("Object.freeze(startup)");
        webViewHostText.Should().Contain("configurable: false");
        appSettingsText.Should().Contain("\"NodePreviewInspectorEnabled\": false");
        appSettingsText.Should().Contain("\"CircleSearchV2ToolEnabled\": true");
        appSettingsText.Should().Contain("\"NPointCalibrationWorkbenchEnabled\": true");
        adrText.Should().Contain("Studio:NodePreviewInspectorEnabled");
        adrText.Should().Contain("G15.2/G16");

        inspectorText.Should().NotMatchRegex(@"\bfetch\s*\(", "Inspector must read artifacts through NodePreviewCoordinator.");
        inspectorText.Should().NotContain("httpClient", "Inspector must not import or create a second Artifact client.");
        inspectorText.Should().NotContain("projectManager", "Inspector must route binding through the composition root and GlobalVariablePanel.");
        inspectorText.Should().NotContain("globalVariableStore", "Inspector must not become a global-variable schema authority.");
        inspectorText.Should().NotContain("saveGlobalVariables", "Inspector must not save global-variable schema directly.");
        inspectorText.Should().NotMatchRegex(@"\bblob\s*\.\s*text\s*\(", "Inspector must not decode a full Artifact Blob before bounding it.");
        inspectorText.Should().Contain("MAX_ARTIFACT_TEXT_PREVIEW_BYTES");
        inspectorText.Should().Contain("MAX_ARTIFACT_TEXT_DISPLAY_CHARS");
        inspectorText.Should().Contain("readArtifactForCurrentState");
        coordinatorText.Should().Contain("readArtifactForCurrentState");
        coordinatorText.Should().Contain("findCurrentArtifact");

        selectionStoreText.Should().NotContain("localStorage");
        selectionStoreText.Should().NotContain("IndexedDB");
        selectionStoreText.Should().NotContain("saveProject");
        selectionStoreText.Should().NotContain("ProjectSave");
        selectionStoreText.Should().NotContain("GlobalVariables");

        var g07FrontendText = string.Join(
            "\n",
            inspectorText,
            selectionStoreText,
            coordinatorText,
            appText);
        g07FrontendText.Should().Contain("resultPathVersion");
        g07FrontendText.Should().NotMatchRegex(@"class\s+\w*ResultPath\w*Parser\b");
        g07FrontendText.Should().NotMatchRegex(@"function\s+\w*ResultPath\w*Parser\s*\(");
        g07FrontendText.Should().NotContain("ResultPathParser");
        appText.Should().Contain("bindPreviewField");

        var previewEndpointText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs");
        previewEndpointText.Should().NotContain("ResultPathVersion");
        previewEndpointText.Should().NotContain("MapPost(\"/api/results");
        previewEndpointText.Should().NotContain("MapGet(\"/api/results/fields");

        var forbiddenRoots = new[]
        {
            "ClearVision.Product/src/ClearVision.Product.Station",
            "ClearVision.Product/src/ClearVision.Product.Runtime",
            "ClearVision.Product/src/ClearVision.Product.Core",
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent"
        };

        foreach (var root in forbiddenRoots)
        {
            var absoluteRoot = Path.Combine(Root, root.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            {
                File.ReadAllText(file).Should().NotContain("NodePreviewInspector", ToRelativePath(file));
            }
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

    [Fact]
    public void ProjectAssets_ShouldUseProjectSaveCoordinatorAuthorityWithoutIndependentRevision()
    {
        var coordinatorText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Application/Services/ProjectSaveCoordinator.cs");
        var dtoText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Application/DTOs/ProjectAssetsDto.cs");
        var endpointText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/CalibrationDraftEndpoints.cs");
        var workbenchText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/calibrationDraftWorkbench.js");
        var runtimeText = ReadRepoText("ClearVision.Product/src/ClearVision.Product.Runtime/RuntimePackageExporter.cs");
        var stationText = string.Join(
            "\n",
            Directory.EnumerateFiles(
                    Path.Combine(Root, "ClearVision.Product/src/ClearVision.Product.Station"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        coordinatorText.Should().Contain("ProjectSaveParticipant.ProjectAssets");
        coordinatorText.Should().Contain("ProjectAssetsFileName");
        coordinatorText.Should().Contain("ProjectAssetSaveCandidate");
        coordinatorText.Should().NotContain("AssetSaveCoordinator");
        coordinatorText.Should().NotContain("AssetRevision");
        dtoText.Should().Contain("ProjectRevision");
        dtoText.Should().NotContain("AssetRevision");
        endpointText.Should().Contain("SaveCalibrationAssetAsync");
        endpointText.Should().NotContain("JsonFileProjectAssetStorage");
        workbenchText.Should().Contain("/calibration-assets/from-draft");
        workbenchText.Should().NotContain("ProjectSaveCoordinator");
        runtimeText.Should().NotContain("ProjectAssets");
        runtimeText.Should().NotContain("CalibrationAssets");
        stationText.Should().NotContain("ProjectAssets");
        stationText.Should().NotContain("CalibrationAssets");
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

    private static string ReadRepoText(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
