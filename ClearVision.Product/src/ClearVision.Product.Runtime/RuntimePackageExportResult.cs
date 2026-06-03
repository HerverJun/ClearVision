using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Runtime;

public sealed class RuntimePackageExportResult
{
    public required string PackageRootPath { get; init; }

    public required RuntimePackageManifest Manifest { get; init; }

    public required RuntimeValidationReport ValidationReport { get; init; }

    public required string ReadmePath { get; init; }
}
