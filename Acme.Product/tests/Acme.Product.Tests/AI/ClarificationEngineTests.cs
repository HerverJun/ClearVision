using System.Text.Json;
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;
using NSubstitute;

namespace Acme.Product.Tests.AI;

[Trait("Category", "Clarification")]
public class ClarificationEngineTests
{
    private readonly ClarificationEngine _engine = new();

    [Fact(DisplayName = "Complete brief for wire-sequence should not require clarification")]
    public void Evaluate_WithCompleteBrief_ShouldNotRequireClarification()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "wire-sequence-terminal",
            ScenarioName = "端子线序检测",
            IntentType = "wire_sequence",
            DefectTypes = new List<string> { "黑-蓝-棕" },
            ModelResource = "wire-seq-yolo@1.2.0",
            Confidence = 0.9
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "wire-sequence-terminal", ScenarioName = "端子线序检测" },
            Confidence = 0.9
        };

        var result = _engine.Evaluate(brief, match);

        result.ClarificationRequired.Should().BeFalse();
        result.Level.Should().Be("none");
    }

    [Fact(DisplayName = "Missing modelPath for wire-sequence should require clarification")]
    public void Evaluate_MissingModelPath_ShouldRequireClarification()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "wire-sequence-terminal",
            ScenarioName = "端子线序检测",
            IntentType = "wire_sequence",
            DefectTypes = new List<string> { "黑-蓝-棕" },
            ModelResource = string.Empty,
            Confidence = 0.8
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "wire-sequence-terminal", ScenarioName = "端子线序检测" },
            Confidence = 0.8
        };

        var result = _engine.Evaluate(brief, match);

        result.ClarificationRequired.Should().BeTrue();
        result.Level.Should().Be("required");
        result.Questions.Should().Contain(q => q.Field == "modelPath");
        result.StillMissingFields.Should().Contain("modelPath");
    }

    [Fact(DisplayName = "Missing defectTypes for carton appearance should require clarification")]
    public void Evaluate_MissingDefectTypes_ShouldRequireClarification()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            ScenarioName = "包装箱外观检测",
            IntentType = "defect_detection",
            DefectTypes = new List<string>(),
            ModelResource = "model.pt",
            Confidence = 0.85
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection", ScenarioName = "包装箱外观检测" },
            Confidence = 0.85
        };

        var result = _engine.Evaluate(brief, match);

        result.ClarificationRequired.Should().BeTrue();
        result.Questions.Should().Contain(q => q.Field == "defectTypes");
    }

    [Fact(DisplayName = "Missing measurementTargets for copper-hole should require clarification")]
    public void Evaluate_MissingMeasurementTargets_ShouldRequireClarification()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "copper-hole-spacing-measurement",
            ScenarioName = "两器铜孔间距检测",
            IntentType = "measurement",
            MeasurementTargets = new List<string>(),
            Confidence = 0.8
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "copper-hole-spacing-measurement", ScenarioName = "两器铜孔间距检测" },
            Confidence = 0.8
        };

        var result = _engine.Evaluate(brief, match);

        result.ClarificationRequired.Should().BeTrue();
        result.Questions.Should().Contain(q => q.Field == "measurementTargets");
    }

    [Fact(DisplayName = "Low confidence scenario should ask for scenario confirmation")]
    public void Evaluate_LowConfidenceScenario_ShouldRequireScenarioConfirmation()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            ScenarioName = "包装箱外观检测",
            IntentType = "defect_detection",
            DefectTypes = new List<string> { "破损" },
            ModelResource = "model.pt",
            Confidence = 0.2
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection", ScenarioName = "包装箱外观检测" },
            Confidence = 0.2
        };

        var result = _engine.Evaluate(brief, match);

        result.ClarificationRequired.Should().BeTrue();
        result.Questions.Should().Contain(q => q.Field == "scenario");
    }

    [Fact(DisplayName = "Unknown scenario with intentType should use default required fields")]
    public void Evaluate_UnknownScenario_ShouldUseDefaultRequiredFields()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = string.Empty,
            IntentType = string.Empty,
            Confidence = 0
        };

        var result = _engine.Evaluate(brief, null);

        result.ClarificationRequired.Should().BeTrue();
        result.Questions.Should().Contain(q => q.Field == "intentType");
    }

    [Fact(DisplayName = "Recommended level when optional fields are missing")]
    public void Evaluate_MissingOptionalFields_ShouldBeRecommended()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            ScenarioName = "包装箱外观检测",
            IntentType = "defect_detection",
            DefectTypes = new List<string> { "破损" },
            ModelResource = "model.pt",
            OutputTarget = "unknown",
            Confidence = 0.85
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection", ScenarioName = "包装箱外观检测" },
            Confidence = 0.85
        };

        var result = _engine.Evaluate(brief, match);

        // defectTypes + modelPath are filled, but outputTarget is "unknown" which isn't in the required fields for carton
        // The carton scenario only requires defectTypes + modelPath, so it should pass
        result.ClarificationRequired.Should().BeFalse();
    }

    [Fact(DisplayName = "Clarification questions should have options when applicable")]
    public void Evaluate_QuestionsShouldHaveOptions()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            ScenarioName = "包装箱外观检测",
            IntentType = "defect_detection",
            DefectTypes = new List<string>(),
            ModelResource = string.Empty,
            Confidence = 0.8
        };
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection", ScenarioName = "包装箱外观检测" },
            Confidence = 0.8
        };

        var result = _engine.Evaluate(brief, match);

        var defectQuestion = result.Questions.FirstOrDefault(q => q.Field == "defectTypes");
        defectQuestion.Should().NotBeNull();
        defectQuestion!.Options.Should().NotBeEmpty();
        defectQuestion.Level.Should().Be("required");
    }

    [Fact(DisplayName = "RequirementBriefExtractor should populate new fields")]
    public void Extract_ShouldPopulateNewFields()
    {
        var extractor = new RequirementBriefExtractor();
        var match = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "carton-appearance-inspection",
                ScenarioName = "包装箱外观检测",
                Industry = "包装终检",
                IntentTypes = new List<string> { "defect_detection" },
                ObjectTypes = new List<string> { "包装箱" },
                DefectTypes = new List<string> { "破损", "压痕" },
                RequiredResources = new List<string> { "DeepLearning.ModelPath" }
            },
            Confidence = 0.85
        };

        var brief = extractor.Extract("检测包装箱破损", null, match);

        brief.Industry.Should().Be("包装终检");
        brief.Confidence.Should().Be(0.85);
        brief.AiModelRequired.Should().BeTrue();
        brief.DecisionRule.Should().Be("pass_fail");
        brief.ObjectName.Should().Be("包装箱");
        brief.MissingFields.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should serialize clarification fields")]
    public async Task HandleAsync_WhenClarificationRequired_ShouldSerializeQuestions()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<Acme.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Acme.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = false,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                RequirementBrief = new AiRequirementBrief
                {
                    ScenarioKey = "carton-appearance-inspection",
                    ScenarioName = "包装箱外观检测",
                    IntentType = "defect_detection"
                },
                ClarificationQuestions = new List<AiClarificationQuestion>
                {
                    new()
                    {
                        Field = "defectTypes",
                        Question = "请补充缺陷类型",
                        Required = true,
                        Level = "required",
                        Options = new List<string> { "破损", "压痕" }
                    }
                }
            }));

        var resultJson = await handler.HandleAsync(
            description: "检测缺陷",
            sessionId: "session-1");

        using var doc = JsonDocument.Parse(resultJson);
        doc.RootElement.GetProperty("status").GetString().Should().Be("clarification_required");

        var questions = doc.RootElement.GetProperty("clarificationQuestions");
        questions.GetArrayLength().Should().Be(1);
        questions[0].GetProperty("field").GetString().Should().Be("defectTypes");
        questions[0].GetProperty("required").GetBoolean().Should().BeTrue();
        questions[0].GetProperty("options").GetArrayLength().Should().Be(2);

        doc.RootElement.TryGetProperty("requirementBrief", out _).Should().BeTrue();
    }
}
