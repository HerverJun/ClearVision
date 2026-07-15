using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Services;

public sealed class OperatorProductMetadataGovernanceTests
{
    private const string ExpectedIdentityHash = "BBAC47F5DF83110BDE52DDA64C01F7335377D9DFCEB17D2B78A8A77F5262A14F";
    private static readonly string RepoRoot = ResolveRepoRoot();

    private static readonly IReadOnlyDictionary<OperatorCategoryId, int> ExpectedCategoryCounts =
        new Dictionary<OperatorCategoryId, int>
        {
            [OperatorCategoryId.Acquisition] = 1,
            [OperatorCategoryId.ImagePreprocessing] = 28,
            [OperatorCategoryId.SegmentationAndRegion] = 17,
            [OperatorCategoryId.FeatureExtraction] = 13,
            [OperatorCategoryId.MatchingAndLocalization] = 17,
            [OperatorCategoryId.DefectDetection] = 4,
            [OperatorCategoryId.Measurement] = 17,
            [OperatorCategoryId.CalibrationAndCoordinates] = 12,
            [OperatorCategoryId.AiInference] = 4,
            [OperatorCategoryId.PointCloud3D] = 6,
            [OperatorCategoryId.DataProcessing] = 18,
            [OperatorCategoryId.FlowControl] = 8,
            [OperatorCategoryId.Communication] = 8,
            [OperatorCategoryId.OutputAndAuxiliary] = 5
        };

    private static readonly IReadOnlyDictionary<OperatorType, OperatorLifecycle> NonStableLifecycles =
        new Dictionary<OperatorType, OperatorLifecycle>
        {
            [OperatorType.AnomalyDetection] = OperatorLifecycle.Experimental,
            [OperatorType.ColorDetection] = OperatorLifecycle.Experimental,
            [OperatorType.DetectionSequenceJudge] = OperatorLifecycle.Experimental,
            [OperatorType.LocalDeformableMatching] = OperatorLifecycle.Experimental,
            [OperatorType.SurfaceDefectDetection] = OperatorLifecycle.Experimental,
            [OperatorType.SubpixelEdgeDetection] = OperatorLifecycle.Reference,
            [OperatorType.MqttPublish] = OperatorLifecycle.Reference,
            [OperatorType.Morphology] = OperatorLifecycle.Legacy
        };

    [Fact]
    public void AllFormalOperators_ShouldUseTheFixedCategoryCatalogAndUniqueDisplayNames()
    {
        var metadata = new OperatorFactory().GetAllMetadata().OrderBy(item => item.Type).ToList();

        metadata.Should().HaveCount(158);
        OperatorCategoryCatalog.All.Should().HaveCount(14);
        OperatorCategoryCatalog.All.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        OperatorCategoryCatalog.All.Select(item => item.DisplayName).Should().OnlyHaveUniqueItems();
        OperatorCategoryCatalog.All.Select(item => item.Order).Should().Equal(Enumerable.Range(1, 14));

        metadata.Select(item => item.DisplayName).Should().OnlyHaveUniqueItems();
        metadata.Should().OnlyContain(item =>
            Enum.IsDefined(item.CategoryId) &&
            item.Category == OperatorCategoryCatalog.GetDisplayName(item.CategoryId));

        metadata
            .GroupBy(item => item.CategoryId)
            .ToDictionary(group => group.Key, group => group.Count())
            .Should()
            .BeEquivalentTo(ExpectedCategoryCounts);

        metadata.Where(item => item.CategoryId == OperatorCategoryId.AiInference)
            .Select(item => item.Type)
            .Should()
            .BeEquivalentTo(new[]
            {
                OperatorType.AnomalyDetection,
                OperatorType.DeepLearning,
                OperatorType.OcrRecognition,
                OperatorType.SemanticSegmentation
            });

        metadata.Where(item => item.CategoryId == OperatorCategoryId.DefectDetection)
            .Select(item => item.Type)
            .Should()
            .BeEquivalentTo(new[]
            {
                OperatorType.EdgePairDefect,
                OperatorType.SurfaceDefectDetection,
                OperatorType.DualModalVoting,
                OperatorType.DetectionSequenceJudge
            });
    }

    [Fact]
    public async Task RuntimeApplicationAndAiCatalogs_ShouldProjectTheSameEffectiveMetadata()
    {
        var factory = new OperatorFactory();
        var runtime = factory.GetAllMetadata().ToDictionary(item => item.Type);
        var scanned = new OperatorMetadataScanner().Scan().ToDictionary(item => item.Type);
        var service = new OperatorService(Substitute.For<IOperatorRepository>(), factory);
        var application = (await service.GetLibraryAsync())
            .ToDictionary(item => Enum.Parse<OperatorType>(item.Type));
        var aiContracts = new VisionAgentOperatorContractCatalog(factory).Operators
            .ToDictionary(item => Enum.Parse<OperatorType>(item.OperatorType));

        scanned.Should().HaveCount(runtime.Count);
        application.Should().HaveCount(runtime.Count);
        aiContracts.Should().HaveCount(runtime.Count);
        VisionAgentReadOnlyCatalog.Operators.Should().HaveCount(runtime.Count);
        VisionAgentReadOnlyCatalog.Schemas.Should().HaveCount(runtime.Count);

        foreach (var (type, metadata) in runtime)
        {
            var source = scanned[type];
            source.DisplayName.Should().Be(metadata.DisplayName, type.ToString());
            source.CategoryId.Should().Be(metadata.CategoryId, type.ToString());
            source.Category.Should().Be(metadata.Category, type.ToString());
            source.Lifecycle.Should().Be(metadata.Lifecycle, type.ToString());

            var dto = application[type];
            dto.DisplayName.Should().Be(metadata.DisplayName, type.ToString());
            dto.CategoryId.Should().Be(metadata.CategoryId.ToString(), type.ToString());
            dto.CategoryOrder.Should().Be(OperatorCategoryCatalog.GetOrder(metadata.CategoryId), type.ToString());
            dto.Category.Should().Be(metadata.Category, type.ToString());
            dto.Lifecycle.Should().Be(metadata.Lifecycle.ToString(), type.ToString());
            dto.DefaultHidden.Should().Be(metadata.DefaultHidden, type.ToString());
            dto.Inputs.Select(item => item.Name).Should().Equal(metadata.InputPorts.Select(item => item.Name));
            dto.Outputs.Select(item => item.Name).Should().Equal(metadata.OutputPorts.Select(item => item.Name));
            dto.Parameters.Select(item => item.Name).Should().Equal(metadata.Parameters.Select(item => item.Name));
            dto.ParameterConstraints.Should().BeEquivalentTo(metadata.ParameterConstraints);
            dto.OutputAvailabilityRules.Should().BeEquivalentTo(metadata.OutputAvailabilityRules);
            dto.ImageInputContracts.Should().BeEquivalentTo(metadata.ImageInputContracts);
            dto.ImageInputContractPresentations.Should().BeEquivalentTo(metadata.ImageInputContractPresentations);

            var ai = aiContracts[type];
            ai.DisplayName.Should().Be(metadata.DisplayName, type.ToString());
            ai.CategoryId.Should().Be(metadata.CategoryId, type.ToString());
            ai.CategoryOrder.Should().Be(OperatorCategoryCatalog.GetOrder(metadata.CategoryId), type.ToString());
            ai.Category.Should().Be(metadata.Category, type.ToString());
            ai.Lifecycle.Should().Be(metadata.Lifecycle, type.ToString());
            ai.DefaultHidden.Should().Be(metadata.DefaultHidden, type.ToString());
            ai.DefaultAiRecommendation.Should().Be(
                ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                    metadata.Lifecycle,
                    metadata.ImageInputContracts),
                type.ToString());
            ai.RequiresLifecycleDisclosure.Should().Be(
                ImageContractPresentationBuilder.RequiresAiDisclosure(
                    metadata.Lifecycle,
                    metadata.ImageInputContracts),
                type.ToString());
            ai.InputPorts.Select(item => item.Name).Should().Equal(metadata.InputPorts.Select(item => item.Name));
            ai.OutputPorts.Select(item => item.Name).Should().Equal(metadata.OutputPorts.Select(item => item.Name));
            ai.Parameters.Select(item => item.Name).Should().Equal(metadata.Parameters.Select(item => item.Name));
            ai.ParameterConstraints.Should().BeEquivalentTo(metadata.ParameterConstraints);
            ai.OutputAvailabilityRules.Should().BeEquivalentTo(metadata.OutputAvailabilityRules);
            ai.ImageInputContracts.Should().BeEquivalentTo(metadata.ImageInputContracts);

            var schema = VisionAgentReadOnlyCatalog.Schemas[type.ToString()];
            schema.Metadata.Type.Should().Be(metadata.Type);
            schema.CategoryId.Should().Be(metadata.CategoryId);
            schema.Lifecycle.Should().Be(metadata.Lifecycle);
            schema.InputPorts.Should().Equal(metadata.InputPorts.Select(item => item.Name));
            schema.OutputPorts.Should().Equal(metadata.OutputPorts.Select(item => item.Name));
            schema.Parameters.Select(item => item.Name).Should().Equal(metadata.Parameters.Select(item => item.Name));
            schema.ParameterConstraints.Should().BeEquivalentTo(metadata.ParameterConstraints);
            schema.OutputAvailabilityRules.Should().BeEquivalentTo(metadata.OutputAvailabilityRules);
            schema.ImageInputContracts.Should().BeEquivalentTo(metadata.ImageInputContracts);
        }
    }

    [Fact]
    public void LocalizationAndServiceAdapters_ShouldNotContainIdentityOverrideTables()
    {
        var localization = File.ReadAllText(Path.Combine(
            RepoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Services",
            "OperatorMetadataLocalization.cs"));
        localization.Should().NotContain("LegacyDisplayMap");
        localization.Should().NotContain("LocalizedMetadata");
        localization.Should().NotContain("metadata.DisplayName =");
        localization.Should().NotContain("metadata.Category =");

        var textLocalization = File.ReadAllText(Path.Combine(
            RepoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Services",
            "OperatorMetadataTextLocalization.cs"));
        textLocalization.Should().NotContain("metadata.DisplayName =");
        textLocalization.Should().NotContain("metadata.Description =");
        textLocalization.Should().NotContain("metadata.Category =");

        var operatorService = File.ReadAllText(Path.Combine(
            RepoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Application",
            "Services",
            "OperatorService.cs"));
        operatorService.Should().NotContain("FactoryAuthoritativeTypes");
        operatorService.Should().NotContain("OperatorType.Filtering,");
        operatorService.Should().NotContain("OperatorType.Measurement,");
        operatorService.Should().NotContain("OperatorType.DeepLearning,");

        var readOnlyCatalog = File.ReadAllText(Path.Combine(
            RepoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "Tools",
            "VisionAgentReadOnlyCatalog.cs"));
        readOnlyCatalog.Should().Contain("ContractCatalog.Operators");
        readOnlyCatalog.Should().NotContain("new Dictionary<string, OperatorSchemaItem>");
    }

    [Fact]
    public async Task LifecyclePolicy_ShouldHideCompatibilityOperatorsAndDiscloseNonStableOperators()
    {
        var factory = new OperatorFactory();
        var metadata = factory.GetAllMetadata().ToDictionary(item => item.Type);

        foreach (var item in metadata.Values)
        {
            var expected = NonStableLifecycles.GetValueOrDefault(item.Type, OperatorLifecycle.Stable);
            item.Lifecycle.Should().Be(expected, item.Type.ToString());
            item.DefaultHidden.Should().Be(expected is OperatorLifecycle.Legacy or OperatorLifecycle.Deprecated);
            OperatorLifecyclePolicy.RequiresDisclosure(expected).Should().Be(expected != OperatorLifecycle.Stable);
        }

        metadata.Values.Should().NotContain(item => item.Lifecycle == OperatorLifecycle.Deprecated);
        metadata[OperatorType.Morphology].DefaultHidden.Should().BeTrue();
        metadata[OperatorType.SubpixelEdgeDetection].DefaultHidden.Should().BeFalse();
        OperatorLifecyclePolicy.IsDefaultAiRecommendation(OperatorLifecycle.Reference).Should().BeFalse();
        OperatorLifecyclePolicy.IsDefaultAiRecommendation(OperatorLifecycle.Legacy).Should().BeFalse();

        var tool = new OperatorCatalogTool();
        var defaultResult = await tool.ExecuteAsync(
            new VisionAgentToolContext(),
            JsonSerializer.SerializeToElement(new { keyword = "形态学（旧版）", topN = 50 }),
            CancellationToken.None);
        GetOperatorTypes(defaultResult).Should().NotContain(nameof(OperatorType.Morphology));

        var compatibilityResult = await tool.ExecuteAsync(
            new VisionAgentToolContext(),
            JsonSerializer.SerializeToElement(new { keyword = "形态学（旧版）", topN = 50, includeCompatibility = true }),
            CancellationToken.None);
        GetOperatorTypes(compatibilityResult).Should().Contain(nameof(OperatorType.Morphology));

        var referenceResult = await tool.ExecuteAsync(
            new VisionAgentToolContext(),
            JsonSerializer.SerializeToElement(new { keyword = "亚像素边缘", topN = 50 }),
            CancellationToken.None);
        var referencePayload = JsonSerializer.SerializeToElement(referenceResult.Data);
        var reference = referencePayload.GetProperty("operators")
            .EnumerateArray()
            .Single(item => item.GetProperty("operatorType").GetString() == nameof(OperatorType.SubpixelEdgeDetection));
        reference.GetProperty("defaultAiRecommendation").GetBoolean().Should().BeFalse();
        reference.GetProperty("requiresLifecycleDisclosure").GetBoolean().Should().BeTrue();

        var compatibilityOnlyMetadata = metadata.Values
            .First(item => ImageContractPresentationBuilder.Summarize(item.ImageInputContracts).CompatibilityOnly);
        var compatibilityOnlyResult = await tool.ExecuteAsync(
            new VisionAgentToolContext(),
            JsonSerializer.SerializeToElement(new
            {
                keyword = compatibilityOnlyMetadata.Type.ToString(),
                topN = 50
            }),
            CancellationToken.None);
        var compatibilityOnlyPayload = JsonSerializer.SerializeToElement(compatibilityOnlyResult.Data);
        var compatibilityOnly = compatibilityOnlyPayload.GetProperty("operators")
            .EnumerateArray()
            .Single(item => item.GetProperty("operatorType").GetString() == compatibilityOnlyMetadata.Type.ToString());
        compatibilityOnly.GetProperty("defaultAiRecommendation").GetBoolean().Should().BeFalse();
        compatibilityOnly.GetProperty("requiresLifecycleDisclosure").GetBoolean().Should().BeTrue();
        var imageContractSummary = compatibilityOnly.GetProperty("imageContract");
        imageContractSummary.GetProperty("CompatibilityOnly").GetBoolean().Should().BeTrue();
        imageContractSummary.GetProperty("HasProductionSupport").GetBoolean().Should().BeFalse();
        imageContractSummary.GetProperty("EvidenceSummary").GetString()
            .Should().Be(ImageContractPresentationBuilder.LegacyCompatibilityNotice);

#pragma warning disable CS0618
        var compatibilityPrompt = new AIPromptBuilder()
            .WithOperatorLibrary()
            .Build();
#pragma warning restore CS0618
        compatibilityPrompt.Should().Contain("`SubpixelEdgeDetection`");
        compatibilityPrompt.Should().Contain("生命周期=Reference");
        compatibilityPrompt.Should().Contain("`ColorDetection`");
        compatibilityPrompt.Should().Contain("生命周期=Experimental");
        compatibilityPrompt.Should().NotContain("`Morphology`");

        factory.CreateOperator(OperatorType.Morphology, "旧版形态学", 1, 2).Type
            .Should().Be(OperatorType.Morphology);
    }

    [Fact]
    public void LegacyOperatorFlow_ShouldRoundTripWithoutIdentityOrContractDrift()
    {
        var factory = new OperatorFactory();
        var created = factory.CreateOperator(OperatorType.Morphology, "旧版形态学", 12, 34);
        var flow = new OperatorFlowDto
        {
            Name = "legacy-morphology-roundtrip",
            Operators = [ToDto(created)]
        };

        var json = JsonSerializer.Serialize(flow);
        var loaded = JsonSerializer.Deserialize<OperatorFlowDto>(json)!.ToEntity().Operators.Single();

        loaded.Id.Should().Be(created.Id);
        loaded.Type.Should().Be(created.Type);
        loaded.Name.Should().Be(created.Name);
        loaded.InputPorts.Select(PortIdentity).Should().Equal(created.InputPorts.Select(PortIdentity));
        loaded.OutputPorts.Select(PortIdentity).Should().Equal(created.OutputPorts.Select(PortIdentity));
        loaded.Parameters.Select(ParameterIdentity).Should().Equal(created.Parameters.Select(ParameterIdentity));
    }

    [Fact]
    public async Task LegacyOperatorFlow_ShouldExecuteAfterRoundTripUsingOperatorTypeIdentity()
    {
        var factory = new OperatorFactory();
        var created = factory.CreateOperator(OperatorType.Morphology, "旧版形态学", 12, 34);
        var flow = new OperatorFlowDto
        {
            Name = "legacy-morphology-execution",
            Operators = [ToDto(created)]
        };
        var loaded = JsonSerializer.Deserialize<OperatorFlowDto>(JsonSerializer.Serialize(flow))!.ToEntity();
        var loadedOperator = loaded.Operators.Single();

        using var source = new Mat(5, 5, MatType.CV_8UC1, Scalar.Black);
        using var image = new ImageWrapper(source.Clone());
        using var service = new FlowExecutionService(
            [new MorphologyOperator(NullLogger<MorphologyOperator>.Instance)],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext());

        var result = await service.ExecuteFlowAsync(
            loaded,
            new Dictionary<string, object> { ["Image"] = image });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        loadedOperator.Id.Should().Be(created.Id);
        loadedOperator.Type.Should().Be(OperatorType.Morphology);
        loadedOperator.Name.Should().Be("旧版形态学");
        var operatorResult = result.OperatorResults.Should().ContainSingle().Subject;
        operatorResult.OperatorId.Should().Be(created.Id);
        operatorResult.OperatorName.Should().Be("旧版形态学");
        operatorResult.OutputData.Should().ContainKey("Image");
        operatorResult.OutputData.Should().ContainKey("LegacyCompatible")
            .WhoseValue.Should().Be(true);
    }

    [Fact]
    public void EveryConditionContract_ShouldReferenceExistingParametersPortsAndOutputs()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToList();

        foreach (var item in metadata)
        {
            var parameterNames = item.Parameters.Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inputNames = item.InputPorts.Select(port => port.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outputNames = item.OutputPorts.Select(port => port.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var constraint in item.ParameterConstraints)
            {
                if (string.IsNullOrWhiteSpace(constraint.AliasFor))
                {
                    parameterNames.Should().Contain(constraint.Parameter, item.Type.ToString());
                }
                else
                {
                    parameterNames.Should().Contain(
                        constraint.AliasFor,
                        $"{item.Type}:{constraint.Parameter} must alias a real canonical parameter");
                }

                foreach (var condition in EnumerateConditions(constraint.RequiredWhen)
                             .Concat(EnumerateConditions(constraint.EnabledWhen))
                             .Concat(EnumerateConditions(constraint.DisabledWhen))
                             .Concat(EnumerateConditions(constraint.VisibleWhen))
                             .Concat(EnumerateConditions(constraint.HiddenWhen))
                             .Concat(EnumerateConditions(constraint.IgnoredWhen)))
                {
                    parameterNames.Should().Contain(condition.Parameter, $"{item.Type}:{constraint.Parameter}");
                }

                foreach (var inputPort in constraint.SatisfiedByInputPorts ?? [])
                {
                    inputNames.Should().Contain(inputPort, $"{item.Type}:{constraint.Parameter}");
                }
            }

            foreach (var rule in item.OutputAvailabilityRules)
            {
                outputNames.Should().Contain(rule.Output, item.Type.ToString());
                foreach (var condition in EnumerateConditions(rule.AvailableWhen))
                {
                    parameterNames.Should().Contain(condition.Parameter, $"{item.Type}:{rule.Output}");
                }
            }
        }

        foreach (var type in new[] { OperatorType.Filtering, OperatorType.Measurement, OperatorType.DeepLearning })
        {
            var item = metadata.Single(metadata => metadata.Type == type);
            item.ParameterConstraints.Should().NotBeEmpty(type.ToString());
            item.OutputAvailabilityRules.Should().NotBeEmpty(type.ToString());
        }
    }

    [Fact]
    public void OperatorIdentitySnapshot_ShouldRemainStable()
    {
        var metadata = new OperatorFactory().GetAllMetadata().OrderBy(item => item.Type).ToList();
        var lines = Enum.GetNames<OperatorType>()
            .Select(name => $"enum|{name}|{(int)Enum.Parse<OperatorType>(name)}")
            .Concat(metadata.Select(item =>
                $"operator|{(int)item.Type}|{item.Type}" +
                $"|inputs:{string.Join(',', item.InputPorts.Select(port => port.Name))}" +
                $"|outputs:{string.Join(',', item.OutputPorts.Select(port => port.Name))}" +
                $"|parameters:{string.Join(',', item.Parameters.Select(parameter => parameter.Name))}"));
        var snapshot = string.Join("\n", lines);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

        hash.Should().Be(ExpectedIdentityHash);
    }

    [Fact]
    public void GenerationFingerprint_ShouldReactOnlyToEffectiveMetadataAndDeclaredDependencies()
    {
        var metadata = new OperatorMetadata
        {
            Type = OperatorType.Comment,
            DisplayName = "注释",
            Description = "test",
            CategoryId = OperatorCategoryId.OutputAndAuxiliary,
            Category = OperatorCategoryCatalog.GetDisplayName(OperatorCategoryId.OutputAndAuxiliary),
            Lifecycle = OperatorLifecycle.Stable,
            GenerationDependencies = ["type:Tests.SharedHelper"]
        };
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type:Tests.SharedHelper"] = "helper-v1",
            ["type:Tests.UnrelatedHelper"] = "unrelated-v1"
        };

        var baseline = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v1", dependencies);
        var sameWithUnrelatedChange = OperatorGenerationFingerprintBuilder.Compute(
            metadata,
            "operator-v1",
            new Dictionary<string, string>(dependencies, StringComparer.Ordinal)
            {
                ["type:Tests.UnrelatedHelper"] = "unrelated-v2"
            });
        var helperChanged = OperatorGenerationFingerprintBuilder.Compute(
            metadata,
            "operator-v1",
            new Dictionary<string, string>(dependencies, StringComparer.Ordinal)
            {
                ["type:Tests.SharedHelper"] = "helper-v2"
            });
        var sourceChanged = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v2", dependencies);
        metadata.DisplayName = "注释节点";
        var metadataChanged = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v1", dependencies);

        sameWithUnrelatedChange.Should().Be(baseline);
        helperChanged.Should().NotBe(baseline);
        sourceChanged.Should().NotBe(baseline);
        metadataChanged.Should().NotBe(baseline);
    }

    [Fact]
    public void GenerationFingerprint_ShouldReactToExactImageCombinationAndVerificationChanges()
    {
        var contract = new ImageInputContract(
            "Image",
            ["CV_64F"],
            [1],
            ["CV_64F"],
            "Exact input contract",
            "None",
            "Preserve",
            "Preserve",
            [
                new ImageContractVariant(
                    "Fixed",
                    "CV_64F",
                    1,
                    "Fixed threshold",
                    ImageContractAdmission.Allowed,
                    ImageContractVerification.VerifiedSupport,
                    "None",
                    "Preserve",
                    "Preserve",
                    ImageContractInputValuePolicy.Any,
                    "IMAGE_DEPTH_UNSUPPORTED",
                    "E2_STAGE2_RUNTIME")
            ],
            "RejectNonFinite",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion);
        var metadata = new OperatorMetadata
        {
            Type = OperatorType.Comment,
            DisplayName = "fingerprint-image-contract",
            Description = "test",
            CategoryId = OperatorCategoryId.OutputAndAuxiliary,
            Category = OperatorCategoryCatalog.GetDisplayName(OperatorCategoryId.OutputAndAuxiliary),
            Lifecycle = OperatorLifecycle.Stable,
            ImageInputContracts = [contract]
        };

        var baseline = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v1");
        metadata.ImageInputContracts =
        [
            contract with
            {
                SupportedChannels = [3],
                Variants = [contract.Variants.Single() with { Channels = 3 }]
            }
        ];
        var combinationChanged = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v1");
        metadata.ImageInputContracts =
        [
            contract with
            {
                Variants =
                [
                    contract.Variants.Single() with
                    {
                        Admission = ImageContractAdmission.Rejected,
                        Verification = ImageContractVerification.VerifiedRejection
                    }
                ]
            }
        ];
        var verificationChanged = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v1");
        metadata.ImageInputContracts =
        [
            contract with
            {
                Variants = [contract.Variants.Single() with { Condition = "Different condition" }]
            }
        ];
        var conditionChanged = OperatorGenerationFingerprintBuilder.Compute(metadata, "operator-v1");

        combinationChanged.Should().NotBe(baseline);
        verificationChanged.Should().NotBe(baseline);
        conditionChanged.Should().NotBe(baseline);
        combinationChanged.Should().NotBe(verificationChanged);
    }

    [Fact]
    public async Task OperatorSchemaTool_ShouldReturnCompactCompleteExactImageContract()
    {
        var result = await new OperatorSchemaTool().ExecuteAsync(
            new VisionAgentToolContext(),
            JsonSerializer.SerializeToElement(new { operatorType = nameof(OperatorType.SharpnessEvaluation) }),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var json = JsonSerializer.Serialize(result.Data);
        json.Length.Should().BeLessThanOrEqualTo(new VisionAgentToolContext().MaxToolResultChars);
        json.Should().Contain("\"ModeGroups\"");
        json.Should().Contain("\"Modes\"");
        json.Should().Contain("\"Cases\"");
        json.Should().Contain("\"Inputs\"");
        json.Should().NotContain("\"Depth\"");
        json.Should().NotContain("\"Channels\"");
        json.Should().Contain("VerifiedSupport");
        json.Should().Contain("VerifiedRejection");

        var detailResult = await new OperatorSchemaTool().ExecuteAsync(
            new VisionAgentToolContext(),
            JsonSerializer.SerializeToElement(new
            {
                operatorType = nameof(OperatorType.SharpnessEvaluation),
                imageMode = "Laplacian:PerMethodDefault:FullOverlay"
            }),
            CancellationToken.None);
        detailResult.Success.Should().BeTrue(detailResult.ErrorMessage);
        var detailJson = JsonSerializer.Serialize(detailResult.Data);
        detailJson.Length.Should().BeLessThanOrEqualTo(new VisionAgentToolContext().MaxToolResultChars);
        detailJson.Should().Contain("\"Variants\"");
        detailJson.Should().Contain("\"When\"");
        detailJson.Should().Contain("\"Convert\"");
        detailJson.Should().Contain("\"Output\"");
        detailJson.Should().Contain("\"Range\"");
        detailJson.Should().Contain("\"Failure\"");
        detailJson.Should().Contain("\"Evidence\"");
    }

    [Theory]
    [InlineData("type:ClearVision.Product.Infrastructure.Operators.SpatialFilterKernel", "Filtering,MedianBlur,BilateralFilter,MeanFilter")]
    [InlineData("type:ClearVision.Product.Infrastructure.Operators.MeasurementGeometryHelper", "Measurement")]
    [InlineData("type:ClearVision.Product.Infrastructure.Operators.DeepLearningTaskResolver", "DeepLearning")]
    [InlineData("type:ClearVision.Product.Infrastructure.Operators.SemanticSegmentationOperator", "DeepLearning")]
    [InlineData("type:ClearVision.Product.Infrastructure.Services.DeepLearningLabelResolver", "DeepLearning")]
    public void SharedDependencyChange_ShouldOnlyAffectExplicitlyDependentOperatorFingerprints(
        string dependencyId,
        string expectedOperators)
    {
        var metadata = new OperatorFactory().GetAllMetadata().OrderBy(item => item.Type).ToList();
        var dependencySources = metadata
            .SelectMany(item => item.GenerationDependencies)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(item => item, item => $"source:{item}:v1", StringComparer.Ordinal);
        var baseline = metadata.ToDictionary(
            item => item.Type,
            item => OperatorGenerationFingerprintBuilder.Compute(
                item,
                $"operator-source:{item.Type}",
                dependencySources));

        dependencySources.Should().ContainKey(dependencyId);
        var changedDependencySources = new Dictionary<string, string>(dependencySources, StringComparer.Ordinal)
        {
            [dependencyId] = dependencySources[dependencyId] + "\n// shared helper changed"
        };
        var explicitlyDependentOperators = metadata
            .Where(item => item.GenerationDependencies.Contains(dependencyId, StringComparer.Ordinal))
            .Select(item => item.Type)
            .ToList();
        var changedOperators = metadata
            .Where(item => OperatorGenerationFingerprintBuilder.Compute(
                item,
                $"operator-source:{item.Type}",
                changedDependencySources) != baseline[item.Type])
            .Select(item => item.Type)
            .ToList();

        explicitlyDependentOperators.Should().Equal(
            expectedOperators.Split(',').Select(Enum.Parse<OperatorType>));
        changedOperators.Should().Equal(explicitlyDependentOperators);
    }

    [Fact]
    public void GeneratedCatalogsCardsAndKnowledgeGraph_ShouldMatchRuntimeMetadata()
    {
        var runtime = new OperatorFactory().GetAllMetadata().ToDictionary(item => item.Type.ToString());
        Dictionary<string, string>? expectedFingerprints = null;
        string? expectedCatalogFingerprint = null;
        var catalogPaths = new[]
        {
            Path.Combine(RepoRoot, "docs", "算子资料", "算子目录.json"),
            Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", "catalog.json"),
            Path.Combine(RepoRoot, "docs", "operators", "catalog.json"),
            Path.Combine(RepoRoot, "算子资料", "算子目录.json"),
            Path.Combine(RepoRoot, "算子资料", "算子名片", "catalog.json")
        };

        foreach (var path in catalogPaths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            root.GetProperty("totalCount").GetInt32().Should().Be(158, path);
            var catalogFingerprint = root.GetProperty("generationFingerprint").GetString();
            catalogFingerprint.Should().NotBeNullOrWhiteSpace(path);
            if (expectedCatalogFingerprint is null)
            {
                expectedCatalogFingerprint = catalogFingerprint;
            }
            else
            {
                catalogFingerprint.Should().Be(expectedCatalogFingerprint, path);
            }
            root.GetProperty("categories").EnumerateObject().Should().HaveCount(14, path);
            var operators = root.GetProperty("operators").EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetString()!, StringComparer.Ordinal);
            operators.Should().HaveCount(runtime.Count, path);
            var fingerprints = operators.ToDictionary(
                item => item.Key,
                item => item.Value.GetProperty("generationFingerprint").GetString()!,
                StringComparer.Ordinal);
            fingerprints.Values.Should().OnlyContain(value => !string.IsNullOrWhiteSpace(value), path);
            if (expectedFingerprints is null)
            {
                expectedFingerprints = fingerprints;
            }
            else
            {
                fingerprints.Should().BeEquivalentTo(expectedFingerprints, path);
            }

            foreach (var (operatorType, metadata) in runtime)
            {
                var item = operators[operatorType];
                item.GetProperty("displayName").GetString().Should().Be(metadata.DisplayName, path);
                item.GetProperty("categoryId").GetString().Should().Be(metadata.CategoryId.ToString(), path);
                item.GetProperty("categoryOrder").GetInt32().Should().Be(OperatorCategoryCatalog.GetOrder(metadata.CategoryId), path);
                item.GetProperty("category").GetString().Should().Be(metadata.Category, path);
                item.GetProperty("lifecycle").GetString().Should().Be(metadata.Lifecycle.ToString(), path);
                item.GetProperty("defaultHidden").GetBoolean().Should().Be(metadata.DefaultHidden, path);
                item.GetProperty("defaultAiRecommendation").GetBoolean().Should().Be(
                    ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                        metadata.Lifecycle,
                        metadata.ImageInputContracts),
                    path);
                item.GetProperty("requiresLifecycleDisclosure").GetBoolean().Should().Be(
                    ImageContractPresentationBuilder.RequiresAiDisclosure(
                        metadata.Lifecycle,
                        metadata.ImageInputContracts),
                    path);
                item.GetProperty("inputPorts").EnumerateArray()
                    .Select(port => port.GetProperty("name").GetString())
                    .Should().Equal(metadata.InputPorts.Select(port => port.Name), path);
                item.GetProperty("outputPorts").EnumerateArray()
                    .Select(port => port.GetProperty("name").GetString())
                    .Should().Equal(metadata.OutputPorts.Select(port => port.Name), path);
                item.GetProperty("parameters").EnumerateArray()
                    .Select(parameter => parameter.GetProperty("name").GetString())
                    .Should().Equal(metadata.Parameters.Select(parameter => parameter.Name), path);
                item.GetProperty("parameterConditions").GetArrayLength().Should().Be(metadata.ParameterConstraints.Count, path);
                item.GetProperty("outputConditions").GetArrayLength().Should().Be(metadata.OutputAvailabilityRules.Count, path);
                AssertImageContractPresentations(
                    item.GetProperty("imageInputContracts"),
                    metadata.ImageInputContractPresentations,
                    path);
                item.GetProperty("generationDependencies").GetArrayLength().Should().Be(metadata.GenerationDependencies.Count, path);
                item.GetProperty("generationFingerprint").GetString().Should().NotBeNullOrWhiteSpace(path);
            }
        }

        AssertKnowledgeCards(
            Path.Combine(RepoRoot, "docs", "ai", "operator-knowledge", "operator_knowledge_cards.json"),
            root => root,
            runtime,
            expectedFingerprints!);
        AssertKnowledgeCards(
            Path.Combine(RepoRoot, "docs", "ai", "operator-knowledge", "operator_knowledge_graph.json"),
            root => root.GetProperty("Cards"),
            runtime,
            expectedFingerprints!);

        using (var graphDocument = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(RepoRoot, "docs", "ai", "operator-knowledge", "operator_knowledge_graph.json"))))
        {
            graphDocument.RootElement.GetProperty("SchemaVersion").GetString()
                .Should().Be("2026-07.operator-knowledge-graph.v4");
            graphDocument.RootElement.GetProperty("GenerationFingerprint").GetString()
                .Should().Be(expectedCatalogFingerprint);
        }

        using (var schemaDocument = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(RepoRoot, "docs", "ai", "operator-knowledge", "operator_knowledge_schema.json"))))
        {
            var schema = schemaDocument.RootElement;
            schema.GetProperty("$id").GetString().Should().Be("clearvision/operator_knowledge_schema.v2.json");
            schema.GetProperty("properties").TryGetProperty("ImageInputContracts", out _).Should().BeTrue();
            schema.GetProperty("$defs").GetProperty("imageContractPresentation")
                .GetProperty("properties").TryGetProperty("ExactVariantGroups", out _).Should().BeTrue();
            schema.GetProperty("$defs").GetProperty("imageContractVariantGroup")
                .GetProperty("properties").TryGetProperty("Verification", out _).Should().BeTrue();
        }

        foreach (var (operatorType, metadata) in runtime)
        {
            foreach (var cardPath in new[]
                     {
                         Path.Combine(RepoRoot, "docs", "算子资料", "算子名片", $"{operatorType}.md"),
                         Path.Combine(RepoRoot, "docs", "operators", $"{operatorType}.md"),
                         Path.Combine(RepoRoot, "算子资料", "算子名片", $"{operatorType}.md")
                     })
            {
                var content = File.ReadAllText(cardPath);
                content.Should().StartWith($"# {metadata.DisplayName} / ", cardPath);
                content.Should().Contain($"| 分类 ID (CategoryId) | `{metadata.CategoryId}` |", cardPath);
                content.Should().Contain($"`{metadata.Lifecycle}`", cardPath);
                content.Should().Contain("组合指纹 (Generation Fingerprint)", cardPath);
            }
        }
    }

    private static void AssertKnowledgeCards(
        string path,
        Func<JsonElement, JsonElement> selectCards,
        IReadOnlyDictionary<string, OperatorMetadata> runtime,
        IReadOnlyDictionary<string, string> expectedFingerprints)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cards = selectCards(document.RootElement).EnumerateArray()
            .ToDictionary(item => item.GetProperty("OperatorType").GetString()!, StringComparer.Ordinal);
        cards.Should().HaveCount(runtime.Count, path);

        foreach (var (operatorType, metadata) in runtime)
        {
            var card = cards[operatorType];
            card.GetProperty("DisplayName").GetString().Should().Be(metadata.DisplayName, path);
            card.GetProperty("CategoryId").GetString().Should().Be(metadata.CategoryId.ToString(), path);
            card.GetProperty("CategoryOrder").GetInt32().Should().Be(OperatorCategoryCatalog.GetOrder(metadata.CategoryId), path);
            card.GetProperty("Category").GetString().Should().Be(metadata.Category, path);
            card.GetProperty("Lifecycle").GetString().Should().Be(metadata.Lifecycle.ToString(), path);
            card.GetProperty("DefaultHidden").GetBoolean().Should().Be(metadata.DefaultHidden, path);
            card.GetProperty("SchemaVersion").GetString().Should().Be("2026-07.operator-knowledge-card.v2", path);
            card.GetProperty("DefaultAiRecommendation").GetBoolean().Should().Be(
                ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                    metadata.Lifecycle,
                    metadata.ImageInputContracts),
                path);
            card.GetProperty("RequiresLifecycleDisclosure").GetBoolean().Should().Be(
                ImageContractPresentationBuilder.RequiresAiDisclosure(
                    metadata.Lifecycle,
                    metadata.ImageInputContracts),
                path);
            card.GetProperty("ParameterConditions").GetArrayLength().Should().Be(metadata.ParameterConstraints.Count, path);
            card.GetProperty("OutputConditions").GetArrayLength().Should().Be(metadata.OutputAvailabilityRules.Count, path);
            AssertImageContractPresentations(
                card.GetProperty("ImageInputContracts"),
                metadata.ImageInputContractPresentations,
                path);
            card.GetProperty("GenerationDependencies").GetArrayLength().Should().Be(metadata.GenerationDependencies.Count, path);
            card.GetProperty("GenerationFingerprint").GetString().Should().Be(expectedFingerprints[operatorType], path);
        }
    }

    private static void AssertImageContractPresentations(
        JsonElement generated,
        IReadOnlyList<ImageInputContractPresentation> runtime,
        string path)
    {
        var deserialized = JsonSerializer.Deserialize<List<ImageInputContractPresentation>>(
            generated.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized.Should().NotBeNull(path);
        deserialized.Should().BeEquivalentTo(runtime, path);
    }

    private static IReadOnlyList<string> GetOperatorTypes(VisionAgentToolResult result)
    {
        result.Success.Should().BeTrue();
        var payload = JsonSerializer.SerializeToElement(result.Data);
        return payload.GetProperty("operators")
            .EnumerateArray()
            .Select(item => item.GetProperty("operatorType").GetString()!)
            .ToList();
    }

    private static IEnumerable<OperatorParameterCondition> EnumerateConditions(
        OperatorParameterConditionSet? conditionSet)
    {
        if (conditionSet is null)
        {
            return [];
        }

        return (conditionSet.All ?? []).Concat(conditionSet.Any ?? []);
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

    private static string PortIdentity(Port port) =>
        $"{port.Id:N}|{port.Name}|{port.Direction}|{port.DataType}|{port.IsRequired}";

    private static string ParameterIdentity(Parameter parameter) =>
        $"{parameter.Id:N}|{parameter.Name}|{parameter.DisplayName}|{parameter.DataType}|{parameter.IsRequired}";

    private static string ResolveRepoRoot([CallerFilePath] string sourceFile = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Cannot resolve ClearVision repository root.");
    }
}
