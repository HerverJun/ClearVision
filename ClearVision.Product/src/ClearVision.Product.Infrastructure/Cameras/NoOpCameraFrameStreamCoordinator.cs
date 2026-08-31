using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Streaming;

namespace ClearVision.Product.Infrastructure.Cameras;

internal sealed class NoOpCameraFrameStreamCoordinator : ICameraFrameStreamCoordinator
{
    public static NoOpCameraFrameStreamCoordinator Instance { get; } = new();

    private NoOpCameraFrameStreamCoordinator()
    {
    }

    public Task<CameraStreamFrame> AcquireFrameAsync(string cameraId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<FrameEnvelope> AcquireFrameEnvelopeAsync(string cameraId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<CameraStreamLease> AcquireStreamLeaseAsync(string cameraId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<CameraStreamFrame> WaitForNextFrameAsync(CameraStreamLease lease, long? afterSequence = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<FrameEnvelope> WaitForNextFrameEnvelopeAsync(CameraStreamLease lease, long? afterSequence = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task ReleaseStreamLeaseAsync(CameraStreamLease lease) => Task.CompletedTask;

    public Task ReleaseIdleStreamAsync(string cameraId) => Task.CompletedTask;

    public Task<CameraPreviewSession> StartPreviewSessionAsync(
        string cameraId,
        string ownerHash,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<CameraStreamFrame> WaitForPreviewFrameAsync(
        string sessionId,
        string ownerHash,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<CameraPreviewHeartbeat?> HeartbeatPreviewSessionAsync(
        string sessionId,
        string ownerHash,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CameraPreviewHeartbeat?>(null);

    public Task<bool> StopPreviewSessionAsync(string sessionId, string ownerHash) => Task.FromResult(false);

    public bool TryGetLatestFrameEnvelope(string cameraId, out FrameEnvelope? frame)
    {
        frame = null;
        return false;
    }

    public IReadOnlyList<FrameEnvelope> GetFrameEnvelopeWindow(string cameraId, long centerSequence, int before, int after) =>
        Array.Empty<FrameEnvelope>();

    public RingBufferStats SnapshotFrameBufferStats(string cameraId) =>
        new(0, 0, 0, null, null);

    public CameraStreamUsageSnapshot SnapshotStreamUsage(string cameraId) =>
        new(cameraId, false, 0, 0, 0, CameraTriggerMode.Software, CameraTriggerModeExtensions.DefaultTargetFrameRateFps);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
