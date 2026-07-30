using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class AiSessionEndpoints
{
    private static readonly AgentRunEventRedactor PublicRedactor = new();

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
        app.MapGet("/api/ai/projects/{projectId:guid}/baseline", HandleGetProjectBaselineAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/resource-candidates/camera-bindings", HandleGetCameraBindingCandidates)
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
        IConversationalFlowService conversationService,
        ICameraManager cameraManager)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(request.ClientMutationId))
        {
            return BadRequest("session_mutation_identity_required", "sessionId 和 clientMutationId 均不能为空。");
        }

        var current = conversationService.GetOwnedSession(AiOwnerIdentity.Resolve(context), sessionId);
        if (current == null)
        {
            return Results.NotFound(new { errorCode = "session_not_found", publicMessage = "会话不存在或当前用户无权访问。" });
        }
        var inputValidation = ValidateBuildInputs(request);
        if (inputValidation != null) return inputValidation;
        var resourceResolution = ResolveResourceDecisions(
            current.WorkspaceSnapshot,
            request.ResourceDecisions,
            cameraManager);
        if (resourceResolution.Error != null) return resourceResolution.Error;

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
                BuildParameterValues = request.BuildParameterValues,
                ReadinessPreview = request.ReadinessPreview,
                ResourceDecisions = resourceResolution.Decisions,
                ResourceRevision = resourceResolution.ResourceRevision,
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

    private static async Task<IResult> HandleGetProjectBaselineAsync(
        Guid projectId,
        IProjectApplicationService projects)
    {
        var result = await AiProjectBaselineValidator.ReadAsync(projectId, projects);
        return result.Success && result.Identity != null
            ? Results.Ok(result.Identity)
            : Results.Json(new { errorCode = result.ErrorCode, publicMessage = result.PublicMessage },
                statusCode: result.FailureStatusCode);
    }

    private static IResult HandleGetCameraBindingCandidates(ICameraManager cameraManager)
    {
        var candidates = cameraManager.GetBindings()
            .Where(binding => VisionAgentResourceIdentity.IsSafeResourceKey(binding.Id))
            .GroupBy(binding => binding.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Select(binding => new AiCameraBindingCandidateV1(
                binding.Id.Trim(),
                PublicRedactor.RedactText(binding.DisplayName),
                binding.IsEnabled))
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCulture)
            .ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Results.Ok(candidates);
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

    private static IResult? ValidateBuildInputs(VisionAgentWorkspaceSnapshotDeltaRequest request)
    {
        if (request.ResourceRevision.HasValue)
        {
            return BadRequest("resource_revision_server_managed", "resourceRevision 只能由服务端推进。");
        }
        if (request.BuildParameterValues is { Count: > 256 })
        {
            return BadRequest("build_parameter_limit_exceeded", "一次最多确认 256 个构建参数。");
        }
        foreach (var pair in request.BuildParameterValues ?? new Dictionary<string, JsonElement>())
        {
            if (!SafeIdentity(pair.Key, 160) || pair.Value.ValueKind is not (
                    JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number or
                    JsonValueKind.True or JsonValueKind.False))
            {
                return BadRequest("build_parameter_value_invalid", "构建参数必须使用已声明参数身份和 JSON 标量值。");
            }
            if (pair.Value.ValueKind == JsonValueKind.String && (pair.Value.GetString()?.Length ?? 0) > 2048)
            {
                return BadRequest("build_parameter_value_too_long", "构建参数值过长，请缩短后重试。");
            }
        }

        return null;
    }

    private static (
        IResult? Error,
        Dictionary<string, JsonElement>? Decisions,
        int? ResourceRevision) ResolveResourceDecisions(
            VisionAgentWorkspaceSnapshot? snapshot,
            IReadOnlyList<AiResourceDecisionSelectionV1>? selections,
            ICameraManager cameraManager)
    {
        if (selections is null) return (null, null, null);
        if (snapshot is null)
        {
            return (BadRequest("resource_snapshot_missing", "当前会话没有可处理的资源快照。"), null, null);
        }
        if (selections.Count is 0 or > 64)
        {
            return (BadRequest("resource_decision_count_invalid", "每次必须提交 1 到 64 个资源身份。"), null, null);
        }

        var duplicateSelection = selections
            .GroupBy(selection => selection.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSelection != null)
        {
            return (BadRequest("resource_identity_duplicate", "同一 canonical resource identity 不能重复提交。"), null, null);
        }

        var missingGroups = snapshot.MissingResources
            .Where(resource => VisionAgentResourceIdentity.IsCanonicalId(resource.CanonicalId))
            .GroupBy(resource => resource.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var bindings = cameraManager.GetBindings();
        var decisions = snapshot.ResourceDecisions
            .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var selection in selections)
        {
            if (!VisionAgentResourceIdentity.IsCanonicalId(selection.CanonicalId) ||
                !missingGroups.TryGetValue(selection.CanonicalId, out var resources) || resources.Length != 1)
            {
                return (BadRequest("resource_identity_invalid", "资源决策必须引用当前 Build 返回的唯一 canonical resource identity。"), null, null);
            }
            var resource = resources[0];
            if (!string.Equals(resource.ResourceType, "camera_binding", StringComparison.OrdinalIgnoreCase))
            {
                return (BadRequest("resource_type_unsupported", "当前仅支持通过权威目录绑定相机资源，其他资源继续保持阻断。"), null, null);
            }
            if (!VisionAgentResourceIdentity.IsSafeResourceKey(selection.ResourceKey))
            {
                return (BadRequest("resource_key_invalid", "资源身份格式无效，不能提交路径或自由文本值。"), null, null);
            }
            var matches = bindings
                .Where(binding => string.Equals(binding.Id?.Trim(), selection.ResourceKey.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                return (BadRequest("resource_not_found", "所选相机绑定不存在或身份不唯一。"), null, null);
            }
            var binding = matches[0];
            if (!binding.IsEnabled)
            {
                return (BadRequest("resource_disabled", "所选相机绑定已禁用，不能用于当前候选。"), null, null);
            }

            var decision = new VisionAgentResourceDecision
            {
                CanonicalId = resource.CanonicalId,
                Status = VisionAgentResourceStatuses.Bound,
                ResourceKey = binding.Id.Trim(),
                ResourceType = resource.ResourceType,
                OperatorKey = resource.OperatorKey,
                OperatorId = resource.OperatorId,
                OperatorType = resource.OperatorType,
                OperatorIndex = resource.OperatorIndex,
                ParameterName = resource.ParameterName,
                ValueSummary = PublicRedactor.RedactText(binding.DisplayName),
                Source = VisionAgentResourceAuthority.CameraBindingSource
            };
            var serialized = JsonSerializer.SerializeToElement(decision, AgentRunEventJson.Options);
            if (!decisions.TryGetValue(decision.CanonicalId, out var existing) ||
                !ResourceDecisionsEqual(existing, decision))
            {
                changed = true;
            }
            decisions[decision.CanonicalId] = serialized;
        }

        if (changed && snapshot.ResourceRevision == int.MaxValue)
        {
            return (Results.Json(new
            {
                errorCode = "resource_revision_exhausted",
                publicMessage = "资源版本已达到上限，需要新建会话后继续。"
            }, statusCode: StatusCodes.Status409Conflict), null, null);
        }
        return (null, decisions, changed ? snapshot.ResourceRevision + 1 : snapshot.ResourceRevision);
    }

    private static bool ResourceDecisionsEqual(JsonElement existing, VisionAgentResourceDecision current)
    {
        if (existing.ValueKind != JsonValueKind.Object) return false;

        try
        {
            return existing.Deserialize<VisionAgentResourceDecision>(AgentRunEventJson.Options) == current;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SafeIdentity(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
            value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or '#');
    }

}
