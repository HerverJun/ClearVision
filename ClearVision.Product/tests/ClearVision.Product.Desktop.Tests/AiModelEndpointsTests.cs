using System.Net;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Tools;
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
        gpt5.GetProperty("wireApi").GetString().Should().Be("chat_completions");
        gpt5.GetProperty("reasoningSupport").GetProperty("familyId").GetString().Should().Be("openai_gpt5");
        gpt5.GetProperty("reasoningSupport").GetProperty("supportsExplicitMode").GetBoolean().Should().BeTrue();
        gpt5.GetProperty("reasoningSupport").GetProperty("supportsEffort").GetBoolean().Should().BeTrue();
        gpt5.GetProperty("reasoningSupport").GetProperty("allowedModes").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["auto", "on"]);
        gpt5.GetProperty("reasoningSupport").GetProperty("allowedEfforts").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["low", "medium", "high", "xhigh"]);
    }

    [Fact]
    public async Task GetAiModels_ForOperator_ShouldReturnSafeModelDtoWithoutAuthMaterial()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: "Operator");
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "secret-model",
            Name = "Secret Model",
            DisplayName = "Secret Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o",
            BaseUrl = "https://internal-gateway.example/v1",
            AuthHeaderName = "Authorization",
            ExtraHeaders = new Dictionary<string, string> { ["authorization"] = "Bearer secret-token" },
            ExtraQuery = new Dictionary<string, string> { ["token"] = "query-secret" },
            ExtraBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                """{"token":"body-secret","safe":"value"}""")
        });

        using var response = await host.Client.GetAsync("/api/ai/models");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        responseJson.Should().NotContain("secret-token");
        responseJson.Should().NotContain("query-secret");
        responseJson.Should().NotContain("body-secret");
        using var document = JsonDocument.Parse(responseJson);
        var model = document.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == "secret-model");
        model.GetProperty("displayName").GetString().Should().Be("Secret Model");
        model.TryGetProperty("baseUrl", out _).Should().BeFalse();
        model.TryGetProperty("authHeaderName", out _).Should().BeFalse();
        model.TryGetProperty("extraHeaders", out _).Should().BeFalse();
        model.TryGetProperty("extraQuery", out _).Should().BeFalse();
        model.TryGetProperty("extraBody", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetAiModels_ForEngineer_ShouldReturnSafeModelDtoWithoutAuthMaterial()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: "Engineer");

        using var response = await host.Client.GetAsync("/api/ai/models");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        using var document = JsonDocument.Parse(responseJson);
        var model = document.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == "model_default");
        model.TryGetProperty("hasApiKey", out _).Should().BeFalse();
        model.TryGetProperty("baseUrl", out _).Should().BeFalse();
        model.TryGetProperty("extraHeaders", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Admin", true)]
    [InlineData("Engineer", true)]
    [InlineData("Operator", true)]
    public async Task GetAiModels_ShouldRequireAuthenticatedSessionWithoutChangingRoleProjection(
        string? userRole,
        bool shouldReturnProjection)
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: userRole);

        using var response = await host.Client.GetAsync("/api/ai/models");
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!shouldReturnProjection)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, responseJson);
            responseJson.Should().Contain("AuthenticatedSessionRequired");
            responseJson.Should().Contain("RequireAuthenticated");
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        using var document = JsonDocument.Parse(responseJson);
        var model = document.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == "model_default");
        model.TryGetProperty("hasApiKey", out _).Should().Be(userRole == "Admin");
    }

    [Fact]
    public async Task GetAiModels_ForAdmin_ShouldMaskSensitiveExtraFields()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "admin-secret-model",
            Name = "Admin Secret Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o",
            BaseUrl = "https://internal-gateway.example/v1?token=url-secret",
            AuthHeaderName = "Authorization",
            ExtraHeaders = new Dictionary<string, string> { ["authorization"] = "Bearer header-secret", ["x-api-key"] = "api-secret" },
            ExtraQuery = new Dictionary<string, string> { ["token"] = "query-secret" },
            ExtraBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                """{"token":"body-secret","safe":"value"}""")
        });

        using var response = await host.Client.GetAsync("/api/ai/models");

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        responseJson.Should().NotContain("url-secret");
        responseJson.Should().NotContain("header-secret");
        responseJson.Should().NotContain("api-secret");
        responseJson.Should().NotContain("query-secret");
        responseJson.Should().NotContain("body-secret");
        using var document = JsonDocument.Parse(responseJson);
        var model = document.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == "admin-secret-model");
        model.GetProperty("baseUrl").GetString().Should().Be("https://<redacted-host>/<redacted-path>");
        model.GetProperty("extraHeaders").GetProperty("authorization").GetString().Should().Be("<redacted>");
        model.GetProperty("extraHeaders").GetProperty("x-api-key").GetString().Should().Be("<redacted>");
        model.GetProperty("extraQuery").GetProperty("token").GetString().Should().Be("<redacted>");
        model.GetProperty("extraBody").GetProperty("token").GetString().Should().Be("<redacted>");
    }

    [Fact]
    public async Task UpdateAiModel_ShouldPreserveRedactedBaseUrlUntilAnExplicitOperation()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "base-url-model",
            Name = "Base URL Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://private-gateway.example/v1",
            ApiKey = "base-url-key"
        });

        using var preserveResponse = await host.Client.PutAsync(
            "/api/ai/models/base-url-model",
            JsonContent(new
            {
                name = "Base URL Model Renamed",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "preserve",
                apiKeyOperation = "keep",
                isEnabled = true
            }));
        preserveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("base-url-model")!.BaseUrl.Should().Be("https://private-gateway.example/v1");

        using var replaceResponse = await host.Client.PutAsync(
            "/api/ai/models/base-url-model",
            JsonContent(new
            {
                name = "Base URL Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "replace",
                baseUrl = "https://replacement.example/v2",
                apiKeyOperation = "keep",
                isEnabled = true
            }));
        replaceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("base-url-model")!.BaseUrl.Should().Be("https://replacement.example/v2");

        using var clearResponse = await host.Client.PutAsync(
            "/api/ai/models/base-url-model",
            JsonContent(new
            {
                name = "Base URL Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "clear",
                apiKeyOperation = "keep",
                isEnabled = true
            }));
        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("base-url-model")!.BaseUrl.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateAiModel_ShouldRejectMaskedAndAmbiguousApiKeyOperations()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "key-contract-model",
            Name = "Key Contract Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.example/v1",
            ApiKey = "original-key"
        });

        using var keepWithKey = await host.Client.PutAsync(
            "/api/ai/models/key-contract-model",
            JsonContent(new
            {
                name = "Key Contract Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "preserve",
                apiKeyOperation = "keep",
                apiKey = "********",
                isEnabled = true
            }));
        keepWithKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var maskedReplace = await host.Client.PutAsync(
            "/api/ai/models/key-contract-model",
            JsonContent(new
            {
                name = "Key Contract Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "preserve",
                apiKeyOperation = "replace",
                apiKey = "****abcd",
                isEnabled = true
            }));
        maskedReplace.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var clearWithKey = await host.Client.PutAsync(
            "/api/ai/models/key-contract-model",
            JsonContent(new
            {
                name = "Key Contract Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "preserve",
                apiKeyOperation = "clear",
                apiKey = "replacement-key",
                isEnabled = true
            }));
        clearWithKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var unknownOperation = await host.Client.PutAsync(
            "/api/ai/models/key-contract-model",
            JsonContent(new
            {
                name = "Key Contract Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "preserve",
                apiKeyOperation = "rotate",
                isEnabled = true
            }));
        unknownOperation.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.AiConfigStore.GetById("key-contract-model")!.ApiKey.Should().Be("original-key");
    }

    [Fact]
    public async Task CreateAiModel_ShouldRequireExplicitApiKeyOperation()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();

        using var missingOperation = await host.Client.PostAsync(
            "/api/ai/models",
            JsonContent(new
            {
                name = "Ambiguous Create",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "clear"
            }));

        missingOperation.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingOperation.Content.ReadAsStringAsync()).Should().Contain("explicitly choose API key operation");
    }

    [Fact]
    public async Task CreateAiModel_ShouldRejectKeepBecauseNewModelHasNoExistingKey()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/ai/models",
            JsonContent(new
            {
                name = "Ambiguous Create",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "clear",
                apiKeyOperation = "keep"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("keep");
    }

    [Theory]
    [InlineData("OpenAI Compatible", "openai_compatible", "chat_completions", "bearer", "Authorization")]
    [InlineData("Anthropic", "anthropic", "chat_completions", "header_key", "x-api-key")]
    [InlineData("Azure OpenAI", "azure_openai", "chat_completions", "header_key", "api-key")]
    [InlineData("Ollama", "ollama_native", "chat_completions", "none", "")]
    [InlineData("Custom Proxy", "openai_compatible", "chat_completions", "header_key", "x-api-key")]
    public async Task CreateAiModel_ShouldAcceptKnownAndCustomProviderContracts(
        string provider,
        string protocol,
        string wireApi,
        string authMode,
        string authHeaderName)
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        var payload = new Dictionary<string, object?>
        {
            ["name"] = $"{provider} Model",
            ["provider"] = provider,
            ["model"] = "provider-model",
            ["baseUrlOperation"] = "replace",
            ["baseUrl"] = "https://provider.example/v1",
            ["protocol"] = protocol,
            ["wireApi"] = wireApi,
            ["authMode"] = authMode,
            ["authHeaderName"] = authHeaderName,
            ["timeoutMs"] = 120000,
            ["isEnabled"] = true,
            ["apiKeyOperation"] = authMode == "none" ? "clear" : "replace"
        };
        if (authMode != "none")
        {
            payload["apiKey"] = "provider-secret";
        }

        using var response = await host.Client.PostAsync("/api/ai/models", JsonContent(payload));
        var responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        var id = document.RootElement.GetProperty("id").GetString();
        id.Should().NotBeNullOrWhiteSpace();
        var model = host.AiConfigStore.GetById(id!);
        model!.Provider.Should().Be(provider);
        model.Protocol.Should().Be(protocol);
        model.WireApi.Should().Be(wireApi);
        model.AuthMode.Should().Be(authMode);
        model.AuthHeaderName.Should().Be(authHeaderName);
    }

    [Fact]
    public async Task CreateAiModel_OllamaWithoutKey_ShouldRequireAndPersistExplicitClear()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();

        using var invalid = await host.Client.PostAsync(
            "/api/ai/models",
            JsonContent(new
            {
                name = "Ollama Invalid Key Contract",
                provider = "Ollama",
                model = "llama3.2",
                baseUrlOperation = "clear",
                protocol = "ollama_native",
                wireApi = "chat_completions",
                authMode = "none",
                authHeaderName = "",
                apiKeyOperation = "replace",
                apiKey = "must-not-be-stored"
            }));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("requires API key operation clear");

        using var valid = await host.Client.PostAsync(
            "/api/ai/models",
            JsonContent(new
            {
                name = "Ollama Explicit Clear",
                provider = "Ollama",
                model = "llama3.2",
                baseUrlOperation = "clear",
                protocol = "ollama_native",
                wireApi = "chat_completions",
                authMode = "none",
                authHeaderName = "",
                apiKeyOperation = "clear"
            }));
        valid.StatusCode.Should().Be(HttpStatusCode.OK, await valid.Content.ReadAsStringAsync());
        var stored = host.AiConfigStore.GetAll().Single(model => model.Name == "Ollama Explicit Clear");
        stored.AuthMode.Should().Be("none");
        stored.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAiModel_AuthenticatedProvider_ShouldAllowExplicitKeylessState()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/ai/models",
            JsonContent(new
            {
                name = "OpenAI Keyless Draft",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "clear",
                protocol = "openai_compatible",
                wireApi = "chat_completions",
                authMode = "bearer",
                authHeaderName = "Authorization",
                apiKeyOperation = "clear"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var stored = host.AiConfigStore.GetAll().Single(model => model.Name == "OpenAI Keyless Draft");
        stored.AuthMode.Should().Be("bearer");
        stored.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAiModel_ShouldRejectKnownProviderProtocolMismatch()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/ai/models",
            JsonContent(new
            {
                name = "Anthropic Through OpenAI",
                provider = "Anthropic",
                model = "claude-3-5-sonnet",
                baseUrlOperation = "clear",
                protocol = "openai_compatible",
                wireApi = "chat_completions",
                authMode = "bearer",
                apiKeyOperation = "clear"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("requires protocol");
        host.AiConfigStore.GetAll().Should().NotContain(model => model.Name == "Anthropic Through OpenAI");
    }

    [Fact]
    public async Task UpdateAiModel_ShouldPreserveIsEnabledWhenFieldIsOmitted()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "disabled-model",
            Name = "Disabled Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            IsEnabled = false
        });

        using var response = await host.Client.PutAsync(
            "/api/ai/models/disabled-model",
            JsonContent(new
            {
                name = "Disabled Model Renamed",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrlOperation = "preserve",
                apiKeyOperation = "keep"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("disabled-model")!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task TestAiModel_ShouldPersistLastTestMetadataAndExposeItAfterProjectionReread()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [{ "message": { "content": "{\"ok\":true}" } }]
                }
                """, Encoding.UTF8, "application/json")
            }));
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "test-metadata-model",
            Name = "Test Metadata Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.example/v1",
            ApiKey = "test-key"
        });

        using var testResponse = await host.Client.PostAsync(
            "/api/ai/models/test-metadata-model/test",
            JsonContent(new { }));
        testResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var testDocument = JsonDocument.Parse(await testResponse.Content.ReadAsStringAsync());
        testDocument.RootElement.GetProperty("connectionOk").GetBoolean().Should().BeTrue();

        var persisted = host.AiConfigStore.GetById("test-metadata-model")!;
        persisted.LastTestStatus.Should().Be("ok");
        persisted.LastTestAt.Should().NotBeNull();
        persisted.LastTestLatencyMs.Should().NotBeNull();

        using var modelsResponse = await host.Client.GetAsync("/api/ai/models");
        using var modelsDocument = JsonDocument.Parse(await modelsResponse.Content.ReadAsStringAsync());
        var model = modelsDocument.RootElement.EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == "test-metadata-model");
        model.GetProperty("lastTestStatus").GetString().Should().Be("ok");
        model.GetProperty("lastTestAt").GetString().Should().NotBeNullOrWhiteSpace();
        model.GetProperty("lastTestLatencyMs").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task TestAiModel_WhenCallerAborts_ShouldNotConvertAbortIntoPersistedTimeoutResult()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "aborted-test-model",
            Name = "Aborted Test Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.example/v1",
            ApiKey = "test-key",
            TimeoutMs = 300000
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/ai/models/aborted-test-model/test")
        {
            Content = JsonContent(new { })
        };
        using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var send = () => host.Client.SendAsync(request, abort.Token);
        await FluentActions.Awaiting(send).Should().ThrowAsync<OperationCanceledException>();

        var persisted = host.AiConfigStore.GetById("aborted-test-model")!;
        persisted.LastTestStatus.Should().Be("untested");
        persisted.LastTestAt.Should().BeNull();
        persisted.LastTestLatencyMs.Should().BeNull();
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
            apiKeyOperation = "replace",
            baseUrlOperation = "replace",
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
            baseUrlOperation = "replace",
            wireApi = "responses",
            apiKey = "created-secret-key",
            apiKeyOperation = "replace",
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
        createdModel.GetProperty("wireApi").GetString().Should().Be("responses");

        var reloaded = host.CreateReloadedStore();
        reloaded.GetById(createdId!)!.ApiKey.Should().Be("created-secret-key");
        reloaded.GetById(createdId!)!.WireApi.Should().Be("responses");
    }

    [Fact]
    public async Task UpdateAiModel_ShouldKeepReplaceClearKeyAndRoutePlannerShadowRoles()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync();
        var originalKey = "old" + "-endpoint-key";
        var replacementKey = "new" + "-endpoint-key";
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "route-model",
            Name = "Route Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = originalKey,
            RoleBindings = new List<string> { AiModelConfig.RoleGeneration },
            IsEnabled = true,
            Priority = 20
        });

        using var keepResponse = await host.Client.PutAsync(
            "/api/ai/models/route-model",
            JsonContent(new
            {
                name = "Route Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrl = "https://api.openai.com/v1",
                baseUrlOperation = "replace",
                apiKeyOperation = "keep",
                roleBindings = new[] { "planner" },
                isEnabled = true,
                priority = 10
            }));
        keepResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("route-model")!.ApiKey.Should().Be(originalKey);

        using var replaceResponse = await host.Client.PutAsync(
            "/api/ai/models/route-model",
            JsonContent(new
            {
                name = "Route Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrl = "https://api.openai.com/v1",
                baseUrlOperation = "replace",
                apiKey = replacementKey,
                apiKeyOperation = "replace",
                roleBindings = new[] { "planner", "vision-agent-shadow-eval" },
                isEnabled = true,
                priority = 5
            }));
        replaceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("route-model")!.ApiKey.Should().Be(replacementKey);

        using var plannerResponse = await host.Client.PostAsync("/api/ai/models/route-model/default-planner", JsonContent(new { }));
        plannerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("route-model")!.RoleBindings.Should().Contain(AiModelConfig.RolePlanner);

        using var shadowResponse = await host.Client.PostAsync("/api/ai/models/route-model/default-shadow-eval", JsonContent(new { }));
        shadowResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("route-model")!.RoleBindings.Should().Contain(AiModelConfig.RoleShadowEval);

        using var clearResponse = await host.Client.PutAsync(
            "/api/ai/models/route-model",
            JsonContent(new
            {
                name = "Route Model",
                provider = "OpenAI Compatible",
                model = "gpt-4o-mini",
                baseUrl = "https://api.openai.com/v1",
                baseUrlOperation = "replace",
                apiKeyOperation = "clear",
                roleBindings = new[] { "planner", "vision-agent-shadow-eval" },
                isEnabled = true,
                priority = 5
            }));
        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.AiConfigStore.GetById("route-model")!.ApiKey.Should().BeEmpty();

        using var modelsResponse = await host.Client.GetAsync("/api/ai/models");
        var modelsJson = await modelsResponse.Content.ReadAsStringAsync();
        modelsJson.Should().NotContain(originalKey);
        modelsJson.Should().NotContain(replacementKey);
        using var modelsDocument = JsonDocument.Parse(modelsJson);
        var model = modelsDocument.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == "route-model");
        model.TryGetProperty("apiKey", out _).Should().BeFalse();
        model.GetProperty("hasApiKey").GetBoolean().Should().BeFalse();
        model.GetProperty("apiKeyMasked").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task TestAiModel_ShouldClassifyMissingKeyInvalidBaseUrlAuthAndTimeout()
    {
        await using var missingHost = await AiModelEndpointTestHost.CreateAsync();
        missingHost.AiConfigStore.Add(new AiModelConfig
        {
            Id = "missing-key",
            Name = "Missing Key",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = ""
        });
        using var missingResponse = await missingHost.Client.PostAsync("/api/ai/models/missing-key/test", JsonContent(new { }));
        var missingJson = await missingResponse.Content.ReadAsStringAsync();
        using (var missingDocument = JsonDocument.Parse(missingJson))
        {
            missingDocument.RootElement.GetProperty("connectionOk").GetBoolean().Should().BeFalse();
            missingDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("missing_api_key");
            missingJson.Should().NotContain("api.openai.com/v1");
        }

        await using var invalidHost = await AiModelEndpointTestHost.CreateAsync();
        invalidHost.AiConfigStore.Add(new AiModelConfig
        {
            Id = "invalid-base",
            Name = "Invalid Base",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "not a url",
            ApiKey = "key"
        });
        using var invalidResponse = await invalidHost.Client.PostAsync("/api/ai/models/invalid-base/test", JsonContent(new { }));
        using (var invalidDocument = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync()))
        {
            invalidDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("base_url_error");
        }

        await using var authHost = await AiModelEndpointTestHost.CreateAsync((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"denied\"}", Encoding.UTF8, "application/json")
            }));
        authHost.AiConfigStore.Add(new AiModelConfig
        {
            Id = "auth-fail",
            Name = "Auth Fail",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "key"
        });
        using var authResponse = await authHost.Client.PostAsync("/api/ai/models/auth-fail/test", JsonContent(new { }));
        using (var authDocument = JsonDocument.Parse(await authResponse.Content.ReadAsStringAsync()))
        {
            authDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("auth_failed");
            authDocument.RootElement.GetProperty("sanitizedMessage").GetString().Should().NotContain("key");
        }

        await using var timeoutHost = await AiModelEndpointTestHost.CreateAsync(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        timeoutHost.AiConfigStore.Add(new AiModelConfig
        {
            Id = "timeout-model",
            Name = "Timeout Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "key",
            TimeoutMs = 1000
        });
        using var timeoutResponse = await timeoutHost.Client.PostAsync("/api/ai/models/timeout-model/test", JsonContent(new { }));
        using (var timeoutDocument = JsonDocument.Parse(await timeoutResponse.Content.ReadAsStringAsync()))
        {
            timeoutDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("timeout");
        }
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

    [Theory]
    [InlineData(null, false)]
    [InlineData("Admin", true)]
    [InlineData("Engineer", true)]
    [InlineData("Operator", true)]
    public async Task PreviewReasoningSupport_ShouldRequireAuthenticatedSession(string? userRole, bool shouldReturnProjection)
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: userRole);
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
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!shouldReturnProjection)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, responseJson);
            responseJson.Should().Contain("AuthenticatedSessionRequired");
            responseJson.Should().Contain("RequireAuthenticated");
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseJson);
        using var document = JsonDocument.Parse(responseJson);
        document.RootElement.GetProperty("familyId").GetString().Should().Be("openai_gpt5");
    }

    [Fact]
    public async Task RuntimePreviewPilotEndpoints_ShouldManageConfigCatalogAndReadinessWithoutLeakingSecrets()
    {
        var appConfig = new AppConfig
        {
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-a",
                    DisplayName = "Line Camera",
                    IpAddress = string.Empty
                }
            ],
            Runtime = new RuntimeConfig
            {
                RuntimePreviewPilot = new RuntimePreviewPilotConfig
                {
                    Enabled = true,
                    AllowedCameraBindingIds = ["cam-a"],
                    AllowedTemplateIds = ["template-a"]
                }
            }
        };
        appConfig.Normalize();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);
        host.AiConfigStore.Add(new AiModelConfig
        {
            Id = "model-a",
            Name = "Shadow Model",
            Provider = "OpenAI Compatible",
            BaseUrl = "https://example.invalid/v1",
            ApiKey = "runtime-preview-secret"
        });

        using var getConfigResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/config");
        getConfigResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var invalidPutResponse = await host.Client.PutAsync(
            "/api/settings/runtime-preview-pilot/config",
            JsonContent(new
            {
                enabled = true,
                mode = "metadata_only",
                allowedCameraBindingIds = new[] { "redacted-ip-token" },
                denyExternalPath = true,
                denyImageBytes = true
            }));
        invalidPutResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var validPutResponse = await host.Client.PutAsync(
            "/api/settings/runtime-preview-pilot/config",
            JsonContent(new
            {
                enabled = true,
                mode = "metadata_only",
                allowedCameraBindingIds = new[] { "cam-a" },
                allowedTemplateIds = new[] { "template-a" },
                fallbackToOffline = true,
                denyExternalPath = true,
                denyImageBytes = true
            }));
        validPutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var catalogResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/catalog");
        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var catalogJson = await catalogResponse.Content.ReadAsStringAsync();
        catalogJson.Should().Contain("cam-a");
        catalogJson.Should().Contain("model-a");
        catalogJson.Should().Contain("<redacted>");
        catalogJson.Should().NotContain("runtime-preview-secret");
        catalogJson.Should().NotContain("redacted-ip-token");
        catalogJson.Should().NotContain("example.invalid/v1");

        using var readyResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/readiness",
            JsonContent(new
            {
                config = new
                {
                    enabled = true,
                    mode = "metadata_only",
                    allowedCameraBindingIds = new[] { "cam-a" },
                    allowedTemplateIds = new[] { "template-a" },
                    fallbackToOffline = true,
                    denyExternalPath = true,
                    denyImageBytes = true
                },
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new
                {
                    flow = RuntimePreviewFlow("cam-a", templateId: "template-a")
                }
            }));
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var readyDocument = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync()))
        {
            var readiness = readyDocument.RootElement.GetProperty("readiness");
            readiness.GetProperty("status").GetString().Should().Be("ready");
            readiness.GetProperty("canRunMetadataPilot").GetBoolean().Should().BeTrue();
            readiness.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        }

        using var deniedResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/readiness",
            JsonContent(new
            {
                config = new
                {
                    enabled = true,
                    mode = "metadata_only",
                    allowedCameraBindingIds = new[] { "cam-a" },
                    fallbackToOffline = true,
                    denyExternalPath = true,
                    denyImageBytes = true
                },
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new
                {
                    flow = RuntimePreviewFlow("cam-a", templatePath: "external:/blocked-template")
                }
            }));
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deniedJson = await deniedResponse.Content.ReadAsStringAsync();
        deniedJson.Should().NotContain("secret");
        deniedJson.Should().NotContain(".png");
        using (var deniedDocument = JsonDocument.Parse(deniedJson))
        {
            var readiness = deniedDocument.RootElement.GetProperty("readiness");
            readiness.GetProperty("status").GetString().Should().Be("denied");
            readiness.GetProperty("canRunMetadataPilot").GetBoolean().Should().BeFalse();
            readiness.GetProperty("fallback").GetProperty("used").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task RuntimePreviewPilotReadinessEndpoint_ShouldRequireAdminBrokerGate()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: "Engineer");

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/readiness",
            JsonContent(new
            {
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a") }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("runtime_preview_endpoint_admin_required");
        json.Should().NotContain("secret");
    }

    [Fact]
    public async Task RuntimePreviewPilotSessionEndpoints_ShouldRunMetadataSimulationAndReturnReport()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);
        host.Client.DefaultRequestHeaders.Add("X-CV-Developer-UI", "true");

        using var simulateResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/simulate",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true
            }));

        simulateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var simulateJson = await simulateResponse.Content.ReadAsStringAsync();
        simulateJson.Should().Contain("session_created");
        simulateJson.Should().Contain("simulation_completed");
        simulateJson.Should().Contain("report_generated");
        simulateJson.Should().NotContain("redacted-ip-token");
        using var simulateDocument = JsonDocument.Parse(simulateJson);
        var report = simulateDocument.RootElement.GetProperty("report");
        report.GetProperty("previewReady").GetBoolean().Should().BeTrue();
        report.GetProperty("realResourcesTouched").GetBoolean().Should().BeFalse();
        report.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        var sessionId = simulateDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString();
        sessionId.Should().NotBeNullOrWhiteSpace();

        using var listResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/sessions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        listJson.Should().Contain(sessionId!);

        using var reportResponse = await host.Client.GetAsync($"/api/settings/runtime-preview-pilot/sessions/{sessionId}/report");
        reportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reportJson = await reportResponse.Content.ReadAsStringAsync();
        reportJson.Should().Contain("runtime_preview_session_metadata");
        reportJson.Should().NotContain("blocked-template");
    }

    [Fact]
    public async Task RuntimePreviewPilotSessionCancel_ShouldRecordCancelledStatus()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);
        using var createResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true
            }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString();

        using var cancelResponse = await host.Client.PostAsync(
            $"/api/settings/runtime-preview-pilot/sessions/{sessionId}/cancel",
            JsonContent(new { }));

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cancelDocument = JsonDocument.Parse(await cancelResponse.Content.ReadAsStringAsync());
        cancelDocument.RootElement.GetProperty("session").GetProperty("status").GetString()
            .Should().Be(RuntimePreviewSessionStatuses.Cancelled);
    }

    [Fact]
    public async Task RuntimePreviewPilotSessionSimulation_ShouldDenyDangerousPathWithoutArtifact()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/simulate",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templatePath: "external:/blocked-template") },
                runtimePreviewConsent = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("secret");
        json.Should().NotContain("blocked-template");
        using var document = JsonDocument.Parse(json);
        var report = document.RootElement.GetProperty("report");
        report.GetProperty("previewReady").GetBoolean().Should().BeFalse();
        report.GetProperty("realResourcesTouched").GetBoolean().Should().BeFalse();
        report.GetProperty("permissionDecision").GetProperty("dangerousDenied").GetBoolean().Should().BeTrue();
        report.GetProperty("simulation").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task RuntimePreviewPilotSessionSimulation_ShouldKeepWorkflowDraftAllowedWhenNotReady()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/simulate",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                arguments = new { flow = RuntimePreviewFlow("cam-missing", templateId: "template-a") },
                runtimePreviewConsent = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("report");
        report.GetProperty("previewReady").GetBoolean().Should().BeFalse();
        report.GetProperty("readiness").GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        report.GetProperty("readiness").GetProperty("pendingActions").GetArrayLength().Should().BeGreaterThan(0);
        report.GetProperty("permissionDecision").GetProperty("status").GetString().Should().Be("denied");
    }

    [Fact]
    public async Task RuntimePreviewPilotSessionReplayExportAndDeployReadiness_ShouldRemainMetadataOnly()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var simulateResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/simulate",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true
            }));
        simulateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var simulateDocument = JsonDocument.Parse(await simulateResponse.Content.ReadAsStringAsync());
        var sessionId = simulateDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString();

        using var replayResponse = await host.Client.GetAsync($"/api/settings/runtime-preview-pilot/sessions/{sessionId}/replay");
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayJson = await replayResponse.Content.ReadAsStringAsync();
        replayJson.Should().Contain("session_replayed");
        replayJson.Should().Contain("\"realResourcesTouched\":false");
        replayJson.Should().NotContain("redacted-ip-token");

        using var exportResponse = await host.Client.GetAsync($"/api/settings/runtime-preview-pilot/sessions/{sessionId}/report/export");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportJson = await exportResponse.Content.ReadAsStringAsync();
        exportJson.Should().Contain("metadata-only.json");
        exportJson.Should().Contain("\"metadataOnly\":true");
        exportJson.Should().NotContain("blocked-template");

        using var deployResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/deploy-readiness",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true,
                requireReplay = true
            }));
        deployResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var deployDocument = JsonDocument.Parse(await deployResponse.Content.ReadAsStringAsync());
        var readinessReport = deployDocument.RootElement.GetProperty("deployReadinessReport");
        readinessReport.GetProperty("readyForDeployment").GetBoolean().Should().BeTrue();
        readinessReport.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        readinessReport.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();
        readinessReport.GetProperty("realResourcesTouched").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RuntimePreviewPilotDeployReadiness_ShouldKeepDraftEditableWhenPrecheckNotReady()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/deploy-readiness",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-missing", templateId: "template-a") },
                runtimePreviewConsent = true,
                requireReplay = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("deployReadinessReport");
        report.GetProperty("previewReady").GetBoolean().Should().BeFalse();
        report.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        report.GetProperty("deploymentBlocked").GetBoolean().Should().BeTrue();
        report.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        report.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RuntimePreviewPilotScenarioEvidenceEndpoint_ShouldReturnSafeBusinessScenarioSet()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/scenario-evidence");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("wire_sequence");
        json.Should().Contain("template_matching");
        json.Should().Contain("hole_distance");
        json.Should().Contain("remote_control_detection");
        json.Should().Contain("allowlist_mismatch");
        json.Should().Contain("\"caseCount\":15");
        json.Should().Contain("\"realResourcesTouched\":false");
        json.Should().NotContain("redacted-ip-token");
        json.Should().NotContain("DB1");
        json.Should().NotContain("external:/");
    }

    [Fact]
    public async Task RuntimePreviewPilotRetentionCleanupEndpoint_ShouldReturnPersistentGovernanceEvidence()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);
        await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/simulate",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true
            }));

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/retention/cleanup",
            JsonContent(new { retentionDays = 30, maxSessions = 200 }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cleanup = document.RootElement.GetProperty("cleanup");
        cleanup.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        cleanup.GetProperty("realResourcesTouched").GetBoolean().Should().BeFalse();
        cleanup.GetProperty("sessionsBefore").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RuntimePreviewPilotV11Endpoints_ShouldRequireAdminBrokerGate()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: "Engineer");

        using var replayResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/sessions/rp_session_missing/replay");
        using var exportResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/sessions/rp_session_missing/report/export");
        using var deployResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/deploy-readiness",
            JsonContent(new { arguments = new { flow = RuntimePreviewFlow("cam-a") }, runtimePreviewConsent = true }));
        using var scenarioResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/scenario-evidence");
        using var cleanupResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/retention/cleanup",
            JsonContent(new { retentionDays = 30, maxSessions = 200 }));

        replayResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deployResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        scenarioResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RuntimePreviewPilotV12Endpoints_ShouldReturnScenarioCorpusPackageReadinessAndGovernanceEvidence()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var corpusResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/scenario-corpus");
        corpusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpusJson = await corpusResponse.Content.ReadAsStringAsync();
        corpusJson.Should().Contain("\"caseCount\":15");
        corpusJson.Should().Contain("draft_editable_package_blocked");
        corpusJson.Should().NotContain("redacted-ip-token");

        using var packageResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/package-readiness",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true
            }));
        packageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var packageDocument = JsonDocument.Parse(await packageResponse.Content.ReadAsStringAsync());
        var packageReport = packageDocument.RootElement.GetProperty("packageReadinessReport");
        var sessionId = packageReport.GetProperty("sessionId").GetString();
        packageReport.GetProperty("readyForPackage").GetBoolean().Should().BeTrue();
        packageReport.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        packageReport.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();

        using var indexResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/governance/index");
        indexResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var indexJson = await indexResponse.Content.ReadAsStringAsync();
        indexJson.Should().Contain("jsonl.v4");
        indexJson.Should().Contain("packageReadinessReportCount");
        indexJson.Should().Contain("manifestDryRunReportCount");

        using var lookupResponse = await host.Client.GetAsync($"/api/settings/runtime-preview-pilot/governance/lookup?sessionId={sessionId}&caseId=RP-SC-001");
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lookupJson = await lookupResponse.Content.ReadAsStringAsync();
        lookupJson.Should().Contain("packageReadinessReport");
        lookupJson.Should().Contain("wire_sequence");

        using var explanationResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/agent-explanation-benchmark");
        explanationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var explanationJson = await explanationResponse.Content.ReadAsStringAsync();
        explanationJson.Should().Contain("\"accepted\":true");
        explanationJson.Should().Contain("nextEngineerAction");
    }

    [Fact]
    public async Task RuntimePreviewPilotPackageReadiness_ShouldBlockNotReadyWithoutCreatingPackage()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/package-readiness",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-missing", templateId: "template-a") },
                runtimePreviewConsent = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("packageReadinessReport");
        report.GetProperty("readyForPackage").GetBoolean().Should().BeFalse();
        report.GetProperty("packageBlocked").GetBoolean().Should().BeTrue();
        report.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        report.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        report.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();
        report.GetProperty("riskSummary").GetString().Should().Contain("blocked");
    }

    [Fact]
    public async Task RuntimePreviewPilotV12Endpoints_ShouldRequireAdminBrokerGate()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: "Engineer");

        using var corpusResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/scenario-corpus");
        using var packageResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/package-readiness",
            JsonContent(new { arguments = new { flow = RuntimePreviewFlow("cam-a") }, runtimePreviewConsent = true }));
        using var indexResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/governance/index");
        using var exportResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/governance/export");
        using var lookupResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/governance/lookup?caseId=RP-SC-001");
        using var explanationResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/agent-explanation-benchmark");

        corpusResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        packageResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        indexResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        explanationResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RuntimePreviewPilotV13Endpoints_ShouldReturnRedactedCorpusManifestDryRunAndLookup()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var corpusResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/redacted-flow-corpus");
        corpusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpusJson = await corpusResponse.Content.ReadAsStringAsync();
        using var corpusDocument = JsonDocument.Parse(corpusJson);
        corpusDocument.RootElement.GetProperty("corpus").GetProperty("caseCount").GetInt32()
            .Should().BeGreaterThanOrEqualTo(60);
        corpusJson.Should().Contain("remote_control_defect");
        corpusJson.Should().Contain("image_bytes_denied_final");
        corpusJson.Should().Contain("stationProfileId");
        corpusJson.Should().Contain("expectedReleaseReviewDecision");
        corpusJson.Should().NotContain("redacted-ip-token");
        corpusJson.Should().NotContain("base64_image_denied");
        corpusJson.Should().NotContain(".cvpkg");

        using var manifestResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/manifest-dry-run",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true
            }));
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var manifestDocument = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync());
        var manifest = manifestDocument.RootElement.GetProperty("manifestDryRunReport");
        var manifestId = manifest.GetProperty("manifestId").GetString();
        manifest.GetProperty("packageReviewAllowed").GetBoolean().Should().BeTrue();
        manifest.GetProperty("manifestArtifactGenerated").GetBoolean().Should().BeFalse();
        manifest.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        manifest.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();
        manifest.GetProperty("dependencyTrace").GetArrayLength().Should().BeGreaterThan(0);

        using var lookupResponse = await host.Client.GetAsync($"/api/settings/runtime-preview-pilot/governance/lookup?manifestId={manifestId}&caseId=RP-RF-001");
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lookupJson = await lookupResponse.Content.ReadAsStringAsync();
        lookupJson.Should().Contain("manifest");
        lookupJson.Should().Contain("redactedFlowCase");
        lookupJson.Should().NotContain(".cvpkg");
    }

    [Fact]
    public async Task RuntimePreviewPilotV14Endpoints_ShouldReturnStationProfilesContractsPreReleaseReviewAndLookup()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var profilesResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/station-profiles");
        profilesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profilesJson = await profilesResponse.Content.ReadAsStringAsync();
        profilesJson.Should().Contain("sp-release-standard-v14");
        profilesJson.Should().Contain("\"plcWriteAllowed\":false");
        profilesJson.Should().Contain("\"networkPolicy\":\"redacted\"");
        profilesJson.Should().NotContain("redacted-ip-token");

        using var registryResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/operator-contract-registry");
        registryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var registryJson = await registryResponse.Content.ReadAsStringAsync();
        registryJson.Should().Contain("ImageAcquisition");
        registryJson.Should().Contain("TemplateMatching");
        registryJson.Should().Contain("CircleMeasurement");
        registryJson.Should().Contain("MeasureDistance");
        registryJson.Should().Contain("DeepLearning");
        registryJson.Should().Contain("ResultOutput");
        registryJson.Should().Contain("operator-contract-registry.final.metadata-only");

        using var reviewResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/pre-release-review",
            JsonContent(new
            {
                caseId = "RP-RF-ENDPOINT",
                stationProfileId = "sp-release-standard-v14",
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-a", templateId: "template-a") },
                runtimePreviewConsent = true,
                requireReplay = true
            }));
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reviewDocument = JsonDocument.Parse(await reviewResponse.Content.ReadAsStringAsync());
        var review = reviewDocument.RootElement.GetProperty("preReleaseReviewReport");
        var reviewId = review.GetProperty("reviewId").GetString();
        var manifestId = review.GetProperty("manifestId").GetString();
        review.GetProperty("caseId").GetString().Should().Be("RP-RF-ENDPOINT");
        review.GetProperty("stationProfileId").GetString().Should().Be("sp-release-standard-v14");
        review.GetProperty("readinessStatus").GetString().Should().Be("ready");
        review.GetProperty("packageReviewAllowed").GetBoolean().Should().BeTrue();
        review.GetProperty("stationCompatible").GetBoolean().Should().BeTrue();
        review.GetProperty("operatorContractsSatisfied").GetBoolean().Should().BeTrue();
        review.GetProperty("releaseReviewAllowed").GetBoolean().Should().BeTrue();
        review.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        review.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        review.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();
        review.GetProperty("realResourcesTouched").GetBoolean().Should().BeFalse();
        reviewDocument.RootElement.GetProperty("stationCompatibilityReport").GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        reviewDocument.RootElement.GetProperty("operatorContractValidationReport").GetProperty("operatorContractsSatisfied").GetBoolean().Should().BeTrue();

        using var lookupResponse = await host.Client.GetAsync($"/api/settings/runtime-preview-pilot/governance/lookup?reviewId={reviewId}&manifestId={manifestId}&stationProfileId=sp-release-standard-v14&caseId=RP-RF-ENDPOINT");
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lookupJson = await lookupResponse.Content.ReadAsStringAsync();
        lookupJson.Should().Contain("preReleaseReview");
        lookupJson.Should().Contain("stationProfileReports");
        lookupJson.Should().Contain("RP-RF-ENDPOINT");
        lookupJson.Should().NotContain(".cvpkg");
    }

    [Theory]
    [InlineData("/api/settings/runtime-preview-pilot/redacted-flow-corpus", "image_bytes_denied_final")]
    [InlineData("/api/settings/runtime-preview-pilot/redacted-flow-corpus", "remote_control_defect")]
    [InlineData("/api/settings/runtime-preview-pilot/station-profiles", "sp-release-standard-v14")]
    [InlineData("/api/settings/runtime-preview-pilot/station-profiles", "\"plcWriteAllowed\":false")]
    [InlineData("/api/settings/runtime-preview-pilot/station-profiles", "\"networkPolicy\":\"redacted\"")]
    [InlineData("/api/settings/runtime-preview-pilot/operator-contract-registry", "operator-contract-registry.final.metadata-only")]
    [InlineData("/api/settings/runtime-preview-pilot/operator-contract-registry", "TemplateMatching")]
    [InlineData("/api/settings/runtime-preview-pilot/operator-contract-registry", "DeepLearning")]
    [InlineData("/api/settings/runtime-preview-pilot/operator-contract-registry", "ResultOutput")]
    [InlineData("/api/settings/runtime-preview-pilot/agent-explanation-benchmark", "nextEngineerAction")]
    [InlineData("/api/settings/runtime-preview-pilot/agent-explanation-benchmark", "stationCompatibilityExplanation")]
    [InlineData("/api/settings/runtime-preview-pilot/agent-explanation-benchmark", "releaseDecisionExplanation")]
    public async Task RuntimePreviewPilotFinalReadOnlyEndpoints_ShouldExposeMetadataOnlyContracts(
        string endpoint,
        string expectedToken)
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.GetAsync(endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain(expectedToken);
        json.Should().Contain("\"metadataOnly\":true");
        json.Should().Contain("\"realResourcesTouched\":false");
        json.Should().NotContain("redacted-ip-token");
        json.Should().NotContain("base64_image_denied");
        json.Should().NotContain(".cvpkg");
    }

    [Fact]
    public async Task RuntimePreviewPilotManifestDryRun_ShouldBlockMissingDependenciesWithoutPackageArtifact()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/manifest-dry-run",
            JsonContent(new
            {
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewFlow("cam-missing", templateId: "template-a") },
                runtimePreviewConsent = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var manifest = document.RootElement.GetProperty("manifestDryRunReport");
        manifest.GetProperty("packageReviewAllowed").GetBoolean().Should().BeFalse();
        manifest.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        manifest.GetProperty("manifestArtifactGenerated").GetBoolean().Should().BeFalse();
        manifest.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        manifest.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();
        manifest.GetProperty("blockedReasons").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RuntimePreviewPilotPreReleaseReview_ShouldRequireApprovalForCompatibleDeepLearning()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/pre-release-review",
            JsonContent(new
            {
                caseId = "RP-RF-ENDPOINT-DL",
                stationProfileId = "sp-dl-review-v14",
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewModelFlow("cam-a", "model-a") },
                runtimePreviewConsent = true,
                requireReplay = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("preReleaseReviewReport");
        report.GetProperty("packageReviewAllowed").GetBoolean().Should().BeTrue();
        report.GetProperty("stationCompatible").GetBoolean().Should().BeTrue();
        report.GetProperty("operatorContractsSatisfied").GetBoolean().Should().BeTrue();
        report.GetProperty("releaseReviewAllowed").GetBoolean().Should().BeFalse();
        report.GetProperty("requiresEngineerApproval").GetBoolean().Should().BeTrue();
        report.GetProperty("engineerActions").GetArrayLength().Should().BeGreaterThan(0);
        report.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        report.GetProperty("realResourcesTouched").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RuntimePreviewPilotPreReleaseReview_ShouldBlockDeepLearningOnTraditionalStation()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/pre-release-review",
            JsonContent(new
            {
                caseId = "RP-RF-ENDPOINT-DL-BLOCKED",
                stationProfileId = "sp-release-standard-v14",
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewModelFlow("cam-a", "model-a") },
                runtimePreviewConsent = true,
                requireReplay = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("preReleaseReviewReport");
        report.GetProperty("stationCompatible").GetBoolean().Should().BeFalse();
        report.GetProperty("releaseReviewAllowed").GetBoolean().Should().BeFalse();
        report.GetProperty("blockedReasons").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(reason => reason!.Contains("DeepLearning", StringComparison.OrdinalIgnoreCase));
        report.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        report.GetProperty("deploymentExecuted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RuntimePreviewPilotPreReleaseReview_ShouldBlockMissingOperatorContractParameter()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/pre-release-review",
            JsonContent(new
            {
                caseId = "RP-RF-ENDPOINT-CONTRACT-BLOCKED",
                stationProfileId = "sp-release-standard-v14",
                config = appConfig.Runtime.RuntimePreviewPilot,
                toolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                arguments = new { flow = RuntimePreviewMissingTemplateFlow("cam-a") },
                runtimePreviewConsent = true,
                requireReplay = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("preReleaseReviewReport");
        report.GetProperty("operatorContractsSatisfied").GetBoolean().Should().BeFalse();
        report.GetProperty("releaseReviewAllowed").GetBoolean().Should().BeFalse();
        report.GetProperty("blockedReasons").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(reason => reason!.Contains("TemplateId", StringComparison.OrdinalIgnoreCase));
        document.RootElement.GetProperty("operatorContractValidationReport").GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RuntimePreviewPilotGovernanceLookup_ShouldReturnStationProfileByStationProfileId()
    {
        var appConfig = RuntimePreviewEndpointConfig();
        await using var host = await AiModelEndpointTestHost.CreateAsync(appConfig: appConfig);

        using var response = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/governance/lookup?stationProfileId=sp-release-standard-v14");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("stationProfile");
        json.Should().Contain("sp-release-standard-v14");
        json.Should().Contain("\"networkPolicy\":\"redacted\"");
        json.Should().NotContain("redacted-ip-token");
    }

    [Fact]
    public async Task RuntimePreviewPilotV13Endpoints_ShouldRequireAdminBrokerGate()
    {
        await using var host = await AiModelEndpointTestHost.CreateAsync(userRole: "Engineer");

        using var corpusResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/redacted-flow-corpus");
        using var manifestResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/manifest-dry-run",
            JsonContent(new { arguments = new { flow = RuntimePreviewFlow("cam-a") }, runtimePreviewConsent = true }));
        using var lookupResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/governance/lookup?manifestId=rp_manifest_dry_run_test");
        using var profilesResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/station-profiles");
        using var registryResponse = await host.Client.GetAsync("/api/settings/runtime-preview-pilot/operator-contract-registry");
        using var reviewResponse = await host.Client.PostAsync(
            "/api/settings/runtime-preview-pilot/sessions/pre-release-review",
            JsonContent(new { arguments = new { flow = RuntimePreviewFlow("cam-a") }, runtimePreviewConsent = true }));

        corpusResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        profilesResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        registryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static StringContent JsonContent(object payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private static object RuntimePreviewFlow(
        string cameraBindingId,
        string? templateId = null,
        string? templatePath = null)
    {
        var templateParameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            templateParameters["TemplatePath"] = templatePath;
        }
        else
        {
            templateParameters["TemplateId"] = templateId ?? "template-a";
        }

        return new
        {
            operators = new object[]
            {
                new
                {
                    tempId = "op_cam",
                    operatorType = "ImageAcquisition",
                    parameters = new Dictionary<string, string>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraBindingId"] = cameraBindingId
                    }
                },
                new
                {
                    tempId = "op_template",
                    operatorType = "TemplateMatching",
                    parameters = templateParameters
                }
            },
            connections = new object[]
            {
                new
                {
                    sourceTempId = "op_cam",
                    sourcePortName = "Image",
                    targetTempId = "op_template",
                    targetPortName = "Image"
                }
            }
        };
    }

    private static object RuntimePreviewModelFlow(string cameraBindingId, string modelId)
    {
        return new
        {
            operators = new object[]
            {
                new
                {
                    tempId = "op_cam",
                    operatorType = "ImageAcquisition",
                    parameters = new Dictionary<string, string>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraBindingId"] = cameraBindingId
                    }
                },
                new
                {
                    tempId = "op_model",
                    operatorType = "DeepLearning",
                    parameters = new Dictionary<string, string>
                    {
                        ["ModelId"] = modelId,
                        ["ModelKind"] = "detection"
                    }
                },
                new
                {
                    tempId = "op_output",
                    operatorType = "ResultOutput",
                    parameters = new Dictionary<string, string>
                    {
                        ["OutputChannelId"] = "qa-metadata"
                    }
                }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object RuntimePreviewMissingTemplateFlow(string cameraBindingId)
    {
        return new
        {
            operators = new object[]
            {
                new
                {
                    tempId = "op_cam",
                    operatorType = "ImageAcquisition",
                    parameters = new Dictionary<string, string>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraBindingId"] = cameraBindingId
                    }
                },
                new
                {
                    tempId = "op_template",
                    operatorType = "TemplateMatching",
                    parameters = new Dictionary<string, string>()
                },
                new
                {
                    tempId = "op_output",
                    operatorType = "ResultOutput",
                    parameters = new Dictionary<string, string>
                    {
                        ["OutputChannelId"] = "qa-metadata"
                    }
                }
            },
            connections = Array.Empty<object>()
        };
    }

    private static AppConfig RuntimePreviewEndpointConfig()
    {
        var appConfig = new AppConfig
        {
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-a",
                    DisplayName = "Line Camera",
                    IpAddress = string.Empty
                }
            ],
            Runtime = new RuntimeConfig
            {
                RuntimePreviewPilot = new RuntimePreviewPilotConfig
                {
                    Enabled = true,
                    Mode = RuntimePreviewPilotConfig.ModeMetadataOnly,
                    AllowedCameraBindingIds = ["cam-a"],
                    AllowedTemplateIds = ["template-a"],
                    AllowedModelIds = ["model-a"],
                    FallbackToOffline = true,
                    DenyExternalPath = true,
                    DenyImageBytes = true
                }
            }
        };
        appConfig.Normalize();
        return appConfig;
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

        public IConfigurationService ConfigurationService { get; private init; } = null!;

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
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? aiSendAsync = null,
            AppConfig? appConfig = null,
            string? userRole = "Admin")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            var storageDirectory = Path.Combine(Path.GetTempPath(), $"cv-ai-model-endpoints-{Guid.NewGuid():N}");
            var effectiveConfig = appConfig ?? new AppConfig();
            var configService = Substitute.For<IConfigurationService>();
            configService.LoadAsync().Returns(_ => Task.FromResult(effectiveConfig));
            configService.GetCurrent().Returns(_ => effectiveConfig);
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(configService);
            builder.Services.AddSingleton(Substitute.For<ClearVision.Product.Core.Cameras.ICameraManager>());
            builder.Services.AddSingleton(new RuntimePreviewGovernanceStore(Path.Combine(storageDirectory, "runtime-preview-governance")));
            builder.Services.AddSingleton<RuntimePreviewPilotResourceCatalog>();
            builder.Services.AddSingleton<RuntimePreviewSessionStore>();
            builder.Services.AddSingleton<RuntimePreviewAuditTrail>();
            builder.Services.AddSingleton<RuntimePreviewReportArchive>();
            builder.Services.AddSingleton<RuntimePreviewResourceBroker>();
            builder.Services.AddSingleton<RuntimePreviewPermissionBroker>();
            builder.Services.AddSingleton<RuntimePreviewResourceAllowlistResolver>();
            builder.Services.AddSingleton<RuntimePreviewPilotReadinessGate>();
            builder.Services.AddSingleton<RuntimePreviewSimulatedExecutionHarness>();
            builder.Services.AddSingleton(new RuntimePackagePrecheckTool());
            builder.Services.AddSingleton<RuntimePreviewGovernanceMaintenanceService>();
            builder.Services.AddSingleton<RuntimePreviewDeployReadinessService>();
            builder.Services.AddSingleton<RuntimePackageManifestDryRunService>();
            builder.Services.AddSingleton<RuntimePreviewScenarioEvidenceService>();
            builder.Services.AddSingleton<RuntimePreviewPackageReadinessBridge>();
            builder.Services.AddSingleton<RuntimePreviewScenarioCorpusService>();
            builder.Services.AddSingleton<RuntimePreviewRedactedFlowCorpusService>();
            builder.Services.AddSingleton<RuntimePreviewStationProfileCatalog>();
            builder.Services.AddSingleton<RuntimePreviewOperatorContractRegistry>();
            builder.Services.AddSingleton<RuntimePreviewStationCompatibilityDryRunService>();
            builder.Services.AddSingleton<RuntimePreviewPreReleaseReviewService>();
            builder.Services.AddSingleton<RuntimePreviewAgentExplanationService>();

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
                if (userRole != null)
                {
                    context.Items["CurrentUser"] = new UserSession
                    {
                        UserId = "admin",
                        Username = "admin",
                        Role = userRole
                    };
                }
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new AiModelEndpointTestHost(app, aiConfigStore, storageDirectory)
            {
                ConfigurationService = configService
            };
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
