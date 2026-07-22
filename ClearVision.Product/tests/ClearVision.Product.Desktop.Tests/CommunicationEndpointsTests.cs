using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.Communication.Gr;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]
public sealed class CommunicationEndpointsTests
{
    [Theory]
    [InlineData("WriteSingle")]
    [InlineData("WriteMultiple")]
    public async Task Diagnostics_WriteFunction_ShouldBeRejectedBeforeConnecting(string functionCode)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var host = await CommunicationEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/communication/diagnostics/execute",
            JsonContent(new
            {
                operation = "ReadOnce",
                host = "127.0.0.1",
                port,
                unitId = 1,
                functionCode
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("COMMUNICATION_WRITE_BLOCKED");
        listener.Pending().Should().BeFalse("diagnostic writes must be rejected before network access");
    }

    [Fact]
    public async Task Diagnostics_UnknownOperation_ShouldBeRejectedBeforeConnecting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var host = await CommunicationEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/communication/diagnostics/execute",
            JsonContent(new
            {
                operation = "StartRobot",
                host = "127.0.0.1",
                port,
                unitId = 1
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("COMMUNICATION_DIAGNOSTIC_OPERATION_BLOCKED");
        listener.Pending().Should().BeFalse("unknown operations must be rejected before network access");
    }

    [Fact]
    public async Task GrTemplateAndProfileEndpoints_ShouldExposeReadOnlyMetadata()
    {
        await using var host = await CommunicationEndpointTestHost.CreateAsync();

        using var templateResponse = await host.Client.GetAsync("/api/communication/templates/gr");
        templateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var templateDocument = JsonDocument.Parse(await templateResponse.Content.ReadAsStringAsync());
        var template = templateDocument.RootElement;
        var templateId = template.GetProperty("templateId").GetString();
        var version = template.GetProperty("version").GetString();
        var hash = template.GetProperty("sha256").GetString();
        template.GetProperty("writePolicy").GetProperty("enabledByDefault").GetBoolean().Should().BeFalse();
        template.GetProperty("writePolicy").GetProperty("allowedAddresses").GetArrayLength().Should().Be(0);

        using var saveResponse = await host.Client.PutAsync(
            "/api/communication/profiles/gr-test",
            JsonContent(new
            {
                name = "GR read-only",
                host = "172.16.87.12",
                port = 502,
                unitId = 255,
                templateId,
                templateVersion = "untrusted-version",
                templateHash = "untrusted-hash"
            }));

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var saveDocument = JsonDocument.Parse(await saveResponse.Content.ReadAsStringAsync());
        var saved = saveDocument.RootElement;
        saved.GetProperty("readOnly").GetBoolean().Should().BeTrue();
        saved.GetProperty("templateVersion").GetString().Should().Be(version);
        saved.GetProperty("templateHash").GetString().Should().Be(hash);

        using var profilesResponse = await host.Client.GetAsync("/api/communication/profiles");
        profilesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var profilesDocument = JsonDocument.Parse(await profilesResponse.Content.ReadAsStringAsync());
        var profile = profilesDocument.RootElement.GetProperty("profiles").EnumerateArray().Single();
        profile.GetProperty("id").GetString().Should().Be("gr-test");
        profile.GetProperty("readOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Diagnostics_ReadFunction_ShouldBeCaseInsensitive()
    {
        await using var host = await CommunicationEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/communication/diagnostics/execute",
            JsonContent(new
            {
                operation = "ReadOnce",
                host = "127.0.0.1",
                port = 1,
                unitId = 1,
                functionCode = "readholding",
                timeoutMs = 100
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("COMMUNICATION_READ_FAILED");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private sealed class CommunicationEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _root;

        private CommunicationEndpointTestHost(WebApplication app, string root)
        {
            _app = app;
            _root = root;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<CommunicationEndpointTestHost> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "clearvision-communication-endpoint-tests",
                Guid.NewGuid().ToString("N"));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            var store = new JsonCommunicationProfileStore(Path.Combine(root, "modbus-profiles.json"));
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<GrRegisterMapCatalog>();
            builder.Services.AddSingleton<GrStateDecoder>();
            builder.Services.AddSingleton(new ModbusCommunicationOperator(
                NullLogger<ModbusCommunicationOperator>.Instance,
                store));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new ClearVision.Product.Application.Services.UserSession
                {
                    UserId = "engineer",
                    Username = "engineer",
                    Role = "Engineer"
                };
                await next();
            });
            app.MapCommunicationEndpoints();
            await app.StartAsync();
            return new CommunicationEndpointTestHost(app, root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
