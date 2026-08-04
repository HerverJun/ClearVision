using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "vision-agent", Suites = "ServicesRegression")]
public sealed class WorkflowArtifactAdmissionGateTests
{
    [Fact]
    public void Inspect_CanonicalArtifact_ShouldBeAllowedEverywhere()
    {
        var result = CreateGate().Inspect(CreateFlow(OperatorType.ImageAcquisition), "test.canonical");

        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Canonical);
        result.Report.Diagnostics.Should().BeEmpty();
        result.Report.OriginalArtifactPreserved.Should().BeFalse();
        result.AllowedToPersist.Should().BeTrue();
        result.AllowedToRun.Should().BeTrue();
        result.AllowedToExport.Should().BeTrue();
        result.AllowedToSyncStation.Should().BeTrue();
    }

    [Fact]
    public void EntityRoundTrip_ShouldPreserveAiAdmissionMetadata()
    {
        var factory = new OperatorFactory();
        var camera = CreateOperator(
            factory.GetMetadata(OperatorType.ImageAcquisition)!,
            Guid.NewGuid());
        var matcher = CreateOperator(
            factory.GetMetadata(OperatorType.TemplateMatching)!,
            Guid.NewGuid());
        camera.Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentTempId"] = "op_cam"
        };
        matcher.Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentTempId"] = "op_match"
        };
        var sourcePort = camera.OutputPorts.Single(port => port.Name.Equals("Image", StringComparison.OrdinalIgnoreCase));
        var targetPort = matcher.InputPorts.Single(port => port.Name.Equals("Image", StringComparison.OrdinalIgnoreCase));
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "AI metadata round trip",
            Operators = [camera, matcher],
            Connections =
            [
                new OperatorConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceOperatorId = camera.Id,
                    SourcePortId = sourcePort.Id,
                    TargetOperatorId = matcher.Id,
                    TargetPortId = targetPort.Id
                }
            ]
        };
        var graph = new CanonicalWorkflowGraph(
            flow.Operators.Select(op => new CanonicalWorkflowNode(
                ReadTempId(op),
                op.Type.ToString(),
                op.Name,
                op.Parameters.ToDictionary(
                    parameter => parameter.Name,
                    parameter => Convert.ToString(parameter.Value ?? parameter.DefaultValue),
                    StringComparer.OrdinalIgnoreCase),
                op.InputPorts.Select(ToFingerprint).ToList(),
                op.OutputPorts.Select(ToFingerprint).ToList())).ToList(),
            flow.Connections.Select(connection => new CanonicalWorkflowConnection(
                ReadTempId(flow.Operators.Single(op => op.Id == connection.SourceOperatorId)),
                flow.Operators.Single(op => op.Id == connection.SourceOperatorId).OutputPorts.Single(port => port.Id == connection.SourcePortId).Name,
                ReadTempId(flow.Operators.Single(op => op.Id == connection.TargetOperatorId)),
                flow.Operators.Single(op => op.Id == connection.TargetOperatorId).InputPorts.Single(port => port.Id == connection.TargetPortId).Name)).ToList(),
            "op_cam");
        var fingerprint = WorkflowArtifactFingerprint.Compute(
            "sha256:plan",
            "catalog:v1",
            "new",
            graph);
        foreach (var op in flow.Operators)
        {
            op.Metadata!["agentTaskType"] = "template_matching";
            op.Metadata["agentArtifactFingerprint"] = fingerprint;
            op.Metadata["agentPlanHash"] = "sha256:plan";
            op.Metadata["agentCatalogVersion"] = "catalog:v1";
            op.Metadata["agentBuildIntent"] = "new";
            op.Metadata["agentRouteSemanticsSatisfied"] = true;
            op.Metadata["agentRouteContractVersion"] = "v1";
        }

        var entity = flow.ToEntity();
        entity.Operators.Select(op => op.Metadata!["agentArtifactFingerprint"])
            .Should().OnlyContain(value => Equals(value, fingerprint));

        var result = CreateGate().Inspect(
            entity,
            "test.entity-round-trip",
            context: new WorkflowArtifactAdmissionContext
            {
                TaskType = "template_matching",
                ArtifactFingerprint = fingerprint,
                RouteSemanticsSatisfied = true
            });

        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Canonical);
        result.Entity.Should().NotBeNull();
        result.Entity!.Operators.Select(op => op.Metadata!["agentTaskType"])
            .Should().OnlyContain(value => Equals(value, "template_matching"));
        result.Entity.Operators.Select(op => op.Metadata!["agentArtifactFingerprint"])
            .Should().OnlyContain(value => Equals(value, fingerprint));
        result.Entity.Operators.Select(op => op.Metadata!["agentRouteSemanticsSatisfied"])
            .Should().OnlyContain(value => Equals(value, true));
    }

    [Fact]
    public void SafeScaffoldRoute_ShouldRemainPreviewOnlyAndNeverExecutable()
    {
        var factory = new OperatorFactory();
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "safe scaffold",
            Operators =
            [
                CreateOperator(factory.GetMetadata(OperatorType.ImageAcquisition)!, Guid.NewGuid()),
                CreateOperator(factory.GetMetadata(OperatorType.ResultJudgment)!, Guid.NewGuid()),
                CreateOperator(factory.GetMetadata(OperatorType.ResultOutput)!, Guid.NewGuid())
            ]
        };

        var tempIds = new[] { "op_cam", "op_judge", "op_out" };
        for (var index = 0; index < flow.Operators.Count; index++)
        {
            flow.Operators[index].Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentTempId"] = tempIds[index],
                ["agentTaskType"] = "surface_defect_detection",
                ["agentPlanHash"] = "sha256:plan",
                ["agentCatalogVersion"] = "catalog:v1",
                ["agentBuildIntent"] = "new",
                ["agentRouteSemanticsSatisfied"] = false,
                ["agentRouteContractVersion"] = "v1"
            };
        }

        var graph = new CanonicalWorkflowGraph(
            flow.Operators.Select((op, index) => new CanonicalWorkflowNode(
                tempIds[index],
                op.Type.ToString(),
                op.Name,
                op.Parameters.ToDictionary(
                    parameter => parameter.Name,
                    parameter => Convert.ToString(parameter.Value ?? parameter.DefaultValue),
                    StringComparer.OrdinalIgnoreCase),
                op.InputPorts.Select(ToFingerprint).ToList(),
                op.OutputPorts.Select(ToFingerprint).ToList())).ToList(),
            [],
            "op_cam");
        var fingerprint = WorkflowArtifactFingerprint.Compute(
            "sha256:plan",
            "catalog:v1",
            "new",
            graph);
        foreach (var op in flow.Operators)
        {
            op.Metadata!["agentArtifactFingerprint"] = fingerprint;
        }

        var result = CreateGate().Inspect(
            flow,
            "test.safe-scaffold",
            context: new WorkflowArtifactAdmissionContext
            {
                TaskType = "surface_defect_detection",
                RouteSemanticsSatisfied = false,
                ArtifactFingerprint = fingerprint
            });

        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Quarantined);
        result.Report.PreviewOnly.Should().BeTrue();
        result.Flow.Should().NotBeNull();
        result.AllowedToPersist.Should().BeFalse();
        result.AllowedToRun.Should().BeFalse();
        result.AllowedToExport.Should().BeFalse();
        result.AllowedToSyncStation.Should().BeFalse();
        result.Report.Diagnostics.Select(item => item.Code).Should().Contain("minimum_scaffold_task_incomplete");
    }

    [Fact]
    public void Inspect_UnknownOperator_ShouldQuarantineWithoutFallback()
    {
        var flow = CreateFlow(OperatorType.ImageAcquisition);
        flow.Operators.Add(new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = "Unknown operator",
            Type = (OperatorType)9999
        });

        var result = CreateGate().Inspect(flow, "test.unknown-operator");

        AssertQuarantined(result, "unknown_operator");
    }

    [Fact]
    public void Inspect_UnknownPortsAndParameter_ShouldQuarantineWithoutDynamicSchema()
    {
        var flow = CreateFlow(OperatorType.ImageAcquisition);
        var operatorDto = flow.Operators[0];
        operatorDto.InputPorts.Add(new PortDto
        {
            Id = Guid.NewGuid(),
            Name = "UnknownInput",
            Direction = PortDirection.Input,
            DataType = PortDataType.Image
        });
        operatorDto.OutputPorts.Add(new PortDto
        {
            Id = Guid.NewGuid(),
            Name = "UnknownOutput",
            Direction = PortDirection.Output,
            DataType = PortDataType.Image
        });
        operatorDto.Parameters.Add(new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = "UnknownParameter",
            DataType = "string"
        });

        var result = CreateGate().Inspect(flow, "test.unknown-schema");

        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Quarantined);
        result.Report.Diagnostics.Select(item => item.Code).Should().Contain([
            "unknown_input_port",
            "unknown_output_port",
            "unknown_parameter"
        ]);
        result.AllowedToRun.Should().BeFalse();
        result.AllowedToExport.Should().BeFalse();
        result.AllowedToSyncStation.Should().BeFalse();
    }

    [Fact]
    public void Inspect_DuplicateOperatorIdentityAndUnknownConnectionEndpoint_ShouldQuarantine()
    {
        var flow = CreateFlow(OperatorType.ImageAcquisition);
        var source = flow.Operators[0];
        flow.Operators.Add(CloneOperator(source, source.Id));
        flow.Connections.Add(new OperatorConnectionDto
        {
            Id = Guid.NewGuid(),
            SourceOperatorId = Guid.NewGuid(),
            SourcePortId = Guid.NewGuid(),
            TargetOperatorId = source.Id,
            TargetPortId = source.InputPorts.First().Id
        });

        var result = CreateGate().Inspect(flow, "test.identity-endpoint");

        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Quarantined);
        result.Report.Diagnostics.Select(item => item.Code).Should().Contain([
            "duplicate_or_empty_operator_id",
            "unknown_connection_endpoint"
        ]);
    }

    [Fact]
    public void Inspect_OperatorIdentityMismatch_ShouldQuarantine()
    {
        var flow = CreateFlow(OperatorType.ImageAcquisition);
        flow.Operators[0].Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentRequestedOperatorType"] = nameof(OperatorType.TemplateMatching)
        };

        var result = CreateGate().Inspect(flow, "test.identity-mismatch");

        AssertQuarantined(result, "operator_name_type_mismatch");
    }

    [Fact]
    public void InspectJson_UnambiguousAlias_ShouldCreateRepairableCanonicalArtifact()
    {
        var flow = CreateFlow(OperatorType.Measurement);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        var original = JsonSerializer.Serialize(flow, options).Replace(
            $"\"{nameof(OperatorType.Measurement)}\"",
            "\"MeasureDistance\"",
            StringComparison.Ordinal);

        var store = new CapturingQuarantineStore();
        var result = CreateGate(store).InspectJson(original, "test.repairable-alias");

        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.RepairableLegacy);
        result.Flow.Should().NotBeNull();
        result.Flow!.Operators.Should().ContainSingle(item => item.Type == OperatorType.Measurement);
        result.Report.OriginalArtifactPreserved.Should().BeTrue();
        result.Report.Repairs.Should().Contain(item => item.Code == "operator_type_alias");
        result.AllowedToRun.Should().BeTrue();
        result.AllowedToExport.Should().BeTrue();
        result.AllowedToSyncStation.Should().BeTrue();
        store.Records.Should().ContainSingle();
    }

    [Fact]
    public void InspectJson_AmbiguousAlias_ShouldQuarantineAndPreserveOriginalSnapshot()
    {
        const string original = "{\"operators\":[{\"id\":\"op-1\",\"type\":\"StrawberryMaturityClassifier\"}],\"connections\":[]}";
        var store = new CapturingQuarantineStore();

        var result = CreateGate(store).InspectJson(original, "test.quarantine");

        AssertQuarantined(result, "ambiguous_or_unknown_operator_alias");
        result.Report.OriginalArtifactHash.Should().StartWith("sha256:");
        result.Report.OriginalArtifactPreserved.Should().BeTrue();
        store.Records.Should().ContainSingle(record =>
            record.OriginalSnapshot == original &&
            record.Report.OriginalArtifactHash == result.Report.OriginalArtifactHash);
    }

    private static WorkflowArtifactAdmissionGate CreateGate(CapturingQuarantineStore? store = null)
    {
        var factory = new OperatorFactory();
        return new WorkflowArtifactAdmissionGate(
            new WorkflowLegacyScanner(factory),
            new WorkflowLegacyRepairService(factory),
            store ?? new CapturingQuarantineStore());
    }

    private static OperatorFlowDto CreateFlow(OperatorType type)
    {
        var factory = new OperatorFactory();
        var metadata = factory.GetMetadata(type) ?? throw new InvalidOperationException($"Missing metadata for {type}.");
        var id = Guid.NewGuid();
        return new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "Canonical test flow",
            Operators = [CreateOperator(metadata, id)]
        };
    }

    private static OperatorDto CreateOperator(OperatorMetadata metadata, Guid id)
    {
        return new OperatorDto
        {
            Id = id,
            Name = metadata.DisplayName,
            Type = metadata.Type,
            InputPorts = metadata.InputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Input,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = metadata.OutputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Output,
                DataType = port.DataType
            }).ToList(),
            Parameters = metadata.Parameters.Select(parameter => new ParameterDto
            {
                Id = Guid.NewGuid(),
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList()
        };
    }

    private static OperatorDto CloneOperator(OperatorDto source, Guid id)
    {
        return new OperatorDto
        {
            Id = id,
            Name = source.Name,
            Type = source.Type,
            InputPorts = source.InputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = source.OutputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            Parameters = source.Parameters.Select(parameter => new ParameterDto
            {
                Id = Guid.NewGuid(),
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                Value = parameter.Value,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList()
        };
    }

    private static string ReadTempId(OperatorDto op) =>
        op.Metadata!["agentTempId"]?.ToString() ?? string.Empty;

    private static VisionAgentPortFingerprint ToFingerprint(PortDto port) => new()
    {
        Name = port.Name,
        DataType = port.DataType.ToString(),
        Required = port.IsRequired
    };

    private static void AssertQuarantined(
        WorkflowArtifactAdmissionResult result,
        params string[] expectedCodes)
    {
        result.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Quarantined);
        result.Flow.Should().BeNull();
        result.Report.Diagnostics.Select(item => item.Code).Should().Contain(expectedCodes);
        result.AllowedToPersist.Should().BeFalse();
        result.AllowedToRun.Should().BeFalse();
        result.AllowedToExport.Should().BeFalse();
        result.AllowedToSyncStation.Should().BeFalse();
    }

    private sealed class CapturingQuarantineStore : IWorkflowArtifactQuarantineStore
    {
        public List<WorkflowArtifactQuarantineRecord> Records { get; } = [];

        public void Preserve(WorkflowArtifactQuarantineRecord record) => Records.Add(record);
    }
}
