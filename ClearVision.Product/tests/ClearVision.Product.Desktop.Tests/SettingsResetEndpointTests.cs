using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Services;
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
    public async Task ResetSettings_ShouldResetAppConfigAndAiModels()
    {
        await using var host = await SettingsResetTestHost.CreateAsync(initialConfig: CreateSensitiveConfig());

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

        using var response = await host.Client.PostAsJsonAsync("/api/settings/reset", new SettingsResetRequest
        {
            ExpectedRevision = host.ConfigurationService.GetCurrent().Revision
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var committed = host.ConfigurationService.GetCurrent();
        committed.General.Theme.Should().Be(GeneralConfig.ThemeDark);
        committed.Runtime.Should().NotBeNull();
        committed.Security.Should().NotBeNull();
        committed.Cameras.Should().BeEmpty();
        committed.ActiveCameraId.Should().BeEmpty();
        committed.Revision.Should().Be(1);
        host.ConfigurationService.MutationCount.Should().Be(1);
        await host.CameraManager.Received(1).ApplyBindingsAsync(
            Arg.Is<List<CameraBindingConfig>>(bindings => bindings.Count == 0),
            string.Empty);
        host.SerialTriggerService.Received(1).ConfigureBindings(
            Arg.Is<IEnumerable<CameraBindingConfig>>(bindings => !bindings.Any()));

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

    [Fact]
    public async Task ResetSettings_WhenAppConfigIsAlreadyDefault_ShouldReconcileRuntimeWithoutRevisionIncrement()
    {
        await using var host = await SettingsResetTestHost.CreateAsync(initialConfig: new AppConfig());

        using var response = await host.Client.PostAsJsonAsync("/api/settings/reset", new SettingsResetRequest
        {
            ExpectedRevision = 0
        });

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        host.ConfigurationService.GetCurrent().Revision.Should().Be(0);
        host.ConfigurationService.MutationCount.Should().Be(0);
        await host.CameraManager.Received(1).ApplyBindingsAsync(
            Arg.Is<List<CameraBindingConfig>>(bindings => bindings.Count == 0),
            string.Empty);
        host.SerialTriggerService.Received(1).ConfigureBindings(
            Arg.Is<IEnumerable<CameraBindingConfig>>(bindings => !bindings.Any()));
    }

    [Fact]
    public async Task GetSettings_WhenReadIsDegraded_ShouldReturnStructuredServiceUnavailable()
    {
        var lastGood = CreateSensitiveConfig();
        lastGood.Revision = 7;
        await using var host = await SettingsResetTestHost.CreateAsync(initialConfig: lastGood);
        host.ConfigurationService.ForcedReadResult = new(
            ClearVision.Product.Core.Interfaces.AppConfigReadStatus.DegradedLastGood,
            lastGood,
            "APP_CONFIG_MALFORMED",
            "Configuration JSON is malformed.");

        using var response = await host.Client.GetAsync("/api/settings");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, responseJson);
        using var document = JsonDocument.Parse(responseJson);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("APP_CONFIG_MALFORMED");
        document.RootElement.GetProperty("configStatus").GetString().Should().Be("DegradedLastGood");
        document.RootElement.GetProperty("hasLastGood").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("revision").GetInt64().Should().Be(7);
    }

    [Fact]
    public async Task ResetSettings_WithoutExpectedRevision_ShouldReturnValidationFailure()
    {
        await using var host = await SettingsResetTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/settings/reset", new SettingsResetRequest());

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, responseJson);
        responseJson.Should().Contain("APP_CONFIG_EXPECTED_REVISION_REQUIRED");
        host.ConfigurationService.MutationCount.Should().Be(0);
        await host.CameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
        host.SerialTriggerService.DidNotReceive().ConfigureBindings(Arg.Any<IEnumerable<CameraBindingConfig>>());
    }

    private sealed class SettingsResetTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SettingsResetTestHost(
            WebApplication app,
            InMemoryAppConfigAuthority configurationService,
            ICameraManager cameraManager,
            ISerialPhotoelectricTriggerInputService serialTriggerService)
        {
            _app = app;
            ConfigurationService = configurationService;
            CameraManager = cameraManager;
            SerialTriggerService = serialTriggerService;
            Client = app.GetTestClient();
            Services = app.Services;
        }

        public HttpClient Client { get; }

        public IServiceProvider Services { get; }

        public InMemoryAppConfigAuthority ConfigurationService { get; }

        public ICameraManager CameraManager { get; }

        public ISerialPhotoelectricTriggerInputService SerialTriggerService { get; }

        public static async Task<SettingsResetTestHost> CreateAsync(string role = "Admin", AppConfig? initialConfig = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var config = initialConfig ?? new AppConfig();
            config.Normalize();
            var configService = new InMemoryAppConfigAuthority(config);
            var cameraManager = Substitute.For<ICameraManager>();
            cameraManager.ApplyBindingsAsync(
                Arg.Any<List<CameraBindingConfig>>(),
                Arg.Any<string>()).Returns(Task.CompletedTask);
            var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
            streamCoordinator.ReleaseIdleStreamAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
            var serialTriggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
            builder.Services.AddSingleton<ClearVision.Product.Core.Interfaces.IConfigurationService>(configService);
            builder.Services.AddSingleton(cameraManager);
            builder.Services.AddSingleton(streamCoordinator);
            builder.Services.AddSingleton(serialTriggerService);
            builder.Services.AddSingleton<CameraConfigurationCoordinator>();
            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<CameraConfigurationCoordinator>>(
                NullLogger<CameraConfigurationCoordinator>.Instance);

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
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = "admin",
                    Username = role.ToLowerInvariant(),
                    Role = role
                };
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new SettingsResetTestHost(app, configService, cameraManager, serialTriggerService);
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
