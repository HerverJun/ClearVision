using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
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
        app.MapPost("/api/ai/agent-plan", HandleCreatePlanAsync);
        app.MapPost("/api/ai/agent-runs", HandleCreateRunAsync);
        app.MapGet("/api/ai/agent-runs/{runId}", HandleReplayRun);
        app.MapGet("/api/ai/agent-runs/{runId}/events", HandleRunEventsAsync);
        app.MapPost("/api/ai/agent-runs/{runId}/stream-token", HandleCreateStreamToken);
        app.MapPost("/api/ai/agent-runs/{runId}/cancel", HandleCancelRun);
        return app;
    }

    private static async Task<IResult> HandleCreatePlanAsync(
        VisionAgentPlanModeRequest request,
        IVisionAgentOrchestrator orchestrator,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Results.BadRequest(new
            {
                error = "Description is required."
            });
        }

        var result = await orchestrator.CreatePlanAsync(request, ct);
        return Results.Ok(result);
    }

    private static Task<IResult> HandleCreateRunAsync(
        AgentRunCreateRequest request,
        HttpContext context,
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

        var ownerHash = ResolveCurrentOwnerHash(context);
        var createResult = streamService.CreateRun(
            request.Description,
            BuildCreatePayload(request),
            ownerHash);
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
        HttpContext context,
        IAgentRunEventStreamService streamService)
    {
        var replay = streamService.Replay(runId);
        if (replay == null)
        {
            return Results.NotFound(new { error = "Agent run not found." });
        }

        return streamService.IsRunOwner(runId, ResolveCurrentOwnerHash(context))
            ? Results.Ok(replay)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static IResult HandleCancelRun(
        string runId,
        HttpContext context,
        IAgentRunEventStreamService streamService)
    {
        var replayBeforeCancel = streamService.Replay(runId);
        if (replayBeforeCancel == null)
        {
            return Results.NotFound(new { error = "Agent run not found." });
        }

        if (!streamService.IsRunOwner(runId, ResolveCurrentOwnerHash(context)))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var cancelled = streamService.Cancel(runId);
        var replay = streamService.Replay(runId);
        return Results.Ok(new
        {
            runId,
            cancelled = cancelled != null,
            summary = replay?.Summary
        });
    }

    private static IResult HandleCreateStreamToken(
        string runId,
        HttpContext context,
        IAgentRunEventStreamService streamService)
    {
        var replay = streamService.Replay(runId);
        if (replay == null)
        {
            return Results.NotFound(new { error = "Agent run not found." });
        }

        var ownerHash = ResolveCurrentOwnerHash(context);
        var token = streamService.IssueStreamToken(runId, ownerHash);
        return string.IsNullOrWhiteSpace(token)
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : Results.Ok(new
            {
                runId,
                streamToken = token,
                streamTokenExpiresInSeconds = 45
            });
    }

    private static async Task HandleRunEventsAsync(
        string runId,
        HttpContext context,
        IAgentRunEventStreamService streamService,
        CancellationToken ct)
    {
        var token = context.Request.Query["streamToken"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token))
        {
            var validation = streamService.ValidateStreamToken(runId, token, consume: true);
            if (!validation.Authorized)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Agent run stream token rejected." }, ct);
                return;
            }
        }
        else if (!streamService.IsRunOwner(runId, ResolveCurrentOwnerHash(context)))
        {
            context.Response.StatusCode = streamService.Replay(runId) == null
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Agent run access denied." }, ct);
            return;
        }

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
        var orchestrator = scope.ServiceProvider.GetRequiredService<IVisionAgentOrchestrator>();
        var cancellationToken = streamService.GetCancellationToken(runId);

        try
        {
            var result = await orchestrator.BuildFromPlanAsync(
                request.ToGenerationRequest(runId),
                cancellationToken);

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
                        sessionId = result.SessionId,
                        generationMode = result.GenerationMode,
                        templateLockLevel = result.TemplateLockLevel,
                        recommendedTemplate = result.RecommendedTemplate,
                        flow = result.Flow,
                        aiExplanation = result.AiExplanation,
                        parametersNeedingReview = result.ParametersNeedingReview,
                        pendingParameters = result.PendingParameters,
                        missingResources = result.MissingResources,
                        pendingActions = result.PendingActions,
                        validationPreview = result.ValidationPreview,
                        dryRunResult = result.DryRunResult,
                        toolTrace = result.ToolTrace,
                        buildResult = result.BuildResult,
                        toolEvidenceTimeline = result.BuildResult?.ToolEvidenceTimeline,
                        workflowDiff = result.BuildResult?.WorkflowDiff,
                        applyGate = result.BuildResult?.ApplyGate,
                        readinessReport = result.BuildResult?.ReadinessReport,
                        stationCompatibilityReport = result.BuildResult?.StationCompatibilityReport,
                        operatorContractReport = result.BuildResult?.OperatorContractReport,
                        releaseReview = result.BuildResult?.ReleaseReview,
                        firstFixRecommendation = result.BuildResult?.FirstFixRecommendation,
                        stageTimeline = result.StageTimeline,
                        turnIntent = result.TurnIntent,
                        interactionState = result.InteractionState,
                        routerConfidence = result.RouterConfidence,
                        planSnapshot = request.BuildFromPlan?.PlanSnapshot,
                        buildFromPlan = BuildReplayPayload(request.BuildFromPlan),
                        buildInputSummary = BuildInputSummary(request),
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

    private static object BuildCreatePayload(AgentRunCreateRequest request)
    {
        return new
        {
            mode = request.Mode ?? request.BuildFromPlan?.BuildIntent ?? "auto",
            useVisionAgentGenerateFlow = request.UseVisionAgentGenerateFlow ?? true,
            agentGenerateFlowMode = request.AgentGenerateFlowMode ?? AiAgentGenerateFlowModes.Scripted,
            attachmentCount = request.BuildFromPlan?.AttachmentSummary.Count ?? request.AttachmentCount ?? request.Attachments?.Count ?? 0,
            planId = request.BuildFromPlan?.PlanId ?? string.Empty,
            planHash = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
            hasPlanSnapshot = request.BuildFromPlan?.PlanSnapshot != null,
            hasCurrentFlowSnapshot = !string.IsNullOrWhiteSpace(request.BuildFromPlan?.CurrentFlowSnapshot) ||
                                     !string.IsNullOrWhiteSpace(request.ExistingFlowJson),
            metadataOnly = true
        };
    }

    private static object? BuildReplayPayload(VisionAgentBuildFromPlanRequest? buildFromPlan)
    {
        if (buildFromPlan == null)
        {
            return null;
        }

        return new
        {
            planId = buildFromPlan.PlanId,
            planHash = buildFromPlan.PlanHash,
            planSnapshot = buildFromPlan.PlanSnapshot,
            userSelections = buildFromPlan.UserSelections,
            acceptedDefaults = buildFromPlan.AcceptedDefaults,
            currentFlowSnapshotIncluded = !string.IsNullOrWhiteSpace(buildFromPlan.CurrentFlowSnapshot),
            templateSelection = buildFromPlan.TemplateSelection,
            templateSelectionMode = buildFromPlan.TemplateSelection?.Mode ?? string.Empty,
            templateId = buildFromPlan.TemplateSelection?.TemplateId ?? string.Empty,
            attachmentSummary = buildFromPlan.AttachmentSummary,
            operatorCatalogVersion = buildFromPlan.OperatorCatalogVersion,
            stationBoundarySummary = buildFromPlan.StationBoundarySummary,
            plcOutputPolicy = buildFromPlan.PlcOutputPolicy,
            buildIntent = buildFromPlan.BuildIntent,
            originalUserPrompt = buildFromPlan.OriginalUserPrompt,
            acceptedRecommendedDefaults = buildFromPlan.AcceptedRecommendedDefaults,
            metadataOnly = true
        };
    }

    private static object BuildInputSummary(AgentRunCreateRequest request)
    {
        var build = request.BuildFromPlan;
        return new
        {
            planId = build?.PlanId ?? string.Empty,
            planHash = build?.PlanHash ?? build?.PlanSnapshot?.PlanHash ?? string.Empty,
            buildIntent = build?.BuildIntent ?? request.Mode ?? "auto",
            currentFlowSnapshotIncluded = !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot) ||
                                          !string.IsNullOrWhiteSpace(request.ExistingFlowJson),
            templateSelectionMode = build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode ?? string.Empty,
            templateId = build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId ?? string.Empty,
            attachmentCount = build?.AttachmentSummary.Count ?? request.AttachmentCount ?? request.Attachments?.Count ?? 0,
            operatorCatalogVersion = build?.OperatorCatalogVersion ?? string.Empty,
            stationBoundarySummary = build?.StationBoundarySummary ?? string.Empty,
            plcOutputPolicy = build?.PlcOutputPolicy ?? string.Empty,
            metadataOnly = true
        };
    }

    private static long ParseLastEventId(HttpRequest request)
    {
        return request.Headers.TryGetValue("Last-Event-ID", out var lastEventIdHeader) &&
               long.TryParse(lastEventIdHeader.FirstOrDefault(), out var parsedId)
            ? parsedId
            : TryParseQuerySequence(request);
    }

    private static long TryParseQuerySequence(HttpRequest request)
    {
        if (long.TryParse(request.Query["lastEventId"].FirstOrDefault(), out var lastEventId))
        {
            return lastEventId;
        }

        return long.TryParse(request.Query["afterSequence"].FirstOrDefault(), out var afterSequence)
            ? afterSequence
            : 0;
    }

    private static string ResolveCurrentOwnerHash(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"agent-run-owner:{userId.Trim()}"));
        return "usr_" + Convert.ToHexString(bytes).ToLowerInvariant();
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
    public VisionAgentBuildFromPlanRequest? BuildFromPlan { get; init; }
    public bool? UseVisionAgentGenerateFlow { get; init; }
    public string? AgentGenerateFlowMode { get; init; }
    public bool RuntimePreviewConsent { get; init; }

    public AiFlowGenerationRequest ToGenerationRequest(string runId)
    {
        var buildIntent = string.IsNullOrWhiteSpace(BuildFromPlan?.BuildIntent)
            ? Mode
            : BuildFromPlan!.BuildIntent;
        var existingFlowJson = !string.IsNullOrWhiteSpace(ExistingFlowJson)
            ? ExistingFlowJson
            : BuildFromPlan?.CurrentFlowSnapshot;
        var templateSelection = BuildFromPlan?.TemplateSelection ?? TemplateSelection;

        return new AiFlowGenerationRequest(
            Description,
            AdditionalContext,
            SessionId,
            existingFlowJson,
            Array.Empty<string>(),
            GenerateFlowModeExtensions.ParseOrAuto(buildIntent),
            DebugPrompt,
            templateSelection)
        {
            RequirementMode = string.IsNullOrWhiteSpace(RequirementMode)
                ? AiRequirementModes.Strict
                : RequirementMode!,
            UseVisionAgentGenerateFlow = UseVisionAgentGenerateFlow ?? true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Normalize(AgentGenerateFlowMode),
            RuntimePreviewConsent = false,
            AgentRunId = runId,
            BuildFromPlan = BuildFromPlan
        };
    }
}
