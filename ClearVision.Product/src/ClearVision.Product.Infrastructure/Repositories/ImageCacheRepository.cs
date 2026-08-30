// ImageCacheRepository.cs
// 图像缓存仓储实现（内存缓存）
// 作者：蘅芜君

using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Infrastructure.Repositories;

/// <summary>
/// 图像缓存仓储实现（内存缓存）
/// </summary>
public class ImageCacheRepository : IImageCacheRepository
{
    private readonly Dictionary<Guid, CacheEntry> _cache = new();
    private readonly object _lock = new();

    private class CacheEntry
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Format { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public ResultImageCacheAuthority? Authority { get; set; }
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
        var id = Guid.NewGuid();

        lock (_lock)
        {
            _cache[id] = new CacheEntry
            {
                Data = imageData,
                Format = format,
                CreatedAt = DateTime.UtcNow,
                Authority = authority
            };
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
        lock (_lock)
        {
            if (_cache.TryGetValue(id, out var entry))
            {
                return Task.FromResult<CachedImage?>(new CachedImage(entry.Data, entry.Format, entry.Authority));
            }
            return Task.FromResult<CachedImage?>(null);
        }
    }

    public Task DeleteAsync(Guid id)
    {
        lock (_lock)
        {
            _cache.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task CleanExpiredAsync(TimeSpan expiration)
    {
        var cutoff = DateTime.UtcNow - expiration;

        lock (_lock)
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.CreatedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}
