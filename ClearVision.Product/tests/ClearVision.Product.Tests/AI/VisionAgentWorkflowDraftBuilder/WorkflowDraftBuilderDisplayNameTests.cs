using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentWorkflowDraftBuilder;

public sealed class WorkflowDraftBuilderDisplayNameTests
{
    [Fact(DisplayName = "WorkflowDraftBuilder should use contract display name for canvas operators")]
    public async Task DraftAsync_ShouldUseContractDisplayNameForCanvasOperators()
    {
        var result = await BuildDraftAsync(
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_1",
                OperatorType = "ImageAcquisition"
            },
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_2",
                OperatorType = "ResultOutput"
            });

        var imageAcquisition = result.Payload.CanvasFlow.Operators
            .Single(op => op.Type == OperatorType.ImageAcquisition);

        imageAcquisition.Name.Should().Be(ContractDisplayName("ImageAcquisition"));
        imageAcquisition.Name.Should().NotBe("op_1");
        imageAcquisition.Name.Should().NotMatchRegex(@"^(op|operator|temp)_\d+$");
    }

    [Fact(DisplayName = "WorkflowDraftBuilder should keep duplicate operator type names friendly and stable")]
    public async Task DraftAsync_ShouldKeepDuplicateOperatorTypeNamesFriendlyAndStable()
    {
        var result = await BuildDraftAsync(
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_cam",
                OperatorType = "ImageAcquisition"
            },
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_threshold_a",
                OperatorType = "Thresholding"
            },
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_threshold_b",
                OperatorType = "Thresholding"
            },
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_out",
                OperatorType = "ResultOutput"
            });

        var thresholdDisplayName = ContractDisplayName("Thresholding");
        var thresholdNames = result.Payload.CanvasFlow.Operators
            .Where(op => op.Type == OperatorType.Thresholding)
            .Select(op => op.Name)
            .ToList();

        thresholdNames.Should().Equal(thresholdDisplayName, thresholdDisplayName);
        thresholdNames.Should().OnlyContain(name =>
            !name.Contains("op_", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("temp_", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("operator_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "WorkflowDraftBuilder should retain canonical tempIds and connect canvas nodes by tempId")]
    public async Task DraftAsync_ShouldRetainCanonicalTempIdsAndConnectCanvasNodesByTempId()
    {
        var result = await BuildDraftAsync(
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_1",
                OperatorType = "ImageAcquisition"
            },
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_2",
                OperatorType = "ResultOutput"
            });
        var flow = result.Payload.CanvasFlow;
        var source = flow.Operators.Single(op => op.Type == OperatorType.ImageAcquisition);
        var target = flow.Operators.Single(op => op.Type == OperatorType.ResultOutput);

        flow.Connections.Should().Contain(connection =>
            connection.SourceOperatorId == source.Id &&
            connection.TargetOperatorId == target.Id);

        using var draftDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            result.Payload.WorkflowDraft,
            VisionAgentBuildSupport.JsonOptions));
        var draft = draftDocument.RootElement;
        var operators = draft.GetProperty("operators").EnumerateArray().ToList();
        var connections = draft.GetProperty("connections").EnumerateArray().ToList();

        operators.Select(op => op.GetProperty("tempId").GetString())
            .Should()
            .Equal("op_1", "op_2");
        operators.Single(op => op.GetProperty("tempId").GetString() == "op_1")
            .GetProperty("displayName")
            .GetString()
            .Should()
            .Be(ContractDisplayName("ImageAcquisition"));
        connections.Should().Contain(connection =>
            connection.GetProperty("sourceTempId").GetString() == "op_1" &&
            connection.GetProperty("targetTempId").GetString() == "op_2");
    }

    private static Task<BuildStepResult<DraftWorkflowResolution>> BuildDraftAsync(
        params VisionAgentOperatorPipelineStep[] steps)
    {
        return new WorkflowDraftBuilder().DraftAsync(
            new AiFlowGenerationRequest("display-name test"),
            new BuildPlanLoad(),
            new BuildIntentResolution("new"),
            new OperatorPipelineResolution(steps.ToList(), []),
            new ParameterMappingResolution([], [], [], "test"),
            CancellationToken.None);
    }

    private static string ContractDisplayName(string operatorType)
    {
        var catalog = new VisionAgentOperatorContractCatalog();
        catalog.TryGet(operatorType, out var contract).Should().BeTrue();
        return contract.DisplayName;
    }
}
