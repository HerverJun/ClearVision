using System.Text.RegularExpressions;
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

        var executors = provider.GetServices<IOperatorExecutor>().ToList();
        Assert.True(executors.Count >= 100, $"Expected shared executor catalog, got {executors.Count}.");
        Assert.Equal(executors.Count, executors.Select(executor => executor.OperatorType).Distinct().Count());
        Assert.Contains(executors, executor => executor is TcpCommunicationOperator);
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
