using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Desktop.Tests;

public sealed class AgentRunEndpointsTests
{
    [Fact(DisplayName = "POST AgentRun creates run and returns started plus brief events")]
    public async Task CreateRun_ShouldReturnInitialEvents()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            description = "Detect scratches on a metal part",
            mode = "new",
            useVisionAgentGenerateFlow = true,
            agentGenerateFlowMode = "scripted"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = document.RootElement.GetProperty("runId").GetString();
        runId.Should().StartWith("ar_");
        document.RootElement.GetProperty("brief").GetString().Should().Contain("Detect scratches");
        document.RootElement.GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("eventType").GetString())
            .Should()
            .Equal(AgentRunEventTypes.RunStarted, AgentRunEventTypes.AssistantBrief);
    }

    [Fact(DisplayName = "POST AgentRun rejects empty description")]
    public async Task CreateRun_ShouldRejectEmptyDescription()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            description = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "AgentRun background GenerateFlow receives safe metadata-only request")]
    public async Task CreateRun_ShouldPassSafeGenerationRequest()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            description = "Modify current flow",
            additionalContext = "keep thresholds",
            sessionId = "session-1",
            existingFlowJson = "{\"operators\":[]}",
            attachments = new[] { @"C:\factory\image.png" },
            attachmentCount = 1,
            mode = "modify",
            runtimePreviewConsent = true,
            templateSelection = new
            {
                mode = "template_fill",
                templateId = "template-1",
                scenarioKey = "scratch"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.Generation.WaitForCallAsync();

        var request = host.Generation.LastRequest!;
        request.AgentRunId.Should().StartWith("ar_");
        request.Description.Should().Be("Modify current flow");
        request.AdditionalContext.Should().Be("keep thresholds");
        request.SessionId.Should().Be("session-1");
        request.ExistingFlowJson.Should().Be("{\"operators\":[]}");
        request.Attachments.Should().BeEmpty();
        request.Mode.Should().Be(GenerateFlowMode.Modify);
        request.RuntimePreviewConsent.Should().BeFalse();
        request.UseVisionAgentGenerateFlow.Should().BeTrue();
        request.TemplateSelection.Should().NotBeNull();
        request.TemplateSelection!.Mode.Should().Be("template_fill");
    }

    [Fact(DisplayName = "GET AgentRun replay returns final summary and events")]
    public async Task Replay_ShouldReturnSummaryAndEvents()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var runId = await host.CreateRunAsync("Generate wire sequence inspection");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("summary").GetProperty("status").GetString().Should().Be(AgentRunEventStatuses.Completed);
        document.RootElement.GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("eventType").GetString())
            .Should()
            .Contain(AgentRunEventTypes.RunCompleted);
    }

    [Fact(DisplayName = "GET AgentRun missing replay returns 404")]
    public async Task ReplayMissingRun_ShouldReturnNotFound()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.GetAsync("/api/ai/agent-runs/ar_missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "GET AgentRun SSE streams replay frames")]
    public async Task Events_ShouldStreamReplayFrames()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var runId = await host.CreateRunAsync("SSE replay");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}/events", HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("event: run.started");
        body.Should().Contain("event: assistant.brief");
        body.Should().Contain("event: run.completed");
        body.Should().Contain("data: ");
    }

    [Fact(DisplayName = "GET AgentRun SSE honors Last-Event-ID replay cursor")]
    public async Task Events_ShouldHonorLastEventId()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var runId = await host.CreateRunAsync("SSE cursor");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/agent-runs/{runId}/events");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "2");
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("event: run.started");
        body.Should().NotContain("event: assistant.brief");
        body.Should().Contain("event: run.completed");
    }

    [Fact(DisplayName = "POST AgentRun cancel emits run.cancelled and cancels background request")]
    public async Task Cancel_ShouldEmitCancelledEvent()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (request, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        });
        var runId = await host.CreateRunAsync("Cancel me");
        await host.Generation.WaitForCallAsync();

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);

        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Generation.LastCancellationToken.IsCancellationRequested.Should().BeTrue();
        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Last().EventType.Should().Be(AgentRunEventTypes.RunCancelled);
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Cancelled);
    }

    [Fact(DisplayName = "POST AgentRun cancel missing run returns 404")]
    public async Task CancelMissingRun_ShouldReturnNotFound()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync("/api/ai/agent-runs/ar_missing/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "AgentRun failed GenerateFlow emits first fix recommendation")]
    public async Task FailedGeneration_ShouldEmitFirstFixRecommendation()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync((_, _) => Task.FromResult(new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeManualRetryRequired,
            ErrorMessage = "Missing safe metadata.",
            FailureSummary = new AiFailureSummary
            {
                Message = "Missing safe metadata.",
                RepairTarget = "Provide the missing threshold metadata."
            }
        }));
        var runId = await host.CreateRunAsync("fail");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Failed);
        replay.Summary.FirstFixRecommendation.Should().Be("Provide the missing threshold metadata.");
        JsonSerializer.Serialize(replay.Events.Last(), AgentRunEventJson.Options)
            .Should()
            .Contain("firstFixRecommendation");
    }

    private sealed class AgentRunEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;

        private AgentRunEndpointTestHost(
            WebApplication app,
            string directory,
            FakeAiFlowGenerationService generation,
            IAgentRunEventStreamService streamService)
        {
            _app = app;
            _directory = directory;
            Generation = generation;
            StreamService = streamService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public FakeAiFlowGenerationService Generation { get; }

        public IAgentRunEventStreamService StreamService { get; }

        public static async Task<AgentRunEndpointTestHost> CreateAsync(
            Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>>? handler = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();

            var directory = Path.Combine(Path.GetTempPath(), $"cv-agent-run-endpoints-{Guid.NewGuid():N}");
            var redactor = new AgentRunEventRedactor();
            var store = new AgentRunEventStore(directory, redactor);
            var streamService = new AgentRunEventStreamService(store, redactor);
            var generation = new FakeAiFlowGenerationService(handler ?? ((_, _) => Task.FromResult(SuccessResult())));

            builder.Services.AddSingleton(redactor);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<IAgentRunEventStreamService>(streamService);
            builder.Services.AddSingleton<IAiFlowGenerationService>(generation);

            var app = builder.Build();
            app.MapAgentRunEndpoints();
            await app.StartAsync();

            return new AgentRunEndpointTestHost(app, directory, generation, streamService);
        }

        public static AiFlowGenerationResult SuccessResult()
        {
            return new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                GenerationMode = "agent_run_event_stream",
                AiExplanation = "Generated metadata-only draft.",
                Flow = new OperatorFlowDto(),
                ToolTrace = [new { toolName = "validate_flow", success = true }],
                PendingParameters = [],
                MissingResources = []
            };
        }

        public async Task<string> CreateRunAsync(string description)
        {
            using var response = await Client.PostAsJsonAsync("/api/ai/agent-runs", new
            {
                description,
                useVisionAgentGenerateFlow = true
            });
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("runId").GetString()!;
        }

        public async Task WaitForTerminalAsync(string runId)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cts.IsCancellationRequested)
            {
                var replay = StreamService.Replay(runId);
                if (replay?.Events.Any(evt =>
                        evt.EventType is AgentRunEventTypes.RunCompleted or AgentRunEventTypes.RunFailed or AgentRunEventTypes.RunCancelled) == true)
                {
                    return;
                }

                await Task.Delay(20, cts.Token);
            }

            throw new TimeoutException("AgentRun terminal event was not emitted.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class FakeAiFlowGenerationService : IAiFlowGenerationService
    {
        private readonly Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> _handler;
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAiFlowGenerationService(Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> handler)
        {
            _handler = handler;
        }

        public AiFlowGenerationRequest? LastRequest { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public async Task<AiFlowGenerationResult> GenerateFlowAsync(
            AiFlowGenerationRequest request,
            Action<string>? onProgress = null,
            Action<AiStreamChunk>? onStreamChunk = null,
            CancellationToken cancellationToken = default,
            Action<GenerateFlowAttachmentReport>? onAttachmentReport = null)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            _called.TrySetResult();
            return await _handler(request, cancellationToken);
        }

        public async Task WaitForCallAsync()
        {
            await _called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
