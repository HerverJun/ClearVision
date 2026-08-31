using System.Net;
using System.Reflection;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public class HttpRequestOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeHttpRequest()
    {
        var executor = new HttpRequestOperator(
            Substitute.For<ILogger<HttpRequestOperator>>(),
            Substitute.For<IHttpResourceBroker>());

        executor.OperatorType.Should().Be(OperatorType.HttpRequest);
    }

    [Fact]
    public void ValidateParameters_WithInvalidMethod_ShouldReturnInvalid()
    {
        var executor = CreateWithoutTransport();
        var op = new Operator("test", OperatorType.HttpRequest, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Url", "https://service.example/api", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Method", "TRACE", "string"));

        executor.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://service.example/file")]
    [InlineData("https://user:secret@service.example/api")]
    [InlineData("not-an-absolute-uri")]
    public void ValidateParameters_WithUnsafeDestination_ShouldReturnInvalid(string url)
    {
        var executor = CreateWithoutTransport();
        var op = new Operator("test", OperatorType.HttpRequest, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Url", url, "string"));
        op.AddParameter(TestHelpers.CreateParameter("Method", "GET", "string"));

        executor.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithBrokerTransport_ShouldExposeResponseCompatibilityKeys()
    {
        CapturedRequest? captured = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            captured = new CapturedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            };
        });
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var executor = CreateWithTransport(transport);

        var op = CreateOperator("POST", "https://service.example/ingest");
        var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Body"] = "{\"job\":\"demo\"}",
            ["Headers"] = new Dictionary<string, object>
            {
                ["X-Correlation-Id"] = "abc-123"
            }
        });

        result.IsSuccess.Should().BeTrue("operator returned {0}", result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["StatusCode"].Should().Be(200);
        result.OutputData["IsSuccess"].Should().Be(true);
        result.OutputData["IsSuccessStatusCode"].Should().Be(true);
        result.OutputData["Response"].Should().Be("{\"ok\":true}");
        result.OutputData["ResponseBody"].Should().Be("{\"ok\":true}");
        captured.Should().NotBeNull();
        captured!.Method.Should().Be("POST");
        captured.Path.Should().Be("/ingest");
        captured.Body.Should().Be("{\"job\":\"demo\"}");
        captured.Headers["X-Correlation-Id"].Should().Be("abc-123");
        handler.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ExecuteFlowAsync_WithoutBodyPortValue_ShouldNotSendInjectedParameters(string method)
    {
        CapturedRequest? captured = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            captured = new CapturedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            };
        });
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var executor = CreateWithTransport(transport);
        var flow = new OperatorFlow("http-no-body");
        var op = CreateOperator(method, "https://service.example/no-body");
        op.AddInputPort("Body", PortDataType.String, false);
        op.AddInputPort("Headers", PortDataType.Any, false);
        op.AddOutputPort("Response", PortDataType.String);
        flow.AddOperator(op);
        using var service = new FlowExecutionService(
            [executor],
            Substitute.For<ILogger<FlowExecutionService>>(),
            Substitute.For<IVariableContext>());

        var result = await service.ExecuteFlowAsync(
            flow,
            new Dictionary<string, object>
            {
                ["Headers"] = new Dictionary<string, object> { ["X-Test"] = "header-only" }
            });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(method);
        captured.Body.Should().BeEmpty();
        captured.Headers.Should().NotContainKey("Content-Type");
        captured.Headers["X-Test"].Should().Be("header-only");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_ShouldNotDispatchHandler()
    {
        var handler = new CaptureHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var transport = new HttpMessageHandlerResourceTransport(handler);
        var executor = CreateWithTransport(transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => executor.ExecuteAsync(
            CreateOperator("POST", "https://service.example/ingest"),
            cancellationToken: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("OPTIONS", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    public void AutomaticRetryPolicy_ShouldOnlyRetrySafeMethods(string method, bool expected)
    {
        var helper = typeof(HttpRequestOperator).GetMethod(
            "IsAutomaticRetryAllowed",
            BindingFlags.NonPublic | BindingFlags.Static);

        helper.Should().NotBeNull();
        var actual = (bool)helper!.Invoke(null, new object[] { method })!;
        actual.Should().Be(expected);
    }

    private static HttpRequestOperator CreateWithoutTransport() => new(
        Substitute.For<ILogger<HttpRequestOperator>>(),
        Substitute.For<IHttpResourceBroker>());

    private static HttpRequestOperator CreateWithTransport(IHttpResourceTransport transport)
    {
        var resolver = new FixedDnsResolver(IPAddress.Parse("93.184.216.34"));
        var broker = new ServerHttpResourceBroker(
            resolver,
            transport,
            Options.Create(new HttpResourceBrokerOptions()));
        return new HttpRequestOperator(
            Substitute.For<ILogger<HttpRequestOperator>>(),
            broker);
    }

    private static Operator CreateOperator(string method, string url)
    {
        var op = new Operator("request", OperatorType.HttpRequest, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Url", url, "string"));
        op.AddParameter(TestHelpers.CreateParameter("Method", method, "string"));
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 10000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("RetryCount", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ContentType", "application/json", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RetryDelayMs", 1000, "int"));
        return op;
    }

    private sealed class FixedDnsResolver(params IPAddress[] addresses) : IHttpResourceDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IPAddress>>(addresses);
        }
    }

    private sealed class CaptureHandler(
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

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string Body,
        Dictionary<string, string> Headers);
}
