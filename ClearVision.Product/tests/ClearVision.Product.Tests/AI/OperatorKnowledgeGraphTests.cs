using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public class OperatorKnowledgeGraphTests
{
    [Fact(DisplayName = "OperatorKnowledgeGraph should include all operator cards and key edge types")]
    public async Task BuildAsync_ShouldCoverOperatorCardsAndEdges()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clearvision-operator-kg-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new OperatorFactory();
            var templateService = new FlowTemplateService(tempRoot);
            var graphService = new OperatorKnowledgeGraphService(factory, templateService);

            var graph = await graphService.BuildAsync();
            var metadataCount = factory.GetAllMetadata().Count();

            graph.Cards.Should().HaveCount(metadataCount);
            graph.Cards.Should().OnlyContain(card => !string.IsNullOrWhiteSpace(card.OperatorType));
            graph.Cards.Should().OnlyContain(card => !string.IsNullOrWhiteSpace(card.DisplayName));

            graph.Edges.Should().Contain(edge => edge.RelationType == "PRODUCES");
            graph.Edges.Should().Contain(edge => edge.RelationType == "CONSUMES");
            graph.Edges.Should().Contain(edge => edge.RelationType == "COMMONLY_PRECEDES");
            graph.Edges.Should().Contain(edge => edge.RelationType == "USED_IN_TEMPLATE");
            graph.Edges.Should().Contain(edge => edge.RelationType == "HAS_EVIDENCE");
            graph.Edges.Should().Contain(edge => edge.RelationType == "ALIAS_OF");
            graph.Edges.Should().Contain(edge => edge.RelationType == "REQUIRES_RESOURCE");

            var deepLearning = graph.Cards.Single(card => card.OperatorType == "DeepLearning");
            deepLearning.Aliases.Should().Contain(
                ["目标检测", "图像分类", "分类推理", "语义分割", "像素级分割"]);
            deepLearning.IntentTags.Should().Contain(
                ["object_detection", "image_classification", "semantic_segmentation"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact(DisplayName = "OperatorKnowledgeGraph should map template resources to REQUIRES_RESOURCE edges")]
    public async Task BuildAsync_ShouldIncludeTemplateRequiredResourceEdges()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clearvision-operator-kg-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new OperatorFactory();
            var templateService = new FlowTemplateService(tempRoot);
            var graphService = new OperatorKnowledgeGraphService(factory, templateService);

            var graph = await graphService.BuildAsync();

            graph.Edges.Should().Contain(edge =>
                edge.RelationType == "REQUIRES_RESOURCE" &&
                edge.Source == "DeepLearning" &&
                edge.Target == "DeepLearning.ModelPath");

            graph.Edges.Should().Contain(edge =>
                edge.RelationType == "COMMONLY_PRECEDES" &&
                edge.Source == "DeepLearning" &&
                edge.Target == "BoxFilter");

            graph.Edges.Should().Contain(edge =>
                edge.RelationType == "COMMONLY_PRECEDES" &&
                edge.Source == "EdgeDetection" &&
                edge.Target == "GapMeasurement");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact(DisplayName = "OperatorKnowledgeRetriever should prioritize wire sequence core chain")]
    public async Task RetrieveAsync_ShouldPrioritizeWireSequenceOperators()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clearvision-operator-kg-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new OperatorFactory();
            var templateService = new FlowTemplateService(tempRoot);
            var scenarioMatcher = new ScenarioMatcher(templateService);
            var graphService = new OperatorKnowledgeGraphService(factory, templateService);
            var retriever = new OperatorKnowledgeRetriever(factory, scenarioMatcher, graphService);

            var slice = await retriever.RetrieveAsync(new OperatorKnowledgeQuery
            {
                Description = "端子线序黑蓝顺序检测",
                TopN = 20
            });

            slice.PrioritizedOperatorTypes.Should().Contain("DeepLearning");
            slice.PrioritizedOperatorTypes.Should().Contain("DetectionSequenceJudge");
            slice.PrioritizedOperatorTypes.Should().Contain("ResultOutput");
            slice.PrioritizedOperatorTypes.Should().NotContain("BoxNms");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact(DisplayName = "Operator knowledge graph artifact should cover all operators and keep operatorType parseable")]
    public void Artifact_ShouldCoverAllOperators_AndOperatorTypeShouldBeParseable()
    {
        var graphPath = GetGraphArtifactPath();
        File.Exists(graphPath).Should().BeTrue(
            $"operator knowledge graph artifact is required: {graphPath}");

        var graph = LoadGraphFromFile(graphPath);
        var factory = new OperatorFactory();
        var metadata = factory.GetAllMetadata().ToList();
        var metadataTypes = metadata
            .Select(item => item.Type.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        graph.Cards.Should().NotBeEmpty();
        graph.Cards
            .Select(card => card.OperatorType)
            .Should()
            .OnlyHaveUniqueItems();

        var cardTypes = graph.Cards
            .Select(card => card.OperatorType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        cardTypes.Should().BeEquivalentTo(
            metadataTypes,
            because: "every registered operator must have exactly one knowledge card");

        graph.Cards.Should().OnlyContain(card =>
            IsOperatorTypeParseable(card.OperatorType));
    }

    [Fact(DisplayName = "Operator knowledge graph artifact should keep ports and parameters aligned with metadata")]
    public void Artifact_ShouldAlignPortsAndParametersWithOperatorMetadata()
    {
        var graph = LoadGraphFromFile(GetGraphArtifactPath());
        var factory = new OperatorFactory();
        var metadataByType = factory.GetAllMetadata()
            .ToDictionary(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase);

        foreach (var card in graph.Cards)
        {
            metadataByType.TryGetValue(card.OperatorType, out var meta)
                .Should().BeTrue($"metadata should exist for card {card.OperatorType}");
            var metadata = meta!;

            var cardInputNames = card.Inputs
                .Select(port => port.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var metadataInputNames = metadata.InputPorts
                .Select(port => port.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            cardInputNames.Should().BeEquivalentTo(
                metadataInputNames,
                $"inputs should align for {card.OperatorType}");

            var cardOutputNames = card.Outputs
                .Select(port => port.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var metadataOutputNames = metadata.OutputPorts
                .Select(port => port.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            cardOutputNames.Should().BeEquivalentTo(
                metadataOutputNames,
                $"outputs should align for {card.OperatorType}");

            var cardParameterNames = card.Parameters
                .Select(parameter => parameter.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var metadataParameterNames = metadata.Parameters
                .Select(parameter => parameter.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            cardParameterNames.Should().BeEquivalentTo(
                metadataParameterNames,
                $"parameters should align for {card.OperatorType}");
        }
    }

    private static OperatorKnowledgeGraph LoadGraphFromFile(string graphPath)
    {
        var json = File.ReadAllText(graphPath);
        var graph = JsonSerializer.Deserialize<OperatorKnowledgeGraph>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        graph.Should().NotBeNull();
        return graph!;
    }

    private static string GetGraphArtifactPath()
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        return Path.Combine(
            workspaceRoot,
            "docs",
            "ai",
            "operator-knowledge",
            "operator_knowledge_graph.json");
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")))
                return current.FullName;

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool IsOperatorTypeParseable(string? operatorType)
    {
        if (string.IsNullOrWhiteSpace(operatorType))
            return false;

        return Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsed)
               && Enum.IsDefined(parsed);
    }
}
