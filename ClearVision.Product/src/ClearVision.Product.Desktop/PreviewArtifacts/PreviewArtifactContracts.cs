using System.Text.Json.Serialization;

namespace ClearVision.Product.Desktop.PreviewArtifacts;

public sealed class PreviewArtifactReferenceV1
{
    public string ArtifactId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string PathHint { get; init; } = "$";
    public string ContentType { get; init; } = "application/octet-stream";
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Channels { get; init; }
}

public readonly record struct PreviewArtifactOwnerScope(
    Guid ProjectId,
    Guid TargetNodeId,
    Guid DebugSessionId,
    long? ClientRequestSequence,
    long? FlowRevision);

public sealed class PreviewArtifactStoreOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(10);
    public int MaxEntries { get; init; } = 256;
    public long MaxTotalBytes { get; init; } = 128L * 1024L * 1024L;
    public long MaxEntryBytes { get; init; } = 32L * 1024L * 1024L;
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);
}

public interface IPreviewArtifactClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemPreviewArtifactClock : IPreviewArtifactClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class PreviewArtifactReadResult
{
    internal PreviewArtifactReadResult(
        PreviewArtifactReferenceV1 reference,
        byte[] bytes)
    {
        Reference = reference;
        Bytes = bytes;
    }

    public PreviewArtifactReferenceV1 Reference { get; }
    public byte[] Bytes { get; }
    public string ContentType => Reference.ContentType;
    public long Length => Reference.Length;
    public string Sha256 => Reference.Sha256;
}

public sealed class PreviewArtifactValue
{
    public required string Kind { get; init; }
    public required string DisplayValue { get; init; }
    public required string PathHint { get; init; }
    public string? OriginalType { get; init; }
    public bool Truncated { get; init; } = true;
    public PreviewArtifactReferenceV1? Artifact { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> ToLegacyDictionary()
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = Kind,
            ["displayValue"] = DisplayValue,
            ["truncated"] = Truncated
        };

        if (Artifact != null)
        {
            result["artifact"] = Artifact;
        }

        foreach (var pair in Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }
}

public sealed class PreviewArtifactMaterializationResult : IDisposable
{
    private PreviewArtifactBatch? _batch;
    private bool _committed;

    internal PreviewArtifactMaterializationResult(
        PreviewArtifactBatch batch,
        Dictionary<string, object> outputData,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics)
    {
        _batch = batch;
        OutputData = outputData;
        Artifacts = artifacts;
        Diagnostics = diagnostics;
    }

    public Dictionary<string, object> OutputData { get; }
    public List<PreviewArtifactReferenceV1> Artifacts { get; }
    public List<string> Diagnostics { get; }

    public void Commit()
    {
        if (_committed)
        {
            return;
        }

        _batch?.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (!_committed)
        {
            _batch?.Rollback();
        }

        _batch?.Dispose();
        _batch = null;
    }
}
