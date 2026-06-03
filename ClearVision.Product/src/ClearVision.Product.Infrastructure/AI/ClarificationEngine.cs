using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI;

public interface IClarificationEngine
{
    AiRequirementBrief ApplyPolicy(
        AiRequirementBrief brief,
        GenerateFlowMode generationMode,
        string? requirementMode);
}

public sealed class ClarificationEngine : IClarificationEngine
{
    public AiRequirementBrief ApplyPolicy(
        AiRequirementBrief brief,
        GenerateFlowMode generationMode,
        string? requirementMode)
    {
        var mode = NormalizeRequirementMode(requirementMode);
        brief.RequirementMode = mode;
        brief.HasOpenQuestions = brief.MissingFacts.Count > 0 || brief.ClarificationQuestions.Count > 0;

        if (generationMode is GenerateFlowMode.Explain or GenerateFlowMode.ReviewPendingParameters)
        {
            brief.ClarificationRequired = false;
            return brief;
        }

        brief.ClarificationRequired = mode == AiRequirementModes.Strict
            ? brief.MissingFacts.Count > 0 || brief.Confidence < 0.45
            : !brief.CanGenerateDraftNow;

        if (!brief.ClarificationRequired && brief.CanGenerateDraftNow && brief.DraftRiskLevel == "high" && brief.Confidence >= 0.55)
        {
            brief.DraftRiskLevel = brief.MissingFacts.Count == 0 ? "medium" : brief.DraftRiskLevel;
        }

        return brief;
    }

    private static string NormalizeRequirementMode(string? requirementMode)
    {
        if (string.IsNullOrWhiteSpace(requirementMode))
            return AiRequirementModes.Strict;

        return requirementMode.Trim().ToLowerInvariant() switch
        {
            AiRequirementModes.Draft => AiRequirementModes.Draft,
            AiRequirementModes.Strict => AiRequirementModes.Strict,
            _ => AiRequirementModes.Strict
        };
    }
}
