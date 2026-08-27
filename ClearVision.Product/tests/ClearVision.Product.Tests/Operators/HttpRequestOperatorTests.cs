using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public class HttpRequestOperatorTests
{
    private readonly HttpRequestOperator _operator;

    public HttpRequestOperatorTests()
    {
        _operator = new HttpRequestOperator(Substitute.For<ILogger<HttpRequestOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeHttpRequest()
    {
        _operator.OperatorType.Should().Be(OperatorType.HttpRequest);
    }

    [Fact]
    public void ValidateParameters_WithInvalidMethod_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.HttpRequest, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Url", "http://127.0.0.1:8080/api", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Method", "TRACE", "string"));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalServer_ShouldExposeResponseCompatibilityKeys()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        const string responseBody = "{\"ok\":true}";
        var serverTask = ServeOnceAsync(listener, responseBody);

        var op = new Operator("test", OperatorType.HttpRequest, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Url", $"http://127.0.0.1:{port}/ingest", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Method", "POST", "string"));
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 10000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("RetryCount", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ContentType", "application/json", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Body"] = "{\"job\":\"demo\"}",
            ["Headers"] = new Dictionary<string, object>
            {
                ["X-Correlation-Id"] = "abc-123"
            }
        });

        var request = await serverTask.WaitAsync(TimeSpan.FromSeconds(10));

        result.IsSuccess.Should().BeTrue("operator returned {0}", result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["StatusCode"].Should().Be(200);
        result.OutputData["IsSuccess"].Should().Be(true);
        result.OutputData["IsSuccessStatusCode"].Should().Be(true);
        result.OutputData["Response"].Should().Be(responseBody);
        result.OutputData["ResponseBody"].Should().Be(responseBody);

        request.Method.Should().Be("POST");
        request.Path.Should().Be("/ingest");
        request.Body.Should().Be("{\"job\":\"demo\"}");
        request.Headers["X-Correlation-Id"].Should().Be("abc-123");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ExecuteFlowAsync_WithoutBodyPortValue_ShouldNotSendInjectedParameters(string method)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = ServeOnceAsync(listener, "{\"ok\":true}");
        var flow = new OperatorFlow("http-no-body");
        var op = new Operator("request", OperatorType.HttpRequest, 0, 0);
        op.AddInputPort("Body", PortDataType.String, false);
        op.AddInputPort("Headers", PortDataType.Any, false);
        op.AddOutputPort("Response", PortDataType.String);
        op.AddParameter(TestHelpers.CreateParameter("Url", $"http://127.0.0.1:{port}/no-body", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Method", method, "string"));
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 10000, "int"));
        op.AddParameter(TestHelpers.CreateParameter("RetryCount", 0, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ContentType", "application/json", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RetryDelayMs", 1000, "int"));
        flow.AddOperator(op);
        using var service = new FlowExecutionService(
            [_operator],
            Substitute.For<ILogger<FlowExecutionService>>(),
            Substitute.For<IVariableContext>());

        var result = await service.ExecuteFlowAsync(
            flow,
            new Dictionary<string, object>
            {
                ["Headers"] = new Dictionary<string, object> { ["X-Test"] = "header-only" }
            });
        var request = await serverTask.WaitAsync(TimeSpan.FromSeconds(10));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        request.Method.Should().Be(method);
        request.Body.Should().BeEmpty();
        request.Headers.Should().NotContainKey("Content-Type");
        request.Headers["X-Test"].Should().Be("header-only");
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

    private static async Task<CapturedRequest> ServeOnceAsync(TcpListener listener, string responseBody)
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync();
        requestLine.Should().NotBeNullOrWhiteSpace();

        var requestLineParts = requestLine!.Split(' ');
        var method = requestLineParts[0];
        var path = requestLineParts[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int contentLength = 0;

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            headers[key] = value;

            if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(value);
            }
        }

        string body = string.Empty;
        if (contentLength > 0)
        {
            var buffer = new char[contentLength];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            body = new string(buffer, 0, read);
        }

        var responseBytes = Encoding.UTF8.GetBytes(responseBody);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync("HTTP/1.1 200 OK");
        await writer.WriteLineAsync("Content-Type: application/json; charset=utf-8");
        await writer.WriteLineAsync($"Content-Length: {responseBytes.Length}");
        await writer.WriteLineAsync("Connection: close");
        await writer.WriteLineAsync();
        await writer.WriteAsync(responseBody);
        await writer.FlushAsync();

        return new CapturedRequest(method, path, body, headers);
    }

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string Body,
        Dictionary<string, string> Headers);
}
