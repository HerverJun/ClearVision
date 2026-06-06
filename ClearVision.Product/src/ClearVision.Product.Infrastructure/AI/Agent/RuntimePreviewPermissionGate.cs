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
            adapterName = "none",
            previewMode = "metadata_only",
            workflowDraftAllowed = true,
            runtimePreviewConsent = context.RuntimePreviewConsent,
            permissionDecision = new
            {
                allowed = false,
                permission = nameof(VisionAgentToolPermission.RuntimePreview),
                runtimePreviewConsent = context.RuntimePreviewConsent,
                pilotEnabled = context.RuntimePreviewPilot.Enabled,
                metadataOnly = true,
                allowlistCounts = new
                {
                    camera = context.RuntimePreviewPilot.AllowedCameraBindingIds.Count,
                    model = context.RuntimePreviewPilot.AllowedModelIds.Count,
                    template = context.RuntimePreviewPilot.AllowedTemplateIds.Count,
                    flow = context.RuntimePreviewPilot.AllowedFlowIds.Count,
                    resourceRoot = context.RuntimePreviewPilot.AllowedResourceRoots.Count
                },
                reasonCode = errorCode,
                reason
            },
            resourceTrace = new
            {
                allowed = false,
                reasonCode = errorCode,
                resourceType = "runtime_preview_permission",
                resourceId = toolName,
                normalizedKey = toolName,
                missingResources = Array.Empty<object>(),
                trace = new[]
                {
                    new
                    {
                        resourceType = "runtime_preview_permission",
                        resourceId = toolName,
                        reasonCode = errorCode,
                        allowed = false
                    }
                }
            },
            readiness = new
            {
                status = "not_ready",
                canRunMetadataPilot = false,
                workflowDraftAllowed = true,
                issues = new[]
                {
                    new
                    {
                        code = errorCode,
                        message = reason,
                        resourceType = "runtime_preview_permission"
                    }
                },
                blockingIssues = Array.Empty<object>(),
                missingResources = Array.Empty<object>(),
                unsafeFindings = Array.Empty<object>(),
                allowlistCoverage = new
                {
                    metadataOnly = true,
                    counts = new
                    {
                        camera = context.RuntimePreviewPilot.AllowedCameraBindingIds.Count,
                        model = context.RuntimePreviewPilot.AllowedModelIds.Count,
                        template = context.RuntimePreviewPilot.AllowedTemplateIds.Count,
                        flow = context.RuntimePreviewPilot.AllowedFlowIds.Count,
                        resourceRoot = context.RuntimePreviewPilot.AllowedResourceRoots.Count
                    }
                },
                resourceTrace = new
                {
                    allowed = false,
                    reasonCode = errorCode,
                    resourceType = "runtime_preview_permission",
                    resourceId = toolName,
                    normalizedKey = toolName,
                    missingResources = Array.Empty<object>(),
                    trace = new[]
                    {
                        new
                        {
                            resourceType = "runtime_preview_permission",
                            resourceId = toolName,
                            reasonCode = errorCode,
                            allowed = false
                        }
                    }
                },
                pendingActions = new[]
                {
                    BuildConsentPendingAction(toolName, reason)
                },
                binaryIncluded = false,
                capturedRealFrame = false,
                loadedModelFiles = false,
                accessedHardware = false,
                stationTouched = false
            },
            fallback = new
            {
                used = false,
                fallbackAdapterName = (string?)null,
                reasonCode = (string?)null,
                reason = (string?)null
            },
            warnings = new[]
            {
                new
                {
                    code = errorCode,
                    message = reason
                }
            },
            blockingIssues = Array.Empty<object>(),
            pendingActions = new[]
            {
                BuildConsentPendingAction(toolName, reason)
            },
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
            pilotEnabled = context.RuntimePreviewPilot.Enabled,
            metadataOnly = true,
            effectiveAdapterName = ReadAdapterName(result.Data),
            allowlistCounts = new
            {
                camera = context.RuntimePreviewPilot.AllowedCameraBindingIds.Count,
                model = context.RuntimePreviewPilot.AllowedModelIds.Count,
                template = context.RuntimePreviewPilot.AllowedTemplateIds.Count,
                flow = context.RuntimePreviewPilot.AllowedFlowIds.Count,
                resourceRoot = context.RuntimePreviewPilot.AllowedResourceRoots.Count
            },
            reason = result.Success ? "runtime_preview_consent_granted" : result.ErrorCode
        };
    }

    private static string? ReadAdapterName(object? data)
    {
        if (data is RuntimePreviewResult preview)
        {
            return preview.AdapterName;
        }

        return null;
    }
}
