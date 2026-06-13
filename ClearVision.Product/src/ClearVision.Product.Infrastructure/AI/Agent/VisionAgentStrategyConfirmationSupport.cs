using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record VisionAgentStrategyConfirmationResolution(
    bool Confirmed,
    string Strategy,
    string Source,
    List<string> StrategyBlockers,
    List<string> UnresolvedBlockers);

internal static class VisionAgentStrategyConfirmationSupport
{
    public const string UserSelectionSource = "user_selection";
    public const string AcceptedRecommendedSource = "accepted_recommended";
    public const string PlannerNoConfirmationRequiredSource = "planner_no_confirmation_required";

    private static readonly HashSet<string> StrategyQuestionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "classification_strategy",
        "model_or_rule_strategy",
        "algorithm_strategy"
    };

    public static VisionAgentStrategyConfirmationResolution Resolve(
        VisionAgentPlanModeResult? plan,
        IReadOnlyDictionary<string, string>? userSelections,
        bool acceptedRecommendedDefaults)
    {
        var blockers = ExtractStrategyBlockers(plan);
        if (TryResolveExplicitChoice(userSelections, out var explicitStrategy))
        {
            return new VisionAgentStrategyConfirmationResolution(
                true,
                explicitStrategy,
                UserSelectionSource,
                blockers,
                []);
        }

        if (acceptedRecommendedDefaults &&
            TryResolveRecommendedChoice(plan, out var recommendedStrategy))
        {
            return new VisionAgentStrategyConfirmationResolution(
                true,
                recommendedStrategy,
                AcceptedRecommendedSource,
                blockers,
                []);
        }

        if (blockers.Count == 0)
        {
            return new VisionAgentStrategyConfirmationResolution(
                true,
                "planner_route",
                PlannerNoConfirmationRequiredSource,
                [],
                []);
        }

        return new VisionAgentStrategyConfirmationResolution(
            false,
            string.Empty,
            string.Empty,
            blockers,
            blockers);
    }

    public static List<string> ExtractStrategyBlockers(VisionAgentPlanModeResult? plan)
    {
        return (plan?.BlockingReasons ?? [])
            .Select(Clean)
            .Where(reason => reason.StartsWith("strategy_confirmation:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    public static List<string> ExtractHardBlockers(VisionAgentPlanModeResult? plan)
    {
        return (plan?.BlockingReasons ?? [])
            .Select(Clean)
            .Where(reason => reason.StartsWith("hard_requirement:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    public static string NormalizeChoice(string? value)
    {
        var normalized = Clean(value)
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        normalized = string.Join('_', normalized.Split('_', StringSplitOptions.RemoveEmptyEntries));

        return normalized switch
        {
            "deep_learning" or
            "deeplearning" or
            "model" or
            "ai" or
            "model_strategy" or
            "classification_model" or
            "model_classification" => "deep_learning",

            "traditional_rule" or
            "traditional" or
            "rule" or
            "rule_based" or
            "classic_rule" or
            "threshold_rule" or
            "numeric_rule" => "traditional_rule",

            "template" or
            "template_strategy" or
            "catalog_template" or
            "selected_template" => "template",

            "planner_route" or
            "planner" or
            "recommended" or
            "use_planner_route" => "planner_route",

            _ => string.Empty
        };
    }

    public static bool IsStrategyQuestionId(string? value)
    {
        return StrategyQuestionIds.Contains(Clean(value));
    }

    private static bool TryResolveExplicitChoice(
        IReadOnlyDictionary<string, string>? userSelections,
        out string strategy)
    {
        strategy = string.Empty;
        if (userSelections == null)
        {
            return false;
        }

        foreach (var item in userSelections)
        {
            if (!IsStrategyQuestionId(item.Key))
            {
                continue;
            }

            var normalized = NormalizeChoice(item.Value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                strategy = normalized;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveRecommendedChoice(
        VisionAgentPlanModeResult? plan,
        out string strategy)
    {
        strategy = string.Empty;
        foreach (var question in plan?.ClarificationQuestions ?? [])
        {
            if (!IsStrategyQuestionId(question.Id))
            {
                continue;
            }

            var recommended = question.Options.FirstOrDefault(option => option.Recommended)?.Value;
            var normalized = NormalizeChoice(recommended);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = NormalizeChoice(question.DefaultValue);
            }

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                strategy = normalized;
                return true;
            }
        }

        return false;
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
