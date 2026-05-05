using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Desktop.Station;

public sealed class StationPackageStore
{
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
            .OrderByDescending(item => item.CreatedAtUtc)
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

        Directory.CreateDirectory(Path.Combine(staging, "package"));
        await File.WriteAllTextAsync(Path.Combine(staging, "package", "README.txt"), "ClearVision Station deployment test package.", cancellationToken);

        var manifest = new StationPackageManifestDto
        {
            PackageId = packageId,
            PackageName = packageName,
            PackageVersion = packageVersion,
            FlowHash = $"sha256:{Guid.NewGuid():N}",
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
}
