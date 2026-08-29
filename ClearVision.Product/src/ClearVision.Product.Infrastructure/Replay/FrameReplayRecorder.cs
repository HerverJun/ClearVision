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

public sealed class FrameReplayRecorderFactory : IFrameReplayRecorderFactory
{
    public static FrameReplayRecorderFactory Instance { get; } = new();

    public IFrameReplayRecorder Create(string rootDirectory) => new FrameReplayRecorder(rootDirectory);
}

public static class FrameReplayFailureCodes
{
    public const string WriteFailed = "CONTINUOUS_REPLAY_WRITE_FAILED";
}

public sealed class FrameReplayRecorder : IFrameReplayRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _rootDirectory;

    public FrameReplayRecorder(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    public async Task<string> SaveTrackAsync(
        string trackId,
        IReadOnlyList<FrameEnvelope> frames,
        TrackDecision? decision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentNullException.ThrowIfNull(frames);

        var safeTrackId = SanitizePathSegment(trackId);
        var trackDirectory = Path.Combine(
            _rootDirectory,
            DateTime.UtcNow.ToString("yyyyMMdd"),
            safeTrackId);
        Directory.CreateDirectory(trackDirectory);

        var metadata = new ReplayTrackMetadata(trackId, DateTimeOffset.UtcNow, decision, new List<ReplayFrameMetadata>());

        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = ResolveExtension(frame);
            var fileName = $"{frame.Sequence:D8}{extension}";
            var filePath = Path.Combine(trackDirectory, fileName);
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

        var metadataPath = Path.Combine(trackDirectory, "metadata.json");
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            cancellationToken);

        return trackDirectory;
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
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
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
