using ClearVision.Product.Application.DTOs;

namespace ClearVision.Product.Application.Services;

public interface IProjectAssetStorage
{
    Task<ProjectAssetsDto> LoadAssetsAsync(Guid projectId);

    Task<ProjectAssetStorageMetadata?> LoadMetadataAsync(Guid projectId);

    Task SaveAssetsAsync(
        Guid projectId,
        ProjectAssetsDto assets,
        long persistenceRevision,
        Guid saveId,
        string assetsHash);

    Task DeleteAssetsAsync(Guid projectId);
}

public sealed record ProjectAssetStorageMetadata(
    int SchemaVersion,
    Guid ProjectId,
    long PersistenceRevision,
    string AssetsHash,
    Guid SaveId,
    DateTimeOffset SavedAtUtc);
