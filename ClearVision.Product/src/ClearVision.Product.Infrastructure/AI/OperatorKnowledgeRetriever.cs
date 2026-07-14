using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI;

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
        var explicitlyRequestsPlatformNms = ExplicitlyRequestsPlatformNms(normalizedText);

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

            if (card.DefaultHidden)
            {
                score -= 1_000.0;
            }
            else if (!card.DefaultAiRecommendation)
            {
                score -= 25.0;
            }

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

            if (IsBoxNms(card.OperatorType) && !explicitlyRequestsPlatformNms)
                score -= 100.0;

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

        if (ShouldPreferModelEmbeddedNms(matchedScenarioKeys, normalizedText))
            prioritizedTypes.Remove(OperatorType.BoxNms.ToString());

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
            .Where(item => !item.DefaultHidden)
            .OrderByDescending(item => OperatorLifecyclePolicy.IsDefaultAiRecommendation(item.Lifecycle))
            .ThenBy(item => OperatorCategoryCatalog.GetOrder(item.CategoryId))
            .ThenBy(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var takeCount = Math.Clamp(query.TopN <= 0 ? 24 : query.TopN, 8, Math.Max(8, metadata.Count));
        var normalizedText = $"{query.Description} {query.AdditionalContext} {string.Join(" ", query.AttachmentNames ?? Array.Empty<string>())}"
            .Trim()
            .ToLowerInvariant();
        var explicitlyRequestsPlatformNms = ExplicitlyRequestsPlatformNms(normalizedText);
        var cards = metadata
            .Where(item => explicitlyRequestsPlatformNms || item.Type != OperatorType.BoxNms)
            .Take(takeCount)
            .Select(item => new OperatorKnowledgeCard
            {
                OperatorType = item.Type.ToString(),
                DisplayName = item.DisplayName,
                CategoryId = item.CategoryId.ToString(),
                CategoryOrder = OperatorCategoryCatalog.GetOrder(item.CategoryId),
                Category = item.Category,
                Lifecycle = item.Lifecycle.ToString(),
                LifecycleNote = item.LifecycleNote,
                DefaultHidden = item.DefaultHidden,
                DefaultAiRecommendation = OperatorLifecyclePolicy.IsDefaultAiRecommendation(item.Lifecycle),
                RequiresLifecycleDisclosure = OperatorLifecyclePolicy.RequiresDisclosure(item.Lifecycle),
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
                }).ToList(),
                ParameterConditions = item.ParameterConstraints.ToList(),
                OutputConditions = item.OutputAvailabilityRules.ToList(),
                ImageInputContracts = item.ImageInputContracts.ToList(),
                ResourceRequirements = item.ParameterConstraints
                    .Where(constraint => !string.IsNullOrWhiteSpace(constraint.ResourceKind))
                    .Select(constraint => new OperatorKnowledgeResourceRequirement
                    {
                        Parameter = constraint.Parameter,
                        ResourceKind = constraint.ResourceKind!,
                        ReasonCode = constraint.ReasonCode,
                        AtLeastOneGroup = constraint.AtLeastOneGroup,
                        RequiredWhen = constraint.RequiredWhen
                    }).ToList(),
                RequiredResources = item.ParameterConstraints
                    .Where(constraint => !string.IsNullOrWhiteSpace(constraint.ResourceKind))
                    .Select(constraint => $"{item.Type}.{constraint.Parameter}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                GenerationDependencies = item.GenerationDependencies.ToList()
            }).ToList();

        return new OperatorKnowledgeSlice
        {
            PrioritizedOperatorTypes = cards.Select(card => card.OperatorType).ToList(),
            Cards = cards,
            RetrievalSummary = "fallback=metadata_only"
        };
    }

    private static bool ShouldPreferModelEmbeddedNms(
        IReadOnlySet<string> matchedScenarioKeys,
        string normalizedText)
    {
        var explicitlyRequestsPlatformNms =
            normalizedText.Contains("boxnms", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("box nms", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("rawyolo", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("raw yolo", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("原始候选", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("平台侧 nms", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("平台侧nms", StringComparison.OrdinalIgnoreCase);

        if (explicitlyRequestsPlatformNms)
            return false;

        return matchedScenarioKeys.Any(key => key.Contains("wire-sequence", StringComparison.OrdinalIgnoreCase)) ||
            normalizedText.Contains("线序", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("端子", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("wire sequence", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("endtoendnms", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("onnx nms", StringComparison.OrdinalIgnoreCase);
    }

    private static OperatorType ParseType(string operatorType)
    {
        return Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsed)
            ? parsed
            : OperatorType.Comment;
    }

    private static bool IsBoxNms(string operatorType) =>
        string.Equals(operatorType, OperatorType.BoxNms.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool ExplicitlyRequestsPlatformNms(string normalizedText) =>
        normalizedText.Contains("boxnms", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("platform nms", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("platform-side nms", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("rawyolo", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("raw yolo", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("平台侧nms", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("平台侧 nms", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("平台侧候选框抑制", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("原始候选框", StringComparison.OrdinalIgnoreCase) ||
        normalizedText.Contains("原始 yolo", StringComparison.OrdinalIgnoreCase);
}
