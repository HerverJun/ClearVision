using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildReadinessReviewService
{
    internal BuildStepResult<StationCompatibilityResolution> BuildStationCompatibility(
        BuildPlanLoad load,
        VisionAgentToolResult packageReadiness)
    {
        var missing = VisionAgentBuildSupport.ReadCount(packageReadiness.Data, "missingResources");
        var blocking = VisionAgentBuildSupport.ReadCount(packageReadiness.Data, "blockingIssues");
        var report = new
        {
            source = "metadata_only_station_compatibility",
            stationTouched = false,
            cameraTouched = false,
            plcTouched = false,
            compatibleForCanvasDraft = true,
            deploymentBlocked = missing > 0 || blocking > 0,
            stationBoundarySummary = load.StationBoundarySummary,
            plcOutputPolicy = load.PlcOutputPolicy,
            missingResourceCount = missing,
            blockingIssueCount = blocking,
            metadataOnly = true
        };
        return VisionAgentBuildSupport.StepResult(
            new StationCompatibilityResolution(report),
            missing > 0 || blocking > 0
                ? "工站兼容性对画布应用是仅元数据安全的；部署仍保持阻断。"
                : "工站兼容性元数据检查通过。",
            AgentRunEventStatuses.Completed,
            report,
            warningCode: missing > 0 || blocking > 0 ? "station_deployment_blocked" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: missing > 0 || blocking > 0 ? "deployment_blocked" : "deployment_ready");
    }

    internal BuildStepResult<OperatorContractResolution> BuildOperatorContractReport(
        OperatorPipelineResolution pipeline,
        VisionAgentToolResult validation)
    {
        var invalid = pipeline.InvalidOperators;
        var report = new
        {
            source = "metadata_only_operator_contract",
            operatorCount = pipeline.Steps.Count,
            invalidOperatorsRemoved = invalid,
            validationBlockingIssueCount = VisionAgentBuildSupport.ReadCount(validation.Data, "blockingIssues"),
            validationWarningCount = VisionAgentBuildSupport.ReadCount(validation.Data, "warnings"),
            catalogBacked = invalid.Count == 0,
            metadataOnly = true
        };
        return VisionAgentBuildSupport.StepResult(
            new OperatorContractResolution(report),
            invalid.Count == 0
                ? "算子契约检查使用了目录支持的算子。"
                : "算子契约检查已在草稿校验前移除非法算子。",
            AgentRunEventStatuses.Completed,
            report,
            warningCode: invalid.Count > 0 ? "operator_contract_repaired" : string.Empty,
            repairAction: invalid.Count > 0 ? "invalid_operator_removed" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: invalid.Count > 0 ? "operator_contract_repaired" : "no_deployment_blocker");
    }

    internal BuildStepResult<ReleaseReviewResolution> BuildReleaseReview(
        VisionAgentToolResult validation,
        VisionAgentToolResult dryRun,
        VisionAgentToolResult packageReadiness,
        ParameterMappingResolution parameters)
    {
        var validationBlocking = VisionAgentBuildSupport.ReadCount(validation.Data, "blockingIssues");
        var dryRunSucceeded = VisionAgentBuildSupport.ReadBool(dryRun.Data, "dryRunSucceeded") != false;
        var deploymentReady = VisionAgentBuildSupport.ReadBool(packageReadiness.Data, "readyForDeployment") == true;
        var missing = parameters.MissingResources.Count + VisionAgentBuildSupport.ReadCount(packageReadiness.Data, "missingResources");
        var report = new
        {
            source = "metadata_only_release_review",
            canvasApplyReady = validationBlocking == 0,
            runtimeDraftReady = validationBlocking == 0 && dryRunSucceeded,
            deploymentReady,
            deploymentBlocked = !deploymentReady,
            missingResourceCount = missing,
            pendingParameterGroupCount = parameters.PendingParameters.Count,
            metadataOnly = true
        };
        return VisionAgentBuildSupport.StepResult(
            new ReleaseReviewResolution(report),
            deploymentReady
                ? "发布复核标记该草稿已部署就绪。"
                : "发布复核允许画布应用，但会在待确认元数据解决前阻断部署。",
            AgentRunEventStatuses.Completed,
            report,
            warningCode: deploymentReady ? string.Empty : "deployment_not_ready",
            applyImpact: validationBlocking == 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: deploymentReady ? "deployment_ready" : "deployment_blocked");
    }
}
