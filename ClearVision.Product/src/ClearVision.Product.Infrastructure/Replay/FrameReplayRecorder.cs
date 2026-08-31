using System.Collections.Concurrent;
using System.Text.Json;
using ClearVision.Product.Core.Streaming;
using ClearVision.Product.Infrastructure.Continuous;

namespace ClearVision.Product.Infrastructure.Replay;

public interface IFrameReplayRecorder
{
    Task<string> SaveTrackAsync(
        string trackId,
        IReadOnlyList<FrameEnvelope> frames,
        TrackDecision? decision = null,
        CancellationToken cancellationToken = default);
}

public interface IFrameReplayRecorderFactory
{
    IFrameReplayRecorder Create(string rootDirectory);
}

/// <summary>
/// Implemented by replay recorders that retain a durable, observable replay cache.
/// Callers that only need to persist a track should depend on <see cref="IFrameReplayRecorder"/>.
/// </summary>
public interface IReplayRetentionHealthProvider
{
    ReplayRetentionHealth GetRetentionHealth();
}

public sealed record ReplayRetentionOptions(
    int MaxTracks = 500,
    long MaxBytes = 2L * 1024 * 1024 * 1024,
    int RetentionDays = 30)
{
    public static ReplayRetentionOptions Default { get; } = new();
}

public sealed record ReplayRetentionHealth(
    string RootDirectory,
    int TrackCount,
    long TotalBytes,
    DateTimeOffset? OldestTrackAtUtc,
    long TrimmedTrackCount,
    bool GapDetected,
    bool Degraded,
    DateTimeOffset? LastSuccessfulCleanupAtUtc);

public sealed class FrameReplayRecorderFactory : IFrameReplayRecorderFactory
{
    public static FrameReplayRecorderFactory Instance { get; } = new();

    public IFrameReplayRecorder Create(string rootDirectory) => new FrameReplayRecorder(rootDirectory);
}

public static class FrameReplayFailureCodes
{
    public const string WriteFailed = "CONTINUOUS_REPLAY_WRITE_FAILED";
    public const string QuotaExceeded = "CONTINUOUS_REPLAY_QUOTA_EXCEEDED";
}

/// <summary>
/// Persists replay tracks under one server-owned root and keeps that root bounded.
/// A completed track is first written to a private pending directory and only becomes
/// visible after its metadata has been written. Retention only removes completed tracks.
/// </summary>
public sealed class FrameReplayRecorder : IFrameReplayRecorder, IReplayRetentionHealthProvider
{
    private const string PendingDirectoryName = ".pending";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, object> RootLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _rootDirectory;
    private readonly int _maxTracks;
    private readonly long _maxBytes;
    private readonly TimeSpan _retention;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _rootGate;
    private long _trimmedTrackCount;
    private bool _gapDetected;
    private bool _degraded;
    private DateTimeOffset? _lastSuccessfulCleanupAtUtc;

    public FrameReplayRecorder(
        string rootDirectory,
        ReplayRetentionOptions? retentionOptions = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        var options = retentionOptions ?? ReplayRetentionOptions.Default;
        _maxTracks = Math.Max(1, options.MaxTracks);
        _maxBytes = Math.Max(1, options.MaxBytes);
        _retention = TimeSpan.FromDays(Math.Clamp(options.RetentionDays, 1, 3650));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _rootGate = RootLocks.GetOrAdd(_rootDirectory, _ => new object());
    }

    public async Task<string> SaveTrackAsync(
        string trackId,
        IReadOnlyList<FrameEnvelope> frames,
        TrackDecision? decision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentNullException.ThrowIfNull(frames);

        var now = _utcNow();
        var safeTrackId = SanitizePathSegment(trackId);
        var pendingDirectory = Path.Combine(
            _rootDirectory,
            PendingDirectoryName,
            $"{safeTrackId}-{Guid.NewGuid():N}");
        string? completedDirectory = null;

        try
        {
            Directory.CreateDirectory(pendingDirectory);
            var metadata = new ReplayTrackMetadata(trackId, now, decision, new List<ReplayFrameMetadata>());

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = ResolveExtension(frame);
                var fileName = $"{frame.Sequence:D8}{extension}";
                var filePath = Path.Combine(pendingDirectory, fileName);
                await File.WriteAllBytesAsync(filePath, frame.Payload.ToArray(), cancellationToken);
                metadata.Frames.Add(new ReplayFrameMetadata(
                    frame.CameraId,
                    frame.Sequence,
                    frame.HostReceiveTimestampUtc,
                    frame.CameraTimestampNs,
                    frame.DeviceFrameCounter,
                    frame.TimestampSource.ToString(),
                    fileName));
            }

            var metadataPath = Path.Combine(pendingDirectory, "metadata.json");
            await File.WriteAllTextAsync(
                metadataPath,
                JsonSerializer.Serialize(metadata, JsonOptions),
                cancellationToken);

            lock (_rootGate)
            {
                Directory.CreateDirectory(_rootDirectory);
                var dateDirectory = Path.Combine(_rootDirectory, now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(dateDirectory);
                completedDirectory = GetAvailableTrackDirectoryLocked(dateDirectory, safeTrackId);
                Directory.Move(pendingDirectory, completedDirectory);

                try
                {
                    CleanupLocked();
                    if (!Directory.Exists(completedDirectory))
                    {
                        throw new IOException(
                            $"{FrameReplayFailureCodes.QuotaExceeded}: the completed replay track exceeds the governed retention quota.");
                    }
                }
                catch
                {
                    _degraded = true;
                    throw;
                }
            }

            return completedDirectory;
        }
        finally
        {
            if (Directory.Exists(pendingDirectory))
            {
                TryDeletePendingDirectory(pendingDirectory);
            }
        }
    }

    public ReplayRetentionHealth GetRetentionHealth()
    {
        lock (_rootGate)
        {
            try
            {
                Directory.CreateDirectory(_rootDirectory);
                CleanupLocked();
                return BuildHealthLocked(degraded: false);
            }
            catch
            {
                _degraded = true;
                return BuildHealthLocked(degraded: true);
            }
        }
    }

    private void CleanupLocked()
    {
        var now = _utcNow();
        var cutoff = now - _retention;
        var tracks = GetCompletedTracksLocked()
            .OrderBy(track => track.SavedAtUtc)
            .ThenBy(track => track.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var expired in tracks.Where(track => track.SavedAtUtc < cutoff).ToList())
        {
            DeleteCompletedTrackLocked(expired);
            tracks.Remove(expired);
        }

        var totalBytes = tracks.Sum(track => track.SizeBytes);
        while (tracks.Count > _maxTracks || totalBytes > _maxBytes)
        {
            if (tracks.Count == 0)
            {
                throw new IOException(
                    $"{FrameReplayFailureCodes.QuotaExceeded}: replay retention cannot satisfy the configured quota.");
            }

            var oldest = tracks[0];
            totalBytes -= oldest.SizeBytes;
            DeleteCompletedTrackLocked(oldest);
            tracks.RemoveAt(0);
        }

        _degraded = false;
        _lastSuccessfulCleanupAtUtc = now;
    }

    private ReplayRetentionHealth BuildHealthLocked(bool degraded)
    {
        var tracks = GetCompletedTracksLocked();
        return new ReplayRetentionHealth(
            _rootDirectory,
            tracks.Count,
            tracks.Sum(track => track.SizeBytes),
            tracks.Count == 0 ? null : tracks.Min(track => track.SavedAtUtc),
            _trimmedTrackCount,
            _gapDetected,
            degraded || _degraded,
            _lastSuccessfulCleanupAtUtc);
    }

    private List<ReplayTrackEntry> GetCompletedTracksLocked()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var tracks = new List<ReplayTrackEntry>();
        foreach (var dayDirectory in Directory.EnumerateDirectories(_rootDirectory))
        {
            if (string.Equals(Path.GetFileName(dayDirectory), PendingDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var trackDirectory in Directory.EnumerateDirectories(dayDirectory))
            {
                if (!IsCompletedTrackDirectory(trackDirectory))
                {
                    continue;
                }

                tracks.Add(new ReplayTrackEntry(
                    trackDirectory,
                    ReadSavedAtUtc(trackDirectory),
                    GetDirectorySize(trackDirectory)));
            }
        }

        return tracks;
    }

    private string GetAvailableTrackDirectoryLocked(string dateDirectory, string safeTrackId)
    {
        var candidate = Path.Combine(dateDirectory, safeTrackId);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        return Path.Combine(dateDirectory, $"{safeTrackId}-{Guid.NewGuid():N}");
    }

    private static bool IsCompletedTrackDirectory(string path) =>
        File.Exists(Path.Combine(path, "metadata.json"));

    private DateTimeOffset ReadSavedAtUtc(string trackDirectory)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<ReplayTrackMetadata>(
                File.ReadAllText(Path.Combine(trackDirectory, "metadata.json")),
                JsonOptions);
            if (metadata?.SavedAtUtc is { } savedAt && savedAt != default)
            {
                return savedAt;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt completed track still remains an owned retention candidate.
        }

        return new DateTimeOffset(Directory.GetLastWriteTimeUtc(trackDirectory), TimeSpan.Zero);
    }

    private static long GetDirectorySize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    private void DeleteCompletedTrackLocked(ReplayTrackEntry track)
    {
        if (!IsOwnedCompletedTrackPath(track.DirectoryPath))
        {
            throw new IOException("CONTINUOUS_REPLAY_ROOT_ESCAPE: retention target is outside the owned replay root.");
        }

        Directory.Delete(track.DirectoryPath, recursive: true);
        var parent = Directory.GetParent(track.DirectoryPath)?.FullName;
        if (parent != null && IsOwnedDateDirectory(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }

        _trimmedTrackCount++;
        _gapDetected = true;
    }

    private bool IsOwnedCompletedTrackPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_rootDirectory, fullPath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 &&
               !segments[0].Equals(PendingDirectoryName, StringComparison.OrdinalIgnoreCase) &&
               IsCompletedTrackDirectory(fullPath);
    }

    private bool IsOwnedDateDirectory(string path)
    {
        var relative = Path.GetRelativePath(_rootDirectory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) &&
               !relative.Contains(Path.DirectorySeparatorChar) &&
               !relative.Contains(Path.AltDirectorySeparatorChar) &&
               !relative.Equals(PendingDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeletePendingDirectory(string pendingDirectory)
    {
        try
        {
            Directory.Delete(pendingDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pending writes have no metadata and are never exposed as replay tracks.
        }
    }

    private static string ResolveExtension(FrameEnvelope frame)
    {
        var format = frame.PixelFormat.ToLowerInvariant();
        if (format.Contains("jpeg") || format.Contains("jpg"))
        {
            return ".jpg";
        }

        if (format.Contains("png"))
        {
            return ".png";
        }

        return ".bin";
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "track" : sanitized;
    }

    private sealed record ReplayTrackEntry(string DirectoryPath, DateTimeOffset SavedAtUtc, long SizeBytes);
}

public sealed record ReplayTrackMetadata(
    string TrackId,
    DateTimeOffset SavedAtUtc,
    TrackDecision? Decision,
    List<ReplayFrameMetadata> Frames);

public sealed record ReplayFrameMetadata(
    string CameraId,
    long Sequence,
    DateTimeOffset HostReceiveTimestampUtc,
    long? CameraTimestampNs,
    long? DeviceFrameCounter,
    string TimestampSource,
    string FileName);
