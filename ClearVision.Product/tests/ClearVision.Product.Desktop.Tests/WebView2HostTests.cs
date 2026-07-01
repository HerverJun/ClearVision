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
    public void BuildStartupInjectionScript_ShouldFreezeSerializedStartupObject()
    {
        var script = WebView2Host.BuildStartupInjectionScript(
            workspaceV2Enabled: true,
            apiBaseUrl: "http://localhost:5000/api",
            cssVersion: "123");

        script.Should().Contain("Object.freeze");
        script.Should().Contain("__CLEARVISION_STARTUP__");
        script.Should().Contain("\"workspaceV2Enabled\":true");
        script.Should().Contain("\"apiBaseUrl\":\"http://localhost:5000/api\"");
        script.Should().Contain("\"hostKind\":\"desktop-webview2\"");
        script.Should().Contain("\"frontendV2BasePath\":\"/v2\"");
        script.Should().Contain("window.__API_BASE_URL__ = \"http://localhost:5000/api\"");
    }
}
