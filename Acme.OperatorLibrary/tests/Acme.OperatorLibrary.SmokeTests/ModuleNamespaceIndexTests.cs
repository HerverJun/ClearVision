using Acme.OperatorLibrary.Modules;
using Acme.Product.Core.Enums;

namespace Acme.OperatorLibrary.SmokeTests;

public class ModuleNamespaceIndexTests
{
    private static readonly IReadOnlyList<(OperatorModule Module, IReadOnlyList<OperatorType> Types)> ModuleNamespaceIndexes =
    [
        (OperatorModule.ImageProcessing, Acme.OperatorLibrary.ImageProcessing.Operators.Types),
        (OperatorModule.Measurement, Acme.OperatorLibrary.Measurement.Operators.Types),
        (OperatorModule.Calibration, Acme.OperatorLibrary.Calibration.Operators.Types),
        (OperatorModule.Communication, Acme.OperatorLibrary.Communication.Operators.Types),
        (OperatorModule.FlowControl, Acme.OperatorLibrary.FlowControl.Operators.Types),
        (OperatorModule.AI, Acme.OperatorLibrary.AI.Operators.Types)
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
            Acme.OperatorLibrary.ImageProcessing.Operators.Types);

        Assert.Contains(
            OperatorType.CaliperTool,
            Acme.OperatorLibrary.Measurement.Operators.Types);

        Assert.Contains(
            OperatorType.CameraCalibration,
            Acme.OperatorLibrary.Calibration.Operators.Types);

        Assert.Contains(
            OperatorType.ModbusCommunication,
            Acme.OperatorLibrary.Communication.Operators.Types);

        Assert.Contains(
            OperatorType.TryCatch,
            Acme.OperatorLibrary.FlowControl.Operators.Types);

        Assert.Contains(
            OperatorType.DeepLearning,
            Acme.OperatorLibrary.AI.Operators.Types);
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
        var enumTypes = Enum.GetValues<OperatorType>().OrderBy(type => (int)type).ToArray();
        var duplicateTypes = indexedTypes
            .GroupBy(type => type)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateTypes);
        Assert.Equal(enumTypes, indexedTypes.OrderBy(type => (int)type));

        foreach (var module in Enum.GetValues<OperatorModule>())
        {
            foreach (var type in OperatorModuleCatalog.GetTypes(module))
            {
                Assert.Equal(module, OperatorModuleCatalog.GetModule(type));
            }
        }
    }
}
