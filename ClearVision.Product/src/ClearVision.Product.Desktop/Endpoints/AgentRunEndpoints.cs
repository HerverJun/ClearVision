using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClearVision.Product.Application.Security;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        app.MapGet("/api/ai/vision-agent/planning-deadline", HandleGetPlanningDeadline);
        app.MapPost("/api/ai/sessions/{sessionId}/workspace-snapshot", HandleUpdateWorkspaceSnapshot);
        app.MapPost("/api/ai/agent-plan/readiness-preview", HandlePreviewPlanReadinessAsync);
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

    private static IResult HandleGetPlanningDeadline(IOptions<VisionAgentPlanningDeadlineOptions> options)
    {
        var value = options.Value.Normalize();
        return Results.Ok(new
        {
            contractVersion = VisionAgentPlanningDeadlineOptions.ContractVersion,
            totalBudgetMs = value.TotalBudgetMs,
            clientNetworkMarginMs = value.ClientNetworkMarginMs,
            minimumRepairBudgetMs = value.MinimumRepairBudgetMs,
            metadataOnly = true
        });
    }

    private static async Task<IResult> HandlePreviewPlanReadinessAsync(
        VisionAgentBuildReadinessPreviewRequest request,
        IVisionAgentBuildApplicationService buildApplication,
        CancellationToken ct)
    {
        var result = await buildApplication.PreviewBuildReadinessAsync(request, ct);
        return Results.Ok(result);
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

        try
        {
            var result = await router.RouteAsync(request, ct);
            return Results.Ok(result);
        }
        catch (VisionAgentPlanningDeadlineExceededException error)
        {
            return PlanningDeadlineExceeded(error);
        }
    }

    private static IResult PlanningDeadlineExceeded(VisionAgentPlanningDeadlineExceededException error) =>
        Results.Json(new
        {
            errorCode = "planning_deadline_exceeded",
            timeoutKind = "total_budget_exceeded",
            stage = error.Stage,
            publicMessage = "Vision Agent planning exceeded the published total time budget. Please retry.",
            metadataOnly = true
        }, statusCode: StatusCodes.Status504GatewayTimeout);

    private static IResult HandleUpdateWorkspaceSnapshot(
        string sessionId,
        VisionAgentWorkspaceSnapshotDeltaRequest request,
        HttpContext context,
        IConversationalFlowService conversationService)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.BadRequest(new { error = "sessionId is required." });
        }

        var result = conversationService.TryUpdateWorkspaceSnapshot(
            ResolveCurrentOwnerHash(context),
            sessionId,
            new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = request.ExpectedRevision,
            ClientMutationId = request.ClientMutationId,
            ProjectId = request.ProjectId,
            LifecycleState = request.LifecycleState,
            PlanQuestionSelections = request.PlanQuestionSelections,
            ConfirmedPlanAnswers = request.ConfirmedPlanAnswers,
            OptimisticPlanAnswers = request.OptimisticPlanAnswers,
            AnswerRevision = request.AnswerRevision,
            ReadinessPreview = request.ReadinessPreview,
            MissingResources = request.MissingResources,
            ResourceDecisions = request.ResourceDecisions,
            ResourceRevision = request.ResourceRevision,
            RequirementMode = request.RequirementMode,
            WorkspaceViewMode = request.WorkspaceViewMode,
            PlanAcceptedRecommendedDefaults = request.PlanAcceptedRecommendedDefaults,
            SubmittedBuildFingerprint = request.SubmittedBuildFingerprint
        });

        if (string.Equals(result.ErrorCode, "session_not_found", StringComparison.Ordinal))
        {
            return Results.NotFound(new { errorCode = "not_found", publicMessage = "Session not found." });
        }

        if (result.Conflict)
        {
            return Results.Conflict(result);
        }

        return result.Success
            ? Results.Ok(result)
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private static Task<IResult> HandleCreatePlanRunAsync(
        VisionAgentPlanModeRequest request,
        HttpContext context,
        IAgentRunEventStreamService streamService,
        IConversationalFlowService conversationService,
        IVisionAgentBuildRunService buildRunService,
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

        if (!request.WorkspaceExpectedRevision.HasValue || request.WorkspaceExpectedRevision.Value < 0)
        {
            return Task.FromResult<IResult>(Results.Json(new
            {
                errorCode = "workspace_revision_required",
                publicMessage = "Plan 状态缺少有效版本号，请刷新后重试。",
                metadataOnly = true
            }, statusCode: StatusCodes.Status409Conflict));
        }

        if (string.IsNullOrWhiteSpace(request.ClientMutationId))
        {
            return Task.FromResult<IResult>(Results.BadRequest(new
            {
                errorCode = "workspace_mutation_id_required",
                publicMessage = "Plan 请求缺少 clientMutationId。",
                metadataOnly = true
            }));
        }

        var ownerHash = ResolveCurrentOwnerHash(context);
        ConversationSession session;
        try
        {
            session = conversationService.GetOrCreateSession(ownerHash, request.SessionId);
        }
        catch (ConversationSessionAccessException)
        {
            return Task.FromResult<IResult>(Results.NotFound(new
            {
                errorCode = "not_found",
                publicMessage = "Session not found."
            }));
        }
        request = request with { SessionId = session.SessionId };

        var runId = BuildPlanRunId(ownerHash, session.SessionId, request.ClientMutationId);
        var initialPersistence = conversationService.TryInitializeWorkspaceSnapshot(ownerHash, session.SessionId, new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = request.WorkspaceExpectedRevision,
            ClientMutationId = BuildPlanInitialMutationId(request.ClientMutationId),
            RequireExpectedRevisionWhenWorkspaceExists = true,
            LifecycleState = "planning",
            PlanRunId = runId,
            PlanRunStatus = AgentRunEventStatuses.Running,
            RequirementMode = request.RequirementMode,
            ConfirmedPlanAnswers = request.ConfirmedPlanAnswers,
            UserTurnId = $"plan:{runId}:user",
            UserMessage = request.Description
        });
        var workspaceSnapshot = initialPersistence.Snapshot;
        if (!initialPersistence.Success)
        {
            var failureCode = initialPersistence.Conflict
                ? initialPersistence.ErrorCode
                : "session_persistence_failed";
            var publicMessage = "Plan Run 创建失败：会话状态未能保存，模型规划未启动。";
            return Task.FromResult<IResult>(Results.Json(new
            {
                errorCode = failureCode,
                publicMessage,
                sessionId = session.SessionId,
                workspaceSnapshot,
                persistenceStatus = initialPersistence.PersistenceStatus,
                metadataOnly = true
            }, statusCode: initialPersistence.Conflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status503ServiceUnavailable));
        }

        var terminalExpectedRevision = initialPersistence.AppliedRevision;
        var createResult = streamService.CreateRun(
            request.Description,
            BuildPlanCreatePayload(request, terminalExpectedRevision),
            ownerHash,
            runId);

        if (createResult.Created)
        {
            AppendPlanEvent(streamService, createResult.RunId, AgentRunEventTypes.PlanCreated, "plan", "规划已创建",
                "已创建 Plan Run，公开进度将通过事件流更新。", AgentRunEventStatuses.Completed, new
                {
                    sessionId = session.SessionId,
                    mode = "plan",
                    metadataOnly = true
                });
            AppendPlanEvent(streamService, createResult.RunId, AgentRunEventTypes.PlanStarted, "plan", "规划已启动",
                "正在进入规划阶段。", AgentRunEventStatuses.Running, new
                {
                    sessionId = session.SessionId,
                    mode = "plan",
                    metadataOnly = true
                });

            _ = Task.Run(async () =>
            {
                await RunCreatePlanAsync(
                    createResult.RunId,
                    request,
                    ownerHash,
                    terminalExpectedRevision,
                    scopeFactory,
                    loggerFactory.CreateLogger("AgentRunPlanMode"));
            });
        }

        var replay = streamService.Replay(createResult.RunId);
        return Task.FromResult<IResult>(Results.Ok(new
        {
            runId = createResult.RunId,
            sessionId = session.SessionId,
            brief = createResult.Brief,
            events = replay?.Events ?? createResult.Events,
            workspaceSnapshot,
            persistenceStatus = initialPersistence.PersistenceStatus,
            idempotentReplay = initialPersistence.IdempotentReplay || !createResult.Created
        }));
    }

    private static Task<IResult> HandleCreateRunAsync(
        AgentRunCreateRequest request,
        HttpContext context,
        IAgentRunEventStreamService streamService,
        IConversationalFlowService conversationService,
        IVisionAgentBuildRunService buildRunService,
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

        var modeDecision = AiAgentGenerateFlowModePolicy.Evaluate(
            request.AgentGenerateFlowMode,
            AiAgentGenerateFlowPolicyKind.Production);
        if (!modeDecision.Allowed)
        {
            return Task.FromResult<IResult>(Results.Json(new
            {
                errorCode = modeDecision.FailureCode,
                publicMessage = modeDecision.FailureMessage,
                requestedMode = modeDecision.RequestedMode,
                effectiveMode = modeDecision.EffectiveMode,
                metadataOnly = true
            }, statusCode: StatusCodes.Status400BadRequest));
        }

        var ownerHash = ResolveCurrentOwnerHash(context);
        string sessionId;
        try
        {
            if (request.BuildFromPlan == null)
            {
                sessionId = conversationService.GetOrCreateSession(ownerHash, request.SessionId).SessionId;
            }
            else
            {
                sessionId = NormalizeAgentRunSessionId(request.SessionId);
                if (conversationService.GetSession(ownerHash, sessionId) == null)
                {
                    return Task.FromResult<IResult>(Results.NotFound(new
                    {
                        errorCode = "not_found",
                        publicMessage = "Session not found."
                    }));
                }
            }
        }
        catch (ConversationSessionAccessException)
        {
            return Task.FromResult<IResult>(Results.NotFound(new
            {
                errorCode = "not_found",
                publicMessage = "Session not found."
            }));
        }

        request = request with { SessionId = sessionId, OwnerHash = ownerHash };

        var createResult = streamService.CreateRun(
            request.Description,
            BuildCreatePayload(request),
            ownerHash);
        VisionAgentWorkspaceSnapshot? workspaceSnapshot = null;
        var persistenceStatus = conversationService.GetLastPersistenceStatus();
        var buildAssociationPrepared = false;
        if (request.BuildFromPlan != null)
        {
            var associationResult = buildRunService.PrepareBuildAssociation(
                request.ToBuildCommand(createResult.RunId));
            workspaceSnapshot = associationResult.Snapshot;
            persistenceStatus = associationResult.PersistenceStatus;
            if (!associationResult.Success)
            {
                var failureCode = associationResult.Conflict
                    ? associationResult.ErrorCode
                    : "session_persistence_failed";
                var statusCode = associationResult.Conflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status503ServiceUnavailable;
                streamService.Fail(
                    createResult.RunId,
                    associationResult.PublicMessage,
                    "请检查本机存储权限或磁盘空间后重试 Build。",
                    new
                    {
                        runKind = VisionAgentRunKindResolver.Build,
                        projectionDisposition = VisionAgentBuildProjectionDispositionResolver.Skip,
                        associationCommitted = false,
                        associationWorkspaceRevision = (long?)null,
                        submittedBuildFingerprint = ComputeSubmittedBuildFingerprint(request),
                        planId = request.BuildFromPlan?.PlanId ?? request.BuildFromPlan?.PlanSnapshot?.PlanId ?? string.Empty,
                        planHash = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
                        answerSetFingerprint = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
                        buildIdentity = BuildBuildIdentity(
                            request.BuildFromPlan?.PlanId ?? request.BuildFromPlan?.PlanSnapshot?.PlanId ?? string.Empty,
                            request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
                            request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
                            ComputeSubmittedBuildFingerprint(request)),
                        status = AiFlowGenerationResult.CompletionStatusFailed,
                        sessionId,
                        failureCode,
                        persistenceStatus,
                        metadataOnly = true
                    });

                var failedReplay = streamService.Replay(createResult.RunId);
                return Task.FromResult<IResult>(Results.Json(new
                {
                    errorCode = failureCode,
                    publicMessage = associationResult.PublicMessage,
                    runId = createResult.RunId,
                    sessionId,
                    brief = createResult.Brief,
                    events = failedReplay?.Events ?? createResult.Events,
                    workspaceSnapshot,
                    persistenceStatus
                }, statusCode: statusCode));
            }

            buildAssociationPrepared = true;
        }
        else
        {
            var beginResult = conversationService.TryBeginAgentRun(
                ownerHash,
                sessionId,
                createResult.RunId,
                VisionAgentRunKindResolver.Build,
                $"agent-run-begin:{createResult.RunId}");
            workspaceSnapshot = beginResult.Snapshot;
            persistenceStatus = beginResult.PersistenceStatus;
            if (!beginResult.Success)
            {
                var failureCode = beginResult.Conflict
                    ? beginResult.ErrorCode
                    : "session_persistence_failed";
                var statusCode = beginResult.Conflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status503ServiceUnavailable;
                var publicMessage = string.IsNullOrWhiteSpace(beginResult.PublicMessage)
                    ? "The same conversation already has an Agent run in progress."
                    : beginResult.PublicMessage;
                streamService.Fail(
                    createResult.RunId,
                    publicMessage,
                    "Wait for the current Agent run to finish, then retry this request.",
                    new
                    {
                        runKind = VisionAgentRunKindResolver.Build,
                        projectionDisposition = VisionAgentBuildProjectionDispositionResolver.Skip,
                        associationCommitted = false,
                        associationWorkspaceRevision = (long?)null,
                        status = AiFlowGenerationResult.CompletionStatusFailed,
                        sessionId,
                        failureCode,
                        publicMessage,
                        workspaceSnapshot,
                        persistenceStatus,
                        metadataOnly = true
                    });

                var failedReplay = streamService.Replay(createResult.RunId);
                return Task.FromResult<IResult>(Results.Json(new
                {
                    errorCode = failureCode,
                    publicMessage,
                    runId = createResult.RunId,
                    sessionId,
                    brief = createResult.Brief,
                    events = failedReplay?.Events ?? createResult.Events,
                    workspaceSnapshot,
                    persistenceStatus,
                    metadataOnly = true
                }, statusCode: statusCode));
            }

            buildAssociationPrepared = true;
        }
        _ = Task.Run(async () =>
        {
            await RunGenerateFlowAsync(
                createResult.RunId,
                request,
                buildAssociationPrepared,
                scopeFactory,
                loggerFactory.CreateLogger("AgentRunGenerateFlow"));
        });

        return Task.FromResult<IResult>(Results.Ok(new
        {
            runId = createResult.RunId,
            sessionId,
            brief = createResult.Brief,
            events = streamService.Replay(createResult.RunId)?.Events ?? createResult.Events,
            workspaceSnapshot,
            persistenceStatus
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
            ? Results.Ok(ProjectPublicReplay(replay))
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static IResult HandleReplayLatestRun(
        HttpContext context,
        IAgentRunEventStreamService streamService)
    {
        var replay = streamService.ReplayLatest(ResolveCurrentOwnerHash(context));
        return replay == null
            ? Results.NotFound(new { error = "No Agent run replay is available." })
            : Results.Ok(ProjectPublicReplay(replay));
    }

    private static object ProjectPublicReplay(AgentRunReplayResult replay) => new
    {
        summary = ProjectPublicSummary(replay.Summary),
        replay.Events,
        replay.Snapshot,
        replay.Diagnostics
    };

    private static object? ProjectPublicSummary(AgentRunSummary? summary)
    {
        if (summary == null)
        {
            return null;
        }

        return new
        {
            summary.RunId,
            summary.CreatedAt,
            summary.UpdatedAt,
            summary.Status,
            summary.Title,
            summary.Summary,
            summary.FirstFixRecommendation,
            summary.LastSequence,
            summary.EventCount,
            summary.DuplicateEventCount,
            summary.DroppedEventCount,
            summary.StaleEventCount,
            summary.TerminalIntent,
            summary.MetadataOnly,
            summary.RedactionPass,
            summary.Payload
        };
    }

    private static IResult HandleCancelRun(
        string runId,
        HttpContext context,
        IAgentRunEventStreamService streamService,
        IConversationalFlowService conversationService)
    {
        var replayBeforeCancel = streamService.Replay(runId);
        if (replayBeforeCancel == null)
        {
            return Results.NotFound(new { error = "Agent run not found." });
        }

        var ownerHash = ResolveCurrentOwnerHash(context);
        if (!streamService.IsRunOwner(runId, ownerHash))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (ReplayHasMode(replayBeforeCancel, "plan"))
        {
            var reservation = streamService.TryReserveTerminal(runId, AgentRunEventStatuses.Cancelled);
            if (!reservation.Acquired)
            {
                return BuildCancelReservationResponse(runId, reservation, streamService.Replay(runId) ?? replayBeforeCancel);
            }

            var cancelledEvent = AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanCancelled, "plan", "规划已取消",
                "用户已取消本次规划，事件流即将关闭。", AgentRunEventStatuses.Cancelled, new
                {
                    metadataOnly = true
                });
            var sessionId = TryResolvePlanRunSessionId(replayBeforeCancel);
            object? persistenceWarning = null;
            VisionAgentWorkspaceSnapshot? finalWorkspaceSnapshot = null;
            ConversationPersistenceStatus? finalPersistenceStatus = null;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var terminalUpdate = new VisionAgentWorkspaceSnapshotUpdate
                {
                    ExpectedRevision = TryResolvePlanTerminalExpectedRevision(replayBeforeCancel),
                    ClientMutationId = BuildPlanTerminalMutationId(runId, AgentRunEventStatuses.Cancelled),
                    RequireExpectedRevisionWhenWorkspaceExists = true,
                    LifecycleState = "plan_cancelled",
                    PlanRunId = runId,
                    PlanRunStatus = AgentRunEventStatuses.Cancelled,
                    PlanTerminalSequence = cancelledEvent?.Sequence,
                    RequirementMode = TryResolvePlanRequirementMode(replayBeforeCancel)
                };
                PreparePlanTerminalIntent(
                    streamService,
                    runId,
                    sessionId,
                    AgentRunEventStatuses.Cancelled,
                    terminalUpdate,
                    "plan_cancelled",
                    reservation);
                var terminalPersistence = conversationService.TryUpdateWorkspaceSnapshot(ownerHash, sessionId, terminalUpdate);
                finalWorkspaceSnapshot = terminalPersistence.Snapshot;
                finalPersistenceStatus = terminalPersistence.PersistenceStatus;
                if (!terminalPersistence.Success)
                {
                    persistenceWarning = BuildPlanPersistenceWarning(terminalPersistence);
                    AppendPlanPersistenceWarning(streamService, runId, terminalPersistence);
                }
            }

            var planCancelled = streamService.Cancel(
                runId,
                "规划已取消。",
                BuildPlanTerminalPayload(
                    "plan_cancelled",
                    sessionId,
                    runId,
                    "规划已取消。",
                    finalWorkspaceSnapshot,
                    finalPersistenceStatus,
                    persistenceWarning),
                reservation);
            var planReplay = streamService.Replay(runId);
            return Results.Ok(new
            {
                runId,
                cancelled = planCancelled != null,
                summary = ProjectPublicSummary(planReplay?.Summary)
            });
        }

        var nonPlanReservation = streamService.TryReserveTerminal(runId, AgentRunEventStatuses.Cancelled);
        if (!nonPlanReservation.Acquired)
        {
            return BuildCancelReservationResponse(runId, nonPlanReservation, streamService.Replay(runId) ?? replayBeforeCancel);
        }

        var cancelled = streamService.Cancel(runId, reservation: nonPlanReservation);
        var replay = streamService.Replay(runId);
        return Results.Ok(new
        {
            runId,
            cancelled = cancelled != null,
            summary = ProjectPublicSummary(replay?.Summary)
        });
    }

    private static IResult BuildCancelReservationResponse(
        string runId,
        AgentRunTerminalReservationResult reservation,
        AgentRunReplayResult? replay)
    {
        var terminalStatus = NormalizeReservedTerminalStatus(reservation.CurrentStatus);
        if (reservation.Outcome == AgentRunTerminalReservationOutcome.RunNotFound)
        {
            return Results.NotFound(new { error = "Agent run not found." });
        }

        if (reservation.Outcome == AgentRunTerminalReservationOutcome.AlreadyTerminal &&
            string.Equals(terminalStatus, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                runId,
                cancelled = true,
                cancellationStatus = AgentRunEventStatuses.Cancelled,
                summary = ProjectPublicSummary(replay?.Summary),
                metadataOnly = true
            });
        }

        if (reservation.Outcome == AgentRunTerminalReservationOutcome.AlreadyReservedBySameStatus &&
            string.Equals(terminalStatus, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                runId,
                cancelled = false,
                cancellationStatus = reservation.CurrentStatus,
                publicMessage = "Cancellation is already being committed.",
                summary = ProjectPublicSummary(replay?.Summary),
                metadataOnly = true
            });
        }

        return Results.Json(new
        {
            errorCode = "run_already_terminal",
            runId,
            terminalStatus,
            currentStatus = reservation.CurrentStatus,
            publicMessage = BuildCancelRejectedPublicMessage(terminalStatus),
            summary = ProjectPublicSummary(replay?.Summary),
            metadataOnly = true
        }, statusCode: StatusCodes.Status409Conflict);
    }

    private static string NormalizeReservedTerminalStatus(string status)
    {
        if (string.Equals(status, "completing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Completed;
        }

        if (string.Equals(status, "failing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Failed;
        }

        if (string.Equals(status, "cancelling", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRunEventStatuses.Cancelled;
        }

        return status;
    }

    private static string BuildCancelRejectedPublicMessage(string terminalStatus)
    {
        if (string.Equals(terminalStatus, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return "\u672c\u6b21\u89c4\u5212\u5df2\u7ecf\u5b8c\u6210\uff0c\u65e0\u6cd5\u518d\u53d6\u6d88\u3002";
        }

        if (string.Equals(terminalStatus, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return "\u672c\u6b21\u89c4\u5212\u5df2\u7ecf\u5931\u8d25\uff0c\u65e0\u6cd5\u518d\u53d6\u6d88\u3002";
        }

        return "\u672c\u6b21\u89c4\u5212\u5df2\u8fdb\u5165\u7ec8\u6001\uff0c\u65e0\u6cd5\u518d\u53d6\u6d88\u3002";
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
        string ownerHash,
        long terminalExpectedRevision,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        using var scope = scopeFactory.CreateScope();
        var streamService = scope.ServiceProvider.GetRequiredService<IAgentRunEventStreamService>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IVisionAgentOrchestrator>();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
        var cancellationToken = streamService.GetCancellationToken(runId);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.PlanContextStarted, "collecting_context",
                "收集上下文", "正在收集公开需求、流程、模板、附件、算子和工站边界。", AgentRunEventStatuses.Running,
                BuildPlanContextPayload(request));
            if (request.SemanticExtraction == null)
            {
                EmitPlanStage(streamService, runId, emitted, AgentRunEventTypes.SemanticStarted, "semantic_extraction",
                    "语义抽取中", "正在抽取视觉需求语义槽位。", AgentRunEventStatuses.Running, new
                    {
                        metadataOnly = true
                    });
            }
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
                var cancelReservation = streamService.TryReserveTerminal(runId, AgentRunEventStatuses.Cancelled);
                if (!cancelReservation.Acquired)
                {
                    return;
                }

                var cancelledEvent = AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanCancelled, "plan",
                    "规划已取消", "规划已取消，未发布完成结果。", AgentRunEventStatuses.Cancelled, new
                    {
                        metadataOnly = true
                    });
                object? cancelPersistenceWarning = null;
                VisionAgentWorkspaceSnapshot? cancelWorkspaceSnapshot = null;
                ConversationPersistenceStatus? cancelPersistenceStatus = null;
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    var terminalUpdate = new VisionAgentWorkspaceSnapshotUpdate
                    {
                        ExpectedRevision = terminalExpectedRevision,
                        ClientMutationId = BuildPlanTerminalMutationId(runId, AgentRunEventStatuses.Cancelled),
                        RequireExpectedRevisionWhenWorkspaceExists = true,
                        LifecycleState = "plan_cancelled",
                        PlanRunId = runId,
                        PlanRunStatus = AgentRunEventStatuses.Cancelled,
                        PlanTerminalSequence = cancelledEvent?.Sequence,
                        RequirementMode = request.RequirementMode
                    };
                    PreparePlanTerminalIntent(
                        streamService,
                        runId,
                        request.SessionId,
                        AgentRunEventStatuses.Cancelled,
                        terminalUpdate,
                        "plan_cancelled",
                        cancelReservation);
                    var terminalPersistence = conversationService.TryUpdateWorkspaceSnapshot(ownerHash, request.SessionId, terminalUpdate);
                    cancelWorkspaceSnapshot = terminalPersistence.Snapshot;
                    cancelPersistenceStatus = terminalPersistence.PersistenceStatus;
                    if (!terminalPersistence.Success)
                    {
                        cancelPersistenceWarning = BuildPlanPersistenceWarning(terminalPersistence);
                        AppendPlanPersistenceWarning(streamService, runId, terminalPersistence);
                    }
                }

                streamService.Cancel(
                    runId,
                    "规划已取消。",
                    BuildPlanTerminalPayload(
                        "plan_cancelled",
                        request.SessionId,
                        runId,
                        "规划已取消。",
                        cancelWorkspaceSnapshot,
                        cancelPersistenceStatus,
                        cancelPersistenceWarning),
                    cancelReservation);
                return;
            }

            var completeReservation = streamService.TryReserveTerminal(runId, AgentRunEventStatuses.Completed);
            if (!completeReservation.Acquired)
            {
                return;
            }

            EmitPlanResultEvents(streamService, runId, result, emitted);

            var completedPayload = BuildPlanCompletedPayload(result, request.SessionId, runId);
            var completedEvent = AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanCompleted, "plan_ready",
                "规划已就绪", BuildPlanCompletionSummary(result), AgentRunEventStatuses.Completed, completedPayload);
            object? persistenceWarning = null;
            VisionAgentWorkspaceSnapshot? finalWorkspaceSnapshot = null;
            ConversationPersistenceStatus? finalPersistenceStatus = null;
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var terminalUpdate = new VisionAgentWorkspaceSnapshotUpdate
                {
                    ExpectedRevision = terminalExpectedRevision,
                    ClientMutationId = BuildPlanTerminalMutationId(runId, AgentRunEventStatuses.Completed),
                    RequireExpectedRevisionWhenWorkspaceExists = true,
                    LifecycleState = result.CanBuild ? "plan_ready" : "plan_blocked",
                    PendingPlanSnapshot = BuildReplaySafePlanResult(result),
                    PlanRunId = runId,
                    PlanRunStatus = AgentRunEventStatuses.Completed,
                    PlanTerminalSequence = completedEvent?.Sequence,
                    RequirementMode = request.RequirementMode,
                    ConfirmedPlanAnswers = result.ConfirmedPlanAnswers.Count > 0
                        ? result.ConfirmedPlanAnswers
                        : request.ConfirmedPlanAnswers
                };
                PreparePlanTerminalIntent(
                    streamService,
                    runId,
                    request.SessionId,
                    AgentRunEventStatuses.Completed,
                    terminalUpdate,
                    BuildPlanTerminalIdentity(result),
                    completeReservation);
                var terminalPersistence = conversationService.TryUpdateWorkspaceSnapshot(ownerHash, request.SessionId, terminalUpdate);
                finalWorkspaceSnapshot = terminalPersistence.Snapshot;
                finalPersistenceStatus = terminalPersistence.PersistenceStatus;
                if (!terminalPersistence.Success)
                {
                    persistenceWarning = BuildPlanPersistenceWarning(terminalPersistence);
                    AppendPlanPersistenceWarning(streamService, runId, terminalPersistence);
                }
            }

            streamService.Complete(
                runId,
                BuildPlanCompletionSummary(result),
                BuildPlanCompletedPayload(
                    result,
                    request.SessionId,
                    runId,
                    finalWorkspaceSnapshot,
                    finalPersistenceStatus,
                    persistenceWarning),
                completeReservation);
        }
        catch (OperationCanceledException)
        {
            var cancelReservation = streamService.TryReserveTerminal(runId, AgentRunEventStatuses.Cancelled);
            if (!cancelReservation.Acquired)
            {
                return;
            }

            var cancelledEvent = AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanCancelled, "plan",
                "规划已取消", "规划已取消，未发布完成结果。", AgentRunEventStatuses.Cancelled, new
                {
                    sessionId = request.SessionId,
                    metadataOnly = true
                });
            object? persistenceWarning = null;
            VisionAgentWorkspaceSnapshot? finalWorkspaceSnapshot = null;
            ConversationPersistenceStatus? finalPersistenceStatus = null;
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var terminalUpdate = new VisionAgentWorkspaceSnapshotUpdate
                {
                    ExpectedRevision = terminalExpectedRevision,
                    ClientMutationId = BuildPlanTerminalMutationId(runId, AgentRunEventStatuses.Cancelled),
                    RequireExpectedRevisionWhenWorkspaceExists = true,
                    LifecycleState = "plan_cancelled",
                    PlanRunId = runId,
                    PlanRunStatus = AgentRunEventStatuses.Cancelled,
                    PlanTerminalSequence = cancelledEvent?.Sequence,
                    RequirementMode = request.RequirementMode
                };
                PreparePlanTerminalIntent(
                    streamService,
                    runId,
                    request.SessionId,
                    AgentRunEventStatuses.Cancelled,
                    terminalUpdate,
                    "plan_cancelled",
                    cancelReservation);
                var terminalPersistence = conversationService.TryUpdateWorkspaceSnapshot(ownerHash, request.SessionId, terminalUpdate);
                finalWorkspaceSnapshot = terminalPersistence.Snapshot;
                finalPersistenceStatus = terminalPersistence.PersistenceStatus;
                if (!terminalPersistence.Success)
                {
                    persistenceWarning = BuildPlanPersistenceWarning(terminalPersistence);
                    AppendPlanPersistenceWarning(streamService, runId, terminalPersistence);
                }
            }
            streamService.Cancel(
                runId,
                "规划已取消。",
                BuildPlanTerminalPayload(
                    "plan_cancelled",
                    request.SessionId,
                    runId,
                    "规划已取消。",
                    finalWorkspaceSnapshot,
                    finalPersistenceStatus,
                    persistenceWarning),
                cancelReservation);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AgentRun PlanMode background task failed. RunId={RunId}", runId);
            var deadlineError = ex as VisionAgentPlanningDeadlineExceededException;
            var errorCode = deadlineError == null ? "plan_failed" : "planning_deadline_exceeded";
            var failReservation = streamService.TryReserveTerminal(runId, AgentRunEventStatuses.Failed);
            if (!failReservation.Acquired)
            {
                return;
            }

            var failedEvent = AppendPlanEvent(streamService, runId, AgentRunEventTypes.PlanFailed, "plan",
                "规划失败", "规划在完成前失败，请检查公开诊断后重试。", AgentRunEventStatuses.Failed, new
                {
                    sessionId = request.SessionId,
                    errorCode,
                    timeoutKind = deadlineError == null ? string.Empty : "total_budget_exceeded",
                    stage = deadlineError?.Stage ?? string.Empty,
                    error = ex.Message,
                    metadataOnly = true
                });
            object? persistenceWarning = null;
            VisionAgentWorkspaceSnapshot? finalWorkspaceSnapshot = null;
            ConversationPersistenceStatus? finalPersistenceStatus = null;
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var terminalUpdate = new VisionAgentWorkspaceSnapshotUpdate
                {
                    ExpectedRevision = terminalExpectedRevision,
                    ClientMutationId = BuildPlanTerminalMutationId(runId, AgentRunEventStatuses.Failed),
                    RequireExpectedRevisionWhenWorkspaceExists = true,
                    LifecycleState = "plan_failed",
                    PlanRunId = runId,
                    PlanRunStatus = AgentRunEventStatuses.Failed,
                    PlanTerminalSequence = failedEvent?.Sequence,
                    RequirementMode = request.RequirementMode
                };
                PreparePlanTerminalIntent(
                    streamService,
                    runId,
                    request.SessionId,
                    AgentRunEventStatuses.Failed,
                    terminalUpdate,
                    "plan_failed",
                    failReservation);
                var terminalPersistence = conversationService.TryUpdateWorkspaceSnapshot(ownerHash, request.SessionId, terminalUpdate);
                finalWorkspaceSnapshot = terminalPersistence.Snapshot;
                finalPersistenceStatus = terminalPersistence.PersistenceStatus;
                if (!terminalPersistence.Success)
                {
                    persistenceWarning = BuildPlanPersistenceWarning(terminalPersistence);
                    AppendPlanPersistenceWarning(streamService, runId, terminalPersistence);
                }
            }
            streamService.Fail(
                runId,
                "规划在完成前失败。",
                "请检查公开诊断并重试规划。",
                new
                {
                    status = "plan_failed",
                    mode = "plan",
                    errorCode,
                    timeoutKind = deadlineError == null ? string.Empty : "total_budget_exceeded",
                    stage = deadlineError?.Stage ?? string.Empty,
                    publicMessage = "规划在完成前失败。",
                    error = ex.Message,
                    workspaceSnapshot = finalWorkspaceSnapshot,
                    persistenceStatus = finalPersistenceStatus,
                    persistenceWarning,
                    metadataOnly = true
                },
                failReservation);
        }
    }

    private static async Task RunGenerateFlowAsync(
        string runId,
        AgentRunCreateRequest request,
        bool buildAssociationPrepared,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        using var scope = scopeFactory.CreateScope();
        var buildRunService = scope.ServiceProvider.GetRequiredService<IVisionAgentBuildRunService>();

        try
        {
            await buildRunService.RunAsync(
                request.ToBuildCommand(runId) with { BuildAssociationPrepared = buildAssociationPrepared },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AgentRun BuildFromPlan runner failed unexpectedly. RunId={RunId}", runId);
        }
    }
    private static object BuildCreatePayload(AgentRunCreateRequest request)
    {
        return new
        {
            runKind = request.BuildFromPlan == null
                ? VisionAgentRunKindResolver.Unknown
                : VisionAgentRunKindResolver.Build,
            mode = request.Mode ?? request.BuildFromPlan?.BuildIntent ?? "auto",
            useVisionAgentGenerateFlow = request.UseVisionAgentGenerateFlow ?? true,
            agentGenerateFlowMode = request.AgentGenerateFlowMode ?? AiAgentGenerateFlowModes.Scripted,
            sessionId = request.SessionId,
            attachmentCount = request.BuildFromPlan?.AttachmentSummary.Count ?? request.AttachmentCount ?? request.Attachments?.Count ?? 0,
            planId = request.BuildFromPlan?.PlanId ?? string.Empty,
            planHash = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
            hasPlanSnapshot = request.BuildFromPlan?.PlanSnapshot != null,
            hasCurrentFlowSnapshot = !string.IsNullOrWhiteSpace(request.BuildFromPlan?.CurrentFlowSnapshot) ||
                                     !string.IsNullOrWhiteSpace(request.ExistingFlowJson),
            metadataOnly = true
        };
    }

    private static string ComputeSubmittedBuildFingerprint(AgentRunCreateRequest request)
    {
        var json = JsonSerializer.Serialize(new
        {
            request.BuildFromPlan?.PlanId,
            planHash = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash,
            request.RequirementMode,
            request.BuildFromPlan?.AcceptedRecommendedDefaults,
            request.BuildFromPlan?.AcceptedDefaults,
            request.BuildFromPlan?.ConfirmedAnswers,
            request.BuildFromPlan?.UserSelections
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildBuildIdentity(
        string planId,
        string planHash,
        string answerSetFingerprint,
        string submittedBuildFingerprint)
    {
        return string.Join(
            ":",
            new[] { planId, planHash, answerSetFingerprint, submittedBuildFingerprint }
                .Select(SanitizeBuildIdentityToken)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string SanitizeBuildIdentityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            string.Empty,
            value.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is ':' or '_' or '-' or '.'));
    }

    private static object BuildPlanCreatePayload(
        VisionAgentPlanModeRequest request,
        long terminalExpectedRevision)
    {
        return new
        {
            runKind = VisionAgentRunKindResolver.Plan,
            mode = "plan",
            sessionId = request.SessionId,
            hasCurrentFlowSnapshot = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            hasCurrentResultSnapshot = !string.IsNullOrWhiteSpace(request.CurrentResultSnapshot),
            attachmentCount = request.AttachmentSummary.Count,
            templateSelectionMode = request.TemplateSelection?.Mode ?? string.Empty,
            templateId = request.TemplateSelection?.TemplateId ?? string.Empty,
            historySummaryIncluded = !string.IsNullOrWhiteSpace(request.HistorySummary),
            requirementMode = request.RequirementMode,
            terminalExpectedRevision,
            metadataOnly = true
        };
    }

    private static object BuildPlanContextPayload(VisionAgentPlanModeRequest request)
    {
        return new
        {
            hasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            sessionId = request.SessionId,
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

    private static object BuildPlanCompletedPayload(
        VisionAgentPlanModeResult result,
        string? sessionId,
        string runId,
        VisionAgentWorkspaceSnapshot? workspaceSnapshot = null,
        ConversationPersistenceStatus? persistenceStatus = null,
        object? persistenceWarning = null)
    {
        var replaySafePlan = BuildReplaySafePlanResult(result);
        return new
        {
            status = "plan_completed",
            generationMode = "plan",
            sessionId,
            planRunId = runId,
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
            workspaceSnapshot,
            persistenceStatus,
            persistenceWarning,
            metadataOnly = true
        };
    }

    private static object BuildPlanTerminalPayload(
        string status,
        string? sessionId,
        string runId,
        string publicMessage,
        VisionAgentWorkspaceSnapshot? workspaceSnapshot,
        ConversationPersistenceStatus? persistenceStatus,
        object? persistenceWarning)
    {
        return new
        {
            status,
            generationMode = "plan",
            sessionId,
            planRunId = runId,
            publicMessage,
            workspaceSnapshot,
            persistenceStatus,
            persistenceWarning,
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
            BuildReadiness = SanitizeBuildReadiness(result.BuildReadiness),
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

    private static VisionAgentBuildReadinessSnapshot SanitizeBuildReadiness(VisionAgentBuildReadinessSnapshot? readiness)
    {
        if (readiness == null)
        {
            return new VisionAgentBuildReadinessSnapshot();
        }

        return readiness with
        {
            Blockers = readiness.Blockers.Select(blocker => blocker with
            {
                Id = SanitizePlanToken(blocker.Id),
                Category = SanitizePlanToken(blocker.Category),
                Field = SanitizePlanToken(blocker.Field),
                QuestionId = SanitizePlanToken(blocker.QuestionId),
                ResolutionMode = SanitizePlanToken(blocker.ResolutionMode),
                PublicLabel = SanitizePlanText(blocker.PublicLabel),
                Resource = SanitizeResourceRequirement(blocker.Resource)
            }).ToList(),
            ResolvedFields = SanitizePlanList(readiness.ResolvedFields).Select(SanitizePlanToken).ToList(),
            RemainingFields = SanitizePlanList(readiness.RemainingFields).Select(SanitizePlanToken).ToList(),
            PrimaryMessage = SanitizePlanText(readiness.PrimaryMessage),
            ContractVersion = SanitizePlanToken(readiness.ContractVersion),
            MissingResources = readiness.MissingResources
                .Select(resource => SanitizeResourceRequirement(resource)!)
                .ToList()
        };
    }

    private static VisionAgentResourceRequirement? SanitizeResourceRequirement(VisionAgentResourceRequirement? resource)
    {
        return resource == null ? null : resource with
        {
            CanonicalId = SanitizePlanText(resource.CanonicalId),
            ResourceType = SanitizePlanToken(resource.ResourceType),
            ResourceName = SanitizePlanText(resource.ResourceName),
            ResourceKey = SanitizePlanText(resource.ResourceKey),
            OperatorKey = SanitizePlanText(resource.OperatorKey),
            OperatorId = SanitizePlanToken(resource.OperatorId),
            OperatorType = SanitizePlanToken(resource.OperatorType),
            ParameterName = SanitizePlanToken(resource.ParameterName),
            Status = SanitizePlanToken(resource.Status),
            BlockingScope = SanitizePlanToken(resource.BlockingScope),
            Source = SanitizePlanToken(resource.Source),
            ResolutionTarget = SanitizePlanText(resource.ResolutionTarget),
            DraftPolicy = SanitizePlanToken(resource.DraftPolicy),
            Description = SanitizePlanText(resource.Description),
            Aliases = resource.Aliases.Select(SanitizePlanText).Where(value => value.Length > 0).ToList()
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

    private static AgentRunEvent? AppendPlanPersistenceWarning(
        IAgentRunEventStreamService streamService,
        string runId,
        VisionAgentWorkspaceSnapshotMutationResult persistence)
    {
        return AppendPlanEvent(
            streamService,
            runId,
            AgentRunEventTypes.StageCompleted,
            "workspace_persistence",
            "Plan 状态未保存",
            "规划结果已生成，但本次 Plan 工作台状态未能保存；请检查本机会话存储后重试。",
            AgentRunEventStatuses.Warning,
            new
            {
                persistenceWarning = BuildPlanPersistenceWarning(persistence),
                persistenceStatus = persistence.PersistenceStatus,
                metadataOnly = true
            });
    }

    private static object BuildPlanPersistenceWarning(VisionAgentWorkspaceSnapshotMutationResult persistence)
    {
        return new
        {
            code = string.IsNullOrWhiteSpace(persistence.ErrorCode)
                ? "session_persistence_failed"
                : persistence.ErrorCode,
            message = string.IsNullOrWhiteSpace(persistence.PublicMessage)
                ? "规划结果已生成，但本次 Plan 工作台状态未能保存。"
                : persistence.PublicMessage,
            persistenceStatus = persistence.PersistenceStatus
        };
    }

    private static string BuildPlanTerminalMutationId(string runId, string status)
    {
        var normalizedStatus = string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            ? AgentRunEventStatuses.Cancelled
            : string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase)
                ? AgentRunEventStatuses.Failed
                : AgentRunEventStatuses.Completed;
        return $"plan-terminal:{runId}:{normalizedStatus}";
    }

    private static string BuildPlanInitialMutationId(string clientMutationId) =>
        $"plan-start:{clientMutationId.Trim()}";

    private static string BuildPlanRunId(
        string ownerHash,
        string sessionId,
        string clientMutationId)
    {
        var identity = string.Join(
            "\n",
            ownerHash.Trim(),
            sessionId.Trim().ToUpperInvariant(),
            clientMutationId.Trim().ToUpperInvariant());
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"ar_plan_{Convert.ToHexString(digest).ToLowerInvariant()[..32]}";
    }

    private static AgentRunTerminalIntentRecord? PreparePlanTerminalIntent(
        IAgentRunEventStreamService streamService,
        string runId,
        string sessionId,
        string targetStatus,
        VisionAgentWorkspaceSnapshotUpdate terminalUpdate,
        string identity,
        AgentRunTerminalReservationResult reservation)
    {
        return streamService.PrepareTerminalIntent(
            runId,
            new AgentRunTerminalIntentDraft
            {
                SessionId = sessionId,
                RunType = "plan",
                TargetStatus = targetStatus,
                TerminalMutationId = terminalUpdate.ClientMutationId ?? BuildPlanTerminalMutationId(runId, targetStatus),
                PayloadFingerprint = ConversationalFlowService.ComputeWorkspaceMutationFingerprint(terminalUpdate),
                ExpectedWorkspaceRevision = terminalUpdate.ExpectedRevision,
                Identity = identity,
                Phase = "TerminalPrepared"
            },
            reservation);
    }

    private static string BuildPlanTerminalIdentity(VisionAgentPlanModeResult result)
    {
        return string.Join(
            ":",
            new[]
            {
                SanitizePlanToken(result.PlanId),
                SanitizePlanToken(result.PlanHash),
                result.CanBuild ? "can_build" : "blocked"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
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

    private static bool IsRunTerminal(IAgentRunEventStreamService streamService, string runId)
    {
        var status = streamService.Replay(runId)?.Summary.Status;
        return string.Equals(status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolvePlanRunSessionId(AgentRunReplayResult replay) =>
        TryResolvePlanRunString(replay, "sessionId");

    private static string? TryResolvePlanRequirementMode(AgentRunReplayResult replay) =>
        TryResolvePlanRunString(replay, "requirementMode");

    private static long? TryResolvePlanTerminalExpectedRevision(AgentRunReplayResult replay)
    {
        foreach (var evt in replay.Events)
        {
            try
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(evt.Payload, SseJsonOptions));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("terminalExpectedRevision", out var element) &&
                    element.TryGetInt64(out var revision))
                {
                    return revision;
                }
            }
            catch (JsonException)
            {
                // Ignore non-object payloads when recovering PlanRun CAS context from replay.
            }
        }

        return null;
    }

    private static string? TryResolvePlanRunString(AgentRunReplayResult replay, string propertyName)
    {
        foreach (var evt in replay.Events)
        {
            try
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(evt.Payload, SseJsonOptions));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(propertyName, out var element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore non-object payloads when recovering PlanRun context from replay.
            }
        }

        return null;
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
        return AuthenticatedOwnerResolver.ResolveOwnerHash(userId);
    }

    private static string NormalizeAgentRunSessionId(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId.Trim();

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
    public string? RequestId { get; init; }
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

    [JsonIgnore]
    public string OwnerHash { get; init; } = string.Empty;

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
            OwnerHash = OwnerHash,
            RequirementMode = string.IsNullOrWhiteSpace(RequirementMode)
                ? AiRequirementModes.Strict
                : RequirementMode!,
            UseVisionAgentGenerateFlow = UseVisionAgentGenerateFlow ?? true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Normalize(AgentGenerateFlowMode),
            RuntimePreviewConsent = RuntimePreviewConsent,
            AgentRunId = runId,
            BuildFromPlan = BuildFromPlan
        };
    }

    public BuildCommand ToBuildCommand(string runId, bool persistResult = false)
    {
        return BuildCommand.FromGenerationRequest(
            ToGenerationRequest(runId),
            runId,
            RequestId,
            BuildCommandTransports.AgentRun,
            persistResult);
    }
}

public sealed record VisionAgentWorkspaceSnapshotDeltaRequest
{
    public long ExpectedRevision { get; init; }
    public string ClientMutationId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? LifecycleState { get; init; }
    public Dictionary<string, string>? PlanQuestionSelections { get; init; }
    public List<VisionAgentPlanAnswer>? ConfirmedPlanAnswers { get; init; }
    public List<VisionAgentPlanAnswer>? OptimisticPlanAnswers { get; init; }
    public int? AnswerRevision { get; init; }
    public VisionAgentBuildReadinessPreviewResult? ReadinessPreview { get; init; }
    public List<VisionAgentResourceRequirement>? MissingResources { get; init; }
    public Dictionary<string, JsonElement>? ResourceDecisions { get; init; }
    public int? ResourceRevision { get; init; }
    public string? RequirementMode { get; init; }
    public string? WorkspaceViewMode { get; init; }
    public bool? PlanAcceptedRecommendedDefaults { get; init; }
    public string? SubmittedBuildFingerprint { get; init; }
}
