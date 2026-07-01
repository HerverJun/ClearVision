using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Runtime;

public sealed class RuntimePackage
{
    public required string RootPath { get; init; }

    public required RuntimePackageManifest Manifest { get; init; }

    public required OperatorFlowDto Flow { get; init; }

    public required byte[] FlowBytes { get; init; }

    public required RuntimeProfile RuntimeProfile { get; init; }

    public required RuntimeValidationReport ValidationReport { get; init; }

    public required RuntimeParameterSchema ParameterSchema { get; init; }

    public required RuntimeSiteProfile DefaultSiteProfile { get; init; }

    public required ProjectGlobalVariableSchema GlobalVariables { get; init; }

    public string PackageFilePath => Path.Combine(RootPath, "package.json");

    public string FlowFilePath => RuntimePathGuard.ResolveChildPath(RootPath, Manifest.EntryFlow);
}
