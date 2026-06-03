using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public class AiFlowValidatorKnowledgeGraphTests
{
    [Fact(DisplayName = "AiFlowValidator should warn when requiredResources from knowledge graph are missing")]
    public void Validate_ShouldEmitKnowledgeRequiredResourceWarning()
    {
        var knowledgeGraphPath = CreateKnowledgeGraphFile(cards: new[]
        {
            new OperatorKnowledgeCard
            {
                OperatorType = "DeepLearning",
                DisplayName = "DeepLearning",
                Category = "AI",
                RequiredResources = ["DeepLearning.ModelPath"]
            }
        });

        try
        {
            var factory = new OperatorFactory();
            var validator = new AiFlowValidator(factory, knowledgeGraphPath);
            var flow = new AiGeneratedFlowJson
            {
                Operators =
                [
                    new AiGeneratedOperator
                    {
                        TempId = "op_1",
                        OperatorType = "DeepLearning",
                        DisplayName = "Detector",
                        Parameters = new Dictionary<string, string>()
                    },
                    new AiGeneratedOperator
                    {
                        TempId = "op_2",
                        OperatorType = "ResultOutput",
                        DisplayName = "Output",
                        Parameters = new Dictionary<string, string>()
                    }
                ],
                Connections = new List<AiGeneratedConnection>()
            };

            var result = validator.Validate(flow);

            result.Diagnostics.Should().Contain(item =>
                item.Code == "knowledge_required_resource_missing" &&
                item.OperatorId == "op_1" &&
                item.ParameterName == "DeepLearning.ModelPath" &&
                item.Severity == AiValidationSeverity.Warning);
        }
        finally
        {
            if (File.Exists(knowledgeGraphPath))
                File.Delete(knowledgeGraphPath);
            var parent = Path.GetDirectoryName(knowledgeGraphPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    [Fact(DisplayName = "AiFlowValidator should warn when antiPatterns from knowledge graph are hit")]
    public void Validate_ShouldEmitKnowledgeAntiPatternWarning()
    {
        var knowledgeGraphPath = CreateKnowledgeGraphFile(cards: new[]
        {
            new OperatorKnowledgeCard
            {
                OperatorType = "DeepLearning",
                DisplayName = "DeepLearning",
                Category = "AI",
                AntiPatterns = ["ModelPath=todo"]
            }
        });

        try
        {
            var factory = new OperatorFactory();
            var validator = new AiFlowValidator(factory, knowledgeGraphPath);
            var flow = new AiGeneratedFlowJson
            {
                Operators =
                [
                    new AiGeneratedOperator
                    {
                        TempId = "op_1",
                        OperatorType = "DeepLearning",
                        DisplayName = "Detector",
                        Parameters = new Dictionary<string, string>
                        {
                            ["ModelPath"] = "todo"
                        }
                    },
                    new AiGeneratedOperator
                    {
                        TempId = "op_2",
                        OperatorType = "ResultOutput",
                        DisplayName = "Output",
                        Parameters = new Dictionary<string, string>()
                    }
                ],
                Connections = new List<AiGeneratedConnection>()
            };

            var result = validator.Validate(flow);

            result.Diagnostics.Should().Contain(item =>
                item.Code == "knowledge_anti_pattern_detected" &&
                item.OperatorId == "op_1" &&
                item.Severity == AiValidationSeverity.Warning);
        }
        finally
        {
            if (File.Exists(knowledgeGraphPath))
                File.Delete(knowledgeGraphPath);
            var parent = Path.GetDirectoryName(knowledgeGraphPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    private static string CreateKnowledgeGraphFile(IEnumerable<OperatorKnowledgeCard> cards)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "clearvision-ai-validator-kg-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var graphPath = Path.Combine(root, "operator_knowledge_graph.json");
        var graph = new OperatorKnowledgeGraph
        {
            Cards = cards.ToList()
        };
        File.WriteAllText(graphPath, JsonSerializer.Serialize(graph, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        return graphPath;
    }
}
