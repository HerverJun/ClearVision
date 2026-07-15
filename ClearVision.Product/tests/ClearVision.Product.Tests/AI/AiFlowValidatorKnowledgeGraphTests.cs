using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
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
                            ["ModelPath"] = "<pending-model-resource>"
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

    [Fact]
    public void Validate_ShouldCanonicalizeCameraAliasesBeforeApplyingMetadataDefaults()
    {
        var factory = new OperatorFactory();
        var validator = new AiFlowValidator(factory, operatorKnowledgeGraphPath: null);
        var flow = new AiGeneratedFlowJson
        {
            Operators =
            [
                new AiGeneratedOperator
                {
                    TempId = "op_camera",
                    OperatorType = "ImageAcquisition",
                    DisplayName = "Camera",
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["sourceType"] = "Camera",
                        ["CameraBindingId"] = "line-camera-01"
                    }
                }
            ],
            Connections = []
        };

        var result = validator.Validate(flow);

        flow.Operators[0].Parameters.Should().ContainKey("SourceType")
            .WhoseValue.Should().Be("Camera");
        flow.Operators[0].Parameters.Should().ContainKey("CameraId")
            .WhoseValue.Should().Be("line-camera-01");
        flow.Operators[0].Parameters.Should().NotContainKeys("sourceType", "CameraBindingId");
        result.Diagnostics.Should().NotContain(item =>
            item.Code == "missing_conditional_parameter" ||
            item.Code == "missing_conditional_parameter_group");
    }

    [Fact]
    public void Validate_ShouldNotTreatBusinessTodoTextAsMissingResource()
    {
        var knowledgeGraphPath = CreateKnowledgeGraphFile(cards:
        [
            new OperatorKnowledgeCard
            {
                OperatorType = "ImageAcquisition",
                DisplayName = "ImageAcquisition",
                Category = "Input",
                RequiredResources = ["ImageAcquisition.CameraId"]
            }
        ]);

        try
        {
            var validator = new AiFlowValidator(new OperatorFactory(), knowledgeGraphPath);
            var flow = new AiGeneratedFlowJson
            {
                Operators =
                [
                    new AiGeneratedOperator
                    {
                        TempId = "op_camera",
                        OperatorType = "ImageAcquisition",
                        DisplayName = "Camera",
                        Parameters = new Dictionary<string, string>
                        {
                            ["SourceType"] = "Camera",
                            ["CameraId"] = "todo-line-camera"
                        }
                    }
                ],
                Connections = []
            };

            var result = validator.Validate(flow);

            result.Diagnostics.Should().NotContain(item =>
                item.Code == "knowledge_required_resource_missing" && item.OperatorId == "op_camera");
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
