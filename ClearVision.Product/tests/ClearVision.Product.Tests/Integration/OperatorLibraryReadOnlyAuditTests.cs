using System.Security.Cryptography;
using System.Text;
using ClearVision.OperatorLibrary.ReadOnlyAudit;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using FluentAssertions;

namespace ClearVision.Product.Tests.Integration;

public sealed class OperatorLibraryReadOnlyAuditTests
{
    [Fact(DisplayName = "Audit rules detect deterministic metadata contract violations")]
    public void AuditRules_DetectDeterministicViolations()
    {
        var metadata = new OperatorMetadata
        {
            Type = OperatorType.BlobAnalysis,
            Parameters =
            [
                new ParameterDefinition
                {
                    Name = "Mode",
                    DefaultValue = "Missing",
                    Options =
                    [
                        new ParameterOption { Value = "A", Label = "A" },
                        new ParameterOption { Value = "B", Label = "B" }
                    ]
                },
                new ParameterDefinition
                {
                    Name = "Range",
                    MinValue = 10,
                    MaxValue = 1
                },
                new ParameterDefinition { Name = "Duplicate" },
                new ParameterDefinition { Name = "Duplicate" }
            ],
            InputPorts =
            [
                new PortDefinition { Name = "Image", DataType = PortDataType.Image },
                new PortDefinition { Name = "Image", DataType = PortDataType.Image }
            ],
            OutputPorts =
            [
                new PortDefinition { Name = "Result", DataType = PortDataType.Any },
                new PortDefinition { Name = "Result", DataType = PortDataType.Any }
            ]
        };

        var findings = AuditRuleEngine.ValidateMetadata(metadata, "synthetic/operator.cs");

        findings.Select(item => item.Code).Should().Contain(
        [
            "DUPLICATE_PARAMETER_NAME",
            "DUPLICATE_INPUT_PORT_NAME",
            "DUPLICATE_OUTPUT_PORT_NAME",
            "ENUM_DEFAULT_NOT_IN_OPTIONS",
            "MIN_GREATER_THAN_MAX"
        ]);
        findings.Should().OnlyContain(item => AuditSchema.Classifications.Contains(item.Classification));
        findings.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Code) &&
            !string.IsNullOrWhiteSpace(item.Severity) &&
            !string.IsNullOrWhiteSpace(item.Confidence) &&
            !string.IsNullOrWhiteSpace(item.Operator) &&
            !string.IsNullOrWhiteSpace(item.Field) &&
            item.Evidence.Count > 0 &&
            !string.IsNullOrWhiteSpace(item.Impact) &&
            !string.IsNullOrWhiteSpace(item.SuggestedAction));
    }

    [Fact(DisplayName = "Audit report schema and manual review evidence are complete")]
    public void AuditReport_SchemaAndManualReviewAreComplete()
    {
        var artifacts = AuditEngine.Generate(new AuditOptions(FindRepoRoot(), "audit-test-sha", ReportOnly: true));

        AuditSchemaValidator.Validate(artifacts.Report).Should().BeEmpty();
        artifacts.Report.ManualReviewSamples.Should().HaveCountGreaterOrEqualTo(15);
        artifacts.Report.ManualReviewSamples.Select(item => item.Category).Distinct().Should().HaveCountGreaterOrEqualTo(6);
        artifacts.Report.Findings.Should().OnlyContain(item => AuditSchema.Classifications.Contains(item.Classification));
        artifacts.Json.Should().NotContain("generatedAtUtc");
        artifacts.Json.Should().NotContain("generatedAt");
        artifacts.Summary.SchemaValid.Should().BeTrue();
    }

    [Fact(DisplayName = "Audit JSON Markdown and summary are deterministic across consecutive runs")]
    public void AuditOutputs_AreDeterministicAcrossConsecutiveRuns()
    {
        var first = AuditEngine.Generate(new AuditOptions(FindRepoRoot(), "audit-test-sha", ReportOnly: true));
        var second = AuditEngine.Generate(new AuditOptions(FindRepoRoot(), "audit-test-sha", ReportOnly: true));

        Hash(first.Json).Should().Be(Hash(second.Json));
        Hash(first.Markdown).Should().Be(Hash(second.Markdown));
        Hash(first.SummaryJson).Should().Be(Hash(second.SummaryJson));
    }

    private static string Hash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")) &&
                Directory.Exists(Path.Combine(current.FullName, "ClearVision.OperatorLibrary")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("ClearVision repository root was not found from the test base directory.");
    }
}
