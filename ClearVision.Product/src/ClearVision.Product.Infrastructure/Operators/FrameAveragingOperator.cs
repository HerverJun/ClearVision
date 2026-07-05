// FrameAveragingOperator.cs
// 帧平均算子
// 对连续帧进行平均或中值融合降噪
// 作者：蘅芜君
using System.Collections.Concurrent;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "帧平均",
    Description = "Averages multi-frame input to reduce temporal noise.",
    Category = "预处理",
    IconName = "frame-average",
    Keywords = new[] { "frame", "averaging", "multi-frame", "denoise" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("FrameCount", "Frame Count", PortDataType.Integer)]
[OperatorParam("FrameCount", "Frame Count", "int", DefaultValue = 8, Min = 1, Max = 64)]
[OperatorParam("Mode", "Mode", "enum", DefaultValue = "Mean", Options = new[] { "Mean|Mean", "Median|Median" })]
public class FrameAveragingOperator : OperatorBase, IDisposable
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, FrameWindowState> _states = new();
    private readonly object _cleanupSync = new();
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public override OperatorType OperatorType => OperatorType.FrameAveraging;

    public FrameAveragingOperator(ILogger<FrameAveragingOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
        }

        var frameCount = GetIntParam(@operator, "FrameCount", 8, 1, 64);
        var mode = GetStringParam(@operator, "Mode", "Mean");
        var nowUtc = DateTime.UtcNow;
        var state = _states.GetOrAdd(@operator.Id, static _ => new FrameWindowState());

        Mat result;
        int bufferedFrameCount;
        lock (state.SyncRoot)
        {
            state.AddFrame(src, frameCount);
            bufferedFrameCount = state.Count;
            result = mode.Equals("Median", StringComparison.OrdinalIgnoreCase)
                ? ComputeMedian(state.GetFrameSnapshot())
                : state.ComputeMean();
            state.LastTouchedUtc = nowUtc;
        }

        TryCleanupStaleStates(nowUtc);

        var output = new Dictionary<string, object>
        {
            { "FrameCount", bufferedFrameCount }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(result, output)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var frameCount = GetIntParam(@operator, "FrameCount", 8);
        if (frameCount < 1 || frameCount > 64)
        {
            return ValidationResult.Invalid("FrameCount must be in [1, 64]");
        }

        var mode = GetStringParam(@operator, "Mode", "Mean");
        var validModes = new[] { "Mean", "Median" };
        if (!validModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Mode must be Mean or Median");
        }

        return ValidationResult.Valid();
    }

    public void Dispose()
    {
        foreach (var state in _states.Values)
        {
            lock (state.SyncRoot)
            {
                state.Clear();
            }
        }

        _states.Clear();
    }

    private sealed class FrameWindowState
    {
        public object SyncRoot { get; } = new();
        private readonly Queue<Mat> _frames = new();
        private Mat? _meanAccumulator;
        private MatType _meanAccumulatorType;
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;
        public int Count => _frames.Count;

        public void Clear()
        {
            while (_frames.Count > 0)
            {
                var stale = _frames.Dequeue();
                stale.Dispose();
            }

            _meanAccumulator?.Dispose();
            _meanAccumulator = null;
        }

        public void AddFrame(Mat src, int frameCount)
        {
            if (_frames.Count > 0)
            {
                var reference = _frames.Peek();
                if (reference.Rows != src.Rows || reference.Cols != src.Cols || reference.Type() != src.Type())
                {
                    Clear();
                }
            }

            var clone = src.Clone();
            _frames.Enqueue(clone);
            AddToAccumulatorIfInitialized(clone);

            while (_frames.Count > frameCount)
            {
                var old = _frames.Dequeue();
                SubtractFromAccumulatorIfInitialized(old);
                old.Dispose();
            }
        }

        public IReadOnlyList<Mat> GetFrameSnapshot()
        {
            return _frames.ToArray();
        }

        public Mat ComputeMean()
        {
            if (_frames.Count == 0)
            {
                throw new InvalidOperationException("No frames available for averaging");
            }

            EnsureMeanAccumulator();
            var result = new Mat();
            _meanAccumulator!.ConvertTo(result, _frames.Peek().Type(), 1.0 / _frames.Count);
            return result;
        }

        private void EnsureMeanAccumulator()
        {
            if (_meanAccumulator != null)
            {
                return;
            }

            var reference = _frames.Peek();
            _meanAccumulatorType = GetMeanAccumulatorType(reference);
            _meanAccumulator = new Mat(reference.Rows, reference.Cols, _meanAccumulatorType, Scalar.All(0));
            foreach (var frame in _frames)
            {
                AddToAccumulator(frame);
            }
        }

        private void AddToAccumulatorIfInitialized(Mat frame)
        {
            if (_meanAccumulator != null)
            {
                AddToAccumulator(frame);
            }
        }

        private void SubtractFromAccumulatorIfInitialized(Mat frame)
        {
            var accumulator = _meanAccumulator;
            if (accumulator == null)
            {
                return;
            }

            using var temp = new Mat();
            frame.ConvertTo(temp, _meanAccumulatorType);
            Cv2.Subtract(accumulator, temp, accumulator);
        }

        private void AddToAccumulator(Mat frame)
        {
            var accumulator = _meanAccumulator ?? throw new InvalidOperationException("Mean accumulator is not initialized");
            using var temp = new Mat();
            frame.ConvertTo(temp, _meanAccumulatorType);
            Cv2.Add(accumulator, temp, accumulator);
        }
    }

    private void TryCleanupStaleStates(DateTime nowUtc)
    {
        if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
        {
            return;
        }

        lock (_cleanupSync)
        {
            if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
            {
                return;
            }

            var staleBefore = nowUtc - StateTtl;
            foreach (var entry in _states)
            {
                var shouldRemove = false;
                var state = entry.Value;
                lock (state.SyncRoot)
                {
                    shouldRemove = state.LastTouchedUtc < staleBefore;
                }

                if (!shouldRemove || !_states.TryRemove(entry.Key, out var removedState))
                {
                    continue;
                }

                lock (removedState.SyncRoot)
                {
                    removedState.Clear();
                }
            }

            _lastCleanupUtc = nowUtc;
        }
    }

    private static Mat ComputeMedian(IReadOnlyList<Mat> frames)
    {
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("No frames available for averaging");
        }

        EnsureSameShapeAndType(frames);

        var rows = frames[0].Rows;
        var channels = frames[0].Channels();
        var depth = frames[0].Depth();
        var flatWidth = frames[0].Cols * channels;
        var flattenedViews = new Mat[frames.Count];

        try
        {
            for (var i = 0; i < frames.Count; i++)
            {
                flattenedViews[i] = frames[i].Reshape(1, rows);
            }

            return depth switch
            {
                MatType.CV_8U => ComputeMedianTyped<byte>(flattenedViews, rows, flatWidth, channels, depth),
                MatType.CV_16U => ComputeMedianTyped<ushort>(flattenedViews, rows, flatWidth, channels, depth),
                MatType.CV_32F => ComputeMedianTyped<float>(flattenedViews, rows, flatWidth, channels, depth),
                MatType.CV_64F => ComputeMedianTyped<double>(flattenedViews, rows, flatWidth, channels, depth),
                _ => throw new InvalidOperationException($"Unsupported depth for frame median fusion: {depth}")
            };
        }
        finally
        {
            foreach (var view in flattenedViews)
            {
                view?.Dispose();
            }
        }
    }

    private static Mat ComputeMedianTyped<T>(
        IReadOnlyList<Mat> flattenedFrames,
        int rows,
        int flatWidth,
        int channels,
        MatType depth)
        where T : unmanaged, IComparable<T>
    {
        using var resultFlat = new Mat(rows, flatWidth, MatType.MakeType(depth, 1));
        var resultIndexer = resultFlat.GetGenericIndexer<T>();
        var frameIndexers = flattenedFrames.Select(frame => frame.GetGenericIndexer<T>()).ToArray();
        var medianIndex = flattenedFrames.Count / 2;

        Parallel.For(0, rows, row =>
        {
            var samples = new T[flattenedFrames.Count];
            for (var col = 0; col < flatWidth; col++)
            {
                for (var frame = 0; frame < flattenedFrames.Count; frame++)
                {
                    samples[frame] = frameIndexers[frame][row, col];
                }

                resultIndexer[row, col] = SelectKthInPlace(samples, medianIndex);
            }
        });

        using var reshaped = resultFlat.Reshape(channels, rows);
        return reshaped.Clone();
    }

    private static T SelectKthInPlace<T>(T[] values, int k)
        where T : IComparable<T>
    {
        var left = 0;
        var right = values.Length - 1;

        while (left <= right)
        {
            if (left == right)
            {
                return values[left];
            }

            var pivotIndex = left + ((right - left) / 2);
            pivotIndex = Partition(values, left, right, pivotIndex);

            if (k == pivotIndex)
            {
                return values[k];
            }

            if (k < pivotIndex)
            {
                right = pivotIndex - 1;
            }
            else
            {
                left = pivotIndex + 1;
            }
        }

        return values[k];
    }

    private static int Partition<T>(T[] values, int left, int right, int pivotIndex)
        where T : IComparable<T>
    {
        var pivotValue = values[pivotIndex];
        Swap(values, pivotIndex, right);
        var storeIndex = left;

        for (var i = left; i < right; i++)
        {
            if (values[i].CompareTo(pivotValue) < 0)
            {
                Swap(values, storeIndex, i);
                storeIndex++;
            }
        }

        Swap(values, storeIndex, right);
        return storeIndex;
    }

    private static void Swap<T>(T[] values, int i, int j)
    {
        if (i == j)
        {
            return;
        }

        (values[i], values[j]) = (values[j], values[i]);
    }

    private static void EnsureSameShapeAndType(IReadOnlyList<Mat> frames)
    {
        var reference = frames[0];
        var rows = reference.Rows;
        var cols = reference.Cols;
        var type = reference.Type();

        for (var i = 1; i < frames.Count; i++)
        {
            if (frames[i].Rows != rows || frames[i].Cols != cols || frames[i].Type() != type)
            {
                throw new InvalidOperationException("All frames must have the same size and type");
            }
        }
    }

    private static MatType GetMeanAccumulatorType(Mat frame)
    {
        return frame.Channels() switch
        {
            1 => MatType.CV_32FC1,
            2 => MatType.CV_32FC2,
            3 => MatType.CV_32FC3,
            4 => MatType.CV_32FC4,
            _ => throw new InvalidOperationException($"Unsupported channel count for frame averaging: {frame.Channels()}")
        };
    }
}

