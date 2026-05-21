using Acme.Product.Core.Cameras;
using Acme.Product.Core.Streaming;

namespace Acme.Product.Infrastructure.Cameras;

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

    public Task<CameraPreviewSession> StartPreviewSessionAsync(string cameraId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task<CameraStreamFrame> WaitForPreviewFrameAsync(string sessionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shared camera stream coordinator is not available in this context.");

    public Task StopPreviewSessionAsync(string sessionId) => Task.CompletedTask;

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
