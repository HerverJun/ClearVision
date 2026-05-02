using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public class RequirementBriefExtractorTests
{
    [Fact]
    public void Extract_WithGenericDefectRequest_ShouldNotDefaultDefectTypeFromTemplate()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "surface-defect",
                ScenarioName = "Surface defect inspection",
                IntentTypes = ["defect_detection"],
                ObjectTypes = ["metal part"],
                DefectTypes = ["scratch", "dent"]
            },
            Confidence = 0.86
        };

        var brief = extractor.Extract("Detect defects on metal part.", null, match);

        brief.ObjectTypes.Should().Contain("metal part");
        brief.DefectTypes.Should().BeEmpty();
        brief.RequiredFields.Should().Contain("defect_type");
        brief.ClarificationQuestions.Should().Contain(question =>
            question.Field == "defect_type" && question.Required);
        brief.ClarificationQuestions.Single(question => question.Field == "defect_type")
            .Options.Should().Contain(["scratch", "dent"]);
    }

    [Fact]
    public void Extract_WithQueuedClarificationQuestions_ShouldIgnoreExamplesAsAnswers()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "surface-defect",
                ScenarioName = "Surface defect inspection",
                IntentTypes = ["defect_detection"],
                ObjectTypes = ["metal part"],
                DefectTypes = ["scratch", "dent", "broken"]
            },
            Confidence = 0.84
        };
        var queuedHint = """
            澄清问题：
            1. 请补充需要判定的缺陷类别，例如 scratch、dent、broken。可选：scratch / dent / broken
            如果想先看草稿，可以切换到“草稿优先”模式。
            """;

        var brief = extractor.Extract("Detect defects on metal part.", queuedHint, match);

        brief.DefectTypes.Should().BeEmpty();
        brief.RequiredFields.Should().Contain("defect_type");
    }

    [Fact]
    public void ApplyPolicy_StrictMode_ShouldNotBlockOnRecommendedResourceQuestions()
    {
        var extractor = new RequirementBriefExtractor();
        var engine = new ClarificationEngine();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "surface-defect",
                ScenarioName = "Surface defect inspection",
                IntentTypes = ["defect_detection"],
                ObjectTypes = ["metal part"],
                DefectTypes = ["scratch"],
                RequiredResources = ["DeepLearning.ModelPath"]
            },
            Confidence = 0.9,
            MissingSignals = ["model_path", "roi", "calibration", "output_target"]
        };

        var brief = extractor.Extract(
            "Detect scratch defects on metal part and show the result in UI.",
            null,
            match);
        var evaluated = engine.ApplyPolicy(brief, GenerateFlowMode.New, AiRequirementModes.Strict);

        evaluated.MissingFacts.Should().BeEmpty();
        evaluated.ClarificationQuestions.Should().Contain(question => question.Field == "model_path" && !question.Required);
        evaluated.ClarificationRequired.Should().BeFalse();
    }

    [Fact]
    public void ApplyPolicy_DraftMode_ShouldAllowDraftWhenOnlyClarificationQuestionsRemain()
    {
        var extractor = new RequirementBriefExtractor();
        var engine = new ClarificationEngine();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "surface-defect",
                ScenarioName = "Surface defect inspection",
                IntentTypes = ["defect_detection"],
                ObjectTypes = ["metal part"],
                DefectTypes = ["scratch", "dent"]
            },
            Confidence = 0.82
        };

        var strictBrief = extractor.Extract("Detect defects on metal part.", null, match);
        var draftBrief = extractor.Extract("Detect defects on metal part.", null, match);
        var strict = engine.ApplyPolicy(strictBrief, GenerateFlowMode.New, AiRequirementModes.Strict);
        var draft = engine.ApplyPolicy(draftBrief, GenerateFlowMode.New, AiRequirementModes.Draft);

        strict.ClarificationRequired.Should().BeTrue();
        draft.CanGenerateDraftNow.Should().BeTrue();
        draft.ClarificationRequired.Should().BeFalse();
        draft.RequirementMode.Should().Be(AiRequirementModes.Draft);
    }

    [Fact]
    public void Extract_WithAmbiguousMeasurementRequest_ShouldProduceClarificationQuestions()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "distance-measurement",
                ScenarioName = "孔距测量",
                IntentTypes = ["measurement"],
                ObjectTypes = ["孔位"],
                MeasurementTargets = ["圆心距离"],
                RequiredResources = ["Camera.Calibration"]
            },
            Confidence = 0.22,
            MissingSignals = ["measurement_target", "ambiguous_negative_signal"]
        };

        var brief = extractor.Extract(
            "请测量孔距，结果发给PLC，并做标定",
            "现场用相机拍照",
            match);

        brief.IntentType.Should().Be("measurement");
        brief.ObjectTypes.Should().BeEmpty();
        brief.ObjectName.Should().Be(match.Scenario!.ScenarioName);
        brief.OutputTarget.Should().Be("PLC");
        brief.CalibrationRequirement.Should().Be("pixel_to_world");
        brief.ClarificationRequired.Should().BeTrue();
        brief.CanGenerateDraftNow.Should().BeFalse();
        brief.RequiredFields.Should().Contain("scene");
        brief.ClarificationQuestions.Should().NotBeEmpty();
        brief.ClarificationQuestions[0].Question.Should().NotBeNullOrWhiteSpace();
        brief.ClarificationQuestions.Single(question => question.Field == "object_type")
            .Options.Should().NotBeEmpty();
        brief.ClarificationQuestions.Single(question => question.Field == "measurement_target")
            .Options.Should().NotBeEmpty();
    }

    [Fact]
    public void Extract_WithConcreteRequest_ShouldAllowDraftGeneration()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "appearance-classification",
                ScenarioName = "表面分类",
                IntentTypes = ["classification"],
                ObjectTypes = ["产品"]
            },
            Confidence = 0.92
        };

        var brief = extractor.Extract(
            "识别产品类别并输出到UI，使用相机拍照",
            "优先给出可运行草稿",
            match);

        brief.IntentType.Should().Be("classification");
        brief.ObjectName.Should().Be("产品");
        brief.OutputTarget.Should().Be("UI");
        brief.ImageSource.Should().Be("camera");
        brief.ClarificationRequired.Should().BeFalse();
        brief.CanGenerateDraftNow.Should().BeTrue();
        brief.DraftRiskLevel.Should().BeOneOf("low", "medium");
    }
}
