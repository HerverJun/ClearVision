namespace Acme.Product.Infrastructure.Diagnostics;

public sealed class ContinuousMetricsCollector
{
    private long _framesReceived;
    private long _arrivalSignals;
    private long _tracksCreated;
    private long _scheduledInferences;
    private long _droppedInferences;
    private long _completedInferences;
    private long _finalDecisions;
    private long _totalLatencyMs;

    public void RecordFrameReceived() => Interlocked.Increment(ref _framesReceived);
    public void RecordArrivalSignal() => Interlocked.Increment(ref _arrivalSignals);
    public void RecordTrackCreated() => Interlocked.Increment(ref _tracksCreated);
    public void RecordInferenceScheduled() => Interlocked.Increment(ref _scheduledInferences);
    public void RecordInferenceDropped() => Interlocked.Increment(ref _droppedInferences);
    public void RecordDecisionFinalized() => Interlocked.Increment(ref _finalDecisions);

    public void RecordInferenceCompleted(TimeSpan latency)
    {
        Interlocked.Increment(ref _completedInferences);
        Interlocked.Add(ref _totalLatencyMs, Math.Max(0, (long)latency.TotalMilliseconds));
    }

    public ContinuousMetricsSnapshot Snapshot()
    {
        var completed = Interlocked.Read(ref _completedInferences);
        var totalLatency = Interlocked.Read(ref _totalLatencyMs);
        return new ContinuousMetricsSnapshot(
            Interlocked.Read(ref _framesReceived),
            Interlocked.Read(ref _arrivalSignals),
            Interlocked.Read(ref _tracksCreated),
            Interlocked.Read(ref _scheduledInferences),
            Interlocked.Read(ref _droppedInferences),
            completed,
            Interlocked.Read(ref _finalDecisions),
            completed == 0 ? 0 : totalLatency / (double)completed);
    }
}

public sealed record ContinuousMetricsSnapshot(
    long FramesReceived,
    long ArrivalSignals,
    long TracksCreated,
    long ScheduledInferences,
    long DroppedInferences,
    long CompletedInferences,
    long FinalDecisions,
    double AverageInferenceLatencyMs);
