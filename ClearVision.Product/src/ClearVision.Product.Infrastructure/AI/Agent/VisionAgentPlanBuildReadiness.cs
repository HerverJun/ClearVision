using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record VisionAgentPlanBuildReadinessResult(
    bool CanBuild,
    List<string> BlockingReasons);

internal static class VisionAgentPlanBuildReadiness
{
    private static readonly HashSet<string> ForbiddenOperatorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator"
    };

    public static VisionAgentPlanBuildReadinessResult Evaluate(
        VisionAgentPlanModeResult? plan,
        IReadOnlyDictionary<string, string>? userSelections = null,
        IReadOnlyList<string>? acceptedDefaults = null,
        bool acceptedRecommendedDefaults = false)
    {
        var blocking = new List<string>();
        if (plan == null)
        {
            return new VisionAgentPlanBuildReadinessResult(false, ["plan_snapshot_missing"]);
        }

        var maturity = plan.RequirementMaturity;
        if (maturity is { CanPlan: false } ||
            maturity?.Maturity is AiRequirementMaturity.AbstractGoal or AiRequirementMaturity.ChatOrHelp)
        {
            blocking.AddRange((maturity.BlockingReasons.Count > 0
                    ? maturity.BlockingReasons
                    : ["requirement_not_plannable"])
                .Select(ClassifyHardRequirement));
        }

        AddHardFieldBlockers(plan, blocking);
        if (!HasSupportedStrategy(plan, userSelections, acceptedDefaults, acceptedRecommendedDefaults, out var invalidOperators))
        {
            blocking.Add("strategy_confirmation:model_or_rule_strategy_missing");
        }

        blocking.AddRange(invalidOperators.Select(op => $"hard_requirement:invalid_operator:{op}"));
        return new VisionAgentPlanBuildReadinessResult(
            blocking.Count == 0,
            blocking
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList());
    }

    private static void AddHardFieldBlockers(
        VisionAgentPlanModeResult plan,
        List<string> blocking)
    {
        var semantic = plan.SemanticExtraction;
        if (semantic != null &&
            semantic.IsVisionRequest &&
            string.Equals(semantic.Source, VisionAgentSemanticSources.Model, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(semantic.InspectionObject))
            {
                blocking.Add("hard_requirement:inspection_object_missing");
            }

            if (string.IsNullOrWhiteSpace(NormalizeTaskType(semantic.TaskType)))
            {
                blocking.Add("hard_requirement:task_type_missing");
            }

            if (string.IsNullOrWhiteSpace(semantic.ImageSource))
            {
                blocking.Add("hard_requirement:image_source_missing");
            }

            if (string.IsNullOrWhiteSpace(semantic.OkCondition) &&
                string.IsNullOrWhiteSpace(semantic.NgCondition) &&
                string.IsNullOrWhiteSpace(semantic.OutputTarget))
            {
                blocking.Add("hard_requirement:acceptance_criteria_missing");
            }

            return;
        }

        var maturity = plan.RequirementMaturity;
        if (maturity == null)
        {
            blocking.Add("hard_requirement:requirement_maturity_missing");
            return;
        }

        foreach (var reason in maturity.BlockingReasons)
        {
            if (reason.Contains("inspection_object", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("task_type", StringComparison.OrdinalIgnoreCase))
            {
                blocking.Add(ClassifyHardRequirement(reason));
            }
        }

        if (!maturity.CanBuild &&
            maturity.MissingFields.Any(field =>
                field.Contains("image_source", StringComparison.OrdinalIgnoreCase) ||
                field.Contains("acceptance_criteria", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var field in maturity.MissingFields.Where(field =>
                         field.Contains("image_source", StringComparison.OrdinalIgnoreCase) ||
                         field.Contains("acceptance_criteria", StringComparison.OrdinalIgnoreCase)))
            {
                blocking.Add(ClassifyHardRequirement($"{field}_missing"));
            }
        }
    }

    private static bool HasSupportedStrategy(
        VisionAgentPlanModeResult plan,
        IReadOnlyDictionary<string, string>? userSelections,
        IReadOnlyList<string>? acceptedDefaults,
        bool acceptedRecommendedDefaults,
        out List<string> invalidOperators)
    {
        invalidOperators = [];
        var routeOperators = plan.RecommendedRoute?.Operators?
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];

        if (routeOperators.Count > 0)
        {
            invalidOperators = routeOperators
                .Where(op => ForbiddenOperatorTypes.Contains(op))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var allowed = new VisionAgentOperatorContractCatalog().OperatorTypes
                .Where(type => !ForbiddenOperatorTypes.Contains(type))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var supportedOperators = routeOperators
                .Where(op => allowed.Contains(op))
                .ToList();
            if (invalidOperators.Count == 0 &&
                supportedOperators.Any(op => !string.Equals(op, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)) &&
                supportedOperators.Any(op => string.Equals(op, "ResultOutput", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (plan.TemplateSelection != null &&
            (!string.IsNullOrWhiteSpace(plan.TemplateSelection.TemplateId) ||
             !string.IsNullOrWhiteSpace(plan.TemplateSelection.ScenarioKey)))
        {
            return true;
        }

        var selectionValues = userSelections?.Values ?? Array.Empty<string>();
        var defaultValues = acceptedDefaults ?? Array.Empty<string>();
        var choiceText = string.Join(' ', selectionValues)
            + " " + string.Join(' ', defaultValues);
        if (acceptedRecommendedDefaults)
        {
            choiceText += " recommended";
        }

        return ContainsAny(
            choiceText,
            "deep",
            "model",
            "rule",
            "traditional",
            "template",
            "threshold",
            "classification",
            "model_pending",
            "template_pending");
    }

    private static string NormalizeTaskType(string? taskType)
    {
        return Clean(taskType).ToLowerInvariant() switch
        {
            AiVisionTaskTypes.Unknown => string.Empty,
            AiVisionTaskTypes.AbstractGoal => string.Empty,
            "" => string.Empty,
            var value => value
        };
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string ClassifyHardRequirement(string reason)
    {
        return reason.StartsWith("hard_requirement:", StringComparison.OrdinalIgnoreCase) ||
               reason.StartsWith("strategy_confirmation:", StringComparison.OrdinalIgnoreCase) ||
               reason.StartsWith("resource_pending:", StringComparison.OrdinalIgnoreCase)
            ? reason
            : $"hard_requirement:{reason}";
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
