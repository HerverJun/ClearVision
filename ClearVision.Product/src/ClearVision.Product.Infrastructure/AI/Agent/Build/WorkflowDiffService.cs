using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class WorkflowDiffService
{
    internal BuildStepResult<VisionAgentWorkflowDiff> Build(
        BuildPlanLoad load,
        DraftWorkflowResolution draft,
        ParameterMappingResolution parameters,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness,
        IReadOnlyList<VisionAgentBuildRepairRecord> repairs)
    {
        var preserved = load.HasCurrentFlow
            ? VisionAgentBuildSupport.ReadExistingNodeIds(load.CurrentFlowSnapshot)
            : [];
        var pendingParameters = VisionAgentBuildSupport.DeduplicatePending(parameters.PendingParameters
            .Concat(VisionAgentBuildSupport.ReadPendingParameters(validation.Data))
            .Concat(VisionAgentBuildSupport.ReadPendingParameters(packageReadiness.Data)));
        var missingResources = VisionAgentBuildSupport.DeduplicateMissing(parameters.MissingResources
            .Concat(VisionAgentBuildSupport.ReadMissingResources(validation.Data))
            .Concat(VisionAgentBuildSupport.ReadMissingResources(packageReadiness.Data)));
        var diff = new VisionAgentWorkflowDiff
        {
            AddedNodes = draft.AddedNodeIds,
            ModifiedNodes = parameters.Mappings
                .Where(item => !item.Pending)
                .Select(item => $"{item.TempId}.{item.ParameterName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PreservedNodes = preserved.ToList(),
            RemovedNodes = [],
            AddedOrChangedParameters = parameters.Mappings
                .Select(item => $"{item.TempId}.{item.ParameterName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PendingParameters = pendingParameters
                .SelectMany(item => item.ParameterNames.Select(name => $"{item.OperatorId}.{name}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MissingResources = missingResources
                .Select(item => item.ResourceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ValidationFailures = VisionAgentBuildSupport.ReadIssueCodes(validation.Data, "blockingIssues"),
            AutoRepairs = repairs.Select(item => item.DiffSummary).ToList(),
            DeploymentBlockers = VisionAgentBuildSupport.ReadIssueCodes(packageReadiness.Data, "blockingIssues")
                .Concat(missingResources.Select(item => item.ResourceKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MetadataOnly = true
        };
        return VisionAgentBuildSupport.StepResult(
            diff,
            $"工作流差异已汇总：新增 {diff.AddedNodes.Count} 个节点，保留 {diff.PreservedNodes.Count} 个节点，仍有 {diff.PendingParameters.Count} 个待确认参数。",
            AgentRunEventStatuses.Completed,
            diff,
            warningCode: diff.DeploymentBlockers.Count > 0 ? "deployment_blockers_present" : string.Empty,
            applyImpact: diff.ValidationFailures.Count == 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: diff.DeploymentBlockers.Count > 0 ? "deployment_blocked" : "deployment_ready");
    }
}
