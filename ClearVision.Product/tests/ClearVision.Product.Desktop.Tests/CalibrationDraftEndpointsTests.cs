using System.Text;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.PreviewArtifacts;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class CalibrationDraftEndpointsTests
{
    [Fact]
    public void SolveDraft_UsesEnabledValidSamplesAndReturnsDraftArtifacts()
    {
        using var store = new PreviewArtifactStore();
        var request = new NPointCalibrationDraftSolveRequest
        {
            SessionId = "session-affine",
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DebugSessionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ClientRequestSequence = 9,
            FlowRevision = 12,
            ImageIdentity = "image-hash",
            Mode = "Affine",
            Unit = "mm",
            SolverOptions = new NPointCalibrationDraftSolverOptionsDto
            {
                MaxAcceptedReprojectionError = 0.05,
                MinInlierCount = 4,
                MinInlierRatio = 1.0
            },
            Samples =
            [
                CreateSample("s1", 1, 0, 0, 5, -4),
                CreateSample("s2", 2, 10, 0, 25, -4),
                CreateSample("s3", 3, 0, 20, 5, 56),
                CreateSample("s4", 4, 10, 20, 25, 56),
                CreateSample("disabled", 5, 100, 100, 999, 999, enabled: false),
                CreateSample("invalid", 6, double.NaN, 30, 0, 0)
            ]
        };

        var response = CalibrationDraftEndpoints.SolveDraft(request, store);

        response.Success.Should().BeTrue();
        response.Status.Should().Be("Solved");
        response.DraftOnly.Should().BeTrue();
        response.NotSavedToProjectAssets.Should().BeTrue();
        response.LastSolveResult.Should().NotBeNull();
        response.LastSolveResult!.TransformModel.Should().Be("Affine");
        response.LastSolveResult.TotalSampleCount.Should().Be(4);
        response.LastSolveResult.InlierCount.Should().Be(4);
        response.LastSolveResult.Accepted.Should().BeTrue();
        response.CandidateBundle.Should().NotBeNull();
        response.CandidateBundle!.Quality.Accepted.Should().BeTrue();
        response.CandidateBundleJson.Should().Contain("NPointCalibrationDraftWorkbench");

        response.Samples.Should().HaveCount(6);
        response.Samples.Where(sample => sample.SampleId is "s1" or "s2" or "s3" or "s4")
            .Should()
            .OnlyContain(sample =>
                sample.Inlier == true &&
                sample.Error.HasValue &&
                sample.Error.Value < 1e-6);
        response.Samples.Single(sample => sample.SampleId == "disabled").Inlier.Should().BeNull();
        response.Samples.Single(sample => sample.SampleId == "invalid").ValidationMessage.Should().Contain("finite");

        response.Artifacts.Select(artifact => artifact.Role)
            .Should()
            .BeEquivalentTo(
                "calibration-draft-session.v1",
                "calibration-sample-table.v1",
                "calibration-candidate-bundle.v1");
        response.Artifacts.Should().OnlyContain(artifact => artifact.ContentType == "application/json");
        response.Artifacts.Should().OnlyContain(artifact => artifact.Length <= 512 * 1024);

        var draftArtifact = response.Artifacts.Single(artifact => artifact.Role == "calibration-draft-session.v1");
        store.TryRead(draftArtifact.ArtifactId, out var draftRead).Should().BeTrue();
        Encoding.UTF8.GetString(draftRead!.Bytes).Should().Contain("\"notSavedToProjectAssets\":true");

        response.Observation.Should().NotBeNull();
        response.Observation!.VisualScene.Should().NotBeNull();
        response.Observation.VisualScene!.Primitives.Should().Contain(primitive =>
            primitive.Layer == "calibration-reprojection" &&
            primitive.ResultPath == "$[\"samples\"][0]");
    }

    private static NPointCalibrationDraftSampleDto CreateSample(
        string sampleId,
        int order,
        double pixelX,
        double pixelY,
        double worldX,
        double worldY,
        bool enabled = true) =>
        new()
        {
            SampleId = sampleId,
            Order = order,
            PixelX = pixelX,
            PixelY = pixelY,
            WorldX = worldX,
            WorldY = worldY,
            Enabled = enabled
        };
}
