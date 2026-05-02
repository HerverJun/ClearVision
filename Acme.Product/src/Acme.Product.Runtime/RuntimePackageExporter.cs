using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acme.Product.Application.DTOs;
using Acme.Product.Application.Services;
using Acme.Product.Core.Services;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Runtime;

public sealed class RuntimePackageExporter
{
    private static readonly string[] SecretLikeTokens =
    [
        "apikey",
        "api_key",
        "secret",
        "token",
        "password",
        "credential"
    ];

    private static readonly HashSet<string> FileLikeParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "filepath",
        "folder",
        "directory",
        "model",
        "weights",
        "onnx",
        "calibration"
    };

    private readonly IOperatorFactory _operatorFactory;
    private readonly ILogger<RuntimePackageExporter> _logger;

    public RuntimePackageExporter(
        IOperatorFactory operatorFactory,
        ILogger<RuntimePackageExporter> logger)
    {
        _operatorFactory = operatorFactory;
        _logger = logger;
    }

    public async Task<RuntimePackageExportResult> ExportAsync(
        RuntimePackageExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        var project = request.Project;
        var flow = project.Flow ?? throw new RuntimePackageException("The selected project does not contain an executable flow.");
        if (flow.Operators.Count == 0)
        {
            throw new RuntimePackageException("The selected project does not contain any operators.");
        }

        var pendingParameters = FindPendingParameters(flow).ToList();
        var missingResources = FindMissingResources(flow).ToList();
        var secretFindings = FindSecretLikeFields(flow).ToList();
        if (secretFindings.Count > 0)
        {
            throw new RuntimePackageException(
                "Runtime package export was blocked because secret-like parameters were detected: " +
                string.Join(", ", secretFindings));
        }

        if (pendingParameters.Count > 0)
        {
            throw new RuntimePackageException(
                "Runtime package export was blocked because required parameters are still pending: " +
                string.Join(", ", pendingParameters));
        }

        if (missingResources.Count > 0)
        {
            throw new RuntimePackageException(
                "Runtime package export was blocked because referenced resources are missing: " +
                string.Join(", ", missingResources));
        }

        var packageId = $"cvpkg-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        var targetRoot = string.IsNullOrWhiteSpace(request.TargetRootDirectory)
            ? RuntimePathGuard.GetDefaultStudioExportRoot()
            : Path.GetFullPath(request.TargetRootDirectory);
        Directory.CreateDirectory(targetRoot);

        var safeProjectName = RuntimePathGuard.SanitizeFileName(project.Name, "runtime-package");
        var packageRoot = Path.Combine(targetRoot, $"{safeProjectName}-{packageId}");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(Path.Combine(packageRoot, "quality"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "field"));

        var flowBytes = JsonSerializer.SerializeToUtf8Bytes(flow, RuntimeJson.StableSerializerOptions);
        var flowHash = RuntimePathGuard.ComputeSha256(flowBytes);
        var profile = new RuntimeProfile();
        var manifest = new RuntimePackageManifest
        {
            PackageId = packageId,
            PackageName = string.IsNullOrWhiteSpace(project.Name) ? packageId : project.Name.Trim(),
            RuntimeApiVersion = profile.RuntimeApiVersion,
            MinStationVersion = "0.1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = request.CreatedBy,
            SourceProjectId = project.Id,
            EntryFlow = "flow.json",
            FlowHash = flowHash,
            OperatorCatalogVersion = BuildOperatorCatalogVersion(),
            ExportAllowed = true,
            PendingParameters = pendingParameters,
            MissingResources = missingResources,
            FieldExtensions = new RuntimeFieldExtensions
            {
                StationProfile = "field/station-profile.json",
                TriggerProfile = "field/trigger-profile.json",
                ResultMappingProfile = "field/result-mapping-profile.json",
                ModelAssets = "field/model-assets.json"
            }
        };

        var validationReport = new RuntimeValidationReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            IsValid = true,
            FlowHash = flowHash,
            Notes =
            [
                $"ProjectId={project.Id:D}",
                $"OperatorCount={flow.Operators.Count}",
                $"ConnectionCount={flow.Connections.Count}"
            ]
        };

        var packageBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, RuntimeJson.SerializerOptions);
        var profileBytes = JsonSerializer.SerializeToUtf8Bytes(profile, RuntimeJson.SerializerOptions);
        var validationBytes = JsonSerializer.SerializeToUtf8Bytes(validationReport, RuntimeJson.SerializerOptions);

        await File.WriteAllBytesAsync(Path.Combine(packageRoot, "package.json"), packageBytes, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(packageRoot, "flow.json"), flowBytes, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(packageRoot, "runtime-profile.json"), profileBytes, cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(packageRoot, "quality", "validation-report.json"),
            validationBytes,
            cancellationToken);

        await WriteFieldSchemaDraftsAsync(packageRoot, cancellationToken);

        var readmePath = Path.Combine(packageRoot, "README.runtime.md");
        await File.WriteAllTextAsync(readmePath, BuildReadme(manifest, validationReport), cancellationToken);

        _logger.LogInformation(
            "Exported runtime package {PackageId} for project {ProjectId} to {PackageRoot}",
            manifest.PackageId,
            project.Id,
            packageRoot);

        return new RuntimePackageExportResult
        {
            PackageRootPath = packageRoot,
            Manifest = manifest,
            ValidationReport = validationReport,
            ReadmePath = readmePath
        };
    }

    private string BuildOperatorCatalogVersion()
    {
        var names = _operatorFactory
            .GetSupportedOperatorTypes()
            .OrderBy(item => item.ToString(), StringComparer.Ordinal)
            .Select(item => item.ToString());
        var payload = string.Join("|", names);
        var hash = RuntimePathGuard.ComputeSha256(Encoding.UTF8.GetBytes(payload));
        return $"{payload.Count(ch => ch == '|') + 1}+{hash[7..19]}";
    }

    private static IEnumerable<string> FindPendingParameters(OperatorFlowDto flow)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                if (!parameter.IsRequired)
                {
                    continue;
                }

                var text = NormalizeScalar(parameter.Value);
                if (text != null)
                {
                    continue;
                }

                yield return $"{op.Name}.{parameter.Name}";
            }
        }
    }

    private static IEnumerable<string> FindMissingResources(OperatorFlowDto flow)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                if (!LooksLikeFileParameter(parameter))
                {
                    continue;
                }

                var text = NormalizeScalar(parameter.Value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!File.Exists(text) && !Directory.Exists(text))
                {
                    yield return $"{op.Name}.{parameter.Name}";
                }
            }
        }
    }

    private static IEnumerable<string> FindSecretLikeFields(OperatorFlowDto flow)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                if (SecretLikeTokens.Any(token =>
                        parameter.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrWhiteSpace(NormalizeScalar(parameter.Value)))
                    {
                        yield return $"{op.Name}.{parameter.Name}";
                    }

                    continue;
                }

                var text = NormalizeScalar(parameter.Value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{op.Name}.{parameter.Name}";
                }
            }
        }
    }

    private static bool LooksLikeFileParameter(ParameterDto parameter)
    {
        if (FileLikeParameterTypes.Contains(parameter.DataType))
        {
            return true;
        }

        return parameter.Name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.Contains("file", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.Contains("model", StringComparison.OrdinalIgnoreCase) ||
               parameter.Name.Contains("calibration", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeScalar(object? value)
    {
        return value switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } element when string.IsNullOrWhiteSpace(element.GetString()) => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim(),
            JsonElement { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } element => element.ToString(),
            _ => value.ToString()
        };
    }

    private static async Task WriteFieldSchemaDraftsAsync(string packageRoot, CancellationToken cancellationToken)
    {
        var fieldRoot = Path.Combine(packageRoot, "field");
        var drafts = new Dictionary<string, object>
        {
            ["station-profile.json"] = new
            {
                stationId = "",
                lineName = "",
                notes = "Reserved for field deployment. Station MVP may ignore this file."
            },
            ["trigger-profile.json"] = new
            {
                mode = "Manual",
                intervalMs = 0,
                notes = "Reserved for V1.1 trigger integration."
            },
            ["result-mapping-profile.json"] = new
            {
                okCode = "OK",
                ngCode = "NG",
                errorCode = "ERROR",
                notes = "Reserved for V1.1 result writeback mapping."
            },
            ["model-assets.json"] = new
            {
                assets = Array.Empty<object>(),
                notes = "Reserved for V1.1 external model assets."
            }
        };

        foreach (var (fileName, payload) in drafts)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, RuntimeJson.SerializerOptions);
            await File.WriteAllBytesAsync(Path.Combine(fieldRoot, fileName), bytes, cancellationToken);
        }
    }

    private static string BuildReadme(RuntimePackageManifest manifest, RuntimeValidationReport validationReport)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ClearVision Runtime Package");
        builder.AppendLine();
        builder.AppendLine($"- PackageId: `{manifest.PackageId}`");
        builder.AppendLine($"- PackageName: `{manifest.PackageName}`");
        builder.AppendLine($"- RuntimeApiVersion: `{manifest.RuntimeApiVersion}`");
        builder.AppendLine($"- MinStationVersion: `{manifest.MinStationVersion}`");
        builder.AppendLine($"- FlowHash: `{manifest.FlowHash}`");
        builder.AppendLine($"- CreatedAt: `{manifest.CreatedAt:O}`");
        builder.AppendLine($"- CreatedBy: `{manifest.CreatedBy}`");
        builder.AppendLine();
        builder.AppendLine("## Notes");
        foreach (var note in validationReport.Notes)
        {
            builder.AppendLine($"- {note}");
        }

        builder.AppendLine();
        builder.AppendLine("## Field Extensions");
        builder.AppendLine("- `field/` is reserved for Station/trigger/result-mapping/model-assets drafts.");
        builder.AppendLine("- Station MVP may safely ignore those files.");
        return builder.ToString();
    }
}
