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
            "将请求与仅元数据模板目录进行匹配。",
            new { request = load.OriginalUserPrompt, topN = 3 },
            cancellationToken,
            AgentRunEventTypes.ToolCallCompleted);

        var selectedTemplateId = VisionAgentBuildSupport.Clean(load.TemplateSelection?.TemplateId);
        var selectedScenario = VisionAgentBuildSupport.Clean(load.TemplateSelection?.ScenarioKey);
        var selectedMode = VisionAgentBuildSupport.Clean(load.TemplateSelection?.Mode);
        var selectedTemplateRequired = !string.IsNullOrWhiteSpace(selectedTemplateId) ||
                                       !string.IsNullOrWhiteSpace(selectedScenario) &&
                                       IsRequiredTemplateMode(selectedMode);
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
                "以只读元数据方式加载已选择或匹配的模板骨架。",
                new { templateId, scenarioKey },
                cancellationToken,
                AgentRunEventTypes.ToolCallCompleted,
                result => !selectedTemplateRequired && IsTemplateNotFound(result),
                "未找到匹配模板骨架，已改用算子链生成。",
                "template_not_found");
            skeleton = skeletonStep.Payload;
            if (!skeleton.Success && strategy != "no_template")
            {
                strategy = selectedTemplateRequired
                    ? "required_template_missing"
                    : "no_template";
            }
        }

        var requiredTemplateMissing = selectedTemplateRequired && skeleton?.Success == false && IsTemplateNotFound(skeleton);
        var missingTemplateResourceKey = requiredTemplateMissing
            ? !string.IsNullOrWhiteSpace(templateId)
                ? templateId
                : scenarioKey
            : string.Empty;
        var resolution = new TemplateStrategyResolution(
            strategy,
            templateId,
            scenarioKey,
            skeleton?.Success == true ? skeleton.Data : null,
            strategy == "no_template" ? "free_generate" :
            strategy.Contains("adapt", StringComparison.OrdinalIgnoreCase) ? "template_adapt" : "template_fill",
            strategy == "no_template" ? "none" :
            strategy.Contains("adapt", StringComparison.OrdinalIgnoreCase) ? "relaxed" : "strict",
            requiredTemplateMissing,
            missingTemplateResourceKey);

        return VisionAgentBuildSupport.StepResult(
            resolution,
            $"模板策略已解析为 {DisplayTemplateStrategy(strategy)}。",
            strategy == "no_template" && skeleton?.Success == false ? AgentRunEventStatuses.Warning : AgentRunEventStatuses.Completed,
            new
            {
                strategy,
                templateId,
                scenarioKey,
                candidate = candidate == null
                    ? null
                    : new { candidate.TemplateId, candidate.ScenarioKey, candidate.Score },
                skeletonLoaded = skeleton?.Success == true,
                templateRequired = selectedTemplateRequired,
                requiredTemplateMissing,
                metadataOnly = true
            },
            warningCode: skeleton?.Success == false
                ? requiredTemplateMissing ? "required_template_missing" : "template_not_found"
                : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: requiredTemplateMissing
                ? "template_resource_pending"
                : "template_resource_may_remain_pending");
    }

    private static string DisplayTemplateStrategy(string strategy)
    {
        return strategy switch
        {
            "catalog_match" => "目录匹配",
            "adapt_selected_template" => "适配已选模板",
            "use_selected_template" => "使用已选模板",
            "catalog_match_without_skeleton" => "目录匹配但模板骨架不可用",
            "required_template_missing" => "必需模板骨架缺失",
            "no_template" => "不使用模板",
            _ => strategy
        };
    }

    private static bool IsRequiredTemplateMode(string mode)
    {
        return mode.Contains("required", StringComparison.OrdinalIgnoreCase) ||
               mode.Contains("lock", StringComparison.OrdinalIgnoreCase) ||
               mode.Contains("selected", StringComparison.OrdinalIgnoreCase) ||
               mode.Contains("fill", StringComparison.OrdinalIgnoreCase) ||
               mode.Contains("adapt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemplateNotFound(VisionAgentToolResult result)
    {
        return string.Equals(result.ErrorCode, "template_not_found", StringComparison.OrdinalIgnoreCase);
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
