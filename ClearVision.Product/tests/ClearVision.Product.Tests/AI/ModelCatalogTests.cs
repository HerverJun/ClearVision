using ClearVision.Product.Infrastructure.AI.Runtime;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class ModelCatalogTests
{
    [Fact]
    public void Load_ShouldFindDefaultCatalog()
    {
        var catalog = ModelCatalog.Load();

        catalog.Models.Should().NotBeEmpty();
        catalog.Models.Should().Contain(x => x.Id == "semantic_identity_2x2");
        catalog.Models.Should().Contain(x => x.Id == "anomaly_embedding_identity_2x2");
        catalog.Models.Should().Contain(x => x.Id == "coco_yolo_external_onnx");
    }

    [Fact]
    public void ResolveExplicitOrCatalogPath_WithModelId_ShouldReturnExistingPath()
    {
        var resolved = ModelCatalog.ResolveExplicitOrCatalogPath(
            explicitPath: null,
            modelId: "semantic_identity_2x2",
            catalogPath: null,
            expectedTypes: ["segmentation"],
            out var entry);

        entry.Should().NotBeNull();
        entry!.Type.Should().Be("segmentation");
        File.Exists(resolved).Should().BeTrue();
    }

    [Fact]
    public void ResolveExplicitOrCatalogPath_WithAnomalyEmbeddingModelId_ShouldReturnExistingPath()
    {
        var resolved = ModelCatalog.ResolveExplicitOrCatalogPath(
            explicitPath: null,
            modelId: "anomaly_embedding_identity_2x2",
            catalogPath: null,
            expectedTypes: ["anomaly_embedding"],
            out var entry);

        entry.Should().NotBeNull();
        entry!.Type.Should().Be("anomaly_embedding");
        File.Exists(resolved).Should().BeTrue();
    }

    [Fact]
    public void ResolveExplicitOrCatalog_WithModelId_ShouldReturnCatalogProvenance()
    {
        var resolved = ModelCatalog.ResolveExplicitOrCatalog(
            explicitPath: null,
            modelId: "semantic_identity_2x2",
            catalogPath: null,
            expectedTypes: ["segmentation"]);

        resolved.Source.Should().Be("ModelCatalog");
        resolved.ModelId.Should().Be("semantic_identity_2x2");
        resolved.Entry.Should().NotBeNull();
        File.Exists(resolved.ResolvedPath).Should().BeTrue();

        var provenance = resolved.ToProvenancePayload();
        provenance["ResolutionSource"].Should().Be("ModelCatalog");
        provenance["ModelType"].Should().Be("segmentation");
        provenance["ModelVersion"].Should().Be("1.0.0");
    }

    [Fact]
    public void Load_WithObjectDetectionManifestFields_ShouldPreserveProvenance()
    {
        var catalog = ModelCatalog.Load();
        var entry = catalog.Models.Single(x => x.Id == "coco_yolo_external_onnx");

        entry.Type.Should().Be("object_detection");
        entry.InputShape.Should().Equal(1, 3, 640, 640);
        entry.ResolvedClassNames.Should().HaveCount(80);
        entry.Preprocess.Should().ContainKey("resize");
        entry.Postprocess.Should().ContainKey("yoloVersion");

        var resolved = ModelCatalog.ResolveExplicitOrCatalog(
            explicitPath: null,
            modelId: "coco_yolo_external_onnx",
            catalogPath: null,
            expectedTypes: ["object_detection"]);

        var provenance = resolved.ToProvenancePayload();
        provenance["ModelType"].Should().Be("object_detection");
        provenance["ClassNames"].Should().BeEquivalentTo(entry.ResolvedClassNames);
        provenance["InputShape"].Should().BeEquivalentTo(new[] { 1, 3, 640, 640 });
    }
}
