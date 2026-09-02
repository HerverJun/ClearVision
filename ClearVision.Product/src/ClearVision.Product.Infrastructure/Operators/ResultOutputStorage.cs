using System.Globalization;
using System.Text;

namespace ClearVision.Product.Infrastructure.Operators;

public sealed record ResultOutputStorageHealth(
    string RootDirectory,
    int FileCount,
    long TotalBytes,
    DateTimeOffset? OldestFileAtUtc,
    long TrimmedFileCount,
    bool GapDetected,
    bool Degraded,
    DateTimeOffset? LastSuccessfulCleanupAtUtc);

public interface IResultOutputStorage
{
    string Save(string formattedText, string format);
    ResultOutputStorageHealth GetHealth();
}

/// <summary>
/// Server-owned result-output storage. The operator never accepts a path, and
/// all cleanup is limited to this manifest-owned root.
/// </summary>
public sealed class ResultOutputStorage : IResultOutputStorage
{
    private const string OwnershipManifestName = ".clearvision-result-output.manifest";
    private const string OwnershipManifestContents = "ClearVision.ResultOutput.v1";
    private readonly object _gate = new();
    private readonly string _rootDirectory;
    private readonly int _maxFiles;
    private readonly long _maxBytes;
    private readonly TimeSpan _retention;
    private readonly Func<DateTimeOffset> _utcNow;
    private long _trimmedFileCount;
    private bool _gapDetected;
    private bool _degraded;
    private DateTimeOffset? _lastSuccessfulCleanupAtUtc;

    public ResultOutputStorage(
        string? rootDirectory = null,
        int maxFiles = 500,
        long maxBytes = 64L * 1024 * 1024,
        TimeSpan? retention = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _rootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision",
                "ResultOutput")
            : rootDirectory);
        _maxFiles = Math.Max(1, maxFiles);
        _maxBytes = Math.Max(1024, maxBytes);
        _retention = retention.GetValueOrDefault(TimeSpan.FromDays(7));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string Save(string formattedText, string format)
    {
        ArgumentNullException.ThrowIfNull(formattedText);
        var extension = ResolveExtension(format);
        var encodedLength = Encoding.UTF8.GetByteCount(formattedText);
        if (encodedLength > _maxBytes)
        {
            throw new InvalidOperationException("RESULT_OUTPUT_QUOTA_EXCEEDED: one output exceeds the governed byte quota.");
        }

        lock (_gate)
        {
            EnsureOwnedRootLocked();
            CleanupLocked(encodedLength, incomingFileCount: 1);
            var now = _utcNow();
            var directory = Path.Combine(_rootDirectory, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"result_{now:HHmmssfff}_{Guid.NewGuid():N}{extension}");
            File.WriteAllText(filePath, formattedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.SetLastWriteTimeUtc(filePath, now.UtcDateTime);
            return filePath;
        }
    }

    public ResultOutputStorageHealth GetHealth()
    {
        lock (_gate)
        {
            try
            {
                EnsureOwnedRootLocked();
                CleanupLocked(incomingBytes: 0);
                var files = GetOwnedOutputFilesLocked();
                return new ResultOutputStorageHealth(
                    _rootDirectory,
                    files.Count,
                    files.Sum(file => file.Length),
                    files.Count == 0 ? null : files.Min(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)),
                    _trimmedFileCount,
                    _gapDetected,
                    _degraded,
                    _lastSuccessfulCleanupAtUtc);
            }
            catch
            {
                _degraded = true;
                return new ResultOutputStorageHealth(
                    _rootDirectory,
                    0,
                    0,
                    null,
                    _trimmedFileCount,
                    _gapDetected,
                    true,
                    _lastSuccessfulCleanupAtUtc);
            }
        }
    }

    private void CleanupLocked(long incomingBytes, int incomingFileCount = 0)
    {
        var now = _utcNow();
        var cutoff = now - (_retention <= TimeSpan.Zero ? TimeSpan.FromDays(7) : _retention);
        var files = GetOwnedOutputFilesLocked()
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var expired in files.Where(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) < cutoff).ToList())
        {
            DeleteOwnedFileLocked(expired);
            files.Remove(expired);
        }

        var totalBytes = files.Sum(file => file.Length);
        while (files.Count + incomingFileCount > _maxFiles || totalBytes + incomingBytes > _maxBytes)
        {
            if (files.Count == 0)
            {
                _degraded = true;
                throw new InvalidOperationException("RESULT_OUTPUT_QUOTA_EXCEEDED: governed output storage cannot satisfy its quota.");
            }

            var oldest = files[0];
            totalBytes -= oldest.Length;
            DeleteOwnedFileLocked(oldest);
            files.RemoveAt(0);
        }

        _degraded = false;
        _lastSuccessfulCleanupAtUtc = now;
    }

    private void EnsureOwnedRootLocked()
    {
        Directory.CreateDirectory(_rootDirectory);
        var manifest = Path.Combine(_rootDirectory, OwnershipManifestName);
        if (!File.Exists(manifest))
        {
            File.WriteAllText(manifest, OwnershipManifestContents, Encoding.UTF8);
            return;
        }

        var contents = File.ReadAllText(manifest, Encoding.UTF8).Trim();
        if (!string.Equals(contents, OwnershipManifestContents, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RESULT_OUTPUT_ROOT_NOT_OWNED: governed output root has an invalid ownership manifest.");
        }
    }

    private List<FileInfo> GetOwnedOutputFilesLocked()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), OwnershipManifestName, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .Where(file => IsWithinOwnedRoot(file.FullName))
            .ToList();
    }

    private void DeleteOwnedFileLocked(FileInfo file)
    {
        if (!IsWithinOwnedRoot(file.FullName))
        {
            throw new InvalidOperationException("RESULT_OUTPUT_ROOT_ESCAPE: cleanup target is outside the governed output root.");
        }

        File.Delete(file.FullName);
        _trimmedFileCount++;
        _gapDetected = true;
    }

    private bool IsWithinOwnedRoot(string path)
    {
        var relative = Path.GetRelativePath(_rootDirectory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string ResolveExtension(string? format) => format?.Trim().ToUpperInvariant() switch
    {
        "CSV" => ".csv",
        "TEXT" => ".txt",
        _ => ".json"
    };
}
