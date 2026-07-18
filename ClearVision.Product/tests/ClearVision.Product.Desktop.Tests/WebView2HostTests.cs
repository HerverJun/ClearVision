using ClearVision.Product.Desktop;
using ClearVision.Product.Desktop.Configuration;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public class WebView2HostTests
{
    [Theory]
    [InlineData(5000, "http://localhost:5000/index.html")]
    [InlineData(5010, "http://localhost:5010/index.html")]
    public void CreateInitialPageUri_ShouldUseEmbeddedLocalhostOrigin(int port, string expected)
    {
        WebView2Host.CreateInitialPageUri(port).ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void CreateInitialPageUri_ShouldRejectInvalidPorts(int port)
    {
        var act = () => WebView2Host.CreateInitialPageUri(port);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CreateStartupPlan_WhenStudioUiIsEnabled_ShouldUseExactV1ContractAndStudioUri()
    {
        var tempRoot = CreateCompleteStudioUiRoot();
        try
        {
            var options = new StudioOptions
            {
                StudioUiEnabled = true,
                WorkspaceCapabilityEnabled = true,
                NodePreviewInspectorEnabled = true,
                PropertyPanelCapabilityEnabled = true
            };

            var plan = WebView2Host.CreateStartupPlan(
                webPort: 5000,
                studioOptions: options,
                cssVersion: "123",
                studioUiWebRoot: tempRoot,
                startupProfile: StudioStartupProfileCatalog.NextPilot,
                sourceSha: "1111111111111111111111111111111111111111",
                authMode: "harness-seeded-session");

            plan.Decision.Kind.Should().Be(StudioStartupPageKind.StudioUi);
            plan.InitialPageUri.Should().Be(new Uri("http://localhost:5000/studio/index.html"));
            plan.DiagnosticHtml.Should().BeNull();
            plan.StartupInjectionScript.Should().NotBeNull();
            plan.StartupInjectionScript.Should().Contain("\"schemaVersion\":1");
            plan.StartupInjectionScript.Should().Contain("\"uiKind\":\"studio-ui\"");
            plan.StartupInjectionScript.Should().Contain("\"hostKind\":\"desktop-webview2\"");
            plan.StartupInjectionScript.Should().Contain("\"apiBaseUrl\":\"http://localhost:5000/api\"");
            plan.StartupInjectionScript.Should().Contain("\"studioUiBasePath\":\"/studio/\"");
            plan.StartupInjectionScript.Should().Contain("\"featureFlags\":");
            plan.StartupInjectionScript.Should().Contain("\"Studio2.Workspace\":true");
            plan.StartupInjectionScript.Should().Contain("Object.freeze(startup)");
            plan.StartupInjectionScript.Should().Contain("writable: false");
            plan.StartupInjectionScript.Should().Contain("configurable: false");
            plan.StartupInjectionScript.Should().NotContain("window.__API_BASE_URL__");
            plan.StartupInjectionScript.Should().NotContain("window.__CSS_VERSION__");
            plan.StartupInjectionScript.Should().NotContain("\"nodePreviewInspectorEnabled\":");
            plan.Diagnostics.Profile.Should().Be(StudioStartupProfileCatalog.NextPilot);
            plan.Diagnostics.PageKind.Should().Be(nameof(StudioStartupPageKind.StudioUi));
            plan.Diagnostics.InitialPageUri.Should().Be("http://localhost:5000/studio/index.html");
            plan.Diagnostics.AssetRoot.Should().Be(Path.GetFullPath(tempRoot));
            plan.Diagnostics.SourceSha.Should().Be("1111111111111111111111111111111111111111");
            plan.Diagnostics.AuthMode.Should().Be("HARNESS-SEEDED-SESSION");
            plan.Diagnostics.ConfigurationRequiresRestart.Should().BeTrue();
            plan.Diagnostics.Flags["Studio:StudioUiEnabled"].Should().BeTrue();
            plan.Diagnostics.Flags["Studio:WorkspaceCapabilityEnabled"].Should().BeTrue();
            plan.Diagnostics.Flags["Studio2.Workspace"].Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, null, StudioStartupProfileCatalog.LegacyDefault)]
    [InlineData(true, true, null, StudioStartupProfileCatalog.NextFullCandidate)]
    [InlineData(false, true, null, StudioStartupProfileCatalog.IsolatedTruthTable)]
    [InlineData(true, false, null, StudioStartupProfileCatalog.IsolatedTruthTable)]
    [InlineData(false, false, StudioStartupProfileCatalog.LegacyDefault, StudioStartupProfileCatalog.LegacyDefault)]
    [InlineData(true, true, StudioStartupProfileCatalog.NextPilot, StudioStartupProfileCatalog.NextPilot)]
    [InlineData(true, true, StudioStartupProfileCatalog.NextFullCandidate, StudioStartupProfileCatalog.NextFullCandidate)]
    public void StudioStartupProfileCatalog_ShouldFreezeNamedProfilesAndTruthTableLabels(
        bool studioUiEnabled,
        bool workspaceEnabled,
        string? requestedProfile,
        string expectedProfile)
    {
        var options = new StudioOptions
        {
            StudioUiEnabled = studioUiEnabled,
            WorkspaceCapabilityEnabled = workspaceEnabled
        };

        StudioStartupProfileCatalog.Resolve(options, requestedProfile)
            .Should()
            .Be(expectedProfile);
    }

    [Theory]
    [InlineData(StudioStartupProfileCatalog.LegacyDefault, true, true)]
    [InlineData(StudioStartupProfileCatalog.NextPilot, true, false)]
    [InlineData(StudioStartupProfileCatalog.NextFullCandidate, false, false)]
    [InlineData("UNKNOWN_PROFILE", false, false)]
    public void StudioStartupProfileCatalog_ShouldRejectMislabelledFlagCombinations(
        string requestedProfile,
        bool studioUiEnabled,
        bool workspaceEnabled)
    {
        var options = new StudioOptions
        {
            StudioUiEnabled = studioUiEnabled,
            WorkspaceCapabilityEnabled = workspaceEnabled
        };

        var act = () => StudioStartupProfileCatalog.Resolve(options, requestedProfile);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildStudioUiStartupInjectionScript_ShouldKeepWorkspaceCapabilityDefaultOff()
    {
        var script = WebView2Host.BuildStudioUiStartupInjectionScript(
            "http://localhost:5000/api",
            new StudioOptions());

        script.Should().Contain("\"Studio2.Workspace\":false");
        script.Should().Contain("Object.freeze(startup)");
        script.Should().NotContain("window.__API_BASE_URL__");
    }

    [Fact]
    public void CreateStartupPlan_WhenStudioUiIsDisabled_ShouldKeepLegacyStartupAndAliases()
    {
        var legacyRoot = Path.Combine(
            Path.GetTempPath(),
            "clearvision-webview-plan-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy");
        try
        {
            var plan = WebView2Host.CreateStartupPlan(
                webPort: 5000,
                studioOptions: new StudioOptions { StudioUiEnabled = false },
                cssVersion: "123",
                legacyWebRoot: legacyRoot);

            plan.Decision.Kind.Should().Be(StudioStartupPageKind.Legacy);
            plan.InitialPageUri.Should().Be(new Uri("http://localhost:5000/index.html"));
            plan.DiagnosticHtml.Should().BeNull();
            plan.StartupInjectionScript.Should().Contain("window.__API_BASE_URL__");
            plan.StartupInjectionScript.Should().Contain("window.__CSS_VERSION__");
            plan.StartupInjectionScript.Should().Contain("\"nodePreviewInspectorEnabled\":false");
            plan.StartupInjectionScript.Should().NotContain("\"schemaVersion\":1");
            plan.StartupInjectionScript.Should().NotContain("\"uiKind\":\"studio-ui\"");
        }
        finally
        {
            Directory.Delete(legacyRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateStartupPlan_WhenStudioUiAssetsAreMissing_ShouldUseDiagnosticWithoutInjection()
    {
        var studioUiRoot = Path.Combine(
            Path.GetTempPath(),
            "clearvision-webview-plan-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(studioUiRoot);
        try
        {
            var plan = WebView2Host.CreateStartupPlan(
                webPort: 5000,
                studioOptions: new StudioOptions { StudioUiEnabled = true },
                cssVersion: "123",
                studioUiWebRoot: studioUiRoot);

            plan.Decision.Kind.Should().Be(StudioStartupPageKind.Diagnostic);
            plan.InitialPageUri.Should().BeNull();
            plan.StartupInjectionScript.Should().BeNull();
            plan.DiagnosticHtml.Should().Contain("不会回退 Legacy");
            plan.DiagnosticHtml.Should().Contain(Path.Combine(studioUiRoot, "index.html"));
            plan.DiagnosticHtml.Should().Contain(Path.Combine(studioUiRoot, "assets"));
            plan.DiagnosticHtml.Should().Contain(Path.Combine(studioUiRoot, ".vite", "manifest.json"));
        }
        finally
        {
            Directory.Delete(studioUiRoot, recursive: true);
        }
    }

    [Fact]
    public void BuildStartupInjectionScript_ShouldDeepFreezeStartupFeatureFlagsAndExposeReadOnlyWindowProperty()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: true,
            propertyPanelCapabilityEnabled: true,
            previewPanelCapabilityEnabled: true,
            globalVariablesCapabilityEnabled: true,
            settingsCapabilityEnabled: true,
            projectPageCapabilityEnabled: true,
            inspectionCapabilityEnabled: true,
            resultsReviewCapabilityEnabled: true,
            aiPanelCapabilityEnabled: true);

        script.Should().Contain("const featureFlags = Object.freeze");
        script.Should().Contain("Object.defineProperty(startup, 'featureFlags'");
        script.Should().Contain("Object.freeze(startup)");
        script.Should().Contain("__CLEARVISION_STARTUP__");
        script.Should().Contain("writable: false");
        script.Should().Contain("configurable: false");
        script.Should().Contain("\"nodePreviewInspectorEnabled\":true");
        script.Should().Contain("\"Studio:NodePreviewInspectorEnabled\":true");
        script.Should().Contain("\"Studio2.PropertyPanel\":true");
        script.Should().Contain("\"Studio2.PreviewPanel\":true");
        script.Should().Contain("\"Studio2.GlobalVariables\":true");
        script.Should().Contain("\"Studio2.Settings\":true");
        script.Should().Contain("\"Studio2.ProjectPage\":true");
        script.Should().Contain("\"Studio2.Inspection\":true");
        script.Should().Contain("\"Studio2.ResultsReview\":true");
        script.Should().Contain("\"Studio2.AiPanel\":true");
        script.Should().Contain("\"Studio:CircleSearchV2ToolEnabled\":true");
        script.Should().Contain("\"Studio:NPointCalibrationWorkbenchEnabled\":true");
        script.Should().Contain("\"apiBaseUrl\":\"http://localhost:5000/api\"");
        script.Should().Contain("\"hostKind\":\"desktop-webview2\"");
        script.Should().NotContain("workspaceV2Enabled");
        script.Should().NotContain("frontendV2BasePath");
        script.Should().Contain("window.__API_BASE_URL__ = \"http://localhost:5000/api\"");
    }

    [Fact]
    public void BuildStartupInjectionScript_ShouldKeepNodePreviewFlagCanonicalInFeatureFlags()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: false);

        script.Should().Contain("\"nodePreviewInspectorEnabled\":false");
        script.Should().Contain("\"Studio:NodePreviewInspectorEnabled\":false");
        script.Should().Contain("\"Studio2.PropertyPanel\":false");
        script.Should().Contain("\"Studio2.PreviewPanel\":false");
        script.Should().Contain("\"Studio2.GlobalVariables\":false");
        script.Should().Contain("\"Studio2.Settings\":false");
        script.Should().Contain("\"Studio2.ProjectPage\":false");
        script.Should().Contain("\"Studio2.Inspection\":false");
        script.Should().Contain("\"Studio2.ResultsReview\":false");
        script.Should().Contain("\"Studio2.AiPanel\":false");
        script.Should().Contain("\"Studio:CircleSearchV2ToolEnabled\":true");
        script.Should().Contain("\"Studio:NPointCalibrationWorkbenchEnabled\":true");
        script.Should().Contain("Object.defineProperty(startup, 'featureFlags'");
        script.Should().Contain("writable: false");
        script.Should().Contain("configurable: false");
    }

    [Fact]
    public void BuildStartupInjectionScript_ShouldExposeCircleSearchV2ToolFlag()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: false,
            circleSearchV2ToolEnabled: false,
            nPointCalibrationWorkbenchEnabled: false);

        script.Should().Contain("\"Studio:CircleSearchV2ToolEnabled\":false");
        script.Should().Contain("\"Studio:NPointCalibrationWorkbenchEnabled\":false");
        script.Should().Contain("Object.freeze(startup)");
    }

    [Fact]
    public void BuildAiWorkspaceFlushStartScript_ShouldStartPromiseAndReturnSynchronously()
    {
        var script = MainForm.BuildAiWorkspaceFlushStartScript("op-1", "host_close");

        script.Should().Contain("window.__clearVisionFlushProjectWorkspace");
        script.Should().Contain("window.__clearVisionFlushAiPanelWorkspace");
        script.Should().Contain("Promise.all(flushers.map(flush => Promise.resolve(flush(reason))))");
        script.Should().Contain("values.every(value => value === true)");
        script.Should().Contain("return true;");
        script.Should().NotContain("async()=>");
        script.Should().NotContain("await (window.__clearVisionFlushAiPanelWorkspace");
        script.Should().Contain("__clearVisionAiWorkspaceFlushResults");
    }

    [Theory]
    [InlineData("{\"status\":\"pending\",\"value\":false}", false, false)]
    [InlineData("{\"status\":\"completed\",\"value\":true}", true, true)]
    [InlineData("{\"status\":\"completed\",\"value\":false}", true, false)]
    [InlineData("{\"status\":\"failed\",\"value\":false}", true, false)]
    [InlineData("{\"status\":\"missing\",\"value\":false}", true, false)]
    [InlineData("{}", true, false)]
    [InlineData("true", true, true)]
    [InlineData("false", true, false)]
    [InlineData("null", true, false)]
    public void ParseAiWorkspaceFlushStatus_ShouldClassifyTerminalState(
        string rawResult,
        bool expectedTerminal,
        bool expectedSucceeded)
    {
        var status = MainForm.ParseAiWorkspaceFlushStatus(rawResult);

        status.IsTerminal.Should().Be(expectedTerminal);
        status.Succeeded.Should().Be(expectedSucceeded);
    }

    private static string CreateCompleteStudioUiRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "clearvision-webview-plan-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Directory.CreateDirectory(Path.Combine(root, ".vite"));
        File.WriteAllText(Path.Combine(root, "index.html"), "studio");
        File.WriteAllText(Path.Combine(root, "assets", "app-12345678.js"), "asset");
        File.WriteAllText(Path.Combine(root, ".vite", "manifest.json"), "{}");
        return root;
    }
}
