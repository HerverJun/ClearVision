using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Handoff;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static partial class AiWorkspaceHandoffEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ForbiddenPropertyFragments =
    [
        "authorization", "api_key", "apikey", "token", "secret", "password", "credential",
        "systemprompt", "system_prompt", "rawprompt", "raw_prompt", "chainofthought",
        "chain_of_thought", "reasoning", "attachment", "runtimehandle", "stationcredential"
    ];

    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\|/users/|/home/|/var/|/tmp/|/mnt/|/data/|/models/|/artifacts/)")]
    private static partial Regex PrivatePathRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex IpAddressRegex();

    [GeneratedRegex(@"(?i)(?:data:[^;]+;base64,|(?<![a-z0-9+/=])[a-z0-9+/]{96,}={0,2}(?![a-z0-9+/=]))")]
    private static partial Regex Base64Regex();

    [GeneratedRegex(@"(?i)(?:plc://[^\s,;]+|(?<![a-z0-9-])(?:DB\d+\.DB[XBWD]\d+(?:\.\d+)?|M\d+(?:\.\d+)?|D\d+)(?![a-z0-9-]))")]
    private static partial Regex PlcAddressRegex();

    public static IEndpointRouteBuilder MapAiWorkspaceHandoffEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ai/handoffs", HandleCreateAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/handoffs/{artifactId}", HandleGetAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/handoffs/by-build/{buildRunId}", HandleGetByBuildAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapGet("/api/ai/handoffs/operations/{clientOperationId:guid}", HandleGetByOperation)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapPost("/api/ai/handoffs/{artifactId}/consume", HandleReserveConsumeAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapPost("/api/ai/handoffs/{artifactId}/acknowledge", HandleAcknowledgeAsync)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        app.MapPost("/api/ai/handoffs/{artifactId}/reject", HandleReject)
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);
        return app;
    }

    private static async Task<IResult> HandleCreateAsync(
        AiWorkspaceHandoffCreateRequestV1 request,
        HttpContext context,
        IConversationalFlowService conversations,
        IAgentRunEventStreamService runs,
        IAiOperationReceiptStore operations,
        IAiWorkspaceHandoffArtifactStore artifacts,
        IProjectApplicationService projects)
    {
        if (request.ClientOperationId == Guid.Empty || request.BuildClientOperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.PlanRunId) ||
            string.IsNullOrWhiteSpace(request.PlanId) || string.IsNullOrWhiteSpace(request.PlanHash) ||
            string.IsNullOrWhiteSpace(request.BuildRunId) || string.IsNullOrWhiteSpace(request.BuildIdentity) ||
            string.IsNullOrWhiteSpace(request.CandidateFlowFingerprint))
        {
            return BadRequest("handoff_identity_required", "交接必须绑定当前 Session、Plan、Build 和候选身份。");
        }

        var ownerHash = AiOwnerIdentity.Resolve(context);
        var requestFingerprint = AiSessionEndpoints.ComputeFingerprint(request with
        {
            ClientOperationId = Guid.Empty
        });
        var reservation = operations.Reserve(
            ownerHash,
            AiOperationKinds.HandoffCreate,
            request.ClientOperationId,
            requestFingerprint,
            request.SessionId,
            ToBaselineIdentity(request.ProjectBaseline));
        var reservationError = AiSessionEndpoints.BuildReservationError(reservation);
        if (reservationError != null) return reservationError;

        if (reservation.Outcome == AiOperationReservationOutcome.Existing)
        {
            var existing = !string.IsNullOrWhiteSpace(reservation.Receipt?.ArtifactId)
                ? artifacts.Get(ownerHash, reservation.Receipt.ArtifactId)
                : artifacts.FindByCreateOperation(ownerHash, request.ClientOperationId);
            if (existing != null)
            {
                operations.MarkCreated(
                    ownerHash,
                    AiOperationKinds.HandoffCreate,
                    request.ClientOperationId,
                    existing.SessionId,
                    existing.BuildRunId,
                    existing.ProjectBaseline,
                    existing.ArtifactId);
                return Results.Ok(AiPublicContractMapper.ToHandoffArtifact(existing));
            }
        }

        IResult Reject(int status, string code, string message)
        {
            operations.MarkFailed(
                ownerHash,
                AiOperationKinds.HandoffCreate,
                request.ClientOperationId,
                code,
                message,
                rejected: true,
                sessionId: request.SessionId,
                runId: request.BuildRunId,
                projectBaseline: ToBaselineIdentity(request.ProjectBaseline));
            return Results.Json(new { errorCode = code, publicMessage = message }, statusCode: status);
        }

        var session = conversations.GetOwnedSession(ownerHash, request.SessionId);
        var snapshot = session?.WorkspaceSnapshot;
        var build = snapshot?.PublicBuildResult;
        var plan = snapshot?.PendingPlanSnapshot;
        if (session == null || snapshot == null || build == null || plan == null)
        {
            return Reject(StatusCodes.Status404NotFound, "handoff_source_not_found", "当前用户没有可交接的 canonical Build。");
        }

        if (snapshot.Revision != request.ExpectedSessionRevision ||
            snapshot.AnswerRevision != request.AnswerRevision ||
            snapshot.ResourceRevision != request.ResourceRevision ||
            build.AnswerRevision != request.AnswerRevision ||
            build.ResourceRevision != request.ResourceRevision)
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_revision_conflict", "会话、参数或资源版本已更新，请重新校验 Build。");
        }

        if (!string.Equals(snapshot.LifecycleState, "build_ready", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.BuildRunStatus, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
            snapshot.BuildTerminalSequence == null ||
            !build.Validation.HandoffEligible || build.Validation.ApplyGate.Blocked ||
            !build.Validation.ApplyGate.CanvasApplyReady || !build.Validation.ApplyGate.RuntimeDraftReady)
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_apply_gate_blocked", "当前 terminal Build 未通过后端交接门禁。");
        }

        if (!IdentityMatches(request, snapshot, build, plan))
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_build_identity_conflict", "Plan、Build 或候选身份与当前 canonical Session 不一致。");
        }

        var planOperation = operations.FindByRun(ownerHash, AiOperationKinds.PlanRun, request.PlanRunId);
        var buildOperation = operations.Get(ownerHash, AiOperationKinds.BuildRun, request.BuildClientOperationId);
        if (!CreatedOperationMatches(planOperation, request.SessionId, request.PlanRunId) ||
            !CreatedOperationMatches(buildOperation, request.SessionId, request.BuildRunId))
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_operation_receipt_conflict", "Plan 或 Build operation receipt 不属于当前用户与会话。");
        }

        var replay = runs.Replay(request.BuildRunId);
        if (replay == null || !runs.IsRunOwner(request.BuildRunId, ownerHash) ||
            !string.Equals(replay.Summary.Status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
            replay.Events.Count(IsTerminalEvent) != 1 ||
            !string.Equals(replay.Events.Single(IsTerminalEvent).Status, AgentRunEventStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_terminal_build_required", "Build 没有唯一且成功的后端终态。");
        }

        var baselineValidation = await ValidateBaselineAsync(request.ProjectBaseline, snapshot.ProjectBaseline, build.ProjectBaseline, projects);
        if (!baselineValidation.Success || baselineValidation.Identity == null)
        {
            return Reject(
                baselineValidation.FailureStatusCode,
                baselineValidation.ErrorCode,
                baselineValidation.PublicMessage);
        }

        if (string.IsNullOrWhiteSpace(session.CurrentCanvasFlowJson))
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_candidate_unavailable", "canonical candidate Flow 已不可用，请重新 Build。");
        }

        OperatorFlowDto candidateFlow;
        string candidateJson;
        string candidateFingerprint;
        try
        {
            candidateFlow = JsonSerializer.Deserialize<OperatorFlowDto>(session.CurrentCanvasFlowJson, JsonOptions)
                ?? throw new JsonException("Candidate flow is null.");
            candidateJson = JsonSerializer.Serialize(candidateFlow, JsonOptions);
            candidateFingerprint = ExecutionFlowIdentity.ComputeFlowHash(candidateFlow.ToEntity());
        }
        catch (Exception)
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_candidate_decode_failed", "canonical candidate Flow 无法安全解码，请重新 Build。");
        }

        if (!SameHash(candidateFingerprint, build.CandidateFlowFingerprint) ||
            !SameHash(candidateFingerprint, request.CandidateFlowFingerprint))
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_candidate_fingerprint_conflict", "候选流程指纹复算失败，请重新 Build。");
        }

        if (!IsPublicCandidate(candidateJson, out var unsafeReason) || !build.MetadataOnly || !build.RedactionPass)
        {
            return Reject(StatusCodes.Status409Conflict, "handoff_candidate_not_public", unsafeReason);
        }

        var result = artifacts.Create(new AiWorkspaceHandoffCreateCommand
        {
            OwnerHash = ownerHash,
            ClientOperationId = request.ClientOperationId,
            SessionId = request.SessionId,
            SessionRevision = snapshot.Revision,
            PlanRunId = request.PlanRunId,
            PlanId = request.PlanId,
            PlanHash = request.PlanHash,
            BuildRunId = request.BuildRunId,
            BuildClientOperationId = request.BuildClientOperationId,
            BuildIdentity = request.BuildIdentity,
            SubmittedBuildFingerprint = build.SubmittedBuildFingerprint,
            AnswerRevision = request.AnswerRevision,
            ResourceRevision = request.ResourceRevision,
            TargetKind = baselineValidation.Identity.TargetKind,
            ProjectBaseline = baselineValidation.Identity,
            CandidateFlowJson = candidateJson,
            CandidateFlowFingerprint = candidateFingerprint,
            PublicBuild = build
        });
        if (result.Artifact == null)
        {
            var status = result.Outcome is AiWorkspaceHandoffStoreOutcome.IdentityConflict or
                AiWorkspaceHandoffStoreOutcome.CapacityExceeded
                ? StatusCodes.Status409Conflict
                : result.Outcome == AiWorkspaceHandoffStoreOutcome.PayloadTooLarge
                    ? StatusCodes.Status413PayloadTooLarge
                    : StatusCodes.Status503ServiceUnavailable;
            return Reject(status, result.ErrorCode, result.PublicMessage);
        }

        var operation = operations.MarkCreated(
            ownerHash,
            AiOperationKinds.HandoffCreate,
            request.ClientOperationId,
            request.SessionId,
            request.BuildRunId,
            baselineValidation.Identity,
            result.Artifact.ArtifactId);
        if (operation == null)
        {
            return Results.Json(new
            {
                errorCode = "handoff_create_unknown_outcome",
                publicMessage = "候选可能已创建，请按当前 Build 查询交接结果，禁止重复创建。"
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var projection = AiPublicContractMapper.ToHandoffArtifact(result.Artifact);
        return result.Outcome == AiWorkspaceHandoffStoreOutcome.Existing
            ? Results.Ok(projection)
            : Results.Created($"/api/ai/handoffs/{result.Artifact.ArtifactId}", projection);
    }

    private static async Task<IResult> HandleGetAsync(
        string artifactId,
        HttpContext context,
        IAiWorkspaceHandoffArtifactStore artifacts,
        IProjectApplicationService projects)
    {
        var artifact = artifacts.Get(AiOwnerIdentity.Resolve(context), artifactId);
        if (artifact == null) return NotFound();
        var conflict = await ValidateCurrentArtifactBaselineAsync(artifact, projects);
        return conflict ?? Results.Ok(AiPublicContractMapper.ToHandoffArtifact(artifact));
    }

    private static IResult HandleGetByOperation(
        Guid clientOperationId,
        HttpContext context,
        IAiWorkspaceHandoffArtifactStore artifacts)
    {
        var artifact = artifacts.FindByCreateOperation(AiOwnerIdentity.Resolve(context), clientOperationId);
        return artifact == null ? NotFound() : Results.Ok(AiPublicContractMapper.ToHandoffArtifact(artifact));
    }

    private static IResult HandleGetByBuildAsync(
        string buildRunId,
        HttpContext context,
        IAiWorkspaceHandoffArtifactStore artifacts)
    {
        var artifact = artifacts.FindByBuildRun(AiOwnerIdentity.Resolve(context), buildRunId);
        return artifact == null ? NotFound() : Results.Ok(AiPublicContractMapper.ToHandoffArtifact(artifact));
    }

    private static async Task<IResult> HandleReserveConsumeAsync(
        string artifactId,
        AiWorkspaceHandoffConsumeRequestV1 request,
        HttpContext context,
        IAiWorkspaceHandoffArtifactStore artifacts,
        IProjectApplicationService projects)
    {
        var owner = AiOwnerIdentity.Resolve(context);
        var artifact = artifacts.Get(owner, artifactId);
        if (artifact == null) return NotFound();
        var validation = await ValidateConsumeRequestAsync(artifact, request, projects);
        if (validation != null) return validation;
        return StoreResult(artifacts.ReserveConsume(owner, artifactId, request.ClientOperationId, request.TargetProjectId));
    }

    private static async Task<IResult> HandleAcknowledgeAsync(
        string artifactId,
        AiWorkspaceHandoffConsumeRequestV1 request,
        HttpContext context,
        IAiWorkspaceHandoffArtifactStore artifacts,
        IProjectApplicationService projects)
    {
        var owner = AiOwnerIdentity.Resolve(context);
        var artifact = artifacts.Get(owner, artifactId);
        if (artifact == null) return NotFound();
        var validation = await ValidateConsumeRequestAsync(artifact, request, projects);
        if (validation != null) return validation;
        return StoreResult(artifacts.Acknowledge(owner, artifactId, request.ClientOperationId, request.TargetProjectId));
    }

    private static IResult HandleReject(
        string artifactId,
        AiWorkspaceHandoffRejectRequestV1 request,
        HttpContext context,
        IAiWorkspaceHandoffArtifactStore artifacts) =>
        StoreResult(artifacts.Reject(
            AiOwnerIdentity.Resolve(context),
            artifactId,
            request.ClientOperationId,
            request.RejectionCode));

    private static async Task<IResult?> ValidateConsumeRequestAsync(
        AiWorkspaceHandoffArtifactV1 artifact,
        AiWorkspaceHandoffConsumeRequestV1 request,
        IProjectApplicationService projects)
    {
        if (request.ClientOperationId == Guid.Empty ||
            !SameHash(request.CandidateFlowFingerprint, artifact.CandidateFlowFingerprint))
        {
            return BadRequest("handoff_consume_identity_required", "接收操作必须绑定当前 artifact 与 candidate fingerprint。");
        }
        if (artifact.TargetKind == "new")
        {
            if (request.TargetProjectId.HasValue)
            {
                return Conflict("handoff_new_target_project_forbidden", "新工程候选在显式保存前不能伪造正式 Project id。");
            }
            return null;
        }
        if (!request.TargetProjectId.HasValue || artifact.ProjectBaseline?.ProjectId != request.TargetProjectId)
        {
            return Conflict("handoff_target_project_conflict", "工作区工程与交接基线不一致。");
        }
        return await ValidateCurrentArtifactBaselineAsync(artifact, projects);
    }

    private static async Task<IResult?> ValidateCurrentArtifactBaselineAsync(
        AiWorkspaceHandoffArtifactV1 artifact,
        IProjectApplicationService projects)
    {
        if (artifact.TargetKind != "existing") return null;
        var baseline = artifact.ProjectBaseline;
        if (baseline?.ProjectId == null) return Conflict("handoff_baseline_invalid", "交接工件缺少既有工程基线。");
        var current = await AiProjectBaselineValidator.ReadAsync(baseline.ProjectId.Value, projects);
        if (!current.Success || current.Identity == null)
        {
            return Results.Json(new
            {
                errorCode = current.ErrorCode,
                publicMessage = current.PublicMessage,
                currentBaseline = current.Identity
            }, statusCode: current.FailureStatusCode);
        }
        if (!SameBaseline(baseline, current.Identity))
        {
            return Results.Json(new
            {
                errorCode = "handoff_baseline_conflict",
                publicMessage = "工程基线已变化，请返回 AI 基于最新工程重新 Build。",
                currentBaseline = current.Identity
            }, statusCode: StatusCodes.Status409Conflict);
        }
        return null;
    }

    private static async Task<AiProjectBaselineValidation> ValidateBaselineAsync(
        AiProjectTargetRequest? requested,
        AiProjectBaselineIdentity? snapshot,
        AiProjectBaselineIdentity? build,
        IProjectApplicationService projects)
    {
        var validation = await AiProjectBaselineValidator.ValidateAsync(requested, projects);
        if (!validation.Success || validation.Identity == null) return validation;
        if (!SameBaseline(validation.Identity, snapshot) || !SameBaseline(validation.Identity, build))
        {
            return AiProjectBaselineValidation.Rejected(
                StatusCodes.Status409Conflict,
                "handoff_baseline_identity_conflict",
                "Build、Session 与当前 Project baseline 不一致，请重新 Build。",
                validation.Identity);
        }
        return validation;
    }

    private static bool IdentityMatches(
        AiWorkspaceHandoffCreateRequestV1 request,
        VisionAgentWorkspaceSnapshot snapshot,
        VisionAgentPublicBuildResultV1 build,
        VisionAgentPlanModeResult plan) =>
        string.Equals(snapshot.PlanRunId, request.PlanRunId, StringComparison.Ordinal) &&
        string.Equals(snapshot.BuildRunId, request.BuildRunId, StringComparison.Ordinal) &&
        Guid.TryParse(snapshot.BuildClientOperationId, out var operationId) &&
        operationId == request.BuildClientOperationId &&
        string.Equals(snapshot.SubmittedBuildFingerprint, build.SubmittedBuildFingerprint, StringComparison.Ordinal) &&
        string.Equals(plan.PlanId, request.PlanId, StringComparison.Ordinal) &&
        SameHash(plan.PlanHash, request.PlanHash) &&
        string.Equals(build.RunId, request.BuildRunId, StringComparison.Ordinal) &&
        build.ClientOperationId == request.BuildClientOperationId &&
        string.Equals(build.BuildIdentity, request.BuildIdentity, StringComparison.Ordinal) &&
        string.Equals(build.PlanId, request.PlanId, StringComparison.Ordinal) &&
        SameHash(build.PlanHash, request.PlanHash) &&
        SameHash(build.CandidateFlowFingerprint, request.CandidateFlowFingerprint);

    private static bool CreatedOperationMatches(AiOperationReceipt? receipt, string sessionId, string runId) =>
        receipt != null && receipt.Status == AiOperationStatuses.Created &&
        string.Equals(receipt.SessionId, sessionId, StringComparison.Ordinal) &&
        string.Equals(receipt.RunId, runId, StringComparison.Ordinal);

    private static bool IsTerminalEvent(AgentRunEvent evt) => evt.EventType is
        AgentRunEventTypes.RunCompleted or AgentRunEventTypes.RunFailed or AgentRunEventTypes.RunCancelled;

    internal static bool IsPublicCandidate(string candidateJson, out string message)
    {
        try
        {
            using var document = JsonDocument.Parse(candidateJson);
            if (!ScanPublic(document.RootElement))
            {
                message = "候选流程包含 secret、私有路径、地址、附件或非 public 状态，不能创建交接工件。";
                return false;
            }
        }
        catch (JsonException)
        {
            message = "候选流程无法通过公开字段检查。";
            return false;
        }
        message = string.Empty;
        return true;
    }

    private static bool ScanPublic(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var normalized = property.Name.Replace("-", string.Empty).Replace("_", string.Empty);
                    if (ForbiddenPropertyFragments.Any(fragment =>
                        normalized.Contains(fragment.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase)) ||
                        !ScanPublic(property.Value)) return false;
                }
                return true;
            case JsonValueKind.Array:
                return element.EnumerateArray().All(ScanPublic);
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                return !PrivatePathRegex().IsMatch(value) && !IpAddressRegex().IsMatch(value) &&
                    !Base64Regex().IsMatch(value) && !PlcAddressRegex().IsMatch(value);
            default:
                return true;
        }
    }

    private static AiProjectBaselineIdentity? ToBaselineIdentity(AiProjectTargetRequest? target)
    {
        if (target == null || string.IsNullOrWhiteSpace(target.TargetKind)) return null;
        return new AiProjectBaselineIdentity
        {
            TargetKind = target.TargetKind.Trim().ToLowerInvariant(),
            ProjectId = target.ProjectId,
            PersistenceRevision = target.PersistenceRevision,
            CanonicalFlowHash = target.CanonicalFlowHash ?? string.Empty
        };
    }

    private static bool SameBaseline(AiProjectBaselineIdentity? left, AiProjectBaselineIdentity? right)
    {
        if (left == null || right == null) return false;
        if (!string.Equals(left.TargetKind, right.TargetKind, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(left.TargetKind, "new", StringComparison.OrdinalIgnoreCase))
        {
            return left.ProjectId == null && right.ProjectId == null &&
                left.PersistenceRevision == null && right.PersistenceRevision == null &&
                string.IsNullOrWhiteSpace(left.CanonicalFlowHash) && string.IsNullOrWhiteSpace(right.CanonicalFlowHash);
        }
        return
            left.ProjectId == right.ProjectId && left.PersistenceRevision == right.PersistenceRevision &&
            SameHash(left.CanonicalFlowHash, right.CanonicalFlowHash);
    }

    private static bool SameHash(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        static string Normalize(string value)
        {
            var normalized = value.Trim();
            if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["sha256:".Length..];
            }
            return normalized.ToUpperInvariant();
        }
        return Normalize(left) == Normalize(right);
    }

    private static IResult StoreResult(AiWorkspaceHandoffStoreResult result)
    {
        if (result.Artifact != null)
        {
            return Results.Ok(AiPublicContractMapper.ToHandoffArtifact(result.Artifact));
        }
        var status = result.Outcome switch
        {
            AiWorkspaceHandoffStoreOutcome.NotFound => StatusCodes.Status404NotFound,
            AiWorkspaceHandoffStoreOutcome.Expired => StatusCodes.Status410Gone,
            AiWorkspaceHandoffStoreOutcome.IdentityConflict or AiWorkspaceHandoffStoreOutcome.InvalidState =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return Results.Json(new { errorCode = result.ErrorCode, publicMessage = result.PublicMessage }, statusCode: status);
    }

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new { errorCode = code, publicMessage = message });

    private static IResult Conflict(string code, string message) =>
        Results.Json(new { errorCode = code, publicMessage = message }, statusCode: StatusCodes.Status409Conflict);

    private static IResult NotFound() =>
        Results.NotFound(new { errorCode = "handoff_not_found", publicMessage = "交接工件不存在或当前用户无权访问。" });
}
