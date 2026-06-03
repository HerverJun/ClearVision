using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public interface IVisionAgentTemporaryFrameStore
{
    string Store(byte[] frameBytes, VisionAgentTemporaryFrameMetadata metadata);

    bool TryGet(string temporaryFrameId, out VisionAgentTemporaryFrame frame);

    bool Remove(string temporaryFrameId);

    int CleanupExpired();

    VisionAgentTemporaryFrameStoreStats GetStats();
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
    public DateTimeOffset StoredAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed class VisionAgentTemporaryFrameStoreOptions
{
    public int MaxFrameCount { get; set; } = 16;
    public long MaxTotalBytes { get; set; } = 64L * 1024 * 1024;
    public long MaxSingleFrameBytes { get; set; } = 16L * 1024 * 1024;
    public int TtlSeconds { get; set; } = 600;
    public bool RemoveAfterReplay { get; set; } = true;
    public int CleanupIntervalSeconds { get; set; } = 60;

    public void Normalize()
    {
        MaxFrameCount = Math.Max(1, MaxFrameCount);
        MaxTotalBytes = Math.Max(1, MaxTotalBytes);
        MaxSingleFrameBytes = Math.Clamp(MaxSingleFrameBytes, 1, MaxTotalBytes);
        TtlSeconds = Math.Max(1, TtlSeconds);
        CleanupIntervalSeconds = Math.Max(1, CleanupIntervalSeconds);
    }
}

public sealed record VisionAgentTemporaryFrameStoreStats
{
    public int FrameCount { get; init; }
    public long TotalBytes { get; init; }
    public int MaxFrameCount { get; init; }
    public long MaxTotalBytes { get; init; }
    public long MaxSingleFrameBytes { get; init; }
    public int TtlSeconds { get; init; }
    public bool RemoveAfterReplay { get; init; }
}

public sealed class VisionAgentTemporaryFrameStore : IVisionAgentTemporaryFrameStore, IDisposable
{
    private readonly ConcurrentDictionary<string, VisionAgentTemporaryFrame> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly VisionAgentTemporaryFrameStoreOptions _options;
    private readonly Timer _cleanupTimer;
    private readonly object _evictionGate = new();
    private long _totalBytes;
    private bool _disposed;

    public VisionAgentTemporaryFrameStore()
        : this(Options.Create(new VisionAgentTemporaryFrameStoreOptions()))
    {
    }

    public VisionAgentTemporaryFrameStore(IOptions<VisionAgentTemporaryFrameStoreOptions> options)
    {
        _options = options.Value;
        _options.Normalize();
        _cleanupTimer = new Timer(
            _ => CleanupExpired(),
            null,
            TimeSpan.FromSeconds(_options.CleanupIntervalSeconds),
            TimeSpan.FromSeconds(_options.CleanupIntervalSeconds));
    }

    public string Store(byte[] frameBytes, VisionAgentTemporaryFrameMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(frameBytes);
        if (frameBytes.Length > _options.MaxSingleFrameBytes)
        {
            throw new InvalidOperationException(
                $"Temporary frame exceeds MaxSingleFrameBytes={_options.MaxSingleFrameBytes}.");
        }

        lock (_evictionGate)
        {
            CleanupExpiredCore(DateTimeOffset.UtcNow);
            EvictUntilWithinLimits(frameBytes.Length);

            var id = $"tmp_frame_{Guid.NewGuid():N}";
            var now = DateTimeOffset.UtcNow;
            var stored = new VisionAgentTemporaryFrame
            {
                TemporaryFrameId = id,
                Bytes = frameBytes,
                Metadata = metadata,
                StoredAtUtc = now,
                ExpiresAtUtc = now.AddSeconds(_options.TtlSeconds)
            };
            _frames[id] = stored;
            _totalBytes += frameBytes.Length;
            return id;
        }
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
            Remove(found.TemporaryFrameId);
            return false;
        }

        frame = found;
        return true;
    }

    public bool Remove(string temporaryFrameId)
    {
        if (string.IsNullOrWhiteSpace(temporaryFrameId))
        {
            return false;
        }

        lock (_evictionGate)
        {
            return RemoveCore(temporaryFrameId.Trim());
        }
    }

    public int CleanupExpired()
    {
        lock (_evictionGate)
        {
            return CleanupExpiredCore(DateTimeOffset.UtcNow);
        }
    }

    public VisionAgentTemporaryFrameStoreStats GetStats()
    {
        return new VisionAgentTemporaryFrameStoreStats
        {
            FrameCount = _frames.Count,
            TotalBytes = Interlocked.Read(ref _totalBytes),
            MaxFrameCount = _options.MaxFrameCount,
            MaxTotalBytes = _options.MaxTotalBytes,
            MaxSingleFrameBytes = _options.MaxSingleFrameBytes,
            TtlSeconds = _options.TtlSeconds,
            RemoveAfterReplay = _options.RemoveAfterReplay
        };
    }

    private int CleanupExpiredCore(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var item in _frames.Values.Where(item => item.ExpiresAtUtc <= now).ToList())
        {
            if (RemoveCore(item.TemporaryFrameId))
            {
                removed++;
            }
        }

        return removed;
    }

    private void EvictUntilWithinLimits(long incomingBytes)
    {
        while ((_frames.Count >= _options.MaxFrameCount ||
                Interlocked.Read(ref _totalBytes) + incomingBytes > _options.MaxTotalBytes) &&
               _frames.Count > 0)
        {
            var victim = _frames.Values
                .OrderBy(item => item.ExpiresAtUtc)
                .ThenBy(item => item.StoredAtUtc)
                .FirstOrDefault();
            if (victim == null || !RemoveCore(victim.TemporaryFrameId))
            {
                break;
            }
        }

        if (Interlocked.Read(ref _totalBytes) + incomingBytes > _options.MaxTotalBytes)
        {
            throw new InvalidOperationException(
                $"Temporary frame store cannot fit frame within MaxTotalBytes={_options.MaxTotalBytes}.");
        }
    }

    private bool RemoveCore(string temporaryFrameId)
    {
        if (!_frames.TryRemove(temporaryFrameId, out var removed))
        {
            return false;
        }

        Interlocked.Add(ref _totalBytes, -removed.Bytes.Length);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cleanupTimer.Dispose();
        _disposed = true;
    }
}

