using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class JsonFileProjectAssetStorage : IProjectAssetStorage
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProjectAssetStorage()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectAssets"))
    {
    }

    public JsonFileProjectAssetStorage(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be empty.", nameof(basePath));
        }

        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<ProjectAssetsDto> LoadAssetsAsync(Guid projectId)
    {
        await _gate.WaitAsync();
        try
        {
            var filePath = GetFilePath(projectId);
            if (!File.Exists(filePath))
            {
                return new ProjectAssetsDto();
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            if (TryDeserializeAssets(json, out var assets))
            {
                return assets;
            }

            File.Copy(filePath, filePath + ".corrupt", overwrite: true);
            var lastGoodPath = GetLastGoodPath(projectId);
            if (File.Exists(lastGoodPath))
            {
                var lastGoodJson = await File.ReadAllTextAsync(lastGoodPath, Encoding.UTF8);
                if (TryDeserializeAssets(lastGoodJson, out var lastGoodAssets))
                {
                    return lastGoodAssets;
                }
            }

            throw new InvalidDataException($"PSV022: project asset authority is corrupt and no valid last-good copy exists. ProjectId={projectId}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProjectAssetStorageMetadata?> LoadMetadataAsync(Guid projectId)
    {
        await _gate.WaitAsync();
        try
        {
            var metadataPath = GetMetadataPath(projectId);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(metadataPath, Encoding.UTF8);
            var metadata = JsonSerializer.Deserialize<ProjectAssetStorageMetadataFile>(
                json,
                ProjectAssetJson.Options);
            if (metadata == null)
            {
                return null;
            }

            return new ProjectAssetStorageMetadata(
                metadata.SchemaVersion,
                metadata.ProjectId,
                metadata.PersistenceRevision,
                metadata.AssetsHash ?? string.Empty,
                metadata.SaveId,
                metadata.SavedAtUtc);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAssetsAsync(
        Guid projectId,
        ProjectAssetsDto assets,
        long persistenceRevision,
        Guid saveId,
        string assetsHash)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(persistenceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsHash);

        var bytes = ProjectAssetJson.SerializeToBytes(assets);
        var computedHash = ProjectAssetJson.ComputeSha256(bytes);
        if (!string.Equals(computedHash, assetsHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PSV018: project asset candidate hash does not match staged manifest.");
        }

        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_basePath);
            var filePath = GetFilePath(projectId);
            var tempPath = filePath + ".tmp";
            var lastGoodPath = GetLastGoodPath(projectId);

            await File.WriteAllBytesAsync(tempPath, bytes);
            if (File.Exists(filePath))
            {
                File.Copy(filePath, lastGoodPath, overwrite: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
            await WriteMetadataAsync(projectId, persistenceRevision, saveId, assetsHash);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAssetsAsync(Guid projectId)
    {
        await _gate.WaitAsync();
        try
        {
            File.Delete(GetFilePath(projectId));
            File.Delete(GetFilePath(projectId) + ".tmp");
            File.Delete(GetFilePath(projectId) + ".corrupt");
            File.Delete(GetLastGoodPath(projectId));
            File.Delete(GetMetadataPath(projectId));
            File.Delete(GetMetadataPath(projectId) + ".tmp");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteMetadataAsync(
        Guid projectId,
        long persistenceRevision,
        Guid saveId,
        string assetsHash)
    {
        var metadata = new ProjectAssetStorageMetadataFile
        {
            SchemaVersion = 1,
            ProjectId = projectId,
            PersistenceRevision = persistenceRevision,
            AssetsHash = assetsHash,
            SaveId = saveId,
            SavedAtUtc = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(metadata, ProjectAssetJson.Options);
        await File.WriteAllTextAsync(GetMetadataPath(projectId), json, new UTF8Encoding(false));
    }

    private static bool TryDeserializeAssets(string json, out ProjectAssetsDto assets)
    {
        assets = new ProjectAssetsDto();
        try
        {
            assets = ProjectAssetJson.Normalize(
                JsonSerializer.Deserialize<ProjectAssetsDto>(json, ProjectAssetJson.Options) ?? new ProjectAssetsDto());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string GetFilePath(Guid projectId) =>
        Path.Combine(_basePath, $"{projectId}.assets.json");

    private string GetLastGoodPath(Guid projectId) =>
        Path.Combine(_basePath, $"{projectId}.assets.last-good.json");

    private string GetMetadataPath(Guid projectId) =>
        Path.Combine(_basePath, $"{projectId}.assets.metadata.json");

    private sealed class ProjectAssetStorageMetadataFile
    {
        public int SchemaVersion { get; init; }

        public Guid ProjectId { get; init; }

        public long PersistenceRevision { get; init; }

        public string? AssetsHash { get; init; }

        public Guid SaveId { get; init; }

        public DateTimeOffset SavedAtUtc { get; init; }
    }
}
