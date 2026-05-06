using Acme.Product.Desktop;
using FluentAssertions;

namespace Acme.Product.Desktop.Tests;

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
}
