using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IRequirementBriefExtractor
{
    AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch);
}

public sealed class RequirementBriefExtractor : IRequirementBriefExtractor
{
    public AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch)
    {
        var text = $"{description} {additionalContext}".Trim();
        var scenario = scenarioMatch?.Scenario;
        var brief = new AiRequirementBrief
        {
            ScenarioKey = scenario?.ScenarioKey ?? string.Empty,
            ScenarioName = scenario?.ScenarioName ?? string.Empty,
            IntentType = ResolveIntentType(scenario, text),
            ObjectTypes = ResolveMatches(scenario?.ObjectTypes, text),
            DefectTypes = ResolveMatches(scenario?.DefectTypes, text),
            MeasurementTargets = ResolveMatches(scenario?.MeasurementTargets, text),
            RequiredResources = scenario?.RequiredResources.ToList() ?? new List<string>()
        };

        brief.ClarificationQuestions = BuildClarificationQuestions(brief, scenarioMatch).Take(3).ToList();
        return brief;
    }

    private static string ResolveIntentType(ScenarioDefinition? scenario, string text)
    {
        if (scenario?.IntentTypes.Count > 0)
            return scenario.IntentTypes[0];

        if (ContainsAny(text, ["测量", "距离", "间距", "gap", "spacing"]))
            return "measurement";
        if (ContainsAny(text, ["漏装", "有无", "存在", "missing"]))
            return "presence_check";
        if (ContainsAny(text, ["缺陷", "划伤", "破损", "外观", "defect", "scratch"]))
            return "defect_detection";

        return string.Empty;
    }

    private static List<string> ResolveMatches(IReadOnlyList<string>? terms, string text)
    {
        if (terms == null || terms.Count == 0)
            return new List<string>();

        var matches = terms
            .Where(term => !string.IsNullOrWhiteSpace(term) &&
                           text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count > 0 ? matches : terms.Take(1).ToList();
    }

    private static IEnumerable<AiClarificationQuestion> BuildClarificationQuestions(
        AiRequirementBrief brief,
        ScenarioMatchResult? scenarioMatch)
    {
        if (string.IsNullOrWhiteSpace(brief.ScenarioKey) || (scenarioMatch?.Confidence ?? 0) < 0.45)
        {
            yield return new AiClarificationQuestion
            {
                Field = "scenario",
                Question = "请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景。",
                Required = true
            };
        }

        if (brief.DefectTypes.Count == 0 && brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "defectTypes",
                Question = "请补充需要判定的缺陷类别，例如划伤、压痕、破损、标签异常。",
                Required = true
            };
        }

        if (brief.MeasurementTargets.Count == 0 && brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "measurementTarget",
                Question = "请补充测量对象、单位和合格范围。",
                Required = true
            };
        }

        if (brief.RequiredResources.Count > 0)
        {
            yield return new AiClarificationQuestion
            {
                Field = "resources",
                Question = $"请确认资源是否已准备：{string.Join(", ", brief.RequiredResources)}。",
                Required = false
            };
        }
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
