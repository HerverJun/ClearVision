using System.Net;
using System.Text;
using System.Text.Json;
using Acme.Product.Application.Services;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Acme.Product.Desktop.Endpoints;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Acme.Product.Desktop.Tests;

public class AiModelEndpointsTests
{
    [Fact]
    public async Task GetAiModels_ShouldReturnReasoningAndSupportMetadata()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "gpt5",
            Name = "GPT 5.4",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.4",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.On,
                Effort = AiReasoningEfforts.High
            }
        });

        using var response = await host.Client.GetAsync("/api/ai/models");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        responseJson.Should().NotContain("default-key");
        using var document = JsonDocument.Parse(responseJson);
        var models = document.RootElement;
        models.ValueKind.Should().Be(JsonValueKind.Array);
        var defaultModel = models.EnumerateArray().First(x => x.GetProperty("id").GetString() == "model_default");
        defaultModel.TryGetProperty("apiKey", out _).Should().BeFalse();
        defaultModel.GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
        var gpt5 = models.EnumerateArray().First(x => x.GetProperty("id").GetString() == "gpt5");
        gpt5.GetProperty("reasoning").GetProperty("mode").GetString().Should().Be("on");
        gpt5.GetProperty("reasoning").GetProperty("effort").GetString().Should().Be("high");
        gpt5.GetProperty("reasoningSupport").GetProperty("familyId").GetString().Should().Be("openai_gpt5");
        gpt5.GetProperty("reasoningSupport").GetProperty("supportsExplicitMode").GetBoolean().Should().BeTrue();
        gpt5.GetProperty("reasoningSupport").GetProperty("supportsEffort").GetBoolean().Should().BeTrue();
        gpt5.GetProperty("reasoningSupport").GetProperty("allowedModes").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["auto", "on"]);
        gpt5.GetProperty("reasoningSupport").GetProperty("allowedEfforts").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["low", "medium", "high"]);
    }

    [Fact]
    public async Task UpdateAiModel_ShouldRejectTurningOffLockedReasonerModel()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "deepseek-reasoner",
            Name = "DeepSeek Reasoner",
            Provider = "OpenAI Compatible",
            Model = "deepseek-reasoner"
        });

        var payload = JsonSerializer.Serialize(new
        {
            name = "DeepSeek Reasoner",
            provider = "OpenAI Compatible",
            model = "deepseek-reasoner",
            baseUrl = "https://api.deepseek.com",
            apiKey = "test-key",
            timeoutMs = 120000,
            reasoning = new
            {
                mode = "off",
                effort = "medium"
            }
        });

        using var response = await host.Client.PutAsync(
            "/api/ai/models/deepseek-reasoner",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("不支持关闭 reasoning / thinking");

        using var modelsResponse = await host.Client.GetAsync("/api/ai/models");
        modelsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await modelsResponse.Content.ReadAsStringAsync());
        var model = document.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == "deepseek-reasoner");
        model.GetProperty("reasoning").GetProperty("mode").GetString().Should().Be("auto");
        model.GetProperty("hasApiKey").GetBoolean().Should().BeFalse();
        host.AiConfigStore.GetById("deepseek-reasoner")!.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAiModel_ShouldMentionJsonWhenUsingOpenAiJsonObjectMode()
    {
        string? capturedRequestJson = null;
        await using var host = await AiModelEndpointTestHost.CreateAsync(async (request, cancellationToken) =>
        {
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "{\"ok\":true}"
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "deepseek-chat",
            Name = "DeepSeek Chat",
            Provider = "OpenAI Compatible",
            Model = "deepseek-chat",
            BaseUrl = "https://api.deepseek.com",
            ApiKey = "test-key"
        });

        using var response = await host.Client.PostAsync(
            "/api/ai/models/deepseek-chat/test",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        responseDocument.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        capturedRequestJson.Should().NotBeNullOrWhiteSpace();
        using var requestDocument = JsonDocument.Parse(capturedRequestJson!);
        requestDocument.RootElement.GetProperty("response_format").GetProperty("type").GetString()
            .Should().Be("json_object");
        var promptText = string.Join(
            "\n",
            requestDocument.RootElement.GetProperty("messages").EnumerateArray()
                .Select(message => message.GetProperty("content").GetString()));
        promptText.ToLowerInvariant().Should().Contain("json");
    }

    [Fact]
    public async Task TestAiModel_ShouldRejectUnexpectedHealthCheckContent()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "OK"
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            }));
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "deepseek-chat",
            Name = "DeepSeek Chat",
            Provider = "OpenAI Compatible",
            Model = "deepseek-chat",
            BaseUrl = "https://api.deepseek.com",
            ApiKey = "test-key"
        });

        using var response = await host.Client.PostAsync(
            "/api/ai/models/deepseek-chat/test",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        responseDocument.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        responseDocument.RootElement.GetProperty("message").GetString()
            .Should().Contain("不是预期的 JSON health-check 响应");
    }

    [Fact]
    public async Task CreateAiModel_ShouldPersistSecretAndNeverReturnApiKey()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        var payload = JsonSerializer.Serialize(new
        {
            name = "Created Model",
            provider = "OpenAI Compatible",
            model = "gpt-4o-mini",
            baseUrl = "https://api.openai.com/v1",
            apiKey = "created-secret-key",
            timeoutMs = 90000
        });

        using var response = await host.Client.PostAsync(
            "/api/ai/models",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.Should().NotContain("created-secret-key");
        using var createDocument = JsonDocument.Parse(responseJson);
        var createdId = createDocument.RootElement.TryGetProperty("id", out var idProperty)
            ? idProperty.GetString()
            : createDocument.RootElement.GetProperty("Id").GetString();
        createdId.Should().NotBeNullOrWhiteSpace();
        host.AiConfigStore.GetById(createdId!)!.ApiKey.Should().Be("created-secret-key");

        using var modelsResponse = await host.Client.GetAsync("/api/ai/models");
        var modelsJson = await modelsResponse.Content.ReadAsStringAsync();
        modelsJson.Should().NotContain("created-secret-key");
        using var modelsDocument = JsonDocument.Parse(modelsJson);
        var createdModel = modelsDocument.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == createdId);
        createdModel.TryGetProperty("apiKey", out _).Should().BeFalse();
        createdModel.GetProperty("hasApiKey").GetBoolean().Should().BeTrue();

        var reloaded = host.CreateReloadedStore();
        reloaded.GetById(createdId!)!.ApiKey.Should().Be("created-secret-key");
    }

    [Fact]
    public async Task PreviewReasoningSupport_ShouldReturnServerResolvedAllowedModesAndEfforts()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        var payload = JsonSerializer.Serialize(new
        {
            provider = "OpenAI Compatible",
            model = "gpt-5.1-mini",
            baseUrl = "https://api.openai.com/v1",
            protocol = (string?)null
        });

        using var response = await host.Client.PostAsync(
            "/api/ai/reasoning-support",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("familyId").GetString().Should().Be("openai_gpt5");
        document.RootElement.GetProperty("allowedModes").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["auto", "off", "on"]);
        document.RootElement.GetProperty("allowedEfforts").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["low", "medium", "high"]);
    }

    private sealed class AiModelEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private readonly string _storageDirectory;

        private AiModelEndpointTestHost(WebApplication app, AiConfigStore aiConfigStore, string storageDirectory)
        {
            _app = app;
            AiConfigStore = aiConfigStore;
            _storageDirectory = storageDirectory;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public AiConfigStore AiConfigStore { get; }

        public AiConfigStore CreateReloadedStore()
        {
            return new AiConfigStore(
                Options.Create(new AiGenerationOptions
                {
                    Provider = "OpenAI Compatible",
                    Model = "gpt-4o-mini",
                    ApiKey = string.Empty,
                    BaseUrl = "https://api.openai.com/v1",
                    TimeoutSeconds = 90
                }),
                NullLogger<AiConfigStore>.Instance,
                _storageDirectory);
        }

        public static async Task<AiModelEndpointTestHost> CreateAsync(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? aiSendAsync = null)
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
            builder.Services.AddSingleton(Substitute.For<Acme.Product.Core.Cameras.ICameraManager>());

            var storageDirectory = Path.Combine(Path.GetTempPath(), $"cv-ai-model-endpoints-{Guid.NewGuid():N}");
            var aiConfigStore = new AiConfigStore(
                Options.Create(new AiGenerationOptions
                {
                    Provider = "OpenAI Compatible",
                    Model = "gpt-4o-mini",
                    ApiKey = "default-key",
                    BaseUrl = "https://api.openai.com/v1",
                    TimeoutSeconds = 90
                }),
                NullLogger<AiConfigStore>.Instance,
                storageDirectory);
            builder.Services.AddSingleton(aiConfigStore);
            var httpClient = aiSendAsync == null
                ? new HttpClient()
                : new HttpClient(new CaptureHandler(aiSendAsync));
            builder.Services.AddSingleton(new AiApiClient(httpClient, aiConfigStore));

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
            return new AiModelEndpointTestHost(app, aiConfigStore, storageDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_storageDirectory))
            {
                Directory.Delete(_storageDirectory, recursive: true);
            }
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

            public CaptureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            {
                _sendAsync = sendAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return _sendAsync(request, cancellationToken);
            }
        }
    }
}
