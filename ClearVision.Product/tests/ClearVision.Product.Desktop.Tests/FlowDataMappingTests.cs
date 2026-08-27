using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;
using OperatorType = ClearVision.Product.Core.Enums.OperatorType;
using PortDataType = ClearVision.Product.Core.Enums.PortDataType;
using PortDirection = ClearVision.Product.Core.Enums.PortDirection;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
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
    public void UpdateFlowRequest_ToPreviewEntity_PreservesFlowCanvasSerializedImageEdge()
    {
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPortId = Guid.NewGuid();
        var thresholdId = Guid.NewGuid();
        var thresholdInputPortId = Guid.NewGuid();
        var thresholdOutputPortId = Guid.NewGuid();

        var flowData = new UpdateFlowRequest
        {
            Operators =
            [
                new OperatorDto
                {
                    Id = acquisitionId,
                    Name = "ImageAcquisition",
                    Type = OperatorType.ImageAcquisition,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = acquisitionOutputPortId,
                            Name = "Image",
                            DataType = PortDataType.Image,
                            Direction = PortDirection.Output
                        }
                    ],
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "SourceType",
                            DisplayName = "SourceType",
                            DataType = "enum",
                            Value = "File",
                            DefaultValue = "File"
                        },
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "FilePath",
                            DisplayName = "FilePath",
                            DataType = "file",
                            Value = "C:\\Images\\part.png",
                            DefaultValue = string.Empty
                        }
                    ]
                },
                new OperatorDto
                {
                    Id = thresholdId,
                    Name = "Threshold",
                    Type = OperatorType.Thresholding,
                    InputPorts =
                    [
                        new PortDto
                        {
                            Id = thresholdInputPortId,
                            Name = "Image",
                            DataType = PortDataType.Image,
                            Direction = PortDirection.Input,
                            IsRequired = true
                        }
                    ],
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = thresholdOutputPortId,
                            Name = "Image",
                            DataType = PortDataType.Image,
                            Direction = PortDirection.Output
                        }
                    ],
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Threshold",
                            DisplayName = "Threshold",
                            DataType = "double",
                            Value = 127.0,
                            DefaultValue = 127.0
                        }
                    ]
                }
            ],
            Connections =
            [
                new OperatorConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceOperatorId = acquisitionId,
                    SourcePortId = acquisitionOutputPortId,
                    TargetOperatorId = thresholdId,
                    TargetPortId = thresholdInputPortId
                }
            ]
        };

        var flow = FlowEntityMapper.ToPreviewEntity(flowData, thresholdId);

        flow.Operators.Select(op => op.Id).Should().BeEquivalentTo([acquisitionId, thresholdId]);
        flow.Connections.Should().ContainSingle();
        flow.Connections.Single().SourceOperatorId.Should().Be(acquisitionId);
        flow.Connections.Single().SourcePortId.Should().Be(acquisitionOutputPortId);
        flow.Connections.Single().TargetOperatorId.Should().Be(thresholdId);
        flow.Connections.Single().TargetPortId.Should().Be(thresholdInputPortId);
    }

    [Fact]
    public void FlowDataDto_ToEntity_PreservesCanvasOperatorDisabledState()
    {
        var operatorId = Guid.NewGuid();
        var dto = new FlowDataDto
        {
            Operators = new List<CanvasOperatorDataDto>
            {
                new()
                {
                    Id = operatorId,
                    Name = "DisabledResize",
                    Type = "ImageResize",
                    IsEnabled = false
                }
            }
        };

        var flow = dto.ToEntity();

        flow.Operators.Should().ContainSingle();
        flow.Operators.Single().Id.Should().Be(operatorId);
        flow.Operators.Single().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateFlowRequest_ToPreviewEntity_RejectsAmbiguousCompatiblePortRecovery()
    {
        var sourceOperatorId = Guid.NewGuid();
        var objectsPortId = Guid.NewGuid();
        var defectsPortId = Guid.NewGuid();
        var targetOperatorId = Guid.NewGuid();
        var targetPortId = Guid.NewGuid();
        var missingSourcePortId = Guid.NewGuid();
        var request = new UpdateFlowRequest
        {
            Operators =
            [
                new OperatorDto
                {
                    Id = sourceOperatorId,
                    Name = "Detector",
                    Type = OperatorType.DeepLearning,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = objectsPortId,
                            Name = "Objects",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.DetectionList
                        },
                        new PortDto
                        {
                            Id = defectsPortId,
                            Name = "Defects",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.DetectionList
                        }
                    ]
                },
                new OperatorDto
                {
                    Id = targetOperatorId,
                    Name = "Filter",
                    Type = OperatorType.BoxFilter,
                    InputPorts =
                    [
                        new PortDto
                        {
                            Id = targetPortId,
                            Name = "Detections",
                            Direction = PortDirection.Input,
                            DataType = PortDataType.DetectionList,
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
                    SourcePortId = missingSourcePortId,
                    TargetOperatorId = targetOperatorId,
                    TargetPortId = targetPortId
                }
            ]
        };

        var act = () => FlowEntityMapper.ToPreviewEntity(request, targetOperatorId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*连接端口恢复存在歧义*")
            .WithMessage("*Objects*")
            .WithMessage("*Defects*");
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

    [Fact]
    public void OperatorFlowDto_ToEntity_PreservesDisabledState()
    {
        var operatorId = Guid.NewGuid();
        var dto = new OperatorFlowDto
        {
            Name = "DisabledOperatorFlow",
            Operators =
            [
                new OperatorDto
                {
                    Id = operatorId,
                    Name = "DisabledThreshold",
                    Type = OperatorType.Thresholding,
                    IsEnabled = false
                }
            ]
        };

        var flow = dto.ToEntity();

        flow.Operators.Should().ContainSingle();
        flow.Operators.Single().Id.Should().Be(operatorId);
        flow.Operators.Single().IsEnabled.Should().BeFalse();
    }
}
