using ClearVision.Product.Desktop.Configuration;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StudioStartupPageResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-startup-page-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WhenProductionIndexExists_ShouldAlwaysUseProductionRoot()
    {
        var legacyRoot = CreateProductionRoot();

        var decision = StudioStartupPageResolver.Resolve(
            new StudioOptions(),
            legacyWebRoot: legacyRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Legacy);
        decision.PagePath.Should().Be(StudioStartupPageResolver.LegacyPagePath);
        decision.RequiredFilePath.Should().Be(Path.Combine(legacyRoot, "index.html"));
        StudioStartupPageResolver.CreateInitialPageUri(5000, decision)
            .ToString()
            .Should()
            .Be("http://localhost:5000/index.html");
    }

    [Fact]
    public void Resolve_WhenProductionIndexIsMissing_ShouldReturnNonNavigableWelcomeDecision()
    {
        var missingRoot = Path.Combine(_tempRoot, "missing-wwwroot");
        Directory.CreateDirectory(missingRoot);

        var decision = StudioStartupPageResolver.Resolve(
            new StudioOptions(),
            legacyWebRoot: missingRoot);

        decision.Kind.Should().Be(StudioStartupPageKind.Welcome);
        decision.IsNavigable.Should().BeFalse();
        decision.PagePath.Should().BeNull();
        decision.DiagnosticMessage.Should().Contain("index.html");
    }

    [Fact]
    public void StudioOptions_ShouldNotExposeWorkspaceV2RuntimeSwitch()
    {
        typeof(StudioOptions).GetProperty("WorkspaceV2Enabled").Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateProductionRoot()
    {
        var root = Path.Combine(_tempRoot, "wwwroot");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html>production");
        return root;
    }
}
