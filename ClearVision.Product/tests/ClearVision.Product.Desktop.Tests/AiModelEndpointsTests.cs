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
            wireApi = "responses",
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
                apiKey = "",
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
                apiKey = "",
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
                    IpAddress = "192.0.2.20"
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
                allowedCameraBindingIds = new[] { "192.0.2.20:8317" },
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
        catalogJson.Should().NotContain("192.0.2.20");
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
                    flow = RuntimePreviewFlow("cam-a", templatePath: "C:\\secret\\template.png")
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
        simulateJson.Should().NotContain("192.0.2.20");
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
        reportJson.Should().NotContain("template.png");
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
                arguments = new { flow = RuntimePreviewFlow("cam-a", templatePath: "C:\\secret\\template.png") },
                runtimePreviewConsent = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("secret");
        json.Should().NotContain("template.png");
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
                    IpAddress = "192.0.2.20"
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
            string userRole = "Admin")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            var effectiveConfig = appConfig ?? new AppConfig();
            var configService = Substitute.For<IConfigurationService>();
            configService.LoadAsync().Returns(_ => Task.FromResult(effectiveConfig));
            configService.GetCurrent().Returns(_ => effectiveConfig);
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(configService);
            builder.Services.AddSingleton(Substitute.For<ClearVision.Product.Core.Cameras.ICameraManager>());
            builder.Services.AddSingleton<RuntimePreviewPilotResourceCatalog>();
            builder.Services.AddSingleton<RuntimePreviewSessionStore>();
            builder.Services.AddSingleton<RuntimePreviewAuditTrail>();
            builder.Services.AddSingleton<RuntimePreviewReportArchive>();
            builder.Services.AddSingleton<RuntimePreviewResourceBroker>();
            builder.Services.AddSingleton<RuntimePreviewPermissionBroker>();
            builder.Services.AddSingleton<RuntimePreviewResourceAllowlistResolver>();
            builder.Services.AddSingleton<RuntimePreviewPilotReadinessGate>();
            builder.Services.AddSingleton<RuntimePreviewSimulatedExecutionHarness>();

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
                    Role = userRole
                };
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
