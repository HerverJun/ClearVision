using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;

namespace ClearVision.Product.Runtime;

public sealed class RuntimePackageExportRequest
{
    public required ProjectDto Project { get; init; }

    public string? TargetRootDirectory { get; init; }

    public string CreatedBy { get; init; } = "ClearVision Studio";

    public ProjectAssetStorageMetadata? ProjectAssetStorageMetadata { get; init; }

    public bool RequireProjectAssetStorageMetadata { get; init; }
}
