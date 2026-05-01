using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public class ClarificationEngineTests
{
    private readonly ClarificationEngine _engine = new();

    [Fact]
    public void Evaluate_EmptyBrief_GatesGeneration()
    {
        var brief = new AiRequirementBrief();
        var result = _engine.Evaluate(brief, null);

        result.GateGeneration.Should().BeTrue();
        result.Questions.Should().NotBeEmpty();
        result.Questions.Should().Contain(q => q.Field == "scenario");
    }

    [Fact]
    public void Evaluate_MissingDefectTypes_ForWireSequence_GatesGeneration()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "wire-sequence-terminal",
            SceneType = "wire_sequence",
            ObjectName = "端子排"
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "wire-sequence-terminal" },
            Confidence = 0.85
        };

        var result = _engine.Evaluate(brief, scenario, "wire-sequence-terminal");

        result.GateGeneration.Should().BeTrue();
        result.MissingRequiredFields.Should().Contain("defectTypes");
        result.Questions.Should().Contain(q => q.Field == "defectTypes");
    }

    [Fact]
    public void Evaluate_DefectTypesFilled_ForWireSequence_PassesGate()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "wire-sequence-terminal",
            SceneType = "wire_sequence",
            ObjectName = "端子排",
            DefectTypes = ["错序"]
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "wire-sequence-terminal" },
            Confidence = 0.85
        };

        var result = _engine.Evaluate(brief, scenario, "wire-sequence-terminal");

        result.GateGeneration.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MissingDefectTypes_ForCartonAppearance_GatesGeneration()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            SceneType = "appearance_defect",
            ObjectName = "包装箱"
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection" },
            Confidence = 0.90
        };

        var result = _engine.Evaluate(brief, scenario);

        result.GateGeneration.Should().BeTrue();
        result.MissingRequiredFields.Should().Contain("defectTypes");
    }

    [Fact]
    public void Evaluate_MissingMeasurementTargets_ForCopperHoleSpacing_GatesGeneration()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "copper-hole-spacing-measurement",
            SceneType = "measurement"
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "copper-hole-spacing-measurement" },
            Confidence = 0.88
        };

        var result = _engine.Evaluate(brief, scenario);

        result.GateGeneration.Should().BeTrue();
        result.MissingRequiredFields.Should().Contain("measurementTargets");
        result.Questions.Should().Contain(q => q.Field == "measurementTargets");
    }

    [Fact]
    public void Evaluate_MissingObjectName_ForAirconIndoor_GatesGeneration()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "aircon-indoor-appearance-inspection",
            SceneType = "appearance_defect",
            DefectTypes = ["划伤"]
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "aircon-indoor-appearance-inspection" },
            Confidence = 0.82
        };

        var result = _engine.Evaluate(brief, scenario);

        result.GateGeneration.Should().BeTrue();
        result.MissingRequiredFields.Should().Contain("objectName");
    }

    [Fact]
    public void Evaluate_LowConfidenceNoScenario_AsksForScenarioFirst()
    {
        var brief = new AiRequirementBrief
        {
            DefectTypes = ["破损"]
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = string.Empty },
            Confidence = 0.20
        };

        var result = _engine.Evaluate(brief, scenario);

        result.GateGeneration.Should().BeTrue();
        result.Questions.Should().Contain(q => q.Field == "scenario");
    }

    [Fact]
    public void Evaluate_QuestionsLimitedToThree()
    {
        var brief = new AiRequirementBrief();
        // Empty brief should trigger many missing fields but only return max 3 questions
        var result = _engine.Evaluate(brief, null);

        result.Questions.Should().HaveCountLessThanOrEqualTo(3);
    }

    [Fact]
    public void Evaluate_RequiredQuestionsComeFirst()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            SceneType = "appearance_defect"
            // Missing both defectTypes (required) and modelResource (recommended)
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection" },
            Confidence = 0.85
        };

        var result = _engine.Evaluate(brief, scenario);

        // First question should be about the required field (defectTypes)
        result.Questions.Should().NotBeEmpty();
        result.Questions[0].Level.Should().Be("Required");
        result.Questions[0].Field.Should().Be("defectTypes");
    }

    [Fact]
    public void Evaluate_AllFieldsComplete_PassesGate()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "wire-sequence-terminal",
            SceneType = "wire_sequence",
            ObjectName = "端子排",
            DefectTypes = ["错序"],
            Industry = "线束装配",
            ImageSource = "camera",
            TriggerMode = "software",
            OutputTarget = "ResultOutput",
            ModelResource = "models/wire_seq.onnx",
            DecisionRule = "线序与期望不符即 NG",
            Confidence = 0.90
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "wire-sequence-terminal" },
            Confidence = 0.92
        };

        var result = _engine.Evaluate(brief, scenario);

        result.GateGeneration.Should().BeFalse();
        result.MissingRequiredFields.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ClarificationQuestionsHaveOptions()
    {
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            SceneType = "appearance_defect"
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "carton-appearance-inspection" },
            Confidence = 0.85
        };

        var result = _engine.Evaluate(brief, scenario);

        var defectQuestion = result.Questions.FirstOrDefault(q => q.Field == "defectTypes");
        defectQuestion.Should().NotBeNull();
        defectQuestion!.Options.Should().NotBeEmpty();
        defectQuestion.Options.Should().Contain("划伤");
        defectQuestion.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Evaluate_WithTemplateScenarioKey_UsesCorrectSpec()
    {
        // Using "remote-controller-missing-inspection" which only requires objectName
        var brief = new AiRequirementBrief
        {
            ScenarioKey = "remote-controller-missing-inspection",
            SceneType = "missing_part",
            ObjectName = "遥控器"
            // No defectTypes - but for this template, defectTypes is not required
        };

        var scenario = new ScenarioMatchResult
        {
            Scenario = new ScenarioDefinition { ScenarioKey = "remote-controller-missing-inspection" },
            Confidence = 0.80
        };

        var result = _engine.Evaluate(brief, scenario, "remote-controller-missing-inspection");

        // Should NOT gate because required fields (objectName) are present
        result.GateGeneration.Should().BeFalse();
    }
}
