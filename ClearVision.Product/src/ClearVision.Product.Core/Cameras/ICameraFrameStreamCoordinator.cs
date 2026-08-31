namespace ClearVision.Product.Core.Cameras;

using ClearVision.Product.Core.Streaming;

public interface ICameraFrameStreamCoordinator : IAsyncDisposable
{
    Task<CameraStreamFrame> AcquireFrameAsync(string cameraId, CancellationToken cancellationToken = default);
    Task<FrameEnvelope> AcquireFrameEnvelopeAsync(string cameraId, CancellationToken cancellationToken = default);
    Task<CameraStreamLease> AcquireStreamLeaseAsync(string cameraId, CancellationToken cancellationToken = default);
    Task<CameraStreamFrame> WaitForNextFrameAsync(
        CameraStreamLease lease,
        long? afterSequence = null,
        CancellationToken cancellationToken = default);
    Task<FrameEnvelope> WaitForNextFrameEnvelopeAsync(
        CameraStreamLease lease,
        long? afterSequence = null,
        CancellationToken cancellationToken = default);
    Task ReleaseStreamLeaseAsync(CameraStreamLease lease);
    Task ReleaseIdleStreamAsync(string cameraId);
    Task<CameraPreviewSession> StartPreviewSessionAsync(
        string cameraId,
        string ownerHash,
        CancellationToken cancellationToken = default);
    Task<CameraStreamFrame> WaitForPreviewFrameAsync(
        string sessionId,
        string ownerHash,
        CancellationToken cancellationToken = default);
    Task<CameraPreviewHeartbeat?> HeartbeatPreviewSessionAsync(
        string sessionId,
        string ownerHash,
        CancellationToken cancellationToken = default);
    Task<bool> StopPreviewSessionAsync(string sessionId, string ownerHash);
    bool TryGetLatestFrameEnvelope(string cameraId, out FrameEnvelope? frame);
    IReadOnlyList<FrameEnvelope> GetFrameEnvelopeWindow(string cameraId, long centerSequence, int before, int after);
    RingBufferStats SnapshotFrameBufferStats(string cameraId);
    CameraStreamUsageSnapshot SnapshotStreamUsage(string cameraId);
}

public sealed record CameraStreamFrame(
    string CameraBindingId,
    byte[] ImageData,
    string ContentType,
    int Width,
    int Height,
    long Sequence,
    DateTime TimestampUtc,
    long? CameraTimestampNs = null,
    long? DeviceFrameCounter = null,
    int? Stride = null);

public sealed record CameraStreamLease(
    string LeaseId,
    string CameraBindingId,
    CameraTriggerMode TriggerMode,
    int TargetFrameRateFps);

public sealed record CameraPreviewSession(
    string SessionId,
    string CameraBindingId,
    CameraTriggerMode TriggerMode,
    int TargetFrameRateFps,
    DateTimeOffset ExpiresAtUtc,
    int HeartbeatIntervalMs);

public sealed record CameraPreviewHeartbeat(
    string SessionId,
    DateTimeOffset ExpiresAtUtc);

public sealed record RingBufferStats(
    int Capacity,
    int Count,
    long OverwrittenCount,
    long? OldestSequence,
    long? LatestSequence);

public sealed record CameraStreamUsageSnapshot(
    string CameraBindingId,
    bool IsRunning,
    int LeaseCount,
    int PreviewSessionCount,
    int PendingFrameWaiters,
    CameraTriggerMode TriggerMode,
    int TargetFrameRateFps);
