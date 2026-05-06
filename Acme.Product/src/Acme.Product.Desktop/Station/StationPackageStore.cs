using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Application.DTOs;
using Acme.Product.Core.Enums;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Desktop.Station;

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
    private readonly string _rootDirectory;

    public StationPackageStore(
        IServiceScopeFactory scopeFactory,
        ILogger<StationPackageStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
            .Select(item => new StationPackageManifestDto
            {
                PackageId = item.PackageId,
                PackageName = item.PackageName,
                PackageVersion = item.PackageVersion,
                FlowHash = item.FlowHash,
                CreatedBy = item.CreatedBy,
                SizeBytes = item.SizeBytes,
                Sha256 = item.Sha256,
                CreatedAtUtc = item.CreatedAtUtc
            })
            .ToList();
    }

    public StationPackageManifestDto? GetPackage(string packageId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var entity = db.StationPackageRecords.FirstOrDefault(item => item.PackageId == packageId);
        return entity == null
            ? null
            : new StationPackageManifestDto
            {
                PackageId = entity.PackageId,
                PackageName = entity.PackageName,
                PackageVersion = entity.PackageVersion,
                FlowHash = entity.FlowHash,
                CreatedBy = entity.CreatedBy,
                SizeBytes = entity.SizeBytes,
                Sha256 = entity.Sha256,
                CreatedAtUtc = entity.CreatedAtUtc
            };
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
        var flowHash = ComputeSha256WithPrefix(flowBytes);
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

    private static OperatorFlowDto CreateSmokeFlow(string packageId)
    {
        return new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = $"Station deploy smoke {packageId}",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "DeploymentSmokeResult",
                    Type = OperatorType.ResultOutput,
                    X = 0,
                    Y = 0,
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Format",
                            DisplayName = "Format",
                            DataType = "enum",
                            DefaultValue = "JSON",
                            Value = "JSON",
                            IsRequired = false,
                            Options =
                            [
                                new Acme.Product.Core.ValueObjects.ParameterOption { Label = "JSON", Value = "JSON" },
                                new Acme.Product.Core.ValueObjects.ParameterOption { Label = "CSV", Value = "CSV" },
                                new Acme.Product.Core.ValueObjects.ParameterOption { Label = "Text", Value = "Text" }
                            ]
                        },
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "SaveToFile",
                            DisplayName = "SaveToFile",
                            DataType = "bool",
                            DefaultValue = false,
                            Value = false,
                            IsRequired = false
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
