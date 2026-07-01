using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildIntentResolver
{
    internal BuildStepResult<BuildIntentResolution> Resolve(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        BuildPlanLoad load)
    {
        var candidate = VisionAgentBuildSupport.Clean(build?.BuildIntent).ToLowerInvariant();
        if (candidate is "complete_parameters" or "complete-parameters")
        {
            candidate = "review_pending_parameters";
        }

        if (candidate is not ("new" or "modify" or "explain" or "review_pending_parameters" or "refactor"))
        {
            candidate = request.Mode switch
            {
                GenerateFlowMode.Modify => "modify",
                GenerateFlowMode.Explain => "explain",
                GenerateFlowMode.ReviewPendingParameters => "review_pending_parameters",
                GenerateFlowMode.New => "new",
                _ when load.HasCurrentFlow => "modify",
                _ => "new"
            };
        }

        if (candidate == "new" && load.HasCurrentFlow &&
            request.Mode is GenerateFlowMode.Auto or GenerateFlowMode.Modify)
        {
            candidate = "modify";
        }

        return VisionAgentBuildSupport.StepResult(
            new BuildIntentResolution(candidate),
            $"构建意图已解析为 {DisplayBuildIntent(candidate)}。",
            AgentRunEventStatuses.Completed,
            new
            {
                buildIntent = candidate,
                hasCurrentFlow = load.HasCurrentFlow,
                currentFlowPreserved = load.HasCurrentFlow && candidate != "new",
                metadataOnly = true
            },
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "no_deployment_blocker");
    }

    private static string DisplayBuildIntent(string value)
    {
        return value switch
        {
            "new" => "新建流程",
            "modify" => "修改现有流程",
            "explain" => "解释流程",
            "review_pending_parameters" => "复核待确认参数",
            "refactor" => "重构流程",
            _ => value
        };
    }
}
