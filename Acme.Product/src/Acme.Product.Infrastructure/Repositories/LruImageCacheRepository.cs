using Acme.Product.Core.Interfaces;

namespace Acme.Product.Infrastructure.Repositories;

public class LruImageCacheRepository : IImageCacheRepository
{
    private readonly Dictionary<Guid, CacheEntry> _cache = new();
    private readonly LinkedList<Guid> _accessOrder = new();
    private readonly object _lock = new();
    private readonly long _maxSizeInBytes;
    private long _currentSizeInBytes;
    private long _hitCount;
    private long _missCount;

    public LruImageCacheRepository(long maxSizeInBytes = 100 * 1024 * 1024)
    {
        _maxSizeInBytes = maxSizeInBytes;
    }

    public Task<Guid> AddAsync(byte[] imageData, string format)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        if (imageData.Length > _maxSizeInBytes)
        {
            throw new ArgumentException(
                $"Image size {imageData.Length} exceeds cache limit {_maxSizeInBytes}.",
                nameof(imageData));
        }

        var id = Guid.NewGuid();
        var size = imageData.Length;

        lock (_lock)
        {
            while (_currentSizeInBytes + size > _maxSizeInBytes && _accessOrder.Count > 0)
            {
                EvictLeastRecentlyUsed();
            }

            _cache[id] = new CacheEntry
            {
                Data = imageData,
                Format = format,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                SizeInBytes = size
            };
            _accessOrder.AddFirst(id);
            _currentSizeInBytes += size;
        }

        return Task.FromResult(id);
    }

    public Task<byte[]?> GetAsync(Guid id)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(id, out var entry))
            {
                entry.LastAccessedAt = DateTime.UtcNow;
                _accessOrder.Remove(id);
                _accessOrder.AddFirst(id);
                Interlocked.Increment(ref _hitCount);
                return Task.FromResult<byte[]?>(entry.Data);
            }

            Interlocked.Increment(ref _missCount);
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task DeleteAsync(Guid id)
    {
        lock (_lock)
        {
            RemoveEntry(id);
        }

        return Task.CompletedTask;
    }

    public Task CleanExpiredAsync(TimeSpan expiration)
    {
        var cutoff = DateTime.UtcNow - expiration;

        lock (_lock)
        {
            var expiredIds = _cache
                .Where(kvp => kvp.Value.LastAccessedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in expiredIds)
            {
                RemoveEntry(id);
            }
        }

        return Task.CompletedTask;
    }

    public CacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            var hits = Interlocked.Read(ref _hitCount);
            var misses = Interlocked.Read(ref _missCount);
            var total = hits + misses;

            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                CurrentSizeInBytes = _currentSizeInBytes,
                MaxSizeInBytes = _maxSizeInBytes,
                HitCount = hits,
                MissCount = misses,
                HitRate = total > 0 ? (double)hits / total : 0
            };
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _accessOrder.Clear();
            _currentSizeInBytes = 0;
        }
    }

    private void EvictLeastRecentlyUsed()
    {
        if (_accessOrder.Last is null)
        {
            return;
        }

        RemoveEntry(_accessOrder.Last.Value);
    }

    private void RemoveEntry(Guid id)
    {
        if (_cache.TryGetValue(id, out var entry))
        {
            _cache.Remove(id);
            _accessOrder.Remove(id);
            _currentSizeInBytes -= entry.SizeInBytes;
        }
    }

    private sealed class CacheEntry
    {
        public byte[] Data { get; init; } = [];
        public string Format { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public DateTime LastAccessedAt { get; set; }
        public long SizeInBytes { get; init; }
    }
}

public class CacheStatistics
{
    public int TotalEntries { get; set; }
    public long CurrentSizeInBytes { get; set; }
    public long MaxSizeInBytes { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRate { get; set; }
    public double CurrentSizeInMB => CurrentSizeInBytes / (1024.0 * 1024.0);
    public double MaxSizeInMB => MaxSizeInBytes / (1024.0 * 1024.0);
}
