using System.Text.Json.Serialization;

namespace ClearVision.Product.Core.AI.Tools;

public record RuntimePreviewArtifactSummary
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; init; } = "metadata";

    [JsonPropertyName("sourceTool")]
    public string SourceTool { get; init; } = string.Empty;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("binaryIncluded")]
    public bool BinaryIncluded { get; init; }

    [JsonPropertyName("byteLength")]
    public long ByteLength { get; init; }

    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }
}

public sealed record RuntimePreviewArtifactMetadata : RuntimePreviewArtifactSummary;
