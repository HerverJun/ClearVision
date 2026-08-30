using ClearVision.Product.Desktop;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public class WebView2HostTests
{
    [Theory]
    [InlineData(5000)]
    [InlineData(5010)]
    public void CreateInitialPageUri_ShouldUseAuthenticatedAppLocalOrigin(int port)
    {
        WebView2Host.CreateInitialPageUri(port).ToString().Should().Be("https://app.local/index.html");
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
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123",
            nodePreviewInspectorEnabled: true,
            propertyPanelCapabilityEnabled: true,
            previewPanelCapabilityEnabled: true,
            globalVariablesCapabilityEnabled: true,
            projectPageCapabilityEnabled: true,
            resultsReviewCapabilityEnabled: true);

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
        script.Should().Contain("\"Studio2.ProjectPage\":true");
        script.Should().Contain("\"Studio2.ResultsReview\":true");
        script.Should().Contain("\"Studio:CircleSearchV2ToolEnabled\":true");
        script.Should().Contain("\"Studio:NPointCalibrationWorkbenchEnabled\":true");
        script.Should().Contain("\"apiBaseUrl\":\"http://localhost:5000/api\"");
        script.Should().Contain("\"hostKind\":\"desktop-webview2\"");
        script.Should().NotContain("workspaceV2Enabled");
        script.Should().NotContain("frontendV2BasePath");
        script.Should().NotContain("Studio2.Settings");
        script.Should().NotContain("Studio2.Inspection");
        script.Should().NotContain("Studio2.AiPanel");
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
        script.Should().Contain("\"Studio2.ProjectPage\":false");
        script.Should().Contain("\"Studio2.ResultsReview\":false");
        script.Should().NotContain("Studio2.Settings");
        script.Should().NotContain("Studio2.Inspection");
        script.Should().NotContain("Studio2.AiPanel");
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

        script.Should().Contain("Promise.resolve(flush(reason))");
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
}
