using ClearVision.OperatorLibrary.Modules;
using ClearVision.Product.Core.Enums;

namespace ClearVision.OperatorLibrary.SmokeTests;

[TestClassification(TestDomain.OperatorLibrary, TestPurpose.Smoke, TestLane.Pr, TestEvidenceType.PackageSmoke, TestOracleType.Contract, TestResourceRequirement.PackageFeed, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-library", Suites = "OperatorLibrarySmoke")]
public class ModuleNamespaceIndexTests
{
    private static readonly IReadOnlyList<(OperatorModule Module, IReadOnlyList<OperatorType> Types)> ModuleNamespaceIndexes =
    [
        (OperatorModule.ImageProcessing, ClearVision.OperatorLibrary.ImageProcessing.Operators.Types),
        (OperatorModule.Measurement, ClearVision.OperatorLibrary.Measurement.Operators.Types),
        (OperatorModule.Calibration, ClearVision.OperatorLibrary.Calibration.Operators.Types),
        (OperatorModule.Communication, ClearVision.OperatorLibrary.Communication.Operators.Types),
        (OperatorModule.FlowControl, ClearVision.OperatorLibrary.FlowControl.Operators.Types),
        (OperatorModule.AI, ClearVision.OperatorLibrary.AI.Operators.Types)
    ];

    public static TheoryData<OperatorModule, IReadOnlyList<OperatorType>> ModuleNamespaceCases
    {
        get
        {
            var data = new TheoryData<OperatorModule, IReadOnlyList<OperatorType>>();
            foreach (var (module, types) in ModuleNamespaceIndexes)
            {
                data.Add(module, types);
            }

            return data;
        }
    }

    [Fact]
    public void ModuleNamespace_ShouldExposeExpectedOperatorGroups()
    {
        Assert.Contains(
            OperatorType.MeanFilter,
            ClearVision.OperatorLibrary.ImageProcessing.Operators.Types);

        Assert.Contains(
            OperatorType.CaliperTool,
            ClearVision.OperatorLibrary.Measurement.Operators.Types);

        Assert.Contains(
            OperatorType.CameraCalibration,
            ClearVision.OperatorLibrary.Calibration.Operators.Types);

        Assert.Contains(
            OperatorType.ModbusCommunication,
            ClearVision.OperatorLibrary.Communication.Operators.Types);

        Assert.Contains(
            OperatorType.TryCatch,
            ClearVision.OperatorLibrary.FlowControl.Operators.Types);

        Assert.Contains(
            OperatorType.DeepLearning,
            ClearVision.OperatorLibrary.AI.Operators.Types);

        Assert.Contains(
            OperatorType.AnomalyDetection,
            ClearVision.OperatorLibrary.AI.Operators.Types);

        Assert.Contains(
            OperatorType.SemanticSegmentation,
            ClearVision.OperatorLibrary.AI.Operators.Types);

        Assert.Contains(
            OperatorType.DetectionSequenceJudge,
            ClearVision.OperatorLibrary.AI.Operators.Types);
    }

    [Fact]
    public void ModuleCatalog_ShouldResolveKnownTypeToExpectedModule()
    {
        Assert.Equal(OperatorModule.ImageProcessing, OperatorModuleCatalog.GetModule(OperatorType.MeanFilter));
        Assert.Equal(OperatorModule.Measurement, OperatorModuleCatalog.GetModule(OperatorType.CaliperTool));
        Assert.Equal(OperatorModule.Calibration, OperatorModuleCatalog.GetModule(OperatorType.CameraCalibration));
        Assert.Equal(OperatorModule.Communication, OperatorModuleCatalog.GetModule(OperatorType.ModbusCommunication));
        Assert.Equal(OperatorModule.FlowControl, OperatorModuleCatalog.GetModule(OperatorType.TryCatch));
        Assert.Equal(OperatorModule.AI, OperatorModuleCatalog.GetModule(OperatorType.DeepLearning));
        Assert.Equal(OperatorModule.AI, OperatorModuleCatalog.GetModule(OperatorType.AnomalyDetection));
        Assert.Equal(OperatorModule.AI, OperatorModuleCatalog.GetModule(OperatorType.SemanticSegmentation));
        Assert.Equal(OperatorModule.AI, OperatorModuleCatalog.GetModule(OperatorType.DetectionSequenceJudge));
    }

    [Theory]
    [MemberData(nameof(ModuleNamespaceCases))]
    public void ModuleNamespace_ShouldMatchCatalogIndex(OperatorModule module, IReadOnlyList<OperatorType> namespaceTypes)
    {
        var catalogTypes = OperatorModuleCatalog.GetTypes(module);

        Assert.NotEmpty(namespaceTypes);
        Assert.Equal(catalogTypes, namespaceTypes);
        Assert.Equal(namespaceTypes.OrderBy(type => (int)type), namespaceTypes);
    }

    [Fact]
    public void ModuleCatalog_ShouldIndexEveryOperatorTypeExactlyOnce()
    {
        var indexedTypes = ModuleNamespaceIndexes
            .SelectMany(item => item.Types)
            .ToArray();
        var packagePublicTypes = Enum.GetValues<OperatorType>()
            .Where(OperatorModuleCatalog.IsPackagePublicType)
            .OrderBy(type => (int)type)
            .ToArray();
        var duplicateTypes = indexedTypes
            .GroupBy(type => type)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateTypes);
        Assert.Equal(packagePublicTypes, indexedTypes.OrderBy(type => (int)type));
        Assert.DoesNotContain(OperatorType.FrameChangeTrigger, indexedTypes);
        Assert.DoesNotContain(OperatorType.OnnxInference, indexedTypes);

        foreach (var module in Enum.GetValues<OperatorModule>())
        {
            foreach (var type in OperatorModuleCatalog.GetTypes(module))
            {
                Assert.Equal(module, OperatorModuleCatalog.GetModule(type));
            }
        }
    }
}
