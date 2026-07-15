using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class OperatorNamingSemanticContractTests
{
    private static readonly NamingContract[] Contracts =
    [
        new(OperatorType.BlobLabeling, typeof(BlobLabelingOperator), 212, "Blob分类标注", "连通域标注", 2, 3, 3),
        new(OperatorType.PointAlignment, typeof(PointAlignmentOperator), 162, "点位偏差计算", "点位对齐", 2, 3, 2),
        new(OperatorType.RoiTransform, typeof(RoiTransformOperator), 216, "ROI位姿变换", "ROI跟踪", 2, 1, 1),
        new(OperatorType.PositionCorrection, typeof(PositionCorrectionOperator), 143, "ROI位姿补偿（像素）", "位置修正", 4, 10, 3),
        new(OperatorType.PointCorrection, typeof(PointCorrectionOperator), 163, "点位刚性补偿", "点位修正", 4, 5, 4),
        new(OperatorType.EdgePairDefect, typeof(EdgePairDefectOperator), 181, "边缘间距缺陷检测", "边缘对缺陷", 3, 4, 4),
        new(OperatorType.StatisticalOutlierRemoval, typeof(StatisticalOutlierRemovalOperator), 218, "点云统计离群点去除（SOR）", "统计滤波", 1, 3, 2),
        new(OperatorType.PPFMatch, typeof(PPFMatchOperator), 222, "PPF点云粗匹配", "PPF表面匹配", 2, 16, 10),
        new(OperatorType.PlanarMatching, typeof(PlanarMatchingOperator), 233, "平面特征匹配", "透视匹配", 2, 19, 20, ["Planar Matching"]),
        new(OperatorType.ColorDetection, typeof(ColorDetectionOperator), 45, "颜色分析", "颜色检测", 2, 10, 18),
        new(OperatorType.GeometricTolerance, typeof(GeometricToleranceOperator), 23, "二维几何公差判定", "几何公差", 5, 7, 5),
        new(OperatorType.DetectionSequenceJudge, typeof(DetectionSequenceJudgeOperator), 61, "检测顺序判定", "线序判定", 4, 13, 13),
        new(OperatorType.ImageDiff, typeof(ImageDiffOperator), 118, "图像差异率分析", "图像对比", 2, 2, 0),
        new(OperatorType.RectangleRegion, typeof(RectangleRegionOperator), 237, "矩形框定义", "矩形区域", 0, 1, 4)
    ];

    [Fact]
    public void SourceAndRuntime_ShouldExposeCorrectedIdentityWithoutShapeDrift()
    {
        var scanned = new OperatorMetadataScanner().Scan();
        var runtimeFactory = new OperatorFactory();

        scanned.Should().HaveCount(158);

        foreach (var contract in Contracts)
        {
            var attribute = contract.OperatorClass.GetCustomAttribute<OperatorMetaAttribute>(inherit: false);
            attribute.Should().NotBeNull();
            attribute!.DisplayName.Should().Be(contract.DisplayName);
            attribute.Keywords.Should().Contain(contract.LegacyDisplayName);
            foreach (var additionalLegacyName in contract.AdditionalLegacyNames)
            {
                attribute.Keywords.Should().Contain(additionalLegacyName);
            }

            ((int)contract.OperatorType).Should().Be(contract.NumericValue);

            var scannedMetadata = scanned.Single(item => item.Type == contract.OperatorType);
            scannedMetadata.DisplayName.Should().Be(contract.DisplayName);
            AssertStableShape(scannedMetadata.InputPorts.Count, scannedMetadata.OutputPorts.Count, scannedMetadata.Parameters.Count, contract);

            var runtimeMetadata = runtimeFactory.GetMetadata(contract.OperatorType);
            runtimeMetadata.Should().NotBeNull();
            runtimeMetadata!.DisplayName.Should().Be(contract.DisplayName);
            runtimeMetadata.Keywords.Should().Contain(contract.LegacyDisplayName);
            AssertStableShape(runtimeMetadata.InputPorts.Count, runtimeMetadata.OutputPorts.Count, runtimeMetadata.Parameters.Count, contract);
        }
    }

    [Fact]
    public void CreatingNodesWithLegacyNames_ShouldPreservePersistedIdentity()
    {
        var factory = new OperatorFactory();

        foreach (var contract in Contracts)
        {
            var created = factory.CreateOperator(contract.OperatorType, contract.LegacyDisplayName, 12, 34);
            created.Type.Should().Be(contract.OperatorType);
            created.Name.Should().Be(contract.LegacyDisplayName);
            created.InputPorts.Should().HaveCount(contract.InputPortCount);
            created.OutputPorts.Should().HaveCount(contract.OutputPortCount);
            created.Parameters.Should().HaveCount(contract.ParameterCount);
        }
    }

    [Fact]
    public void LegacyNamedFlows_ShouldRoundTripWithoutIdentityOrContractDrift()
    {
        var factory = new OperatorFactory();
        var created = Contracts
            .Select((contract, index) => factory.CreateOperator(
                contract.OperatorType,
                contract.LegacyDisplayName,
                index * 10,
                index * 20))
            .ToList();
        var saved = new OperatorFlowDto
        {
            Name = "legacy-operator-name-roundtrip",
            Operators = created.Select(ToDto).ToList()
        };

        var loaded = JsonSerializer.Deserialize<OperatorFlowDto>(JsonSerializer.Serialize(saved))!
            .ToEntity()
            .Operators;

        loaded.Should().HaveCount(created.Count);
        for (var index = 0; index < created.Count; index++)
        {
            var before = created[index];
            var after = loaded[index];
            after.Id.Should().Be(before.Id);
            after.Type.Should().Be(before.Type);
            after.Name.Should().Be(before.Name);
            after.InputPorts.Select(PortIdentity).Should().Equal(before.InputPorts.Select(PortIdentity));
            after.OutputPorts.Select(PortIdentity).Should().Equal(before.OutputPorts.Select(PortIdentity));
            after.Parameters.Select(ParameterIdentity).Should().Equal(before.Parameters.Select(ParameterIdentity));
        }
    }

    private static OperatorDto ToDto(Operator source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Type = source.Type,
        X = source.Position.X,
        Y = source.Position.Y,
        IsEnabled = source.IsEnabled,
        InputPorts = source.InputPorts.Select(port => new PortDto
        {
            Id = port.Id,
            Name = port.Name,
            Direction = port.Direction,
            DataType = port.DataType,
            IsRequired = port.IsRequired
        }).ToList(),
        OutputPorts = source.OutputPorts.Select(port => new PortDto
        {
            Id = port.Id,
            Name = port.Name,
            Direction = port.Direction,
            DataType = port.DataType,
            IsRequired = port.IsRequired
        }).ToList(),
        Parameters = source.Parameters.Select(parameter => new ParameterDto
        {
            Id = parameter.Id,
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

    private static string PortIdentity(ClearVision.Product.Core.ValueObjects.Port port) =>
        $"{port.Id:N}|{port.Name}|{port.Direction}|{port.DataType}|{port.IsRequired}";

    private static string ParameterIdentity(ClearVision.Product.Core.ValueObjects.Parameter parameter) =>
        $"{parameter.Id:N}|{parameter.Name}|{parameter.DisplayName}|{parameter.DataType}|{parameter.IsRequired}";

    private static void AssertStableShape(int inputs, int outputs, int parameters, NamingContract contract)
    {
        inputs.Should().Be(contract.InputPortCount, contract.OperatorType.ToString());
        outputs.Should().Be(contract.OutputPortCount, contract.OperatorType.ToString());
        parameters.Should().Be(contract.ParameterCount, contract.OperatorType.ToString());
    }

    private sealed record NamingContract(
        OperatorType OperatorType,
        Type OperatorClass,
        int NumericValue,
        string DisplayName,
        string LegacyDisplayName,
        int InputPortCount,
        int OutputPortCount,
        int ParameterCount,
        IReadOnlyList<string>? ExtraLegacyNames = null)
    {
        public IReadOnlyList<string> AdditionalLegacyNames { get; } = ExtraLegacyNames ?? [];
    }
}
