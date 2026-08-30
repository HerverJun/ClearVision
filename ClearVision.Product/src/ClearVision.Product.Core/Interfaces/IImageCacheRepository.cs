// IImageCacheRepository.cs
// 清理过期缓存
// 作者：蘅芜君

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// 图像缓存仓储接口
/// </summary>
public interface IImageCacheRepository
{
    /// <summary>
    /// 添加图像到缓存
    /// </summary>
    Task<Guid> AddAsync(byte[] imageData, string format);

    /// <summary>
    /// Adds an inspection result image whose cache entry is bound to its database authority.
    /// </summary>
    Task<Guid> AddResultAsync(
        byte[] imageData,
        string format,
        ResultImageCacheAuthority authority);

    /// <summary>
    /// 获取图像
    /// </summary>
    Task<byte[]?> GetAsync(Guid id);

    /// <summary>
    /// Gets image bytes together with the authority metadata captured at admission time.
    /// </summary>
    Task<CachedImage?> GetEntryAsync(Guid id);

    /// <summary>
    /// 删除图像
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    Task CleanExpiredAsync(TimeSpan expiration);
}

/// <summary>
/// Database resource identity bound to one cached inspection result image.
/// </summary>
public sealed record ResultImageCacheAuthority(Guid ProjectId, Guid ResultId);

/// <summary>
/// Cached image payload and its optional resource authority.
/// Upload/preview images remain unbound; formal result images must have an authority.
/// </summary>
public sealed record CachedImage(
    byte[] Data,
    string Format,
    ResultImageCacheAuthority? Authority);
