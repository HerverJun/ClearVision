namespace Acme.Product.Infrastructure.Continuous;

public sealed record LightweightTrackerOptions(
    int MaxSequenceGap = 5,
    int TrackTimeoutMs = 1000,
    int FreezeAfterSignals = 1);

public sealed record LightweightTrack(
    string TrackId,
    string CameraId,
    long FirstSequence,
    long LastSequence,
    DateTimeOffset StartedUtc,
    DateTimeOffset UpdatedUtc,
    int SignalCount,
    bool IsNew,
    bool IsFrozen);

public interface ILightweightTracker
{
    LightweightTrack Update(ArrivalSignal signal);
    IReadOnlyList<LightweightTrack> SnapshotActiveTracks(DateTimeOffset now);
}

public sealed class LightweightTracker : ILightweightTracker
{
    private readonly LightweightTrackerOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, MutableTrack> _activeTracks = new(StringComparer.OrdinalIgnoreCase);

    public LightweightTracker(LightweightTrackerOptions? options = null)
    {
        _options = options ?? new LightweightTrackerOptions();
    }

    public LightweightTrack Update(ArrivalSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        lock (_gate)
        {
            PruneExpired(signal.EventTimeUtc);
            if (_activeTracks.TryGetValue(signal.CameraId, out var existing) &&
                !existing.IsFrozen &&
                signal.Sequence - existing.LastSequence <= _options.MaxSequenceGap)
            {
                existing.LastSequence = signal.Sequence;
                existing.UpdatedUtc = signal.EventTimeUtc;
                existing.SignalCount++;
                existing.IsFrozen = existing.SignalCount >= _options.FreezeAfterSignals;
                return existing.ToImmutable(isNew: false);
            }

            var track = new MutableTrack
            {
                TrackId = $"{signal.CameraId}:{signal.Sequence}",
                CameraId = signal.CameraId,
                FirstSequence = signal.Sequence,
                LastSequence = signal.Sequence,
                StartedUtc = signal.EventTimeUtc,
                UpdatedUtc = signal.EventTimeUtc,
                SignalCount = 1,
                IsFrozen = _options.FreezeAfterSignals <= 1
            };
            _activeTracks[signal.CameraId] = track;
            return track.ToImmutable(isNew: true);
        }
    }

    public IReadOnlyList<LightweightTrack> SnapshotActiveTracks(DateTimeOffset now)
    {
        lock (_gate)
        {
            PruneExpired(now);
            return _activeTracks.Values
                .Select(track => track.ToImmutable(isNew: false))
                .ToList();
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var expired = _activeTracks
            .Where(item => (now - item.Value.UpdatedUtc).TotalMilliseconds > _options.TrackTimeoutMs)
            .Select(item => item.Key)
            .ToList();

        foreach (var key in expired)
        {
            _activeTracks.Remove(key);
        }
    }

    private sealed class MutableTrack
    {
        public required string TrackId { get; init; }
        public required string CameraId { get; init; }
        public long FirstSequence { get; init; }
        public long LastSequence { get; set; }
        public DateTimeOffset StartedUtc { get; init; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public int SignalCount { get; set; }
        public bool IsFrozen { get; set; }

        public LightweightTrack ToImmutable(bool isNew) =>
            new(TrackId, CameraId, FirstSequence, LastSequence, StartedUtc, UpdatedUtc, SignalCount, isNew, IsFrozen);
    }
}
