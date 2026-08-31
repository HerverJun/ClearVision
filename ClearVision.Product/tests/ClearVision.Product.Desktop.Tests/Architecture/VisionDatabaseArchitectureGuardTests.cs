using System.Runtime.CompilerServices;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests.Architecture;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class VisionDatabaseArchitectureGuardTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ProductionSource_ShouldNotContainAppDbContext()
    {
        var legacyContextPath = Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/Persistence/AppDbContext.cs");

        File.Exists(legacyContextPath).Should().BeFalse("VisionDbContext is the only production EF context");

        foreach (var sourceFile in EnumerateProductionSourceFiles())
        {
            File.ReadAllText(sourceFile)
                .Should()
                .NotContain("AppDbContext", ToRelativePath(sourceFile));
        }
    }

    [Fact]
    public void ProductionRuntime_ShouldRegisterAndResolveOnlyVisionDbContext()
    {
        var sourceFiles = EnumerateProductionSourceFiles()
            .Select(file => (Path: ToRelativePath(file), Text: File.ReadAllText(file)))
            .ToList();

        var registrations = sourceFiles
            .Where(file => file.Text.Contains("AddDbContext", StringComparison.Ordinal))
            .ToList();
        registrations.Should().ContainSingle("the Desktop runtime must have one EF registration authority");
        registrations[0].Path.Should().Be(
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/DependencyInjection/VisionRuntimeServiceCollectionExtensions.cs");
        registrations[0].Text.Should().Contain("services.AddDbContext<VisionDbContext>");
        registrations[0].Text.Should().NotContain("AddDbContext<AppDbContext>");

        var bootstrap = sourceFiles.Single(file => file.Path.EndsWith("/ClearVision.Product.Desktop/Program.cs", StringComparison.Ordinal));
        bootstrap.Text.Should().Contain("GetRequiredService<ClearVision.Product.Infrastructure.Data.VisionDbContext>()");
        bootstrap.Text.Should().Contain("VisionDatabaseInitializer.InitializeAsync");
        bootstrap.Text.Should().NotContain("AppDbContext");

        var maintenance = sourceFiles.Single(file => file.Path.EndsWith("/ClearVision.Product.Desktop/Data/VisionDatabaseMaintenance.cs", StringComparison.Ordinal));
        maintenance.Text.Should().Contain("GetRequiredService<VisionDbContext>()");
        maintenance.Text.Should().NotContain("AppDbContext");
    }

    [Fact]
    public void StationDdl_ShouldRemainCentralizedInVisionDatabaseMaintenance()
    {
        var stationDdlMarkers = new[]
        {
            "CREATE TABLE",
            "ALTER TABLE",
            "CREATE INDEX",
            "DROP TABLE"
        };

        var violations = EnumerateProductionSourceFiles()
            .Select(file => new
            {
                Path = ToRelativePath(file),
                Text = File.ReadAllText(file)
            })
            .Where(file => file.Text.Contains("Station", StringComparison.Ordinal) &&
                           stationDdlMarkers.Any(marker => file.Text.Contains(marker, StringComparison.Ordinal)))
            .Where(file => !file.Path.EndsWith("/ClearVision.Product.Desktop/Data/VisionDatabaseMaintenance.cs", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToList();

        violations.Should().BeEmpty("Station legacy repair SQL is centralized in VisionDatabaseMaintenance, not Program or business services");
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles()
    {
        return Directory.EnumerateFiles(
                Path.Combine(Root, "ClearVision.Product/src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRelativePath(string path)
    {
        return Path.GetRelativePath(Root, path).Replace('\\', '/');
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var startPath in new[]
                 {
                     Path.GetDirectoryName(sourceFile),
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                continue;
            }

            var directory = new DirectoryInfo(startPath);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ClearVision.Product", "ClearVision.Product.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
