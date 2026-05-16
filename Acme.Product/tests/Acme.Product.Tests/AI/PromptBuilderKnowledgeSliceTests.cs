using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.AI;
using Acme.Product.Infrastructure.Services;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public class PromptBuilderKnowledgeSliceTests
{
    [Fact(DisplayName = "PromptBuilder should include operator knowledge slice when retriever is available")]
    public void BuildSystemPrompt_WithKnowledgeRetriever_ShouldEmbedKnowledgeSlice()
    {
        var factory = new OperatorFactory();
        var retriever = new FakeOperatorKnowledgeRetriever(new OperatorKnowledgeSlice
        {
            RetrievalSummary = "operator_count=2; scenario_hints=wire-sequence-terminal",
            MatchedScenarioKeys = ["wire-sequence-terminal"],
            Cards =
            [
                CreateValidatedCard(factory, "DeepLearning", card =>
                {
                    card.RequiredResources = ["DeepLearning.ModelPath"];
                    card.TypicalUpstream = ["ImageResize"];
                    card.TypicalDownstream = ["BoxFilter", "BoxNms"];
                    card.AntiPatterns = ["ModelPath=todo"];
                    card.KnownLimitations = ["功能可用但未完成现场工业验证"];
                    card.Evidence = new OperatorKnowledgeEvidence
                    {
                        Contract = "Yes",
                        Golden = "Yes",
                        Dataset = "Partial",
                        FieldReplay = "No",
                        PrecisionClaim = "P95",
                        IndustrialStatus = "功能可用但未完成现场工业验证",
                        QScore = "Q2"
                    };
                }),
                CreateValidatedCard(factory, "ResultOutput", card =>
                {
                    card.TypicalUpstream = ["ResultJudgment"];
                    card.Evidence = new OperatorKnowledgeEvidence
                    {
                        Contract = "Yes",
                        Golden = "Yes",
                        Dataset = "Yes",
                        FieldReplay = "Yes",
                        PrecisionClaim = "N/A",
                        IndustrialStatus = "已验证"
                    };
                })
            ]
        });

        var builder = new PromptBuilder(factory, retriever);

        var prompt = builder.BuildSystemPrompt("端子线序检测");

        prompt.Should().Contain("# Operator Knowledge Slice");
        prompt.Should().Contain("operator_count=2; scenario_hints=wire-sequence-terminal");
        prompt.Should().Contain("wire-sequence-terminal");
        prompt.Should().Contain("DeepLearning.ModelPath");
        prompt.Should().Contain("ModelPath=todo");
        prompt.Should().Contain("功能可用但未完成现场工业验证");
        prompt.Should().Contain("Q2");
    }

    [Fact(DisplayName = "PromptBuilder should filter out knowledge cards that fail metadata validation")]
    public void BuildSystemPrompt_WithInvalidKnowledgeCards_ShouldDropThem()
    {
        var factory = new OperatorFactory();
        var retriever = new FakeOperatorKnowledgeRetriever(new OperatorKnowledgeSlice
        {
            RetrievalSummary = "operator_count=2; scenario_hints=wire-sequence-terminal",
            MatchedScenarioKeys = ["wire-sequence-terminal"],
            Cards =
            [
                CreateValidatedCard(factory, "ResultOutput"),
                new OperatorKnowledgeCard
                {
                    OperatorType = "UnknownOperator",
                    DisplayName = "Unknown",
                    Category = "Test",
                    Inputs =
                    [
                        new OperatorKnowledgePort
                        {
                            Name = "Image",
                            DisplayName = "Image",
                            DataType = "Image"
                        }
                    ]
                }
            ],
            PrioritizedOperatorTypes = ["ResultOutput", "UnknownOperator"]
        });

        var builder = new PromptBuilder(factory, retriever);

        var prompt = builder.BuildSystemPrompt("端子线序检测");

        prompt.Should().Contain("ResultOutput");
        prompt.Should().NotContain("UnknownOperator");
    }

    private static OperatorKnowledgeCard CreateValidatedCard(
        IOperatorFactory factory,
        string operatorType,
        Action<OperatorKnowledgeCard>? configure = null)
    {
        var metadata = factory.GetAllMetadata()
            .Single(item => item.Type.ToString().Equals(operatorType, StringComparison.OrdinalIgnoreCase));
        var card = new OperatorKnowledgeCard
        {
            OperatorType = metadata.Type.ToString(),
            DisplayName = metadata.DisplayName,
            Category = metadata.Category,
            Inputs = metadata.InputPorts.Select(port => new OperatorKnowledgePort
            {
                Name = port.Name,
                DisplayName = port.DisplayName,
                DataType = port.DataType.ToString(),
                IsRequired = port.IsRequired,
                Description = port.Description
            }).ToList(),
            Outputs = metadata.OutputPorts.Select(port => new OperatorKnowledgePort
            {
                Name = port.Name,
                DisplayName = port.DisplayName,
                DataType = port.DataType.ToString(),
                IsRequired = port.IsRequired,
                Description = port.Description
            }).ToList(),
            Parameters = metadata.Parameters.Select(parameter => new OperatorKnowledgeParameter
            {
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                DataType = parameter.DataType,
                Description = parameter.Description,
                DefaultValue = parameter.DefaultValue?.ToString(),
                MinValue = parameter.MinValue?.ToString(),
                MaxValue = parameter.MaxValue?.ToString(),
                IsRequired = parameter.IsRequired,
                AllowedValues = parameter.Options?.Select(option => option.Label).ToList() ?? []
            }).ToList()
        };

        configure?.Invoke(card);
        return card;
    }

    private sealed class FakeOperatorKnowledgeRetriever : IOperatorKnowledgeRetriever
    {
        private readonly OperatorKnowledgeSlice _slice;

        public FakeOperatorKnowledgeRetriever(OperatorKnowledgeSlice slice)
        {
            _slice = slice;
        }

        public Task<OperatorKnowledgeSlice> RetrieveAsync(
            OperatorKnowledgeQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_slice);
        }
    }
}
