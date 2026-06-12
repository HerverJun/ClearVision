using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly Regex PlanPrivateMarkerRegex = new(
        @"(?i)\b(rawPrompt|systemPrompt|chainOfThought|chain_of_thought|reasoningContent|reasoning_content|hiddenReasoning)\b\s*[:=]\s*[^,;}\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanSecretMarkerRegex = new(
        @"(?i)\b(authorization|x-api-key|api[-_ ]?key|token|secret|bearer)\b\s*[:=]\s*[""']?[^""'\s,;}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanWindowsPathRegex = new(
        @"(?i)(?:[a-z]:\\|\\\\)[^\s""'<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanUnixPathRegex = new(
        @"(?i)(?:/users/|/home/|/var/|/tmp/|/mnt/|/data/|/models/|/artifacts/)[^\s""'<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanArtifactPathRegex = new(
        @"(?i)[a-z0-9_\-./\\:]+?\.(?:cvpkg|onnx|pt|pth|engine|weights|blob|zip|7z|tar|gz|png|jpg|jpeg|bmp|tif|tiff)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanDataImageRegex = new(
        @"(?i)data:image/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanLongBase64Regex = new(
        @"(?<![a-z0-9+/=])(?:[a-z0-9+/]{96,}={0,2})(?![a-z0-9+/=])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PlanIPv4Regex = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlanPlcAddressRegex = new(
        @"(?i)\b(DB\d+\.DB[XBWD]\d+(?:\.\d+)?|M\d+(?:\.\d+)?|D\d+|plc://[^\s,;""'}]+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapAgentRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ai/agent-plan", HandleCreatePlanAsync);
        app.MapPost("/api/ai/agent-intent-router-runs", HandleCreateIntentRouterRunAsync);
        app.MapPost("/api/ai/agent-plan-runs", HandleCreatePlanRunAsync);
        app.MapPost("/api/ai/agent-runs", HandleCreateRunAsync);
        app.MapGet("/api/ai/agent-runs/latest", HandleReplayLatestRun);
        app.MapGet("/api/ai/agent-runs/{runId}", HandleReplayRun);
        app.MapGet("/api/ai/agent-runs/{runId}/events", HandleRunEventsAsync);
        app.MapPost("/api/ai/agent-runs/{runId}/stream-token", HandleCreateStreamToken);
        app.MapPost("/api/ai/agent-runs/{runId}/cancel", HandleCancelRun);
        return app;
    }

    private static async Task<IResult> HandleCreateIntentRouterRunAsync(
        VisionAgentIntentRouterRequest request,
        IVisionAgentIntentRouterService router,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Results.BadRequest(new
            {
                error = "Description is required."
            });
        }

        var result = await router.RouteAsync(request, ct);
        return Results.Ok(result);
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

    private static Task<IResult> HandleCreatePlanRunAsync(
        VisionAgentPlanModeRequest request,
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
            BuildPlanCreatePayload(request),
            ownerHash);

        AppendPlanEvent(streamService, createResult.RunId, AgentRunEventTypes.PlanCreated, "plan", "规划已创建",
            "已创建 Plan Run，公开进度将通过事件流更新。", AgentRunEventStatuses.Completed, new
            {
                mode = "plan",
                metadataOnly = true
            });
        AppendPlanEvent(streamService, createResult.RunId, AgentRunEventTypes.PlanStarted, "plan", "规划已启动",
            "正在进入规划阶段。", AgentRunEventStatuses.Running, new
            {
                mode = "plan",
                metadataOnly = true
            });

        _ = Task.Run(async () =>
        {
            await RunCreatePlanAsync(
                createResult.RunId,
                request,
                scopeFactory,
                loggerFactory.CreateLogger("AgentRunPlanMode"));
        });

        var replay = streamService.Replay(createResult.RunId);
        return Task.FromResult<IResult>(Results.Ok(new
        {
            runId = createResult.RunId,
            brief = createResult.Brief,
            events = replay?.Events ?? createResult.Events
        }));
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

    private static IResult HandleReplayLatestRun(
        HttpContext context,
        IAgentRunEventStreamService streamService)
    {
        var replay = streamService.ReplayLatest(ResolveCurrentOwnerHash(context));
        return replay == null
            ? Results.NotFound(new { error = "No Agent run replay is available." })
            : Results.Ok(replay);
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

        if (ReplayHasMode(replayBeforeCancel, "plan"))
        {
            AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanCancelled, "plan", "规划已取消",
                "用户已取消本次规划，事件流即将关闭。", AgentRunEventStatuses.Cancelled, new
                {
                    metadataOnly = true
                });
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

    private static async Task RunCreatePlanAsync(
        string runId,
        VisionAgentPlanModeRequest request,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        using var scope = scopeFactory.CreateScope();
        var streamService = scope.ServiceProvider.GetRequiredService<IAgentRunEventStreamService>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IVisionAgentOrchestrator>();
        var cancellationToken = streamService.GetCancellationToken(runId);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanContextStarted, "collecting_context",
                "收集上下文", "正在收集公开需求、流程、模板、附件、算子和工站边界。", AgentRunEventStatuses.Running,
                BuildPlanContextPayload(request));
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.SemanticStarted, "semantic_extraction",
                "语义抽取中", "正在抽取视觉需求语义槽位。", AgentRunEventStatuses.Running, new
                {
                    metadataOnly = true
                });
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanContextCompleted, "collecting_context",
                "上下文已收集", "已收集公开需求、流程、模板、附件、算子和工站边界。", AgentRunEventStatuses.Completed,
                BuildPlanContextPayload(request));
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanModelStarted, "planning_with_model",
                "模型规划中", "模型正在生成公开结构化规划候选。", AgentRunEventStatuses.Running, new
                {
                    metadataOnly = true
                });

            var result = await orchestrator.CreatePlanAsync(request, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanCancelled, "plan",
                    "规划已取消", "规划已取消，未发布完成结果。", AgentRunEventStatuses.Cancelled, new
                    {
                        metadataOnly = true
                    });
                streamService.Cancel(runId, "规划已取消。");
                return;
            }

            EmitPlanResultEvents(streamService, runId, result, emitted);

            var completedPayload = BuildPlanCompletedPayload(result);
            AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanCompleted, "plan_ready",
                "规划已就绪", BuildPlanCompletionSummary(result), AgentRunEventStatuses.Completed, completedPayload);

            streamService.Complete(runId, BuildPlanCompletionSummary(result), completedPayload);
        }
        catch (OperationCanceledException)
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanCancelled, "plan",
                "规划已取消", "规划已取消，未发布完成结果。", AgentRunEventStatuses.Cancelled, new
                {
                    metadataOnly = true
                });
            streamService.Cancel(runId, "规划已取消。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AgentRun PlanMode background task failed. RunId={RunId}", runId);
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanFailed, "plan",
                "规划失败", "规划在完成前失败，请检查公开诊断后重试。", AgentRunEventStatuses.Failed, new
                {
                    error = ex.Message,
                    metadataOnly = true
                });
            streamService.Fail(
                runId,
                "规划在完成前失败。",
                "请检查公开诊断并重试规划。",
                new
                {
                    mode = "plan",
                    error = ex.Message,
                    metadataOnly = true
                });
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
                streamService.Complete(runId, "视觉智能体已完成仅元数据流程草稿构建。", new
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
                        requirementMaturity = result.RequirementMaturity,
                        decisionTrace = result.DecisionTrace,
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
                result.ErrorMessage ?? result.FailureSummary?.Message ?? "视觉智能体运行失败。",
                result.FailureSummary?.RepairTarget ??
                "请查看公开诊断，补齐缺失元数据或处理阻断意图后重试。",
                new
                {
                    status = result.CompletionStatus,
                    failureType = result.FailureType,
                    failureSummary = result.FailureSummary,
                    diagnostics = result.LastAttemptDiagnostics,
                    requirementMaturity = result.RequirementMaturity,
                    decisionTrace = result.DecisionTrace,
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
                "视觉智能体在完成前失败。",
                "请重试本轮请求；如果后台任务持续失败，再检查后端日志。",
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

    private static object BuildPlanCreatePayload(VisionAgentPlanModeRequest request)
    {
        return new
        {
            mode = "plan",
            hasCurrentFlowSnapshot = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            hasCurrentResultSnapshot = !string.IsNullOrWhiteSpace(request.CurrentResultSnapshot),
            attachmentCount = request.AttachmentSummary.Count,
            templateSelectionMode = request.TemplateSelection?.Mode ?? string.Empty,
            templateId = request.TemplateSelection?.TemplateId ?? string.Empty,
            historySummaryIncluded = !string.IsNullOrWhiteSpace(request.HistorySummary),
            metadataOnly = true
        };
    }

    private static object BuildPlanContextPayload(VisionAgentPlanModeRequest request)
    {
        return new
        {
            hasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            hasCurrentResult = !string.IsNullOrWhiteSpace(request.CurrentResultSnapshot),
            attachmentCount = request.AttachmentSummary.Count,
            templateSelectionMode = request.TemplateSelection?.Mode ?? string.Empty,
            templateId = request.TemplateSelection?.TemplateId ?? string.Empty,
            contextKinds = new[]
            {
                "user_requirement",
                string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot) ? "new_flow" : "current_flow",
                string.IsNullOrWhiteSpace(request.CurrentResultSnapshot) ? "no_current_result" : "current_result",
                request.TemplateSelection == null ? "template_catalog" : "template_selection",
                "operator_catalog",
                "station_boundary"
            },
            metadataOnly = true
        };
    }

    private static object BuildPlanCompletedPayload(VisionAgentPlanModeResult result)
    {
        var replaySafePlan = BuildReplaySafePlanResult(result);
        return new
        {
            status = "plan_completed",
            generationMode = "plan",
            planSource = replaySafePlan.PlanSource,
            fallbackReason = replaySafePlan.FallbackReason,
            plannerFailureStage = replaySafePlan.PlannerFailureStage,
            plannerFailureCode = replaySafePlan.PlannerFailureCode,
            sanitizedErrorKind = replaySafePlan.SanitizedErrorKind,
            sanitizedErrorMessage = replaySafePlan.SanitizedErrorMessage,
            planResult = replaySafePlan,
            planModeResult = replaySafePlan,
            planId = replaySafePlan.PlanId,
            planHash = replaySafePlan.PlanHash,
            canBuild = replaySafePlan.CanBuild,
            questionCount = replaySafePlan.ClarificationQuestions.Count,
            publicEventCount = replaySafePlan.PublicEvents.Count,
            metadataOnly = true
        };
    }

    private static VisionAgentPlanModeResult BuildReplaySafePlanResult(VisionAgentPlanModeResult result)
    {
        return result with
        {
            PlanId = SanitizePlanToken(result.PlanId),
            PlanHash = SanitizePlanToken(result.PlanHash),
            PlanSource = SanitizePlanToken(result.PlanSource),
            FallbackReason = SanitizePlanToken(result.FallbackReason),
            PlannerFailureStage = SanitizePlanToken(result.PlannerFailureStage),
            PlannerFailureCode = SanitizePlanToken(result.PlannerFailureCode),
            SanitizedErrorKind = SanitizePlanToken(result.SanitizedErrorKind),
            SanitizedErrorMessage = SanitizePlanText(result.SanitizedErrorMessage),
            OriginalUserPrompt = SanitizePlanText(result.OriginalUserPrompt),
            Goal = SanitizePlanText(result.Goal),
            Intent = SanitizePlanToken(result.Intent),
            Confidence = SanitizePlanToken(result.Confidence),
            RequirementUnderstanding = SanitizePlanList(result.RequirementUnderstanding),
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = SanitizePlanToken(result.RecommendedRoute.RouteId),
                Title = SanitizePlanText(result.RecommendedRoute.Title),
                Summary = SanitizePlanText(result.RecommendedRoute.Summary),
                Operators = SanitizePlanList(result.RecommendedRoute.Operators).Select(SanitizePlanToken).ToList(),
                TemplateDecision = SanitizePlanText(result.RecommendedRoute.TemplateDecision)
            },
            ClarificationQuestions = result.ClarificationQuestions.Select(question => question with
            {
                Id = SanitizePlanToken(question.Id),
                Title = SanitizePlanText(question.Title),
                Why = SanitizePlanText(question.Why),
                DefaultValue = SanitizePlanToken(question.DefaultValue),
                DefaultAssumption = SanitizePlanText(question.DefaultAssumption),
                Impact = SanitizePlanText(question.Impact),
                Options = question.Options.Select(option => option with
                {
                    Value = SanitizePlanToken(option.Value),
                    Label = SanitizePlanText(option.Label),
                    Description = SanitizePlanText(option.Description),
                    Impact = SanitizePlanText(option.Impact)
                }).ToList()
            }).ToList(),
            RecommendedDefaults = result.RecommendedDefaults.Select(item => item with
            {
                Id = SanitizePlanToken(item.Id),
                Label = SanitizePlanText(item.Label),
                Value = SanitizePlanText(item.Value),
                Impact = SanitizePlanText(item.Impact)
            }).ToList(),
            Risks = SanitizePlanList(result.Risks),
            AcceptanceCriteria = SanitizePlanList(result.AcceptanceCriteria),
            ExecutablePlan = SanitizePlanList(result.ExecutablePlan),
            BlockingReasons = SanitizePlanList(result.BlockingReasons).Select(SanitizePlanToken).ToList(),
            RequirementMaturity = result.RequirementMaturity == null
                ? null
                : result.RequirementMaturity with
                {
                    Maturity = SanitizePlanToken(result.RequirementMaturity.Maturity),
                    TaskType = SanitizePlanToken(result.RequirementMaturity.TaskType),
                    ObjectSignals = SanitizePlanList(result.RequirementMaturity.ObjectSignals),
                    TaskSignals = SanitizePlanList(result.RequirementMaturity.TaskSignals),
                    MissingFields = SanitizePlanList(result.RequirementMaturity.MissingFields).Select(SanitizePlanToken).ToList(),
                    BlockingReasons = SanitizePlanList(result.RequirementMaturity.BlockingReasons).Select(SanitizePlanToken).ToList(),
                    PublicReason = SanitizePlanText(result.RequirementMaturity.PublicReason)
                },
            DecisionTrace = result.DecisionTrace == null
                ? null
                : result.DecisionTrace with
                {
                    RawUserText = SanitizePlanText(result.DecisionTrace.RawUserText),
                    TurnIntent = SanitizePlanToken(result.DecisionTrace.TurnIntent),
                    InteractionState = SanitizePlanToken(result.DecisionTrace.InteractionState),
                    BusinessSignalsHit = SanitizePlanList(result.DecisionTrace.BusinessSignalsHit),
                    NewFlowSignalsHit = SanitizePlanList(result.DecisionTrace.NewFlowSignalsHit),
                    TaskTypeSignalsHit = SanitizePlanList(result.DecisionTrace.TaskTypeSignalsHit),
                    ObjectSignalsHit = SanitizePlanList(result.DecisionTrace.ObjectSignalsHit),
                    MaturityLevel = SanitizePlanToken(result.DecisionTrace.MaturityLevel),
                    TaskType = SanitizePlanToken(result.DecisionTrace.TaskType),
                    FallbackReason = SanitizePlanToken(result.DecisionTrace.FallbackReason),
                    BlockingReasons = SanitizePlanList(result.DecisionTrace.BlockingReasons).Select(SanitizePlanToken).ToList()
                },
            NextAction = SanitizePlanText(result.NextAction),
            ContextSummary = result.ContextSummary with
            {
                TemplateSelectionMode = SanitizePlanToken(result.ContextSummary.TemplateSelectionMode),
                TemplateId = SanitizePlanToken(result.ContextSummary.TemplateId),
                ContextKinds = SanitizePlanList(result.ContextSummary.ContextKinds).Select(SanitizePlanToken).ToList(),
                OperatorCatalogTools = SanitizePlanList(result.ContextSummary.OperatorCatalogTools).Select(SanitizePlanToken).ToList()
            },
            OperatorCatalogVersion = SanitizePlanToken(result.OperatorCatalogVersion),
            TemplateCatalogVersion = SanitizePlanToken(result.TemplateCatalogVersion),
            TemplateSelection = result.TemplateSelection == null
                ? null
                : new AiTemplateSelectionInfo
                {
                    Mode = SanitizePlanToken(result.TemplateSelection.Mode),
                    TemplateId = SanitizePlanToken(result.TemplateSelection.TemplateId),
                    ScenarioKey = SanitizePlanToken(result.TemplateSelection.ScenarioKey)
                },
            StationBoundarySummary = SanitizePlanText(result.StationBoundarySummary),
            PlcOutputPolicy = SanitizePlanText(result.PlcOutputPolicy),
            PlanWarnings = SanitizePlanList(result.PlanWarnings),
            ContractRepairNotes = SanitizePlanList(result.ContractRepairNotes).Select(SanitizePlanToken).ToList(),
            PublicEvents = result.PublicEvents.Select(evt => evt with
            {
                Stage = SanitizePlanToken(evt.Stage),
                Status = SanitizePlanToken(evt.Status),
                Title = SanitizePlanText(evt.Title),
                Summary = SanitizePlanText(evt.Summary),
                Metadata = evt.Metadata.ToDictionary(
                    pair => SanitizePlanToken(pair.Key),
                    pair => SanitizePlanText(pair.Value),
                    StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            MetadataOnly = true
        };
    }

    private static List<string> SanitizePlanList(IEnumerable<string>? values)
    {
        return values?.Select(SanitizePlanText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
    }

    private static string SanitizePlanToken(string? value)
    {
        var text = SanitizePlanText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return new string(text
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':')
            .ToArray());
    }

    private static string SanitizePlanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        text = PlanPrivateMarkerRegex.Replace(text, "[redacted:private-planning]");
        text = PlanSecretMarkerRegex.Replace(text, "[redacted:secret]");
        text = PlanDataImageRegex.Replace(text, "[redacted:image-bytes]");
        text = PlanLongBase64Regex.Replace(text, "[redacted:base64]");
        text = PlanWindowsPathRegex.Replace(text, "[redacted:path]");
        text = PlanUnixPathRegex.Replace(text, "[redacted:path]");
        text = PlanArtifactPathRegex.Replace(text, "[redacted:artifact-path]");
        text = PlanIPv4Regex.Replace(text, "[redacted:ip]");
        text = PlanPlcAddressRegex.Replace(text, "[redacted:plc-address]");
        return text;
    }

    private static void EmitPlanResultEvents(
        IAgentRunEventStreamService streamService,
        string runId,
        VisionAgentPlanModeResult result,
        HashSet<string> emitted)
    {
        foreach (var publicEvent in result.PublicEvents)
        {
            EmitPublicPlanEvent(streamService, runId, emitted, publicEvent);
        }

        var isFallback = string.Equals(result.PlanSource, "rule_fallback", StringComparison.OrdinalIgnoreCase);
        var fallbackReason = result.FallbackReason ?? string.Empty;
        var plannerFailureMetadata = BuildPlannerFailureMetadata(result);
        if (string.Equals(fallbackReason, "planner_timeout", StringComparison.OrdinalIgnoreCase))
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanModelTimeout, "planning_with_model",
                "模型规划超时", "模型规划超时，已使用规则兜底方案。", AgentRunEventStatuses.Failed, new
                {
                    fallbackReason,
                    plannerFailureMetadata.plannerFailureStage,
                    plannerFailureMetadata.plannerFailureCode,
                    plannerFailureMetadata.sanitizedErrorKind,
                    plannerFailureMetadata.sanitizedErrorMessage,
                    metadataOnly = true
                });
        }
        else if (isFallback && !string.IsNullOrWhiteSpace(fallbackReason))
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanModelFailed, "planning_with_model",
                "模型规划失败", "模型规划未能产出可用规划，已使用规则兜底方案。", AgentRunEventStatuses.Failed, new
                {
                    fallbackReason,
                    plannerFailureMetadata.plannerFailureStage,
                    plannerFailureMetadata.plannerFailureCode,
                    plannerFailureMetadata.sanitizedErrorKind,
                    plannerFailureMetadata.sanitizedErrorMessage,
                    metadataOnly = true
                });
        }
        else
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanModelCompleted, "planning_with_model",
                "模型规划完成", "模型已返回公开结构化规划候选。", AgentRunEventStatuses.Completed, new
                {
                    metadataOnly = true
                });
        }

        EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanContractStarted, "validating_plan_contract",
            "校验规划契约", "正在校验规划结构、问题质量、算子目录和模板约束。", AgentRunEventStatuses.Running, new
            {
                metadataOnly = true
            });
        EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanContractCompleted, "validating_plan_contract",
            "规划契约已校验", "规划已归一到公开 PlanModeResult 契约。", AgentRunEventStatuses.Completed, new
            {
                repairCount = result.ContractRepairNotes.Count,
                warningCount = result.PlanWarnings.Count,
                metadataOnly = true
            });
        EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanSafetyCompleted, "applying_safety_constraints",
            "安全约束已应用", "已应用脱敏、元数据边界、资源占位和 PLC 安全策略。", AgentRunEventStatuses.Completed, new
            {
                metadataOnly = true
            });

        if (isFallback)
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanFallbackUsed, "rule_fallback_used",
                "已使用规则兜底方案", BuildFallbackSummary(fallbackReason), AgentRunEventStatuses.Completed, new
                {
                    fallbackReason,
                    plannerFailureMetadata.plannerFailureStage,
                    plannerFailureMetadata.plannerFailureCode,
                    plannerFailureMetadata.sanitizedErrorKind,
                    plannerFailureMetadata.sanitizedErrorMessage,
                    metadataOnly = true
                });
        }
    }

    private sealed record PlannerFailureMetadata(
        string plannerFailureStage,
        string plannerFailureCode,
        string sanitizedErrorKind,
        string sanitizedErrorMessage);

    private static PlannerFailureMetadata BuildPlannerFailureMetadata(VisionAgentPlanModeResult result)
    {
        return new PlannerFailureMetadata(
            SanitizePlanToken(result.PlannerFailureStage),
            SanitizePlanToken(result.PlannerFailureCode),
            SanitizePlanToken(result.SanitizedErrorKind),
            SanitizePlanText(result.SanitizedErrorMessage));
    }

    private static void EmitPublicPlanEvent(
        IAgentRunEventStreamService streamService,
        string runId,
        HashSet<string> emitted,
        VisionAgentPlanPublicEvent publicEvent)
    {
        var stage = publicEvent.Stage ?? string.Empty;
        var status = publicEvent.Status ?? string.Empty;
        var eventType = MapPlanPublicEventType(publicEvent);
        if (string.IsNullOrWhiteSpace(eventType) || string.Equals(eventType, AgentRunEventTypes.PlanCompleted, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EmitPlanStage(
            streamService,
            runId,
            emitted,
            eventType,
            stage,
            publicEvent.Title,
            publicEvent.Summary,
            NormalizePlanStatus(status),
            new
            {
                publicEvent.Metadata,
                metadataOnly = true
            });
    }

    private static string? MapPlanPublicEventType(VisionAgentPlanPublicEvent publicEvent)
    {
        var stage = publicEvent.Stage ?? string.Empty;
        var status = publicEvent.Status ?? string.Empty;
        return stage switch
        {
            "semantic_extraction" when IsStatus(status, "started") || IsStatus(status, AgentRunEventStatuses.Running) => AgentRunEventTypes.SemanticStarted,
            "semantic_extraction" when IsStatus(status, "failed") => AgentRunEventTypes.SemanticFailed,
            "semantic_extraction" => AgentRunEventTypes.SemanticCompleted,
            "semantic_fallback_used" => AgentRunEventTypes.SemanticFallbackUsed,
            "collecting_context" when IsStatus(status, "started") => AgentRunEventTypes.PlanContextStarted,
            "collecting_context" => AgentRunEventTypes.PlanContextCompleted,
            "planning_with_model" when IsStatus(status, "started") => AgentRunEventTypes.PlanModelStarted,
            "planning_with_model" when IsStatus(status, "completed") => AgentRunEventTypes.PlanModelCompleted,
            "planning_with_model" when IsPlannerTimeout(publicEvent) => AgentRunEventTypes.PlanModelTimeout,
            "planning_with_model" => AgentRunEventTypes.PlanModelFailed,
            "validating_plan_contract" when IsStatus(status, "started") => AgentRunEventTypes.PlanContractStarted,
            "validating_plan_contract" => AgentRunEventTypes.PlanContractCompleted,
            "applying_safety_constraints" => AgentRunEventTypes.PlanSafetyCompleted,
            "rule_fallback_used" => AgentRunEventTypes.PlanFallbackUsed,
            "plan_ready" => AgentRunEventTypes.PlanCompleted,
            _ => null
        };
    }

    private static bool IsPlannerTimeout(VisionAgentPlanPublicEvent publicEvent)
    {
        return publicEvent.Metadata.TryGetValue("fallbackReason", out var reason) &&
               string.Equals(reason, "planner_timeout", StringComparison.OrdinalIgnoreCase) ||
               publicEvent.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               publicEvent.Summary.Contains("超时", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatus(string status, string expected)
    {
        return string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePlanStatus(string status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? AgentRunEventStatuses.Completed
            : status.Trim().ToLowerInvariant();
    }

    private static void EmitPlanStage(
        IAgentRunEventStreamService streamService,
        string runId,
        HashSet<string> emitted,
        string eventType,
        string stage,
        string title,
        string summary,
        string status,
        object? payload)
    {
        var key = $"{eventType}:{stage}:{status}";
        if (!emitted.Add(key))
        {
            return;
        }

        AppendPlanEvent(streamService, runId, eventType, stage, title, summary, status, payload);
    }

    private static AgentRunEvent? AppendPlanEvent(
        IAgentRunEventStreamService streamService,
        string runId,
        string eventType,
        string stage,
        string title,
        string summary,
        string status,
        object? payload)
    {
        return streamService.Append(runId, new AgentRunEventDraft
        {
            EventType = eventType,
            Stage = stage,
            Title = title,
            Summary = summary,
            Status = status,
            Payload = payload
        });
    }

    private static string BuildPlanCompletionSummary(VisionAgentPlanModeResult result)
    {
        if (string.Equals(result.FallbackReason, "planner_timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "模型规划超时，已使用规则兜底方案。";
        }

        return string.Equals(result.PlanSource, "rule_fallback", StringComparison.OrdinalIgnoreCase)
            ? "规划已完成，已使用规则兜底方案。"
            : "规划已完成，可以开始构建。";
    }

    private static string BuildFallbackSummary(string fallbackReason)
    {
        return string.Equals(fallbackReason, "planner_timeout", StringComparison.OrdinalIgnoreCase)
            ? "模型规划超时，已使用规则兜底方案。"
            : "已使用规则兜底方案。";
    }

    private static bool ReplayHasMode(AgentRunReplayResult? replay, string mode)
    {
        if (replay == null || string.IsNullOrWhiteSpace(mode))
        {
            return false;
        }

        foreach (var evt in replay.Events)
        {
            if (!string.Equals(evt.EventType, AgentRunEventTypes.RunStarted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(evt.Payload, SseJsonOptions));
                if (document.RootElement.TryGetProperty("mode", out var modeElement) &&
                    string.Equals(modeElement.GetString(), mode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return false;
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
            requirementMaturity = buildFromPlan.RequirementMaturity,
            decisionTrace = buildFromPlan.DecisionTrace,
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
