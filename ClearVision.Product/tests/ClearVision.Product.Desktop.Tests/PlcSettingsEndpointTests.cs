using System.Net;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public class PlcSettingsEndpointTests
{
    [Fact]
    public async Task GetPlcSettings_ShouldNormalizeLegacyFlatCommunicationConfig()
    {
        var legacyConfig = new AppConfig
        {
            Communication = new CommunicationConfig
            {
                Protocol = "MC",
                PlcIpAddress = "192.168.3.9",
                PlcPort = 5002,
                Mappings = new List<PlcAddressMapping>
                {
                    new() { Name = "Trigger", Address = "D100", DataType = "Word", CanWrite = false }
                }
            }
        };

        await using var host = await PlcSettingsTestHost.CreateAsync(legacyConfig);
        using var response = await host.Client.GetAsync("/api/plc/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var settings = document.RootElement.GetProperty("settings");
        settings.GetProperty("activeProtocol").GetString().Should().Be("MC");
        settings.GetProperty("mc").GetProperty("ipAddress").GetString().Should().Be("192.168.3.9");
        settings.GetProperty("mc").GetProperty("port").GetInt32().Should().Be(5002);
        settings.GetProperty("mc").GetProperty("mappings").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PutPlcSettings_ShouldRejectInvalidMappingsWithoutPersisting()
    {
        await using var host = await PlcSettingsTestHost.CreateAsync(new AppConfig());
        var payload = new
        {
            expectedRevision = host.ConfigurationService.GetCurrent().Revision,
            activeProtocol = "S7",
            heartbeatIntervalMs = 1000,
            s7 = new
            {
                ipAddress = "192.168.0.10",
                port = 102,
                cpuType = "S7-1200",
                rack = 0,
                slot = 1,
                mappings = new[]
                {
                    new { name = "Start", address = "BAD", dataType = "Bool", description = "", canWrite = false },
                    new { name = "Start", address = "DB1.DBX0.0", dataType = "Int16", description = "", canWrite = false }
                }
            },
            mc = new
            {
                ipAddress = "192.168.3.1",
                port = 5002,
                mappings = Array.Empty<object>()
            },
            fins = new
            {
                ipAddress = "192.168.250.1",
                port = 9600,
                mappings = Array.Empty<object>()
            }
        };

        using var response = await host.Client.PutAsync(
            "/api/plc/settings",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        host.ConfigurationService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task PutPlcSettings_ShouldRejectNonAdminUser()
    {
        await using var host = await PlcSettingsTestHost.CreateAsync(new AppConfig(), role: "Operator");

        using var response = await host.Client.PutAsync(
            "/api/plc/settings",
            new StringContent("{\"expectedRevision\":0}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.ConfigurationService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task PutPlcSettings_ShouldPersistNormalizedCommunicationProfile()
    {
        await using var host = await PlcSettingsTestHost.CreateAsync(new AppConfig());
        var payload = new
        {
            expectedRevision = host.ConfigurationService.GetCurrent().Revision,
            activeProtocol = "FINS",
            heartbeatIntervalMs = 1200,
            s7 = new
            {
                ipAddress = "192.168.0.1",
                port = 102,
                cpuType = "S7-1200",
                rack = 0,
                slot = 1,
                mappings = Array.Empty<object>()
            },
            mc = new
            {
                ipAddress = "192.168.3.1",
                port = 5002,
                mappings = Array.Empty<object>()
            },
            fins = new
            {
                ipAddress = "192.168.250.99",
                port = 9600,
                mappings = new[]
                {
                    new { name = "Ready", address = "DM100", dataType = "Word", description = "ready", canWrite = false }
                }
            }
        };

        using var response = await host.Client.PutAsync(
            "/api/plc/settings",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("settings").GetProperty("activeProtocol").GetString().Should().Be("FINS");
        document.RootElement.GetProperty("settings").GetProperty("fins").GetProperty("ipAddress").GetString().Should().Be("192.168.250.99");

        var committed = host.ConfigurationService.GetCurrent();
        committed.Communication.ActiveProtocol.Should().Be("FINS");
        committed.Communication.Fins.IpAddress.Should().Be("192.168.250.99");
        committed.Communication.Fins.Mappings.Should().ContainSingle(mapping => mapping.Address == "DM100");
        committed.Revision.Should().Be(1);
    }

    [Fact]
    public async Task PutPlcSettings_ShouldPreserveSystemCameraAndTcpSettings()
    {
        var initialConfig = new AppConfig
        {
            General = new GeneralConfig
            {
                SoftwareTitle = "Line A",
                Theme = GeneralConfig.ThemeLight,
                AutoStart = true
            },
            Storage = new StorageConfig
            {
                ImageSavePath = @"D:\VisionData",
                RetentionDays = 45,
                MinFreeSpaceGb = 11
            },
            TcpCommunication = new TcpCommunicationConfig
            {
                Profiles =
                [
                    new TcpCommunicationProfile
                    {
                        Id = "robot",
                        Name = "Robot",
                        RemoteHost = "10.0.0.7",
                        RemotePort = 9000
                    }
                ]
            },
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-main",
                    DisplayName = "Main Camera",
                    SerialNumber = "SN-MAIN"
                }
            ],
            ActiveCameraId = "cam-main"
        };
        initialConfig.Normalize();
        await using var host = await PlcSettingsTestHost.CreateAsync(initialConfig);
        var payload = new
        {
            expectedRevision = initialConfig.Revision,
            activeProtocol = "MC",
            heartbeatIntervalMs = 1500,
            s7 = new
            {
                ipAddress = "192.168.0.1",
                port = 102,
                cpuType = "S7-1200",
                rack = 0,
                slot = 1,
                mappings = Array.Empty<object>()
            },
            mc = new
            {
                ipAddress = "192.168.3.10",
                port = 5002,
                mappings = Array.Empty<object>()
            },
            fins = new
            {
                ipAddress = "192.168.250.1",
                port = 9600,
                mappings = Array.Empty<object>()
            }
        };

        using var response = await host.Client.PutAsync(
            "/api/plc/settings",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var committed = host.ConfigurationService.GetCurrent();
        committed.Communication.ActiveProtocol.Should().Be(CommunicationConfig.ProtocolMc);
        committed.Communication.Mc.IpAddress.Should().Be("192.168.3.10");
        committed.General.SoftwareTitle.Should().Be(initialConfig.General.SoftwareTitle);
        committed.General.AutoStart.Should().Be(initialConfig.General.AutoStart);
        committed.Storage.MinFreeSpaceGb.Should().Be(initialConfig.Storage.MinFreeSpaceGb);
        committed.TcpCommunication.Profiles.Should().ContainSingle(profile => profile.Id == "robot");
        committed.Cameras.Should().ContainSingle(binding => binding.Id == "cam-main");
        committed.ActiveCameraId.Should().Be("cam-main");
    }

    [Fact]
    public async Task PutPlcMappings_ShouldRejectNonAdminUser()
    {
        await using var host = await PlcSettingsTestHost.CreateAsync(new AppConfig(), role: "Operator");

        using var response = await host.Client.PutAsync(
            "/api/plc/mappings",
            new StringContent("{\"expectedRevision\":0,\"mappings\":[]}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.ConfigurationService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task PlcHardwareReadAndProbeEndpoints_ShouldRejectOperator()
    {
        await using var host = await PlcSettingsTestHost.CreateAsync(new AppConfig(), role: "Operator");

        using var settingsResponse = await host.Client.GetAsync("/api/plc/settings");
        using var mappingsResponse = await host.Client.GetAsync("/api/plc/mappings");
        using var testResponse = await host.Client.PostAsync(
            "/api/plc/test-connection",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        settingsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        mappingsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        testResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("S7", "S7://192.0.2.10:1102?cpu=S7-1500&rack=2&slot=3")]
    [InlineData("MC", "MC://192.0.2.20:5502")]
    [InlineData("FINS", "FINS://192.0.2.30:19600")]
    public async Task PostTestConnection_ShouldDispatchOnlyPersistedProfile(
        string protocol,
        string expectedConnectionString)
    {
        var config = new AppConfig
        {
            Communication = new CommunicationConfig
            {
                ActiveProtocol = protocol,
                S7 = new S7CommunicationProfile
                {
                    IpAddress = "192.0.2.10",
                    Port = 1102,
                    CpuType = "S7-1500",
                    Rack = 2,
                    Slot = 3
                },
                Mc = new PlcCommunicationProfile { IpAddress = "192.0.2.20", Port = 5502 },
                Fins = new PlcCommunicationProfile { IpAddress = "192.0.2.30", Port = 19600 }
            }
        };
        var probe = Substitute.For<IPlcConnectionTestProbe>();
        probe.TestAsync(Arg.Any<string>(), Arg.Any<Microsoft.Extensions.Logging.ILogger>(), Arg.Any<CancellationToken>())
            .Returns(true);
        await using var host = await PlcSettingsTestHost.CreateAsync(config, probe: probe);

        using var response = await host.Client.PostAsync(
            "/api/plc/test-connection",
            new StringContent(
                JsonSerializer.Serialize(new { profileId = protocol }),
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("protocol").GetString().Should().Be(protocol);
        await probe.Received(1).TestAsync(
            expectedConnectionString,
            Arg.Any<Microsoft.Extensions.Logging.ILogger>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostTestConnection_WithRawTargetEvenForAdmin_ShouldRejectBeforeProbe()
    {
        var probe = Substitute.For<IPlcConnectionTestProbe>();
        await using var host = await PlcSettingsTestHost.CreateAsync(new AppConfig(), probe: probe);

        using var response = await host.Client.PostAsync(
            "/api/plc/test-connection",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    profileId = "S7",
                    protocol = "S7",
                    ipAddress = "203.0.113.44",
                    port = 102,
                    cpuType = "S7-1500",
                    rack = 0,
                    slot = 1
                }),
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PLC_RAW_TARGET_FORBIDDEN");
        await probe.DidNotReceive().TestAsync(
            Arg.Any<string>(),
            Arg.Any<Microsoft.Extensions.Logging.ILogger>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class PlcSettingsTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PlcSettingsTestHost(WebApplication app, InMemoryAppConfigAuthority configurationService)
        {
            _app = app;
            ConfigurationService = configurationService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public InMemoryAppConfigAuthority ConfigurationService { get; }

        public static async Task<PlcSettingsTestHost> CreateAsync(
            AppConfig initialConfig,
            string? role = "Admin",
            IPlcConnectionTestProbe? probe = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            initialConfig.Normalize();
            var configService = new InMemoryAppConfigAuthority(initialConfig);
            builder.Services.AddSingleton<ClearVision.Product.Core.Interfaces.IConfigurationService>(configService);
            if (probe != null)
            {
                builder.Services.AddSingleton(probe);
            }

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (role is not null)
                {
                    context.Items["CurrentUser"] = new ClearVision.Product.Application.Services.UserSession
                    {
                        UserId = role.ToLowerInvariant(),
                        Username = role.ToLowerInvariant(),
                        Role = role
                    };
                }

                await next();
            });
            app.MapPlcEndpoints();
            await app.StartAsync();
            return new PlcSettingsTestHost(app, configService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
