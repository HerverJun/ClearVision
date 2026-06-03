using System;
using System.Collections.Concurrent;
using System.Linq;

namespace ClearVision.Product.Infrastructure.AI.Runtime;

public static class TemporaryFrameCache
{
    private sealed class CachedFrame
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; } = string.Empty;
        public DateTime ExpireTime { get; set; }
    }

    private static readonly ConcurrentDictionary<string, CachedFrame> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Add(string id, byte[] data, int width, int height, string format, TimeSpan ttl)
    {
        // Limit total size to 50 images to prevent OOM
        if (_cache.Count >= 50)
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _cache.Where(kv => kv.Value.ExpireTime < now).Select(kv => kv.Key).ToList();
            foreach (var k in expiredKeys)
            {
                _cache.TryRemove(k, out _);
            }

            if (_cache.Count >= 50)
            {
                var oldestKeys = _cache.OrderBy(kv => kv.Value.ExpireTime).Take(10).Select(kv => kv.Key).ToList();
                foreach (var k in oldestKeys)
                {
                    _cache.TryRemove(k, out _);
                }
            }
        }

        _cache[id] = new CachedFrame
        {
            Data = data,
            Width = width,
            Height = height,
            Format = format,
            ExpireTime = DateTime.UtcNow.Add(ttl)
        };
    }

    public static bool TryGet(string id, out byte[] data, out int width, out int height, out string format)
    {
        data = Array.Empty<byte>();
        width = 0;
        height = 0;
        format = string.Empty;

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(id, out var frame))
        {
            if (frame.ExpireTime > now)
            {
                data = frame.Data;
                width = frame.Width;
                height = frame.Height;
                format = frame.Format;
                return true;
            }
            else
            {
                _cache.TryRemove(id, out _);
            }
        }
        return false;
    }

    public static void ClearExpired()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache.Where(kv => kv.Value.ExpireTime < now).Select(kv => kv.Key).ToList();
        foreach (var k in expiredKeys)
        {
            _cache.TryRemove(k, out _);
        }
    }
}
