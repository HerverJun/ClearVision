using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using System.Text.Json;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class OperatorExposureCatalogTests
{
    [Fact]
    public void Catalog_ShouldClassifyEveryEnumExactlyOnceAndFailClosedForUnknownValues()
    {
        var enumValues = Enum.GetValues<OperatorType>();
        var entries = OperatorExposureCatalog.Entries;

        entries.Should().HaveCount(enumValues.Length);
        entries.Select(entry => entry.OperatorType).Should().OnlyHaveUniqueItems();
        entries.Select(entry => entry.OperatorType).Should().BeEquivalentTo(enumValues);
        OperatorExposureCatalog.PopulationFingerprint.Should().MatchRegex("^sha256:[0-9a-f]{64}$");

        var action = () => OperatorExposureCatalog.GetExposure((OperatorType)9999);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Catalog_ShouldKeepGovernedBoundaryAndFactoryProjection()
    {
        OperatorExposureCatalog.GetExposure(OperatorType.MqttPublish).Should().Be(OperatorExposure.Disabled);
        OperatorExposureCatalog.GetExposure(OperatorType.FrameChangeTrigger).Should().Be(OperatorExposure.PackageInternal);
        OperatorExposureCatalog.GetExposure(OperatorType.Preprocessing).Should().Be(OperatorExposure.LegacyAlias);

        var visible = new OperatorFactory().GetAllMetadata().Select(metadata => metadata.Type).ToArray();
        visible.Should().NotContain(OperatorType.MqttPublish);
        visible.Should().NotContain(OperatorType.Preprocessing);
        visible.Should().Contain(OperatorType.FrameChangeTrigger);
        visible.Should().OnlyContain(type => OperatorExposureCatalog.IsProductVisible(type));
    }

    [Fact]
    public void Wave3BCriticalContracts_ShouldBindOwnerBaselineToleranceAndTruthfulReports()
    {
        var repoRoot = ResolveRepoRoot();
        using var governance = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot,
            "quality",
            "evals",
            "baselines",
            "critical-contract-governance.json")));
        var root = governance.RootElement;
        root.GetProperty("populationFingerprint").GetString()
            .Should().Be(OperatorExposureCatalog.PopulationFingerprint);

        var contracts = root.GetProperty("criticalContracts").EnumerateArray().ToArray();
        contracts.Select(item => item.GetProperty("id").GetString()).Should().BeEquivalentTo(new[]
        {
            "CV-AUDIT-004",
            "operator-dynamic-population",
            "deep-learning-smoke-precision-isolation",
            "delivery-manifest-identity",
            "core20-sidecar-stale-fingerprint",
            "publish-checks-truthfulness"
        });
        foreach (var contract in contracts)
        {
            foreach (var field in new[] { "owner", "domain", "purpose", "lane", "evidence", "oracle", "resource", "baseline", "tolerance" })
            {
                contract.GetProperty(field).GetString().Should().NotBeNullOrWhiteSpace(
                    $"{contract.GetProperty("id").GetString()} requires {field}");
            }

            File.Exists(Path.Combine(repoRoot, contract.GetProperty("baseline").GetString()!))
                .Should().BeTrue(contract.GetProperty("id").GetString());
        }

        var release = root.GetProperty("releaseGovernance");
        release.GetProperty("G02").GetString().Should().Be("OPEN_BLOCKED_NOT_PUSHED");
        release.GetProperty("G08").GetString().Should().Be("REPORT_ONLY");

        using var smokeReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot, "quality", "evals", "reports", "DeepLearning_coco_real_model_baseline.json")));
        smokeReport.RootElement.GetProperty("EvidencePurpose").GetString().Should().Be("InferenceSmokeOnly");
        smokeReport.RootElement.GetProperty("Accepted").GetBoolean().Should().BeFalse();

        using var precisionEvaluation = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot, "quality", "evals", "reports", "DeepLearning_delivery_model_evidence_evaluation.json")));
        precisionEvaluation.RootElement.GetProperty("precisionDisposition").GetString().Should().Be("FAIL");
        precisionEvaluation.RootElement.GetProperty("releaseReady").GetBoolean().Should().BeFalse();

        using var publishChecks = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot, "quality", "evals", "reports", "PublishChecks_fixture_evaluation.json")));
        publishChecks.RootElement.GetProperty("evidenceClass").GetString().Should().Be("fixture-only");
        publishChecks.RootElement.GetProperty("deliveryEvidence").GetBoolean().Should().BeFalse();
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) &&
                Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root.");
    }
}
