using Acme.Product.Core.Entities;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public class RequirementBriefExtractorTests
{
    [Fact]
    public void Extract_GenericDefectRequest_ShouldAskRequiredClarification()
    {
        var extractor = new RequirementBriefExtractor();

        var brief = extractor.Extract("检测缺陷", null, null);

        brief.IntentType.Should().Be("defect_detection");
        brief.ClarificationQuestions.Should().Contain(question => question.Field == "scenario" && question.Required);
        brief.ClarificationQuestions.Should().Contain(question => question.Field == "defectTypes" && question.Required);
        brief.CanGenerateDraftNow.Should().BeFalse();
        brief.MissingFields.Should().Contain("defectTypes");
    }

    [Fact]
    public void Extract_YoloCartonDamage_ShouldAllowDraftButKeepModelResourceAsMissing()
    {
        var extractor = new RequirementBriefExtractor();
        var match = BuildMatch(
            scenarioKey: "carton-appearance-inspection",
            scenarioName: "包装箱外观检测",
            industry: "包装终检",
            intentTypes: ["defect_detection", "appearance_inspection"],
            objectTypes: ["carton", "package"],
            defectTypes: ["破损", "压痕"],
            measurementTargets: [],
            requiredResources: ["DeepLearning.ModelPath"],
            confidence: 0.82);

        var brief = extractor.Extract("用 YOLO 检测包装箱破损", null, match);

        brief.ScenarioKey.Should().Be("carton-appearance-inspection");
        brief.DefectTypes.Should().Contain("破损");
        brief.AiModelRequired.Should().BeTrue();
        brief.ModelResource.Should().Be("missing");
        brief.ClarificationQuestions.Where(question => question.Required).Should().BeEmpty();
        brief.ClarificationQuestions.Should().Contain(question => question.Field == "resources" && !question.Required);
        brief.CanGenerateDraftNow.Should().BeTrue();
    }

    [Fact]
    public void Extract_CopperHoleSpacingWithoutRange_ShouldAskMeasurementRule()
    {
        var extractor = new RequirementBriefExtractor();
        var match = BuildMatch(
            scenarioKey: "copper-hole-spacing-measurement",
            scenarioName: "两器铜孔间距检测",
            industry: "空调制造",
            intentTypes: ["measurement", "gap_measurement"],
            objectTypes: ["copper_hole"],
            defectTypes: ["out_of_range"],
            measurementTargets: ["hole_spacing"],
            requiredResources: [],
            confidence: 0.8);

        var brief = extractor.Extract("测铜孔间距", null, match);

        brief.MeasurementTargets.Should().Contain("间距");
        brief.ClarificationQuestions.Should().Contain(question =>
            question.Field == "measurementRule" &&
            question.Required &&
            question.Question.Contains("合格范围", StringComparison.Ordinal));
        brief.CanGenerateDraftNow.Should().BeFalse();
    }

    private static ScenarioMatchResult BuildMatch(
        string scenarioKey,
        string scenarioName,
        string industry,
        IReadOnlyList<string> intentTypes,
        IReadOnlyList<string> objectTypes,
        IReadOnlyList<string> defectTypes,
        IReadOnlyList<string> measurementTargets,
        IReadOnlyList<string> requiredResources,
        double confidence)
    {
        return new ScenarioMatchResult
        {
            Confidence = confidence,
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = scenarioKey,
                ScenarioName = scenarioName,
                Industry = industry,
                IntentTypes = intentTypes.ToList(),
                ObjectTypes = objectTypes.ToList(),
                DefectTypes = defectTypes.ToList(),
                MeasurementTargets = measurementTargets.ToList(),
                RequiredResources = requiredResources.ToList()
            }
        };
    }
}
