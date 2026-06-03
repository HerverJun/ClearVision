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

        public static async Task<SettingsResetTestHost> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var configService = Substitute.For<IConfigurationService>();
            configService.LoadAsync().Returns(Task.FromResult(new AppConfig()));
            configService.GetCurrent().Returns(new AppConfig());
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
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = "admin",
                    Username = "admin",
                    Role = "Admin"
                };
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
}
