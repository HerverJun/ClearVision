using Acme.Product.Core.Enums;
using Acme.Product.Core.Services;

namespace Acme.Product.Infrastructure.Continuous;

public sealed record TrackFrameJudgment(
    string TrackId,
    long Sequence,
    Dictionary<string, object>? OutputData,
    double Confidence = 1.0);

public sealed record TrackDecision(
    string TrackId,
    InspectionStatus Status,
    int FrameCount,
    int OkVotes,
    int NgVotes,
    long BestSequence,
    double ConsensusScore,
    bool IsFinal);

public sealed class TrackConsensusJudge
{
    private readonly int _minConsensusFrames;
    private readonly double _consensusThreshold;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<TrackFrameJudgment>> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _finalizedTracks = new(StringComparer.OrdinalIgnoreCase);

    public TrackConsensusJudge(int minConsensusFrames = 3, double consensusThreshold = 0.6)
    {
        _minConsensusFrames = Math.Max(1, minConsensusFrames);
        _consensusThreshold = Math.Clamp(consensusThreshold, 0.0, 1.0);
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
            }

            list.Add(judgment);
            if (list.Count < _minConsensusFrames)
            {
                return null;
            }

            var evaluated = list
                .Select(item => (Item: item, Evaluation: InspectionJudgmentResolver.DetermineStatusFromFlowOutput(item.OutputData)))
                .ToList();
            var okVotes = evaluated.Count(item => item.Evaluation.Status == InspectionStatus.OK);
            var ngVotes = evaluated.Count(item => item.Evaluation.Status == InspectionStatus.NG);
            var totalVotes = Math.Max(1, okVotes + ngVotes);
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
                IsFinal: true);

            _finalizedTracks.Add(judgment.TrackId);
            _frames.Remove(judgment.TrackId);
            return decision;
        }
    }
}
