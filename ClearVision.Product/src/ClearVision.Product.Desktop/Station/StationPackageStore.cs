using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Station;

public sealed class StationPackageStore
{
    private static readonly JsonSerializerOptions PackageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private static readonly JsonSerializerOptions StablePackageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StationPackageStore> _logger;
    private readonly IWorkflowArtifactAdmissionGate? _workflowArtifactAdmissionGate;
    private readonly string _rootDirectory;

    public StationPackageStore(
        IServiceScopeFactory scopeFactory,
        ILogger<StationPackageStore> logger,
        IWorkflowArtifactAdmissionGate? workflowArtifactAdmissionGate = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _workflowArtifactAdmissionGate = workflowArtifactAdmissionGate;
        _rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVisionStudio",
            "packages");
        Directory.CreateDirectory(FilesDirectory);
    }

    private string FilesDirectory => Path.Combine(_rootDirectory, "files");

    public IReadOnlyList<StationPackageManifestDto> GetPackages()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return db.StationPackageRecords
            .OrderByDescending(item => item.Id)
            .AsEnumerable()
            .Select(item => ToManifest(item))
            .ToList();
    }

    public IReadOnlyList<StationPackageManifestDto> GetProductionPackages()
    {
        return GetPackages()
            .Where(item => item.PackageKind == StationPackageKind.Production)
            .ToList();
    }

    public StationPackageManifestDto? GetPackage(string packageId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var entity = db.StationPackageRecords.FirstOrDefault(item => item.PackageId == packageId);
        return entity == null ? null : ToManifest(entity);
    }

    public string? GetPackagePath(string packageId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return db.StationPackageRecords
            .Where(item => item.PackageId == packageId)
            .Select(item => item.FilePath)
            .FirstOrDefault();
    }

    public bool TryGetPackageFileForDownload(string packageId, out string path)
    {
        path = string.Empty;
        var storedPath = GetPackagePath(packageId);
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        if (!TryNormalizeAllowedPackagePath(storedPath, out var normalizedPath))
        {
            _logger.LogWarning("Rejected Station package download for {PackageId}; stored path is outside the package directory.", packageId);
            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            return false;
        }

        path = normalizedPath;
        return true;
    }

    public async Task<StationPackageManifestDto> CreateTestPackageAsync(CancellationToken cancellationToken)
    {
        var packageId = $"pkg_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..32];
        var packageName = "ClearVision Test Package";
        var packageVersion = "0.1.0";
        var packageDirectory = Path.Combine(FilesDirectory, packageId);
        Directory.CreateDirectory(packageDirectory);

        var staging = Path.Combine(packageDirectory, "staging");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        var runtimeRoot = Path.Combine(staging, "package");
        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "quality"));
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "field"));

        var flow = CreateSmokeFlow(packageId);
        var flowBytes = JsonSerializer.SerializeToUtf8Bytes(flow, StablePackageJsonOptions);
        var flowEntity = flow.ToEntity();
        var flowHash = ExecutionFlowIdentity.ComputeFlowHash(flowEntity);
        var runtimeManifest = new RuntimePackageManifest
        {
            PackageId = packageId,
            PackageName = packageName,
            RuntimeApiVersion = "1.0",
            MinStationVersion = "0.1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "Studio",
            SourceProjectId = Guid.Empty,
            EntryFlow = "flow.json",
            FlowHash = flowHash,
            DecisionConfigurationHash = ExecutionFlowIdentity.ComputeDecisionConfigurationHash(
                flowEntity.DecisionConfiguration),
            OperatorCatalogVersion = "test-package",
            ExportAllowed = true,
            PendingParameters = [],
            MissingResources = [],
            FieldExtensions = new RuntimeFieldExtensions
            {
                RuntimeParameters = "field/runtime-parameters.json",
                DefaultSiteProfile = "field/station-profile.default.json"
            }
        };
        var runtimeProfile = new RuntimeProfile();
        var validationReport = new RuntimeValidationReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            IsValid = true,
            FlowHash = flowHash,
            Notes =
            [
                "Purpose=Station remote deployment smoke test",
                "OperatorCount=1",
                "ConnectionCount=0"
            ]
        };
        var parameterSchema = new RuntimeParameterSchema
        {
            PackageId = packageId,
            FlowHash = flowHash,
            Parameters = []
        };
        var defaultSiteProfile = new RuntimeSiteProfile
        {
            ProfileId = "package-default",
            PackageId = packageId,
            FlowHash = flowHash,
            Revision = 0,
            UpdatedAtUtc = runtimeManifest.CreatedAt,
            UpdatedBy = "Studio",
            Overrides = []
        };

        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "package.json"), JsonSerializer.SerializeToUtf8Bytes(runtimeManifest, PackageJsonOptions), cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "flow.json"), flowBytes, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "runtime-profile.json"), JsonSerializer.SerializeToUtf8Bytes(runtimeProfile, PackageJsonOptions), cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "quality", "validation-report.json"), JsonSerializer.SerializeToUtf8Bytes(validationReport, PackageJsonOptions), cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "field", "runtime-parameters.json"), JsonSerializer.SerializeToUtf8Bytes(parameterSchema, PackageJsonOptions), cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(runtimeRoot, "field", "station-profile.default.json"), JsonSerializer.SerializeToUtf8Bytes(defaultSiteProfile, PackageJsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "README.runtime.md"), "ClearVision Station deployment smoke-test runtime package.", cancellationToken);

        var manifest = new StationPackageManifestDto
        {
            PackageId = packageId,
            PackageName = packageName,
            PackageVersion = packageVersion,
            PackageKind = StationPackageKind.Test,
            FlowHash = flowHash,
            CreatedBy = "Studio",
            MinStationVersion = "0.1.0",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(staging, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var targetFile = Path.Combine(packageDirectory, $"{packageId}.cvpkg");
        if (File.Exists(targetFile))
        {
            File.Delete(targetFile);
        }

        ZipFile.CreateFromDirectory(staging, targetFile, CompressionLevel.Fastest, includeBaseDirectory: false);
        Directory.Delete(staging, recursive: true);

        manifest.SizeBytes = new FileInfo(targetFile).Length;
        manifest.Sha256 = await ComputeSha256Async(targetFile, cancellationToken);
        Persist(manifest, targetFile);
        return manifest;
    }

    public async Task<StationPackageManifestDto> ImportRuntimePackageAsync(
        string runtimePackageRootPath,
        string? createdBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimePackageRootPath))
        {
            throw new ArgumentException("Runtime package root path is required.", nameof(runtimePackageRootPath));
        }

        var runtimeRoot = Path.GetFullPath(runtimePackageRootPath);
        var runtimeManifestPath = Path.Combine(runtimeRoot, "package.json");
        if (!File.Exists(runtimeManifestPath))
        {
            throw new FileNotFoundException("Runtime package manifest was not found.", runtimeManifestPath);
        }

        var runtimeManifestJson = await File.ReadAllTextAsync(runtimeManifestPath, cancellationToken);
        var runtimeManifest = JsonSerializer.Deserialize<RuntimePackageManifest>(runtimeManifestJson, PackageJsonOptions)
            ?? throw new InvalidOperationException("Runtime package manifest could not be read.");
        if (!string.Equals(runtimeManifest.EntryFlow, "flow.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimePackageException("Station import requires the canonical flow.json entry artifact.");
        }

        var runtimeFlowPath = Path.Combine(runtimeRoot, "flow.json");
        if (!File.Exists(runtimeFlowPath))
        {
            throw new RuntimePackageException("Runtime package flow.json was not found.");
        }

        string? admittedFlowJson = null;
        string? admittedFlowHash = null;
        string? admittedDecisionConfigurationHash = null;
        if (_workflowArtifactAdmissionGate == null)
        {
            throw WorkflowArtifactAdmissionFailures.GateUnavailable("station.import");
        }

        if (_workflowArtifactAdmissionGate != null)
        {
            var originalFlowJson = await File.ReadAllTextAsync(runtimeFlowPath, cancellationToken);
            var admission = _workflowArtifactAdmissionGate.InspectJson(originalFlowJson, "station.import");
            if (!admission.AllowedToSyncStation || admission.Flow == null)
            {
                var diagnostic = admission.Report.Diagnostics.FirstOrDefault()?.Code ??
                    $"workflow_artifact_{admission.Disposition.ToString().ToLowerInvariant()}";
                throw new RuntimePackageException(
                    $"Station import blocked by workflow artifact admission: {diagnostic}. {admission.Report.PublicMessage}");
            }

            if (admission.Disposition == WorkflowArtifactAdmissionDisposition.RepairableLegacy)
            {
                admittedFlowJson = JsonSerializer.Serialize(admission.Flow, StablePackageJsonOptions);
                var admittedEntity = admission.Flow.ToEntity();
                admittedFlowHash = ExecutionFlowIdentity.ComputeFlowHash(admittedEntity);
                admittedDecisionConfigurationHash = ExecutionFlowIdentity.ComputeDecisionConfigurationHash(
                    admittedEntity.DecisionConfiguration);
                runtimeManifest.FlowHash = admittedFlowHash;
                runtimeManifest.DecisionConfigurationHash = admittedDecisionConfigurationHash;
            }
        }

        var packageId = SanitizePackageId(runtimeManifest.PackageId);
        var packageDirectory = Path.Combine(FilesDirectory, packageId);
        Directory.CreateDirectory(packageDirectory);

        var staging = Path.Combine(packageDirectory, "staging");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        var stagedRuntimeRoot = Path.Combine(staging, "package");
        CopyDirectory(runtimeRoot, stagedRuntimeRoot);
        if (admittedFlowJson != null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(stagedRuntimeRoot, "flow.json"),
                admittedFlowJson,
                cancellationToken);
            await RewriteHashBoundMetadataAsync(
                stagedRuntimeRoot,
                admittedFlowHash!,
                admittedDecisionConfigurationHash!,
                cancellationToken);
        }

        var manifest = new StationPackageManifestDto
        {
            PackageId = packageId,
            PackageName = string.IsNullOrWhiteSpace(runtimeManifest.PackageName)
                ? packageId
                : runtimeManifest.PackageName.Trim(),
            PackageVersion = string.IsNullOrWhiteSpace(runtimeManifest.RuntimeApiVersion)
                ? "1.0"
                : runtimeManifest.RuntimeApiVersion.Trim(),
            PackageKind = StationPackageKind.Production,
            FlowHash = runtimeManifest.FlowHash,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy)
                ? (string.IsNullOrWhiteSpace(runtimeManifest.CreatedBy) ? "Studio" : runtimeManifest.CreatedBy.Trim())
                : createdBy.Trim(),
            MinStationVersion = string.IsNullOrWhiteSpace(runtimeManifest.MinStationVersion)
                ? "0.1.0"
                : runtimeManifest.MinStationVersion.Trim(),
            CreatedAtUtc = runtimeManifest.CreatedAt == default ? DateTimeOffset.UtcNow : runtimeManifest.CreatedAt
        };

        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(
            Path.Combine(staging, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var targetFile = Path.Combine(packageDirectory, $"{packageId}.cvpkg");
        if (File.Exists(targetFile))
        {
            File.Delete(targetFile);
        }

        ZipFile.CreateFromDirectory(staging, targetFile, CompressionLevel.Fastest, includeBaseDirectory: false);
        Directory.Delete(staging, recursive: true);

        manifest.SizeBytes = new FileInfo(targetFile).Length;
        manifest.Sha256 = await ComputeSha256Async(targetFile, cancellationToken);
        Persist(manifest, targetFile);
        return manifest;
    }

    private static async Task RewriteHashBoundMetadataAsync(
        string packageRoot,
        string flowHash,
        string decisionConfigurationHash,
        CancellationToken cancellationToken)
    {
        var files = new[]
        {
            Path.Combine(packageRoot, "quality", "validation-report.json"),
            Path.Combine(packageRoot, "field", "runtime-parameters.json"),
            Path.Combine(packageRoot, "field", "station-profile.default.json"),
            Path.Combine(packageRoot, "field", "station-profile.json"),
            Path.Combine(packageRoot, "field", "result-mapping-profile.json")
        };

        foreach (var file in files.Where(File.Exists))
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken));
            }
            catch (JsonException)
            {
                continue;
            }

            if (root == null)
            {
                continue;
            }

            RewriteHashProperties(root, flowHash, decisionConfigurationHash);
            await File.WriteAllTextAsync(file, root.ToJsonString(StablePackageJsonOptions), cancellationToken);
        }
    }

    private static void RewriteHashProperties(
        JsonNode node,
        string flowHash,
        string decisionConfigurationHash)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (property.Key.Equals("flowHash", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("packageFlowHash", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("executionFlowHash", StringComparison.OrdinalIgnoreCase))
                {
                    obj[property.Key] = flowHash;
                }
                else if (property.Key.Equals("decisionConfigurationHash", StringComparison.OrdinalIgnoreCase))
                {
                    obj[property.Key] = decisionConfigurationHash;
                }
                else if (property.Value != null)
                {
                    RewriteHashProperties(property.Value, flowHash, decisionConfigurationHash);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null)
                {
                    RewriteHashProperties(item, flowHash, decisionConfigurationHash);
                }
            }
        }
    }

    private static OperatorFlowDto CreateSmokeFlow(string packageId)
    {
        var judgmentId = Guid.NewGuid();
        var decisionPortId = Guid.NewGuid();
        return new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = $"Station deploy smoke {packageId}",
            DecisionConfiguration = new DecisionConfiguration
            {
                FinalDecisionBinding = new FinalDecisionBinding
                {
                    SourceOperatorId = judgmentId,
                    SourceOutputPortId = decisionPortId,
                    SourceOutputName = "JudgmentResult",
                    DataType = DecisionValueType.String,
                    Rule = DecisionInterpretationRule.StringMap,
                    OkValue = "OK",
                    NgValue = "NG"
                }
            },
            Operators =
            [
                new OperatorDto
                {
                    Id = judgmentId,
                    Name = "DeploymentSmokeJudgment",
                    Type = OperatorType.ResultJudgment,
                    X = 0,
                    Y = 0,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = decisionPortId,
                            Name = "JudgmentResult",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.String
                        }
                    ]
                }
            ],
            Connections = []
        };
    }

    private void Persist(StationPackageManifestDto manifest, string path)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var existing = db.StationPackageRecords.FirstOrDefault(item => item.PackageId == manifest.PackageId);
        if (existing == null)
        {
            existing = new StationPackageRecordEntity
            {
                PackageId = manifest.PackageId
            };
            db.StationPackageRecords.Add(existing);
        }

        existing.PackageName = manifest.PackageName;
        existing.PackageVersion = manifest.PackageVersion;
        existing.PackageKind = manifest.PackageKind.ToString();
        existing.FlowHash = manifest.FlowHash;
        existing.FileName = Path.GetFileName(path);
        existing.FilePath = path;
        existing.SizeBytes = manifest.SizeBytes;
        existing.Sha256 = manifest.Sha256;
        existing.CreatedBy = manifest.CreatedBy;
        existing.CreatedAtUtc = manifest.CreatedAtUtc;
        db.SaveChanges();

        _logger.LogInformation("Stored Station package {PackageId} at {PackagePath}", manifest.PackageId, path);
    }

    private static StationPackageManifestDto ToManifest(StationPackageRecordEntity entity)
    {
        return new StationPackageManifestDto
        {
            PackageId = entity.PackageId,
            PackageName = entity.PackageName,
            PackageVersion = entity.PackageVersion,
            PackageKind = ParsePackageKind(entity.PackageKind),
            FlowHash = entity.FlowHash,
            CreatedBy = entity.CreatedBy,
            SizeBytes = entity.SizeBytes,
            Sha256 = entity.Sha256,
            CreatedAtUtc = entity.CreatedAtUtc
        };
    }

    private static StationPackageKind ParsePackageKind(string? value)
    {
        return Enum.TryParse<StationPackageKind>(value, ignoreCase: true, out var parsed)
            ? parsed
            : StationPackageKind.Production;
    }

    private bool TryNormalizeAllowedPackagePath(string storedPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        try
        {
            var fullPath = Path.GetFullPath(storedPath);
            var filesRoot = EnsureTrailingSeparator(Path.GetFullPath(FilesDirectory));
            if (!fullPath.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string SanitizePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return $"pkg_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..32];
        }

        var safe = string.Concat(packageId.Trim().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'
                ? ch
                : '_'));
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".."
            ? $"pkg_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..32]
            : safe;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256WithPrefix(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
