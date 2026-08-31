using System.Globalization;
using System.Text;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Infrastructure.Services;

internal sealed class InspectionImageStorageGovernor
{
    private const string OwnershipManifestName = ".clearvision-inspection-images.manifest";
    private const string OwnershipManifestContents = "ClearVision.InspectionImages.v1";

    private readonly object _gate = new();
    private readonly IInspectionStorageFreeSpaceProvider _freeSpaceProvider;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<string, RootSnapshot> _roots = new(StringComparer.OrdinalIgnoreCase);
    private long _trimmedFileCount;
    private bool _gapDetected;
    private bool _degraded;
    private DateTimeOffset? _lastSuccessfulCleanupAtUtc;

    public InspectionImageStorageGovernor(
        IInspectionStorageFreeSpaceProvider? freeSpaceProvider = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _freeSpaceProvider = freeSpaceProvider ?? new DriveInspectionStorageFreeSpaceProvider();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public bool TryPrepareRoot(StorageConfig storage, string candidateRoot, out string managedRoot)
    {
        lock (_gate)
        {
            managedRoot = string.Empty;
            try
            {
                managedRoot = EnsureManagedRootLocked(candidateRoot);
                var snapshot = CleanupAndMeasureLocked(managedRoot, storage.RetentionDays);
                var minimumFreeBytes = Math.Max(0L, storage.MinFreeSpaceGb) * 1024L * 1024L * 1024L;
                if (minimumFreeBytes > 0 &&
                    (!snapshot.AvailableFreeBytes.HasValue || snapshot.AvailableFreeBytes.Value < minimumFreeBytes))
                {
                    _roots[managedRoot] = snapshot with { Degraded = true };
                    _degraded = true;
                    return false;
                }

                _roots[managedRoot] = snapshot;
                _degraded = _roots.Values.Any(root => root.Degraded);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (!string.IsNullOrWhiteSpace(managedRoot))
                {
                    _roots[managedRoot] = new RootSnapshot(0, 0, null, null, true);
                }

                _degraded = true;
                return false;
            }
        }
    }

    public void EnsureProductionStartAllowed(StorageConfig storage)
    {
        lock (_gate)
        {
            var failures = new List<string>();
            foreach (var candidateRoot in InspectionImagePersistencePaths.ResolveImageSaveRoots(storage.ImageSavePath))
            {
                if (TryPrepareRoot(storage, candidateRoot, out var managedRoot))
                {
                    return;
                }

                failures.Add(string.IsNullOrWhiteSpace(managedRoot) ? candidateRoot : managedRoot);
            }

            var configuredMinimum = Math.Max(0, storage.MinFreeSpaceGb);
            throw new InvalidOperationException(
                $"INSPECTION_STORAGE_START_BLOCKED: no managed image root meets MinFreeSpaceGb={configuredMinimum}. Roots={string.Join(";", failures)}.");
        }
    }

    public InspectionImageStorageHealth GetHealth()
    {
        lock (_gate)
        {
            var snapshots = _roots.Values.ToList();
            return new InspectionImageStorageHealth(
                snapshots.Count,
                snapshots.Sum(root => root.FileCount),
                snapshots.Sum(root => root.TotalBytes),
                snapshots.Count == 0 ? null : snapshots.Where(root => root.OldestImageAtUtc.HasValue).Select(root => root.OldestImageAtUtc).Min(),
                _trimmedFileCount,
                _gapDetected,
                _degraded || snapshots.Any(root => root.Degraded),
                snapshots.Select(root => root.AvailableFreeBytes).FirstOrDefault(bytes => bytes.HasValue),
                _lastSuccessfulCleanupAtUtc);
        }
    }

    private string EnsureManagedRootLocked(string candidateRoot)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateRoot));
        Directory.CreateDirectory(normalizedCandidate);
        if (HasValidManifest(normalizedCandidate))
        {
            return normalizedCandidate;
        }

        var manifestPath = Path.Combine(normalizedCandidate, OwnershipManifestName);
        if (File.Exists(manifestPath))
        {
            throw new IOException("INSPECTION_IMAGE_ROOT_NOT_OWNED: image root has an invalid ownership manifest.");
        }

        if (!Directory.EnumerateFileSystemEntries(normalizedCandidate).Any())
        {
            WriteManifest(normalizedCandidate);
            return normalizedCandidate;
        }

        // An administrator may configure an existing broad directory. Never claim or
        // recursively clean it. A dedicated child gives ClearVision an owned boundary.
        var managedChild = Path.Combine(normalizedCandidate, ".clearvision-managed-images");
        Directory.CreateDirectory(managedChild);
        if (!HasValidManifest(managedChild))
        {
            var childManifest = Path.Combine(managedChild, OwnershipManifestName);
            if (File.Exists(childManifest))
            {
                throw new IOException("INSPECTION_IMAGE_ROOT_NOT_OWNED: managed image child has an invalid ownership manifest.");
            }

            if (Directory.EnumerateFileSystemEntries(managedChild).Any())
            {
                throw new IOException("INSPECTION_IMAGE_ROOT_NOT_EMPTY: managed image child cannot be safely claimed.");
            }

            WriteManifest(managedChild);
        }

        return managedChild;
    }

    private RootSnapshot CleanupAndMeasureLocked(string managedRoot, int retentionDays)
    {
        if (!HasValidManifest(managedRoot))
        {
            throw new IOException("INSPECTION_IMAGE_ROOT_NOT_OWNED: cleanup requires a valid ownership manifest.");
        }

        var now = _utcNow();
        var cutoffDate = retentionDays > 0
            ? DateOnly.FromDateTime(now.UtcDateTime.Date).AddDays(-Math.Min(retentionDays, 3650))
            : (DateOnly?)null;

        foreach (var dayDirectory in Directory.EnumerateDirectories(managedRoot))
        {
            if (!TryGetOwnedDateDirectory(dayDirectory, out var day))
            {
                continue;
            }

            if (cutoffDate.HasValue && day < cutoffDate.Value)
            {
                var files = CountOwnedImageFiles(dayDirectory);
                Directory.Delete(dayDirectory, recursive: true);
                _trimmedFileCount += files;
                _gapDetected = _gapDetected || files > 0;
            }
        }

        var filesAfterCleanup = EnumerateOwnedImageFiles(managedRoot).ToList();
        DateTimeOffset? oldest = filesAfterCleanup.Count == 0
            ? null
            : filesAfterCleanup.Min(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        var snapshot = new RootSnapshot(
            filesAfterCleanup.Count,
            filesAfterCleanup.Sum(file => file.Length),
            oldest,
            _freeSpaceProvider.GetAvailableFreeBytes(managedRoot),
            false);
        _lastSuccessfulCleanupAtUtc = now;
        return snapshot;
    }

    private static bool HasValidManifest(string root)
    {
        var manifestPath = Path.Combine(root, OwnershipManifestName);
        return File.Exists(manifestPath) &&
               string.Equals(
                   File.ReadAllText(manifestPath, Encoding.UTF8).Trim(),
                   OwnershipManifestContents,
                   StringComparison.Ordinal);
    }

    private static void WriteManifest(string root)
    {
        File.WriteAllText(
            Path.Combine(root, OwnershipManifestName),
            OwnershipManifestContents,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool TryGetOwnedDateDirectory(string path, out DateOnly day)
    {
        return DateOnly.TryParseExact(
            Path.GetFileName(path),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out day);
    }

    private static int CountOwnedImageFiles(string dayDirectory) => Directory
        .EnumerateDirectories(dayDirectory)
        .Where(statusDirectory => Path.GetFileName(statusDirectory) is "OK" or "NG" or "ERROR")
        .SelectMany(statusDirectory => Directory.EnumerateFiles(statusDirectory, "*", SearchOption.TopDirectoryOnly))
        .Count();

    private static IEnumerable<FileInfo> EnumerateOwnedImageFiles(string managedRoot)
    {
        foreach (var dayDirectory in Directory.EnumerateDirectories(managedRoot))
        {
            if (!TryGetOwnedDateDirectory(dayDirectory, out _))
            {
                continue;
            }

            foreach (var statusDirectory in Directory.EnumerateDirectories(dayDirectory))
            {
                var status = Path.GetFileName(statusDirectory);
                if (status is not ("OK" or "NG" or "ERROR"))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(statusDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    yield return new FileInfo(file);
                }
            }
        }
    }

    private sealed record RootSnapshot(
        int FileCount,
        long TotalBytes,
        DateTimeOffset? OldestImageAtUtc,
        long? AvailableFreeBytes,
        bool Degraded);
}

public sealed class DriveInspectionStorageFreeSpaceProvider : IInspectionStorageFreeSpaceProvider
{
    public long? GetAvailableFreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
