using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class PlanSelectionResolver
{
    private static readonly HashSet<string> ForbiddenOperatorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator"
    };

    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public PlanSelectionResolver()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    internal PlanSelectionResolver(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

    internal BuildStepResult<PlanSelectionResolution> Resolve(
        BuildPlanLoad load,
        TemplateStrategyResolution template,
        List<string> publicWarnings)
    {
        var strategy = ResolveStrategy(load);
        var evidence = new List<string>();
        var blocking = new List<string>();
        var source = "planner_route";
        var route = load.Plan?.RecommendedRoute ?? new VisionAgentRecommendedRoute();

        if (strategy == "template")
        {
            if (template.TemplateSkeleton != null)
            {
                source = "user_template";
                route = RouteFromTemplate(template, route);
                evidence.Add("explicit_template_selection");
            }
            else if (template.RequiredTemplateMissing)
            {
                source = "user_template_missing";
                blocking.Add("resource_pending:template_resource_pending");
                evidence.Add("explicit_template_missing");
            }
        }
        else if (strategy == "deep_learning" &&
                 IsAttributeClassification(load))
        {
            source = "user_strategy";
            route = BuildRoute(
                "attribute_classification_deep_learning",
                "Attribute classification with deep learning",
                "User-selected deep learning strategy for attribute OK/NG classification.",
                ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                "planner_route");
            evidence.Add("user_selected_deep_learning");
        }
        else if (strategy == "traditional_rule" &&
                 IsAttributeClassification(load))
        {
            source = "user_strategy";
            route = BuildRoute(
                "attribute_classification_traditional_rule",
                "Attribute classification with traditional vision",
                "User-selected traditional rule strategy for attribute OK/NG classification.",
                ["ImageAcquisition", "RoiManager", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
                "planner_route");
            evidence.Add("user_selected_traditional_rule");
        }
        else if (strategy == "planner" &&
                 template.TemplateSkeleton != null &&
                 string.Equals(template.Strategy, "catalog_match", StringComparison.OrdinalIgnoreCase))
        {
            source = "catalog_template";
            route = RouteFromTemplate(template, route);
            evidence.Add("compatible_catalog_template");
        }
        else if (strategy == "planner")
        {
            evidence.Add("planner_recommended_route");
        }

        var validRoute = ValidateRoute(route, out var invalidOperators);
        if (invalidOperators.Count > 0)
        {
            publicWarnings.Add("invalid_operator_removed");
            evidence.Add("invalid_operator_removed");
        }

        var resolution = new PlanSelectionResolution(
            validRoute,
            source,
            strategy,
            blocking
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList(),
            evidence);

        return VisionAgentBuildSupport.StepResult(
            resolution,
            $"Build route selection resolved from {source}.",
            AgentRunEventStatuses.Completed,
            new
            {
                selectionSource = resolution.SelectionSource,
                strategy = resolution.Strategy,
                effectiveRouteId = resolution.EffectiveRoute.RouteId,
                effectiveOperators = resolution.EffectiveRoute.Operators,
                blockingReasons = resolution.BlockingReasons,
                evidence = resolution.Evidence,
                metadataOnly = true
            },
            warningCode: invalidOperators.Count > 0 ? "invalid_operator_removed" : string.Empty,
            repairAction: invalidOperators.Count > 0 ? "removed_invalid_operators" : string.Empty,
            applyImpact: resolution.BlockingReasons.Any(IsHardOrStrategyBlocker)
                ? "build_blocked_until_selection_fixed"
                : "editable_draft_allowed",
            deploymentImpact: resolution.BlockingReasons.Any(reason => reason.StartsWith("resource_pending:", StringComparison.OrdinalIgnoreCase))
                ? "deployment_blocked_until_resources_bound"
                : "no_deployment_blocker");
    }

    internal static bool IsHardOrStrategyBlocker(string reason)
    {
        return reason.StartsWith("hard_requirement:", StringComparison.OrdinalIgnoreCase) ||
               reason.StartsWith("strategy_confirmation:", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveStrategy(BuildPlanLoad load)
    {
        if (HasExplicitTemplateSelection(load))
        {
            return "template";
        }

        var explicitStrategyText = string.Join(' ', load.UserSelections
            .Where(item => IsStrategySelectionKey(item.Key))
            .Select(item => item.Value));
        if (ContainsAny(explicitStrategyText, "traditional", "traditional_rule", "rule_based", "threshold", "blob", "\u4f20\u7edf", "\u89c4\u5219"))
        {
            return "traditional_rule";
        }

        if (ContainsAny(explicitStrategyText, "deep_learning", "deep learning", "model", "ai", "classification", "\u6a21\u578b"))
        {
            return "deep_learning";
        }

        if (ContainsAny(explicitStrategyText, "template", "\u6a21\u677f"))
        {
            return "template";
        }

        var valueText = string.Join(' ', load.UserSelections.Select(item => item.Value));
        if (ContainsAny(valueText, "traditional_rule", "traditional", "rule_based", "threshold_rule", "threshold", "blob", "\u4f20\u7edf\u89c4\u5219"))
        {
            return "traditional_rule";
        }

        if (ContainsAny(valueText, "deep_learning", "deep learning", "model", "ai", "\u6a21\u578b"))
        {
            return "deep_learning";
        }

        if (ContainsAny(valueText, "template", "\u6a21\u677f"))
        {
            return "template";
        }

        var acceptedStrategyText = string.Join(' ', load.AcceptedDefaults.Where(IsAcceptedStrategyHint));
        if (ContainsAny(acceptedStrategyText, "traditional_rule", "traditional", "rule_based", "threshold", "blob"))
        {
            return "traditional_rule";
        }

        if (ContainsAny(acceptedStrategyText, "deep_learning", "model_strategy", "model"))
        {
            return "deep_learning";
        }

        if (ContainsAny(acceptedStrategyText, "template_strategy", "template"))
        {
            return "template";
        }

        return "planner";
    }

    private static bool IsStrategySelectionKey(string key)
    {
        var normalized = VisionAgentBuildSupport.Clean(key).ToLowerInvariant();
        return normalized.Contains("strategy", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("algorithm", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("method", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("model_or_rule", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAcceptedStrategyHint(string value)
    {
        var normalized = VisionAgentBuildSupport.Clean(value).ToLowerInvariant();
        return normalized.Contains("strategy", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("deep_learning", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("traditional_rule", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("template", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitTemplateSelection(BuildPlanLoad load)
    {
        var selection = load.TemplateSelection;
        if (selection == null)
        {
            return false;
        }

        var mode = VisionAgentBuildSupport.Clean(selection.Mode);
        return !string.IsNullOrWhiteSpace(selection.TemplateId) ||
               !string.IsNullOrWhiteSpace(selection.ScenarioKey) &&
               (mode.Contains("selected", StringComparison.OrdinalIgnoreCase) ||
                mode.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                mode.Contains("lock", StringComparison.OrdinalIgnoreCase) ||
                mode.Contains("fill", StringComparison.OrdinalIgnoreCase) ||
                mode.Contains("adapt", StringComparison.OrdinalIgnoreCase));
    }

    private VisionAgentRecommendedRoute RouteFromTemplate(
        TemplateStrategyResolution template,
        VisionAgentRecommendedRoute fallback)
    {
        var operators = ReadOperatorTypes(template.TemplateSkeleton).ToList();
        return BuildRoute(
            string.IsNullOrWhiteSpace(template.TemplateId)
                ? FirstNonEmpty(template.ScenarioKey, fallback.RouteId, "selected_template")
                : template.TemplateId,
            "Selected template route",
            "User-selected template skeleton drives the Build operator chain.",
            operators.Count == 0 ? fallback.Operators : operators,
            "use_selected_template");
    }

    private VisionAgentRecommendedRoute ValidateRoute(
        VisionAgentRecommendedRoute route,
        out List<string> invalidOperators)
    {
        invalidOperators = [];
        var allowed = _contractCatalog.OperatorTypes
            .Where(type => !ForbiddenOperatorTypes.Contains(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var operators = new List<string>();
        foreach (var requested in route.Operators.Select(VisionAgentBuildSupport.Clean))
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                continue;
            }

            var canonical = _contractCatalog.CanonicalizeOperatorType(requested);
            if (allowed.Contains(canonical))
            {
                operators.Add(canonical);
            }
            else
            {
                invalidOperators.Add(requested);
            }
        }

        return route with
        {
            Operators = operators
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList()
        };
    }

    private static VisionAgentRecommendedRoute BuildRoute(
        string routeId,
        string title,
        string summary,
        List<string> operators,
        string templateDecision)
    {
        return new VisionAgentRecommendedRoute
        {
            RouteId = routeId,
            Title = title,
            Summary = summary,
            Operators = operators,
            TemplateDecision = templateDecision
        };
    }

    private static bool IsAttributeClassification(BuildPlanLoad load)
    {
        var taskType = load.Plan?.SemanticExtraction?.TaskType ?? string.Empty;
        var text = $"{load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt} {load.Plan?.RecommendedRoute.RouteId}";
        return taskType.Equals(AiVisionTaskTypes.AttributeClassification, StringComparison.OrdinalIgnoreCase) ||
               taskType.Equals(AiVisionTaskTypes.Classification, StringComparison.OrdinalIgnoreCase) ||
               text.Contains("attribute_classification", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("classification", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static IEnumerable<string> ReadOperatorTypes(object? templateSkeleton)
    {
        var root = VisionAgentBuildSupport.ToJsonElementOrNull(templateSkeleton);
        if (root == null ||
            !VisionAgentBuildSupport.TryGetProperty(root.Value, "operators", out var operators) ||
            operators.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var op in operators.EnumerateArray())
        {
            var type = VisionAgentBuildSupport.ReadString(op, "operatorType") ??
                       VisionAgentBuildSupport.ReadString(op, "type");
            if (!string.IsNullOrWhiteSpace(type))
            {
                yield return type;
            }
        }
    }
}
