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
        var attributeClassification = IsAttributeClassification(load);
        var templateExplicitlySelected = HasExplicitTemplateSelection(load);
        var confirmation = templateExplicitlySelected
            ? new VisionAgentStrategyConfirmationResolution(
                true,
                "template",
                VisionAgentStrategyConfirmationSupport.UserSelectionSource,
                VisionAgentStrategyConfirmationSupport.ExtractStrategyBlockers(load.Plan),
                [])
            : VisionAgentStrategyConfirmationSupport.Resolve(
                load.Plan,
                load.BuildDecisions,
                load.AcceptedRecommendedDefaults,
                load.ValidatedPlanAnswers.AcceptedAnswers);
        var strategy = ResolveStrategy(confirmation, attributeClassification, templateExplicitlySelected);
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
                 attributeClassification)
        {
            source = confirmation.Source == VisionAgentStrategyConfirmationSupport.AcceptedRecommendedSource
                ? "accepted_recommended"
                : "user_strategy";
            route = BuildRoute(
                "attribute_classification_deep_learning",
                "Attribute classification with deep learning",
                "User-selected deep learning strategy for attribute OK/NG classification.",
                ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                "planner_route");
            evidence.Add("user_selected_deep_learning");
        }
        else if (strategy == "traditional_rule" &&
                 attributeClassification)
        {
            source = confirmation.Source == VisionAgentStrategyConfirmationSupport.AcceptedRecommendedSource
                ? "accepted_recommended"
                : "user_strategy";
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

        route = EnsureTemplateStrategyRoute(load, route, evidence);
        var validRoute = ValidateRoute(route, out var invalidOperators);
        if (confirmation.UnresolvedBlockers.Count > 0)
        {
            if (RequiresStrategyConfirmation(load, validRoute, template))
            {
                blocking.AddRange(confirmation.UnresolvedBlockers);
                evidence.Add("strategy_confirmation_required");
            }
            else
            {
                evidence.Add("planner_route_used_without_strategy_confirmation");
            }
        }

        var parameterStrategy = ResolveParameterStrategy(load, validRoute);
        if (invalidOperators.Count > 0)
        {
            publicWarnings.Add("invalid_operator_removed");
            evidence.Add("invalid_operator_removed");
        }

        var resolution = new PlanSelectionResolution(
            validRoute,
            source,
            strategy,
            confirmation.Confirmed,
            confirmation.Source,
            confirmation.UnresolvedBlockers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList(),
            parameterStrategy,
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
                strategyConfirmed = resolution.StrategyConfirmed,
                strategyConfirmationSource = resolution.StrategyConfirmationSource,
                unresolvedStrategyBlockers = resolution.UnresolvedStrategyBlockers,
                effectiveRouteId = resolution.EffectiveRoute.RouteId,
                effectiveOperators = resolution.EffectiveRoute.Operators,
                parameterStrategy = resolution.ParameterStrategy,
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

    private static string ResolveStrategy(
        VisionAgentStrategyConfirmationResolution confirmation,
        bool attributeClassification,
        bool templateExplicitlySelected)
    {
        if (templateExplicitlySelected)
        {
            return "template";
        }

        if (!confirmation.Confirmed)
        {
            return "planner";
        }

        return confirmation.Strategy switch
        {
            "template" => "template",
            "deep_learning" when attributeClassification => "deep_learning",
            "traditional_rule" when attributeClassification => "traditional_rule",
            _ => "planner"
        };
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

    private static bool RequiresStrategyConfirmation(
        BuildPlanLoad load,
        VisionAgentRecommendedRoute route,
        TemplateStrategyResolution template)
    {
        return !load.RequirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) ||
               load.EffectiveRequirement.Maturity.CanPlan != true ||
               !HasSupportedPlannerRoute(route, template, load.RequirementMode);
    }

    private static bool HasSupportedPlannerRoute(
        VisionAgentRecommendedRoute route,
        TemplateStrategyResolution template,
        string requirementMode)
    {
        if (template.TemplateSkeleton != null)
        {
            return true;
        }

        var operators = route.Operators
            .Select(VisionAgentBuildSupport.Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var hasProcessingOperator = operators.Any(op =>
            !op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
            !op.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase));
        var hasTerminalOutput = operators.Any(op =>
            op.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase));
        return hasProcessingOperator &&
               (hasTerminalOutput || requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase));
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

    private static VisionAgentRecommendedRoute EnsureTemplateStrategyRoute(
        BuildPlanLoad load,
        VisionAgentRecommendedRoute route,
        List<string> evidence)
    {
        if (!HasTemplateStrategyAnswer(load) &&
            !HasTemplateLayoutIntent(load))
        {
            return route;
        }

        var operators = route.Operators
            .Select(VisionAgentBuildSupport.Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (operators.Any(op => op.Equals("TemplateMatching", StringComparison.OrdinalIgnoreCase)) ||
            !operators.Any(op => op.Equals("RoiManager", StringComparison.OrdinalIgnoreCase) ||
                                 op.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase)))
        {
            return route;
        }

        var insertAt = operators.FindIndex(op => op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase));
        operators.Insert(insertAt >= 0 ? insertAt + 1 : 0, "TemplateMatching");
        evidence.Add("template_strategy_route_augmented");
        return route with
        {
            Operators = operators,
            Summary = string.IsNullOrWhiteSpace(route.Summary)
                ? "模板定位后进入 ROI 或模型检测链。"
                : $"{route.Summary} 已按模板策略加入模板定位。"
        };
    }

    private static bool HasTemplateStrategyAnswer(BuildPlanLoad load)
    {
        if (load.BuildDecisions.TryGetValue(VisionAgentPlanAnswerFields.TemplateStrategy, out var decision) &&
            ContainsTemplateSignal(decision))
        {
            return true;
        }

        return load.ValidatedPlanAnswers.AcceptedAnswers.Any(answer =>
            answer.Field.Equals(VisionAgentPlanAnswerFields.TemplateStrategy, StringComparison.OrdinalIgnoreCase) &&
            ContainsTemplateSignal(answer.Value));
    }

    private static bool ContainsTemplateSignal(string? value)
    {
        var clean = VisionAgentBuildSupport.Clean(value);
        return clean.Contains("template", StringComparison.OrdinalIgnoreCase) ||
               clean.Contains("模板", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTemplateLayoutIntent(BuildPlanLoad load)
    {
        var text = $"{load.OriginalUserPrompt} {load.Plan?.Goal} {load.Plan?.RecommendedRoute.RouteId} {load.Plan?.RecommendedRoute.Summary}";
        return ContainsAny(text, "remote", "button", "keypad", "key press", "layout", "panel", "遥控器", "按键", "按钮", "键盘", "面板", "布局");
    }

    private static bool IsAttributeClassification(BuildPlanLoad load)
    {
        if ((load.Plan?.RecommendedRoute.Operators ?? [])
            .Any(op => op.Equals("TemplateMatching", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var taskType = FirstNonEmpty(
            EffectiveValue(load, VisionAgentPlanAnswerFields.TaskType),
            load.Plan?.SemanticExtraction?.TaskType ?? string.Empty);
        var targetAttribute = FirstNonEmpty(
            EffectiveValue(load, VisionAgentPlanAnswerFields.TargetAttribute),
            load.Plan?.SemanticExtraction?.TargetAttribute ?? string.Empty);
        var acceptance = FirstNonEmpty(
            EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria),
            load.Plan?.SemanticExtraction?.OkCondition ?? string.Empty,
            load.Plan?.SemanticExtraction?.NgCondition ?? string.Empty);
        var text = $"{targetAttribute} {acceptance} {load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt} {load.Plan?.RecommendedRoute.RouteId}";
        return taskType.Equals(AiVisionTaskTypes.AttributeClassification, StringComparison.OrdinalIgnoreCase) ||
               taskType.Equals(AiVisionTaskTypes.Classification, StringComparison.OrdinalIgnoreCase) ||
               text.Contains("attribute_classification", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("classification", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveParameterStrategy(
        BuildPlanLoad load,
        VisionAgentRecommendedRoute route)
    {
        if (!IsAttributeClassification(load))
        {
            return string.Empty;
        }

        if (route.Operators.Any(op => op.Equals("Thresholding", StringComparison.OrdinalIgnoreCase)) &&
            route.Operators.Any(op => op.Equals("BlobAnalysis", StringComparison.OrdinalIgnoreCase)))
        {
            return "traditional_numeric_rule";
        }

        if (route.Operators.Any(op => op.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase)))
        {
            return "deep_learning_classification";
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string EffectiveValue(BuildPlanLoad load, string field)
    {
        return load.EffectiveRequirement.Values.TryGetValue(field, out var value)
            ? VisionAgentBuildSupport.Clean(value)
            : string.Empty;
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
