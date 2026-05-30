namespace Acme.Product.Application.Services;

public static class InspectionImageFormatDetector
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static string GuessExtension(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 2)
        {
            return ".bin";
        }

        if (bytes.AsSpan().StartsWith(PngSignature))
        {
            return ".png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return ".bin";
    }

    public static string GuessFormat(byte[]? bytes)
    {
        return GuessExtension(bytes).TrimStart('.');
    }
}
