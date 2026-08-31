using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Threading.Channels;
using ClearVision.Product.Application.Services;
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

public sealed record InspectionResultSpoolPartitionHealth(
    int RecordCount,
    long TotalBytes,
    DateTimeOffset? OldestRecordAtUtc,
    long TrimmedRecordCount,
    bool GapDetected);

public sealed record InspectionResultSpoolHealth(
    InspectionResultSpoolPartitionHealth Spool,
    InspectionResultSpoolPartitionHealth DeadLetter,
    bool Degraded,
    DateTimeOffset? LastSuccessfulCleanupAtUtc);

public sealed class InspectionResultBackgroundService : BackgroundService, IInspectionResultChannelWriter
{
    private readonly Channel<InspectionResult> _channel;
    private readonly ILogger<InspectionResultBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _batchSize;
    private readonly int _queueCapacity;
    private readonly int _maxSaveRetries;
    private readonly long _maxQueuedImageBytes;
    private readonly int _maxSpoolRecords;
    private readonly long _maxSpoolBytes;
    private readonly TimeSpan _maxSpoolAge;
    private readonly int _maxDeadLetterRecords;
    private readonly long _maxDeadLetterBytes;
    private readonly TimeSpan _maxDeadLetterAge;
    private readonly string _spoolDirectory;
    private readonly string _spoolFilePath;
    private readonly string _deadLetterFilePath;
    private readonly string _spoolBlobDirectory;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _queuedImageBytesLock = new();
    private readonly object _spoolRetentionLock = new();
    // Every JSONL reader, writer, replay and trim shares this gate.  The
    // retention lock protects the metrics; this gate protects the files.
    private readonly SemaphoreSlim _spoolFileGate = new(1, 1);
    private long _queuedImageBytes;
    private long _spooledResultCount;
    private long _deadLetterResultCount;
    private long _trimmedSpoolResultCount;
    private long _trimmedDeadLetterResultCount;
    private bool _spoolGapDetected;
    private bool _deadLetterGapDetected;
    private bool _spoolRetentionDegraded;
    private DateTimeOffset? _lastSuccessfulSpoolCleanupAtUtc;

    private static readonly JsonSerializerOptions SpoolJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InspectionResultBackgroundService(
        ILogger<InspectionResultBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration? configuration = null,
        Func<DateTimeOffset>? utcNow = null)
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
        _maxSpoolRecords = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxSpoolRecords",
            "Performance__Persistence__MaxSpoolRecords",
            "CV_PERSISTENCE_MAX_SPOOL_RECORDS",
            fallback: 10_000,
            min: 1,
            max: 1_000_000);
        _maxSpoolBytes = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxSpoolBytes",
            "Performance__Persistence__MaxSpoolBytes",
            "CV_PERSISTENCE_MAX_SPOOL_BYTES",
            fallback: 512 * 1024 * 1024,
            min: 1,
            max: int.MaxValue);
        _maxSpoolAge = TimeSpan.FromDays(ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxSpoolDays",
            "Performance__Persistence__MaxSpoolDays",
            "CV_PERSISTENCE_MAX_SPOOL_DAYS",
            fallback: 7,
            min: 1,
            max: 3650));
        _maxDeadLetterRecords = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxDeadLetterRecords",
            "Performance__Persistence__MaxDeadLetterRecords",
            "CV_PERSISTENCE_MAX_DEADLETTER_RECORDS",
            fallback: 2_000,
            min: 1,
            max: 1_000_000);
        _maxDeadLetterBytes = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxDeadLetterBytes",
            "Performance__Persistence__MaxDeadLetterBytes",
            "CV_PERSISTENCE_MAX_DEADLETTER_BYTES",
            fallback: 128 * 1024 * 1024,
            min: 1,
            max: int.MaxValue);
        _maxDeadLetterAge = TimeSpan.FromDays(ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:MaxDeadLetterDays",
            "Performance__Persistence__MaxDeadLetterDays",
            "CV_PERSISTENCE_MAX_DEADLETTER_DAYS",
            fallback: 30,
            min: 1,
            max: 3650));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

        var spoolDirectory = ResolveConfiguredPath(
            configuration?["Performance:Persistence:SpoolDirectory"],
            "Performance__Persistence__SpoolDirectory",
            "CV_PERSISTENCE_SPOOL_DIRECTORY",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision",
                "inspection-result-spool"));
        _spoolDirectory = Path.GetFullPath(spoolDirectory);
        Directory.CreateDirectory(_spoolDirectory);
        _spoolFilePath = Path.Combine(_spoolDirectory, "inspection-results.jsonl");
        _deadLetterFilePath = Path.Combine(_spoolDirectory, "inspection-results.deadletter.jsonl");
        _spoolBlobDirectory = Path.Combine(_spoolDirectory, "inspection-result-blobs");
        Directory.CreateDirectory(_spoolBlobDirectory);

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

        try
        {
            await AppendSpoolAsync(results, _spoolFilePath, cancellationToken);
            Interlocked.Add(ref _spooledResultCount, results.Count);
            _logger.LogError(
                "Inspection result batch moved to durable spool after retries. Count={Count}, SpoolFile={SpoolFile}, TotalSpooled={Spooled}",
                results.Count,
                _spoolFilePath,
                Volatile.Read(ref _spooledResultCount));
        }
        catch (Exception ex)
        {
            await TryAppendDeadLetterBatchAsync(results, ex, cancellationToken);
        }

        return true;
    }

    private async Task TryAppendDeadLetterBatchAsync(
        List<InspectionResult> results,
        Exception spoolException,
        CancellationToken cancellationToken)
    {
        try
        {
            await AppendSpoolAsync(results, _deadLetterFilePath, cancellationToken);
            Interlocked.Add(ref _deadLetterResultCount, results.Count);
            _logger.LogCritical(
                spoolException,
                "Inspection result batch could not be saved or spooled; moved to dead letter and released queue budget. Count={Count}, DeadLetterFile={DeadLetterFile}, DeadLetter={DeadLetter}",
                results.Count,
                _deadLetterFilePath,
                Volatile.Read(ref _deadLetterResultCount));
        }
        catch (Exception deadLetterException)
        {
            Interlocked.Add(ref _deadLetterResultCount, results.Count);
            _logger.LogCritical(
                deadLetterException,
                "Inspection result batch could not be saved, spooled, or dead-lettered; dropping batch to release queue budget. Count={Count}, SpoolFile={SpoolFile}, DeadLetterFile={DeadLetterFile}, DeadLetter={DeadLetter}",
                results.Count,
                _spoolFilePath,
                _deadLetterFilePath,
                Volatile.Read(ref _deadLetterResultCount));
            _logger.LogCritical(
                spoolException,
                "Original inspection result spool failure before drop. Count={Count}, SpoolFile={SpoolFile}",
                results.Count,
                _spoolFilePath);
        }
    }

    private async Task<bool> TrySaveBatchAsync(List<InspectionResult> results, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IInspectionResultRepository>();
            var persistenceResults = results
                .Select(InspectionResultPersistenceSnapshot.WithoutOutputImage)
                .ToList();
            await repo.AddRangeAsync(persistenceResults);
            var evidenceService = scope.ServiceProvider.GetService<IInspectionEvidenceManifestService>();
            if (evidenceService != null)
            {
                foreach (var result in results)
                {
                    try
                    {
                        await evidenceService.CaptureAsync(result, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Evidence manifest capture failed without affecting InspectionResult batch persistence. ResultId={ResultId}",
                            result.Id);
                    }
                }
            }

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
        CleanupSpoolRetention();
        await _spoolFileGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_spoolFilePath))
            {
                return;
            }

            var replayed = new List<ReplaySpoolEntry>(_batchSize);
            var tempPath = _spoolFilePath + ".tmp";
            var remainingCount = 0;
            var replayCompleted = false;

            try
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
                        if (record == null || record.IsDiagnostic)
                        {
                            throw new InvalidDataException("INSPECTION_RESULT_SPOOL_RECORD_MALFORMED");
                        }

                        replayed.Add(new ReplaySpoolEntry(line, record, record.ToEntity(ReadOutputImageBlob)));
                        if (replayed.Count >= _batchSize)
                        {
                            if (!await TrySaveBatchAsync(replayed.Select(entry => entry.Result).ToList(), cancellationToken))
                            {
                                remainingCount += await WriteSpoolLinesAsync(remainingWriter, replayed, cancellationToken);
                            }

                            replayed.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        await AppendDeadLetterDiagnosticCoreAsync("INSPECTION_RESULT_SPOOL_RECORD_MALFORMED", line, cancellationToken);
                        Interlocked.Increment(ref _deadLetterResultCount);
                        _logger.LogWarning(ex, "Moved invalid inspection result spool line to dead letter file.");
                    }
                }

                if (replayed.Count > 0 && !await TrySaveBatchAsync(replayed.Select(entry => entry.Result).ToList(), cancellationToken))
                {
                    remainingCount += await WriteSpoolLinesAsync(remainingWriter, replayed, cancellationToken);
                }
                await remainingWriter.FlushAsync(cancellationToken);
                replayCompleted = true;
            }
            finally
            {
                if (!replayCompleted)
                {
                    TryDeleteTempSpool(tempPath);
                }
            }

            File.Move(tempPath, _spoolFilePath, overwrite: true);
            _logger.LogInformation(
                "Inspection result spool replay completed. Remaining={Remaining}, DeadLetter={DeadLetter}",
                remainingCount,
                Volatile.Read(ref _deadLetterResultCount));
        }
        finally
        {
            _spoolFileGate.Release();
        }
        CleanupSpoolRetention();
    }

    private void TryDeleteTempSpool(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete partial inspection result spool temp file: {TempPath}", tempPath);
        }
    }

    private async Task AppendSpoolAsync(IEnumerable<InspectionResult> results, string path, CancellationToken cancellationToken)
    {
        await _spoolFileGate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Publish the blob and its JSONL reference while holding the same
                // gate used by cleanup/replay.  Otherwise cleanup can observe a
                // freshly-written but not-yet-referenced blob and delete it before
                // the spool record is appended.
                var record = await CreateSpoolRecordAsync(result, cancellationToken);
                await writer.WriteLineAsync(JsonSerializer.Serialize(record, SpoolJsonOptions));
            }

            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _spoolFileGate.Release();
        }
        CleanupSpoolRetention();
    }

    private static async Task<int> WriteSpoolLinesAsync(
        TextWriter writer,
        IEnumerable<ReplaySpoolEntry> results,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(result.RawLine);
            count++;
        }

        return count;
    }

    public InspectionResultSpoolHealth GetSpoolHealth()
    {
        CleanupSpoolRetention();
        _spoolFileGate.Wait();
        try
        {
            lock (_spoolRetentionLock)
            {
                var spool = ReadPartitionHealthLocked(_spoolFilePath, _trimmedSpoolResultCount, _spoolGapDetected);
                var deadLetter = ReadPartitionHealthLocked(_deadLetterFilePath, _trimmedDeadLetterResultCount, _deadLetterGapDetected);
                return new InspectionResultSpoolHealth(
                    spool,
                    deadLetter,
                    _spoolRetentionDegraded,
                    _lastSuccessfulSpoolCleanupAtUtc);
            }
        }
        finally
        {
            _spoolFileGate.Release();
        }
    }

    private async Task<InspectionResultSpoolRecord> CreateSpoolRecordAsync(
        InspectionResult result,
        CancellationToken cancellationToken)
    {
        var blobId = result.OutputImage is { Length: > 0 }
            ? await WriteOutputImageBlobAsync(result.OutputImage, cancellationToken)
            : null;
        return InspectionResultSpoolRecord.FromEntity(result, blobId, _utcNow());
    }

    private async Task<string> WriteOutputImageBlobAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var blobId = Guid.NewGuid().ToString("N");
        var blobPath = GetBlobPath(blobId) ?? throw new InvalidOperationException("INSPECTION_RESULT_SPOOL_BLOB_ID_INVALID");
        var tempPath = blobPath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, imageBytes, cancellationToken);
            File.Move(tempPath, blobPath, overwrite: false);
            return blobId;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private byte[] ReadOutputImageBlob(string blobId)
    {
        var blobPath = GetBlobPath(blobId);
        if (blobPath == null || !File.Exists(blobPath))
        {
            throw new InvalidDataException("INSPECTION_RESULT_SPOOL_BLOB_MISSING");
        }

        return File.ReadAllBytes(blobPath);
    }

    private async Task AppendDeadLetterDiagnosticAsync(
        string failureCode,
        string rawLine,
        CancellationToken cancellationToken)
    {
        await _spoolFileGate.WaitAsync(cancellationToken);
        try
        {
            await AppendDeadLetterDiagnosticCoreAsync(failureCode, rawLine, cancellationToken);
        }
        finally
        {
            _spoolFileGate.Release();
        }
        CleanupSpoolRetention();
    }

    private async Task AppendDeadLetterDiagnosticCoreAsync(
        string failureCode,
        string rawLine,
        CancellationToken cancellationToken)
    {
        var record = InspectionResultSpoolRecord.Diagnostic(
            failureCode,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawLine))),
            _utcNow());
        await File.AppendAllTextAsync(
            _deadLetterFilePath,
            JsonSerializer.Serialize(record, SpoolJsonOptions) + Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);
    }

    private void CleanupSpoolRetention()
    {
        _spoolFileGate.Wait();
        try
        {
            lock (_spoolRetentionLock)
            {
                try
                {
                    var spool = TrimSpoolPartitionLocked(
                        _spoolFilePath,
                        _maxSpoolRecords,
                        _maxSpoolBytes,
                        _maxSpoolAge,
                        isDeadLetter: false);
                    var deadLetter = TrimSpoolPartitionLocked(
                        _deadLetterFilePath,
                        _maxDeadLetterRecords,
                        _maxDeadLetterBytes,
                        _maxDeadLetterAge,
                        isDeadLetter: true);
                    DeleteUnreferencedOutputImageBlobsLocked(spool.ReferencedBlobIds.Concat(deadLetter.ReferencedBlobIds));
                    _spoolRetentionDegraded = false;
                    _lastSuccessfulSpoolCleanupAtUtc = _utcNow();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    _spoolRetentionDegraded = true;
                    _logger.LogWarning(ex, "Inspection result spool retention cleanup is degraded.");
                }
            }
        }
        finally
        {
            _spoolFileGate.Release();
        }
    }

    private SpoolPartitionState TrimSpoolPartitionLocked(
        string path,
        int maxRecords,
        long maxBytes,
        TimeSpan maxAge,
        bool isDeadLetter)
    {
        if (!File.Exists(path))
        {
            return SpoolPartitionState.Empty;
        }

        if (Directory.Exists(path))
        {
            throw new IOException("INSPECTION_RESULT_SPOOL_PATH_INVALID");
        }

        var changed = false;
        var dropped = 0;
        var fileTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        var entries = new List<SpoolLine>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                changed = true;
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<InspectionResultSpoolRecord>(line, SpoolJsonOptions)
                    ?? throw new InvalidDataException("INSPECTION_RESULT_SPOOL_RECORD_MALFORMED");
                if (record.SpooledAtUtc == default)
                {
                    record.SpooledAtUtc = fileTimestamp;
                    changed = true;
                }

                if (record.LegacyOutputImage is { Length: > 0 })
                {
                    record.OutputImageBlobId = WriteLegacyOutputImageBlobLocked(record.LegacyOutputImage);
                    record.LegacyOutputImage = null;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(record.OutputImageBlobId) &&
                    (GetBlobPath(record.OutputImageBlobId) is not { } blobPath || !File.Exists(blobPath)))
                {
                    record.OutputImageBlobId = null;
                    record.FailureCode = "INSPECTION_RESULT_SPOOL_BLOB_MISSING";
                    changed = true;
                }

                var normalizedLine = JsonSerializer.Serialize(record, SpoolJsonOptions);
                changed |= !string.Equals(normalizedLine, line, StringComparison.Ordinal);
                entries.Add(new SpoolLine(normalizedLine, record, EstimateSpoolLineBytes(normalizedLine, record)));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                changed = true;
                dropped++;
            }
        }

        var cutoff = _utcNow() - maxAge;
        foreach (var expired in entries.Where(entry => entry.Record.SpooledAtUtc < cutoff).ToList())
        {
            entries.Remove(expired);
            dropped++;
            changed = true;
        }

        var totalBytes = entries.Sum(entry => entry.TotalBytes);
        while (entries.Count > maxRecords || totalBytes > maxBytes)
        {
            if (entries.Count == 0)
            {
                break;
            }

            var oldest = entries
                .OrderBy(entry => entry.Record.SpooledAtUtc)
                .ThenBy(entry => entry.Line, StringComparer.Ordinal)
                .First();
            entries.Remove(oldest);
            totalBytes -= oldest.TotalBytes;
            dropped++;
            changed = true;
        }

        if (dropped > 0)
        {
            if (isDeadLetter)
            {
                _trimmedDeadLetterResultCount += dropped;
                _deadLetterGapDetected = true;
            }
            else
            {
                _trimmedSpoolResultCount += dropped;
                _spoolGapDetected = true;
            }
        }

        if (changed)
        {
            RewriteSpoolPartitionLocked(path, entries.Select(entry => entry.Line));
        }

        return new SpoolPartitionState(
            entries.Count,
            Math.Max(0, totalBytes),
            entries.Count == 0 ? null : entries.Min(entry => entry.Record.SpooledAtUtc),
            entries.Select(entry => entry.Record.OutputImageBlobId)
                .Where(blobId => !string.IsNullOrWhiteSpace(blobId))
                .Select(blobId => blobId!)
                .ToHashSet(StringComparer.Ordinal));
    }

    private InspectionResultSpoolPartitionHealth ReadPartitionHealthLocked(
        string path,
        long trimmedCount,
        bool gapDetected)
    {
        if (!File.Exists(path) || Directory.Exists(path))
        {
            return new InspectionResultSpoolPartitionHealth(0, 0, null, trimmedCount, gapDetected);
        }

        var records = new List<SpoolLine>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            try
            {
                var record = JsonSerializer.Deserialize<InspectionResultSpoolRecord>(line, SpoolJsonOptions);
                if (record != null)
                {
                    records.Add(new SpoolLine(line, record, EstimateSpoolLineBytes(line, record)));
                }
            }
            catch (JsonException)
            {
                // Cleanup will remove corrupt data on its next successful pass.
            }
        }

        return new InspectionResultSpoolPartitionHealth(
            records.Count,
            records.Sum(record => record.TotalBytes),
            records.Count == 0 ? null : records.Min(record => record.Record.SpooledAtUtc),
            trimmedCount,
            gapDetected);
    }

    private void RewriteSpoolPartitionLocked(string path, IEnumerable<string> lines)
    {
        var tempPath = path + ".retention.tmp";
        try
        {
            File.WriteAllLines(tempPath, lines, new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private long EstimateSpoolLineBytes(string line, InspectionResultSpoolRecord record)
    {
        var blobBytes = string.IsNullOrWhiteSpace(record.OutputImageBlobId)
            ? 0
            : GetBlobPath(record.OutputImageBlobId) is { } blobPath && File.Exists(blobPath)
                ? new FileInfo(blobPath).Length
                : 0;
        return Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length + blobBytes;
    }

    private string WriteLegacyOutputImageBlobLocked(byte[] legacyImage)
    {
        var blobId = Guid.NewGuid().ToString("N");
        var blobPath = GetBlobPath(blobId) ?? throw new IOException("INSPECTION_RESULT_SPOOL_BLOB_ID_INVALID");
        var tempPath = blobPath + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, legacyImage);
            File.Move(tempPath, blobPath, overwrite: false);
            return blobId;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void DeleteUnreferencedOutputImageBlobsLocked(IEnumerable<string> referencedBlobIds)
    {
        var referenced = referencedBlobIds.ToHashSet(StringComparer.Ordinal);
        foreach (var blobPath in Directory.EnumerateFiles(_spoolBlobDirectory, "*.bin", SearchOption.TopDirectoryOnly))
        {
            var blobId = Path.GetFileNameWithoutExtension(blobPath);
            if (!referenced.Contains(blobId))
            {
                File.Delete(blobPath);
            }
        }
    }

    private string? GetBlobPath(string? blobId)
    {
        if (string.IsNullOrWhiteSpace(blobId) || !Guid.TryParseExact(blobId, "N", out _))
        {
            return null;
        }

        var candidate = Path.Combine(_spoolBlobDirectory, blobId + ".bin");
        var relative = Path.GetRelativePath(_spoolBlobDirectory, Path.GetFullPath(candidate));
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            ? null
            : candidate;
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

    private sealed record ReplaySpoolEntry(
        string RawLine,
        InspectionResultSpoolRecord Record,
        InspectionResult Result);

    private sealed record SpoolLine(
        string Line,
        InspectionResultSpoolRecord Record,
        long TotalBytes);

    private sealed record SpoolPartitionState(
        int RecordCount,
        long TotalBytes,
        DateTimeOffset? OldestRecordAtUtc,
        HashSet<string> ReferencedBlobIds)
    {
        public static SpoolPartitionState Empty { get; } = new(0, 0, null, new HashSet<string>(StringComparer.Ordinal));
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
        public string? OutputImageBlobId { get; set; }

        // Read-only migration support for old spool rows. New rows never serialize image bytes in JSONL.
        [JsonPropertyName("outputImage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte[]? LegacyOutputImage { get; set; }

        public DateTimeOffset SpooledAtUtc { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FailureCode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FailurePayloadHash { get; set; }
        public DateTime InspectionTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? OutputDataJson { get; set; }
        public string? AnalysisDataJson { get; set; }
        public string? FlowVersionHash { get; set; }
        public string? CalibrationBundleId { get; set; }
        public Guid? SessionId { get; set; }
        public List<InspectionDefectSpoolRecord> Defects { get; set; } = [];

        public bool IsDiagnostic => !string.IsNullOrWhiteSpace(FailureCode);

        public static InspectionResultSpoolRecord FromEntity(
            InspectionResult result,
            string? outputImageBlobId,
            DateTimeOffset spooledAtUtc)
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
                OutputImageBlobId = outputImageBlobId,
                SpooledAtUtc = spooledAtUtc,
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

        public static InspectionResultSpoolRecord Diagnostic(
            string failureCode,
            string failurePayloadHash,
            DateTimeOffset spooledAtUtc) => new()
        {
            SpooledAtUtc = spooledAtUtc,
            FailureCode = failureCode,
            FailurePayloadHash = failurePayloadHash
        };

        public InspectionResult ToEntity(Func<string, byte[]> readOutputImageBlob)
        {
            if (IsDiagnostic)
            {
                throw new InvalidDataException(FailureCode);
            }

            var result = new InspectionResult(ProjectId, ImageId);
            result.SetResult(Status, ProcessingTimeMs, ConfidenceScore, ErrorMessage);
            if (!string.IsNullOrWhiteSpace(OutputImageBlobId))
            {
                result.SetOutputImage(readOutputImageBlob(OutputImageBlobId));
            }
            else if (LegacyOutputImage is { Length: > 0 })
            {
                result.SetOutputImage(LegacyOutputImage);
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
