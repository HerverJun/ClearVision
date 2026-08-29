using System.Threading.Channels;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.Streaming;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Continuous;

public sealed record ScheduledInferenceItem(
    FrameEnvelope Frame,
    string? TrackId,
    Func<FrameEnvelope, CancellationToken, Task<FlowExecutionResult>> ExecuteAsync);

public sealed record ScheduledInferenceResult(
    FrameEnvelope Frame,
    string? TrackId,
    FlowExecutionResult? Result,
    Exception? Error,
    TimeSpan Latency);

public sealed class InferenceScheduler : IAsyncDisposable
{
    private readonly Channel<ScheduledInferenceItem> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger? _logger;
    private readonly Task _worker;
    private long _acceptedCount;
    private long _droppedCount;
    private long _completedCount;
    private long _subscriberFailureCount;
    private int _queueDepth;

    public InferenceScheduler(int queueLength = 8, ILogger? logger = null)
    {
        queueLength = Math.Clamp(queueLength, 1, 1024);
        _logger = logger;
        _queue = Channel.CreateBounded<ScheduledInferenceItem>(new BoundedChannelOptions(queueLength)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public event Func<ScheduledInferenceResult, Task>? ResultReady;

    public InferenceSchedulerSnapshot Snapshot() =>
        new(
            Interlocked.Read(ref _acceptedCount),
            Interlocked.Read(ref _droppedCount),
            Interlocked.Read(ref _completedCount),
            Interlocked.Read(ref _subscriberFailureCount),
            Volatile.Read(ref _queueDepth));

    public bool TrySchedule(ScheduledInferenceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_cts.IsCancellationRequested)
        {
            return false;
        }

        var accepted = _queue.Writer.TryWrite(item);
        if (accepted)
        {
            Interlocked.Increment(ref _acceptedCount);
            Interlocked.Increment(ref _queueDepth);
        }
        else
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return accepted;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            await _cts.CancelAsync();
            _queue.Writer.TryComplete();
        }

        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(_cts.Token))
        {
            Interlocked.Decrement(ref _queueDepth);
            var started = DateTimeOffset.UtcNow;
            FlowExecutionResult? result = null;
            Exception? error = null;
            try
            {
                result = await item.ExecuteAsync(item.Frame, _cts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex;
            }

            Interlocked.Increment(ref _completedCount);
            await PublishResultAsync(new ScheduledInferenceResult(
                item.Frame,
                item.TrackId,
                result,
                error,
                DateTimeOffset.UtcNow - started));
        }
    }

    private async Task PublishResultAsync(ScheduledInferenceResult result)
    {
        var callback = ResultReady;
        if (callback == null)
        {
            return;
        }

        foreach (var subscriber in callback.GetInvocationList().Cast<Func<ScheduledInferenceResult, Task>>())
        {
            try
            {
                await subscriber(result);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _subscriberFailureCount);
                _logger?.LogError(
                    ex,
                    "[InferenceScheduler] ResultReady subscriber failed. CameraId={CameraId}, Sequence={Sequence}, TrackId={TrackId}",
                    result.Frame.CameraId,
                    result.Frame.Sequence,
                    result.TrackId);
            }
        }
    }
}

public sealed record InferenceSchedulerSnapshot(
    long AcceptedCount,
    long DroppedCount,
    long CompletedCount,
    long SubscriberFailureCount,
    int QueueDepth);
