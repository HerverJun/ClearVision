using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Infrastructure.Continuous;

public sealed record TrackFrameJudgment(
    string TrackId,
    long Sequence,
    Dictionary<string, object>? OutputData,
    double Confidence = 1.0,
    InspectionOutcome? Outcome = null);

public sealed record TrackDecision(
    string TrackId,
    InspectionStatus Status,
    int FrameCount,
    int OkVotes,
    int NgVotes,
    long BestSequence,
    double ConsensusScore,
    bool IsFinal,
    InspectionOutcome? Outcome = null);

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
                .Select(item => (Item: item, Outcome: item.Outcome ?? FromLegacyHeuristic(item.OutputData)))
                .ToList();
            var comparable = evaluated
                .Where(item => item.Outcome.Execution == ExecutionOutcome.Succeeded &&
                               item.Outcome.Decision is DecisionOutcome.Ok or DecisionOutcome.Ng)
                .ToList();
            var okVotes = comparable.Count(item => item.Outcome.Decision == DecisionOutcome.Ok);
            var ngVotes = comparable.Count(item => item.Outcome.Decision == DecisionOutcome.Ng);
            var totalVotes = okVotes + ngVotes;
            if (evaluated.Any(item => item.Outcome.Decision == DecisionOutcome.Invalid))
            {
                return FinalizeNonComparable(judgment.TrackId, list, DecisionOutcome.Invalid);
            }
            if (totalVotes == 0)
            {
                var terminalDecision = evaluated.Any(item => item.Outcome.Decision == DecisionOutcome.Invalid)
                    ? DecisionOutcome.Invalid
                    : evaluated.All(item => item.Outcome.Decision == DecisionOutcome.NotApplicable)
                        ? DecisionOutcome.NotApplicable
                        : DecisionOutcome.Undetermined;
                return FinalizeNonComparable(judgment.TrackId, list, terminalDecision);
            }
            var majorityStatus = ngVotes > okVotes ? InspectionStatus.NG : InspectionStatus.OK;
            var majorityVotes = majorityStatus == InspectionStatus.NG ? ngVotes : okVotes;
            var score = majorityVotes / (double)totalVotes;

            if (score < _consensusThreshold)
            {
                return null;
            }

            var best = list
                .OrderByDescending(item => item.Confidence)
                .ThenBy(item => item.Sequence)
                .First();
            var decision = new TrackDecision(
                judgment.TrackId,
                majorityStatus,
                list.Count,
                okVotes,
                ngVotes,
                best.Sequence,
                score,
                IsFinal: true,
                new InspectionOutcome(
                    ExecutionOutcome.Succeeded,
                    majorityStatus == InspectionStatus.OK ? DecisionOutcome.Ok : DecisionOutcome.Ng,
                    "ContinuousConsensus",
                    "CONTINUOUS_CONSENSUS_REACHED",
                    null,
                    HasJudgmentSignal: true));

            AddFinalizedTrack(judgment.TrackId);
            _frames.Remove(judgment.TrackId);
            _pendingTrackTouches.Remove(judgment.TrackId);
            return decision;
        }
    }

    private TrackDecision FinalizeNonComparable(
        string trackId,
        IReadOnlyList<TrackFrameJudgment> frames,
        DecisionOutcome decisionOutcome)
    {
        var best = frames
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.Sequence)
            .First();
        var outcome = new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            decisionOutcome,
            "ContinuousConsensus",
            decisionOutcome switch
            {
                DecisionOutcome.Invalid => "CONTINUOUS_CONSENSUS_INVALID",
                DecisionOutcome.NotApplicable => "CONTINUOUS_CONSENSUS_NOT_APPLICABLE",
                _ => "CONTINUOUS_CONSENSUS_NO_VALID_VOTES"
            },
            "Continuous consensus completed without comparable OK/NG votes.",
            HasJudgmentSignal: decisionOutcome == DecisionOutcome.Invalid &&
                               frames.Any(frame => frame.Outcome?.HasJudgmentSignal == true));
        var decision = new TrackDecision(
            trackId,
            LegacyInspectionStatusProjection.Project(outcome),
            frames.Count,
            0,
            0,
            best.Sequence,
            0,
            IsFinal: true,
            outcome);
        AddFinalizedTrack(trackId);
        _frames.Remove(trackId);
        _pendingTrackTouches.Remove(trackId);
        return decision;
    }

    private static InspectionOutcome FromLegacyHeuristic(Dictionary<string, object>? outputData)
    {
        var evaluation = InspectionJudgmentResolver.DetermineDecisionFromLegacyHeuristic(outputData);
        return new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            evaluation.Decision,
            evaluation.DecisionSource,
            evaluation.ReasonCode,
            evaluation.Message,
            evaluation.HasJudgmentSignal);
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
}
