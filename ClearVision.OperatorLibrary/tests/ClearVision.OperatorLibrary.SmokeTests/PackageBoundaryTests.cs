using System.Reflection;
using ClearVision.OperatorLibrary.Modules;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;

namespace ClearVision.OperatorLibrary.SmokeTests;

[TestClassification(TestDomain.OperatorLibrary, TestPurpose.Smoke, TestLane.Pr, TestEvidenceType.PackageSmoke, TestOracleType.Contract, TestResourceRequirement.PackageFeed, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-library", Suites = "OperatorLibrarySmoke")]
public sealed class PackageBoundaryTests
{
    private const string ExpectedPopulationFingerprint =
        "sha256:4cd53973dd918e3669dc06e2ae1e901b440810e9021ac974fcce1038b719896a";

    [Fact]
    public void InstalledPackage_ShouldExposeOnlyTheGovernedPublicCatalog()
    {
        var publicEntries = OperatorExposureCatalog.Entries
            .Where(entry => entry.Exposure == OperatorExposure.PackagePublic)
            .OrderBy(entry => (int)entry.OperatorType)
            .ToArray();
        var indexedTypes = Enum.GetValues<OperatorModule>()
            .SelectMany(OperatorModuleCatalog.GetTypes)
            .OrderBy(type => (int)type)
            .ToArray();

        Assert.Equal(156, publicEntries.Length);
        Assert.Equal(ExpectedPopulationFingerprint, OperatorExposureCatalog.PopulationFingerprint);
        Assert.Equal(publicEntries.Select(entry => entry.OperatorType), indexedTypes);
        Assert.DoesNotContain(OperatorType.MqttPublish, indexedTypes);
        Assert.DoesNotContain(OperatorType.FrameChangeTrigger, indexedTypes);
        Assert.DoesNotContain(OperatorType.Preprocessing, indexedTypes);
        Assert.DoesNotContain(OperatorType.ModbusRtuCommunication, indexedTypes);
        Assert.DoesNotContain(OperatorType.GaussianBlur, indexedTypes);
        Assert.DoesNotContain(OperatorType.OnnxInference, indexedTypes);
    }

    [Fact]
    public void InstalledPackage_ShouldExposePublicMetadataWithoutDisabledExecutors()
    {
        var assembly = typeof(OperatorExposureCatalog).Assembly;
        var metadata = typeof(MeanFilterOperator).GetCustomAttribute<OperatorMetaAttribute>();

        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrWhiteSpace(metadata.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(metadata.Description));
        Assert.Equal(OperatorCategoryId.ImagePreprocessing, metadata.CategoryId);
        Assert.Null(assembly.GetType("ClearVision.Product.Infrastructure.Operators.MqttPublishOperator"));
        Assert.Null(assembly.GetType("ClearVision.Product.Infrastructure.Operators.FrameChangeTriggerOperator"));
    }

    [Fact]
    public void InstalledPackage_ShouldContainContractsButNoProductExecutionHost()
    {
        var assembly = typeof(OperatorExposureCatalog).Assembly;
        var forbiddenAssemblyPrefixes = new[]
        {
            "ClearVision.Product.Application",
            "ClearVision.Product.Infrastructure",
            "ClearVision.Product.Desktop",
            "ClearVision.Product.Station",
            "ClearVision.Product.Agent"
        };

        Assert.Same(assembly, typeof(IHttpResourceBroker).Assembly);
        Assert.Null(assembly.GetType("ClearVision.Product.Infrastructure.Services.ServerHttpResourceBroker"));
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => forbiddenAssemblyPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));
        Assert.DoesNotContain(
            assembly.GetTypes(),
            type => type.Namespace?.StartsWith("ClearVision.Product.Desktop", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("ClearVision.Product.Station", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("ClearVision.Product.Agent", StringComparison.Ordinal) == true);
    }
}
