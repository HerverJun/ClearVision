using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class JsonFileProjectFlowStorage : IProjectFlowStorage
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProjectFlowStorage()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectFlows"))
    {
    }

    public JsonFileProjectFlowStorage(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be empty.", nameof(basePath));
        }

        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task SaveFlowJsonAsync(Guid projectId, string flowJson)
    {
        await SaveFlowJsonAsync(projectId, flowJson, persistenceRevision: 0);
    }

    public async Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
    {
        ArgumentNullException.ThrowIfNull(flowJson);
        ArgumentOutOfRangeException.ThrowIfNegative(persistenceRevision);
        ValidateJson(flowJson);

        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_basePath);
            var filePath = GetFilePath(projectId);
            var tempPath = filePath + ".tmp";
            var lastGoodPath = GetLastGoodPath(projectId);

            await File.WriteAllTextAsync(tempPath, flowJson, new UTF8Encoding(false));
            if (File.Exists(filePath))
            {
                File.Copy(filePath, lastGoodPath, overwrite: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
            await WriteMetadataAsync(projectId, flowJson, persistenceRevision);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> LoadFlowJsonAsync(Guid projectId)
    {
        await _gate.WaitAsync();
        try
        {
            var filePath = GetFilePath(projectId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            if (TryValidateJson(json))
            {
                return json;
            }

            var errorPath = filePath + ".corrupt";
            File.Copy(filePath, errorPath, overwrite: true);

            var lastGoodPath = GetLastGoodPath(projectId);
            if (File.Exists(lastGoodPath))
            {
                var lastGood = await File.ReadAllTextAsync(lastGoodPath, Encoding.UTF8);
                if (TryValidateJson(lastGood))
                {
                    return lastGood;
                }
            }

            throw new InvalidDataException($"Project flow JSON is corrupt and no valid last-good copy exists. ProjectId={projectId}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFlowJsonAsync(Guid projectId)
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

    public async Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId)
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
            var metadata = JsonSerializer.Deserialize<ProjectFlowStorageMetadataFile>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (metadata == null)
            {
                return null;
            }

            return new ProjectFlowStorageMetadata(
                metadata.SchemaVersion,
                metadata.ProjectId,
                metadata.PersistenceRevision,
                metadata.FlowHash ?? string.Empty,
                metadata.SavedAtUtc);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteMetadataAsync(Guid projectId, string flowJson, long persistenceRevision)
    {
        var metadata = new
        {
            schemaVersion = 1,
            projectId,
            persistenceRevision,
            flowHash = ComputeSha256(flowJson),
            savedAtUtc = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetMetadataPath(projectId), json, new UTF8Encoding(false));
    }

    private string GetFilePath(Guid projectId)
    {
        return Path.Combine(_basePath, $"{projectId}.json");
    }

    private string GetLastGoodPath(Guid projectId)
    {
        return Path.Combine(_basePath, $"{projectId}.last-good.json");
    }

    private string GetMetadataPath(Guid projectId)
    {
        return Path.Combine(_basePath, $"{projectId}.metadata.json");
    }

    private static void ValidateJson(string json)
    {
        using var _ = JsonDocument.Parse(json);
    }

    private static bool TryValidateJson(string json)
    {
        try
        {
            ValidateJson(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class ProjectFlowStorageMetadataFile
    {
        public int SchemaVersion { get; init; }

        public Guid ProjectId { get; init; }

        public long PersistenceRevision { get; init; }

        public string? FlowHash { get; init; }

        public DateTimeOffset SavedAtUtc { get; init; }
    }
}
