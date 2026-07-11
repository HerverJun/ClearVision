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

    [Fact(DisplayName = "Audit report schema and review baseline evidence are complete")]
    public void AuditReport_SchemaAndReviewBaselineAreComplete()
    {
        var artifacts = AuditEngine.Generate(new AuditOptions(FindRepoRoot(), "audit-test-sha", ReportOnly: true));

        AuditSchemaValidator.Validate(artifacts.Report).Should().BeEmpty();
        artifacts.Report.SchemaVersion.Should().Be("2026-07-10.operator-audit.v4");
        artifacts.Summary.SchemaVersion.Should().Be("2026-07-10.operator-audit-summary.v3");
        artifacts.Report.ReviewEntries.Should().HaveCountGreaterOrEqualTo(15);
        artifacts.Summary.Review.ReviewedCount.Should().BeGreaterOrEqualTo(15);
        artifacts.Summary.Review.Categories.Should().HaveCountGreaterOrEqualTo(6);
        artifacts.Summary.Review.StaticDifferenceCount.Should().Be(10);
        artifacts.Summary.Review.ResolvedDifferenceCount.Should().Be(5);
        artifacts.Summary.Review.ProductionReachableCount.Should().Be(11);
        artifacts.Summary.Review.FixedProductionDefectCount.Should().Be(5);
        artifacts.Summary.Review.OpenProductionDefectCount.Should().Be(1);
        artifacts.Summary.Review.CandidateCount.Should().Be(2);
        artifacts.Summary.Review.IntentionalDifferenceCount.Should().Be(6);
        artifacts.Summary.Review.AuditFalsePositiveCount.Should().Be(1);
        artifacts.Report.ReviewEntries.Should().OnlyContain(entry =>
            !string.IsNullOrWhiteSpace(entry.StaticDifferenceStatus) &&
            !string.IsNullOrWhiteSpace(entry.ProductionReachability));
        artifacts.Report.ReviewEntries
            .Where(entry => entry.Verdict == "fixed-production-defect")
            .Should().OnlyContain(entry =>
                entry.StaticDifferenceStatus == "resolved" &&
                entry.ProductionReachability == "reachable");
        var operatorLibrary = artifacts.Report.Surfaces.Single(surface => surface.Name == "operatorLibrary");
        operatorLibrary.Status.Should().Be("unavailable");
        operatorLibrary.ObservedCount.Should().BeNull();
        operatorLibrary.FailureReason.Should().NotBeNullOrWhiteSpace();
        artifacts.Report.Findings.Should().OnlyContain(item => AuditSchema.Classifications.Contains(item.Classification));
        artifacts.Json.Should().NotContain("generatedAtUtc");
        artifacts.Json.Should().NotContain("generatedAt");
        artifacts.Markdown.Should().Contain("| Static confirmed findings |");
        artifacts.Markdown.Should().Contain("| Accepted intentional differences | 6 |");
        artifacts.Markdown.Should().Contain("| Open production defects | 1 |");
        artifacts.Markdown.Should().Contain("| New confirmed gate | pass (0) |");
        artifacts.Markdown.Should().Contain("- Open production defects: 1");
        artifacts.Summary.SchemaValid.Should().BeTrue();
    }

    [Fact]
    public void CandidateOutputFlow_InternalFeatureDictionary_ShouldNotProduceFinding()
    {
        var findings = AuditCandidateProbe.AnalyzeOutputFindings(
            Source("""
                var featureValues = new Dictionary<string, object> { ["Area"] = 10 };
                var output = new Dictionary<string, object> { ["Result"] = featureValues.Count };
                return OperatorExecutionOutput.Success(output);
                """),
            "Synthetic",
            ["Result"]);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void CandidateOutputFlow_ParameterMapping_ShouldNotProduceFinding()
    {
        var findings = AuditCandidateProbe.AnalyzeOutputFindings(
            Source("""
                var parameterMap = new Dictionary<string, object> { ["Threshold"] = 10 };
                return OperatorExecutionOutput.Success(new Dictionary<string, object> { ["Result"] = 1 });
                """),
            "Synthetic",
            ["Result"]);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void CandidateOutputFlow_SuccessAdditionalData_ShouldProduceUndeclaredCandidate()
    {
        var findings = AuditCandidateProbe.AnalyzeOutputFindings(
            Source("""
                var additionalData = new Dictionary<string, object> { ["Undeclared"] = 1 };
                return OperatorExecutionOutput.Success(additionalData);
                """),
            "Synthetic",
            []);

        findings.Should().ContainSingle(item =>
            item.Code == "RUNTIME_OUTPUT_UNDOCUMENTED" && item.Field == "Undeclared");
    }

    [Fact]
    public void CandidateOutputFlow_HelperSuccessPath_ShouldBeRecognized()
    {
        var findings = AuditCandidateProbe.AnalyzeOutputFindings(
            Source(
                "return OperatorExecutionOutput.Success(CreateOutputs());",
                """
                private static Dictionary<string, object> CreateOutputs()
                {
                    return new Dictionary<string, object> { ["HelperKey"] = 1 };
                }
                """),
            "Synthetic",
            []);

        findings.Should().ContainSingle(item => item.Field == "HelperKey");
    }

    [Fact]
    public void CandidateOutputFlow_DynamicSuccessKey_ShouldRemainLowConfidenceCandidate()
    {
        var findings = AuditCandidateProbe.AnalyzeOutputFindings(
            Source("""
                var key = DateTime.UtcNow.Ticks.ToString();
                var output = new Dictionary<string, object>();
                output[key] = 1;
                return OperatorExecutionOutput.Success(output);
                """),
            "Synthetic",
            []);

        findings.Should().ContainSingle(item => item.Code == "RUNTIME_OUTPUT_DYNAMIC_UNPROVEN");
    }

    [Fact]
    public void CandidateOutputFlow_DuplicateIdentity_ShouldBeDeduplicated()
    {
        var findings = AuditCandidateProbe.AnalyzeOutputFindings(
            Source("""
                var output = new Dictionary<string, object> { ["Duplicate"] = 1 };
                output["Duplicate"] = 2;
                return OperatorExecutionOutput.Success(output);
                """),
            "Synthetic",
            []);

        findings.Should().ContainSingle(item =>
            item.Code == "RUNTIME_OUTPUT_UNDOCUMENTED" && item.Field == "Duplicate");
    }

    [Fact]
    public void ConfirmedGate_ShouldOnlyFailNewConfirmedIdentities()
    {
        var baseline = new[]
        {
            new ConfirmedFindingBaselineEntry("KNOWN", "OperatorA", "FieldA", "intentional", "intentional-difference")
        };
        var findings = new[]
        {
            Finding("KNOWN", "confirmed", "OperatorA", "FieldA"),
            Finding("NEW", "confirmed", "OperatorB", "FieldB"),
            Finding("CANDIDATE", "candidate", "OperatorC", "FieldC")
        };

        var result = ConfirmedFindingGate.Evaluate(findings, baseline);

        result.NewConfirmedFindings.Should().ContainSingle(item => item.Code == "NEW");
    }

    [Fact]
    public void ConfirmedGate_ResolvedBaselineAndCandidates_ShouldPass()
    {
        var baseline = new[]
        {
            new ConfirmedFindingBaselineEntry("RESOLVED", "OperatorA", "FieldA", "intentional", "intentional-difference")
        };

        var result = ConfirmedFindingGate.Evaluate(
            [Finding("CANDIDATE", "candidate", "OperatorB", "FieldB")],
            baseline);

        result.NewConfirmedFindings.Should().BeEmpty();
    }

    [Fact]
    public void ConfirmedGate_MalformedBaseline_ShouldFail()
    {
        var action = () => AuditBaselineStore.ParseConfirmedBaseline("[{\"code\":\"BROKEN\"}]", "test baseline");

        action.Should().Throw<InvalidDataException>();
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

    private static string Source(string body, string helper = "")
    {
        return $$"""
            using System;
            using System.Collections.Generic;
            public static class OperatorExecutionOutput
            {
                public static object Success(object data) => data;
            }
            public sealed class SyntheticOperator
            {
                public object Run()
                {
                    {{body}}
                }

                {{helper}}
            }
            """;
    }

    private static AuditFinding Finding(string code, string classification, string @operator, string field)
    {
        return new AuditFinding(
            code,
            "warning",
            "high",
            classification,
            @operator,
            field,
            ["test"],
            "impact",
            "action");
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
