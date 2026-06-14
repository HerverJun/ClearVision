using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record VisionAgentPlanBuildReadinessResult(
    bool CanBuild,
    List<string> BlockingReasons,
    List<string> ResolvedFields,
    List<string> RemainingFields,
    List<string> Warnings);

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
        IReadOnlyDictionary<string, string>? buildDecisions = null,
        IReadOnlyList<string>? acceptedDefaults = null,
        bool acceptedRecommendedDefaults = false,
        VisionAgentPlanAnswerValidationResult? validatedAnswers = null,
        VisionAgentEffectiveRequirement? effectiveRequirement = null,
        string requirementMode = AiRequirementModes.Strict)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();
        if (plan == null)
        {
            return new VisionAgentPlanBuildReadinessResult(false, ["plan_snapshot_missing"], [], [], []);
        }

        blocking.AddRange(VisionAgentStrategyConfirmationSupport.ExtractHardBlockers(plan)
            .Where(reason => !IsDraftableImageSourceBlocker(plan, reason)));

        var maturity = effectiveRequirement?.Maturity ?? plan.RequirementMaturity;
        if (maturity is { CanPlan: false } ||
            maturity?.Maturity is AiRequirementMaturity.AbstractGoal or AiRequirementMaturity.ChatOrHelp)
        {
            blocking.AddRange((maturity.BlockingReasons.Count > 0
                    ? maturity.BlockingReasons
                    : ["requirement_not_plannable"])
                .Select(ClassifyHardRequirement));
        }

        AddValidationBlockers(validatedAnswers, blocking, warnings);
        AddHardFieldBlockers(plan, maturity, effectiveRequirement, requirementMode, blocking);
        var strategyConfirmation = VisionAgentStrategyConfirmationSupport.Resolve(
            plan,
            buildDecisions,
            acceptedRecommendedDefaults);
        blocking.AddRange(strategyConfirmation.UnresolvedBlockers);

        if (!HasSupportedRouteOrTemplate(plan, out var invalidOperators))
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
                .ToList(),
            effectiveRequirement?.ResolvedFields ?? validatedAnswers?.ResolvedFields ?? [],
            effectiveRequirement?.RemainingFields ?? [],
            warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList());
    }

    private static void AddValidationBlockers(
        VisionAgentPlanAnswerValidationResult? validatedAnswers,
        List<string> blocking,
        List<string> warnings)
    {
        if (validatedAnswers == null)
        {
            return;
        }

        if (validatedAnswers.InvalidQuestionIds.Count > 0)
        {
            blocking.Add("hard_requirement:invalid_plan_answer_question");
        }

        if (validatedAnswers.InvalidValues.Count > 0)
        {
            blocking.Add("hard_requirement:invalid_plan_answer_value");
        }

        foreach (var field in validatedAnswers.ConflictedFields)
        {
            blocking.Add($"hard_requirement:conflicted_plan_answer:{field}");
        }

        warnings.AddRange(validatedAnswers.Warnings);
    }

    private static void AddHardFieldBlockers(
        VisionAgentPlanModeResult plan,
        AiRequirementMaturityResult? maturity,
        VisionAgentEffectiveRequirement? effectiveRequirement,
        string requirementMode,
        List<string> blocking)
    {
        if (effectiveRequirement != null)
        {
            foreach (var field in effectiveRequirement.RemainingFields)
            {
                if (field.Contains("image_source", StringComparison.OrdinalIgnoreCase) &&
                    CanDraftWithPendingImageSource(plan))
                {
                    continue;
                }

                var isBlocking = requirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase)
                    ? VisionAgentPlanFieldPolicy.IsDraftBlocking(field, maturity?.TaskType, maturity)
                    : VisionAgentPlanFieldPolicy.IsStrictBlocking(field, maturity?.TaskType, maturity);
                if (isBlocking)
                {
                    blocking.Add(ClassifyHardRequirement($"{field}_missing"));
                }
            }

            return;
        }

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
                IsBlockingMissingField(plan, field)))
        {
            foreach (var field in maturity.MissingFields.Where(field =>
                         IsBlockingMissingField(plan, field)))
            {
                blocking.Add(ClassifyHardRequirement($"{field}_missing"));
            }
        }
    }

    private static bool IsBlockingMissingField(
        VisionAgentPlanModeResult plan,
        string field)
    {
        if (field.Contains("image_source", StringComparison.OrdinalIgnoreCase))
        {
            return !CanDraftWithPendingImageSource(plan);
        }

        return field.Contains("acceptance_criteria", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDraftableImageSourceBlocker(
        VisionAgentPlanModeResult plan,
        string reason)
    {
        return reason.Contains("image_source", StringComparison.OrdinalIgnoreCase) &&
               CanDraftWithPendingImageSource(plan);
    }

    private static bool CanDraftWithPendingImageSource(VisionAgentPlanModeResult plan)
    {
        return (plan.RecommendedRoute?.Operators ?? [])
            .Select(Clean)
            .Any(op => op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSupportedRouteOrTemplate(
        VisionAgentPlanModeResult plan,
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

        return false;
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
