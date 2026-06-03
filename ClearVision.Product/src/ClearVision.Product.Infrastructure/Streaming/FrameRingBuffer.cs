using ClearVision.Product.Core.Streaming;
using CoreRingBufferStats = ClearVision.Product.Core.Cameras.RingBufferStats;

namespace ClearVision.Product.Infrastructure.Streaming;

public sealed class FrameRingBuffer
{
    private readonly object _gate = new();
    private readonly FrameEnvelope?[] _frames;
    private int _nextIndex;
    private int _count;
    private long _overwrittenCount;

    public FrameRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _frames = new FrameEnvelope[capacity];
    }

    public int Capacity => _frames.Length;

    public void Push(FrameEnvelope frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            if (_count == _frames.Length)
            {
                _overwrittenCount++;
            }
            else
            {
                _count++;
            }

            _frames[_nextIndex] = frame;
            _nextIndex = (_nextIndex + 1) % _frames.Length;
        }
    }

    public bool TryGetLatest(out FrameEnvelope? frame)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                frame = null;
                return false;
            }

            var index = (_nextIndex - 1 + _frames.Length) % _frames.Length;
            frame = _frames[index];
            return frame != null;
        }
    }

    public IReadOnlyList<FrameEnvelope> SliceBySequence(long from, long to)
    {
        if (to < from)
        {
            return Array.Empty<FrameEnvelope>();
        }

        lock (_gate)
        {
            var result = new List<FrameEnvelope>(Math.Min(_count, (int)Math.Min(int.MaxValue, to - from + 1)));
            if (_count == 0)
            {
                return result;
            }

            var start = _count == _frames.Length ? _nextIndex : 0;
            for (var i = 0; i < _count; i++)
            {
                var frame = _frames[(start + i) % _frames.Length];
                if (frame != null && frame.Sequence >= from && frame.Sequence <= to)
                {
                    result.Add(frame);
                }
            }

            return result;
        }
    }

    public IReadOnlyList<FrameEnvelope> SliceAround(long centerSeq, int before, int after)
    {
        before = Math.Max(0, before);
        after = Math.Max(0, after);
        return SliceBySequence(centerSeq - before, centerSeq + after);
    }

    public CoreRingBufferStats SnapshotStats()
    {
        lock (_gate)
        {
            FrameEnvelope? oldest = null;
            FrameEnvelope? latest = null;
            if (_count > 0)
            {
                var start = _count == _frames.Length ? _nextIndex : 0;
                for (var i = 0; i < _count; i++)
                {
                    var frame = _frames[(start + i) % _frames.Length];
                    if (frame != null)
                    {
                        oldest ??= frame;
                        latest = frame;
                    }
                }
            }

            return new CoreRingBufferStats(
                Capacity,
                _count,
                _overwrittenCount,
                oldest?.Sequence,
                latest?.Sequence);
        }
    }
}
