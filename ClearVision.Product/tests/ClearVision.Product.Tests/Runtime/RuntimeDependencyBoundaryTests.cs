using System.Text.RegularExpressions;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
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
