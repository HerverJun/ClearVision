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
        IReadOnlyDictionary<string, string>? buildDecisions,
        bool acceptedRecommendedDefaults,
        IReadOnlyList<VisionAgentPlanAnswer>? confirmedAnswers = null)
    {
        var blockers = ExtractStrategyBlockers(plan);
        var hasExplicitChoice = TryResolveExplicitChoice(buildDecisions, out var explicitStrategy);
        if (acceptedRecommendedDefaults &&
            TryResolveRecommendedChoice(plan, out var recommendedStrategy) &&
            (!hasExplicitChoice || string.Equals(explicitStrategy, recommendedStrategy, StringComparison.OrdinalIgnoreCase)))
        {
            return new VisionAgentStrategyConfirmationResolution(
                true,
                recommendedStrategy,
                AcceptedRecommendedSource,
                blockers,
                []);
        }

        if (hasExplicitChoice)
        {
            return new VisionAgentStrategyConfirmationResolution(
                true,
                explicitStrategy,
                UserSelectionSource,
                blockers,
                []);
        }

        var unresolvedBlockers = ResolveUnresolvedBlockers(plan, blockers, confirmedAnswers);
        if (unresolvedBlockers.Count == 0)
        {
            return new VisionAgentStrategyConfirmationResolution(
                true,
                "planner_route",
                blockers.Count > 0 && confirmedAnswers?.Count > 0
                    ? UserSelectionSource
                    : PlannerNoConfirmationRequiredSource,
                blockers,
                []);
        }

        return new VisionAgentStrategyConfirmationResolution(
            false,
            string.Empty,
            string.Empty,
            blockers,
            unresolvedBlockers);
    }

    public static List<string> ExtractStrategyBlockers(VisionAgentPlanModeResult? plan)
    {
        return (plan?.BlockingReasons ?? [])
            .Select(Clean)
            .Where(reason => reason.StartsWith("strategy_confirmation:", StringComparison.OrdinalIgnoreCase))
            .Where(reason => IsRealStrategyBlocker(plan, reason))
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
        var clean = Clean(value);
        return StrategyQuestionIds.Contains(clean) ||
               clean.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveExplicitChoice(
        IReadOnlyDictionary<string, string>? buildDecisions,
        out string strategy)
    {
        strategy = string.Empty;
        if (buildDecisions == null)
        {
            return false;
        }

        foreach (var item in buildDecisions)
        {
            if (!item.Key.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase) &&
                !IsStrategyQuestionId(item.Key))
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
            var field = VisionAgentPlanFieldPolicy.ResolveQuestionField(question);
            if (!IsStrategyQuestionId(question.Id) &&
                !field.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase))
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

    private static List<string> ResolveUnresolvedBlockers(
        VisionAgentPlanModeResult? plan,
        IReadOnlyList<string> blockers,
        IReadOnlyList<VisionAgentPlanAnswer>? confirmedAnswers)
    {
        if (blockers.Count == 0)
        {
            return [];
        }

        var answers = confirmedAnswers?
            .Where(answer => !string.IsNullOrWhiteSpace(answer.Value))
            .ToList() ?? [];
        if (answers.Count == 0)
        {
            return blockers.ToList();
        }

        return blockers
            .Where(blocker => !IsBlockerSatisfiedByAnswer(plan, blocker, answers))
            .ToList();
    }

    private static bool IsBlockerSatisfiedByAnswer(
        VisionAgentPlanModeResult? plan,
        string blocker,
        IReadOnlyList<VisionAgentPlanAnswer> answers)
    {
        var key = StrategyBlockerKey(blocker);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var keyField = ResolveIdentifierField(key);
        foreach (var answer in answers)
        {
            var answerQuestionId = Clean(answer.QuestionId);
            var answerField = VisionAgentPlanFieldPolicy.NormalizeField(answer.Field);
            if (answerQuestionId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(keyField) &&
                 answerField.Equals(keyField, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var question = (plan?.ClarificationQuestions ?? [])
                .FirstOrDefault(item => Clean(item.Id).Equals(answerQuestionId, StringComparison.OrdinalIgnoreCase));
            if (question == null)
            {
                continue;
            }

            var questionField = VisionAgentPlanFieldPolicy.ResolveQuestionField(question, [blocker]);
            if (Clean(question.Id).Equals(key, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(keyField) &&
                 questionField.Equals(keyField, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string StrategyBlockerKey(string blocker)
    {
        var key = Clean(blocker);
        const string prefix = "strategy_confirmation:";
        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            key = key[prefix.Length..];
        }

        const string missingSuffix = "_missing";
        if (key.EndsWith(missingSuffix, StringComparison.OrdinalIgnoreCase))
        {
            key = key[..^missingSuffix.Length];
        }

        return key;
    }

    private static bool IsRealStrategyBlocker(VisionAgentPlanModeResult? plan, string blocker)
    {
        var key = StrategyBlockerKey(blocker);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.Equals("planner_candidate_not_buildable", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsStrategyQuestionId(key))
        {
            return true;
        }

        var keyField = ResolveIdentifierField(key);
        if (!string.IsNullOrWhiteSpace(keyField))
        {
            return keyField.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase);
        }

        var question = (plan?.ClarificationQuestions ?? [])
            .FirstOrDefault(item => Clean(item.Id).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (question == null)
        {
            return true;
        }

        var questionField = VisionAgentPlanFieldPolicy.ResolveQuestionField(question);
        return string.IsNullOrWhiteSpace(questionField) ||
               questionField.Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveIdentifierField(string value)
    {
        return VisionAgentPlanFieldPolicy.ResolveQuestionField(new VisionAgentClarificationQuestion
        {
            Id = value,
            Field = value
        });
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
