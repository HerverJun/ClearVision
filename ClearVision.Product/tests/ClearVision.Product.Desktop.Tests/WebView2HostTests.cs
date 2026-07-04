using ClearVision.Product.Desktop;
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
    [InlineData(false, "http://localhost:5000/index.html")]
    [InlineData(true, "http://localhost:5000/v2/index.html")]
    public void CreateInitialPageUri_ShouldSelectRootFromWorkspaceV2Flag(
        bool workspaceV2Enabled,
        string expected)
    {
        WebView2Host.CreateInitialPageUri(5000, workspaceV2Enabled).ToString().Should().Be(expected);
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
    public void BuildStartupInjectionScript_ShouldDeepFreezeStartupFeatureFlagsAndExposeReadOnlyWindowProperty()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            workspaceV2Enabled: true,
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: true);

        script.Should().Contain("const featureFlags = Object.freeze");
        script.Should().Contain("Object.defineProperty(startup, 'featureFlags'");
        script.Should().Contain("Object.freeze(startup)");
        script.Should().Contain("__CLEARVISION_STARTUP__");
        script.Should().Contain("writable: false");
        script.Should().Contain("configurable: false");
        script.Should().Contain("\"workspaceV2Enabled\":true");
        script.Should().Contain("\"nodePreviewInspectorEnabled\":true");
        script.Should().Contain("\"Studio:NodePreviewInspectorEnabled\":true");
        script.Should().Contain("\"Studio:CircleSearchV2ToolEnabled\":true");
        script.Should().Contain("\"Studio:NPointCalibrationWorkbenchEnabled\":true");
        script.Should().Contain("\"apiBaseUrl\":\"http://localhost:5000/api\"");
        script.Should().Contain("\"hostKind\":\"desktop-webview2\"");
        script.Should().Contain("\"frontendV2BasePath\":\"/v2\"");
        script.Should().Contain("window.__API_BASE_URL__ = \"http://localhost:5000/api\"");
    }

    [Fact]
    public void BuildStartupInjectionScript_ShouldKeepNodePreviewFlagCanonicalInFeatureFlags()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            workspaceV2Enabled: false,
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: false);

        script.Should().Contain("\"nodePreviewInspectorEnabled\":false");
        script.Should().Contain("\"Studio:NodePreviewInspectorEnabled\":false");
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
            workspaceV2Enabled: false,
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: false,
            circleSearchV2ToolEnabled: false,
            nPointCalibrationWorkbenchEnabled: false);

        script.Should().Contain("\"Studio:CircleSearchV2ToolEnabled\":false");
        script.Should().Contain("\"Studio:NPointCalibrationWorkbenchEnabled\":false");
        script.Should().Contain("Object.freeze(startup)");
    }
}
