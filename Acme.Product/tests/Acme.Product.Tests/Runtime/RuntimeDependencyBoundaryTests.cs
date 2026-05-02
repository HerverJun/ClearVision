using System.Text.RegularExpressions;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Tests.Runtime;

public sealed class RuntimeDependencyBoundaryTests
{
    [Fact]
    public void StationAndRuntimeProjects_DoNotContainStudioOnlyDependencies()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Acme.Product.Station", "Acme.Product.Station.csproj"),
            Path.Combine(repoRoot, "src", "Acme.Product.Runtime", "Acme.Product.Runtime.csproj")
        }
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "Acme.Product.Station"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "Acme.Product.Runtime"), "*.cs", SearchOption.AllDirectories));

        var forbidden = new Regex("WebView2|Microsoft\\.Web\\.WebView2|wwwroot|Kestrel|WebApplication|MapVisionApiEndpoints|Acme\\.Product\\.Desktop", RegexOptions.CultureInvariant);
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
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Acme.Product.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Acme.Product.sln.");
    }
}
