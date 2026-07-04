using System.Security.Cryptography;
using System.Text.Json;

namespace ClearVision.Product.Application.DTOs;

public sealed class ProjectAssetsDto
{
    public int SchemaVersion { get; set; } = 1;

    public List<ProjectCalibrationAssetDto> CalibrationAssets { get; set; } = [];

    public List<ProjectSpatialAssetDto> SpatialAssets { get; set; } = [];
}

public sealed class ProjectCalibrationAssetDto
{
    public string AssetId { get; set; } = string.Empty;

    public string Kind { get; set; } = "CalibrationBundleV2";

    public string Version { get; set; } = string.Empty;

    public string Producer { get; set; } = string.Empty;

    public string SourceDraftSessionId { get; set; } = string.Empty;

    public Guid? TargetNodeId { get; set; }

    public string ImageIdentity { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public long ProjectRevision { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string Status { get; set; } = "authority";

    public JsonElement Payload { get; set; } = ProjectAssetJson.CreateEmptyPayload();
}

public sealed class ProjectSpatialAssetDto
{
    public string AssetId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Producer { get; set; } = string.Empty;

    public string SourceDraftSessionId { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public long ProjectRevision { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string Status { get; set; } = "authority";

    public JsonElement Payload { get; set; } = ProjectAssetJson.CreateEmptyPayload();
}

public sealed class ProjectCalibrationAssetSaveRequest
{
    public long? ExpectedPersistenceRevision { get; set; }

    public string? AssetId { get; set; }

    public string? Version { get; set; }

    public string Producer { get; set; } = "NPointCalibrationDraftWorkbench";

    public string? SourceDraftSessionId { get; set; }

    public Guid? TargetNodeId { get; set; }

    public string? ImageIdentity { get; set; }

    public string? ExpectedContentHash { get; set; }

    public JsonElement Payload { get; set; }
}

public sealed class ProjectCalibrationAssetSaveResponse
{
    public string SchemaVersion { get; set; } = "project-calibration-asset-save.v1";

    public Guid ProjectId { get; set; }

    public long PersistenceRevision { get; set; }

    public string AssetsHash { get; set; } = string.Empty;

    public ProjectCalibrationAssetDto Asset { get; set; } = new();

    public ProjectAssetsDto Assets { get; set; } = new();
}

public static class ProjectAssetJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static JsonElement CreateEmptyPayload() =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>(), Options);

    public static byte[] SerializeToBytes(ProjectAssetsDto assets) =>
        JsonSerializer.SerializeToUtf8Bytes(Normalize(assets ?? new ProjectAssetsDto()), Options);

    public static ProjectAssetsDto Clone(ProjectAssetsDto assets) =>
        Normalize(JsonSerializer.Deserialize<ProjectAssetsDto>(SerializeToBytes(assets), Options) ?? new ProjectAssetsDto());

    public static bool HasAssets(ProjectAssetsDto? assets) =>
        assets?.CalibrationAssets?.Count > 0 || assets?.SpatialAssets?.Count > 0;

    public static ProjectAssetsDto WithProjectRevision(ProjectAssetsDto assets, long projectRevision)
    {
        var clone = Clone(assets);
        clone.CalibrationAssets = clone.CalibrationAssets
            .OrderBy(asset => asset.AssetId, StringComparer.Ordinal)
            .Select(asset =>
            {
                asset.ProjectRevision = projectRevision;
                asset.Status = string.IsNullOrWhiteSpace(asset.Status) ? "authority" : asset.Status;
                return asset;
            })
            .ToList();
        clone.SpatialAssets = clone.SpatialAssets
            .OrderBy(asset => asset.AssetId, StringComparer.Ordinal)
            .Select(asset =>
            {
                asset.ProjectRevision = projectRevision;
                asset.Status = string.IsNullOrWhiteSpace(asset.Status) ? "authority" : asset.Status;
                return asset;
            })
            .ToList();
        return clone;
    }

    public static ProjectAssetsDto Normalize(ProjectAssetsDto assets)
    {
        assets.SchemaVersion = assets.SchemaVersion <= 0 ? 1 : assets.SchemaVersion;
        assets.CalibrationAssets ??= [];
        assets.SpatialAssets ??= [];
        return assets;
    }

    public static string ComputeAssetsHash(ProjectAssetsDto assets) =>
        ComputeSha256(SerializeToBytes(assets));

    public static string ComputePayloadHash(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new InvalidOperationException("PSV019: calibration asset payload is required.");
        }

        return ComputeSha256(JsonSerializer.SerializeToUtf8Bytes(payload, Options));
    }

    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
