using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

public interface IInspectionResultChannelWriter
{
    bool TryWrite(InspectionResult result);

    ValueTask WriteAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        TryWrite(result);
        return ValueTask.CompletedTask;
    }
}

public sealed class InspectionResultBackgroundService : BackgroundService, IInspectionResultChannelWriter
{
    private readonly Channel<InspectionResult> _channel;
    private readonly ILogger<InspectionResultBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _batchSize;
    private readonly int _queueCapacity;
    private readonly int _maxSaveRetries;
    private readonly long _maxQueuedImageBytes;
    private readonly string _spoolFilePath;
    private readonly string _deadLetterFilePath;
    private readonly object _queuedImageBytesLock = new();
    private long _queuedImageBytes;
    private long _spooledResultCount;
    private long _deadLetterResultCount;

    private static readonly JsonSerializerOptions SpoolJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InspectionResultBackgroundService(
        ILogger<InspectionResultBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _batchSize = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:BatchSize",
            "Performance__Persistence__BatchSize",
            "CV_PERSISTENCE_BATCH_SIZE",
            fallback: 50,
            min: 1,
            max: 1000);
        _queueCapacity = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:QueueCapacity",
            "Performance__Persistence__QueueCapacity",
            "CV_PERSISTENCE_QUEUE_CAPACITY",
            fallback: 1000,
            min: 1,
            max: 100_000);
        _maxSaveRetries = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxSaveRetries",
            "Performance__Persistence__MaxSaveRetries",
            "CV_PERSISTENCE_MAX_SAVE_RETRIES",
            fallback: 3,
            min: 1,
            max: 20);
        _maxQueuedImageBytes = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxQueuedImageBytes",
            "Performance__Persistence__MaxQueuedImageBytes",
            "CV_PERSISTENCE_MAX_QUEUED_IMAGE_BYTES",
            fallback: 64 * 1024 * 1024,
            min: 1,
            max: int.MaxValue);

        var spoolDirectory = ResolveConfiguredPath(
            configuration?["Performance:Persistence:SpoolDirectory"],
            "Performance__Persistence__SpoolDirectory",
            "CV_PERSISTENCE_SPOOL_DIRECTORY",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision",
                "inspection-result-spool"));
        Directory.CreateDirectory(spoolDirectory);
        _spoolFilePath = Path.Combine(spoolDirectory, "inspection-results.jsonl");
        _deadLetterFilePath = Path.Combine(spoolDirectory, "inspection-results.deadletter.jsonl");

        _channel = Channel.CreateBounded<InspectionResult>(new BoundedChannelOptions(_queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryWrite(InspectionResult result)
    {
        if (!TryReserveQueuedImageBytes(result))
        {
            _logger.LogWarning(
                "Inspection result persistence image budget is full; caller should use WriteAsync to apply backpressure. ResultId={ResultId}, OutputImageBytes={OutputImageBytes}, QueuedImageBytes={QueuedImageBytes}, MaxQueuedImageBytes={MaxQueuedImageBytes}",
                result.Id,
                GetOutputImageBytes(result),
                Volatile.Read(ref _queuedImageBytes),
                _maxQueuedImageBytes);
            return false;
        }

        var written = _channel.Writer.TryWrite(result);
        if (!written)
        {
            ReleaseQueuedImageBytes(result);
            _logger.LogWarning(
                "Inspection result persistence queue is full; caller should use WriteAsync to avoid dropping the result. ResultId={ResultId}",
                result.Id);
        }

        return written;
    }

    public async ValueTask WriteAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        await WaitForQueuedImageBudgetAsync(result, cancellationToken);

        if (_channel.Writer.TryWrite(result))
        {
            return;
        }

        try
        {
            await _channel.Writer.WriteAsync(result, cancellationToken);
        }
        catch
        {
            ReleaseQueuedImageBytes(result);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inspection result background persistence service started.");
        await ReplaySpooledResultsAsync(stoppingToken);

        var batch = new List<InspectionResult>(_batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (batch.Count < _batchSize && _channel.Reader.TryRead(out var result))
                    {
                        batch.Add(result);
                    }

                    if (batch.Count > 0 && await SaveBatchWithRetryOrSpoolAsync(batch, stoppingToken))
                    {
                        ClearBatchAndReleaseQueuedImageBytes(batch);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (batch.Count > 0)
                {
                    await SaveBatchWithRetryOrSpoolAsync(batch, CancellationToken.None);
                    ClearBatchAndReleaseQueuedImageBytes(batch);
                }

                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure in inspection result persistence loop.");
            }
        }

        while (_channel.Reader.TryRead(out var lastResult))
        {
            ClearBatchAndReleaseQueuedImageBytes(batch);
            batch.Add(lastResult);
            while (batch.Count < _batchSize && _channel.Reader.TryRead(out var result))
            {
                batch.Add(result);
            }

            await SaveBatchWithRetryOrSpoolAsync(batch, CancellationToken.None);
            ClearBatchAndReleaseQueuedImageBytes(batch);
        }
    }

    private async Task<bool> SaveBatchWithRetryOrSpoolAsync(List<InspectionResult> results, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _maxSaveRetries; attempt++)
        {
            if (await TrySaveBatchAsync(results, cancellationToken))
            {
                return true;
            }

            if (attempt < _maxSaveRetries)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(5_000, 200 * Math.Pow(2, attempt - 1)));
                _logger.LogWarning(
                    "Inspection result batch save failed; retrying. Attempt={Attempt}, MaxAttempts={MaxAttempts}, Count={Count}, DelayMs={DelayMs}",
                    attempt,
                    _maxSaveRetries,
                    results.Count,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        await AppendSpoolAsync(results, _spoolFilePath, cancellationToken);
        Interlocked.Add(ref _spooledResultCount, results.Count);
        _logger.LogError(
            "Inspection result batch moved to durable spool after retries. Count={Count}, SpoolFile={SpoolFile}, TotalSpooled={Spooled}",
            results.Count,
            _spoolFilePath,
            Volatile.Read(ref _spooledResultCount));
        return true;
    }

    private async Task<bool> TrySaveBatchAsync(List<InspectionResult> results, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IInspectionResultRepository>();
            await repo.AddRangeAsync(results);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inspection result batch save failed. Count={Count}", results.Count);
            return false;
        }
    }

    private async Task ReplaySpooledResultsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_spoolFilePath))
        {
            return;
        }

        var replayed = new List<InspectionResult>(_batchSize);
        var tempPath = _spoolFilePath + ".tmp";
        var remainingCount = 0;

        {
            await using var remainingStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var remainingWriter = new StreamWriter(remainingStream, new UTF8Encoding(false));

            foreach (var line in File.ReadLines(_spoolFilePath, Encoding.UTF8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var record = JsonSerializer.Deserialize<InspectionResultSpoolRecord>(line, SpoolJsonOptions);
                    if (record == null)
                    {
                        continue;
                    }

                    replayed.Add(record.ToEntity());
                    if (replayed.Count >= _batchSize)
                    {
                        if (!await TrySaveBatchAsync(replayed, cancellationToken))
                        {
                            remainingCount += await WriteSpoolLinesAsync(remainingWriter, replayed, cancellationToken);
                        }

                        replayed.Clear();
                    }
                }
                catch (Exception ex)
                {
                    await File.AppendAllTextAsync(_deadLetterFilePath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
                    Interlocked.Increment(ref _deadLetterResultCount);
                    _logger.LogWarning(ex, "Moved invalid inspection result spool line to dead letter file.");
                }
            }

            if (replayed.Count > 0 && !await TrySaveBatchAsync(replayed, cancellationToken))
            {
                remainingCount += await WriteSpoolLinesAsync(remainingWriter, replayed, cancellationToken);
            }

            await remainingWriter.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, _spoolFilePath, overwrite: true);
        _logger.LogInformation(
            "Inspection result spool replay completed. Remaining={Remaining}, DeadLetter={DeadLetter}",
            remainingCount,
            Volatile.Read(ref _deadLetterResultCount));
    }

    private async Task AppendSpoolAsync(IEnumerable<InspectionResult> results, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(ToSpoolLine(result));
        }
    }

    private static async Task<int> WriteSpoolLinesAsync(
        TextWriter writer,
        IEnumerable<InspectionResult> results,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(ToSpoolLine(result));
            count++;
        }

        return count;
    }

    private static string ToSpoolLine(InspectionResult result)
    {
        return JsonSerializer.Serialize(InspectionResultSpoolRecord.FromEntity(result), SpoolJsonOptions);
    }

    private async ValueTask WaitForQueuedImageBudgetAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        while (!TryReserveQueuedImageBytes(result))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken);
        }
    }

    private bool TryReserveQueuedImageBytes(InspectionResult result)
    {
        var imageBytes = GetOutputImageBytes(result);
        if (imageBytes <= 0)
        {
            return true;
        }

        lock (_queuedImageBytesLock)
        {
            if (_queuedImageBytes + imageBytes <= _maxQueuedImageBytes ||
                (imageBytes > _maxQueuedImageBytes && _queuedImageBytes == 0))
            {
                _queuedImageBytes += imageBytes;
                return true;
            }

            return false;
        }
    }

    private void ReleaseQueuedImageBytes(InspectionResult result)
    {
        var imageBytes = GetOutputImageBytes(result);
        if (imageBytes <= 0)
        {
            return;
        }

        lock (_queuedImageBytesLock)
        {
            _queuedImageBytes = Math.Max(0, _queuedImageBytes - imageBytes);
        }
    }

    private void ClearBatchAndReleaseQueuedImageBytes(List<InspectionResult> batch)
    {
        foreach (var result in batch)
        {
            ReleaseQueuedImageBytes(result);
        }

        batch.Clear();
    }

    private static int GetOutputImageBytes(InspectionResult result)
    {
        return result.OutputImage?.Length ?? 0;
    }

    private static int ResolveConfiguredInt(
        IConfiguration? configuration,
        string configurationKey,
        string environmentKey,
        string fallbackEnvironmentKey,
        int fallback,
        int min,
        int max)
    {
        var configured = configuration?[configurationKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(environmentKey);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(fallbackEnvironmentKey);
        }

        return int.TryParse(configured, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static string ResolveConfiguredPath(
        string? configured,
        string environmentKey,
        string fallbackEnvironmentKey,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(environmentKey);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(fallbackEnvironmentKey);
        }

        return string.IsNullOrWhiteSpace(configured)
            ? fallback
            : Environment.ExpandEnvironmentVariables(configured.Trim());
    }

    private sealed class InspectionResultSpoolRecord
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid? ImageId { get; set; }
        public InspectionStatus Status { get; set; }
        public long ProcessingTimeMs { get; set; }
        public double? ConfidenceScore { get; set; }
        public string? ErrorMessage { get; set; }
        public byte[]? OutputImage { get; set; }
        public DateTime InspectionTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? OutputDataJson { get; set; }
        public string? AnalysisDataJson { get; set; }
        public string? FlowVersionHash { get; set; }
        public string? CalibrationBundleId { get; set; }
        public Guid? SessionId { get; set; }
        public List<InspectionDefectSpoolRecord> Defects { get; set; } = [];

        public static InspectionResultSpoolRecord FromEntity(InspectionResult result)
        {
            return new InspectionResultSpoolRecord
            {
                Id = result.Id,
                ProjectId = result.ProjectId,
                ImageId = result.ImageId,
                Status = result.Status,
                ProcessingTimeMs = result.ProcessingTimeMs,
                ConfidenceScore = result.ConfidenceScore,
                ErrorMessage = result.ErrorMessage,
                OutputImage = result.OutputImage,
                InspectionTime = result.InspectionTime,
                CreatedAt = result.CreatedAt,
                ModifiedAt = result.ModifiedAt,
                OutputDataJson = result.OutputDataJson,
                AnalysisDataJson = result.AnalysisDataJson,
                FlowVersionHash = result.FlowVersionHash,
                CalibrationBundleId = result.CalibrationBundleId,
                SessionId = result.SessionId,
                Defects = result.Defects.Select(InspectionDefectSpoolRecord.FromEntity).ToList()
            };
        }

        public InspectionResult ToEntity()
        {
            var result = new InspectionResult(ProjectId, ImageId);
            result.SetResult(Status, ProcessingTimeMs, ConfidenceScore, ErrorMessage);
            if (OutputImage is { Length: > 0 })
            {
                result.SetOutputImage(OutputImage);
            }

            if (!string.IsNullOrWhiteSpace(OutputDataJson))
            {
                result.SetOutputDataJson(OutputDataJson);
            }

            if (!string.IsNullOrWhiteSpace(AnalysisDataJson))
            {
                result.SetAnalysisDataJson(AnalysisDataJson);
            }

            result.SetTraceability(FlowVersionHash, CalibrationBundleId, SessionId);
            var inspectionResultId = Id == Guid.Empty ? result.Id : Id;
            foreach (var defect in Defects)
            {
                result.AddDefect(defect.ToEntity(inspectionResultId));
            }

            if (Id != Guid.Empty)
            {
                result.RestorePersistenceMetadata(
                    Id,
                    InspectionTime == default ? result.InspectionTime : InspectionTime,
                    CreatedAt == default ? result.CreatedAt : CreatedAt,
                    ModifiedAt);
            }

            return result;
        }
    }

    private sealed class InspectionDefectSpoolRecord
    {
        public DefectType Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double ConfidenceScore { get; set; }
        public string? Description { get; set; }
        public string? AnnotationData { get; set; }

        public static InspectionDefectSpoolRecord FromEntity(Defect defect)
        {
            return new InspectionDefectSpoolRecord
            {
                Type = defect.Type,
                X = defect.X,
                Y = defect.Y,
                Width = defect.Width,
                Height = defect.Height,
                ConfidenceScore = defect.ConfidenceScore,
                Description = defect.Description,
                AnnotationData = defect.AnnotationData
            };
        }

        public Defect ToEntity(Guid inspectionResultId)
        {
            return new Defect(
                inspectionResultId,
                Type,
                X,
                Y,
                Width,
                Height,
                ConfidenceScore,
                Description,
                AnnotationData);
        }
    }
}
