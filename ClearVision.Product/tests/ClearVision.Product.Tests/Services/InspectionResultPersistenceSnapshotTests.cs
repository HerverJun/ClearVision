using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class InspectionResultPersistenceSnapshotTests
{
    [Fact]
    public void WithoutOutputImage_ShouldPreserveSummaryAndDropLargeImageBytes()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var imageId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var source = new InspectionResult(Guid.NewGuid(), imageId);
        var executionSnapshotId = Guid.NewGuid();
        source.SetOutcome(
            new InspectionOutcome(
                ExecutionOutcome.Succeeded,
                DecisionOutcome.Ng,
                "FinalDecision",
                "ThresholdExceeded",
                null,
                true),
            42,
            0.8);
        source.SetTraceability("FLOW", "BUNDLE", sessionId);
        source.RestoreExecutionTraceability(
            executionSnapshotId,
            17,
            "DECISION",
            "PACKAGE",
            "ProjectSnapshot",
            "Formal",
            "Primary");
        source.SetOutputImage(imageBytes);
        source.SetOutputDataJson("""{"score":42}""");
        source.SetAnalysisDataJson("""{"cards":[]}""");
        source.AddDefect(new Defect(source.Id, DefectType.Other, 1, 2, 3, 4, 0.7, "defect"));

        var snapshot = InspectionResultPersistenceSnapshot.WithoutOutputImage(source);

        snapshot.Id.Should().Be(source.Id);
        snapshot.ProjectId.Should().Be(source.ProjectId);
        snapshot.ImageId.Should().Be(imageId);
        snapshot.OutputImage.Should().BeNull();
        snapshot.OutputDataJson.Should().Be(source.OutputDataJson);
        snapshot.AnalysisDataJson.Should().Be(source.AnalysisDataJson);
        snapshot.FlowVersionHash.Should().Be("FLOW");
        snapshot.CalibrationBundleId.Should().Be("BUNDLE");
        snapshot.SessionId.Should().Be(sessionId);
        snapshot.ExecutionOutcome.Should().Be(ExecutionOutcome.Succeeded);
        snapshot.DecisionOutcome.Should().Be(DecisionOutcome.Ng);
        snapshot.DecisionSource.Should().Be("FinalDecision");
        snapshot.ReasonCode.Should().Be("ThresholdExceeded");
        snapshot.HasJudgmentSignal.Should().BeTrue();
        snapshot.ExecutionSnapshotId.Should().Be(executionSnapshotId);
        snapshot.ProjectPersistenceRevision.Should().Be(17);
        snapshot.DecisionConfigurationHash.Should().Be("DECISION");
        snapshot.RuntimePackageId.Should().Be("PACKAGE");
        snapshot.ExecutionSource.Should().Be("ProjectSnapshot");
        snapshot.ExecutionRunMode.Should().Be("Formal");
        snapshot.ShadowRole.Should().Be("Primary");
        snapshot.Defects.Should().ContainSingle();

        source.OutputImage.Should().BeSameAs(imageBytes);
    }

    [Fact]
    public void WithoutOutputImage_ShouldPreserveLegacyOutcomeAsLegacy()
    {
        var source = new InspectionResult(Guid.NewGuid());
        source.RestoreLegacyResult(InspectionStatus.NG, 13, 0.7);

        var snapshot = InspectionResultPersistenceSnapshot.WithoutOutputImage(source);

        snapshot.Status.Should().Be(InspectionStatus.NG);
        snapshot.ExecutionOutcome.Should().BeNull();
        snapshot.DecisionOutcome.Should().BeNull();
        snapshot.HasJudgmentSignal.Should().BeNull();
        snapshot.GetOutcome().Decision.Should().Be(DecisionOutcome.Ng);
    }
}
