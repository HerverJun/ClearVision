using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class ApplyGateResolver
{
    internal BuildStepResult<VisionAgentApplyGate> Build(
        VisionAgentToolResult validation,
        VisionAgentToolResult dryRun,
        VisionAgentToolResult packageReadiness,
        VisionAgentWorkflowDiff diff)
    {
        var validationBlocking = VisionAgentBuildSupport.ReadCount(validation.Data, "blockingIssues");
        var dryRunSucceeded = VisionAgentBuildSupport.ReadBool(dryRun.Data, "dryRunSucceeded") != false;
        var deploymentReady = VisionAgentBuildSupport.ReadBool(packageReadiness.Data, "readyForDeployment") == true &&
                              diff.DeploymentBlockers.Count == 0;
        var canvasReady = validationBlocking == 0;
        var runtimeReady = canvasReady && dryRunSucceeded;
        var gate = new VisionAgentApplyGate
        {
            CanvasApplyReady = canvasReady,
            RuntimeDraftReady = runtimeReady,
            DeploymentReady = deploymentReady,
            Blocked = !canvasReady,
            Status = !canvasReady ? "blocked" :
                deploymentReady ? "deployment_ready" :
                runtimeReady ? "runtime_draft_ready" : "canvas_apply_ready",
            ApplyBlockers = canvasReady ? [] : VisionAgentBuildSupport.ReadIssueCodes(validation.Data, "blockingIssues"),
            DeploymentBlockers = deploymentReady
                ? []
                : diff.DeploymentBlockers.Count > 0 ? diff.DeploymentBlockers : ["deployment_metadata_pending"],
            MetadataOnly = true
        };
        return VisionAgentBuildSupport.StepResult(
            gate,
            $"应用门禁已解析为 {DisplayGateStatus(gate.Status)}。",
            canvasReady ? AgentRunEventStatuses.Completed : AgentRunEventStatuses.Blocked,
            gate,
            warningCode: deploymentReady ? string.Empty : "deployment_not_ready",
            applyImpact: canvasReady ? "editable_draft_allowed" : "blocked",
            deploymentImpact: deploymentReady ? "deployment_ready" : "deployment_blocked");
    }

    private static string DisplayGateStatus(string status)
    {
        return status switch
        {
            "blocked" => "已阻断",
            "deployment_ready" => "部署就绪",
            "runtime_draft_ready" => "运行草稿就绪",
            "canvas_apply_ready" => "画布可应用",
            _ => status
        };
    }
}
