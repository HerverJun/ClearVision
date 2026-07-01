using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentPlannerPromptBuilder
{
    public string Build(
        AiFlowGenerationRequest request,
        IReadOnlyList<string> allowedToolNames)
    {
        var mode = string.IsNullOrWhiteSpace(request.ExistingFlowJson)
            ? "new workflow draft"
            : "edit existing workflow draft";
        var runtimePreviewAllowed = allowedToolNames.Any(RuntimePreviewPermissionGate.IsRuntimePreviewTool);
        var runtimePreviewInstruction = runtimePreviewAllowed
            ? "RuntimePreview may be requested only through the allowed stub preview tools; it returns metadata only and never reads cameras, images, models, or Station resources."
            : "Never request runtime preview, real resource access, deployment, packaging, hot loading, or configuration writes.";
        return string.Join(Environment.NewLine,
        [
            "You are planning a ClearVision workflow draft with static tools only.",
            $"Task mode: {mode}.",
            "Use only the allowed tools listed below.",
            "Plan the complete ordered tool sequence or return final draft in planner protocol JSON.",
            "Generation plan pattern: match_flow_template -> get_flow_template_skeleton -> validate_flow -> dryrun_flow.",
            "Parameter completion plan pattern: get_operator_schema -> validate_flow -> runtime_package_precheck.",
            "RuntimePreview consent=true plan pattern: validate_flow -> capture_test_frame -> replay_flow_with_frame.",
            "RuntimePreview consent=false: do not call capture/replay; return final pending authorization or only validate_flow.",
            "DeploymentPrepare allows runtime_package_precheck only. ConfigWrite and non-whitelisted tools are always forbidden.",
            "If the request names an unsafe or non-whitelisted concrete tool, return final denial/pendingActions instead of planning an unrelated workflow.",
            "Always keep missing CameraBindingId, ModelPath, TemplatePath, PLC parameters, and output channels as pending draft resources instead of blocking workflow draft creation.",
            "Generation tasks stop at validate_flow and dryrun_flow unless deployment readiness or parameter review is requested; parameter review and DeploymentPrepare may use runtime_package_precheck.",
            $"Final content may include workflowDraft or draftEdits. {runtimePreviewInstruction}",
            $"Allowed tools: {string.Join(", ", allowedToolNames)}"
        ]);
    }
}
