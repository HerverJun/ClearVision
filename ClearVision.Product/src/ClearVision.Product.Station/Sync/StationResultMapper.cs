using System.Text.Json;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Station.Sync;

public static class StationResultMapper
{
    private const int MaxPreviewValueLength = 512;

    public static StationResultSummaryDto ToSummary(RuntimeNormalizedResult result, StationIdentityContext identity)
    {
        return new StationResultSummaryDto
        {
            SchemaVersion = StationSyncContractDefaults.SchemaVersion,
            StationId = identity.StationId,
            LineName = identity.LineName,
            MessageId = $"result_{identity.StationId}_{Guid.NewGuid():N}",
            RunId = result.RunId,
            PackageId = result.PackageId,
            PackageName = result.PackageName,
            PackageVersion = identity.CurrentPackageVersion ?? string.Empty,
            FlowHash = result.FlowHash,
            ImageId = result.ImageId,
            Outcome = result.Outcome,
            InspectionStatus = result.InspectionStatus,
            ExecutionTimeMs = result.ExecutionTimeMs,
            DiagnosticCode = result.DiagnosticCode,
            DiagnosticMessage = Truncate(result.DiagnosticMessage, MaxPreviewValueLength),
            PrimaryOutputsPreview = BuildPrimaryOutputsPreview(result.PrimaryOutputs),
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static Dictionary<string, string?> BuildPrimaryOutputsPreview(IReadOnlyDictionary<string, object?> outputs)
    {
        var preview = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in outputs.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (LooksLikeImageKey(key) || LooksLikeSceneOrArtifactPayloadKey(key) || value is byte[])
            {
                continue;
            }

            preview[key] = FormatValue(value);
            if (preview.Count >= 32)
            {
                break;
            }
        }

        return preview;
    }

    private static bool LooksLikeImageKey(string key)
    {
        return key.Contains("image", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("bitmap", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("base64", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSceneOrArtifactPayloadKey(string key)
    {
        return key.Equals("Scene", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("VisualScene", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("OutputScene", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("ArtifactPayload", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("LargeArtifactPayload", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => Truncate(text, MaxPreviewValueLength),
            bool or int or long or float or double or decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            JsonElement element when element.ValueKind == JsonValueKind.String => Truncate(element.GetString(), MaxPreviewValueLength),
            JsonElement element when element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            JsonElement => "[complex]",
            System.Collections.IEnumerable and not string => "[sequence]",
            _ => Truncate(value.ToString(), MaxPreviewValueLength)
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
