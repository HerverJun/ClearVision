using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class OperatorNamingSemanticContractTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

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
    public void SourceRuntimeAndAiCatalog_ShouldExposeTheSameCorrectedDisplayNames()
    {
        var scanned = new OperatorMetadataScanner().Scan();
        var runtimeFactory = new OperatorFactory();
        var aiCatalog = new VisionAgentOperatorContractCatalog(runtimeFactory);

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
            AssertStableShape(scannedMetadata.InputPorts.Count, scannedMetadata.OutputPorts.Count, scannedMetadata.Parameters.Count, contract);

            var runtimeMetadata = runtimeFactory.GetMetadata(contract.OperatorType);
            runtimeMetadata.Should().NotBeNull();
            runtimeMetadata!.DisplayName.Should().Be(contract.DisplayName);
            runtimeMetadata.Keywords.Should().Contain(contract.LegacyDisplayName);
            AssertStableShape(runtimeMetadata.InputPorts.Count, runtimeMetadata.OutputPorts.Count, runtimeMetadata.Parameters.Count, contract);

            aiCatalog.TryGet(contract.OperatorType.ToString(), out var aiContract).Should().BeTrue();
            aiContract.DisplayName.Should().Be(contract.DisplayName);
        }
    }

    [Fact]
    public void ActiveGeneratedCatalogsAndCards_ShouldMatchRuntimeNames()
    {
        var catalogPaths = new[]
        {
            Path.Combine(RepoRoot, "docs", "算子资料", "算子目录.json"),
            Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", "catalog.json"),
            Path.Combine(RepoRoot, "docs", "operators", "catalog.json"),
            Path.Combine(RepoRoot, "算子资料", "算子目录.json"),
            Path.Combine(RepoRoot, "算子资料", "算子名片", "catalog.json")
        };

        foreach (var catalogPath in catalogPaths)
        {
            File.Exists(catalogPath).Should().BeTrue(catalogPath);
            using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(158);
            var operators = document.RootElement.GetProperty("operators")
                .EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetString()!, StringComparer.Ordinal);

            foreach (var contract in Contracts)
            {
                operators[contract.OperatorType.ToString()]
                    .GetProperty("displayName")
                    .GetString()
                    .Should().Be(contract.DisplayName, catalogPath);
            }
        }

        var markdownCatalogs = new[]
        {
            Path.Combine(RepoRoot, "docs", "算子资料", "算子目录.md"),
            Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", "CATALOG.md"),
            Path.Combine(RepoRoot, "算子资料", "算子目录.md"),
            Path.Combine(RepoRoot, "算子资料", "算子名片", "CATALOG.md")
        };

        foreach (var markdownPath in markdownCatalogs)
        {
            var markdown = File.ReadAllText(markdownPath);
            foreach (var contract in Contracts)
            {
                markdown.Should().Contain($"`OperatorType.{contract.OperatorType}` | {contract.DisplayName}", markdownPath);
            }
        }

        foreach (var contract in Contracts)
        {
            var cardPaths = new[]
            {
                Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", $"{contract.OperatorType}.md"),
                Path.Combine(RepoRoot, "docs", "operators", $"{contract.OperatorType}.md"),
                Path.Combine(RepoRoot, "算子资料", "算子名片", $"{contract.OperatorType}.md")
            };

            foreach (var cardPath in cardPaths)
            {
                File.ReadLines(cardPath).First().Should().StartWith($"# {contract.DisplayName} / ", cardPath);
            }
        }
    }

    [Fact]
    public void CreatingNodesWithLegacyNames_ShouldPreserveTheirPersistedIdentity()
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

    private static void AssertStableShape(int inputs, int outputs, int parameters, NamingContract contract)
    {
        inputs.Should().Be(contract.InputPortCount, contract.OperatorType.ToString());
        outputs.Should().Be(contract.OutputPortCount, contract.OperatorType.ToString());
        parameters.Should().Be(contract.ParameterCount, contract.OperatorType.ToString());
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
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
