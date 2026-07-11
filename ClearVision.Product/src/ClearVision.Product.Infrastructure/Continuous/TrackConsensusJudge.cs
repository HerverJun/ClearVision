using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Infrastructure.Continuous;

public sealed record TrackFrameJudgment(
    string TrackId,
    long Sequence,
    Dictionary<string, object> OutputData,
    double Confidence,
    InspectionOutcome Outcome,
    string? CorrelationId = null,
    byte[]? OutputImage = null,
    TimeSpan Latency = default);

public sealed record TrackDecision(
    string TrackId,
    InspectionStatus Status,
    int FrameCount,
    int OkVotes,
    int NgVotes,
    long BestSequence,
    double ConsensusScore,
    bool IsFinal,
    InspectionOutcome? Outcome = null,
    TrackFrameJudgment? RepresentativeFrame = null,
    TrackFrameJudgment? TerminalFrame = null)
{
    public TrackFrameJudgment? ResultFrame => RepresentativeFrame ?? TerminalFrame;
}

public sealed record TrackConsensusSnapshot(
    int PendingTrackCount,
    int PendingFrameCount,
    int FinalizedTrackCount,
    int MaxPendingTracks,
    int MaxFramesPerTrack,
    int MaxFinalizedTracks);

public sealed class TrackConsensusJudge
{
    private const int DefaultMaxPendingTracks = 2048;
    private const int DefaultMaxFramesPerTrack = 128;
    private const int DefaultMaxFinalizedTracks = 4096;

    private readonly int _minConsensusFrames;
    private readonly double _consensusThreshold;
    private readonly int _maxPendingTracks;
    private readonly int _maxFramesPerTrack;
    private readonly int _maxFinalizedTracks;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<TrackFrameJudgment>> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingTrackTouches = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _finalizedTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _finalizedTrackOrder = new();
    private long _touchCounter;

    public TrackConsensusJudge(
        int minConsensusFrames = 3,
        double consensusThreshold = 0.6,
        int maxPendingTracks = DefaultMaxPendingTracks,
        int maxFramesPerTrack = DefaultMaxFramesPerTrack,
        int maxFinalizedTracks = DefaultMaxFinalizedTracks)
    {
        _minConsensusFrames = Math.Max(1, minConsensusFrames);
        _consensusThreshold = Math.Clamp(consensusThreshold, 0.0, 1.0);
        _maxPendingTracks = Math.Max(1, maxPendingTracks);
        _maxFramesPerTrack = Math.Max(_minConsensusFrames, maxFramesPerTrack);
        _maxFinalizedTracks = Math.Max(0, maxFinalizedTracks);
    }

    public TrackDecision? AddFrame(TrackFrameJudgment judgment)
    {
        ArgumentNullException.ThrowIfNull(judgment);
        if (string.IsNullOrWhiteSpace(judgment.TrackId))
        {
            throw new ArgumentException("TrackId is required.", nameof(judgment));
        }

        lock (_gate)
        {
            if (_finalizedTracks.Contains(judgment.TrackId))
            {
                return null;
            }

            if (!_frames.TryGetValue(judgment.TrackId, out var list))
            {
                list = new List<TrackFrameJudgment>();
                _frames[judgment.TrackId] = list;
                PrunePendingTracksIfNeeded();
            }

            list.Add(judgment);
            TouchPendingTrack(judgment.TrackId);
            PruneTrackFrames(list);
            if (list.Count < _minConsensusFrames)
            {
                return null;
            }

            var evaluated = list
                .Select(item => (Item: item, Outcome: item.Outcome))
                .ToList();
            var comparable = evaluated
                .Where(item => item.Outcome.Execution == ExecutionOutcome.Succeeded &&
                               item.Outcome.Decision is DecisionOutcome.Ok or DecisionOutcome.Ng)
                .ToList();
            var okVotes = comparable.Count(item => item.Outcome.Decision == DecisionOutcome.Ok);
            var ngVotes = comparable.Count(item => item.Outcome.Decision == DecisionOutcome.Ng);
            var totalVotes = okVotes + ngVotes;
            if (totalVotes >= _minConsensusFrames)
            {
                var majorityStatus = ngVotes > okVotes ? InspectionStatus.NG : InspectionStatus.OK;
                var majorityVotes = majorityStatus == InspectionStatus.NG ? ngVotes : okVotes;
                var score = majorityVotes / (double)totalVotes;

                if (score >= _consensusThreshold)
                {
                    return FinalizeComparable(
                        judgment.TrackId,
                        list,
                        comparable,
                        majorityStatus,
                        okVotes,
                        ngVotes,
                        score);
                }
            }

            // A window containing only non-comparable outcomes cannot gain a product
            // conclusion without a new valid vote, so the configured minimum window is
            // enough to produce its controlled terminal outcome. Mixed windows keep
            // collecting until their bounded consensus window is exhausted.
            if (totalVotes == 0 && list.Count >= _minConsensusFrames ||
                list.Count >= _maxFramesPerTrack)
            {
                return FinalizeNonComparable(
                    judgment.TrackId,
                    list,
                    comparable,
                    DetermineTerminalDecision(evaluated));
            }

            return null;
        }
    }

    private TrackDecision FinalizeComparable(
        string trackId,
        IReadOnlyList<TrackFrameJudgment> frames,
        IReadOnlyList<(TrackFrameJudgment Item, InspectionOutcome Outcome)> comparable,
        InspectionStatus majorityStatus,
        int okVotes,
        int ngVotes,
        double score)
    {
        var majorityDecision = majorityStatus == InspectionStatus.OK
            ? DecisionOutcome.Ok
            : DecisionOutcome.Ng;
        var representative = comparable
            .Where(item => item.Outcome.Decision == majorityDecision)
            .Select(item => item.Item)
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.Sequence)
            .First();
        var decision = new TrackDecision(
            trackId,
            majorityStatus,
            frames.Count,
            okVotes,
            ngVotes,
            representative.Sequence,
            score,
            IsFinal: true,
            new InspectionOutcome(
                ExecutionOutcome.Succeeded,
                majorityDecision,
                "ContinuousConsensus",
                "CONTINUOUS_CONSENSUS_REACHED",
                null,
                HasJudgmentSignal: true),
            RepresentativeFrame: representative);
        FinalizeTrack(trackId);
        return decision;
    }

    private TrackDecision FinalizeNonComparable(
        string trackId,
        IReadOnlyList<TrackFrameJudgment> frames,
        IReadOnlyList<(TrackFrameJudgment Item, InspectionOutcome Outcome)> comparable,
        DecisionOutcome decisionOutcome)
    {
        var okVotes = comparable.Count(item => item.Outcome.Decision == DecisionOutcome.Ok);
        var ngVotes = comparable.Count(item => item.Outcome.Decision == DecisionOutcome.Ng);
        var totalVotes = okVotes + ngVotes;
        var evidenceFrame = (totalVotes > 0
                ? comparable.Select(item => item.Item)
                : frames.Where(frame => frame.Outcome.Decision == decisionOutcome))
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.Sequence)
            .FirstOrDefault();
        var consensusScore = totalVotes > 0
            ? Math.Max(okVotes, ngVotes) / (double)totalVotes
            : 0;
        var isConflict = totalVotes > 0 && decisionOutcome == DecisionOutcome.Undetermined;
        var outcome = new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            decisionOutcome,
            "ContinuousConsensus",
            decisionOutcome switch
            {
                DecisionOutcome.Invalid => "CONTINUOUS_CONSENSUS_INVALID",
                DecisionOutcome.NotApplicable => "CONTINUOUS_CONSENSUS_NOT_APPLICABLE",
                DecisionOutcome.Undetermined when isConflict => "CONTINUOUS_CONSENSUS_CONFLICT",
                _ => "CONTINUOUS_CONSENSUS_NO_VALID_VOTES"
            },
            isConflict
                ? "Continuous consensus window exhausted with conflicting OK/NG votes."
                : "Continuous consensus completed without comparable OK/NG votes.",
            HasJudgmentSignal: totalVotes > 0 ||
                               decisionOutcome == DecisionOutcome.Invalid &&
                               frames.Any(frame => frame.Outcome.HasJudgmentSignal));
        var decision = new TrackDecision(
            trackId,
            LegacyInspectionStatusProjection.Project(outcome),
            frames.Count,
            okVotes,
            ngVotes,
            evidenceFrame?.Sequence ?? 0,
            consensusScore,
            IsFinal: true,
            outcome,
            RepresentativeFrame: isConflict ? evidenceFrame : null,
            TerminalFrame: isConflict ? null : evidenceFrame);
        FinalizeTrack(trackId);
        return decision;
    }

    private static DecisionOutcome DetermineTerminalDecision(
        IReadOnlyList<(TrackFrameJudgment Item, InspectionOutcome Outcome)> evaluated)
    {
        if (evaluated.Any(item => item.Outcome.Decision == DecisionOutcome.Invalid))
        {
            return DecisionOutcome.Invalid;
        }

        return evaluated.All(item => item.Outcome.Decision == DecisionOutcome.NotApplicable)
            ? DecisionOutcome.NotApplicable
            : DecisionOutcome.Undetermined;
    }

    public TrackConsensusSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new TrackConsensusSnapshot(
                _frames.Count,
                _frames.Values.Sum(list => list.Count),
                _finalizedTracks.Count,
                _maxPendingTracks,
                _maxFramesPerTrack,
                _maxFinalizedTracks);
        }
    }

    private void TouchPendingTrack(string trackId)
    {
        _pendingTrackTouches[trackId] = ++_touchCounter;
    }

    private void PruneTrackFrames(List<TrackFrameJudgment> frames)
    {
        if (frames.Count <= _maxFramesPerTrack)
        {
            return;
        }

        frames.RemoveRange(0, frames.Count - _maxFramesPerTrack);
    }

    private void PrunePendingTracksIfNeeded()
    {
        while (_frames.Count > _maxPendingTracks)
        {
            var oldestTrackId = _pendingTrackTouches.Count == 0
                ? _frames.Keys.FirstOrDefault()
                : _pendingTrackTouches
                    .OrderBy(pair => pair.Value)
                    .Select(pair => pair.Key)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(oldestTrackId))
            {
                return;
            }

            _frames.Remove(oldestTrackId);
            _pendingTrackTouches.Remove(oldestTrackId);
        }
    }

    private void AddFinalizedTrack(string trackId)
    {
        if (_maxFinalizedTracks == 0)
        {
            return;
        }

        if (_finalizedTracks.Add(trackId))
        {
            _finalizedTrackOrder.Enqueue(trackId);
        }

        while (_finalizedTracks.Count > _maxFinalizedTracks && _finalizedTrackOrder.TryDequeue(out var oldestTrackId))
        {
            _finalizedTracks.Remove(oldestTrackId);
        }
    }

    private void FinalizeTrack(string trackId)
    {
        AddFinalizedTrack(trackId);
        _frames.Remove(trackId);
        _pendingTrackTouches.Remove(trackId);
    }
}
