using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentWorkflowDraftBuilder;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
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
        MetadataValue(imageAcquisition, "agentTempId").Should().Be("op_1");
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

    [Fact(DisplayName = "WorkflowDraftBuilder should reuse existing friendly node by agent temp id metadata")]
    public async Task DraftAsync_ShouldReuseExistingNodeByAgentTempIdMetadata()
    {
        var existingImage = ExistingOperator(
            OperatorType.ImageAcquisition,
            ContractDisplayName("ImageAcquisition"),
            "op_1");
        var currentFlow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "existing flow",
            Operators = [existingImage],
            Connections = []
        };

        var result = await BuildDraftAsync(
            "modify",
            currentFlow,
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
        flow.Operators.Should().HaveCount(2);
        flow.Operators.Single(op => op.Id == existingImage.Id)
            .Name
            .Should()
            .Be(ContractDisplayName("ImageAcquisition"));
        var resultOutput = flow.Operators.Single(op => op.Type == OperatorType.ResultOutput);
        MetadataValue(resultOutput, "agentTempId").Should().Be("op_2");
        flow.Connections.Should().Contain(connection =>
            connection.SourceOperatorId == existingImage.Id &&
            connection.TargetOperatorId == resultOutput.Id);
    }

    [Fact(DisplayName = "WorkflowDraftBuilder should reuse legacy tempId name when metadata is missing")]
    public async Task DraftAsync_ShouldReuseLegacyTempIdNameWithoutMetadata()
    {
        var existingImage = ExistingOperator(
            OperatorType.ImageAcquisition,
            "op_1",
            agentTempId: null);
        var currentFlow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "legacy flow",
            Operators = [existingImage],
            Connections = []
        };

        var result = await BuildDraftAsync(
            "repair",
            currentFlow,
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_1",
                OperatorType = "ImageAcquisition"
            });

        result.Payload.CanvasFlow.Operators.Should().HaveCount(1);
        var reused = result.Payload.CanvasFlow.Operators.Single();
        reused.Id.Should().Be(existingImage.Id);
        MetadataValue(reused, "agentTempId").Should().Be("op_1");
    }

    [Fact(DisplayName = "WorkflowDraftBuilder should keep same-type existing nodes separated by agent temp id")]
    public async Task DraftAsync_ShouldNotMergeSameTypeNodesWithDifferentAgentTempIds()
    {
        var thresholdA = ExistingOperator(
            OperatorType.Thresholding,
            ContractDisplayName("Thresholding"),
            "op_a");
        var thresholdB = ExistingOperator(
            OperatorType.Thresholding,
            ContractDisplayName("Thresholding"),
            "op_b");
        var currentFlow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "existing duplicate flow",
            Operators = [thresholdA, thresholdB],
            Connections = []
        };

        var result = await BuildDraftAsync(
            "append",
            currentFlow,
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_a",
                OperatorType = "Thresholding"
            },
            new VisionAgentOperatorPipelineStep
            {
                TempId = "op_b",
                OperatorType = "Thresholding"
            });

        result.Payload.CanvasFlow.Operators.Should().HaveCount(2);
        result.Payload.CanvasFlow.Operators.Select(op => op.Id)
            .Should()
            .BeEquivalentTo([thresholdA.Id, thresholdB.Id]);
        result.Payload.CanvasFlow.Operators.Select(op => MetadataValue(op, "agentTempId"))
            .Should()
            .BeEquivalentTo(["op_a", "op_b"]);
    }

    private static Task<BuildStepResult<DraftWorkflowResolution>> BuildDraftAsync(
        params VisionAgentOperatorPipelineStep[] steps)
    {
        return BuildDraftAsync("new", null, steps);
    }

    private static Task<BuildStepResult<DraftWorkflowResolution>> BuildDraftAsync(
        string buildIntent,
        OperatorFlowDto? currentFlow,
        params VisionAgentOperatorPipelineStep[] steps)
    {
        var load = new BuildPlanLoad
        {
            CurrentFlowSnapshot = currentFlow == null
                ? string.Empty
                : JsonSerializer.Serialize(currentFlow, VisionAgentBuildSupport.JsonOptions)
        };

        return new WorkflowDraftBuilder().DraftAsync(
            new AiFlowGenerationRequest("display-name test"),
            load,
            new BuildIntentResolution(buildIntent),
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

    private static OperatorDto ExistingOperator(
        OperatorType type,
        string name,
        string? agentTempId)
    {
        var catalog = new VisionAgentOperatorContractCatalog();
        catalog.TryGet(type.ToString(), out var contract).Should().BeTrue();
        return new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            Metadata = agentTempId == null
                ? null
                : new Dictionary<string, object?>
                {
                    ["agentTempId"] = agentTempId
                },
            InputPorts = contract.InputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Input,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = contract.OutputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Output,
                DataType = port.DataType
            }).ToList()
        };
    }

    private static string MetadataValue(OperatorDto op, string key)
    {
        var metadata = op.Metadata;
        metadata.Should().NotBeNull();
        metadata!.TryGetValue(key, out var value).Should().BeTrue();
        return value?.ToString() ?? string.Empty;
    }
}
