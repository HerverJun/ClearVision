using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
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
    public void ManualAiCatalogs_ShouldUseRuntimeDisplayNames()
    {
        var runtimeNames = new OperatorFactory()
            .GetAllMetadata()
            .ToDictionary(item => item.Type, item => item.DisplayName);

        foreach (var item in VisionAgentReadOnlyCatalog.Operators)
        {
            Enum.TryParse<OperatorType>(item.OperatorType, out var operatorType).Should().BeTrue(item.OperatorType);
            item.DisplayName.Should().Be(runtimeNames[operatorType], item.OperatorType);
        }

#pragma warning disable CS0618
        var prompt = new AIPromptBuilder()
            .WithOperatorLibrary()
            .Build();
#pragma warning restore CS0618

        var promptNames = Regex.Matches(
                prompt,
                @"^- \*\*(?<name>.+?)\*\* \(`(?<type>[^`]+)`\):",
                RegexOptions.Multiline)
            .Cast<Match>()
            .ToDictionary(
                match => Enum.Parse<OperatorType>(match.Groups["type"].Value),
                match => match.Groups["name"].Value);

        promptNames.Should().NotBeEmpty();
        foreach (var (operatorType, displayName) in promptNames)
        {
            displayName.Should().Be(runtimeNames[operatorType], operatorType.ToString());
        }
    }

    [Fact]
    public void ActiveGeneratedCatalogsAndCards_ShouldMatchRuntimeNames()
    {
        var runtimeNames = new OperatorFactory()
            .GetAllMetadata()
            .ToDictionary(item => item.Type, item => item.DisplayName);
        runtimeNames.Should().HaveCount(158);

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

            operators.Should().HaveCount(runtimeNames.Count, catalogPath);
            foreach (var (operatorType, displayName) in runtimeNames)
            {
                operators[operatorType.ToString()]
                    .GetProperty("displayName")
                    .GetString()
                    .Should().Be(displayName, catalogPath);
            }
        }

        var markdownCatalogs = new[]
        {
            Path.Combine(RepoRoot, "docs", "算子资料", "算子目录.md"),
            Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", "CATALOG.md"),
            Path.Combine(RepoRoot, "docs", "CATALOG.md"),
            Path.Combine(RepoRoot, "docs", "OPERATOR_CATALOG.md"),
            Path.Combine(RepoRoot, "docs", "operators", "CATALOG.md"),
            Path.Combine(RepoRoot, "算子资料", "算子目录.md"),
            Path.Combine(RepoRoot, "算子资料", "算子名片", "CATALOG.md")
        };

        foreach (var markdownPath in markdownCatalogs)
        {
            var markdown = File.ReadAllText(markdownPath);
            foreach (var (operatorType, displayName) in runtimeNames)
            {
                markdown.Should().Contain($"`OperatorType.{operatorType}` | {displayName}", markdownPath);
            }
        }

        foreach (var (operatorType, displayName) in runtimeNames)
        {
            var cardPaths = new[]
            {
                Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", $"{operatorType}.md"),
                Path.Combine(RepoRoot, "docs", "operators", $"{operatorType}.md"),
                Path.Combine(RepoRoot, "算子资料", "算子名片", $"{operatorType}.md")
            };

            foreach (var cardPath in cardPaths)
            {
                File.ReadLines(cardPath).First().Should().StartWith($"# {displayName} / ", cardPath);
            }
        }

        AssertKnowledgeCards(
            Path.Combine(RepoRoot, "docs", "ai", "operator-knowledge", "operator_knowledge_cards.json"),
            root => root,
            runtimeNames);
        AssertKnowledgeCards(
            Path.Combine(RepoRoot, "docs", "ai", "operator-knowledge", "operator_knowledge_graph.json"),
            root => root.GetProperty("Cards"),
            runtimeNames);
    }

    [Fact]
    public void ActiveOperatorHandbook_ShouldUseCurrentNamingSemantics()
    {
        var activeRoots = new[]
        {
            Path.Combine(RepoRoot, "docs", "算子资料"),
            Path.Combine(RepoRoot, "算子资料")
        };
        var expectedHandbookMarkers = new[]
        {
            "#### Threshold（全局阈值处理）",
            "#### GeometricToleranceOperator（二维几何公差判定）",
            "#### RoiManagerOperator（ROI裁剪与掩膜）",
            "#### ColorDetectionOperator（颜色分析）",
            "#### TryCatchOperator（Try分支透传）",
            "#### ModbusCommunicationOperator（Modbus TCP通信）",
            "| CoordinateTransform | 像素到物理坐标（单点） |"
        };
        var staleHandbookClaims = new[]
        {
            "| **显示名称** | 二值化 |",
            "| **显示名称** | 几何公差 |",
            "| **显示名称** | ROI管理器 |",
            "| **显示名称** | 颜色检测 |",
            "| **显示名称** | 异常捕获 |",
            "| **显示名称** | Modbus通信 |",
            "| **功能描述** | Try-Catch流程控制，异常时切换到Catch分支 |",
            "| **功能描述** | Modbus TCP/RTU通信（带连接池） |"
        };

        foreach (var root in activeRoots)
        {
            var readme = File.ReadAllText(Path.Combine(root, "README.md"));
            readme.Should().Contain("- 正式算子：158 个。", root);
            readme.Should().Contain("- 运行时元数据：158 个", root);
            readme.Should().NotContain("正式算子：156 个", root);
            readme.Should().NotContain("运行时元数据：160 个", root);

            var handbook = File.ReadAllText(Path.Combine(root, "算子手册.md"));
            handbook.Should().Contain("当前正式口径包含 **158 个算子**", root);
            foreach (var marker in expectedHandbookMarkers)
            {
                handbook.Should().Contain(marker, root);
            }

            foreach (var staleClaim in staleHandbookClaims)
            {
                handbook.Should().NotContain(staleClaim, root);
            }
        }
    }

    [Fact]
    public async Task LegacyDisplayNames_ShouldRemainSearchableInAiOperatorCatalog()
    {
        var tool = new OperatorCatalogTool();

        foreach (var contract in Contracts)
        {
            foreach (var legacyName in LegacyNames(contract))
            {
                var result = await tool.ExecuteAsync(
                    new VisionAgentToolContext(),
                    JsonSerializer.SerializeToElement(new { keyword = legacyName, topN = 50 }),
                    CancellationToken.None);

                result.Success.Should().BeTrue(legacyName);
                var payload = JsonSerializer.SerializeToElement(result.Data);
                payload.GetProperty("operators")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("operatorType").GetString())
                    .Should()
                    .Contain(contract.OperatorType.ToString(), legacyName);
            }
        }

        var runtimeFactory = new OperatorFactory();
        foreach (var (operatorType, aliases) in CompatibilitySearchAliases)
        {
            var metadata = runtimeFactory.GetMetadata(operatorType);
            metadata.Should().NotBeNull();

            foreach (var alias in aliases)
            {
                metadata!.Keywords.Should().Contain(
                    keyword => string.Equals(keyword, alias, StringComparison.OrdinalIgnoreCase),
                    alias);

                var result = await tool.ExecuteAsync(
                    new VisionAgentToolContext(),
                    JsonSerializer.SerializeToElement(new { keyword = alias, topN = 50 }),
                    CancellationToken.None);

                result.Success.Should().BeTrue(alias);
                var payload = JsonSerializer.SerializeToElement(result.Data);
                payload.GetProperty("operators")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("operatorType").GetString())
                    .Should()
                    .Contain(operatorType.ToString(), alias);
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

        var json = JsonSerializer.Serialize(saved);
        var loadedDto = JsonSerializer.Deserialize<OperatorFlowDto>(json);
        loadedDto.Should().NotBeNull();
        var loaded = loadedDto!.ToEntity().Operators;

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

    [Fact]
    public async Task LegacyNamedFlow_ShouldExecuteAfterRoundTripUsingOperatorTypeIdentity()
    {
        var factory = new OperatorFactory();
        var created = factory.CreateOperator(OperatorType.RectangleRegion, "矩形区域", 12, 34);
        created.UpdateParameter("X", 7);
        created.UpdateParameter("Y", 9);
        created.UpdateParameter("Width", 40);
        created.UpdateParameter("Height", 22);

        var saved = new OperatorFlowDto
        {
            Name = "legacy-name-execution",
            Operators = [ToDto(created)]
        };
        var json = JsonSerializer.Serialize(saved);
        var loaded = JsonSerializer.Deserialize<OperatorFlowDto>(json)!.ToEntity();

        using var service = new FlowExecutionService(
            [new RectangleRegionOperator(NullLogger<RectangleRegionOperator>.Instance)],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext());

        var result = await service.ExecuteFlowAsync(loaded);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var operatorResult = result.OperatorResults.Should().ContainSingle().Subject;
        operatorResult.OperatorId.Should().Be(created.Id);
        operatorResult.OperatorName.Should().Be("矩形区域");
        var rectangle = operatorResult.OutputData!["Rectangle"]
            .Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        Convert.ToInt32(rectangle["X"]).Should().Be(7);
        Convert.ToInt32(rectangle["Y"]).Should().Be(9);
        Convert.ToInt32(rectangle["Width"]).Should().Be(40);
        Convert.ToInt32(rectangle["Height"]).Should().Be(22);
    }

    private static void AssertKnowledgeCards(
        string path,
        Func<JsonElement, JsonElement> selectCards,
        IReadOnlyDictionary<OperatorType, string> runtimeNames)
    {
        File.Exists(path).Should().BeTrue(path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cardsElement = selectCards(document.RootElement);
        cardsElement.GetArrayLength().Should().Be(158, path);
        var cards = cardsElement
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("OperatorType").GetString()!, StringComparer.Ordinal);

        foreach (var (operatorType, displayName) in runtimeNames)
        {
            cards[operatorType.ToString()]
                .GetProperty("DisplayName")
                .GetString()
                .Should().Be(displayName, path);
        }

        foreach (var contract in Contracts)
        {
            var card = cards[contract.OperatorType.ToString()];
            card.GetProperty("DisplayName").GetString().Should().Be(contract.DisplayName, path);
            var aliases = card.GetProperty("Aliases")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => item != null)
                .ToList();
            foreach (var legacyName in LegacyNames(contract))
            {
                aliases.Should().Contain(
                    alias => string.Equals(alias, legacyName, StringComparison.OrdinalIgnoreCase),
                    path);
            }
        }

        foreach (var (operatorType, legacyNames) in CompatibilitySearchAliases)
        {
            var aliases = cards[operatorType.ToString()]
                .GetProperty("Aliases")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => item != null)
                .ToList();

            foreach (var legacyName in legacyNames)
            {
                aliases.Should().Contain(
                    alias => string.Equals(alias, legacyName, StringComparison.OrdinalIgnoreCase),
                    path);
            }
        }
    }

    private static IEnumerable<string> LegacyNames(NamingContract contract)
    {
        yield return contract.LegacyDisplayName;
        foreach (var additionalLegacyName in contract.AdditionalLegacyNames)
        {
            yield return additionalLegacyName;
        }
    }

    private static OperatorDto ToDto(Operator source)
    {
        return new OperatorDto
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
    }

    private static string PortIdentity(ClearVision.Product.Core.ValueObjects.Port port)
    {
        return $"{port.Id:N}|{port.Name}|{port.Direction}|{port.DataType}|{port.IsRequired}";
    }

    private static string ParameterIdentity(ClearVision.Product.Core.ValueObjects.Parameter parameter)
    {
        return $"{parameter.Id:N}|{parameter.Name}|{parameter.DisplayName}|{parameter.DataType}|{parameter.IsRequired}";
    }

    private static void AssertStableShape(int inputs, int outputs, int parameters, NamingContract contract)
    {
        inputs.Should().Be(contract.InputPortCount, contract.OperatorType.ToString());
        outputs.Should().Be(contract.OutputPortCount, contract.OperatorType.ToString());
        parameters.Should().Be(contract.ParameterCount, contract.OperatorType.ToString());
    }

    private static string ResolveRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = !string.IsNullOrWhiteSpace(sourceFilePath)
            ? new FileInfo(sourceFilePath).Directory
            : new DirectoryInfo(AppContext.BaseDirectory);

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
