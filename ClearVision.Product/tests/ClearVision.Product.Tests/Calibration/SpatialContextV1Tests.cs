using System.Text.Json;
using ClearVision.Product.Infrastructure.Calibration;
using FluentAssertions;

namespace ClearVision.Product.Tests.Calibration;

public sealed class SpatialContextV1Tests
{
    [Fact]
    public void DefaultImageFull_ShouldRepresentLegacyImagesAsImageFullPixels()
    {
        var context = SpatialContextV1.DefaultImageFull();

        context.SchemaVersion.Should().Be(1);
        context.CurrentFrame.Kind.Should().Be(SpatialFrameKindV1.ImageFull);
        context.CurrentFrame.Unit.Should().Be(SpatialUnitV1.Pixel);
        context.CurrentFrame.UnitSymbol.Should().Be("px");
        context.Binding.TryValidate(out var error).Should().BeTrue(error);

        context.TryResolveTransform(context.CurrentFrame, context.CurrentFrame, out var identity, out error)
            .Should().BeTrue(error);
        identity.TryApply(12.5, 8.25, out var x, out var y, out error).Should().BeTrue(error);
        x.Should().BeApproximately(12.5, 1e-12);
        y.Should().BeApproximately(8.25, 1e-12);
    }

    [Fact]
    public void Transform_ShouldApplyAndInvertWithTargetEqualsTSourceDirection()
    {
        var image = FrameRefV1.ImageFull();
        var world = FrameRefV1.World2D();
        var matrix = new[]
        {
            new[] { 0.02, 0.0, 1.0 },
            new[] { 0.0, 0.02, 2.0 },
            new[] { 0.0, 0.0, 1.0 }
        };

        SpatialTransform2DV1.TryCreate(image, world, matrix, out var transform, out var error).Should().BeTrue(error);

        transform.TryApply(100, 50, out var worldX, out var worldY, out error).Should().BeTrue(error);
        worldX.Should().BeApproximately(3.0, 1e-12);
        worldY.Should().BeApproximately(3.0, 1e-12);

        transform.TryInverse(out var inverse, out error).Should().BeTrue(error);
        inverse.TryApply(worldX, worldY, out var imageX, out var imageY, out error).Should().BeTrue(error);
        imageX.Should().BeApproximately(100, 1e-9);
        imageY.Should().BeApproximately(50, 1e-9);
    }

    [Fact]
    public void Context_ShouldResolveMultiLevelRoiLocalToWorldTransform()
    {
        var roi = FrameRefV1.RoiLocal("roi.local.node-a", "image.full");
        var image = FrameRefV1.ImageFull();
        var world = FrameRefV1.World2D();

        var localToImage = new SpatialTransform2DV1(
            roi,
            image,
            [
                [1, 0, 30],
                [0, 1, 40],
                [0, 0, 1]
            ]);
        var imageToWorld = new SpatialTransform2DV1(
            image,
            world,
            [
                [0.5, 0, 0],
                [0, 0.5, 0],
                [0, 0, 1]
            ]);
        var context = new SpatialContextV1(roi, [localToImage, imageToWorld]);

        context.TryResolveTransform(roi, world, out var localToWorld, out var error).Should().BeTrue(error);
        localToWorld.TryApply(10, 20, out var x, out var y, out error).Should().BeTrue(error);

        x.Should().BeApproximately(20, 1e-12);
        y.Should().BeApproximately(30, 1e-12);
    }

    [Fact]
    public void Compose_ShouldFailClosedOnFrameMismatch()
    {
        var roi = FrameRefV1.RoiLocal("roi.local.node-a", "image.full");
        var image = FrameRefV1.ImageFull();
        var undistorted = FrameRefV1.Undistorted();
        var world = FrameRefV1.World2D();

        var localToImage = new SpatialTransform2DV1(roi, image, SpatialTransform2DV1.CreateIdentity3x3());
        var undistortedToWorld = new SpatialTransform2DV1(undistorted, world, SpatialTransform2DV1.CreateIdentity3x3());

        SpatialTransform2DV1.TryCompose(localToImage, undistortedToWorld, out _, out var error).Should().BeFalse();
        error.Should().Contain("Frame mismatch");
    }

    [Fact]
    public void FrameRef_ShouldRejectForbiddenUnitCombinations()
    {
        FrameRefV1.TryCreate(
                "bad.image",
                SpatialFrameKindV1.ImageFull,
                SpatialUnitV1.Millimeter,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Contain("does not support unit");

        FrameRefV1.TryCreate(
                "bad.world",
                SpatialFrameKindV1.World2D,
                SpatialUnitV1.Pixel,
                out _,
                out error)
            .Should().BeFalse();
        error.Should().Contain("does not support unit");
    }

    [Theory]
    [InlineData(SpatialUnitV1.Meter, "m")]
    [InlineData(SpatialUnitV1.Centimeter, "cm")]
    [InlineData(SpatialUnitV1.Micrometer, "um")]
    public void FrameRef_ShouldSerializePhysicalWorld2DUnits(SpatialUnitV1 unit, string symbol)
    {
        var frame = FrameRefV1.World2D(unit: unit);

        frame.Unit.Should().Be(unit);
        frame.UnitSymbol.Should().Be(symbol);
    }

    [Fact]
    public void Transform_ShouldRejectUnitlessToPixelAndNonFiniteMatrix()
    {
        var unitlessImage = new FrameRefV1("image.normalized", SpatialFrameKindV1.ImageFull, SpatialUnitV1.Unitless);
        var pixelImage = FrameRefV1.ImageFull();

        SpatialTransform2DV1.TryCreate(
                unitlessImage,
                pixelImage,
                SpatialTransform2DV1.CreateIdentity3x3(),
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Contain("unit combination");

        SpatialTransform2DV1.TryCreate(
                pixelImage,
                FrameRefV1.World2D(),
                [
                    [1, 0, double.NaN],
                    [0, 1, 0],
                    [0, 0, 1]
                ],
                out _,
                out error)
            .Should().BeFalse();
        error.Should().Contain("NaN");
    }

    [Fact]
    public void Inverse_ShouldFailClosedForSingularMatrix()
    {
        var transform = new SpatialTransform2DV1(
            FrameRefV1.ImageFull(),
            FrameRefV1.World2D(),
            [
                [1, 0, 0],
                [0, 0, 0],
                [0, 0, 1]
            ]);

        transform.TryInverse(out _, out var error).Should().BeFalse();
        error.Should().Contain("singular");
    }

    [Fact]
    public void Binding_ShouldValidateOutputAndArtifactIdentity()
    {
        var projectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var operatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var outputPortId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var debugSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var outputBinding = SpatialContextBindingV1.ForFlowOutput(
            operatorId,
            outputPortId,
            "Image",
            projectId,
            "run-0001");
        outputBinding.TryValidate(out var error).Should().BeTrue(error);
        outputBinding.HasFlowOutputBinding.Should().BeTrue();
        outputBinding.HasPreviewArtifactBinding.Should().BeFalse();

        var artifactBinding = SpatialContextBindingV1.ForPreviewArtifact(
            projectId,
            operatorId,
            debugSessionId,
            7,
            9,
            "artifact_abc-123",
            outputPortId,
            "Image");
        artifactBinding.TryValidate(out error).Should().BeTrue(error);
        artifactBinding.HasPreviewArtifactBinding.Should().BeTrue();

        var invalid = artifactBinding with { ArtifactId = "../secret" };
        invalid.TryValidate(out error).Should().BeFalse();
        error.Should().Contain("ArtifactId");
    }

    [Fact]
    public void SpatialContext_ShouldJsonRoundTripAsVersionedSidecarContract()
    {
        var roi = FrameRefV1.RoiLocal("roi.local.node-a", "image.full");
        var image = FrameRefV1.ImageFull();
        var operatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var outputPortId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var context = new SpatialContextV1(
            roi,
            [
                new SpatialTransform2DV1(
                    roi,
                    image,
                    [
                        [1, 0, 12],
                        [0, 1, 34],
                        [0, 0, 1]
                    ])
            ],
            SpatialContextBindingV1.ForFlowOutput(operatorId, outputPortId, "Image"));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(context, options);

        json.Should().Contain("\"schemaVersion\":1");
        json.Should().Contain("\"currentFrame\"");
        json.Should().Contain("\"transforms\"");

        var roundTrip = JsonSerializer.Deserialize<SpatialContextV1>(json, options);
        roundTrip.Should().NotBeNull();
        roundTrip!.SchemaVersion.Should().Be(1);
        roundTrip.CurrentFrame.Should().Be(roi);
        roundTrip.Transforms.Should().ContainSingle();
        roundTrip.Binding.OutputPortId.Should().Be(outputPortId);
    }

    [Fact]
    public void ImageWrapperSource_ShouldRemainFreeOfSpatialContextMetadata()
    {
        var root = FindRepositoryRoot();
        var imageWrapperPath = Path.Combine(
            root,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Operators",
            "ImageWrapper.cs");

        var text = File.ReadAllText(imageWrapperPath);
        text.Should().NotContain("SpatialContext");
        text.Should().NotContain("FrameRef");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClearVision.Product", "ClearVision.Product.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
