using ClearVision.Product.Desktop;
using ClearVision.Product.Desktop.Configuration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            plan.StartupInjectionScript.Should().Contain("\"startupProfile\":\"NEXT_PILOT\"");
            plan.StartupInjectionScript.Should().Contain("\"profileAllowedRoles\":[\"Admin\"]");
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
    [InlineData(true, true, StudioStartupProfileCatalog.LegacyFallback, StudioStartupProfileCatalog.LegacyFallback)]
    [InlineData(false, false, StudioStartupProfileCatalog.NextInternalPilot, StudioStartupProfileCatalog.NextInternalPilot)]
    [InlineData(false, false, StudioStartupProfileCatalog.NextEngineerPilot, StudioStartupProfileCatalog.NextEngineerPilot)]
    [InlineData(true, true, StudioStartupProfileCatalog.NextOperatorPilot, StudioStartupProfileCatalog.NextOperatorPilot)]
    [InlineData(false, false, StudioStartupProfileCatalog.NextDefaultCandidate, StudioStartupProfileCatalog.NextDefaultCandidate)]
    [InlineData(false, false, StudioStartupProfileCatalog.NextDefault, StudioStartupProfileCatalog.NextDefault)]
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
    [InlineData("UNKNOWN_PROFILE")]
    [InlineData(StudioStartupProfileCatalog.IsolatedTruthTable)]
    [InlineData("")]
    [InlineData("   ")]
    public void StudioStartupProfileCatalog_ShouldRejectUnknownConfiguredProfiles(
        string requestedProfile)
    {
        var options = new StudioOptions();

        var act = () => StudioStartupProfileCatalog.Resolve(options, requestedProfile);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StudioStartupProfileCatalog_ShouldPreferExplicitRollbackOverConfiguredDefault()
    {
        var configured = new StudioOptions
        {
            StartupProfile = StudioStartupProfileCatalog.NextDefault,
            StudioUiEnabled = true,
            WorkspaceCapabilityEnabled = true,
            SettingsCapabilityEnabled = true,
            InspectionRunCapabilityEnabled = true,
            AiPanelCapabilityEnabled = true,
            AiWorkbenchCapabilityEnabled = true
        };

        StudioStartupProfileCatalog.Resolve(configured)
            .Should()
            .Be(StudioStartupProfileCatalog.NextDefault);
        StudioStartupProfileCatalog.Resolve(configured, StudioStartupProfileCatalog.LegacyFallback)
            .Should()
            .Be(StudioStartupProfileCatalog.LegacyFallback);

        var fallback = StudioStartupProfileCatalog.CreateEffectiveOptions(
            configured,
            StudioStartupProfileCatalog.LegacyFallback);
        fallback.StudioUiEnabled.Should().BeFalse();
        fallback.WorkspaceCapabilityEnabled.Should().BeFalse();
        fallback.SettingsCapabilityEnabled.Should().BeTrue();
        fallback.InspectionRunCapabilityEnabled.Should().BeTrue();
        fallback.AiPanelCapabilityEnabled.Should().BeTrue();
        fallback.AiWorkbenchCapabilityEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(StudioStartupProfileCatalog.NextInternalPilot, true, true)]
    [InlineData(StudioStartupProfileCatalog.NextEngineerPilot, true, true)]
    [InlineData(StudioStartupProfileCatalog.NextOperatorPilot, true, false)]
    [InlineData(StudioStartupProfileCatalog.NextDefaultCandidate, true, true)]
    [InlineData(StudioStartupProfileCatalog.NextDefault, true, true)]
    [InlineData(StudioStartupProfileCatalog.LegacyDefault, false, false)]
    [InlineData(StudioStartupProfileCatalog.LegacyFallback, false, false)]
    public void StudioStartupProfileCatalog_ShouldProjectNamedEntryAndWorkspaceCapabilities(
        string profile,
        bool expectedStudioUiEnabled,
        bool expectedWorkspaceEnabled)
    {
        var effective = StudioStartupProfileCatalog.CreateEffectiveOptions(
            new StudioOptions
            {
                StudioUiEnabled = true,
                WorkspaceCapabilityEnabled = true
            },
            profile);

        effective.StudioUiEnabled.Should().Be(expectedStudioUiEnabled);
        effective.WorkspaceCapabilityEnabled.Should().Be(expectedWorkspaceEnabled);
    }

    [Fact]
    public void StudioStartupProfileCatalog_ShouldProjectPilotRoleAndCapabilityRestrictions()
    {
        var configured = new StudioOptions
        {
            SettingsCapabilityEnabled = true,
            InspectionRunCapabilityEnabled = true,
            AiPanelCapabilityEnabled = true,
            AiWorkbenchCapabilityEnabled = true
        };

        var internalPilot = StudioStartupProfileCatalog.CreateEffectiveOptions(
            configured,
            StudioStartupProfileCatalog.NextInternalPilot);
        StudioStartupProfileCatalog.AllowedRolesFor(StudioStartupProfileCatalog.NextInternalPilot)
            .Should().Equal("Admin");
        internalPilot.WorkspaceCapabilityEnabled.Should().BeTrue();
        internalPilot.SettingsCapabilityEnabled.Should().BeTrue();
        internalPilot.InspectionRunCapabilityEnabled.Should().BeTrue();
        internalPilot.AiPanelCapabilityEnabled.Should().BeTrue();
        internalPilot.AiWorkbenchCapabilityEnabled.Should().BeTrue();

        var operatorPilot = StudioStartupProfileCatalog.CreateEffectiveOptions(
            configured,
            StudioStartupProfileCatalog.NextOperatorPilot);
        StudioStartupProfileCatalog.AllowedRolesFor(StudioStartupProfileCatalog.NextOperatorPilot)
            .Should().Equal("Operator");
        operatorPilot.StudioUiEnabled.Should().BeTrue();
        operatorPilot.WorkspaceCapabilityEnabled.Should().BeFalse();
        operatorPilot.SettingsCapabilityEnabled.Should().BeFalse();
        operatorPilot.InspectionRunCapabilityEnabled.Should().BeFalse();
        operatorPilot.AiPanelCapabilityEnabled.Should().BeFalse();
        operatorPilot.AiWorkbenchCapabilityEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(StudioStartupProfileCatalog.NextDefaultCandidate, true)]
    [InlineData(StudioStartupProfileCatalog.LegacyFallback, true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("UNKNOWN_PROFILE", false)]
    [InlineData(StudioStartupProfileCatalog.IsolatedTruthTable, false)]
    public void StudioOptionsValidator_ShouldAcceptOnlyKnownConfiguredProfiles(
        string? startupProfile,
        bool expectedSuccess)
    {
        var result = new StudioOptionsValidator().Validate(
            name: null,
            new StudioOptions { StartupProfile = startupProfile });

        result.Succeeded.Should().Be(expectedSuccess);
    }

    [Fact]
    public void RequireValidStudioOptions_ShouldRejectInvalidProfileBeforeStartupRecovery()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<StudioOptions>, StudioOptionsValidator>();
        services.AddOptions<StudioOptions>().Configure(options => options.StartupProfile = "UNKNOWN_PROFILE");
        using var provider = services.BuildServiceProvider();

        var act = () => Program.RequireValidStudioOptions(provider);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*UNKNOWN_PROFILE*");
    }

    [Fact]
    public void CreateStartupPlan_WhenLegacyFallbackOverridesNextDefault_ShouldUseLegacyRoot()
    {
        var legacyRoot = Path.Combine(Path.GetTempPath(), "clearvision-profile-fallback", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "index.html"), "legacy");
        try
        {
            var plan = WebView2Host.CreateStartupPlan(
                webPort: 5000,
                studioOptions: new StudioOptions
                {
                    StartupProfile = StudioStartupProfileCatalog.NextDefault,
                    StudioUiEnabled = true,
                    WorkspaceCapabilityEnabled = true,
                    SettingsCapabilityEnabled = true,
                    InspectionCapabilityEnabled = true,
                    AiPanelCapabilityEnabled = true,
                    AiWorkbenchCapabilityEnabled = true
                },
                cssVersion: "123",
                legacyWebRoot: legacyRoot,
                startupProfile: StudioStartupProfileCatalog.LegacyFallback);

            plan.Decision.Kind.Should().Be(StudioStartupPageKind.Legacy);
            plan.InitialPageUri.Should().Be(new Uri("http://localhost:5000/index.html"));
            plan.Diagnostics.Profile.Should().Be(StudioStartupProfileCatalog.LegacyFallback);
            plan.Diagnostics.Flags["Studio:StudioUiEnabled"].Should().BeFalse();
            plan.Diagnostics.Flags["Studio:WorkspaceCapabilityEnabled"].Should().BeFalse();
            plan.StartupInjectionScript.Should().Contain("\"Studio2.Settings\":true");
            plan.StartupInjectionScript.Should().Contain("\"Studio2.Inspection\":true");
            plan.StartupInjectionScript.Should().Contain("\"Studio2.AiPanel\":true");
            plan.StartupInjectionScript.Should().Contain("\"Studio2.AiWorkbench\":true");
        }
        finally
        {
            Directory.Delete(legacyRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateStartupPlan_WhenNextDefaultOverridesLegacyRawFlags_ShouldUseStudioUiRoot()
    {
        var studioRoot = CreateCompleteStudioUiRoot();
        try
        {
            var plan = WebView2Host.CreateStartupPlan(
                webPort: 5000,
                studioOptions: new StudioOptions
                {
                    StartupProfile = StudioStartupProfileCatalog.NextDefault,
                    StudioUiEnabled = false,
                    WorkspaceCapabilityEnabled = false
                },
                cssVersion: "123",
                studioUiWebRoot: studioRoot);

            plan.Decision.Kind.Should().Be(StudioStartupPageKind.StudioUi);
            plan.Diagnostics.Profile.Should().Be(StudioStartupProfileCatalog.NextDefault);
            plan.Diagnostics.Flags["Studio:StudioUiEnabled"].Should().BeTrue();
            plan.Diagnostics.Flags["Studio:WorkspaceCapabilityEnabled"].Should().BeTrue();
            plan.StartupInjectionScript.Should().Contain("\"startupProfile\":\"NEXT_DEFAULT\"");
        }
        finally
        {
            Directory.Delete(studioRoot, recursive: true);
        }
    }

    [Fact]
    public void BuildStudioUiStartupInjectionScript_ShouldKeepWorkspaceCapabilityDefaultOff()
    {
        var script = WebView2Host.BuildStudioUiStartupInjectionScript(
            "http://localhost:5000/api",
            new StudioOptions());

        script.Should().Contain("\"Studio2.Workspace\":false");
        script.Should().Contain("\"Studio2.StationsRead\":false");
        script.Should().Contain("\"Studio2.InspectionRun\":false");
        script.Should().Contain("Object.freeze(startup)");
        script.Should().Contain("const profileAllowedRoles = Object.freeze([");
        script.Should().Contain("Object.defineProperty(startup, 'profileAllowedRoles'");
        script.Should().NotContain("window.__API_BASE_URL__");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void BuildStudioUiStartupInjectionScript_ShouldMapF05OptionsOneToOneAndKeepLegacyInspectionIndependent(
        bool stationsReadEnabled,
        bool inspectionRunEnabled,
        bool legacyInspectionEnabled)
    {
        var script = WebView2Host.BuildStudioUiStartupInjectionScript(
            "http://localhost:5000/api",
            new StudioOptions
            {
                StationsReadCapabilityEnabled = stationsReadEnabled,
                InspectionRunCapabilityEnabled = inspectionRunEnabled,
                InspectionCapabilityEnabled = legacyInspectionEnabled
            });

        script.Should().Contain($"\"Studio2.StationsRead\":{stationsReadEnabled.ToString().ToLowerInvariant()}");
        script.Should().Contain($"\"Studio2.InspectionRun\":{inspectionRunEnabled.ToString().ToLowerInvariant()}");
        script.Should().Contain($"\"Studio2.Inspection\":{legacyInspectionEnabled.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public void BuildLegacyStartupInjectionScript_ShouldNotReuseF05StudioUiFlags()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            inspectionCapabilityEnabled: true);

        script.Should().Contain("\"Studio2.Inspection\":true");
        script.Should().NotContain("Studio2.InspectionRun");
        script.Should().NotContain("Studio2.StationsRead");
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
    public void CreateStartupPlan_WhenProfileIsAbsentAndTruthTableIsIsolated_ShouldPreserveRawEntryProjection()
    {
        var studioUiRoot = Path.Combine(
            Path.GetTempPath(),
            "clearvision-webview-plan-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(studioUiRoot);
        File.WriteAllText(Path.Combine(studioUiRoot, "index.html"), "studio-ui");
        var assetsRoot = Path.Combine(studioUiRoot, "assets");
        var viteRoot = Path.Combine(studioUiRoot, ".vite");
        Directory.CreateDirectory(assetsRoot);
        Directory.CreateDirectory(viteRoot);
        File.WriteAllText(Path.Combine(assetsRoot, "app.js"), "export {}; ");
        File.WriteAllText(Path.Combine(viteRoot, "manifest.json"), "{}");

        try
        {
            var plan = WebView2Host.CreateStartupPlan(
                webPort: 5000,
                studioOptions: new StudioOptions
                {
                    StudioUiEnabled = true,
                    WorkspaceCapabilityEnabled = false
                },
                cssVersion: "123",
                studioUiWebRoot: studioUiRoot);

            plan.Decision.Kind.Should().Be(StudioStartupPageKind.StudioUi);
            plan.Diagnostics.Profile.Should().Be(StudioStartupProfileCatalog.IsolatedTruthTable);
            plan.Diagnostics.Flags["Studio:StudioUiEnabled"].Should().BeTrue();
            plan.Diagnostics.Flags["Studio:WorkspaceCapabilityEnabled"].Should().BeFalse();
            plan.StartupInjectionScript.Should().Contain("\"startupProfile\":\"ISOLATED_TRUTH_TABLE\"");
            plan.StartupInjectionScript.Should().Contain("\"profileAllowedRoles\":[\"Admin\",\"Engineer\",\"Operator\"]");
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
        script.Should().Contain(".filter(item => typeof item.flush === 'function')");
        script.Should().Contain("Promise.all(flushers.map(invoke))");
        script.Should().Contain("const workspace = results.find(result => result.name === 'workspace') || skipped;");
        script.Should().Contain("const succeeded = [workspace, ai].every(result =>");
        script.Should().NotContain("values.every(value => value === true)");
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

    [Fact]
    public void ParseAiWorkspaceFlushStatus_ShouldKeepPerStageUnknownAndTimeoutOutcomes()
    {
        var status = MainForm.ParseAiWorkspaceFlushStatus(
            "{\"status\":\"completed\",\"value\":false," +
            "\"workspace\":{\"status\":\"unknown\",\"value\":false}," +
            "\"ai\":{\"status\":\"timeout\",\"value\":false}," +
            "\"error\":\"flush did not settle\"}");

        status.IsTerminal.Should().BeTrue();
        status.Succeeded.Should().BeFalse();
        status.Outcome.Should().Be("failed");
        status.WorkspaceOutcome.Should().Be("unknown");
        status.AiOutcome.Should().Be("timeout");
        status.Error.Should().Be("flush did not settle");
    }

    [Fact]
    public void ParseAiWorkspaceFlushStatus_ShouldClassifyMissingScriptResultAsUnknown()
    {
        var status = MainForm.ParseAiWorkspaceFlushStatus(null);

        status.IsTerminal.Should().BeTrue();
        status.Succeeded.Should().BeFalse();
        status.Outcome.Should().Be("unknown");
        status.WorkspaceOutcome.Should().Be("unknown");
        status.AiOutcome.Should().Be("unknown");
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
