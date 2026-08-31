using System.Security.Cryptography;
using System.Text.Json;

namespace ClearVision.Product.Infrastructure.Calibration;

/// <summary>
/// Adds the immutable candidate contract consumed by the governed project
/// calibration-asset save surface. Operators only produce candidates; they do
/// not persist project assets or write client-selected filesystem paths.
/// </summary>
internal static class CalibrationAssetCandidateOutput
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void AddTo(
        IDictionary<string, object> output,
        string? calibrationAssetId,
        string calibrationData)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(calibrationData);

        output["CalibrationAssetId"] = calibrationAssetId?.Trim() ?? string.Empty;
        output["CalibrationAssetCandidate"] = true;
        output["CalibrationContentHash"] = ComputePayloadHash(calibrationData);
    }

    private static string ComputePayloadHash(string calibrationData)
    {
        using var document = JsonDocument.Parse(calibrationData);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(document.RootElement, PayloadJsonOptions);
        var hash = SHA256.HashData(payloadBytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
