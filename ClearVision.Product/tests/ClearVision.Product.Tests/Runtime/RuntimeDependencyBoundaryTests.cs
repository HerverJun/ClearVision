using System.Text.RegularExpressions;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Continuous;
using ClearVision.Product.Infrastructure.DependencyInjection;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Tests.Runtime;

public sealed class RuntimeDependencyBoundaryTests
{
    [Fact]
    public void StationAndRuntimeProjects_DoNotContainStudioOnlyDependencies()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "ClearVision.Product.Station", "ClearVision.Product.Station.csproj"),
            Path.Combine(repoRoot, "src", "ClearVision.Product.Runtime", "ClearVision.Product.Runtime.csproj")
        }
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "ClearVision.Product.Station"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "ClearVision.Product.Runtime"), "*.cs", SearchOption.AllDirectories));

        var forbidden = new Regex("WebView2|Microsoft\\.Web\\.WebView2|wwwroot|Kestrel|WebApplication|MapVisionApiEndpoints|ClearVision\\.Product\\.Desktop", RegexOptions.CultureInvariant);
        var hits = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => new { file, line, index }))
            .Where(item => forbidden.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(repoRoot, item.file)}:{item.index + 1}:{item.line.Trim()}")
            .ToList();

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SharedRuntimeRegistration_ExposesFlowExecutionAndOperatorExecutors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVisionRuntimeCoreServices();

        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IFlowExecutionService>());

        var executorImplementationTypes = GetRuntimeOperatorExecutorImplementationTypes();
        var executors = provider.GetServices<IOperatorExecutor>().ToList();
        var registeredTypes = executors.Select(executor => executor.GetType()).ToHashSet();
        var missingTypes = executorImplementationTypes
            .Where(type => !registeredTypes.Contains(type))
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            missingTypes.Count == 0,
            "Concrete IOperatorExecutor implementations not resolved from DI: " + string.Join(", ", missingTypes));
        Assert.True(
            executors.Count >= executorImplementationTypes.Count,
            $"Expected at least {executorImplementationTypes.Count} shared executors, got {executors.Count}.");

        var duplicateOperatorTypes = executors
            .GroupBy(executor => executor.OperatorType)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(executor => executor.GetType().Name).OrderBy(name => name))}")
            .OrderBy(item => item)
            .ToList();
        Assert.True(
            duplicateOperatorTypes.Count == 0,
            "Duplicate operator executors by OperatorType: " + string.Join("; ", duplicateOperatorTypes));

        var registeredOperatorTypes = executors.Select(executor => executor.OperatorType).ToHashSet();
        var factory = provider.GetRequiredService<IOperatorFactory>();
        var visibleWithoutExecutor = factory.GetAllMetadata()
            .Select(metadata => metadata.Type)
            .Distinct()
            .Where(type => !registeredOperatorTypes.Contains(OperatorTypeAliasResolver.Resolve(type)))
            .OrderBy(type => type)
            .ToList();
        Assert.True(
            visibleWithoutExecutor.Count == 0,
            "OperatorFactory exposes operator types without a registered executor: " + string.Join(", ", visibleWithoutExecutor));

        var requiredOperatorTypes = new[]
        {
            OperatorType.BinaryImageToRegion,
            OperatorType.RegionErosion,
            OperatorType.RegionDilation,
            OperatorType.RegionOpening,
            OperatorType.RegionClosing,
            OperatorType.RegionSkeleton,
            OperatorType.RegionUnion,
            OperatorType.RegionIntersection,
            OperatorType.RegionDifference,
            OperatorType.RegionComplement,
            OperatorType.FisheyeCalibration,
            OperatorType.FisheyeUndistort,
            OperatorType.StereoCalibration,
            OperatorType.PlanarMatching,
            OperatorType.LocalDeformableMatching,
            OperatorType.DistanceTransform,
            OperatorType.MinEnclosingGeometry,
            OperatorType.ArcCaliper,
            OperatorType.ContourExtrema,
            OperatorType.FFT1D,
            OperatorType.FrequencyFilter,
            OperatorType.InverseFFT1D,
            OperatorType.PhaseClosure,
            OperatorType.AnomalyDetection,
            OperatorType.HandEyeCalibrationValidator,
            OperatorType.Delay,
            OperatorType.Comment,
            OperatorType.Aggregator,
            OperatorType.Comparator,
            OperatorType.RoiTransform
        };
        var missingRequiredTypes = requiredOperatorTypes
            .Where(type => !registeredOperatorTypes.Contains(type))
            .OrderBy(type => type)
            .ToList();
        Assert.True(
            missingRequiredTypes.Count == 0,
            "Required operator executors not registered: " + string.Join(", ", missingRequiredTypes));
    }

    [Fact]
    public void ProductExecutionEntrypoints_DoNotCallLegacyPolicylessFlowOverloads()
    {
        var repoRoot = FindRepoRoot();
        var relativeFiles = new[]
        {
            "src/ClearVision.Product.Application/Services/InspectionService.cs",
            "src/ClearVision.Product.Application/Commands/Inspections/ExecuteInspectionCommand.cs",
            "src/ClearVision.Product.Infrastructure/Services/InspectionWorker.cs",
            "src/ClearVision.Product.Infrastructure/Continuous/ContinuousInspectionWorker.cs",
            "src/ClearVision.Product.Infrastructure/Services/FlowNodePreviewService.cs",
            "src/ClearVision.Product.Infrastructure/Services/AutoTuneService.cs",
            "src/ClearVision.Product.Infrastructure/AI/DryRun/DryRunService.cs",
            "src/ClearVision.Product.Infrastructure/Services/IntelligentDetectionService.cs",
            "src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs",
            "src/ClearVision.Product.Runtime/RuntimeHost.cs"
        };
        var legacyCall = new Regex(@"\.ExecuteFlow(?:Debug)?Async\s*\(", RegexOptions.CultureInvariant);
        var disabledBlock = new Regex(@"#if\s+false[\s\S]*?#endif", RegexOptions.CultureInvariant);

        var hits = relativeFiles
            .Select(relative => new
            {
                Relative = relative,
                Text = disabledBlock.Replace(File.ReadAllText(Path.Combine(repoRoot, relative)), string.Empty)
            })
            .SelectMany(item => item.Text.Split('\n')
                .Select((line, index) => new { item.Relative, Line = line, Index = index + 1 }))
            .Where(item => legacyCall.IsMatch(item.Line))
            .Select(item => $"{item.Relative}:{item.Index}:{item.Line.Trim()}")
            .ToList();

        Assert.True(
            hits.Count == 0,
            "Policyless flow execution calls found in product entrypoints: " + string.Join("; ", hits));
    }

    [Fact]
    public void RuntimeWorkers_DoNotExposeOperatorFlowStartBypasses()
    {
        var inspectionBypasses = typeof(IInspectionWorker).GetMethods()
            .Where(method => method.Name == nameof(IInspectionWorker.TryStartRunAsync))
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ClearVision.Product.Core.Entities.OperatorFlow)))
            .Select(method => method.ToString())
            .ToList();
        var continuousBypasses = typeof(ContinuousInspectionWorker).GetMethods()
            .Where(method => method.Name == nameof(ContinuousInspectionWorker.RunAsync))
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ClearVision.Product.Core.Entities.OperatorFlow)))
            .Select(method => method.ToString())
            .ToList();

        Assert.Empty(inspectionBypasses);
        Assert.Empty(continuousBypasses);
    }

    private static List<Type> GetRuntimeOperatorExecutorImplementationTypes()
    {
        return typeof(ImageAcquisitionOperator).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                typeof(IOperatorExecutor).IsAssignableFrom(type) &&
                type.Namespace != null &&
                (string.Equals(type.Namespace, typeof(ImageAcquisitionOperator).Namespace, StringComparison.Ordinal) ||
                    type.Namespace.StartsWith(typeof(ImageAcquisitionOperator).Namespace + ".", StringComparison.Ordinal)))
            .OrderBy(type => type.Name)
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "ClearVision.Product.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ClearVision.Product.sln.");
    }
}
