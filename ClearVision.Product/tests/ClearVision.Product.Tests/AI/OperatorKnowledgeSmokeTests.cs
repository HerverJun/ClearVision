using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class OperatorKnowledgeSmokeTests
{
    [Fact(DisplayName = "OperatorKnowledgeGraphService should include DeepLearning evidence and topology hints")]
    public async Task GraphService_ShouldBuildDeepLearningCard()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clearvision-operator-kg-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new OperatorFactory();
            var templateService = new FlowTemplateService(tempRoot);
            var graphService = new OperatorKnowledgeGraphService(factory, templateService);

            var graph = await graphService.BuildAsync();
            var deepLearning = graph.Cards.Single(card => card.OperatorType == "DeepLearning");

            deepLearning.Evidence.IndustrialStatus.Should().NotBeNullOrWhiteSpace();
            deepLearning.TypicalDownstream.Should().Contain(item => item.Equals("BoxFilter", StringComparison.OrdinalIgnoreCase));
            graph.Edges.Should().Contain(edge =>
                edge.RelationType == "COMMONLY_PRECEDES" &&
                edge.Source == "DeepLearning" &&
                edge.Target == "BoxFilter");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact(DisplayName = "OperatorKnowledgeRetriever should return scenario-linked measurement chain")]
    public async Task Retriever_ShouldReturnMeasurementChain()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clearvision-operator-kg-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new OperatorFactory();
            var templateService = new FlowTemplateService(tempRoot);
            var scenarioMatcher = new ScenarioMatcher(templateService);
            var graphService = new OperatorKnowledgeGraphService(factory, templateService);
            var retriever = new OperatorKnowledgeRetriever(factory, scenarioMatcher, graphService);

            var slice = await retriever.RetrieveAsync(new OperatorKnowledgeQuery
            {
                Description = "测量两器铜孔之间的间距是否合格",
                TopN = 20
            });

            slice.PrioritizedOperatorTypes.Should().Contain("EdgeDetection");
            slice.PrioritizedOperatorTypes.Should().Contain("GapMeasurement");
            slice.PrioritizedOperatorTypes.Should().Contain("ResultJudgment");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
