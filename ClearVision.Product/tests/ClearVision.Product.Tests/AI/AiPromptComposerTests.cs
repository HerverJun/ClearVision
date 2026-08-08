using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class AiPromptComposerTests
{
    [Fact]
    public void BuildReferenceFlowSummary_WithCanvasFlow_ShouldSummarizeOperatorsAndConnections()
    {
        const string flowJson = """
            {
              "operators": [
                {
                  "id": "op-1",
                  "name": "Gaussian",
                  "type": "Filtering",
                  "inputPorts": [{ "id": "in-1", "name": "Image" }],
                  "outputPorts": [{ "id": "out-1", "name": "Image" }],
                  "parameters": [{ "name": "KernelSize", "value": "5" }]
                },
                {
                  "id": "op-2",
                  "name": "Blob",
                  "type": "BlobAnalysis",
                  "inputPorts": [{ "id": "in-2", "name": "Image" }],
                  "outputPorts": [{ "id": "out-2", "name": "BlobCount" }],
                  "parameters": [{ "name": "MinArea", "value": "50" }]
                }
              ],
              "connections": [
                {
                  "sourceOperatorId": "op-1",
                  "sourcePortId": "out-1",
                  "targetOperatorId": "op-2",
                  "targetPortId": "in-2"
                }
              ]
            }
            """;

        var summary = AiPromptComposer.BuildReferenceFlowSummary(flowJson);

        summary.Should().Contain("operatorId=op-1");
        summary.Should().Contain("operatorType=Filtering");
        summary.Should().Contain("parameters={KernelSize=5}");
        summary.Should().Contain("op-1.Image -> op-2.Image");
    }

    [Fact]
    public void BuildUserPrompt_ShouldComposeStableSections()
    {
        var prompt = AiPromptComposer.BuildUserPrompt(new AiPromptRequest(
            Task: "Detect scratches on metal parts.",
            Mode: GenerateFlowMode.Modify,
            AdditionalContext: "Prefer traditional operators.",
            TemplatePriority: "templateFirst=true",
            AttachmentContext: "1. sample.png | type=png | resolution=1024x768",
            SessionSummary: "- user: detect scratches\n- assistant: previous draft created",
            RequirementBriefSection: "requirementMode=strict\nclarificationRequired=true",
            ReferenceFlowSummary: "operatorCount=1",
            OutputRequirements: "- Return JSON only."));

        prompt.Should().Contain("Request:");
        prompt.Should().Contain("Mode:");
        prompt.Should().Contain("mode=modify");
        prompt.Should().Contain("AttachmentContext:");
        prompt.Should().Contain("SessionSummary:");
        prompt.Should().Contain("RequirementBrief:");
        prompt.Should().Contain("clarificationRequired=true");
        prompt.Should().Contain("ReferenceFlowSummary:");
        prompt.Should().Contain("OutputRequirements:");
    }

    [Fact]
    public void BuildUserPrompt_ModifyMode_ShouldRequireTargetedEditsAndStableRuntimeKeys()
    {
        var prompt = AiPromptComposer.BuildUserPrompt(new AiPromptRequest(
            Task: "把算子名称改成中文。",
            Mode: GenerateFlowMode.Modify,
            ReferenceFlowSummary: "operatorCount=2\n- operatorId=op_1 | operatorType=ImageAcquisition"));

        prompt.Should().Contain("mode=modify");
        prompt.Should().Contain("Only change the user-requested operators");
        prompt.Should().Contain("Treat ReferenceFlowSummary as the authoritative baseline");
        prompt.Should().Contain("copy its id/tempId, operatorType, ports, parameters, and connections exactly");
        prompt.Should().Contain("change only displayName, explanation, and user-visible notes");
        prompt.Should().Contain("Never translate operatorType, port names, parameter keys, JSON keys, or enum values");
        prompt.Should().Contain("parametersNeedingReview");
    }

    [Fact]
    public void BuildReferenceFlowSummary_WithEmptyFlow_ShouldReturnEmpty()
    {
        const string flowJson = """{"operators":[],"connections":[]}""";

        AiPromptComposer.BuildReferenceFlowSummary(flowJson).Should().BeEmpty();
    }
}
