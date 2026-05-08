using System.Collections.Concurrent;
using System.Threading.Channels;
using Acme.Product.Core.Interfaces;
using Microsoft.Extensions.Hosting;

namespace Acme.Product.Infrastructure.Repositories;

public class LruImageCacheRepository : BackgroundService, IImageCacheRepository
{
    private readonly Dictionary<Guid, CacheEntry> _cache = new();
    private readonly LinkedList<Guid> _accessOrder = new();
    private readonly ConcurrentDictionary<Guid, CacheEntry> _pendingAdds = new();
    private readonly Channel<Guid> _addQueue;
    private readonly object _lock = new();
    private readonly long _maxSizeInBytes;
    private long _currentSizeInBytes;
    private long _pendingSizeInBytes;
    private long _hitCount;
    private long _missCount;

    public LruImageCacheRepository(
        long maxSizeInBytes = 100 * 1024 * 1024,
        int queueCapacity = 512)
    {
        if (maxSizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSizeInBytes));
        }

        _maxSizeInBytes = maxSizeInBytes;
        _addQueue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Math.Max(1, queueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
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
        var now = DateTime.UtcNow;
        var entry = new CacheEntry
        {
            Data = imageData,
            Format = format,
            CreatedAt = now,
            LastAccessedAt = now,
            SizeInBytes = imageData.Length
        };

        _pendingAdds[id] = entry;
        Interlocked.Add(ref _pendingSizeInBytes, entry.SizeInBytes);

        if (!_addQueue.Writer.TryWrite(id))
        {
            _ = EnqueueWhenAvailableAsync(id);
        }

        return Task.FromResult(id);
    }

    public Task<byte[]?> GetAsync(Guid id)
    {
        if (_pendingAdds.TryGetValue(id, out var pending))
        {
            pending.LastAccessedAt = DateTime.UtcNow;
            Interlocked.Increment(ref _hitCount);
            return Task.FromResult<byte[]?>(pending.Data);
        }

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
        }

        Interlocked.Increment(ref _missCount);
        return Task.FromResult<byte[]?>(null);
    }

    public Task DeleteAsync(Guid id)
    {
        RemovePending(id);

        lock (_lock)
        {
            RemoveEntry(id);
        }

        return Task.CompletedTask;
    }

    public Task CleanExpiredAsync(TimeSpan expiration)
    {
        var cutoff = DateTime.UtcNow - expiration;

        foreach (var pair in _pendingAdds.ToArray())
        {
            if (pair.Value.LastAccessedAt < cutoff)
            {
                RemovePending(pair.Key);
            }
        }

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
            var pendingCount = _pendingAdds.Count;
            var pendingSize = Interlocked.Read(ref _pendingSizeInBytes);
            var hits = Interlocked.Read(ref _hitCount);
            var misses = Interlocked.Read(ref _missCount);
            var total = hits + misses;

            return new CacheStatistics
            {
                TotalEntries = _cache.Count + pendingCount,
                CurrentSizeInBytes = _currentSizeInBytes + pendingSize,
                MaxSizeInBytes = _maxSizeInBytes,
                HitCount = hits,
                MissCount = misses,
                HitRate = total > 0 ? (double)hits / total : 0
            };
        }
    }

    public void Clear()
    {
        foreach (var id in _pendingAdds.Keys.ToArray())
        {
            RemovePending(id);
        }

        lock (_lock)
        {
            _cache.Clear();
            _accessOrder.Clear();
            _currentSizeInBytes = 0;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in _addQueue.Reader.ReadAllAsync(stoppingToken))
            {
                CommitPendingAdd(id);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            while (_addQueue.Reader.TryRead(out var id))
            {
                CommitPendingAdd(id);
            }

            foreach (var id in _pendingAdds.Keys.ToArray())
            {
                CommitPendingAdd(id);
            }
        }
    }

    private async Task EnqueueWhenAvailableAsync(Guid id)
    {
        try
        {
            await _addQueue.Writer.WriteAsync(id);
        }
        catch (InvalidOperationException)
        {
            CommitPendingAdd(id);
        }
    }

    private void CommitPendingAdd(Guid id)
    {
        if (!_pendingAdds.TryRemove(id, out var entry))
        {
            return;
        }

        Interlocked.Add(ref _pendingSizeInBytes, -entry.SizeInBytes);

        lock (_lock)
        {
            while (_currentSizeInBytes + entry.SizeInBytes > _maxSizeInBytes && _accessOrder.Count > 0)
            {
                EvictLeastRecentlyUsed();
            }

            _cache[id] = entry;
            _accessOrder.AddFirst(id);
            _currentSizeInBytes += entry.SizeInBytes;
        }
    }

    private void RemovePending(Guid id)
    {
        if (_pendingAdds.TryRemove(id, out var entry))
        {
            Interlocked.Add(ref _pendingSizeInBytes, -entry.SizeInBytes);
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
