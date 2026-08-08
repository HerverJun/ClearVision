using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
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
        new(OperatorType.RectangleRegion, typeof(RectangleRegionOperator), 237, "矩形框定义", "矩形区域", 0, 1, 4),
        new(OperatorType.CoordinateTransform, typeof(CoordinateTransformOperator), 26, "像素到物理坐标（单点）", "坐标转换", 4, 3, 2, ["Coordinate Transform"]),
        new(OperatorType.RoiManager, typeof(RoiManagerOperator), 42, "ROI裁剪与掩膜", "ROI管理器", 1, 3, 10),
        new(OperatorType.TryCatch, typeof(TryCatchOperator), 83, "Try分支透传", "异常捕获", 1, 4, 3, ["Try-Catch 流程控制"]),
        new(OperatorType.ModbusCommunication, typeof(ModbusCommunicationOperator), 27, "Modbus TCP通信", "Modbus通信", 1, 2, 9, ["Modbus Communication"]),
        new(OperatorType.Thresholding, typeof(ThresholdOperator), 4, "全局阈值处理", "二值化", 1, 1, 4, ["Threshold"]),
        new(OperatorType.FFT1D, typeof(FFT1DOperator), 251, "信号/图像傅里叶变换（FFT）", "一维FFT", 2, 4, 0, ["FFT 1D"]),
        new(OperatorType.InverseFFT1D, typeof(InverseFFT1DOperator), 253, "信号/图像逆傅里叶变换（IFFT）", "一维逆FFT", 2, 4, 0, ["Inverse FFT 1D"]),
        new(OperatorType.PhaseClosure, typeof(PhaseClosureOperator), 254, "相位解缠绕", "Phase Closure", 4, 4, 0, ["相位闭合"])
    ];

    private static readonly IReadOnlyDictionary<OperatorType, string[]> CompatibilitySearchAliases =
        new Dictionary<OperatorType, string[]>
        {
            [OperatorType.ImageAcquisition] = ["Image Acquisition"],
            [OperatorType.Filtering] = ["Filtering", "滤波处理"],
            [OperatorType.RoiManager] = ["ROI管理", "ROI Manager"],
            [OperatorType.TemplateMatching] = ["Template Matching"],
            [OperatorType.BlobAnalysis] = ["Blob Analysis", "斑点分析"],
            [OperatorType.EdgeDetection] = ["Edge Detection"],
            [OperatorType.ShapeMatching] = ["Shape Matching"],
            [OperatorType.DeepLearning] = ["Deep Learning", "深度学习检测", "深度学习推理"],
            [OperatorType.SemanticSegmentation] = ["Semantic Segmentation"],
            [OperatorType.SurfaceDefectDetection] = ["Surface Defect Detection"],
            [OperatorType.CircleMeasurement] = ["Circle Measurement"],
            [OperatorType.Measurement] = ["Measurement", "几何测量"],
            [OperatorType.UnitConvert] = ["Unit Convert"],
            [OperatorType.DetectionSequenceJudge] = ["序列判定"],
            [OperatorType.ImageAdd] = ["Image Add", "图像叠加"],
            [OperatorType.ImageCompose] = ["图像合成"],
            [OperatorType.ResultJudgment] = ["Result Judgment"],
            [OperatorType.ResultOutput] = ["Result Output"],
            [OperatorType.Thresholding] = ["阈值分割", "Thresholding"],
            [OperatorType.HttpRequest] = ["HTTP Request", "HTTP请求"],
            [OperatorType.ScriptOperator] = ["Script Operator"],
            [OperatorType.GeoMeasurement] = ["几何距离测量"],
            [OperatorType.TcpCommunication] = ["TCP通讯"],
            [OperatorType.ForEach] = ["循环处理"],
            [OperatorType.ArrayIndexer] = ["数组索引"],
            [OperatorType.JsonExtractor] = ["JSON提取"],
            [OperatorType.MathOperation] = ["数学运算"],
            [OperatorType.MqttPublish] = ["MQTT Publish", "MQTT发布"],
            [OperatorType.SiemensS7Communication] = ["西门子S7"]
        };

    [Fact]
    public void OperatorDtoType_ShouldWriteStableNameAndReadLegacyNumericValue()
    {
        var json = JsonSerializer.Serialize(new OperatorDto
        {
            Type = OperatorType.TemplateMatching,
            InputPorts =
            [
                new PortDto { Direction = PortDirection.Input, DataType = PortDataType.Image }
            ]
        });
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty(nameof(OperatorDto.Type)).GetString()
            .Should().Be(nameof(OperatorType.TemplateMatching));
        var port = document.RootElement.GetProperty(nameof(OperatorDto.InputPorts))[0];
        port.GetProperty(nameof(PortDto.Direction)).GetString().Should().Be(nameof(PortDirection.Input));
        port.GetProperty(nameof(PortDto.DataType)).GetString().Should().Be(nameof(PortDataType.Image));

        var legacy = JsonSerializer.Deserialize<OperatorDto>("""{"Type":7,"InputPorts":[{"Direction":0,"DataType":0}]}""");
        legacy!.Type.Should().Be(OperatorType.TemplateMatching);
        legacy.InputPorts.Single().Direction.Should().Be(PortDirection.Input);
        legacy.InputPorts.Single().DataType.Should().Be(PortDataType.Image);
    }

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
