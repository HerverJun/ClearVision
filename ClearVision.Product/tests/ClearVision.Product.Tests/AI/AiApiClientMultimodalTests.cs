using System.Net;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

public class AiApiClientMultimodalTests : IDisposable
{
    private readonly List<string> _tempConfigDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempConfigDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact(DisplayName = "AiApiClient should send OpenAI multimodal message content with text and image")]
    public async Task CompleteAsync_OpenAi_ShouldSerializeTextAndImageParts()
    {
        var imagePath = CreateTinyPngFile();
        string? capturedRequestJson = null;

        try
        {
            var handler = new CaptureHandler(async (request, cancellationToken) =>
            {
                capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                var responseJson = """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "{\"ok\":true}"
                      }
                    }
                  ]
                }
                """;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            var configStore = CreateIsolatedConfigStore();
            var apiClient = new AiApiClient(httpClient, configStore);

            var messages = new List<ChatMessage>
            {
                new("user", new List<ChatMessageContentPart>
                {
                    ChatMessageContentPart.TextPart("Please compare these images."),
                    ChatMessageContentPart.ImageFile(imagePath, "high")
                })
            };

            var result = await apiClient.CompleteAsync(
                systemPrompt: "You are a visual QA assistant.",
                messages: messages,
                options: new AiGenerationOptions
                {
                    Provider = "OpenAI",
                    ApiKey = "test-key",
                    Model = "gpt-4o-mini",
                    MaxTokens = 512,
                    TimeoutSeconds = 10
                });

            result.Content.Should().Be("{\"ok\":true}");
            capturedRequestJson.Should().NotBeNullOrWhiteSpace();

            using var doc = JsonDocument.Parse(capturedRequestJson!);
            var requestMessages = doc.RootElement.GetProperty("messages");
            requestMessages.GetArrayLength().Should().Be(2);

            var userMessage = requestMessages[1];
            userMessage.GetProperty("role").GetString().Should().Be("user");
            var content = userMessage.GetProperty("content");
            content.ValueKind.Should().Be(JsonValueKind.Array);
            content.GetArrayLength().Should().Be(2);

            content[0].GetProperty("type").GetString().Should().Be("text");
            content[0].GetProperty("text").GetString().Should().Be("Please compare these images.");

            content[1].GetProperty("type").GetString().Should().Be("image_url");
            content[1].GetProperty("image_url").GetProperty("detail").GetString().Should().Be("high");
            content[1].GetProperty("image_url").GetProperty("url").GetString()
                .Should().StartWith("data:image/png;base64,");
        }
        finally
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }
        }
    }

    [Fact(DisplayName = "AiApiClient stream should parse non-SSE OpenAI compatible JSON payload")]
    public async Task StreamCompleteAsync_OpenAi_ShouldHandleNonSseJsonPayload()
    {
        var responseJson = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"ok\":true}"
              }
            }
          ]
        }
        """;

        var handler = new CaptureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        }));

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);
        var chunks = new List<AiStreamChunk>();

        var result = await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: chunks.Add,
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10
            });

        result.Content.Should().Be("{\"ok\":true}");
        chunks.Count(c => c.ChunkType == AiStreamChunkType.Done).Should().Be(1);
        chunks.Where(c => c.ChunkType == AiStreamChunkType.Content).Select(c => c.Content).Should().Contain("{\"ok\":true}");
    }

    [Fact(DisplayName = "AiApiClient stream should parse response.output_text.delta events")]
    public async Task StreamCompleteAsync_OpenAi_ShouldHandleResponseOutputTextDelta()
    {
        var ssePayload = string.Join("\n",
        [
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"ok\\\":\"}",
            "",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"true}\"}",
            "",
            "data: [DONE]",
            ""
        ]);

        var handler = new CaptureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
        }));

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);
        var chunks = new List<AiStreamChunk>();

        var result = await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: chunks.Add,
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10
            });

        result.Content.Should().Be("{\"ok\":true}");
        chunks.Count(c => c.ChunkType == AiStreamChunkType.Content).Should().BeGreaterThan(0);
        chunks.Count(c => c.ChunkType == AiStreamChunkType.Done).Should().Be(1);
    }

    [Fact(DisplayName = "AiApiClient should call OpenAI Responses API when wireApi is responses")]
    public async Task CompleteAsync_OpenAiResponses_ShouldUseResponsesEndpoint()
    {
        string? capturedUrl = null;
        string? capturedAuthorization = null;
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "output": [
                    {
                      "type": "message",
                      "content": [
                        { "type": "output_text", "text": "{\"ok\":true}" }
                      ]
                    }
                  ],
                  "usage": {
                    "input_tokens": 12,
                    "output_tokens": 6
                  }
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                Protocol = AiModelConfig.ProtocolOpenAiCompatible,
                WireApi = AiModelConfig.WireApiResponses,
                ApiKey = "codex-key",
                Model = "gpt-5.5",
                BaseUrl = "http://192.168.31.152:8317/v1",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.XHigh
            });

        result.Content.Should().Be("{\"ok\":true}");
        result.TokenUsage!.InputTokens.Should().Be(12);
        result.TokenUsage.OutputTokens.Should().Be(6);
        capturedUrl.Should().Be("http://192.168.31.152:8317/v1/responses");
        capturedAuthorization.Should().Be("Bearer codex-key");

        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("model").GetString().Should().Be("gpt-5.5");
        root.GetProperty("instructions").GetString().Should().Be("system");
        root.GetProperty("input")[0].GetProperty("role").GetString().Should().Be("user");
        root.GetProperty("max_output_tokens").GetInt32().Should().Be(256);
        root.GetProperty("reasoning").GetProperty("effort").GetString().Should().Be("xhigh");
        root.TryGetProperty("messages", out _).Should().BeFalse();
        root.TryGetProperty("max_tokens", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "AiApiClient should stream OpenAI Responses API chunks")]
    public async Task StreamCompleteAsync_OpenAiResponses_ShouldParseOutputTextDelta()
    {
        string? capturedUrl = null;
        var ssePayload = string.Join("\n",
        [
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"ok\\\":\"}",
            "",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"true}\"}",
            "",
            "data: {\"type\":\"response.output_text.done\",\"text\":\"{\\\"ok\\\":true}\"}",
            "",
            "data: {\"type\":\"response.completed\",\"output_text\":\"{\\\"ok\\\":true}\",\"response\":{\"usage\":{\"input_tokens\":10,\"output_tokens\":4}}}",
            "",
            "data: [DONE]",
            ""
        ]);

        var handler = new CaptureHandler((request, _) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            });
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);
        var chunks = new List<AiStreamChunk>();

        var result = await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: chunks.Add,
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                WireApi = AiModelConfig.WireApiResponses,
                ApiKey = "codex-key",
                Model = "gpt-5.5",
                BaseUrl = "http://192.168.31.152:8317/v1",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.XHigh
            });

        capturedUrl.Should().Be("http://192.168.31.152:8317/v1/responses");
        result.Content.Should().Be("{\"ok\":true}");
        result.TokenUsage!.InputTokens.Should().Be(10);
        result.TokenUsage.OutputTokens.Should().Be(4);
        chunks.Count(c => c.ChunkType == AiStreamChunkType.Content).Should().Be(2);
        chunks.Count(c => c.ChunkType == AiStreamChunkType.Done).Should().Be(1);
    }

    [Fact(DisplayName = "AiApiClient stream should ignore null usage chunks")]
    public async Task StreamCompleteAsync_OpenAi_ShouldIgnoreNullUsageChunks()
    {
        var ssePayload = string.Join("\n",
        [
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"\"}}],\"usage\":null}",
            "",
            "data: {\"choices\":[{\"delta\":{\"content\":\"{\\\"ok\\\":true}\"}}],\"usage\":null}",
            "",
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":null}",
            "",
            "data: [DONE]",
            ""
        ]);

        var handler = new CaptureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
        }));

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: _ => { },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "deepseek-chat",
                BaseUrl = "https://api.deepseek.com/v1",
                MaxTokens = 256,
                TimeoutSeconds = 10
            });

        result.Content.Should().Be("{\"ok\":true}");
        result.TokenUsage.Should().BeNull();
    }

    [Fact(DisplayName = "AiApiClient OpenAI request snapshot should include JSON mode when capability allows it")]
    public async Task CompleteAsync_OpenAi_ShouldMatchRequestSnapshot()
    {
        string? capturedUrl = null;
        string? capturedAuthorization = null;
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return CreateOpenAiJsonResponse();
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system json",
            messages: new List<ChatMessage> { new("user", "return json") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                Protocol = AiModelConfig.ProtocolOpenAiCompatible,
                ApiKey = "openai-key",
                Model = "gpt-4o-mini",
                MaxTokens = 256,
                Temperature = 0.2,
                TimeoutSeconds = 10,
                Capabilities = new AiModelCapabilities { SupportsJsonMode = true }
            });

        capturedUrl.Should().Be("https://api.openai.com/v1/chat/completions");
        capturedAuthorization.Should().Be("Bearer openai-key");
        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        root.GetProperty("max_tokens").GetInt32().Should().Be(256);
        root.GetProperty("temperature").GetDouble().Should().Be(0.2);
        root.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        root.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("system");
        root.GetProperty("messages")[1].GetProperty("content").GetString().Should().Be("return json");
    }

    [Fact(DisplayName = "AiApiClient OpenAI native tool call should serialize tools and parse tool_calls")]
    public async Task CompleteWithToolsAsync_OpenAi_ShouldSerializeAndParseToolCalls()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": null,
                        "tool_calls": [
                          {
                            "id": "call_schema_1",
                            "type": "function",
                            "function": {
                              "name": "get_operator_schema",
                              "arguments": "{\"operatorType\":\"ImageAcquisition\"}"
                            }
                          }
                        ]
                      }
                    }
                  ],
                  "usage": { "prompt_tokens": 11, "completion_tokens": 7 }
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.CompleteWithToolsAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "inspect schema") },
            tools: [CreateToolDefinition("get_operator_schema")],
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                Protocol = AiModelConfig.ProtocolOpenAiCompatible,
                ApiKey = "openai-key",
                Model = "gpt-4o-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                Capabilities = new AiModelCapabilities { SupportsToolCall = true, SupportsJsonMode = true }
            });

        result.Content.Should().BeEmpty();
        result.ToolCalls.Should().ContainSingle();
        result.ToolCalls[0].Id.Should().Be("call_schema_1");
        result.ToolCalls[0].Name.Should().Be("get_operator_schema");
        result.ToolCalls[0].Arguments.GetProperty("operatorType").GetString()
            .Should().Be("ImageAcquisition");
        result.TokenUsage!.InputTokens.Should().Be(11);

        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("tools")[0].GetProperty("type").GetString().Should().Be("function");
        root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString()
            .Should().Be("get_operator_schema");
        root.GetProperty("tool_choice").GetString().Should().Be("auto");
    }

    [Fact(DisplayName = "AiApiClient OpenAI Responses native tool call should serialize tools and parse function_call output")]
    public async Task CompleteWithToolsAsync_OpenAiResponses_ShouldSerializeAndParseToolCalls()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "output": [
                    {
                      "type": "function_call",
                      "id": "fc_schema_1",
                      "call_id": "call_schema_1",
                      "name": "get_operator_schema",
                      "arguments": "{\"operatorType\":\"BlobAnalysis\"}"
                    }
                  ],
                  "usage": { "input_tokens": 13, "output_tokens": 5 }
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.CompleteWithToolsAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "inspect schema") },
            tools: [CreateToolDefinition("get_operator_schema")],
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                Protocol = AiModelConfig.ProtocolOpenAiCompatible,
                WireApi = AiModelConfig.WireApiResponses,
                ApiKey = "openai-key",
                Model = "gpt-5-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                Capabilities = new AiModelCapabilities { SupportsToolCall = true, SupportsJsonMode = false }
            });

        result.ToolCalls.Should().ContainSingle();
        result.ToolCalls[0].Id.Should().Be("call_schema_1");
        result.ToolCalls[0].ResponseItemId.Should().Be("fc_schema_1");
        result.ToolCalls[0].Arguments.GetProperty("operatorType").GetString()
            .Should().Be("BlobAnalysis");

        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("tools")[0].GetProperty("type").GetString().Should().Be("function");
        root.GetProperty("tools")[0].GetProperty("name").GetString()
            .Should().Be("get_operator_schema");
        root.GetProperty("tool_choice").GetString().Should().Be("auto");
    }

    [Fact(DisplayName = "AiApiClient DeepSeek request snapshot should disable thinking explicitly")]
    public async Task StreamCompleteAsync_DeepSeek_ShouldMatchRequestSnapshot()
    {
        string? capturedUrl = null;
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return CreateOpenAiSseResponse();
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.StreamCompleteAsync(
            systemPrompt: "system json",
            messages: new List<ChatMessage> { new("user", "return json") },
            onChunk: _ => { },
            options: new AiGenerationOptions
            {
                Provider = "DeepSeek",
                Protocol = AiModelConfig.ProtocolOpenAiCompatible,
                ApiKey = "deepseek-key",
                Model = "deepseek-v4-flash",
                BaseUrl = "https://api.deepseek.com",
                MaxTokens = 512,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.Off,
                Capabilities = new AiModelCapabilities { SupportsJsonMode = true, SupportsStreamOptions = true }
            });

        capturedUrl.Should().Be("https://api.deepseek.com/v1/chat/completions");
        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("model").GetString().Should().Be("deepseek-v4-flash");
        root.GetProperty("stream").GetBoolean().Should().BeTrue();
        root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().Should().BeTrue();
        root.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        root.GetProperty("thinking").GetProperty("type").GetString().Should().Be("disabled");
    }

    [Fact(DisplayName = "AiApiClient should omit JSON mode and stream options when capabilities disable them")]
    public async Task StreamCompleteAsync_OpenAiCompatible_ShouldRespectDisabledCapabilities()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return CreateOpenAiSseResponse();
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: _ => { },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "custom-chat",
                BaseUrl = "https://proxy.example.com/openai",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                Capabilities = new AiModelCapabilities
                {
                    SupportsJsonMode = false,
                    SupportsStreamOptions = false
                }
            });

        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.TryGetProperty("response_format", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("stream_options", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "AiApiClient should preserve query string on full OpenAI-compatible endpoint")]
    public async Task CompleteAsync_OpenAiCompatible_ShouldPreserveFullEndpointQuery()
    {
        string? capturedUrl = null;
        var handler = new CaptureHandler((request, _) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            return Task.FromResult(CreateOpenAiJsonResponse());
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system json",
            messages: new List<ChatMessage> { new("user", "return json") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "custom-chat",
                BaseUrl = "https://proxy.example.com/v1/chat/completions?tenant=cn",
                ExtraQuery = new Dictionary<string, string> { ["trace"] = "1" },
                MaxTokens = 256,
                TimeoutSeconds = 10
            });

        capturedUrl.Should().Be("https://proxy.example.com/v1/chat/completions?tenant=cn&trace=1");
    }

    [Fact(DisplayName = "AiApiClient Azure OpenAI request snapshot should use deployment URL and api-key")]
    public async Task CompleteAsync_AzureOpenAi_ShouldMatchRequestSnapshot()
    {
        string? capturedUrl = null;
        string? capturedApiKey = null;
        string? capturedAuthorization = null;
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedApiKey = request.Headers.TryGetValues("api-key", out var apiKeyValues)
                ? apiKeyValues.Single()
                : null;
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return CreateOpenAiJsonResponse();
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system json",
            messages: new List<ChatMessage> { new("user", "return json") },
            options: new AiGenerationOptions
            {
                Provider = "Azure OpenAI",
                Protocol = AiModelConfig.ProtocolAzureOpenAi,
                ApiKey = "azure-key",
                Model = "deployment-a",
                BaseUrl = "https://clearvision.openai.azure.com",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ExtraQuery = new Dictionary<string, string> { ["api-version"] = "2024-02-15-preview" },
                Capabilities = new AiModelCapabilities { SupportsJsonMode = true }
            });

        capturedUrl.Should().Be("https://clearvision.openai.azure.com/openai/deployments/deployment-a/chat/completions?api-version=2024-02-15-preview");
        capturedApiKey.Should().Be("azure-key");
        capturedAuthorization.Should().BeNull();
        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.TryGetProperty("model", out _).Should().BeFalse();
        document.RootElement.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
    }

    [Fact(DisplayName = "AiApiClient Anthropic request snapshot should use x-api-key and thinking budget")]
    public async Task CompleteAsync_Anthropic_ShouldMatchRequestSnapshot()
    {
        string? capturedUrl = null;
        string? capturedApiKey = null;
        string? capturedAnthropicVersion = null;
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedApiKey = request.Headers.TryGetValues("x-api-key", out var apiKeyValues)
                ? apiKeyValues.Single()
                : null;
            capturedAnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var versionValues)
                ? versionValues.Single()
                : null;
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return CreateAnthropicJsonResponse();
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system json",
            messages: new List<ChatMessage> { new("user", "return json") },
            options: new AiGenerationOptions
            {
                Provider = "Anthropic Claude",
                Protocol = AiModelConfig.ProtocolAnthropic,
                ApiKey = "anthropic-key",
                Model = "claude-sonnet-4-5",
                MaxTokens = 1024,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.Low
            });

        capturedUrl.Should().Be("https://api.anthropic.com/v1/messages");
        capturedApiKey.Should().Be("anthropic-key");
        capturedAnthropicVersion.Should().Be("2023-06-01");
        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("model").GetString().Should().Be("claude-sonnet-4-5");
        root.GetProperty("system").GetString().Should().Be("system json");
        root.GetProperty("thinking").GetProperty("type").GetString().Should().Be("enabled");
        root.GetProperty("thinking").GetProperty("budget_tokens").GetInt32().Should().Be(1024);
        root.TryGetProperty("response_format", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "AiApiClient Anthropic native tool call should serialize tools and parse tool_use")]
    public async Task CompleteWithToolsAsync_Anthropic_ShouldSerializeAndParseToolUse()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "content": [
                    {
                      "type": "tool_use",
                      "id": "toolu_schema_1",
                      "name": "get_operator_schema",
                      "input": { "operatorType": "Calibration" }
                    }
                  ],
                  "usage": { "input_tokens": 17, "output_tokens": 8 }
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.CompleteWithToolsAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "inspect schema") },
            tools: [CreateToolDefinition("get_operator_schema")],
            options: new AiGenerationOptions
            {
                Provider = "Anthropic Claude",
                Protocol = AiModelConfig.ProtocolAnthropic,
                ApiKey = "anthropic-key",
                Model = "claude-sonnet-4-5",
                MaxTokens = 512,
                TimeoutSeconds = 10,
                Capabilities = new AiModelCapabilities { SupportsToolCall = true }
            });

        result.ToolCalls.Should().ContainSingle();
        result.ToolCalls[0].Id.Should().Be("toolu_schema_1");
        result.ToolCalls[0].Arguments.GetProperty("operatorType").GetString()
            .Should().Be("Calibration");
        result.TokenUsage!.OutputTokens.Should().Be(8);

        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("tools")[0].GetProperty("name").GetString()
            .Should().Be("get_operator_schema");
        root.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString()
            .Should().Be("object");
    }

    [Fact(DisplayName = "AiApiClient Ollama native request snapshot should use api chat and format json")]
    public async Task CompleteAsync_OllamaNative_ShouldMatchRequestSnapshot()
    {
        string? capturedUrl = null;
        string? capturedAuthorization = null;
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "model": "llama3",
                  "message": { "role": "assistant", "content": "{\"ok\":true}" },
                  "done": true
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system json",
            messages: new List<ChatMessage> { new("user", "return json") },
            options: new AiGenerationOptions
            {
                Provider = "Ollama",
                Protocol = AiModelConfig.ProtocolOllamaNative,
                AuthMode = AiModelConfig.AuthModeNone,
                Model = "llama3",
                BaseUrl = "http://localhost:11434",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                Temperature = 0.1,
                Capabilities = new AiModelCapabilities { SupportsJsonMode = true }
            });

        capturedUrl.Should().Be("http://localhost:11434/api/chat");
        capturedAuthorization.Should().BeNull();
        using var document = JsonDocument.Parse(capturedRequestJson!);
        var root = document.RootElement;
        root.GetProperty("model").GetString().Should().Be("llama3");
        root.GetProperty("stream").GetBoolean().Should().BeFalse();
        root.GetProperty("format").GetString().Should().Be("json");
        root.GetProperty("options").GetProperty("temperature").GetDouble().Should().Be(0.1);
        root.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("system");
        root.GetProperty("messages")[1].GetProperty("content").GetString().Should().Be("return json");
    }

    [Fact(DisplayName = "AiApiClient stream should recover JSON from reasoning when content chunk is missing")]
    public async Task StreamCompleteAsync_OpenAi_ShouldRecoverJsonFromReasoning()
    {
        var ssePayload = string.Join("\n",
        [
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"分析完成，输出 JSON: {\\\"ok\\\":true}\"}}]}",
            "",
            "data: [DONE]",
            ""
        ]);

        var handler = new CaptureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
        }));

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);
        var chunks = new List<AiStreamChunk>();

        var result = await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: chunks.Add,
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10
            });

        result.Content.Should().Be("{\"ok\":true}");
        result.Reasoning.Should().Contain("输出 JSON");
        chunks.Count(c => c.ChunkType == AiStreamChunkType.Done).Should().Be(1);
    }

    [Fact(DisplayName = "AiApiClient stream should fallback to non-stream completion when stream has no content")]
    public async Task StreamCompleteAsync_OpenAi_ShouldFallbackToNonStreamCall()
    {
        var callCount = 0;
        var handler = new CaptureHandler((_, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                var emptySse = string.Join("\n",
                [
                    "data: {\"choices\":[{\"delta\":{}}]}",
                    "",
                    "data: [DONE]",
                    ""
                ]);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(emptySse, Encoding.UTF8, "text/event-stream")
                });
            }

            var responseJson = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"ok\":true}"
                  }
                }
              ]
            }
            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.StreamCompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            onChunk: _ => { },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10
            });

        callCount.Should().Be(2);
        result.Content.Should().Be("{\"ok\":true}");
    }

    [Fact(DisplayName = "AiApiClient should emit reasoning_effort for GPT-5 explicit reasoning settings")]
    public async Task CompleteAsync_OpenAi_ShouldSerializeReasoningEffortForGpt5()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
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

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "gpt-5.4",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.High
            });

        result.Content.Should().Be("{\"ok\":true}");
        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("high");
        document.RootElement.TryGetProperty("temperature", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "AiApiClient should serialize Anthropic thinking budget from explicit reasoning effort")]
    public async Task CompleteAsync_Anthropic_ShouldSerializeThinkingBudget()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
        {
            capturedRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "content": [
                    { "type": "thinking", "thinking": "analysis" },
                    { "type": "text", "text": "{\"ok\":true}" }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "Anthropic Claude",
                ApiKey = "test-key",
                Model = "claude-sonnet-4-5",
                MaxTokens = 1024,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.High
            });

        result.Content.Should().Be("{\"ok\":true}");
        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.GetProperty("thinking").GetProperty("type").GetString().Should().Be("enabled");
        document.RootElement.GetProperty("thinking").GetProperty("budget_tokens").GetInt32().Should().Be(3072);
        document.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(3584);
    }

    [Fact(DisplayName = "AiApiClient should serialize DeepSeek thinking flag when reasoning is enabled")]
    public async Task CompleteAsync_DeepSeek_ShouldSerializeThinkingFlag()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
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

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "deepseek-chat",
                BaseUrl = "https://api.deepseek.com/v1",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On
            });

        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.GetProperty("thinking").GetProperty("type").GetString().Should().Be("enabled");
    }

    [Fact(DisplayName = "AiApiClient should reject GPT-5 legacy none reasoning mode before sending request")]
    public async Task CompleteAsync_Gpt5Legacy_ShouldRejectReasoningOff()
    {
        using var httpClient = new HttpClient(new CaptureHandler((request, cancellationToken) =>
        {
            throw new Xunit.Sdk.XunitException("Request should not be sent for invalid GPT-5 legacy reasoning mode.");
        }));
        var apiClient = CreateApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "gpt-5.4",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.Off,
                ReasoningEffort = AiReasoningEfforts.Medium
            }));

        ex.Message.Should().Contain("不支持关闭 reasoning / thinking");
    }

    [Fact(DisplayName = "AiApiClient should serialize none reasoning effort for GPT-5.1 off mode")]
    public async Task CompleteAsync_Gpt51_ShouldSerializeNoneReasoningEffort()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
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

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "gpt-5.1-mini",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                Temperature = 0.3,
                ReasoningMode = AiReasoningModes.Off,
                ReasoningEffort = AiReasoningEfforts.Medium
            });

        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("none");
        document.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.3);
    }

    [Fact(DisplayName = "AiApiClient should serialize GLM disabled thinking when reasoning is off")]
    public async Task CompleteAsync_Glm_ShouldSerializeDisabledThinkingFlag()
    {
        string? capturedRequestJson = null;
        var handler = new CaptureHandler(async (request, cancellationToken) =>
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

        using var httpClient = new HttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "glm-4.5",
                BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.Off
            });

        using var document = JsonDocument.Parse(capturedRequestJson!);
        document.RootElement.GetProperty("thinking").GetProperty("type").GetString().Should().Be("disabled");
    }

    [Fact(DisplayName = "AiApiClient should reject GPT-5 Pro non-high effort before sending request")]
    public async Task CompleteAsync_Gpt5Pro_ShouldRejectNonHighEffort()
    {
        using var httpClient = new HttpClient(new CaptureHandler((request, cancellationToken) =>
        {
            throw new Xunit.Sdk.XunitException("Request should not be sent for invalid GPT-5 Pro reasoning effort.");
        }));
        var apiClient = CreateApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "gpt-5-pro",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.Medium
            }));

        ex.Message.Should().Contain("仅支持 High 思考强度");
    }

    [Fact(DisplayName = "AiApiClient should reject GPT-5.2 Pro low effort before sending request")]
    public async Task CompleteAsync_Gpt52Pro_ShouldRejectLowEffort()
    {
        using var httpClient = new HttpClient(new CaptureHandler((request, cancellationToken) =>
        {
            throw new Xunit.Sdk.XunitException("Request should not be sent for invalid GPT-5.2 Pro reasoning effort.");
        }));
        var apiClient = CreateApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => apiClient.CompleteAsync(
            systemPrompt: "system",
            messages: new List<ChatMessage> { new("user", "test") },
            options: new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                ApiKey = "test-key",
                Model = "gpt-5.2-pro",
                MaxTokens = 256,
                TimeoutSeconds = 10,
                ReasoningMode = AiReasoningModes.On,
                ReasoningEffort = AiReasoningEfforts.Low
            }));

        ex.Message.Should().Contain("仅支持 Medium / High 思考强度");
    }

    private AiApiClient CreateApiClient(HttpClient httpClient)
    {
        var configStore = CreateIsolatedConfigStore();
        return new AiApiClient(httpClient, configStore);
    }

    private AiConfigStore CreateIsolatedConfigStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cv-ai-mm-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempConfigDirs.Add(dir);

        return new AiConfigStore(
            Options.Create(new AiGenerationOptions()),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiConfigStore>>(),
            dir);
    }

    private static HttpResponseMessage CreateOpenAiJsonResponse()
    {
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
    }

    private static HttpResponseMessage CreateOpenAiSseResponse()
    {
        var ssePayload = string.Join("\n",
        [
            "data: {\"choices\":[{\"delta\":{\"content\":\"{\\\"ok\\\":true}\"}}]}",
            "",
            "data: [DONE]",
            ""
        ]);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
        };
    }

    private static HttpResponseMessage CreateAnthropicJsonResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "content": [
                { "type": "text", "text": "{\"ok\":true}" }
              ]
            }
            """, Encoding.UTF8, "application/json")
        };
    }

    private static AiNativeToolDefinition CreateToolDefinition(string name) => new()
    {
        Name = name,
        Description = "Test tool",
        ParametersSchema = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "operatorType": { "type": "string" }
          }
        }
        """)
    };

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string CreateTinyPngFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cv-ai-mm-{Guid.NewGuid():N}.png");
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aYQAAAAASUVORK5CYII=");
        File.WriteAllBytes(path, pngBytes);
        return path;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public CaptureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }
}
