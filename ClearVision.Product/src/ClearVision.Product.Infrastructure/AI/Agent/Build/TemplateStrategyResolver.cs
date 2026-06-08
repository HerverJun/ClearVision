using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class TemplateStrategyResolver
{
    private readonly BuildToolRunner _toolRunner;

    public TemplateStrategyResolver(BuildToolRunner toolRunner)
    {
        _toolRunner = toolRunner;
    }

    internal async Task<BuildStepResult<TemplateStrategyResolution>> ResolveAsync(
        string? runId,
        List<VisionAgentToolEvidence> evidence,
        BuildPlanLoad load,
        VisionAgentToolContext toolContext,
        CancellationToken cancellationToken)
    {
        var match = await _toolRunner.RunRegisteredToolAsync(
            runId,
            evidence,
            toolContext,
            "template_strategy",
            "match_flow_template",
            "Match the request against metadata-only template catalog.",
            new { request = load.OriginalUserPrompt, topN = 3 },
            cancellationToken,
            AgentRunEventTypes.ToolCallCompleted);

        var selectedTemplateId = VisionAgentBuildSupport.Clean(load.TemplateSelection?.TemplateId);
        var selectedScenario = VisionAgentBuildSupport.Clean(load.TemplateSelection?.ScenarioKey);
        var selectedMode = VisionAgentBuildSupport.Clean(load.TemplateSelection?.Mode);
        var candidate = FirstTemplateCandidate(match.Payload.Data);
        var strategy = "catalog_match";
        var templateId = selectedTemplateId;
        var scenarioKey = selectedScenario;
        if (!string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            strategy = selectedMode.Contains("adapt", StringComparison.OrdinalIgnoreCase)
                ? "adapt_selected_template"
                : "use_selected_template";
        }
        else if (candidate != null && candidate.Score >= 0.4)
        {
            templateId = candidate.TemplateId;
            scenarioKey = candidate.ScenarioKey;
        }
        else
        {
            strategy = "no_template";
        }

        VisionAgentToolResult? skeleton = null;
        if (!string.IsNullOrWhiteSpace(templateId) || !string.IsNullOrWhiteSpace(scenarioKey))
        {
            var skeletonStep = await _toolRunner.RunRegisteredToolAsync(
                runId,
                evidence,
                toolContext,
                "template_strategy",
                "get_flow_template_skeleton",
                "Load selected or matched template skeleton as read-only metadata.",
                new { templateId, scenarioKey },
                cancellationToken,
                AgentRunEventTypes.ToolCallCompleted);
            skeleton = skeletonStep.Payload;
            if (!skeleton.Success && strategy != "no_template")
            {
                strategy = "catalog_match_without_skeleton";
            }
        }

        var resolution = new TemplateStrategyResolution(
            strategy,
            templateId,
            scenarioKey,
            skeleton?.Success == true ? skeleton.Data : null,
            strategy == "no_template" ? "free_generate" :
            strategy.Contains("adapt", StringComparison.OrdinalIgnoreCase) ? "template_adapt" : "template_fill",
            strategy == "no_template" ? "none" :
            strategy.Contains("adapt", StringComparison.OrdinalIgnoreCase) ? "relaxed" : "strict");

        return VisionAgentBuildSupport.StepResult(
            resolution,
            $"Template strategy resolved as {strategy}.",
            AgentRunEventStatuses.Completed,
            new
            {
                strategy,
                templateId,
                scenarioKey,
                candidate = candidate == null
                    ? null
                    : new { candidate.TemplateId, candidate.ScenarioKey, candidate.Score },
                skeletonLoaded = skeleton?.Success == true,
                metadataOnly = true
            },
            warningCode: skeleton?.Success == false ? "template_skeleton_unavailable" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "template_resource_may_remain_pending");
    }

    private static TemplateCandidate? FirstTemplateCandidate(object? data)
    {
        var root = VisionAgentBuildSupport.ToJsonElementOrNull(data);
        if (root == null ||
            !VisionAgentBuildSupport.TryGetProperty(root.Value, "candidates", out var candidates) ||
            candidates.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in candidates.EnumerateArray())
        {
            return new TemplateCandidate(
                VisionAgentBuildSupport.ReadString(item, "templateId") ?? string.Empty,
                VisionAgentBuildSupport.ReadString(item, "scenarioKey") ?? string.Empty,
                VisionAgentBuildSupport.ReadDouble(item, "score"));
        }

        return null;
    }
}
