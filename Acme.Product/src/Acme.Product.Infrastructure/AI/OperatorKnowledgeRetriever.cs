using Acme.Product.Core.Services;
using Acme.Product.Core.Enums;

namespace Acme.Product.Infrastructure.AI;

public interface IOperatorKnowledgeRetriever
{
    Task<OperatorKnowledgeSlice> RetrieveAsync(
        OperatorKnowledgeQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class OperatorKnowledgeRetriever : IOperatorKnowledgeRetriever
{
    private static readonly HashSet<OperatorType> CoreTypes =
    [
        OperatorType.ImageAcquisition,
        OperatorType.ResultOutput,
        OperatorType.ResultJudgment,
        OperatorType.ConditionalBranch
    ];

    private readonly IOperatorFactory _operatorFactory;
    private readonly IScenarioMatcher _scenarioMatcher;
    private readonly IOperatorKnowledgeGraphService _knowledgeGraphService;

    public OperatorKnowledgeRetriever(
        IOperatorFactory operatorFactory,
        IScenarioMatcher scenarioMatcher,
        IOperatorKnowledgeGraphService knowledgeGraphService)
    {
        _operatorFactory = operatorFactory;
        _scenarioMatcher = scenarioMatcher;
        _knowledgeGraphService = knowledgeGraphService;
    }

    public async Task<OperatorKnowledgeSlice> RetrieveAsync(
        OperatorKnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        var graph = await _knowledgeGraphService.BuildAsync(cancellationToken);
        var cards = graph.Cards;
        if (cards.Count == 0)
            return BuildFallbackSlice(query);

        var scored = cards.ToDictionary(card => card.OperatorType, _ => 0.0, StringComparer.OrdinalIgnoreCase);
        var matchedScenarioKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templateMappedOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var userText = $"{query.Description} {query.AdditionalContext} {string.Join(" ", query.AttachmentNames ?? Array.Empty<string>())}"
            .Trim();
        var normalizedText = userText.ToLowerInvariant();

        var scenarioMatches = await _scenarioMatcher.MatchAsync(
            query.Description,
            query.AdditionalContext,
            query.AttachmentNames,
            topN: 3,
            cancellationToken: cancellationToken);

        foreach (var match in scenarioMatches)
        {
            if (!string.IsNullOrWhiteSpace(match.Scenario.ScenarioKey))
                matchedScenarioKeys.Add(match.Scenario.ScenarioKey);
        }

        if (query.ScenarioHints != null)
        {
            foreach (var hint in query.ScenarioHints.Where(item => !string.IsNullOrWhiteSpace(item)))
                matchedScenarioKeys.Add(hint!);
        }

        if (matchedScenarioKeys.Count > 0)
        {
            foreach (var edge in graph.Edges.Where(item => item.RelationType.Equals("USED_IN_TEMPLATE", StringComparison.OrdinalIgnoreCase)))
            {
                if (matchedScenarioKeys.Any(scenario => scenario.Equals(edge.Target, StringComparison.OrdinalIgnoreCase) ||
                                                       scenario.Contains(edge.Target, StringComparison.OrdinalIgnoreCase) ||
                                                       edge.Target.Contains(scenario, StringComparison.OrdinalIgnoreCase)))
                {
                    templateMappedOperators.Add(edge.Source);
                }
            }
        }

        foreach (var card in cards)
        {
            var score = 0.0;

            if (CoreTypes.Contains(ParseType(card.OperatorType)))
                score += 1.0;

            if (matchedScenarioKeys.Count > 0 &&
                card.ScenarioTags.Any(tag => matchedScenarioKeys.Any(scenario => scenario.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
                                                                            tag.Contains(scenario, StringComparison.OrdinalIgnoreCase))))
            {
                score += 6.0;
            }

            if (templateMappedOperators.Contains(card.OperatorType))
            {
                score += 7.0;
            }

            if (!string.IsNullOrWhiteSpace(normalizedText))
            {
                score += card.Aliases.Count(alias => normalizedText.Contains(alias, StringComparison.OrdinalIgnoreCase)) * 3.0;
                score += card.IntentTags.Count(intent => normalizedText.Contains(intent, StringComparison.OrdinalIgnoreCase)) * 2.0;
                score += card.ScenarioTags.Count(tag => normalizedText.Contains(tag, StringComparison.OrdinalIgnoreCase)) * 2.0;
                score += card.RequiredResources.Count(resource => normalizedText.Contains(resource, StringComparison.OrdinalIgnoreCase)) * 1.0;
            }

            if (card.KnownLimitations.Any(item => item.Contains("未完成现场工业验证", StringComparison.OrdinalIgnoreCase)))
                score += 0.2;

            scored[card.OperatorType] = score;
        }

        var topN = Math.Clamp(query.TopN <= 0 ? 24 : query.TopN, 8, cards.Count);
        var prioritizedTypes = scored
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var core in CoreTypes.Select(type => type.ToString()))
            prioritizedTypes.Add(core);

        var finalCards = cards
            .Where(card => prioritizedTypes.Contains(card.OperatorType))
            .OrderBy(card => card.OperatorType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OperatorKnowledgeSlice
        {
            PrioritizedOperatorTypes = finalCards.Select(card => card.OperatorType).ToList(),
            Cards = finalCards,
            MatchedScenarioKeys = matchedScenarioKeys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            RetrievalSummary = $"operator_count={finalCards.Count}; scenario_hints={string.Join(",", matchedScenarioKeys)}"
        };
    }

    private OperatorKnowledgeSlice BuildFallbackSlice(OperatorKnowledgeQuery query)
    {
        var metadata = _operatorFactory.GetAllMetadata()
            .OrderBy(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var takeCount = Math.Clamp(query.TopN <= 0 ? 24 : query.TopN, 8, Math.Max(8, metadata.Count));
        var cards = metadata.Take(takeCount).Select(item => new OperatorKnowledgeCard
        {
            OperatorType = item.Type.ToString(),
            DisplayName = item.DisplayName,
            Category = item.Category,
            Aliases = [item.DisplayName, item.Type.ToString()],
            Inputs = item.InputPorts.Select(port => new OperatorKnowledgePort
            {
                Name = port.Name,
                DisplayName = port.DisplayName,
                DataType = port.DataType.ToString(),
                IsRequired = port.IsRequired,
                Description = port.Description
            }).ToList(),
            Outputs = item.OutputPorts.Select(port => new OperatorKnowledgePort
            {
                Name = port.Name,
                DisplayName = port.DisplayName,
                DataType = port.DataType.ToString(),
                IsRequired = port.IsRequired,
                Description = port.Description
            }).ToList(),
            Parameters = item.Parameters.Select(parameter => new OperatorKnowledgeParameter
            {
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                DataType = parameter.DataType,
                Description = parameter.Description,
                DefaultValue = parameter.DefaultValue?.ToString(),
                MinValue = parameter.MinValue?.ToString(),
                MaxValue = parameter.MaxValue?.ToString(),
                IsRequired = parameter.IsRequired
            }).ToList()
        }).ToList();

        return new OperatorKnowledgeSlice
        {
            PrioritizedOperatorTypes = cards.Select(card => card.OperatorType).ToList(),
            Cards = cards,
            RetrievalSummary = "fallback=metadata_only"
        };
    }

    private static OperatorType ParseType(string operatorType)
    {
        return Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsed)
            ? parsed
            : OperatorType.Comment;
    }
}
