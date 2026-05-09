using System.Text.Json;
using Acme.Product.Infrastructure.Plugins;
using FluentAssertions;

namespace Acme.Product.Tests.Plugins;

public class OperatorPluginManifestCompatibilityTests
{
    [Fact]
    public void Evaluate_WithCompatibleManifest_ShouldPass()
    {
        var manifest = CreateManifest();

        var result = OperatorPluginManifestCompatibility.Evaluate(manifest, new Version(1, 2, 0));

        result.IsCompatible.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WithTooOldHost_ShouldReportVersionIssue()
    {
        var manifest = CreateManifest();
        manifest.HostCompatibility.MinHostVersion = "2.0.0";

        var result = OperatorPluginManifestCompatibility.Evaluate(manifest, new Version(1, 9, 0));

        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Code == "HostVersionTooOld");
    }

    [Fact]
    public void Evaluate_WithPlaceholderEnabledByDefault_ShouldReportIssue()
    {
        var manifest = CreateManifest();
        manifest.Operators[0].Maturity = OperatorIntegrationMaturity.PlaceholderDisabled;
        manifest.Operators[0].EnabledByDefault = true;

        var result = OperatorPluginManifestCompatibility.Evaluate(manifest, new Version(1, 2, 0));

        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Code == "PlaceholderEnabledByDefault");
    }

    [Fact]
    public void Deserialize_SampleManifest_ShouldUseDocumentedJsonContract()
    {
        var samplePath = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "operator-library",
            "operator-plugin-manifest.sample.json");
        var json = File.ReadAllText(samplePath);

        var manifest = JsonSerializer.Deserialize<OperatorPluginManifest>(
            json,
            OperatorPluginManifestJson.SerializerOptions);

        manifest.Should().NotBeNull();
        manifest!.PluginId.Should().Be("clearvision.operator-library");
        manifest.Maturity.Should().Be(OperatorIntegrationMaturity.Experimental);
        manifest.HostCompatibility.OperatorContractVersion.Should().Be(OperatorPluginManifestDefaults.OperatorContractVersion);
        manifest.Operators.Should().Contain(op =>
            op.OperatorType == "MqttPublish" &&
            op.Maturity == OperatorIntegrationMaturity.PlaceholderDisabled &&
            op.EnabledByDefault == false);

        var result = OperatorPluginManifestCompatibility.Evaluate(manifest, new Version(1, 2, 0));
        result.IsCompatible.Should().BeTrue();
    }

    [Fact]
    public void Serialize_Manifest_ShouldEmitCamelCaseAndStringEnums()
    {
        var manifest = CreateManifest();

        var json = JsonSerializer.Serialize(manifest, OperatorPluginManifestJson.SerializerOptions);

        json.Should().Contain("\"pluginId\":");
        json.Should().Contain("\"enabledByDefault\": true");
        json.Should().Contain("\"maturity\": \"Experimental\"");
        json.Should().NotContain("\"PluginId\":");
        json.Should().NotContain("\"Maturity\": 1");
    }

    private static OperatorPluginManifest CreateManifest()
    {
        return new OperatorPluginManifest
        {
            PluginId = "clearvision.operator-library",
            PackageId = "Acme.OperatorLibrary",
            PackageVersion = "1.0.2",
            DisplayName = "ClearVision Operator Library",
            Maturity = OperatorIntegrationMaturity.Experimental,
            HostCompatibility = new OperatorPluginHostCompatibility
            {
                MinHostVersion = "1.0.0",
                MaxHostVersion = "1.9.0",
                OperatorContractVersion = OperatorPluginManifestDefaults.OperatorContractVersion
            },
            NativeProfiles = { "win-x64" },
            Capabilities = { "operators", "metadata" },
            Operators =
            {
                new OperatorPluginOperatorManifest
                {
                    OperatorType = "TemplateMatching",
                    RuntimeTypeName = "Acme.Product.Infrastructure.Operators.TemplateMatchOperator",
                    Maturity = OperatorIntegrationMaturity.Experimental,
                    EnabledByDefault = true,
                    Tags = { "matching" }
                }
            }
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var samplePath = Path.Combine(
                directory.FullName,
                "docs",
                "operator-library",
                "operator-plugin-manifest.sample.json");
            if (File.Exists(samplePath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
