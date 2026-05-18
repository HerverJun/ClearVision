using System.Linq;
using Microsoft.AspNetCore.Http;
using OpenCvSharp;

namespace Acme.Product.Desktop.Endpoints;

internal static class ImagePayloadDecoder
{
    public const int MaxImageBytes = 25 * 1024 * 1024;

    private const int MaxImageBase64Chars = ((MaxImageBytes + 2) / 3) * 4;
    private const int MaxImagePayloadChars = MaxImageBase64Chars + 1024 * 1024;

    public static bool TryDecodeBytes(
        string? dataBase64,
        string fieldName,
        out byte[] imageData,
        out string errorMessage,
        out int statusCode)
    {
        imageData = [];
        errorMessage = string.Empty;
        statusCode = StatusCodes.Status400BadRequest;

        if (string.IsNullOrWhiteSpace(dataBase64))
        {
            errorMessage = $"{fieldName} is required.";
            return false;
        }

        var payload = dataBase64.Trim();
        if (payload.Length > MaxImagePayloadChars)
        {
            statusCode = StatusCodes.Status413PayloadTooLarge;
            errorMessage = BuildSizeError();
            return false;
        }

        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = payload.IndexOf(',');
            if (commaIndex < 0 || commaIndex == payload.Length - 1)
            {
                errorMessage = $"{fieldName} data URL is invalid.";
                return false;
            }

            payload = payload[(commaIndex + 1)..];
        }

        var base64CharCount = 0;
        var paddingCount = 0;
        foreach (var ch in payload)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            base64CharCount++;
            if (ch == '=')
            {
                paddingCount++;
            }

            if (base64CharCount > MaxImageBase64Chars)
            {
                statusCode = StatusCodes.Status413PayloadTooLarge;
                errorMessage = BuildSizeError();
                return false;
            }
        }

        if (base64CharCount == 0)
        {
            errorMessage = $"{fieldName} is required.";
            return false;
        }

        var estimatedBytes = (((base64CharCount + 3) / 4) * 3) - Math.Min(paddingCount, 2);
        if (estimatedBytes > MaxImageBytes)
        {
            statusCode = StatusCodes.Status413PayloadTooLarge;
            errorMessage = BuildSizeError();
            return false;
        }

        var compactPayload = new string(payload.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        try
        {
            imageData = Convert.FromBase64String(compactPayload);
        }
        catch (FormatException)
        {
            errorMessage = $"{fieldName} format is invalid.";
            return false;
        }

        if (imageData.Length > MaxImageBytes)
        {
            imageData = [];
            statusCode = StatusCodes.Status413PayloadTooLarge;
            errorMessage = BuildSizeError();
            return false;
        }

        return true;
    }

    public static bool TryDecodeImage(
        string? imageBase64,
        out Mat image,
        out string errorMessage,
        out int statusCode)
    {
        image = new Mat();
        if (!TryDecodeBytes(imageBase64, "ImageBase64", out var imageData, out errorMessage, out statusCode))
        {
            return false;
        }

        try
        {
            image = Cv2.ImDecode(imageData, ImreadModes.Color);
            if (image.Empty())
            {
                image.Dispose();
                errorMessage = "Image decoding failed.";
                statusCode = StatusCodes.Status400BadRequest;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            statusCode = StatusCodes.Status400BadRequest;
            return false;
        }
    }

    public static IResult ToErrorResult(string errorMessage, int statusCode)
    {
        return statusCode == StatusCodes.Status413PayloadTooLarge
            ? Results.Json(new { Error = errorMessage }, statusCode: StatusCodes.Status413PayloadTooLarge)
            : Results.BadRequest(new { Error = errorMessage });
    }

    private static string BuildSizeError()
    {
        return $"Image upload exceeds the {MaxImageBytes} byte limit.";
    }
}
