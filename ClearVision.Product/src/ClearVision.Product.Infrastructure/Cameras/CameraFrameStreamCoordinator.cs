using System.Collections.Concurrent;
using System.Globalization;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Streaming;
using ClearVision.Product.Infrastructure.Streaming;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Cameras;

public sealed class CameraFrameStreamCoordinator : ICameraFrameStreamCoordinator
{
    private static readonly TimeSpan DirectAcquireIdleTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultFrameHealthProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultPreviewSessionTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPreviewHeartbeatInterval = TimeSpan.FromSeconds(10);
    private readonly ICameraManager _cameraManager;
    private readonly ILogger<CameraFrameStreamCoordinator> _logger;
    private readonly ITriggerInputService _triggerInputService;
    private readonly ISerialPhotoelectricTriggerInputService _serialPhotoelectricTriggerInputService;
    private readonly TimeSpan _frameHealthProbeInterval;
    private readonly TimeSpan _directAcquireIdleTimeout;
    private readonly TimeSpan _previewSessionTtl;
    private readonly TimeSpan _previewHeartbeatInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, ProducerEntry> _producers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PreviewSessionState> _previewSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ProducerEntry> _retiredProducers = new();

    private sealed class ProducerEntry
    {
        public ProducerEntry(string cameraBindingId)
        {
            CameraBindingId = cameraBindingId;
        }

        public string CameraBindingId { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public HashSet<string> ActiveStreamLeaseIds { get; } = new(StringComparer.Ordinal);
        public int LeaseCount { get; set; }
        public int PreviewSessionCount { get; set; }
        public bool IsRunning { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string ConfigurationSignature { get; set; } = string.Empty;
        public CameraTriggerMode TriggerMode { get; set; } = CameraTriggerMode.Software;
        public int TargetFrameRateFps { get; set; } = CameraTriggerModeExtensions.DefaultTargetFrameRateFps;
        public CameraStreamFrame? LatestFrame { get; set; }
        public FrameRingBuffer History { get; set; } = new(24);
        public long Sequence;
        public long LastPublishedTicks;
        public long MinFrameTicks { get; set; } = TimeSpan.TicksPerSecond / CameraTriggerModeExtensions.DefaultTargetFrameRateFps;
        public int PendingFrameWaiters;
        public object SignalGate { get; } = new();
        public TaskCompletionSource<long> NextFrameSignal { get; set; } = CreateFrameSignal();
        public CancellationTokenSource? IdleStopCts { get; set; }
        public ICameraLease? CameraLease { get; set; }
        public IIndustrialCamera? EventCamera { get; set; }
        public EventHandler<CameraFrameReceivedEventArgs>? FrameReceivedHandler { get; set; }
    }

    private sealed class PreviewSessionState
    {
        public required string SessionId { get; init; }
        public required string CameraBindingId { get; init; }
        public required string OwnerHash { get; init; }
        public object Gate { get; } = new();
        public CancellationTokenSource LifetimeCts { get; } = new();
        public ITimer? ExpiryTimer { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public bool IsClosing { get; set; }
        public long LastObservedSequence { get; set; }
        public CameraTriggerMode TriggerMode { get; init; }
        public int TargetFrameRateFps { get; init; }
    }

    private sealed record ResolvedBinding(
        string CameraBindingId,
        string SerialNumber,
        double ExposureTimeUs,
        double GainDb,
        CameraPixelFormat PixelFormat,
        CameraTriggerMode TriggerMode,
        string HardwareTriggerSource,
        EnterPhotoelectricTriggerOptions? EnterPhotoelectricTrigger,
        SerialPhotoelectricTriggerOptions? SerialPhotoelectricTrigger,
        int TargetFrameRateFps,
        int FrameBufferCapacity);

    public CameraFrameStreamCoordinator(
        ICameraManager cameraManager,
        ILogger<CameraFrameStreamCoordinator> logger)
        : this(cameraManager, logger, NoOpTriggerInputService.Instance, NoOpSerialPhotoelectricTriggerInputService.Instance)
    {
    }

    public CameraFrameStreamCoordinator(
        ICameraManager cameraManager,
        ILogger<CameraFrameStreamCoordinator> logger,
        ITriggerInputService triggerInputService)
        : this(cameraManager, logger, triggerInputService, NoOpSerialPhotoelectricTriggerInputService.Instance)
    {
    }

    public CameraFrameStreamCoordinator(
        ICameraManager cameraManager,
        ILogger<CameraFrameStreamCoordinator> logger,
        ITriggerInputService triggerInputService,
        ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService)
        : this(cameraManager, logger, triggerInputService, serialPhotoelectricTriggerInputService, DefaultFrameHealthProbeInterval)
    {
    }

    public CameraFrameStreamCoordinator(
        ICameraManager cameraManager,
        ILogger<CameraFrameStreamCoordinator> logger,
        ITriggerInputService triggerInputService,
        ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService,
        TimeSpan frameHealthProbeInterval)
        : this(cameraManager, logger, triggerInputService, serialPhotoelectricTriggerInputService, frameHealthProbeInterval, DirectAcquireIdleTimeout)
    {
    }

    internal CameraFrameStreamCoordinator(
        ICameraManager cameraManager,
        ILogger<CameraFrameStreamCoordinator> logger,
        ITriggerInputService triggerInputService,
        ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService,
        TimeSpan frameHealthProbeInterval,
        TimeSpan directAcquireIdleTimeout)
        : this(
            cameraManager,
            logger,
            triggerInputService,
            serialPhotoelectricTriggerInputService,
            frameHealthProbeInterval,
            directAcquireIdleTimeout,
            DefaultPreviewSessionTtl,
            DefaultPreviewHeartbeatInterval,
            TimeProvider.System)
    {
    }

    internal CameraFrameStreamCoordinator(
        ICameraManager cameraManager,
        ILogger<CameraFrameStreamCoordinator> logger,
        ITriggerInputService triggerInputService,
        ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService,
        TimeSpan frameHealthProbeInterval,
        TimeSpan directAcquireIdleTimeout,
        TimeSpan previewSessionTtl,
        TimeSpan previewHeartbeatInterval,
        TimeProvider timeProvider)
    {
        _cameraManager = cameraManager;
        _logger = logger;
        _triggerInputService = triggerInputService;
        _serialPhotoelectricTriggerInputService = serialPhotoelectricTriggerInputService;
        _frameHealthProbeInterval = frameHealthProbeInterval > TimeSpan.Zero
            ? frameHealthProbeInterval
            : DefaultFrameHealthProbeInterval;
        _directAcquireIdleTimeout = directAcquireIdleTimeout > TimeSpan.Zero
            ? directAcquireIdleTimeout
            : DirectAcquireIdleTimeout;
        _previewSessionTtl = previewSessionTtl > TimeSpan.Zero
            ? previewSessionTtl
            : throw new ArgumentOutOfRangeException(nameof(previewSessionTtl));
        _previewHeartbeatInterval = previewHeartbeatInterval > TimeSpan.Zero &&
                                    previewHeartbeatInterval < _previewSessionTtl
            ? previewHeartbeatInterval
            : throw new ArgumentOutOfRangeException(nameof(previewHeartbeatInterval));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CameraStreamFrame> AcquireFrameAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var binding = ResolveBinding(cameraId);
        if (!binding.TriggerMode.IsFrameDriven())
        {
            return await AcquireSoftwareFrameAsync(binding, cancellationToken);
        }

        var entry = await EnsureProducerAsync(binding, cancellationToken);
        long? afterSequence;
        CancellationTokenSource? idleStopCts = null;
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            idleStopCts = entry.IdleStopCts;
            entry.IdleStopCts = null;
            afterSequence = entry.LatestFrame?.Sequence;
        }
        finally
        {
            entry.Gate.Release();
        }

        CancelAndDisposeIdleStop(idleStopCts);
        try
        {
            return await WaitForFrameCoreAsync(entry, afterSequence, cancellationToken);
        }
        finally
        {
            await ArmDirectAcquireIdleStopAsync(entry);
        }
    }

    public async Task<FrameEnvelope> AcquireFrameEnvelopeAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var frame = await AcquireFrameAsync(cameraId, cancellationToken);
        return ToEnvelope(frame);
    }

    public async Task<CameraStreamLease> AcquireStreamLeaseAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var binding = ResolveBinding(cameraId);
        if (!binding.TriggerMode.IsFrameDriven())
        {
            throw new InvalidOperationException($"Camera binding '{binding.CameraBindingId}' is not configured for frame-driven acquisition.");
        }

        var entry = await EnsureProducerAsync(binding, cancellationToken);
        CancellationTokenSource? idleStopCts = null;
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            idleStopCts = entry.IdleStopCts;
            entry.IdleStopCts = null;
            var leaseId = Guid.NewGuid().ToString("N");
            entry.ActiveStreamLeaseIds.Add(leaseId);
            entry.LeaseCount = entry.ActiveStreamLeaseIds.Count;
            return new CameraStreamLease(
                leaseId,
                binding.CameraBindingId,
                binding.TriggerMode,
                binding.TargetFrameRateFps);
        }
        finally
        {
            entry.Gate.Release();
            CancelAndDisposeIdleStop(idleStopCts);
        }
    }

    public async Task<CameraStreamFrame> WaitForNextFrameAsync(
        CameraStreamLease lease,
        long? afterSequence = null,
        CancellationToken cancellationToken = default)
    {
        if (!_producers.TryGetValue(lease.CameraBindingId, out var entry))
        {
            throw new KeyNotFoundException($"Camera stream producer not found: {lease.CameraBindingId}");
        }

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentProducer(entry) ||
                !entry.IsRunning ||
                !entry.ActiveStreamLeaseIds.Contains(lease.LeaseId))
            {
                throw new KeyNotFoundException("Camera stream lease was not found.");
            }
        }
        finally
        {
            entry.Gate.Release();
        }

        return await WaitForFrameCoreAsync(entry, afterSequence, cancellationToken);
    }

    public async Task<FrameEnvelope> WaitForNextFrameEnvelopeAsync(
        CameraStreamLease lease,
        long? afterSequence = null,
        CancellationToken cancellationToken = default)
    {
        var frame = await WaitForNextFrameAsync(lease, afterSequence, cancellationToken);
        return ToEnvelope(frame);
    }

    public async Task ReleaseStreamLeaseAsync(CameraStreamLease lease)
    {
        if (!_producers.TryGetValue(lease.CameraBindingId, out var entry))
        {
            return;
        }

        await entry.Gate.WaitAsync();
        try
        {
            if (!entry.ActiveStreamLeaseIds.Remove(lease.LeaseId))
            {
                return;
            }

            entry.LeaseCount = entry.ActiveStreamLeaseIds.Count;

            if (entry.LeaseCount == 0 && entry.PreviewSessionCount == 0)
            {
                await StopProducerCoreAsync(entry);
                RetireProducer(entry);
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task ReleaseIdleStreamAsync(string cameraId)
    {
        var producerKey = ResolveProducerKey(cameraId);
        if (!_producers.TryGetValue(producerKey, out var entry))
        {
            return;
        }

        await entry.Gate.WaitAsync();
        try
        {
            if (entry.LeaseCount == 0 &&
                entry.PreviewSessionCount == 0 &&
                Volatile.Read(ref entry.PendingFrameWaiters) == 0)
            {
                await StopProducerCoreAsync(entry);
                RetireProducer(entry);
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task<CameraPreviewSession> StartPreviewSessionAsync(
        string cameraId,
        string ownerHash,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerHash = NormalizePreviewOwnerHash(ownerHash);
        var binding = ResolveBinding(cameraId);
        if (!binding.TriggerMode.IsFrameDriven())
        {
            throw new InvalidOperationException($"Camera binding '{binding.CameraBindingId}' is not configured for continuous preview.");
        }

        var configurationSignature = CreateConfigurationSignature(binding);
        while (true)
        {
            var entry = await EnsureProducerAsync(binding, cancellationToken);
            CancellationTokenSource? idleStopCts = null;
            PreviewSessionState? state = null;
            await entry.Gate.WaitAsync(cancellationToken);
            try
            {
                if (!IsCurrentProducer(entry) ||
                    !entry.IsRunning ||
                    !string.Equals(
                        entry.ConfigurationSignature,
                        configurationSignature,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var sessionId = Guid.NewGuid().ToString("N");
                var expiresAtUtc = _timeProvider.GetUtcNow().Add(_previewSessionTtl);
                var newState = new PreviewSessionState
                {
                    SessionId = sessionId,
                    CameraBindingId = binding.CameraBindingId,
                    OwnerHash = normalizedOwnerHash,
                    TriggerMode = binding.TriggerMode,
                    TargetFrameRateFps = binding.TargetFrameRateFps,
                    LastObservedSequence = 0,
                    ExpiresAtUtc = expiresAtUtc
                };
                state = newState;
                newState.ExpiryTimer = _timeProvider.CreateTimer(
                    _ => OnPreviewSessionExpiryTimer(newState),
                    null,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
                idleStopCts = entry.IdleStopCts;
                entry.IdleStopCts = null;
                if (!_previewSessions.TryAdd(sessionId, newState))
                {
                    throw new InvalidOperationException("Unable to allocate a unique preview session.");
                }

                entry.PreviewSessionCount++;
            }
            catch
            {
                state?.ExpiryTimer?.Dispose();
                state?.LifetimeCts.Dispose();
                throw;
            }
            finally
            {
                entry.Gate.Release();
                CancelAndDisposeIdleStop(idleStopCts);
            }

            state!.ExpiryTimer!.Change(_previewSessionTtl, Timeout.InfiniteTimeSpan);
            return new CameraPreviewSession(
                state.SessionId,
                binding.CameraBindingId,
                binding.TriggerMode,
                binding.TargetFrameRateFps,
                state.ExpiresAtUtc,
                checked((int)_previewHeartbeatInterval.TotalMilliseconds));
        }
    }

    public async Task<CameraStreamFrame> WaitForPreviewFrameAsync(
        string sessionId,
        string ownerHash,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = NormalizePreviewSessionId(sessionId);
        var normalizedOwnerHash = NormalizePreviewOwnerHash(ownerHash);
        if (!_previewSessions.TryGetValue(normalizedSessionId, out var session))
        {
            throw CreatePreviewSessionNotFoundException();
        }

        if (!_producers.TryGetValue(session.CameraBindingId, out var entry))
        {
            throw CreatePreviewSessionNotFoundException();
        }

        long? afterSequence;
        CancellationTokenSource linkedCts;
        var expired = false;
        lock (session.Gate)
        {
            if (session.IsClosing ||
                !string.Equals(session.OwnerHash, normalizedOwnerHash, StringComparison.Ordinal))
            {
                throw CreatePreviewSessionNotFoundException();
            }

            if (_timeProvider.GetUtcNow() >= session.ExpiresAtUtc)
            {
                expired = true;
                linkedCts = new CancellationTokenSource();
                afterSequence = null;
            }
            else
            {
                afterSequence = session.LastObservedSequence > 0 ? session.LastObservedSequence : null;
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    session.LifetimeCts.Token);
            }
        }

        if (expired)
        {
            linkedCts.Dispose();
            if (TryClaimPreviewSession(session, normalizedOwnerHash, requireExpired: true))
            {
                await CompletePreviewSessionStopAsync(session, "expired");
            }

            throw CreatePreviewSessionNotFoundException();
        }

        using (linkedCts)
        {
            var frame = await WaitForFrameCoreAsync(entry, afterSequence, linkedCts.Token);
            var expiredAfterWait = false;
            lock (session.Gate)
            {
                if (session.IsClosing ||
                    !string.Equals(session.OwnerHash, normalizedOwnerHash, StringComparison.Ordinal))
                {
                    throw CreatePreviewSessionNotFoundException();
                }

                if (_timeProvider.GetUtcNow() >= session.ExpiresAtUtc)
                {
                    expiredAfterWait = true;
                }
                else
                {
                    session.LastObservedSequence = frame.Sequence;
                }
            }

            if (expiredAfterWait)
            {
                if (TryClaimPreviewSession(session, normalizedOwnerHash, requireExpired: true))
                {
                    await CompletePreviewSessionStopAsync(session, "expired");
                }

                throw CreatePreviewSessionNotFoundException();
            }

            return frame;
        }
    }

    public async Task<CameraPreviewHeartbeat?> HeartbeatPreviewSessionAsync(
        string sessionId,
        string ownerHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedSessionId = NormalizePreviewSessionId(sessionId);
        var normalizedOwnerHash = NormalizePreviewOwnerHash(ownerHash);
        if (!_previewSessions.TryGetValue(normalizedSessionId, out var session))
        {
            return null;
        }

        DateTimeOffset expiresAtUtc = default;
        var expired = false;
        lock (session.Gate)
        {
            if (session.IsClosing ||
                !string.Equals(session.OwnerHash, normalizedOwnerHash, StringComparison.Ordinal))
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            if (now >= session.ExpiresAtUtc)
            {
                expired = true;
            }
            else
            {
                expiresAtUtc = now.Add(_previewSessionTtl);
                session.ExpiresAtUtc = expiresAtUtc;
                session.ExpiryTimer!.Change(_previewSessionTtl, Timeout.InfiniteTimeSpan);
            }
        }

        if (expired)
        {
            if (TryClaimPreviewSession(session, normalizedOwnerHash, requireExpired: true))
            {
                await CompletePreviewSessionStopAsync(session, "expired");
            }

            return null;
        }

        return new CameraPreviewHeartbeat(session.SessionId, expiresAtUtc);
    }

    public async Task<bool> StopPreviewSessionAsync(string sessionId, string ownerHash)
    {
        var normalizedSessionId = NormalizePreviewSessionId(sessionId);
        var normalizedOwnerHash = NormalizePreviewOwnerHash(ownerHash);
        if (!_previewSessions.TryGetValue(normalizedSessionId, out var session) ||
            !TryClaimPreviewSession(session, normalizedOwnerHash, requireExpired: false))
        {
            return false;
        }

        await CompletePreviewSessionStopAsync(session, "stopped");
        return true;
    }

    private static string NormalizePreviewSessionId(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return sessionId.Trim();
    }

    private static string NormalizePreviewOwnerHash(string ownerHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerHash);
        return ownerHash.Trim();
    }

    private static KeyNotFoundException CreatePreviewSessionNotFoundException() =>
        new("Preview session was not found.");

    private void OnPreviewSessionExpiryTimer(PreviewSessionState session)
    {
        _ = ExpirePreviewSessionAsync(session);
    }

    private async Task ExpirePreviewSessionAsync(PreviewSessionState session)
    {
        try
        {
            if (!TryClaimPreviewSession(session, ownerHash: null, requireExpired: true))
            {
                return;
            }

            await CompletePreviewSessionStopAsync(session, "expired");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to expire abandoned camera preview session. CameraBindingId={CameraBindingId}",
                session.CameraBindingId);
        }
    }

    private bool TryClaimPreviewSession(
        PreviewSessionState session,
        string? ownerHash,
        bool requireExpired)
    {
        lock (session.Gate)
        {
            if (session.IsClosing ||
                (ownerHash != null &&
                 !string.Equals(session.OwnerHash, ownerHash, StringComparison.Ordinal)))
            {
                return false;
            }

            if (requireExpired)
            {
                var remaining = session.ExpiresAtUtc - _timeProvider.GetUtcNow();
                if (remaining > TimeSpan.Zero)
                {
                    session.ExpiryTimer!.Change(remaining, Timeout.InfiniteTimeSpan);
                    return false;
                }
            }

            var sessions = (ICollection<KeyValuePair<string, PreviewSessionState>>)_previewSessions;
            if (!sessions.Remove(new KeyValuePair<string, PreviewSessionState>(session.SessionId, session)))
            {
                session.IsClosing = true;
                return false;
            }

            session.IsClosing = true;
            return true;
        }
    }

    private async Task CompletePreviewSessionStopAsync(PreviewSessionState session, string reason)
    {
        session.ExpiryTimer?.Dispose();
        session.LifetimeCts.Cancel();

        try
        {
            if (!_producers.TryGetValue(session.CameraBindingId, out var entry))
            {
                return;
            }

            await entry.Gate.WaitAsync();
            try
            {
                if (entry.PreviewSessionCount > 0)
                {
                    entry.PreviewSessionCount--;
                }

                if (entry.LeaseCount == 0 && entry.PreviewSessionCount == 0)
                {
                    await StopProducerCoreAsync(entry);
                    RetireProducer(entry);
                }
            }
            finally
            {
                entry.Gate.Release();
            }
        }
        finally
        {
            session.LifetimeCts.Dispose();
        }

        _logger.LogInformation(
            "Camera preview session {Reason}. CameraBindingId={CameraBindingId}",
            reason,
            session.CameraBindingId);
    }

    public bool TryGetLatestFrameEnvelope(string cameraId, out FrameEnvelope? frame)
    {
        frame = null;
        if (!_producers.TryGetValue(ResolveProducerKey(cameraId), out var entry))
        {
            return false;
        }

        return entry.History.TryGetLatest(out frame);
    }

    public IReadOnlyList<FrameEnvelope> GetFrameEnvelopeWindow(string cameraId, long centerSequence, int before, int after)
    {
        if (!_producers.TryGetValue(ResolveProducerKey(cameraId), out var entry))
        {
            return Array.Empty<FrameEnvelope>();
        }

        return entry.History.SliceAround(centerSequence, before, after);
    }

    public RingBufferStats SnapshotFrameBufferStats(string cameraId)
    {
        if (!_producers.TryGetValue(ResolveProducerKey(cameraId), out var entry))
        {
            return new RingBufferStats(0, 0, 0, null, null);
        }

        return entry.History.SnapshotStats();
    }

    public CameraStreamUsageSnapshot SnapshotStreamUsage(string cameraId)
    {
        var producerKey = ResolveProducerKey(cameraId);
        if (!_producers.TryGetValue(producerKey, out var entry))
        {
            return new CameraStreamUsageSnapshot(
                producerKey,
                false,
                0,
                0,
                0,
                CameraTriggerMode.Software,
                CameraTriggerModeExtensions.DefaultTargetFrameRateFps);
        }

        return new CameraStreamUsageSnapshot(
            producerKey,
            entry.IsRunning,
            entry.LeaseCount,
            entry.PreviewSessionCount,
            Volatile.Read(ref entry.PendingFrameWaiters),
            entry.TriggerMode,
            entry.TargetFrameRateFps);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _previewSessions.Values.ToArray())
        {
            if (TryClaimPreviewSession(session, ownerHash: null, requireExpired: false))
            {
                await CompletePreviewSessionStopAsync(session, "disposed");
            }
        }

        foreach (var entry in _producers.Values.ToArray())
        {
            await entry.Gate.WaitAsync();
            try
            {
                await StopProducerCoreAsync(entry);
                RetireProducer(entry);
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        _producers.Clear();
        DisposeRetiredProducerGates();
    }

    private ResolvedBinding ResolveBinding(string cameraId)
    {
        var binding = _cameraManager.RequireEnabledBinding(cameraId);
        return new ResolvedBinding(
            binding.Id,
            binding.SerialNumber,
            binding.ExposureTimeUs,
            binding.GainDb,
            CameraPixelFormatExtensions.Normalize(binding.PixelFormat),
            CameraTriggerModeExtensions.Normalize(binding.TriggerMode),
            CameraHardwareTriggerSourceExtensions.Normalize(binding.HardwareTriggerSource),
            binding.UsesEnterPhotoelectricTrigger()
                ? binding.ToEnterPhotoelectricTriggerOptions()
                : null,
            binding.UsesSerialPhotoelectricTrigger()
                ? binding.ToSerialPhotoelectricTriggerOptions()
                : null,
            CameraTriggerModeExtensions.NormalizeTargetFrameRate(binding.TargetFrameRateFps),
            ResolveHistoryCapacity(binding));
    }

    private async Task<ProducerEntry> EnsureProducerAsync(ResolvedBinding binding, CancellationToken cancellationToken)
    {
        var configurationSignature = CreateConfigurationSignature(binding);
        while (true)
        {
            var entry = _producers.GetOrAdd(binding.CameraBindingId, id => new ProducerEntry(id));
            await entry.Gate.WaitAsync(cancellationToken);
            try
            {
                if (!IsCurrentProducer(entry))
                {
                    continue;
                }

                if (entry.IsRunning)
                {
                    if (string.Equals(entry.ConfigurationSignature, configurationSignature, StringComparison.Ordinal))
                    {
                        return entry;
                    }

                    if (entry.LeaseCount > 0 || entry.PreviewSessionCount > 0 || Volatile.Read(ref entry.PendingFrameWaiters) > 0)
                    {
                        throw new InvalidOperationException(
                            $"Camera stream '{binding.CameraBindingId}' is running with a different configuration. Stop preview/inspection before applying new camera settings.");
                    }

                    await StopProducerCoreAsync(entry);
                }

                await StartProducerCoreAsync(entry, binding, configurationSignature, cancellationToken);
                return entry;
            }
            finally
            {
                entry.Gate.Release();
            }
        }
    }

    private bool IsCurrentProducer(ProducerEntry entry) =>
        _producers.TryGetValue(entry.CameraBindingId, out var current) &&
        ReferenceEquals(current, entry);

    private async Task StartProducerCoreAsync(
        ProducerEntry entry,
        ResolvedBinding binding,
        string configurationSignature,
        CancellationToken cancellationToken)
    {
        var cameraLease = await _cameraManager.AcquireByBindingLeaseAsync(
            binding.CameraBindingId,
            cancellationToken);
        entry.CameraLease = cameraLease;
        ICamera? camera = null;

        try
        {
            camera = cameraLease.Camera;
            await ApplyCommonCameraSettingsAsync(camera, binding);
            if (camera is IIndustrialCamera industrialCamera)
            {
                await industrialCamera.SetTriggerModeAsync(binding.TriggerMode, binding.HardwareTriggerSource);
            }

            entry.IsRunning = false;
            entry.SerialNumber = binding.SerialNumber;
            entry.ConfigurationSignature = configurationSignature;
            entry.TriggerMode = binding.TriggerMode;
            entry.TargetFrameRateFps = binding.TargetFrameRateFps;
            entry.LatestFrame = null;
            entry.History = new FrameRingBuffer(binding.FrameBufferCapacity);
            entry.Sequence = 0;
            entry.LastPublishedTicks = 0;
            entry.MinFrameTicks = TimeSpan.TicksPerSecond / CameraTriggerModeExtensions.NormalizeTargetFrameRate(binding.TargetFrameRateFps);
            entry.PendingFrameWaiters = 0;
            lock (entry.SignalGate)
            {
                entry.NextFrameSignal = CreateFrameSignal();
            }

            if (camera is IIndustrialCamera eventCamera)
            {
                EventHandler<CameraFrameReceivedEventArgs> handler = (_, args) =>
                {
                    try
                    {
                        if (!TryEnterPublishWindow(entry))
                        {
                            return;
                        }

                        var contentType = args.ImageData.Length > 2 &&
                                          args.ImageData[0] == 0xFF &&
                                          args.ImageData[1] == 0xD8
                            ? "image/jpeg"
                            : "image/png";
                        var frame = CreateFrame(
                            binding.CameraBindingId,
                            args.ImageData,
                            contentType,
                            args.Width,
                            args.Height,
                            args.CameraTimestampNs.HasValue ? (long?)args.CameraTimestampNs.Value : null,
                            args.DeviceFrameCounter.HasValue ? (long?)args.DeviceFrameCounter.Value : null,
                            args.Stride);
                        PublishFrame(entry, frame);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish shared camera frame from metadata event. CameraBindingId={CameraBindingId}", binding.CameraBindingId);
                    }
                };

                eventCamera.FrameReceived += handler;
                entry.EventCamera = eventCamera;
                entry.FrameReceivedHandler = handler;
            }

            await camera.StartContinuousAcquisitionAsync(async imageData =>
            {
                try
                {
                    if (entry.EventCamera is CameraProviderAdapter)
                    {
                        return;
                    }

                    if (!TryEnterPublishWindow(entry))
                    {
                        return;
                    }

                    var frame = CreateFrame(binding.CameraBindingId, imageData, "image/jpeg");
                    PublishFrame(entry, frame);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish shared camera frame. CameraBindingId={CameraBindingId}", binding.CameraBindingId);
                }

                await Task.CompletedTask;
            });

            entry.IsRunning = true;
        }
        catch (Exception ex)
        {
            await RollBackFailedProducerStartAsync(entry, camera, ex);
            throw;
        }

        _logger.LogInformation(
            "Shared camera stream started. CameraBindingId={CameraBindingId}, TriggerMode={TriggerMode}, TargetFrameRateFps={TargetFrameRateFps}",
            binding.CameraBindingId,
            binding.TriggerMode,
            binding.TargetFrameRateFps);
    }

    private async Task RollBackFailedProducerStartAsync(ProducerEntry entry, ICamera? camera, Exception exception)
    {
        try
        {
            if (entry.EventCamera != null && entry.FrameReceivedHandler != null)
            {
                entry.EventCamera.FrameReceived -= entry.FrameReceivedHandler;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to detach camera frame event handler after start failure. CameraBindingId={CameraBindingId}", entry.CameraBindingId);
        }

        try
        {
            if (camera != null)
            {
                await camera.StopContinuousAcquisitionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop camera acquisition after start failure. CameraBindingId={CameraBindingId}", entry.CameraBindingId);
        }

        entry.EventCamera = null;
        entry.FrameReceivedHandler = null;
        CancelAndDisposeIdleStop(entry.IdleStopCts);
        entry.IdleStopCts = null;
        entry.IsRunning = false;
        entry.SerialNumber = string.Empty;
        entry.ConfigurationSignature = string.Empty;
        entry.LatestFrame = null;
        entry.History = new FrameRingBuffer(24);
        entry.Sequence = 0;
        entry.LastPublishedTicks = 0;
        entry.MinFrameTicks = TimeSpan.TicksPerSecond / CameraTriggerModeExtensions.DefaultTargetFrameRateFps;
        entry.ActiveStreamLeaseIds.Clear();
        entry.LeaseCount = 0;
        CompleteFrameWaiters(entry, new InvalidOperationException($"Camera stream '{entry.CameraBindingId}' failed to start.", exception));
        await ReleaseProducerCameraLeaseAsync(entry);

        _logger.LogWarning(exception, "Failed to start shared camera stream. CameraBindingId={CameraBindingId}", entry.CameraBindingId);
    }

    private async Task StopProducerCoreAsync(ProducerEntry entry, Exception? completionException = null)
    {
        if (!entry.IsRunning)
        {
            await ReleaseProducerCameraLeaseAsync(entry);
            CompleteFrameWaiters(
                entry,
                completionException ?? new OperationCanceledException($"Camera stream '{entry.CameraBindingId}' is not running."));
            return;
        }

        try
        {
            if (entry.EventCamera != null && entry.FrameReceivedHandler != null)
            {
                entry.EventCamera.FrameReceived -= entry.FrameReceivedHandler;
                entry.EventCamera = null;
                entry.FrameReceivedHandler = null;
            }

            var camera = entry.CameraLease?.Camera;
            if (camera != null)
            {
                await camera.StopContinuousAcquisitionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop shared camera stream cleanly. CameraBindingId={CameraBindingId}", entry.CameraBindingId);
        }
        finally
        {
            CancelAndDisposeIdleStop(entry.IdleStopCts);
            entry.IdleStopCts = null;
            entry.IsRunning = false;
            entry.SerialNumber = string.Empty;
            entry.ConfigurationSignature = string.Empty;
            entry.LatestFrame = null;
            entry.History = new FrameRingBuffer(24);
            entry.Sequence = 0;
            entry.LastPublishedTicks = 0;
            entry.MinFrameTicks = TimeSpan.TicksPerSecond / CameraTriggerModeExtensions.DefaultTargetFrameRateFps;
            entry.ActiveStreamLeaseIds.Clear();
            entry.LeaseCount = 0;
            await ReleaseProducerCameraLeaseAsync(entry);
            CompleteFrameWaiters(
                entry,
                completionException ?? new OperationCanceledException($"Camera stream '{entry.CameraBindingId}' has stopped."));
        }
    }

    private async ValueTask ReleaseProducerCameraLeaseAsync(ProducerEntry entry)
    {
        var cameraLease = entry.CameraLease;
        entry.CameraLease = null;
        if (cameraLease == null)
        {
            return;
        }

        try
        {
            await cameraLease.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to release shared camera lease. CameraBindingId={CameraBindingId}",
                entry.CameraBindingId);
        }
    }

    private async Task ArmDirectAcquireIdleStopAsync(ProducerEntry entry)
    {
        var idleCts = new CancellationTokenSource();
        CancellationTokenSource? previousCts = null;
        var shouldArm = false;

        try
        {
            await entry.Gate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            idleCts.Dispose();
            return;
        }

        try
        {
            previousCts = entry.IdleStopCts;
            entry.IdleStopCts = null;
            if (entry.IsRunning && entry.LeaseCount == 0 && entry.PreviewSessionCount == 0)
            {
                entry.IdleStopCts = idleCts;
                shouldArm = true;
            }
        }
        finally
        {
            entry.Gate.Release();
        }

        CancelAndDisposeIdleStop(previousCts);
        if (!shouldArm)
        {
            idleCts.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_directAcquireIdleTimeout, idleCts.Token);
                await entry.Gate.WaitAsync(idleCts.Token);
                try
                {
                    if (idleCts.IsCancellationRequested || !ReferenceEquals(entry.IdleStopCts, idleCts))
                    {
                        return;
                    }

                    entry.IdleStopCts = null;
                    if (entry.LeaseCount == 0 && entry.PreviewSessionCount == 0)
                    {
                        await StopProducerCoreAsync(entry);
                        RetireProducer(entry);
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                idleCts.Dispose();
            }
        });
    }

    private static void CancelAndDisposeIdleStop(CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private void RetireProducer(ProducerEntry entry)
    {
        var producers = (ICollection<KeyValuePair<string, ProducerEntry>>)_producers;
        if (producers.Remove(new KeyValuePair<string, ProducerEntry>(entry.CameraBindingId, entry)))
        {
            _retiredProducers.Enqueue(entry);
        }
    }

    private void DisposeRetiredProducerGates()
    {
        while (_retiredProducers.TryDequeue(out var entry))
        {
            entry.Gate.Dispose();
        }
    }

    private async Task<CameraStreamFrame> AcquireSoftwareFrameAsync(ResolvedBinding binding, CancellationToken cancellationToken)
    {
        await using var cameraLease = await _cameraManager.AcquireByBindingLeaseAsync(
            binding.CameraBindingId,
            cancellationToken);
        var camera = cameraLease.Camera;
        await ApplyCommonCameraSettingsAsync(camera, binding);
        if (camera is IIndustrialCamera industrialCamera)
        {
            await industrialCamera.SetTriggerModeAsync(CameraTriggerMode.Software);
        }

        if (binding.EnterPhotoelectricTrigger != null)
        {
            await _triggerInputService.WaitForEnterPhotoelectricAsync(
                binding.EnterPhotoelectricTrigger,
                cancellationToken);
        }
        else if (binding.SerialPhotoelectricTrigger != null)
        {
            await _serialPhotoelectricTriggerInputService.WaitForSerialPhotoelectricAsync(
                binding.SerialPhotoelectricTrigger,
                cancellationToken);
        }

        var imageData = await camera.AcquireSingleFrameAsync();
        return CreateFrame(binding.CameraBindingId, imageData, "image/png");
    }

    private static async Task ApplyCommonCameraSettingsAsync(ICamera camera, ResolvedBinding binding)
    {
        await camera.SetExposureTimeAsync(binding.ExposureTimeUs);
        await camera.SetGainAsync(binding.GainDb);
        if (camera is IIndustrialCamera industrialCamera)
        {
            await industrialCamera.SetPixelFormatAsync(binding.PixelFormat);
        }
    }

    private async Task<CameraStreamFrame> WaitForFrameCoreAsync(
        ProducerEntry entry,
        long? afterSequence,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var latestFrame = entry.LatestFrame;
            if (latestFrame != null && (!afterSequence.HasValue || latestFrame.Sequence > afterSequence.Value))
            {
                return latestFrame;
            }

            if (!entry.IsRunning)
            {
                throw new OperationCanceledException($"Camera stream '{entry.CameraBindingId}' is not running.", cancellationToken);
            }

            Interlocked.Increment(ref entry.PendingFrameWaiters);
            try
            {
                latestFrame = entry.LatestFrame;
                if (latestFrame != null && (!afterSequence.HasValue || latestFrame.Sequence > afterSequence.Value))
                {
                    return latestFrame;
                }

                if (!entry.IsRunning)
                {
                    throw new OperationCanceledException($"Camera stream '{entry.CameraBindingId}' is not running.", cancellationToken);
                }

                TaskCompletionSource<long> signal;
                lock (entry.SignalGate)
                {
                    signal = entry.NextFrameSignal;
                }

                try
                {
                    await signal.Task.WaitAsync(_frameHealthProbeInterval, cancellationToken);
                }
                catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (IsProducerCameraAcquiring(entry))
                    {
                        continue;
                    }

                    var message = $"Camera stream '{entry.CameraBindingId}' stopped producing frames because the camera acquisition loop is no longer running.";
                    await MarkProducerFaultedAsync(entry, message);
                    throw new InvalidOperationException(message);
                }
            }
            finally
            {
                Interlocked.Decrement(ref entry.PendingFrameWaiters);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private bool IsProducerCameraAcquiring(ProducerEntry entry)
    {
        if (!entry.IsRunning)
        {
            return false;
        }

        if (entry.EventCamera != null)
        {
            return entry.EventCamera.IsAcquiring;
        }

        return entry.CameraLease?.Camera.IsAcquiring == true;
    }

    private async Task MarkProducerFaultedAsync(ProducerEntry entry, string reason)
    {
        await entry.Gate.WaitAsync();
        try
        {
            if (!entry.IsRunning)
            {
                return;
            }

            _logger.LogWarning(
                "Shared camera stream faulted. CameraBindingId={CameraBindingId}, Reason={Reason}",
                entry.CameraBindingId,
                reason);

            await StopProducerCoreAsync(entry, new InvalidOperationException(reason));
            RetireProducer(entry);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private static void PublishFrame(ProducerEntry entry, CameraStreamFrame frame)
    {
        var nextSequence = Interlocked.Increment(ref entry.Sequence);
        var publishedFrame = frame with
        {
            Sequence = nextSequence,
            TimestampUtc = DateTime.UtcNow
        };

        entry.LatestFrame = publishedFrame;
        entry.History.Push(ToEnvelope(publishedFrame));
        if (Volatile.Read(ref entry.PendingFrameWaiters) > 0)
        {
            TaskCompletionSource<long> completedSignal;
            lock (entry.SignalGate)
            {
                completedSignal = entry.NextFrameSignal;
                entry.NextFrameSignal = CreateFrameSignal();
            }

            completedSignal.TrySetResult(nextSequence);
        }
    }

    private static bool TryEnterPublishWindow(ProducerEntry entry)
    {
        var minFrameTicks = entry.MinFrameTicks;
        while (true)
        {
            var previousTicks = Interlocked.Read(ref entry.LastPublishedTicks);
            var nowTicks = DateTime.UtcNow.Ticks;
            if (previousTicks != 0 && nowTicks - previousTicks < minFrameTicks)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref entry.LastPublishedTicks, nowTicks, previousTicks) == previousTicks)
            {
                return true;
            }
        }
    }

    private static CameraStreamFrame CreateFrame(
        string cameraBindingId,
        byte[] imageData,
        string contentType,
        int width = 0,
        int height = 0,
        long? cameraTimestampNs = null,
        long? deviceFrameCounter = null,
        int? stride = null)
    {
        if (width <= 0 || height <= 0)
        {
            using var decoded = Cv2.ImDecode(imageData, ImreadModes.Unchanged);
            if (decoded.Empty())
            {
                throw new InvalidOperationException("Unable to decode camera frame.");
            }

            width = decoded.Width;
            height = decoded.Height;
        }

        return new CameraStreamFrame(
            cameraBindingId,
            imageData,
            contentType,
            width,
            height,
            0,
            DateTime.UtcNow,
            cameraTimestampNs,
            deviceFrameCounter,
            stride);
    }

    private static FrameEnvelope ToEnvelope(CameraStreamFrame frame)
    {
        return new FrameEnvelope(
            frame.CameraBindingId,
            frame.Sequence,
            new DateTimeOffset(DateTime.SpecifyKind(frame.TimestampUtc, DateTimeKind.Utc)),
            frame.Width,
            frame.Height,
            frame.ContentType,
            FramePayloadKind.EncodedImage,
            frame.ImageData,
            frame.CameraTimestampNs,
            frame.DeviceFrameCounter,
            frame.Stride,
            frame.CameraTimestampNs.HasValue || frame.DeviceFrameCounter.HasValue
                ? FrameTimestampSource.CameraPreferred
                : FrameTimestampSource.HostFallback,
            $"{frame.CameraBindingId}:{frame.Sequence}");
    }

    private static int ResolveHistoryCapacity(CameraBindingConfig binding)
    {
        binding.ContinuousInspection ??= new ClearVision.Product.Core.Continuous.ContinuousInspectionConfig();
        binding.ContinuousInspection.Normalize();
        return Math.Max(
            1,
            Math.Max(
                binding.ContinuousInspection.BufferCapacity,
                binding.ContinuousInspection.PreEventFrames + binding.ContinuousInspection.PostEventFrames + 1));
    }

    private string ResolveProducerKey(string cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return string.Empty;
        }

        return _cameraManager.FindBinding(cameraId)?.Id ?? cameraId.Trim();
    }

    private static string CreateConfigurationSignature(ResolvedBinding binding)
    {
        return string.Join(
            "|",
            binding.CameraBindingId,
            binding.SerialNumber,
            binding.ExposureTimeUs.ToString("R", CultureInfo.InvariantCulture),
            binding.GainDb.ToString("R", CultureInfo.InvariantCulture),
            binding.PixelFormat.ToConfigValue(),
            binding.TriggerMode.ToConfigValue(),
            binding.HardwareTriggerSource,
            binding.TargetFrameRateFps.ToString(CultureInfo.InvariantCulture),
            binding.FrameBufferCapacity.ToString(CultureInfo.InvariantCulture));
    }

    private static void CompleteFrameWaiters(ProducerEntry entry, Exception exception)
    {
        TaskCompletionSource<long> completedSignal;
        lock (entry.SignalGate)
        {
            completedSignal = entry.NextFrameSignal;
            entry.NextFrameSignal = CreateFrameSignal();
        }

        completedSignal.TrySetException(exception);
    }

    private static TaskCompletionSource<long> CreateFrameSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
