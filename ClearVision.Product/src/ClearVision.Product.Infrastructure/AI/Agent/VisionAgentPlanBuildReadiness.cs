using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record VisionAgentPlanBuildReadinessResult(
    bool CanBuild,
    List<string> BlockingReasons,
    List<string> ResolvedFields,
    List<string> RemainingFields,
    List<string> Warnings);

internal static class VisionAgentPlanBuildReadiness
{
    public static VisionAgentPlanBuildReadinessResult Evaluate(
        VisionAgentPlanModeResult? plan,
        IReadOnlyDictionary<string, string>? buildDecisions = null,
        IReadOnlyList<string>? acceptedDefaults = null,
        bool acceptedRecommendedDefaults = false,
        VisionAgentPlanAnswerValidationResult? validatedAnswers = null,
        VisionAgentEffectiveRequirement? effectiveRequirement = null,
        string requirementMode = AiRequirementModes.Strict)
    {
        var snapshot = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            buildDecisions,
            acceptedDefaults,
            acceptedRecommendedDefaults,
            validatedAnswers,
            effectiveRequirement,
            requirementMode);
        return new VisionAgentPlanBuildReadinessResult(
            snapshot.CanBuild,
            snapshot.Blockers
                .Where(blocker => blocker.BlocksBuild)
                .Select(blocker => blocker.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList(),
            snapshot.ResolvedFields,
            snapshot.RemainingFields,
            snapshot.Blockers
                .Where(blocker => !blocker.BlocksBuild)
                .Select(blocker => blocker.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList());
    }
}
