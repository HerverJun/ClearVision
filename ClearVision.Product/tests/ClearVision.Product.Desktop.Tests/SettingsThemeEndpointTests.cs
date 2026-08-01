using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
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
            Theme = " LIGHT "
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.ConfigurationService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.General != null &&
            config.General.Theme == GeneralConfig.ThemeLight &&
            config.General.SoftwareTitle == initialConfig.General.SoftwareTitle &&
            config.General.AutoStart == initialConfig.General.AutoStart &&
            config.Communication != null &&
            config.Communication.ActiveProtocol == initialConfig.Communication.ActiveProtocol &&
            config.Communication.HeartbeatIntervalMs == initialConfig.Communication.HeartbeatIntervalMs &&
            config.Storage != null &&
            config.Storage.RetentionDays == initialConfig.Storage.RetentionDays &&
            config.Storage.MinFreeSpaceGb == initialConfig.Storage.MinFreeSpaceGb &&
            config.Runtime != null &&
            config.Runtime.MissingMaterialTimeoutSeconds == initialConfig.Runtime.MissingMaterialTimeoutSeconds &&
            config.Security != null &&
            config.Security.PasswordMinLength == initialConfig.Security.PasswordMinLength &&
            config.ActiveCameraId == initialConfig.ActiveCameraId));

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
            Theme = GeneralConfig.ThemeDark
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await host.ConfigurationService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    [Fact]
    public async Task UpdateSettings_WithGeneralScope_ShouldPreserveOtherSectionsAndRetiredAutoStart()
    {
        var initialConfig = CreateRichSettingsConfig();
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            saveScope = "general",
            general = new
            {
                softwareTitle = "Updated Station",
                theme = "light"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await host.ConfigurationService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.General.SoftwareTitle == "Updated Station" &&
            config.General.Theme == GeneralConfig.ThemeLight &&
            config.General.AutoStart == initialConfig.General.AutoStart &&
            config.Communication.ActiveProtocol == initialConfig.Communication.ActiveProtocol &&
            config.TcpCommunication.Profiles.Count == 1 &&
            config.TcpCommunication.Profiles[0].Id == "robot" &&
            config.Cameras.Count == 1 &&
            config.Cameras[0].Id == "cam-001" &&
            config.Runtime.RuntimePreviewPilot.Enabled == initialConfig.Runtime.RuntimePreviewPilot.Enabled &&
            config.Security.SessionTimeoutMinutes == initialConfig.Security.SessionTimeoutMinutes &&
            config.Storage.MinFreeSpaceGb == initialConfig.Storage.MinFreeSpaceGb &&
            config.ActiveCameraId == initialConfig.ActiveCameraId));
    }

    [Fact]
    public async Task UpdateSettings_ShouldReturnPersistedRevision_AndSubsequentGetShouldMatch()
    {
        var initialConfig = CreateRichSettingsConfig();
        initialConfig.Revision = 12;
        initialConfig.Normalize();
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            saveScope = "general",
            general = new
            {
                softwareTitle = "Revisioned Station"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var saveDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var persistedRevision = saveDocument.RootElement
            .GetProperty("config")
            .GetProperty("revision")
            .GetInt64();
        persistedRevision.Should().BeGreaterThan(initialConfig.Revision);

        using var getResponse = await host.Client.GetAsync("/api/settings");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        getDocument.RootElement.GetProperty("revision").GetInt64().Should().Be(persistedRevision);
        getDocument.RootElement.GetProperty("general").GetProperty("softwareTitle")
            .GetString().Should().Be("Revisioned Station");
    }

    [Fact]
    public async Task UpdateSettings_WithMissingSections_ShouldNotClearExistingSections()
    {
        var initialConfig = CreateRichSettingsConfig();
        await using var host = await SettingsThemeTestHost.CreateAsync(initialConfig);

        using var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            saveScope = "storage",
            storage = new
            {
                imageSavePath = @"E:\VisionData",
                retentionDays = 90
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await host.ConfigurationService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.Storage.ImageSavePath == @"E:\VisionData" &&
            config.Storage.RetentionDays == 90 &&
            config.Storage.SavePolicy == initialConfig.Storage.SavePolicy &&
            config.Storage.MinFreeSpaceGb == initialConfig.Storage.MinFreeSpaceGb &&
            config.General.SoftwareTitle == initialConfig.General.SoftwareTitle &&
            config.Communication.ActiveProtocol == initialConfig.Communication.ActiveProtocol &&
            config.TcpCommunication.Profiles.Count == 1 &&
            config.Cameras.Count == 1 &&
            config.Security.PasswordMinLength == initialConfig.Security.PasswordMinLength));
    }

    [Theory]
    [InlineData("{\"saveScope\":\"unknown\",\"general\":{\"softwareTitle\":\"Updated\"}}")]
    [InlineData("{\"saveScope\":\"general\"}")]
    [InlineData("{\"saveScope\":\"general\",\"storage\":{\"retentionDays\":1}}")]
    [InlineData("{\"saveScope\":\"general\",\"general\":{\"unknownField\":true}}")]
    [InlineData("{\"saveScope\":\"general\",\"general\":{\"softwareTitle\":\"\"}}")]
    [InlineData("{\"saveScope\":\"storage\",\"storage\":{\"retentionDays\":-1}}")]
    [InlineData("{\"saveScope\":\"runtime\",\"runtime\":{\"missingMaterialTimeoutSeconds\":-1}}")]
    [InlineData("{\"saveScope\":\"runtime\",\"runtime\":{\"runtimePreviewPilot\":{\"mode\":\"metadata_only\"}}}")]
    [InlineData("{\"saveScope\":\"security\",\"security\":{\"passwordMinLength\":5}}")]
    [InlineData("{\"saveScope\":\"security\",\"security\":{\"sessionTimeoutMinutes\":30}}")]
    [InlineData("{\"saveScope\":\"general\",\"general\":{\"softwareTitle\":\"A\",\"softwareTitle\":\"B\"}}")]
    [InlineData("{\"saveScope\":\"general\",\"general\":{\"softwareTitle\":\"A\"},\"GENERAL\":{\"theme\":\"light\"}}")]
    public async Task UpdateSettings_WithMalformedScopedPayload_ShouldRejectBeforePersistence(string json)
    {
        await using var host = await SettingsThemeTestHost.CreateAsync(CreateRichSettingsConfig());

        using var response = await host.Client.PutAsync(
            "/api/settings",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await host.ConfigurationService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    [Fact]
    public async Task UpdateSettings_WithLegacyUsersScope_ShouldMergeSecurityPolicy()
    {
        await using var host = await SettingsThemeTestHost.CreateAsync(CreateRichSettingsConfig());

        using var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            saveScope = "users",
            security = new { passwordMinLength = 12 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.ConfigurationService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.Security.PasswordMinLength == 12));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task UpdateSettings_WithNonObjectPayload_ShouldRejectBeforePersistence(string json)
    {
        await using var host = await SettingsThemeTestHost.CreateAsync(CreateRichSettingsConfig());

        using var response = await host.Client.PutAsync(
            "/api/settings",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await host.ConfigurationService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    private sealed class SettingsThemeTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SettingsThemeTestHost(WebApplication app, IConfigurationService configurationService)
        {
            _app = app;
            ConfigurationService = configurationService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IConfigurationService ConfigurationService { get; }

        public static async Task<SettingsThemeTestHost> CreateAsync(AppConfig initialConfig, string role = "Admin")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var configService = Substitute.For<IConfigurationService>();
            var persistedConfig = CloneConfig(initialConfig);
            configService.LoadAsync().Returns(_ => Task.FromResult(CloneConfig(persistedConfig)));
            configService.GetCurrent().Returns(_ => CloneConfig(persistedConfig));
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(callInfo =>
            {
                var savedConfig = CloneConfig(callInfo.Arg<AppConfig>());
                savedConfig.Revision = Math.Max(savedConfig.Revision, persistedConfig.Revision) + 1;
                persistedConfig = savedConfig;
                return Task.CompletedTask;
            });
            builder.Services.AddSingleton(configService);
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

        private static AppConfig CloneConfig(AppConfig source)
        {
            return JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(source))
                ?? new AppConfig();
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
