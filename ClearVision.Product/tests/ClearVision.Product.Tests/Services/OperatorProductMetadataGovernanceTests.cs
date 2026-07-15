using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class OperatorProductMetadataGovernanceTests
{
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
    public void CatalogIdentity_ShouldHaveFixedCountUniqueTypesNamesAndCategories()
    {
        var metadata = new OperatorFactory().GetAllMetadata().OrderBy(item => item.Type).ToList();

        metadata.Should().HaveCount(158);
        metadata.Select(item => item.Type).Should().OnlyHaveUniqueItems();
        metadata.Select(item => item.DisplayName).Should().OnlyHaveUniqueItems();

        OperatorCategoryCatalog.All.Should().HaveCount(14);
        OperatorCategoryCatalog.All.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        OperatorCategoryCatalog.All.Select(item => item.DisplayName).Should().OnlyHaveUniqueItems();
        OperatorCategoryCatalog.All.Select(item => item.Order).Should().Equal(Enumerable.Range(1, 14));

        metadata.Should().OnlyContain(item =>
            Enum.IsDefined(item.CategoryId) &&
            item.Category == OperatorCategoryCatalog.GetDisplayName(item.CategoryId));
        metadata
            .GroupBy(item => item.CategoryId)
            .ToDictionary(group => group.Key, group => group.Count())
            .Should()
            .BeEquivalentTo(ExpectedCategoryCounts);
    }

    [Fact]
    public void LifecycleAndDefaultHidden_ShouldMatchTheScopedIdentityPolicy()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToDictionary(item => item.Type);

        foreach (var item in metadata.Values)
        {
            var expected = NonStableLifecycles.GetValueOrDefault(item.Type, OperatorLifecycle.Stable);
            item.Lifecycle.Should().Be(expected, item.Type.ToString());
            item.DefaultHidden.Should().Be(expected is OperatorLifecycle.Legacy or OperatorLifecycle.Deprecated);
        }

        metadata.Values.Count(item => item.Lifecycle == OperatorLifecycle.Stable).Should().Be(150);
        metadata.Values.Count(item => item.Lifecycle == OperatorLifecycle.Experimental).Should().Be(5);
        metadata.Values.Count(item => item.Lifecycle == OperatorLifecycle.Reference).Should().Be(2);
        metadata.Values.Count(item => item.Lifecycle == OperatorLifecycle.Legacy).Should().Be(1);
        metadata.Values.Should().NotContain(item => item.Lifecycle == OperatorLifecycle.Deprecated);
    }

    [Fact]
    public void ScannerAndRuntime_ShouldPreserveCurrentPortsParametersAndOutputs()
    {
        var scanned = new OperatorMetadataScanner().Scan().ToDictionary(item => item.Type);
        var runtime = new OperatorFactory().GetAllMetadata().ToDictionary(item => item.Type);

        scanned.Should().HaveCount(158);
        runtime.Keys.Should().BeEquivalentTo(scanned.Keys);

        foreach (var (type, current) in runtime)
        {
            var source = scanned[type];
            source.DisplayName.Should().Be(current.DisplayName, type.ToString());
            source.Description.Should().Be(current.Description, type.ToString());
            source.CategoryId.Should().Be(current.CategoryId, type.ToString());
            source.Category.Should().Be(current.Category, type.ToString());
            source.Lifecycle.Should().Be(current.Lifecycle, type.ToString());
            source.LifecycleNote.Should().Be(current.LifecycleNote, type.ToString());
            source.IconName.Should().Be(current.IconName, type.ToString());
            (source.Keywords ?? []).Should().Equal(current.Keywords ?? []);
            (source.Tags ?? []).Should().Equal(current.Tags ?? []);
            source.Version.Should().Be(current.Version, type.ToString());

            source.InputPorts.Select(PortShape).Should().Equal(current.InputPorts.Select(PortShape));
            source.OutputPorts.Select(PortShape).Should().Equal(current.OutputPorts.Select(PortShape));
            source.Parameters.Select(ParameterShape).Should().Equal(current.Parameters.Select(ParameterShape));
        }
    }

    [Fact]
    public void DeferredStableLineContracts_ShouldNotBeDeclaredByTheScopedMetadataModel()
    {
        var properties = typeof(OperatorMetadata).GetProperties().Select(property => property.Name).ToList();

        properties.Should().NotContain("OutputAvailabilityRules");
        properties.Should().NotContain("GenerationDependencies");
        properties.Should().NotContain("ImageInputContracts");
        properties.Should().NotContain("SideEffect");
        properties.Should().NotContain("Readiness");
    }

    private static string PortShape(PortDefinition port) =>
        $"{port.Name}|{port.DataType}|{port.IsRequired}";

    private static string ParameterShape(ParameterDefinition parameter) =>
        $"{parameter.Name}|{parameter.DataType}|{parameter.IsRequired}";
}
