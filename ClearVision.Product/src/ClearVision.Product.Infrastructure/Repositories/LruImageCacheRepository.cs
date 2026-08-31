using System.Collections.Concurrent;
using System.Threading.Channels;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Infrastructure.Repositories;

public class LruImageCacheRepository : BackgroundService, IImageCacheRepository
{
    private readonly Dictionary<Guid, CacheEntry> _cache = new();
    private readonly LinkedList<Guid> _uploadAccessOrder = new();
    private readonly LinkedList<Guid> _resultAccessOrder = new();
    private readonly ConcurrentDictionary<Guid, CacheEntry> _pendingAdds = new();
    private readonly Channel<Guid> _addQueue;
    private readonly object _lock = new();
    private readonly object _admissionLock = new();
    private readonly long _uploadMaxSizeInBytes;
    private readonly long _resultMaxSizeInBytes;
    private long _uploadCurrentSizeInBytes;
    private long _resultCurrentSizeInBytes;
    private long _uploadPendingSizeInBytes;
    private long _resultPendingSizeInBytes;
    private long _hitCount;
    private long _missCount;

    public LruImageCacheRepository(
        long maxSizeInBytes = 100 * 1024 * 1024,
        int queueCapacity = 512,
        long? resultMaxSizeInBytes = null)
    {
        if (maxSizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSizeInBytes));
        }

        if (resultMaxSizeInBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resultMaxSizeInBytes));
        }

        // Upload/preview images and authoritative result images intentionally
        // have independent budgets. Pressure in one namespace can never evict
        // an entry from the other namespace.
        _uploadMaxSizeInBytes = maxSizeInBytes;
        _resultMaxSizeInBytes = resultMaxSizeInBytes ?? maxSizeInBytes;
        _addQueue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Math.Max(1, queueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task<Guid> AddAsync(byte[] imageData, string format)
    {
        return AddInternalAsync(imageData, format, authority: null);
    }

    public Task<Guid> AddResultAsync(
        byte[] imageData,
        string format,
        ResultImageCacheAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.ProjectId == Guid.Empty || authority.ResultId == Guid.Empty)
        {
            throw new ArgumentException("Result image authority must contain non-empty project and result identifiers.", nameof(authority));
        }

        return AddInternalAsync(imageData, format, authority);
    }

    private Task<Guid> AddInternalAsync(
        byte[] imageData,
        string format,
        ResultImageCacheAuthority? authority)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        var isResult = authority != null;
        var partitionLimit = GetPartitionLimit(isResult);
        if (imageData.Length > partitionLimit)
        {
            throw new ArgumentException(
                $"Image size {imageData.Length} exceeds its cache namespace limit {partitionLimit}.",
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
            SizeInBytes = imageData.Length,
            Authority = authority,
            IsResult = isResult
        };

        lock (_admissionLock)
        {
            if (GetRetainedSizeInBytes(isResult) + entry.SizeInBytes > partitionLimit)
            {
                DrainQueuedPendingAdds();
            }

            if (GetRetainedSizeInBytes(isResult) + entry.SizeInBytes > partitionLimit)
            {
                CommitEntry(id, entry);
                return Task.FromResult(id);
            }

            _pendingAdds[id] = entry;
            AddPendingSize(entry, entry.SizeInBytes);

            if (!_addQueue.Writer.TryWrite(id))
            {
                CommitPendingAdd(id);
            }
        }

        return Task.FromResult(id);
    }

    public async Task<byte[]?> GetAsync(Guid id)
    {
        var entry = await GetEntryAsync(id);
        return entry?.Data;
    }

    public Task<CachedImage?> GetEntryAsync(Guid id)
    {
        if (_pendingAdds.TryGetValue(id, out var pending))
        {
            pending.LastAccessedAt = DateTime.UtcNow;
            Interlocked.Increment(ref _hitCount);
            return Task.FromResult<CachedImage?>(ToCachedImage(pending));
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(id, out var entry))
            {
                entry.LastAccessedAt = DateTime.UtcNow;
                var accessOrder = GetAccessOrder(entry.IsResult);
                accessOrder.Remove(id);
                accessOrder.AddFirst(id);
                Interlocked.Increment(ref _hitCount);
                return Task.FromResult<CachedImage?>(ToCachedImage(entry));
            }
        }

        Interlocked.Increment(ref _missCount);
        return Task.FromResult<CachedImage?>(null);
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
            var uploadPendingSize = Interlocked.Read(ref _uploadPendingSizeInBytes);
            var resultPendingSize = Interlocked.Read(ref _resultPendingSizeInBytes);
            var hits = Interlocked.Read(ref _hitCount);
            var misses = Interlocked.Read(ref _missCount);
            var total = hits + misses;
            var pendingUploadCount = _pendingAdds.Count(item => !item.Value.IsResult);
            var pendingResultCount = pendingCount - pendingUploadCount;

            return new CacheStatistics
            {
                TotalEntries = _cache.Count + pendingCount,
                CurrentSizeInBytes = _uploadCurrentSizeInBytes + _resultCurrentSizeInBytes + uploadPendingSize + resultPendingSize,
                MaxSizeInBytes = _uploadMaxSizeInBytes + _resultMaxSizeInBytes,
                UploadEntries = _cache.Count(item => !item.Value.IsResult) + pendingUploadCount,
                ResultEntries = _cache.Count(item => item.Value.IsResult) + pendingResultCount,
                UploadSizeInBytes = _uploadCurrentSizeInBytes + uploadPendingSize,
                ResultSizeInBytes = _resultCurrentSizeInBytes + resultPendingSize,
                UploadMaxSizeInBytes = _uploadMaxSizeInBytes,
                ResultMaxSizeInBytes = _resultMaxSizeInBytes,
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
            _uploadAccessOrder.Clear();
            _resultAccessOrder.Clear();
            _uploadCurrentSizeInBytes = 0;
            _resultCurrentSizeInBytes = 0;
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

    private void CommitPendingAdd(Guid id)
    {
        if (!_pendingAdds.TryRemove(id, out var entry))
        {
            return;
        }

        AddPendingSize(entry, -entry.SizeInBytes);
        CommitEntry(id, entry);
    }

    private void CommitEntry(Guid id, CacheEntry entry)
    {
        lock (_lock)
        {
            var accessOrder = GetAccessOrder(entry.IsResult);
            while (GetCurrentSize(entry.IsResult) + entry.SizeInBytes > GetPartitionLimit(entry.IsResult) && accessOrder.Count > 0)
            {
                EvictLeastRecentlyUsed(entry.IsResult);
            }

            _cache[id] = entry;
            accessOrder.AddFirst(id);
            AddCurrentSize(entry, entry.SizeInBytes);
        }
    }

    private void DrainQueuedPendingAdds()
    {
        while (_addQueue.Reader.TryRead(out var id))
        {
            CommitPendingAdd(id);
        }
    }

    private long GetRetainedSizeInBytes(bool isResult)
    {
        lock (_lock)
        {
            return GetCurrentSize(isResult) + (isResult
                ? Interlocked.Read(ref _resultPendingSizeInBytes)
                : Interlocked.Read(ref _uploadPendingSizeInBytes));
        }
    }

    private void RemovePending(Guid id)
    {
        if (_pendingAdds.TryRemove(id, out var entry))
        {
            AddPendingSize(entry, -entry.SizeInBytes);
        }
    }

    private void EvictLeastRecentlyUsed(bool isResult)
    {
        var accessOrder = GetAccessOrder(isResult);
        if (accessOrder.Last is null)
        {
            return;
        }

        RemoveEntry(accessOrder.Last.Value);
    }

    private void RemoveEntry(Guid id)
    {
        if (_cache.TryGetValue(id, out var entry))
        {
            _cache.Remove(id);
            GetAccessOrder(entry.IsResult).Remove(id);
            AddCurrentSize(entry, -entry.SizeInBytes);
        }
    }

    private LinkedList<Guid> GetAccessOrder(bool isResult) =>
        isResult ? _resultAccessOrder : _uploadAccessOrder;

    private long GetPartitionLimit(bool isResult) =>
        isResult ? _resultMaxSizeInBytes : _uploadMaxSizeInBytes;

    private long GetCurrentSize(bool isResult) =>
        isResult ? _resultCurrentSizeInBytes : _uploadCurrentSizeInBytes;

    private void AddCurrentSize(CacheEntry entry, long delta)
    {
        if (entry.IsResult)
        {
            _resultCurrentSizeInBytes += delta;
        }
        else
        {
            _uploadCurrentSizeInBytes += delta;
        }
    }

    private void AddPendingSize(CacheEntry entry, long delta)
    {
        if (entry.IsResult)
        {
            Interlocked.Add(ref _resultPendingSizeInBytes, delta);
        }
        else
        {
            Interlocked.Add(ref _uploadPendingSizeInBytes, delta);
        }
    }

    private static CachedImage ToCachedImage(CacheEntry entry) =>
        new(entry.Data, entry.Format, entry.Authority);

    private sealed class CacheEntry
    {
        public byte[] Data { get; init; } = [];
        public string Format { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public DateTime LastAccessedAt { get; set; }
        public long SizeInBytes { get; init; }
        public ResultImageCacheAuthority? Authority { get; init; }
        public bool IsResult { get; init; }
    }
}

public class CacheStatistics
{
    public int TotalEntries { get; set; }
    public long CurrentSizeInBytes { get; set; }
    public long MaxSizeInBytes { get; set; }
    public int UploadEntries { get; set; }
    public int ResultEntries { get; set; }
    public long UploadSizeInBytes { get; set; }
    public long ResultSizeInBytes { get; set; }
    public long UploadMaxSizeInBytes { get; set; }
    public long ResultMaxSizeInBytes { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRate { get; set; }
    public double CurrentSizeInMB => CurrentSizeInBytes / (1024.0 * 1024.0);
    public double MaxSizeInMB => MaxSizeInBytes / (1024.0 * 1024.0);
}
