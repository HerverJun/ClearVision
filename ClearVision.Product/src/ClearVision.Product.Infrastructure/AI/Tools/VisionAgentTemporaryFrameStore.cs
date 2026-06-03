using System.Collections.Concurrent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public interface IVisionAgentTemporaryFrameStore
{
    string Store(byte[] frameBytes, VisionAgentTemporaryFrameMetadata metadata);

    bool TryGet(string temporaryFrameId, out VisionAgentTemporaryFrame frame);
}

public sealed record VisionAgentTemporaryFrameMetadata
{
    public string CameraBindingId { get; init; } = string.Empty;
    public string CameraId { get; init; } = string.Empty;
    public string CameraName { get; init; } = string.Empty;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? PixelFormat { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record VisionAgentTemporaryFrame
{
    public string TemporaryFrameId { get; init; } = string.Empty;
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public VisionAgentTemporaryFrameMetadata Metadata { get; init; } = new();
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed class VisionAgentTemporaryFrameStore : IVisionAgentTemporaryFrameStore
{
    private readonly ConcurrentDictionary<string, VisionAgentTemporaryFrame> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);

    public string Store(byte[] frameBytes, VisionAgentTemporaryFrameMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(frameBytes);
        CleanupExpired();

        var id = $"tmp_frame_{Guid.NewGuid():N}";
        _frames[id] = new VisionAgentTemporaryFrame
        {
            TemporaryFrameId = id,
            Bytes = frameBytes,
            Metadata = metadata,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_ttl)
        };
        return id;
    }

    public bool TryGet(string temporaryFrameId, out VisionAgentTemporaryFrame frame)
    {
        frame = null!;
        if (string.IsNullOrWhiteSpace(temporaryFrameId))
        {
            return false;
        }

        if (!_frames.TryGetValue(temporaryFrameId.Trim(), out var found))
        {
            return false;
        }

        if (found.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _frames.TryRemove(found.TemporaryFrameId, out _);
            return false;
        }

        frame = found;
        return true;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _frames.Values.Where(item => item.ExpiresAtUtc <= now))
        {
            _frames.TryRemove(item.TemporaryFrameId, out _);
        }
    }
}

