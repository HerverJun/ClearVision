using System.Text.Json;
using Acme.Product.Application.DTOs;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Runtime;

public sealed class RuntimePackageLoader
{
    private readonly RuntimePackageValidator _validator;
    private readonly ILogger<RuntimePackageLoader> _logger;

    public RuntimePackageLoader(
        RuntimePackageValidator validator,
        ILogger<RuntimePackageLoader> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public async Task<RuntimePackage> LoadAsync(string packageRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            throw new RuntimePackageException("Package path is required.");
        }

        var normalizedRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new RuntimePackageException($"Package directory does not exist: {normalizedRoot}");
        }

        try
        {
            var packageFile = Path.Combine(normalizedRoot, "package.json");
            var flowDefaultFile = Path.Combine(normalizedRoot, "flow.json");
            var profileFile = Path.Combine(normalizedRoot, "runtime-profile.json");
            var validationFile = Path.Combine(normalizedRoot, "quality", "validation-report.json");

            var manifest = await ReadJsonAsync<RuntimePackageManifest>(packageFile, cancellationToken)
                ?? throw new RuntimePackageException("Failed to parse package.json.");

            var flowFile = RuntimePathGuard.ResolveChildPath(normalizedRoot, manifest.EntryFlow);
            var flowBytes = await File.ReadAllBytesAsync(flowFile, cancellationToken);
            var flow = JsonSerializer.Deserialize<OperatorFlowDto>(flowBytes, RuntimeJson.SerializerOptions)
                ?? throw new RuntimePackageException("Failed to parse flow.json.");
            var runtimeProfile = await ReadJsonAsync<RuntimeProfile>(profileFile, cancellationToken)
                ?? new RuntimeProfile();
            var validationReport = await ReadJsonAsync<RuntimeValidationReport>(validationFile, cancellationToken)
                ?? new RuntimeValidationReport();

            var package = new RuntimePackage
            {
                RootPath = normalizedRoot,
                Manifest = manifest,
                Flow = flow,
                FlowBytes = flowBytes,
                RuntimeProfile = runtimeProfile,
                ValidationReport = validationReport
            };

            var validation = await _validator.ValidateAsync(package, cancellationToken);
            if (!validation.IsValid)
            {
                throw new RuntimePackageException(validation.ToUserMessage())
                {
                    ValidationResult = validation
                };
            }

            _logger.LogInformation(
                "Loaded runtime package {PackageId} from {PackageRoot}",
                package.Manifest.PackageId,
                normalizedRoot);

            return package;
        }
        catch (RuntimePackageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimePackageException("Failed to load runtime package.", ex);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, RuntimeJson.SerializerOptions, cancellationToken);
    }
}
