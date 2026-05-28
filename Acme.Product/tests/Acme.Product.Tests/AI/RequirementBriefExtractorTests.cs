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
            .Options.Should().Contain(["划伤/划痕", "压痕/凹坑"]);
    }

    [Fact]
    public void Extract_WithTemplateSnakeCaseOptions_ShouldExposeChineseReferenceOptions()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "copper-hole-measurement",
                ScenarioName = "Copper hole spacing measurement",
                IntentTypes = ["measurement"],
                ObjectTypes = ["copper_hole", "heat_exchanger"],
                MeasurementTargets = ["hole_spacing", "copper_hole_spacing"]
            },
            Confidence = 0.2,
            MissingSignals = ["object_type", "measurement_target"]
        };

        var brief = extractor.Extract("测量孔距", null, match);

        var objectOptions = brief.ClarificationQuestions.Single(question => question.Field == "object_type").Options;
        var measurementOptions = brief.ClarificationQuestions.Single(question => question.Field == "measurement_target").Options;
        objectOptions.Should().Contain(["铜孔/孔位", "换热器"]);
        measurementOptions.Should().Contain(["孔距/圆心距离", "铜孔孔距"]);
        objectOptions.Should().NotContain(option => option.Contains('_', StringComparison.Ordinal));
        measurementOptions.Should().NotContain(option => option.Contains('_', StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_WithResolvedObject_ShouldNotKeepObjectMissingSignalAsBlocking()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "carton-appearance-inspection",
                ScenarioName = "包装箱外观检测",
                IntentTypes = ["defect_detection"],
                ObjectTypes = ["carton", "package", "label"],
                DefectTypes = ["破损", "压痕", "标签异常"]
            },
            Confidence = 0.9,
            MissingSignals = ["object_type"]
        };

        var brief = extractor.Extract("检测包装箱破损", null, match);

        brief.ObjectName.Should().NotBeNullOrWhiteSpace();
        brief.RequiredFields.Should().NotContain("object_type");
        brief.MissingFacts.Should().NotContain("需要确认检测对象");
    }

    [Fact]
    public void Extract_WithStandaloneMetalScratchText_ShouldRecognizeVisionFacts()
    {
        var extractor = new RequirementBriefExtractor();

        var brief = extractor.Extract("金属表面划痕", null, scenarioMatch: null);

        brief.IntentType.Should().Be("defect_detection");
        brief.ObjectName.Should().Be("金属件");
        brief.DefectTypes.Should().Contain("划伤");
        brief.KnownFacts.Should().Contain(fact => fact.Contains("金属件", StringComparison.Ordinal));
        brief.KnownFacts.Should().Contain(fact => fact.Contains("划伤", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_WithTraditionalTemplateMatchingText_ShouldPreferTemplateMatchingIntent()
    {
        var extractor = new RequirementBriefExtractor();

        var brief = extractor.Extract(
            "传统视觉模板匹配，上传标准模板图，后续产品图片与参考图对比判断合格与否。",
            null,
            scenarioMatch: null);

        brief.IntentType.Should().Be("template_matching_inspection");
        brief.ObjectName.Should().Be("产品/标准模板");
        brief.DecisionRule.Should().Be("OK/NG");
        brief.RequiredResources.Should().NotContain("DeepLearning.ModelPath");
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
    public void Extract_WithBlockingClarificationChecklist_ShouldIgnoreExamplesAsAnswers()
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
            请先补充以下阻断澄清项，再继续生成：
            阻断待确认项：
            - 需要确认缺陷类别
            澄清问题：
            1. 请补充需要判定的缺陷类别，例如 scratch、dent、broken。可选：scratch / dent / broken
            非阻断待补：模型资源、ROI范围
            """;

        var brief = extractor.Extract("Detect defects on metal part.", queuedHint, match);

        brief.DefectTypes.Should().BeEmpty();
        brief.RequiredFields.Should().Contain("defect_type");
        brief.KnownFacts.Should().NotContain(fact => fact.Contains("scratch", StringComparison.OrdinalIgnoreCase));
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
    public void ApplyPolicy_StrictMeasurementRequest_ShouldAskCalibrationBeforeGeneration()
    {
        var extractor = new RequirementBriefExtractor();
        var engine = new ClarificationEngine();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "copper-hole-spacing-measurement",
                ScenarioName = "两器铜孔间距检测",
                IntentTypes = ["measurement"],
                ObjectTypes = ["copper_hole"],
                MeasurementTargets = ["hole_spacing"]
            },
            Confidence = 0.9
        };

        var brief = extractor.Extract("测量两个圆形孔位的圆心距离。", null, match);
        var evaluated = engine.ApplyPolicy(brief, GenerateFlowMode.New, AiRequirementModes.Strict);

        evaluated.IntentType.Should().Be("measurement");
        evaluated.ObjectName.Should().NotBeNullOrWhiteSpace();
        evaluated.MeasurementTargets.Should().Contain("孔距/圆心距离");
        evaluated.RequiredFields.Should().Contain("calibration");
        evaluated.MissingFacts.Should().Contain("需要确认标定或像素转物理单位换算");
        evaluated.ClarificationQuestions.Should().Contain(question =>
            question.Field == "calibration" && question.Required);
        evaluated.ClarificationRequired.Should().BeTrue();
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
