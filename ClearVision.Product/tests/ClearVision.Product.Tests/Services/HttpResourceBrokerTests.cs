using System.Collections.Concurrent;
using System.Net;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public class HttpResourceBrokerTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    [Fact]
    public async Task SendAsync_WhenSchemeIsUnsupported_ShouldRejectBeforeDnsOrHandler()
    {
        var resolver = PublicResolver();
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(resolver, transport);

        var action = () => broker.SendAsync(
            Request("file:///etc/passwd"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_DESTINATION_INVALID");
        resolver.Hosts.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenHostIsNotServerAllowlisted_ShouldRejectBeforeDnsOrHandler()
    {
        var resolver = PublicResolver();
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(
            resolver,
            transport,
            new HttpResourceBrokerOptions { AllowedHosts = ["approved.example"] });

        var action = () => broker.SendAsync(
            Request("https://unapproved.example/api"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_HOST_NOT_ALLOWED");
        resolver.Hosts.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenMethodIsNotAllowed_ShouldRejectBeforeDnsOrHandler()
    {
        var resolver = PublicResolver();
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(resolver, transport);

        var action = () => broker.SendAsync(
            new HttpResourceRequest(
                "https://service.example/api",
                new HttpMethod("CONNECT"),
                null,
                null),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_METHOD_NOT_ALLOWED");
        resolver.Hosts.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenDnsContainsForbiddenAddress_ShouldRejectBeforeHandler()
    {
        var resolver = new FakeDnsResolver((_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                [PublicAddress, IPAddress.Loopback]));
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(resolver, transport);

        var action = () => broker.SendAsync(
            Request("https://mixed.example/api"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_DESTINATION_CIDR_FORBIDDEN");
        resolver.Hosts.Should().ContainSingle().Which.Should().Be("mixed.example");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenRedirectTargetsLoopback_ShouldRejectBeforeSecondHandlerDispatch()
    {
        var resolver = new FakeDnsResolver((host, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                [IPAddress.TryParse(host, out var literal) ? literal : PublicAddress]));
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.Host.Equals("start.example", StringComparison.OrdinalIgnoreCase))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("http://127.0.0.1/private");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(resolver, transport);

        var action = () => broker.SendAsync(
            Request("https://start.example/api"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_DESTINATION_CIDR_FORBIDDEN");
        resolver.Hosts.Should().Equal("start.example", "127.0.0.1");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenEveryRedirectDestinationIsAllowed_ShouldValidateEveryHop()
    {
        var resolver = new FakeDnsResolver((_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([PublicAddress]));
        var observed = new ConcurrentQueue<(string Host, string Method, string? Authorization)>();
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            observed.Enqueue((
                request.RequestUri!.Host,
                request.Method.Method,
                request.Headers.Authorization?.ToString()));
            if (request.RequestUri.Host.Equals("start.example", StringComparison.OrdinalIgnoreCase))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirect.Headers.Location = new Uri("https://next.example/final");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("accepted")
            });
        });
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(
            resolver,
            transport,
            new HttpResourceBrokerOptions
            {
                AllowedHosts = ["start.example", "next.example"],
                AllowedPorts = [443]
            });

        var response = await broker.SendAsync(
            new HttpResourceRequest(
                "https://start.example/api",
                HttpMethod.Post,
                "payload",
                "text/plain",
                new Dictionary<string, string> { ["Authorization"] = "Bearer secret" }),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Body.Should().Be("accepted");
        response.FinalUri.Should().Be(new Uri("https://next.example/final"));
        response.RedirectCount.Should().Be(1);
        resolver.Hosts.Should().Equal("start.example", "next.example");
        handler.CallCount.Should().Be(2);
        observed.Should().Equal(
            ("start.example", "POST", "Bearer secret"),
            ("next.example", "POST", (string?)null));
    }

    [Fact]
    public async Task SendAsync_WhenRedirectUsesUnapprovedPort_ShouldRejectBeforeDnsOrSecondDispatch()
    {
        var resolver = new FakeDnsResolver((_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([PublicAddress]));
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://service.example:8443/internal");
            return Task.FromResult(redirect);
        });
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(
            resolver,
            transport,
            new HttpResourceBrokerOptions { AllowedPorts = [443] });

        var action = () => broker.SendAsync(
            Request("https://service.example/api"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_PORT_NOT_ALLOWED");
        resolver.Hosts.Should().ContainSingle().Which.Should().Be("service.example");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenResponseExceedsBudget_ShouldFailClosed()
    {
        var resolver = PublicResolver();
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("0123456789")
            }));
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(
            resolver,
            transport,
            new HttpResourceBrokerOptions { MaxResponseBodyBytes = 8 });

        var action = () => broker.SendAsync(
            Request("https://service.example/api"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_RESPONSE_BODY_TOO_LARGE");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenCancelledBeforeCall_ShouldNotResolveOrDispatch()
    {
        var resolver = PublicResolver();
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(resolver, transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => broker.SendAsync(
            Request("https://service.example/api"),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        resolver.Hosts.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenCancelledDuringDnsPreflight_ShouldNotDispatchHandler()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new FakeDnsResolver((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<IPAddress>>([PublicAddress]);
        });
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(resolver, transport);

        var action = () => broker.SendAsync(
            Request("https://service.example/api"),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        resolver.Hosts.Should().ContainSingle();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenPrivateCidrIsServerApproved_ShouldPreserveIndustrialHttpIo()
    {
        var resolver = new FakeDnsResolver((_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("10.20.30.40")]));
        var handler = SuccessHandler("industrial-ok");
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(
            resolver,
            transport,
            new HttpResourceBrokerOptions
            {
                AllowedHosts = ["mes.production.local"],
                AllowedPorts = [8443],
                AdditionalAllowedCidrs = ["10.20.0.0/16"]
            });

        var response = await broker.SendAsync(
            Request("https://mes.production.local:8443/report"),
            CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeTrue();
        response.Body.Should().Be("industrial-ok");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenServerCidrIsOverbroad_ShouldStillRejectLoopback()
    {
        var resolver = new FakeDnsResolver((_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]));
        var handler = SuccessHandler();
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var broker = CreateBroker(
            resolver,
            transport,
            new HttpResourceBrokerOptions { AdditionalAllowedCidrs = ["0.0.0.0/0"] });

        var action = () => broker.SendAsync(
            Request("https://service.example/api"),
            CancellationToken.None);

        var failure = await action.Should().ThrowAsync<HttpResourceBrokerException>();
        failure.Which.Code.Should().Be("HTTP_DESTINATION_CIDR_FORBIDDEN");
        handler.CallCount.Should().Be(0);
    }

    private static ServerHttpResourceBroker CreateBroker(
        IHttpResourceDnsResolver resolver,
        IHttpResourceTransport transport,
        HttpResourceBrokerOptions? options = null) =>
        new(
            resolver,
            transport,
            Options.Create(options ?? new HttpResourceBrokerOptions()));

    private static HttpResourceRequest Request(string url) =>
        new(url, HttpMethod.Get, null, null);

    private static FakeDnsResolver PublicResolver() =>
        new((_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([PublicAddress]));

    private static FakeHttpMessageHandler SuccessHandler(string body = "ok") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        }));

    private sealed class FakeDnsResolver(
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> resolveAsync)
        : IHttpResourceDnsResolver
    {
        public ConcurrentQueue<string> Hosts { get; } = new();

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            Hosts.Enqueue(host);
            return resolveAsync(host, cancellationToken);
        }
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return sendAsync(request, cancellationToken);
        }
    }
}
