using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.AspNetCore.Http;

namespace ClearVision.Product.Desktop.Endpoints;

public sealed record AiSessionCreateRequest
{
    public Guid ClientOperationId { get; init; }
    public Guid? ProjectId { get; init; }
}

public sealed record AiProjectTargetRequest
{
    public string TargetKind { get; init; } = string.Empty;
    public Guid? ProjectId { get; init; }
    public long? PersistenceRevision { get; init; }
    public string? CanonicalFlowHash { get; init; }
}

public sealed record AiSessionSummaryV1(
    string SessionId,
    string LifecycleState,
    Guid? ProjectId,
    long Revision,
    DateTime UpdatedAtUtc);

public sealed record AiSessionSnapshotV1(
    int SchemaVersion,
    long Revision,
    Guid? ProjectId,
    string LifecycleState,
    string? PlanRunId,
    string? PlanRunStatus,
    string? BuildRunId,
    string? BuildRunStatus,
    Guid? BuildClientOperationId,
    AiProjectBaselineIdentity? ProjectBaseline,
    string RequirementMode,
    IReadOnlyDictionary<string, string> PlanQuestionSelections,
    IReadOnlyList<VisionAgentPlanAnswer> ConfirmedPlanAnswers,
    IReadOnlyList<VisionAgentPlanAnswer> OptimisticPlanAnswers,
    int AnswerRevision,
    VisionAgentBuildReadinessPreviewResult? ReadinessPreview,
    bool PlanAcceptedRecommendedDefaults,
    long? PlanTerminalSequence,
    DateTime UpdatedAtUtc);

public sealed record AiSessionDetailV1(
    string SessionId,
    AiSessionSnapshotV1 Snapshot,
    DateTime UpdatedAtUtc);

public sealed record AiSessionPageV1(
    IReadOnlyList<AiSessionSummaryV1> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record AiOperationProjectionV1(
    Guid ClientOperationId,
    string Kind,
    string Status,
    string? SessionId,
    string? RunId,
    string PayloadFingerprint,
    AiProjectBaselineIdentity? ProjectBaseline,
    string? ErrorCode,
    string? PublicMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal static class AiPublicContractMapper
{
    private static readonly AgentRunEventRedactor PublicRedactor = new();

    public static AiSessionSummaryV1 ToSummary(ConversationSession session)
    {
        var snapshot = session.WorkspaceSnapshot;
        return new AiSessionSummaryV1(
            session.SessionId,
            NormalizeLifecycle(snapshot?.LifecycleState),
            ParseProjectId(snapshot?.ProjectId),
            snapshot?.Revision ?? 0,
            session.UpdatedAtUtc);
    }

    public static AiSessionDetailV1 ToDetail(ConversationSession session) =>
        new(session.SessionId, ToSnapshot(session.WorkspaceSnapshot), session.UpdatedAtUtc);

    public static AiSessionSnapshotV1 ToSnapshot(VisionAgentWorkspaceSnapshot? snapshot) =>
        new(
            Math.Max(1, snapshot?.SchemaVersion ?? 1),
            snapshot?.Revision ?? 0,
            ParseProjectId(snapshot?.ProjectId),
            NormalizeLifecycle(snapshot?.LifecycleState),
            NormalizeOptional(snapshot?.PlanRunId),
            NormalizeOptional(snapshot?.PlanRunStatus),
            NormalizeOptional(snapshot?.BuildRunId),
            NormalizeOptional(snapshot?.BuildRunStatus),
            Guid.TryParse(snapshot?.BuildClientOperationId, out var operationId) ? operationId : null,
            snapshot?.ProjectBaseline is null ? null : snapshot.ProjectBaseline with { },
            NormalizeRequirementMode(snapshot?.RequirementMode),
            ToPublicSelections(snapshot?.PlanQuestionSelections),
            ToPublicAnswers(snapshot?.ConfirmedPlanAnswers),
            ToPublicAnswers(snapshot?.OptimisticPlanAnswers),
            Math.Max(0, snapshot?.AnswerRevision ?? 0),
            ToPublicReadinessPreview(snapshot?.ReadinessPreview),
            snapshot?.PlanAcceptedRecommendedDefaults == true,
            snapshot?.PlanTerminalSequence,
            snapshot?.UpdatedAtUtc ?? DateTime.UnixEpoch);

    public static AiOperationProjectionV1 ToOperation(AiOperationReceipt receipt) =>
        new(
            receipt.ClientOperationId,
            receipt.Kind,
            receipt.Status,
            NormalizeOptional(receipt.SessionId),
            NormalizeOptional(receipt.RunId),
            receipt.PayloadFingerprint,
            receipt.ProjectBaseline is null ? null : receipt.ProjectBaseline with { },
            NormalizeOptional(receipt.PublicErrorCode),
            NormalizeOptional(receipt.PublicMessage),
            receipt.CreatedAtUtc,
            receipt.UpdatedAtUtc,
            receipt.ExpiresAtUtc);

    private static Guid? ParseProjectId(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string NormalizeLifecycle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "idle" : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequirementMode(string? value) =>
        string.Equals(value, AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
            ? AiRequirementModes.Draft
            : AiRequirementModes.Strict;

    private static IReadOnlyDictionary<string, string> ToPublicSelections(
        IReadOnlyDictionary<string, string>? selections)
    {
        if (selections == null || selections.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            if (!IsSafeCanonicalField(selection.Key)) continue;
            result[selection.Key] = PublicRedactor.RedactText(selection.Value);
        }
        return result;
    }

    private static IReadOnlyList<VisionAgentPlanAnswer> ToPublicAnswers(
        IReadOnlyList<VisionAgentPlanAnswer>? answers) =>
        answers?
            .Where(answer => IsSafeCanonicalField(answer.Field) &&
                (string.IsNullOrWhiteSpace(answer.QuestionId) || IsSafePublicIdentifier(answer.QuestionId)))
            .Select(answer => answer with { Value = PublicRedactor.RedactText(answer.Value) })
            .ToArray() ?? [];

    private static VisionAgentBuildReadinessPreviewResult? ToPublicReadinessPreview(
        VisionAgentBuildReadinessPreviewResult? preview)
    {
        if (preview == null) return null;
        var redacted = PublicRedactor.RedactObject(preview);
        return JsonSerializer.Deserialize<VisionAgentBuildReadinessPreviewResult>(
            JsonSerializer.Serialize(redacted, AgentRunEventJson.Options),
            AgentRunEventJson.Options);
    }

    private static bool IsSafeCanonicalField(string? value)
    {
        return IsSafePublicIdentifier(value, allowColon: false);
    }

    private static bool IsSafePublicIdentifier(string? value, bool allowColon = true)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        if (value.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not '.' &&
            (!allowColon || character != ':')))
        {
            return false;
        }

        var forbidden = new[]
        {
            "authorization", "api_key", "apikey", "token", "secret", "password", "credential",
            "systemprompt", "rawprompt", "chainofthought", "reasoning", "hiddenreasoning"
        };
        return !forbidden.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record AiProjectBaselineValidation(
    bool Success,
    int FailureStatusCode,
    string ErrorCode,
    string PublicMessage,
    AiProjectBaselineIdentity? Identity,
    string? CanonicalFlowJson)
{
    public static AiProjectBaselineValidation Accepted(
        AiProjectBaselineIdentity identity,
        string? canonicalFlowJson = null) =>
        new(true, 0, string.Empty, string.Empty, identity, canonicalFlowJson);

    public static AiProjectBaselineValidation Rejected(
        int statusCode,
        string errorCode,
        string publicMessage,
        AiProjectBaselineIdentity? current = null) =>
        new(false, statusCode, errorCode, publicMessage, current, null);
}

internal static class AiProjectBaselineValidator
{
    public static async Task<AiProjectBaselineValidation> ValidateAsync(
        AiProjectTargetRequest? target,
        IProjectApplicationService projects)
    {
        if (target == null)
        {
            return AiProjectBaselineValidation.Rejected(
                StatusCodes.Status400BadRequest,
                "project_target_required",
                "Build 请求必须明确 targetKind。"
            );
        }

        var targetKind = target.TargetKind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (targetKind == "new")
        {
            if (target.ProjectId.HasValue || target.PersistenceRevision.HasValue ||
                !string.IsNullOrWhiteSpace(target.CanonicalFlowHash))
            {
                return AiProjectBaselineValidation.Rejected(
                    StatusCodes.Status400BadRequest,
                    "new_target_baseline_forbidden",
                    "新工程目标不能伪造正式工程版本或流程基线。"
                );
            }

            return AiProjectBaselineValidation.Accepted(new AiProjectBaselineIdentity { TargetKind = "new" });
        }

        if (targetKind != "existing" || !target.ProjectId.HasValue || target.ProjectId == Guid.Empty ||
            !target.PersistenceRevision.HasValue || target.PersistenceRevision < 0 ||
            string.IsNullOrWhiteSpace(target.CanonicalFlowHash))
        {
            return AiProjectBaselineValidation.Rejected(
                StatusCodes.Status400BadRequest,
                "existing_project_baseline_required",
                "既有工程目标必须提供 projectId、PersistenceRevision 和 canonical flow hash。"
            );
        }

        var project = await projects.GetByIdAsync(target.ProjectId.Value);
        if (project == null)
        {
            return AiProjectBaselineValidation.Rejected(
                StatusCodes.Status404NotFound,
                "project_not_found",
                "工程不存在或当前用户无权访问。"
            );
        }

        var canonicalFlow = project.Flow ?? new OperatorFlowDto { Name = "MainFlow" };
        var canonicalHash = ExecutionFlowIdentity.ComputeFlowHash(canonicalFlow.ToEntity());
        var current = new AiProjectBaselineIdentity
        {
            TargetKind = "existing",
            ProjectId = project.Id,
            PersistenceRevision = project.PersistenceRevision,
            CanonicalFlowHash = canonicalHash
        };

        if (target.PersistenceRevision.Value != project.PersistenceRevision)
        {
            return AiProjectBaselineValidation.Rejected(
                StatusCodes.Status409Conflict,
                "project_revision_conflict",
                "工程版本已更新，请重新载入后再创建 Build。",
                current);
        }

        if (!string.Equals(NormalizeHash(target.CanonicalFlowHash), NormalizeHash(canonicalHash), StringComparison.Ordinal))
        {
            return AiProjectBaselineValidation.Rejected(
                StatusCodes.Status409Conflict,
                "canonical_flow_hash_conflict",
                "工程流程已更新，请重新载入后再创建 Build。",
                current);
        }

        return AiProjectBaselineValidation.Accepted(
            current,
            JsonSerializer.Serialize(canonicalFlow));
    }

    private static string NormalizeHash(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sha256:".Length..];
        }
        return normalized.ToUpperInvariant();
    }
}
