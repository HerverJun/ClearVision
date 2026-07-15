using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
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
        source.SetResult(InspectionStatus.NG, 42, 0.8, "ng");
        source.SetTraceability("FLOW", "BUNDLE", sessionId);
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
        snapshot.Defects.Should().ContainSingle();

        source.OutputImage.Should().BeSameAs(imageBytes);
    }
}
