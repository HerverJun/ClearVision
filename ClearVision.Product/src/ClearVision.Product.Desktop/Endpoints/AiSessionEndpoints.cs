using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class AiSessionEndpoints
{
    public static IEndpointRouteBuilder MapAiSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ai/sessions", HandleCreateSessionAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/sessions", HandleListSessions)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/sessions/{sessionId}", HandleGetSession)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapDelete("/api/ai/sessions/{sessionId}", HandleDeleteSession)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapPost("/api/ai/sessions/{sessionId}/workspace-snapshot", HandleUpdateWorkspaceSnapshot)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/operations/{clientOperationId:guid}", HandleGetOperation)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        return app;
    }

    private static async Task<IResult> HandleCreateSessionAsync(
        AiSessionCreateRequest request,
        HttpContext context,
        IConversationalFlowService conversationService,
        IAiOperationReceiptStore operations,
        IProjectApplicationService projects)
    {
        if (request.ClientOperationId == Guid.Empty)
        {
            return BadRequest("client_operation_id_required", "clientOperationId 不能为空。");
        }

        if (request.ProjectId.HasValue && request.ProjectId != Guid.Empty &&
            await projects.GetByIdAsync(request.ProjectId.Value) == null)
        {
            return Results.NotFound(new
            {
                errorCode = "project_not_found",
                publicMessage = "工程不存在或当前用户无权访问。"
            });
        }

        var ownerHash = AiOwnerIdentity.Resolve(context);
        var reservation = operations.Reserve(
            ownerHash,
            AiOperationKinds.SessionCreate,
            request.ClientOperationId,
            ComputeFingerprint(new { schemaVersion = 1, request.ProjectId }),
            projectBaseline: null);
        var reservationError = BuildReservationError(reservation);
        if (reservationError != null) return reservationError;

        if (reservation.Outcome == AiOperationReservationOutcome.Existing)
        {
            var existing = reservation.Receipt!;
            var session = string.IsNullOrWhiteSpace(existing.SessionId)
                ? null
                : conversationService.GetOwnedSession(ownerHash, existing.SessionId);
            return Results.Ok(new
            {
                operation = AiPublicContractMapper.ToOperation(existing),
                session = session == null ? null : AiPublicContractMapper.ToDetail(session)
            });
        }

        var created = conversationService.GetOrCreateOwnedSession(
            ownerHash,
            sessionId: null,
            request.ProjectId?.ToString("D"));
        if (created.Status != ConversationOwnedSessionStatus.Ready || created.Session == null)
        {
            var failed = operations.MarkFailed(
                ownerHash,
                AiOperationKinds.SessionCreate,
                request.ClientOperationId,
                "session_persistence_failed",
                "会话未能安全保存，请检查本机存储后重试。");
            return Results.Json(new
            {
                errorCode = "session_persistence_failed",
                publicMessage = "会话未能安全保存，请检查本机存储后重试。",
                operation = failed == null ? null : AiPublicContractMapper.ToOperation(failed)
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var receipt = operations.MarkCreated(
            ownerHash,
            AiOperationKinds.SessionCreate,
            request.ClientOperationId,
            sessionId: created.Session.SessionId);
        if (receipt == null)
        {
            return Results.Json(new
            {
                errorCode = "operation_receipt_persistence_failed",
                publicMessage = "会话已创建，但操作回执未能确认；请使用会话列表恢复。"
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(new
        {
            operation = AiPublicContractMapper.ToOperation(receipt),
            session = AiPublicContractMapper.ToDetail(created.Session)
        }, statusCode: StatusCodes.Status201Created);
    }

    private static IResult HandleListSessions(
        HttpContext context,
        IConversationalFlowService conversationService,
        int offset = 0,
        int limit = 25)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        var sessions = conversationService.ListOwnedSessions(AiOwnerIdentity.Resolve(context));
        var items = sessions.Skip(offset).Take(limit)
            .Select(summary => conversationService.GetOwnedSession(AiOwnerIdentity.Resolve(context), summary.SessionId))
            .Where(session => session != null)
            .Select(session => AiPublicContractMapper.ToSummary(session!))
            .ToArray();
        return Results.Ok(new AiSessionPageV1(items, offset, limit, sessions.Count));
    }

    private static IResult HandleGetSession(
        string sessionId,
        HttpContext context,
        IConversationalFlowService conversationService)
    {
        var session = conversationService.GetOwnedSession(AiOwnerIdentity.Resolve(context), sessionId);
        return session == null
            ? Results.NotFound(new { errorCode = "session_not_found", publicMessage = "会话不存在或当前用户无权访问。" })
            : Results.Ok(AiPublicContractMapper.ToDetail(session));
    }

    private static IResult HandleDeleteSession(
        string sessionId,
        long expectedRevision,
        Guid clientMutationId,
        HttpContext context,
        IConversationalFlowService conversationService,
        IAiOperationReceiptStore operations)
    {
        if (clientMutationId == Guid.Empty)
        {
            return BadRequest("client_mutation_id_required", "clientMutationId 不能为空。");
        }

        var ownerHash = AiOwnerIdentity.Resolve(context);
        var reservation = operations.Reserve(
            ownerHash,
            AiOperationKinds.SessionDelete,
            clientMutationId,
            ComputeFingerprint(new { schemaVersion = 1, sessionId = sessionId.Trim(), expectedRevision }),
            sessionId);
        var reservationError = BuildReservationError(reservation);
        if (reservationError != null) return reservationError;
        if (reservation.Outcome == AiOperationReservationOutcome.Existing)
        {
            return Results.Ok(new
            {
                deleted = reservation.Receipt!.Status == AiOperationStatuses.Created,
                operation = AiPublicContractMapper.ToOperation(reservation.Receipt)
            });
        }

        var result = conversationService.DeleteOwnedSession(
            ownerHash,
            sessionId,
            expectedRevision,
            clientMutationId.ToString("D"));
        if (result.Status == ConversationSessionDeleteStatus.NotFound)
        {
            operations.MarkFailed(ownerHash, AiOperationKinds.SessionDelete, clientMutationId,
                "session_not_found", "会话不存在或当前用户无权访问。", rejected: true, sessionId: sessionId);
            return Results.NotFound(new { errorCode = "session_not_found", publicMessage = "会话不存在或当前用户无权访问。" });
        }
        if (result.Status is ConversationSessionDeleteStatus.Conflict or ConversationSessionDeleteStatus.ActiveRun)
        {
            var rejected = operations.MarkFailed(ownerHash, AiOperationKinds.SessionDelete, clientMutationId,
                result.ErrorCode, result.PublicMessage, rejected: true, sessionId: sessionId);
            return Results.Json(new
            {
                errorCode = result.ErrorCode,
                publicMessage = result.PublicMessage,
                latestSnapshot = AiPublicContractMapper.ToSnapshot(result.Snapshot),
                operation = rejected == null ? null : AiPublicContractMapper.ToOperation(rejected)
            }, statusCode: StatusCodes.Status409Conflict);
        }
        if (result.Status == ConversationSessionDeleteStatus.PersistenceFailed)
        {
            var failed = operations.MarkFailed(ownerHash, AiOperationKinds.SessionDelete, clientMutationId,
                result.ErrorCode, result.PublicMessage, sessionId: sessionId);
            return Results.Json(new
            {
                errorCode = result.ErrorCode,
                publicMessage = result.PublicMessage,
                operation = failed == null ? null : AiPublicContractMapper.ToOperation(failed)
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var receipt = operations.MarkCreated(ownerHash, AiOperationKinds.SessionDelete, clientMutationId, sessionId: sessionId);
        return Results.Ok(new
        {
            deleted = true,
            operation = receipt == null ? null : AiPublicContractMapper.ToOperation(receipt)
        });
    }

    private static IResult HandleUpdateWorkspaceSnapshot(
        string sessionId,
        VisionAgentWorkspaceSnapshotDeltaRequest request,
        HttpContext context,
        IConversationalFlowService conversationService)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(request.ClientMutationId))
        {
            return BadRequest("session_mutation_identity_required", "sessionId 和 clientMutationId 均不能为空。");
        }

        var result = conversationService.TryUpdateOwnedWorkspaceSnapshot(
            AiOwnerIdentity.Resolve(context),
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
                ResourceDecisions = request.ResourceDecisions,
                RequirementMode = request.RequirementMode,
                WorkspaceViewMode = request.WorkspaceViewMode,
                PlanAcceptedRecommendedDefaults = request.PlanAcceptedRecommendedDefaults,
                SubmittedBuildFingerprint = request.SubmittedBuildFingerprint
            });
        if (result.NotFound)
        {
            return Results.NotFound(new { errorCode = "session_not_found", publicMessage = "会话不存在或当前用户无权访问。" });
        }
        if (result.Conflict)
        {
            return Results.Json(new
            {
                errorCode = result.ErrorCode,
                publicMessage = result.PublicMessage,
                latestSnapshot = AiPublicContractMapper.ToSnapshot(result.Snapshot)
            }, statusCode: StatusCodes.Status409Conflict);
        }
        if (!result.Success)
        {
            return Results.Json(new
            {
                errorCode = "session_persistence_failed",
                publicMessage = result.PublicMessage
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            snapshot = AiPublicContractMapper.ToSnapshot(result.Snapshot)
        });
    }

    private static IResult HandleGetOperation(
        Guid clientOperationId,
        string? kind,
        HttpContext context,
        IAiOperationReceiptStore operations)
    {
        var ownerHash = AiOwnerIdentity.Resolve(context);
        AiOperationReceipt? receipt;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!AiOperationKinds.IsSupported(kind))
            {
                return BadRequest("operation_kind_invalid", "operation kind 不受支持。");
            }
            receipt = operations.Get(ownerHash, AiOperationKinds.Normalize(kind), clientOperationId);
        }
        else
        {
            var matches = operations.Find(ownerHash, clientOperationId);
            if (matches.Count > 1)
            {
                return Results.Json(new
                {
                    errorCode = "operation_kind_required",
                    publicMessage = "同一 clientOperationId 存在多个操作类型，请指定 kind。"
                }, statusCode: StatusCodes.Status409Conflict);
            }
            receipt = matches.SingleOrDefault();
        }

        return receipt == null
            ? Results.NotFound(new { errorCode = "operation_not_found", publicMessage = "操作不存在或当前用户无权访问。" })
            : Results.Ok(AiPublicContractMapper.ToOperation(receipt));
    }

    internal static IResult? BuildReservationError(AiOperationReservationResult reservation)
    {
        return reservation.Outcome switch
        {
            AiOperationReservationOutcome.IdentityConflict => Results.Json(new
            {
                errorCode = reservation.ErrorCode,
                publicMessage = reservation.PublicMessage,
                operation = reservation.Receipt == null ? null : AiPublicContractMapper.ToOperation(reservation.Receipt)
            }, statusCode: StatusCodes.Status409Conflict),
            AiOperationReservationOutcome.PersistenceFailed => Results.Json(new
            {
                errorCode = reservation.ErrorCode,
                publicMessage = reservation.PublicMessage
            }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => null
        };
    }

    internal static string ComputeFingerprint(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static IResult BadRequest(string errorCode, string publicMessage) =>
        Results.BadRequest(new { errorCode, publicMessage });
}
