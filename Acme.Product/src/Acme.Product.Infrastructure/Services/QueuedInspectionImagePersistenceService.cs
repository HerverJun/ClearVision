using System.Threading.Channels;
using Acme.Product.Application.Services;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Services;

public sealed class QueuedInspectionImagePersistenceService : BackgroundService, IInspectionImagePersistenceService
{
    private const int QueueCapacity = 256;
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly IConfigurationService _configurationService;
    private readonly InspectionImagePersistenceService _inner;
    private readonly ILogger<QueuedInspectionImagePersistenceService> _logger;
    private readonly Channel<InspectionResult> _queue;
    private readonly long _maxQueuedImageBytes;
    private readonly object _queuedImageBytesLock = new();
    private long _queuedImageBytes;
    private long _droppedImageCount;

    public QueuedInspectionImagePersistenceService(
        IConfigurationService configurationService,
        InspectionImagePersistenceService inner,
        ILogger<QueuedInspectionImagePersistenceService> logger,
        IConfiguration? configuration = null)
    {
        _configurationService = configurationService;
        _inner = inner;
        _logger = logger;
        _maxQueuedImageBytes = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxQueuedImageBytes",
            "Performance__Persistence__MaxQueuedImageBytes",
            "CV_PERSISTENCE_MAX_QUEUED_IMAGE_BYTES",
            fallback: 64 * 1024 * 1024,
            min: 1,
            max: int.MaxValue);
        _queue = Channel.CreateBounded<InspectionResult>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.OutputImage == null || result.OutputImage.Length == 0)
        {
            return Task.CompletedTask;
        }

        var storage = _configurationService.GetCurrent().Storage ?? new StorageConfig();
        if (!InspectionImagePersistencePolicy.ShouldPersistImage(storage.SavePolicy, result.Status))
        {
            return Task.CompletedTask;
        }

        if (!TryReserveQueuedImageBytes(result.OutputImage.Length))
        {
            var droppedCount = Interlocked.Increment(ref _droppedImageCount);
            _logger.LogWarning(
                "[InspectionImagePersistenceQueue] Image queue byte budget is full; skipping image persistence to protect inspection cadence. ResultId={ResultId}, OutputImageBytes={OutputImageBytes}, QueuedImageBytes={QueuedImageBytes}, MaxQueuedImageBytes={MaxQueuedImageBytes}, DroppedCount={DroppedCount}",
                result.Id,
                result.OutputImage.Length,
                Volatile.Read(ref _queuedImageBytes),
                _maxQueuedImageBytes,
                droppedCount);
            return Task.CompletedTask;
        }

        var snapshot = SnapshotResult(result);
        if (!_queue.Writer.TryWrite(snapshot))
        {
            ReleaseQueuedImageBytes(snapshot);
            var droppedCount = Interlocked.Increment(ref _droppedImageCount);
            _logger.LogWarning(
                "[InspectionImagePersistenceQueue] NG 图像保存队列已满或已停止，跳过本次图像落盘以保护检测节拍。 ResultId={ResultId}, DroppedCount={DroppedCount}",
                result.Id,
                droppedCount);
        }

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var result in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _inner.PersistAsync(result, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[InspectionImagePersistenceQueue] 后台保存检测图像失败。 ResultId={ResultId}", result.Id);
                }
                finally
                {
                    ReleaseQueuedImageBytes(result);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        var executeTask = ExecuteTask;
        if (executeTask == null)
        {
            await base.StopAsync(cancellationToken);
            return;
        }

        var drainTask = Task.Delay(ShutdownDrainTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(executeTask, drainTask);
        if (completedTask == executeTask)
        {
            await executeTask;
            return;
        }

        _logger.LogWarning(
            "[InspectionImagePersistenceQueue] 停机前未能在 {TimeoutSeconds:F0} 秒内排空图像保存队列，将按主机关闭流程取消剩余保存。",
            ShutdownDrainTimeout.TotalSeconds);
        await base.StopAsync(cancellationToken);
    }

    private static InspectionResult SnapshotResult(InspectionResult source)
    {
        var snapshot = new InspectionResult(source.ProjectId, source.ImageId);
        snapshot.SetResult(source.Status, source.ProcessingTimeMs, source.ConfidenceScore, source.ErrorMessage);
        snapshot.SetOutputImage(source.OutputImage!.ToArray());
        if (!string.IsNullOrWhiteSpace(source.OutputDataJson))
        {
            snapshot.SetOutputDataJson(source.OutputDataJson);
        }

        if (!string.IsNullOrWhiteSpace(source.AnalysisDataJson))
        {
            snapshot.SetAnalysisDataJson(source.AnalysisDataJson);
        }

        snapshot.RestorePersistenceMetadata(source.Id, source.InspectionTime, source.CreatedAt, source.ModifiedAt);
        return snapshot;
    }

    private bool TryReserveQueuedImageBytes(long imageBytes)
    {
        if (imageBytes <= 0)
        {
            return true;
        }

        lock (_queuedImageBytesLock)
        {
            if (_queuedImageBytes > _maxQueuedImageBytes - imageBytes)
            {
                return false;
            }

            _queuedImageBytes += imageBytes;
            return true;
        }
    }

    private void ReleaseQueuedImageBytes(InspectionResult result)
    {
        var imageBytes = result.OutputImage?.LongLength ?? 0;
        if (imageBytes <= 0)
        {
            return;
        }

        lock (_queuedImageBytesLock)
        {
            _queuedImageBytes = Math.Max(0, _queuedImageBytes - imageBytes);
        }
    }

    private static int ResolveConfiguredInt(
        IConfiguration? configuration,
        string key,
        string environmentKey,
        string legacyEnvironmentKey,
        int fallback,
        int min,
        int max)
    {
        var raw = configuration?[key]
            ?? Environment.GetEnvironmentVariable(environmentKey)
            ?? Environment.GetEnvironmentVariable(legacyEnvironmentKey);

        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }
}
