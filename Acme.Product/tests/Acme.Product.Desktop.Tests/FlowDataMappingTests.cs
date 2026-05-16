using Acme.Product.Application.DTOs;
using Acme.Product.Desktop.Endpoints;
using FluentAssertions;
using OperatorType = Acme.Product.Core.Enums.OperatorType;
using PortDataType = Acme.Product.Core.Enums.PortDataType;
using PortDirection = Acme.Product.Core.Enums.PortDirection;

namespace Acme.Product.Desktop.Tests;

public class FlowDataMappingTests
{
    [Fact]
    public void FlowDataDto_ToEntity_PreservesExplicitOperatorAndPortIds()
    {
        var sourceOperatorId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetOperatorId = Guid.NewGuid();
        var targetPortId = Guid.NewGuid();

        var dto = new FlowDataDto
        {
            Id = Guid.NewGuid(),
            Name = "AutoTuneFlow",
            Operators = new List<CanvasOperatorDataDto>
            {
                new()
                {
                    Id = sourceOperatorId,
                    Name = "Detector",
                    Type = "DeepLearning",
                    OutputPorts = new List<CanvasPortDataDto>
                    {
                        new()
                        {
                            Id = sourcePortId,
                            Name = "DetectionList",
                            DataType = "DetectionList"
                        }
                    }
                },
                new()
                {
                    Id = targetOperatorId,
                    Name = "Judge",
                    Type = "DetectionSequenceJudge",
                    InputPorts = new List<CanvasPortDataDto>
                    {
                        new()
                        {
                            Id = targetPortId,
                            Name = "Detections",
                            DataType = "DetectionList",
                            IsRequired = true
                        }
                    }
                }
            },
            Connections = new List<FlowConnectionDto>
            {
                new()
                {
                    SourceOperatorId = sourceOperatorId,
                    SourcePortId = sourcePortId,
                    TargetOperatorId = targetOperatorId,
                    TargetPortId = targetPortId
                }
            }
        };

        var flow = dto.ToEntity();

        flow.Operators.Select(op => op.Id).Should().Contain(new[] { sourceOperatorId, targetOperatorId });
        flow.Operators.Single(op => op.Id == sourceOperatorId).OutputPorts.Single().Id.Should().Be(sourcePortId);
        flow.Operators.Single(op => op.Id == targetOperatorId).InputPorts.Single().Id.Should().Be(targetPortId);
        flow.Connections.Should().ContainSingle();
        flow.Connections.Single().SourceOperatorId.Should().Be(sourceOperatorId);
        flow.Connections.Single().SourcePortId.Should().Be(sourcePortId);
        flow.Connections.Single().TargetOperatorId.Should().Be(targetOperatorId);
        flow.Connections.Single().TargetPortId.Should().Be(targetPortId);
    }

    [Fact]
    public void FlowDataDto_ToEntity_WhenUsingLegacyNodes_ResolvesPortsFromMetadata()
    {
        var sourceOperatorId = Guid.NewGuid();
        var targetOperatorId = Guid.NewGuid();

        var dto = new FlowDataDto
        {
            Nodes = new List<FlowNodeDto>
            {
                new()
                {
                    Id = sourceOperatorId,
                    Name = "Resize",
                    Type = OperatorType.ImageResize,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Width"] = 640,
                        ["Height"] = 640
                    }
                },
                new()
                {
                    Id = targetOperatorId,
                    Name = "Output",
                    Type = OperatorType.ResultOutput
                }
            },
            Connections = new List<FlowConnectionDto>
            {
                new()
                {
                    SourceId = sourceOperatorId,
                    TargetId = targetOperatorId
                }
            }
        };

        var flow = dto.ToEntity();

        flow.Connections.Should().ContainSingle();
        flow.Connections.Single().SourceOperatorId.Should().Be(sourceOperatorId);
        flow.Connections.Single().TargetOperatorId.Should().Be(targetOperatorId);
        flow.Connections.Single().SourcePortId.Should().NotBe(Guid.Empty);
        flow.Connections.Single().TargetPortId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void OperatorFlowDto_ToEntity_AllowsFrontendNumberCompatibilityGroups()
    {
        var sourceOperatorId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetOperatorId = Guid.NewGuid();
        var targetPortId = Guid.NewGuid();

        var dto = new OperatorFlowDto
        {
            Name = "NumberCompatibilityFlow",
            Operators =
            [
                new OperatorDto
                {
                    Id = sourceOperatorId,
                    Name = "Counter",
                    Type = OperatorType.VariableIncrement,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = sourcePortId,
                            Name = "Count",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.Integer
                        }
                    ]
                },
                new OperatorDto
                {
                    Id = targetOperatorId,
                    Name = "Threshold",
                    Type = OperatorType.Thresholding,
                    InputPorts =
                    [
                        new PortDto
                        {
                            Id = targetPortId,
                            Name = "Threshold",
                            Direction = PortDirection.Input,
                            DataType = PortDataType.Float,
                            IsRequired = true
                        }
                    ]
                }
            ],
            Connections =
            [
                new OperatorConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceOperatorId = sourceOperatorId,
                    SourcePortId = sourcePortId,
                    TargetOperatorId = targetOperatorId,
                    TargetPortId = targetPortId
                }
            ]
        };

        var flow = dto.ToEntity();

        flow.Connections.Should().ContainSingle();
        flow.Connections.Single().SourcePortId.Should().Be(sourcePortId);
        flow.Connections.Single().TargetPortId.Should().Be(targetPortId);
    }
}
