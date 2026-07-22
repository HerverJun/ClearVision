using ClearVision.Product.Application.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class InspectionImageFormatDetectorTests
{
    [Theory]
    [MemberData(nameof(ImageSamples))]
    public void GuessExtension_ShouldDetectKnownSignatures(byte[]? bytes, string expectedExtension)
    {
        var extension = InspectionImageFormatDetector.GuessExtension(bytes);

        extension.Should().Be(expectedExtension);
    }

    [Theory]
    [MemberData(nameof(ImageSamples))]
    public void GuessFormat_ShouldReturnExtensionWithoutDot(byte[]? bytes, string expectedExtension)
    {
        var format = InspectionImageFormatDetector.GuessFormat(bytes);

        format.Should().Be(expectedExtension.TrimStart('.'));
    }

    [Theory]
    [MemberData(nameof(ImageContentTypes))]
    public void GuessContentType_ShouldReturnBrowserCompatibleType(byte[]? bytes, string expectedContentType)
    {
        var contentType = InspectionImageFormatDetector.GuessContentType(bytes);

        contentType.Should().Be(expectedContentType);
    }

    public static TheoryData<byte[]?, string> ImageSamples()
    {
        return new TheoryData<byte[]?, string>
        {
            { null, ".bin" },
            { [], ".bin" },
            { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], ".png" },
            { [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00, 0x00], ".bin" },
            { [0xFF, 0xD8], ".bin" },
            { [0xFF, 0xD8, 0xFF], ".jpg" },
            { [0x42, 0x4D], ".bmp" },
            { [0x01, 0x02, 0x03], ".bin" }
        };
    }

    public static TheoryData<byte[]?, string> ImageContentTypes()
    {
        return new TheoryData<byte[]?, string>
        {
            { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png" },
            { [0xFF, 0xD8, 0xFF], "image/jpeg" },
            { [0x42, 0x4D], "image/bmp" },
            { [0x01, 0x02, 0x03], "application/octet-stream" }
        };
    }
}
