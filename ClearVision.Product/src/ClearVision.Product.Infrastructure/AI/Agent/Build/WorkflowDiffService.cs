using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed class WorkflowDiffService
{
    public BuildStepResult<VisionAgentWorkflowDiff> Build(
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
            PendingParameters = parameters.PendingParameters
                .SelectMany(item => item.ParameterNames.Select(name => $"{item.OperatorId}.{name}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MissingResources = parameters.MissingResources
                .Select(item => item.ResourceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ValidationFailures = VisionAgentBuildSupport.ReadIssueCodes(validation.Data, "blockingIssues"),
            AutoRepairs = repairs.Select(item => item.DiffSummary).ToList(),
            DeploymentBlockers = VisionAgentBuildSupport.ReadIssueCodes(packageReadiness.Data, "blockingIssues")
                .Concat(parameters.MissingResources.Select(item => item.ResourceKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MetadataOnly = true
        };
        return VisionAgentBuildSupport.StepResult(
            diff,
            $"Workflow diff: {diff.AddedNodes.Count} added, {diff.PreservedNodes.Count} preserved, {diff.PendingParameters.Count} pending parameter(s).",
            AgentRunEventStatuses.Completed,
            diff,
            warningCode: diff.DeploymentBlockers.Count > 0 ? "deployment_blockers_present" : string.Empty,
            applyImpact: diff.ValidationFailures.Count == 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: diff.DeploymentBlockers.Count > 0 ? "deployment_blocked" : "deployment_ready");
    }
}
