using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Endpoints;

public static class AgentRunEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapAgentRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ai/agent-runs", HandleCreateRunAsync);
        app.MapGet("/api/ai/agent-runs/{runId}", HandleReplayRun);
        app.MapGet("/api/ai/agent-runs/{runId}/events", HandleRunEventsAsync);
        app.MapPost("/api/ai/agent-runs/{runId}/cancel", HandleCancelRun);
        return app;
    }

    private static Task<IResult> HandleCreateRunAsync(
        AgentRunCreateRequest request,
        IAgentRunEventStreamService streamService,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Task.FromResult<IResult>(Results.BadRequest(new
            {
                error = "Description is required."
            }));
        }

        var createResult = streamService.CreateRun(request.Description, new
        {
            mode = request.Mode ?? "auto",
            useVisionAgentGenerateFlow = request.UseVisionAgentGenerateFlow ?? true,
            agentGenerateFlowMode = request.AgentGenerateFlowMode ?? AiAgentGenerateFlowModes.Scripted,
            attachmentCount = request.AttachmentCount ?? request.Attachments?.Count ?? 0,
            metadataOnly = true
        });

        _ = Task.Run(async () =>
        {
            await RunGenerateFlowAsync(
                createResult.RunId,
                request,
                scopeFactory,
                loggerFactory.CreateLogger("AgentRunGenerateFlow"));
        });

        return Task.FromResult<IResult>(Results.Ok(new
        {
            runId = createResult.RunId,
            brief = createResult.Brief,
            events = createResult.Events
        }));
    }

    private static IResult HandleReplayRun(
        string runId,
        IAgentRunEventStreamService streamService)
    {
        var replay = streamService.Replay(runId);
        return replay == null
            ? Results.NotFound(new { error = "Agent run not found." })
            : Results.Ok(replay);
    }

    private static IResult HandleCancelRun(
        string runId,
        IAgentRunEventStreamService streamService)
    {
        var cancelled = streamService.Cancel(runId);
        var replay = streamService.Replay(runId);
        return replay == null && cancelled == null
            ? Results.NotFound(new { error = "Agent run not found." })
            : Results.Ok(new
            {
                runId,
                cancelled = cancelled != null,
                summary = replay?.Summary
            });
    }

    private static async Task HandleRunEventsAsync(
        string runId,
        HttpContext context,
        IAgentRunEventStreamService streamService,
        CancellationToken ct)
    {
        var afterSequence = ParseLastEventId(context.Request);
        using var subscription = streamService.Subscribe(runId, afterSequence);
        if (subscription == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Agent run not found." }, ct);
            return;
        }

        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        await context.Response.StartAsync(ct);

        foreach (var evt in subscription.ReplayEvents)
        {
            await WriteAgentRunSseAsync(context.Response, evt, ct);
        }

        try
        {
            await foreach (var evt in subscription.LiveEvents.ReadAllAsync(ct))
            {
                await WriteAgentRunSseAsync(context.Response, evt, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected.
        }
    }

    private static async Task RunGenerateFlowAsync(
        string runId,
        AgentRunCreateRequest request,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        using var scope = scopeFactory.CreateScope();
        var streamService = scope.ServiceProvider.GetRequiredService<IAgentRunEventStreamService>();
        var generationService = scope.ServiceProvider.GetRequiredService<IAiFlowGenerationService>();
        var cancellationToken = streamService.GetCancellationToken(runId);

        try
        {
            var result = await generationService.GenerateFlowAsync(
                request.ToGenerationRequest(runId),
                cancellationToken: cancellationToken);

            if (cancellationToken.IsCancellationRequested ||
                string.Equals(result.CompletionStatus, AiFlowGenerationResult.CompletionStatusCancelled, StringComparison.OrdinalIgnoreCase))
            {
                streamService.Cancel(runId);
                return;
            }

            if (result.Success)
            {
                streamService.Complete(runId, "Vision Agent completed the metadata-only workflow draft run.", new
                {
                    status = result.CompletionStatus,
                    generationMode = result.GenerationMode,
                    toolTraceCount = result.ToolTrace.Count,
                    pendingParameterCount = result.PendingParameters.Count,
                    missingResourceCount = result.MissingResources.Count,
                    reportId = $"agent-report-{runId}",
                    metadataOnly = true
                });
                return;
            }

            streamService.Fail(
                runId,
                result.ErrorMessage ?? result.FailureSummary?.Message ?? "Vision Agent run failed.",
                result.FailureSummary?.RepairTarget ??
                "Review the public diagnostics, fix missing metadata or blocked intent, and retry.",
                new
                {
                    status = result.CompletionStatus,
                    failureType = result.FailureType,
                    failureSummary = result.FailureSummary,
                    diagnostics = result.LastAttemptDiagnostics,
                    metadataOnly = true
                });
        }
        catch (OperationCanceledException)
        {
            streamService.Cancel(runId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AgentRun GenerateFlow background task failed. RunId={RunId}", runId);
            streamService.Fail(
                runId,
                "Vision Agent run failed before completion.",
                "Retry the request or inspect backend logs if the background task continues to fail.",
                new
                {
                    error = ex.Message,
                    metadataOnly = true
                });
        }
    }

    private static long ParseLastEventId(HttpRequest request)
    {
        return request.Headers.TryGetValue("Last-Event-ID", out var lastEventIdHeader) &&
               long.TryParse(lastEventIdHeader.FirstOrDefault(), out var parsedId)
            ? parsedId
            : 0;
    }

    private static async Task WriteAgentRunSseAsync(
        HttpResponse response,
        AgentRunEvent evt,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, SseJsonOptions);
        await response.WriteAsync($"id: {evt.Sequence}\n", ct);
        await response.WriteAsync($"event: {evt.EventType}\n", ct);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

public sealed record AgentRunCreateRequest
{
    public string Description { get; init; } = string.Empty;
    public string? AdditionalContext { get; init; }
    public string? SessionId { get; init; }
    public string? ExistingFlowJson { get; init; }
    public IReadOnlyList<string>? Attachments { get; init; }
    public int? AttachmentCount { get; init; }
    public string? Mode { get; init; }
    public bool DebugPrompt { get; init; }
    public string? RequirementMode { get; init; }
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public bool? UseVisionAgentGenerateFlow { get; init; }
    public string? AgentGenerateFlowMode { get; init; }
    public bool RuntimePreviewConsent { get; init; }

    public AiFlowGenerationRequest ToGenerationRequest(string runId)
    {
        return new AiFlowGenerationRequest(
            Description,
            AdditionalContext,
            SessionId,
            ExistingFlowJson,
            Array.Empty<string>(),
            GenerateFlowModeExtensions.ParseOrAuto(Mode),
            DebugPrompt,
            TemplateSelection)
        {
            RequirementMode = string.IsNullOrWhiteSpace(RequirementMode)
                ? AiRequirementModes.Strict
                : RequirementMode!,
            UseVisionAgentGenerateFlow = UseVisionAgentGenerateFlow ?? true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Normalize(AgentGenerateFlowMode),
            RuntimePreviewConsent = false,
            AgentRunId = runId
        };
    }
}
