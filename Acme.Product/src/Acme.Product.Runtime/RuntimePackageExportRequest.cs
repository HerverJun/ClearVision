using Acme.Product.Application.DTOs;

namespace Acme.Product.Runtime;

public sealed class RuntimePackageExportRequest
{
    public required ProjectDto Project { get; init; }

    public string? TargetRootDirectory { get; init; }

    public string CreatedBy { get; init; } = "ClearVision Studio";
}
