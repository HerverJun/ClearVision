using ClearVision.Product.Infrastructure.Utilities;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Utilities;

public sealed class ImageStreamUtilityTests
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void MatToCompressedBase64_WhenMaxHeightApplied_ShouldPreserveAspectRatio()
    {
        using var image = new Mat(200, 400, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(image, format: "png", maxHeight: 100);
        var bytes = Convert.FromBase64String(encoded);
        using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);

        decoded.Width.Should().Be(200);
        decoded.Height.Should().Be(100);
    }

    [Fact]
    public void MatToCompressedBase64_WhenMaxWidthApplied_ShouldRoundPreservedAspectRatio()
    {
        using var image = new Mat(67, 100, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(image, format: "png", maxWidth: 50);
        var bytes = Convert.FromBase64String(encoded);
        using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);

        decoded.Width.Should().Be(50);
        decoded.Height.Should().Be(34);
    }

    [Theory]
    [InlineData(100, 65, 50, null, 50, 33)]
    [InlineData(65, 100, null, 50, 33, 50)]
    public void MatToCompressedBase64_WhenScaledDimensionIsAtMidpoint_ShouldRoundAwayFromZero(
        int sourceWidth,
        int sourceHeight,
        int? maxWidth,
        int? maxHeight,
        int expectedWidth,
        int expectedHeight)
    {
        using var image = new Mat(sourceHeight, sourceWidth, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(
            image,
            format: "png",
            maxWidth: maxWidth,
            maxHeight: maxHeight);
        var bytes = Convert.FromBase64String(encoded);
        using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);

        decoded.Width.Should().Be(expectedWidth);
        decoded.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void MatToCompressedBase64_WhenFormatHasLeadingDot_ShouldRespectFormat()
    {
        using var image = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(image, format: ".png");
        var bytes = Convert.FromBase64String(encoded);

        bytes.Should().StartWith(PngSignature);
    }

    [Fact]
    public void MatToCompressedBase64_WhenMaxBoundsAreNonPositive_ShouldKeepOriginalSize()
    {
        using var image = new Mat(12, 16, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(
            image,
            format: "png",
            maxWidth: 0,
            maxHeight: -1);
        var bytes = Convert.FromBase64String(encoded);
        using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);

        decoded.Width.Should().Be(16);
        decoded.Height.Should().Be(12);
    }

    [Fact]
    public void CompressAndEncodeToBase64_WhenFormatHasLeadingDot_ShouldRespectFormat()
    {
        using var image = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);
        var sourceBytes = image.ToBytes(".png");

        var encoded = ImageStreamUtility.CompressAndEncodeToBase64(sourceBytes, format: ".png");
        var bytes = Convert.FromBase64String(encoded);

        bytes.Should().StartWith(PngSignature);
    }

    [Fact]
    public void MatToCompressedBase64_WhenFormatIsMimeType_ShouldRespectFormat()
    {
        using var image = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(image, format: "image/png");
        var bytes = Convert.FromBase64String(encoded);

        bytes.Should().StartWith(PngSignature);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(500)]
    public void MatToCompressedBase64_WhenJpegQualityIsOutOfRange_ShouldStillEncode(int quality)
    {
        using var image = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);

        var encoded = ImageStreamUtility.MatToCompressedBase64(image, quality: quality);
        var bytes = Convert.FromBase64String(encoded);

        bytes.Should().StartWith(JpegSignature);
    }

    [Theory]
    [InlineData(3, 4, false)]
    [InlineData(4, 4, false)]
    [InlineData(5, 4, true)]
    public void ShouldCompress_ShouldOnlyCompressWhenImageExceedsLimit(
        int imageSize,
        long maxSizeBytes,
        bool expected)
    {
        var imageData = new byte[imageSize];

        var shouldCompress = ImageStreamUtility.ShouldCompress(imageData, maxSizeBytes);

        shouldCompress.Should().Be(expected);
    }

    [Theory]
    [InlineData(1000, ".jpg", 500, 100)]
    [InlineData(1000, "jpeg", 80, 80)]
    [InlineData(1000, "image/jpeg", 80, 80)]
    [InlineData(1000, "jpg", -10, 0)]
    [InlineData(1000, ".png", 80, 500)]
    [InlineData(1000, "image/png", 80, 500)]
    [InlineData(1000, "webp", 80, 1000)]
    [InlineData(-1000, "png", 80, 0)]
    public void EstimateCompressedSize_ShouldNormalizeFormatAndClampBounds(
        long originalSize,
        string format,
        int quality,
        long expectedSize)
    {
        var estimate = ImageStreamUtility.EstimateCompressedSize(originalSize, format, quality);

        estimate.Should().Be(expectedSize);
    }
}
