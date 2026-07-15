using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationResultMapperTests
{
    [Fact]
    public void ToSummary_ShouldNeverCopyImagePayloadsIntoStudioSyncPreview()
    {
        var result = new RuntimeNormalizedResult
        {
            RunId = "run-1",
            PackageId = "pkg-1",
            PackageName = "Package 1",
            PackageFlowHash = "sha256:package",
            ExecutionFlowHash = "sha256:execution",
            FlowHash = "sha256:execution",
            ProjectRevision = 12,
            DecisionConfigurationHash = "sha256:decision",
            ExecutionSnapshotId = Guid.NewGuid(),
            ExecutionRunMode = ExecutionRunMode.StationRuntime.ToString(),
            ImageId = "image-1",
            Outcome = RuntimeRunOutcome.Ok,
            ExecutionOutcome = ExecutionOutcome.Succeeded,
            DecisionOutcome = DecisionOutcome.Ok,
            HasJudgmentSignal = true,
            DecisionSource = "FinalDecisionBinding:judge:Judgment",
            ReasonCode = "DecisionResolved",
            ExecutionTimeMs = 12,
            DiagnosticCode = "OK",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-12),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            SourceImageBytes = [1, 2, 3],
            OutputImageBytes = [4, 5, 6],
            PrimaryOutputs = new Dictionary<string, object?>
            {
                ["score"] = 0.98d,
                ["thumbnail"] = "base64-image-data",
                ["outputImage"] = "should-not-leave-station",
                ["binaryBlob"] = new byte[] { 7, 8, 9 },
                ["Scene"] = new { layers = new[] { "full-scene" } },
                ["OutputScene"] = "should-not-leave-station",
                ["ArtifactPayload"] = "large-payload",
                ["measurements"] = new[] { 1, 2, 3 }
            }
        };

        var summary = StationResultMapper.ToSummary(
            result,
            new StationIdentityContext
            {
                StationId = "station-a",
                LineName = "line-a",
                CurrentPackageVersion = "1.0.0"
            });

        summary.PrimaryOutputsPreview.Should().ContainKey("score");
        summary.PrimaryOutputsPreview.Should().ContainKey("measurements");
        summary.PrimaryOutputsPreview.Keys.Should().NotContain(key =>
            key.Contains("image", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("base64", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("binaryBlob", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("scene", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("artifactPayload", StringComparison.OrdinalIgnoreCase));
        summary.ExecutionOutcome.Should().Be(ExecutionOutcome.Succeeded);
        summary.DecisionOutcome.Should().Be(DecisionOutcome.Ok);
        summary.HasJudgmentSignal.Should().BeTrue();
        summary.DecisionSource.Should().Be("FinalDecisionBinding:judge:Judgment");
        summary.ReasonCode.Should().Be("DecisionResolved");
        summary.PackageFlowHash.Should().Be("sha256:package");
        summary.ExecutionFlowHash.Should().Be("sha256:execution");
        summary.FlowHash.Should().Be(summary.ExecutionFlowHash);
        summary.ProjectRevision.Should().Be(result.ProjectRevision);
        summary.DecisionConfigurationHash.Should().Be(result.DecisionConfigurationHash);
        summary.ExecutionSnapshotId.Should().Be(result.ExecutionSnapshotId);
        summary.ExecutionRunMode.Should().Be(result.ExecutionRunMode);
    }
}
