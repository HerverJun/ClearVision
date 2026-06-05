using System.Security.Cryptography;
using System.Text;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewArtifactStore
{
    public RuntimePreviewArtifactSummary CreateFrameMetadata(
        string sourceTool,
        string frameId,
        string operatorTempId,
        string cameraBindingId)
    {
        return Create(
            "frame_metadata",
            sourceTool,
            frameId,
            new
            {
                frameId,
                operatorTempId = SafeToken(operatorTempId),
                cameraBinding = SafeToken(cameraBindingId),
                fixtureKind = "offline_frame_metadata"
            });
    }

    public RuntimePreviewArtifactSummary CreateOperatorResultMetadata(
        string sourceTool,
        string frameId,
        string tempId,
        string operatorType,
        string status,
        int index)
    {
        return Create(
            "operator_result_metadata",
            sourceTool,
            $"operator-result-{StableSuffix(frameId, tempId, operatorType, index.ToString())}",
            new
            {
                frameId,
                tempId = SafeToken(tempId),
                operatorType = SafeToken(operatorType),
                status = SafeToken(status),
                produced = ProducedToken(operatorType)
            });
    }

    public RuntimePreviewArtifactSummary CreateReplaySummaryMetadata(
        string sourceTool,
        string frameId,
        int executedCount,
        int skippedCount,
        int blockingIssueCount)
    {
        return Create(
            "replay_summary_metadata",
            sourceTool,
            $"replay-summary-{StableSuffix(frameId, executedCount.ToString(), skippedCount.ToString(), blockingIssueCount.ToString())}",
            new
            {
                frameId,
                executedCount,
                skippedCount,
                blockingIssueCount,
                generatedRealImages = false,
                loadedModelFiles = false,
                accessedHardware = false,
                stationTouched = false
            });
    }

    private static RuntimePreviewArtifactSummary Create(
        string artifactType,
        string sourceTool,
        string artifactId,
        object metadata)
    {
        return new RuntimePreviewArtifactSummary
        {
            ArtifactId = artifactId,
            ArtifactType = artifactType,
            SourceTool = sourceTool,
            MetadataOnly = true,
            BinaryIncluded = false,
            ByteLength = 0,
            Metadata = metadata
        };
    }

    public static string StableSuffix(params string[] values)
    {
        var normalized = string.Join("|", values.Select(value => value.Trim().ToUpperInvariant()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unspecified";
        }

        var trimmed = value.Trim();
        if (LooksLikePath(trimmed) ||
            trimmed.Contains("base64", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Length > 80)
        {
            return "<redacted>";
        }

        return trimmed;
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(":\\", StringComparison.Ordinal) ||
               value.Contains(":/", StringComparison.Ordinal) ||
               value.Contains("\\", StringComparison.Ordinal) ||
               value.Contains("/", StringComparison.Ordinal);
    }

    private static string ProducedToken(string operatorType)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "offline_frame_token",
            "TemplateMatching" => "offline_template_match_metadata",
            "DeepLearning" => "offline_model_inference_metadata",
            "CircleMeasurement" => "offline_measurement_metadata",
            "MeasureDistance" => "offline_distance_metadata",
            "ResultOutput" => "offline_output_metadata",
            _ => "offline_operator_metadata"
        };
    }
}
