using System.Linq;
using System.Net;
using System.Text.Json;
using ClearVision.Product.Application.Services;
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

public class SettingsResetEndpointTests
{
    [Fact]
    public async Task GetSettings_ForOperator_ShouldReturnOnlySafeUiSubset()
    {
        await using var host = await SettingsResetTestHost.CreateAsync(role: "Operator", initialConfig: CreateSensitiveConfig());

        using var response = await host.Client.GetAsync("/api/settings");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        responseJson.Should().Contain("safeSubset");
        responseJson.Should().Contain("general");
        responseJson.Should().Contain("theme");
        responseJson.Should().NotContain("communication");
        responseJson.Should().NotContain("plc");
        responseJson.Should().NotContain("cameras");
        responseJson.Should().NotContain("storage");
        responseJson.Should().NotContain("security");
        responseJson.Should().NotContain("runtime");
        responseJson.Should().NotContain("192.168.10.8");
        responseJson.Should().NotContain("CAM-SECRET-001");
        responseJson.Should().NotContain("D:\\SensitiveImages");
    }

    [Fact]
    public async Task GetSettings_ForAdmin_ShouldReturnFullConfig()
    {
        await using var host = await SettingsResetTestHost.CreateAsync(initialConfig: CreateSensitiveConfig());

        using var response = await host.Client.GetAsync("/api/settings");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        responseJson.Should().Contain("communication");
        responseJson.Should().Contain("cameras");
        responseJson.Should().Contain("storage");
        responseJson.Should().Contain("security");
        responseJson.Should().Contain("192.168.10.8");
        responseJson.Should().Contain("CAM-SECRET-001");
    }

    [Fact]
    public async Task GetSettings_ForEngineer_ShouldReturnOnlySafeUiSubset()
    {
        await using var host = await SettingsResetTestHost.CreateAsync(role: "Engineer", initialConfig: CreateSensitiveConfig());

        using var response = await host.Client.GetAsync("/api/settings");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        responseJson.Should().Contain("safeSubset");
        responseJson.Should().Contain("general");
        responseJson.Should().NotContain("communication");
        responseJson.Should().NotContain("storage");
        responseJson.Should().NotContain("192.168.10.8");
        responseJson.Should().NotContain("CAM-SECRET-001");
    }

    [Fact]
    public async Task GetSettings_WithoutAuthenticatedSession_ShouldRejectBeforeProjection()
    {
        await using var host = await SettingsResetTestHost.CreateAsync(role: null, initialConfig: CreateSensitiveConfig());

        using var response = await host.Client.GetAsync("/api/settings");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, responseJson);
        responseJson.Should().Contain("AuthenticatedSessionRequired");
        responseJson.Should().Contain("RequireAuthenticated");
    }

    [Fact]
    public async Task ResetSettings_ShouldResetAppConfigAndAiModels()
    {
        await using var host = await SettingsResetTestHost.CreateAsync();

        var aiConfigStore = host.Services.GetRequiredService<AiConfigStore>();
        aiConfigStore.Add(new AiModelConfig
        {
            Id = "custom-model",
            Name = "Custom Model",
            Provider = "custom-provider",
            ApiKey = "custom-key",
            Model = "custom-model-name"
        });
        aiConfigStore.SetActive("custom-model");

        using var response = await host.Client.PostAsync("/api/settings/reset", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.ConfigurationService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.General != null &&
            config.General.Theme == GeneralConfig.ThemeDark &&
            config.Runtime != null &&
            config.Security != null));

        var models = aiConfigStore.GetAll();
        models.Should().ContainSingle();
        models[0].Id.Should().Be("model_default");
        models[0].Provider.Should().Be("openai");
        models[0].Model.Should().Be("gpt-4o-mini");
        models[0].IsActive.Should().BeTrue();

        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.Should().NotContain("default-key");
        responseJson.Should().NotContain("custom-key");
        using var document = JsonDocument.Parse(responseJson);
        var resetScope = document.RootElement.GetProperty("resetScope").EnumerateArray().Select(x => x.GetString()).ToArray();
        resetScope.Should().Contain(new[] { "appConfig", "aiModels" });
        document.RootElement.GetProperty("aiModels").GetArrayLength().Should().Be(1);
        var aiModel = document.RootElement.GetProperty("aiModels")[0];
        aiModel.TryGetProperty("apiKey", out _).Should().BeFalse();
        aiModel.GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("config").ValueKind.Should().Be(JsonValueKind.Object);
        document.RootElement.GetProperty("config").GetProperty("general").GetProperty("theme").GetString().Should().Be(GeneralConfig.ThemeDark);
    }

    private sealed class SettingsResetTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SettingsResetTestHost(WebApplication app, IConfigurationService configurationService)
        {
            _app = app;
            ConfigurationService = configurationService;
            Client = app.GetTestClient();
            Services = app.Services;
        }

        public HttpClient Client { get; }

        public IServiceProvider Services { get; }

        public IConfigurationService ConfigurationService { get; }

        public static async Task<SettingsResetTestHost> CreateAsync(string? role = "Admin", AppConfig? initialConfig = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var configService = Substitute.For<IConfigurationService>();
            var config = initialConfig ?? new AppConfig();
            configService.LoadAsync().Returns(Task.FromResult(config));
            configService.GetCurrent().Returns(config);
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
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
                Path.Combine(Path.GetTempPath(), $"cv-settings-reset-{Guid.NewGuid():N}"));
            builder.Services.AddSingleton(aiConfigStore);
            builder.Services.AddSingleton(new AiApiClient(new HttpClient(), aiConfigStore));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (role != null)
                {
                    context.Items["CurrentUser"] = new UserSession
                    {
                        UserId = "admin",
                        Username = role.ToLowerInvariant(),
                        Role = role
                    };
                }
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new SettingsResetTestHost(app, configService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static AppConfig CreateSensitiveConfig()
    {
        return new AppConfig
        {
            General = new GeneralConfig { Theme = GeneralConfig.ThemeLight, SoftwareTitle = "ClearVision" },
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolS7,
                S7 = new S7CommunicationProfile
                {
                    IpAddress = "192.168.10.8",
                    Port = 102,
                    Mappings = [new PlcAddressMapping { Name = "Start", Address = "DB1.DBX0.0" }]
                }
            },
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-a",
                    SerialNumber = "CAM-SECRET-001",
                    IpAddress = "10.10.0.9"
                }
            ],
            Storage = new StorageConfig { ImageSavePath = @"D:\SensitiveImages" },
            Runtime = new RuntimeConfig { AutoRun = true },
            Security = new SecurityConfig { PasswordMinLength = 12 }
        };
    }
}
