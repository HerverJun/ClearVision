using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public static class RuntimePreviewPermissionGate
{
    public const string ConsentRequiredErrorCode = "runtime_preview_consent_required";
    public const string PermissionDeniedErrorCode = "runtime_preview_permission_denied";

    public static readonly string CaptureToolName = "capture_" + "test_frame";
    public static readonly string ReplayToolName = "replay_" + "flow_with_frame";

    public static bool IsRuntimePreviewTool(string? toolName)
    {
        return string.Equals(toolName, CaptureToolName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, ReplayToolName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasConsent(AiFlowGenerationRequest request)
    {
        return request.UseVisionAgentGenerateFlow && request.RuntimePreviewConsent;
    }

    public static bool CanRun(VisionAgentToolContext context)
    {
        return context.RuntimePreviewConsent &&
               context.AllowedPermissions.Contains(VisionAgentToolPermission.RuntimePreview);
    }

    public static VisionAgentToolResult DeniedToolResult(string toolName, VisionAgentToolContext context)
    {
        var reason = context.RuntimePreviewConsent
            ? "RuntimePreview permission is not enabled in this agent context."
            : "RuntimePreview requires explicit user consent for this request.";
        var errorCode = context.RuntimePreviewConsent
            ? PermissionDeniedErrorCode
            : ConsentRequiredErrorCode;
        return VisionAgentToolResult.Fail(errorCode, reason, new
        {
            previewReady = false,
            toolName,
            runtimePreviewConsent = context.RuntimePreviewConsent,
            warnings = new[]
            {
                new
                {
                    code = errorCode,
                    message = reason
                }
            },
            blockingIssues = Array.Empty<object>(),
            artifacts = Array.Empty<object>()
        }) with
        {
            PendingActions =
            [
                BuildConsentPendingAction(toolName, reason)
            ]
        };
    }

    public static VisionAgentPendingAction BuildConsentPendingAction(string toolName, string reason)
    {
        return new VisionAgentPendingAction
        {
            ActionType = "AuthorizeRuntimePreview",
            Title = "Authorize RuntimePreview for this request",
            Summary = reason,
            RequiresUserConfirmation = true,
            Payload = new
            {
                toolName,
                scope = RuntimePreviewConsentScopes.SingleRequest,
                runtimePreviewConsentRequired = true
            }
        };
    }

    public static object PermissionDecision(
        VisionAgentToolPermission permission,
        VisionAgentToolContext context,
        VisionAgentToolResult result)
    {
        if (permission != VisionAgentToolPermission.RuntimePreview)
        {
            return new
            {
                allowed = result.Success,
                permission = permission.ToString()
            };
        }

        return new
        {
            allowed = result.Success,
            permission = permission.ToString(),
            runtimePreviewConsent = context.RuntimePreviewConsent,
            reason = result.Success ? "runtime_preview_consent_granted" : result.ErrorCode
        };
    }
}
