using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public class SettingsThemeEndpointTests
{
    [Fact]
    public async Task UpdateTheme_ShouldNormalizeThemeWithoutOverwritingOtherSettings()
    {
        var initialConfig = new AppConfig
        {
            General = new GeneralConfig
            {
                SoftwareTitle = "ClearVision",
                Theme = GeneralConfig.ThemeDark,
                AutoStart = true
            },
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolMc,
                HeartbeatIntervalMs = 4321,
                Mc = new PlcCommunicationProfile
                {
                    IpAddress = "10.0.0.8",
                    Port = 5002,
                    Mappings = new List<PlcAddressMapping>()
                }
            },
            Storage = new StorageConfig
            {
                ImageSavePath = @"D:\VisionData\Images",
                RetentionDays = 45,
                MinFreeSpaceGb = 9
            },
            Runtime = new RuntimeConfig
            {
                MissingMaterialTimeoutSeconds = 18
            },
            Security = new SecurityConfig
            {
                PasswordMinLength = 10
            },
            ActiveCameraId = "camera-001"
        };
        initialConfig.Normalize();

        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings/theme", new ThemeUpdateRequest
        {
            Theme = " LIGHT ",
            ExpectedRevision = initialConfig.Revision
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var committed = host.ConfigurationService.GetCurrent();
        committed.General.Theme.Should().Be(GeneralConfig.ThemeLight);
        committed.General.SoftwareTitle.Should().Be(initialConfig.General.SoftwareTitle);
        committed.General.AutoStart.Should().Be(initialConfig.General.AutoStart);
        committed.Communication.ActiveProtocol.Should().Be(initialConfig.Communication.ActiveProtocol);
        committed.Communication.HeartbeatIntervalMs.Should().Be(initialConfig.Communication.HeartbeatIntervalMs);
        committed.Storage.RetentionDays.Should().Be(initialConfig.Storage.RetentionDays);
        committed.Storage.MinFreeSpaceGb.Should().Be(initialConfig.Storage.MinFreeSpaceGb);
        committed.Runtime.MissingMaterialTimeoutSeconds.Should().Be(initialConfig.Runtime.MissingMaterialTimeoutSeconds);
        committed.Security.PasswordMinLength.Should().Be(initialConfig.Security.PasswordMinLength);
        committed.ActiveCameraId.Should().Be(initialConfig.ActiveCameraId);
        committed.Revision.Should().Be(initialConfig.Revision + 1);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("theme").GetString().Should().Be(GeneralConfig.ThemeLight);
    }

    [Fact]
    public async Task UpdateTheme_ShouldRejectOperator()
    {
        var initialConfig = new AppConfig();
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig, role: "Operator");

        using var response = await host.Client.PutAsJsonAsync("/api/settings/theme", new ThemeUpdateRequest
        {
            Theme = GeneralConfig.ThemeDark,
            ExpectedRevision = initialConfig.Revision
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.ConfigurationService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateSettings_WithGeneralScope_ShouldPreserveOtherSectionsAndRetiredAutoStart()
    {
        var initialConfig = CreateRichSettingsConfig();
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            expectedRevision = initialConfig.Revision,
            saveScope = "general",
            general = new
            {
                softwareTitle = "Updated Station",
                theme = "light"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var committed = host.ConfigurationService.GetCurrent();
        committed.General.SoftwareTitle.Should().Be("Updated Station");
        committed.General.Theme.Should().Be(GeneralConfig.ThemeLight);
        committed.General.AutoStart.Should().Be(initialConfig.General.AutoStart);
        committed.Communication.ActiveProtocol.Should().Be(initialConfig.Communication.ActiveProtocol);
        committed.TcpCommunication.Profiles.Should().ContainSingle(profile => profile.Id == "robot");
        committed.Cameras.Should().ContainSingle(binding => binding.Id == "cam-001");
        committed.Runtime.RuntimePreviewPilot.Enabled.Should().Be(initialConfig.Runtime.RuntimePreviewPilot.Enabled);
        committed.Security.SessionTimeoutMinutes.Should().Be(initialConfig.Security.SessionTimeoutMinutes);
        committed.Storage.MinFreeSpaceGb.Should().Be(initialConfig.Storage.MinFreeSpaceGb);
        committed.ActiveCameraId.Should().Be(initialConfig.ActiveCameraId);
    }

    [Fact]
    public async Task UpdateSettings_WithMissingSections_ShouldNotClearExistingSections()
    {
        var initialConfig = CreateRichSettingsConfig();
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            expectedRevision = initialConfig.Revision,
            saveScope = "storage",
            storage = new
            {
                imageSavePath = @"E:\VisionData",
                retentionDays = 90
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var committed = host.ConfigurationService.GetCurrent();
        committed.Storage.ImageSavePath.Should().Be(@"E:\VisionData");
        committed.Storage.RetentionDays.Should().Be(90);
        committed.Storage.SavePolicy.Should().Be(initialConfig.Storage.SavePolicy);
        committed.Storage.MinFreeSpaceGb.Should().Be(initialConfig.Storage.MinFreeSpaceGb);
        committed.General.SoftwareTitle.Should().Be(initialConfig.General.SoftwareTitle);
        committed.Communication.ActiveProtocol.Should().Be(initialConfig.Communication.ActiveProtocol);
        committed.TcpCommunication.Profiles.Should().HaveCount(1);
        committed.Cameras.Should().HaveCount(1);
        committed.Security.PasswordMinLength.Should().Be(initialConfig.Security.PasswordMinLength);
    }

    [Fact]
    public async Task UpdateTheme_WithStaleRevision_ShouldReturnConflictWithoutMutation()
    {
        var initialConfig = CreateRichSettingsConfig();
        initialConfig.Revision = 4;
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings/theme", new ThemeUpdateRequest
        {
            Theme = GeneralConfig.ThemeLight,
            ExpectedRevision = 3
        });

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, responseJson);
        responseJson.Should().Contain("APP_CONFIG_REVISION_CONFLICT");
        host.ConfigurationService.GetCurrent().Revision.Should().Be(4);
        host.ConfigurationService.GetCurrent().General.Theme.Should().Be(GeneralConfig.ThemeDark);
        host.ConfigurationService.MutationCount.Should().Be(0);
    }

    private sealed class SettingsThemeTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SettingsThemeTestHost(WebApplication app, InMemoryAppConfigAuthority configurationService)
        {
            _app = app;
            ConfigurationService = configurationService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public InMemoryAppConfigAuthority ConfigurationService { get; }

        public static async Task<SettingsThemeTestHost> CreateAsync(AppConfig initialConfig, string role = "Admin")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var configService = new InMemoryAppConfigAuthority(initialConfig);
            builder.Services.AddSingleton<ClearVision.Product.Core.Interfaces.IConfigurationService>(configService);
            builder.Services.AddSingleton(Substitute.For<ClearVision.Product.Core.Cameras.ICameraManager>());

            var aiConfigStore = new AiConfigStore(
                Options.Create(new AiGenerationOptions
                {
                    Provider = "openai",
                    Model = "gpt-4o-mini",
                    ApiKey = "default-key",
                    BaseUrl = "https://api.openai.com/v1",
                    TimeoutSeconds = 90
                }),
                NullLogger<AiConfigStore>.Instance,
                Path.Combine(Path.GetTempPath(), $"cv-settings-theme-{Guid.NewGuid():N}"));
            builder.Services.AddSingleton(aiConfigStore);
            builder.Services.AddSingleton(new AiApiClient(new HttpClient(), aiConfigStore));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new ClearVision.Product.Application.Services.UserSession
                {
                    UserId = role.ToLowerInvariant(),
                    Username = role.ToLowerInvariant(),
                    Role = role
                };
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new SettingsThemeTestHost(app, configService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

    }

    private static AppConfig CreateRichSettingsConfig()
    {
        var config = new AppConfig
        {
            General = new GeneralConfig
            {
                SoftwareTitle = "ClearVision",
                Theme = GeneralConfig.ThemeDark,
                AutoStart = true
            },
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolMc,
                HeartbeatIntervalMs = 4321,
                Mc = new PlcCommunicationProfile
                {
                    IpAddress = "10.0.0.8",
                    Port = 5002,
                    Mappings = new List<PlcAddressMapping>()
                }
            },
            TcpCommunication = new TcpCommunicationConfig
            {
                Profiles =
                [
                    new TcpCommunicationProfile
                    {
                        Id = "robot",
                        Name = "Robot",
                        RemoteHost = "10.0.0.9",
                        RemotePort = 9000
                    }
                ]
            },
            Storage = new StorageConfig
            {
                ImageSavePath = @"D:\VisionData\Images",
                SavePolicy = "NgOnly",
                RetentionDays = 45,
                MinFreeSpaceGb = 9
            },
            Runtime = new RuntimeConfig
            {
                MissingMaterialTimeoutSeconds = 180,
                RuntimePreviewPilot = new RuntimePreviewPilotConfig
                {
                    Enabled = true,
                    AllowedModelIds = ["model-a"]
                }
            },
            Security = new SecurityConfig
            {
                PasswordMinLength = 10,
                SessionTimeoutMinutes = 999,
                LoginFailureLockoutCount = 7
            },
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-001",
                    DisplayName = "Camera 1",
                    SerialNumber = "SN001"
                }
            ],
            ActiveCameraId = "cam-001"
        };
        config.Normalize();
        return config;
    }
}
