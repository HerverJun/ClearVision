using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Station.Sync;

public sealed record StationCommandResultSpoolHealth(
    int PendingCount,
    long PendingBytes,
    DateTimeOffset? OldestPendingAtUtc,
    long TrimmedCount,
    bool GapDetected,
    bool Degraded,
    DateTimeOffset? LastSuccessfulCleanupAtUtc);

/// <summary>
/// A durable, bounded operation log for command-result delivery. Command results use a
/// distinct spool from inspection results because acknowledgements are keyed by
/// CommandId+Status and must preserve that replay contract.
/// </summary>
public sealed class StationCommandResultSpoolStore
{
    private const int OperationCompactionThreshold = 512;
    private const string OperationUpsert = "upsert";
    private const string OperationAcknowledge = "ack";

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private readonly ILogger<StationCommandResultSpoolStore> _logger;
    private readonly int _maxPendingRecords;
    private readonly long _maxPendingBytes;
    private readonly TimeSpan _maxPendingAge;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly List<StationCommandResultDto> _pending = [];
    private int _operationCountSinceCompaction;
    private long _trimmedCount;
    private bool _gapDetected;
    private bool _degraded;
    private DateTimeOffset? _lastSuccessfulCleanupAtUtc;

    public StationCommandResultSpoolStore(
        IOptions<StationSyncOptions> options,
        ILogger<StationCommandResultSpoolStore> logger)
        : this(options, logger, null)
    {
    }

    internal StationCommandResultSpoolStore(
        IOptions<StationSyncOptions> options,
        ILogger<StationCommandResultSpoolStore> logger,
        Func<DateTimeOffset>? utcNow)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var configured = options.Value;
        _maxPendingRecords = Math.Max(1, configured.MaxCommandResultSpoolRecords);
        _maxPendingBytes = Math.Max(1, configured.MaxCommandResultSpoolMb) * 1024L * 1024L;
        _maxPendingAge = TimeSpan.FromDays(Math.Max(1, configured.MaxCommandResultSpoolDays));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        var directoryPath = string.IsNullOrWhiteSpace(configured.ResolvedSpoolDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation",
                "spool")
            : configured.ResolvedSpoolDirectory;
        Directory.CreateDirectory(directoryPath);
        _filePath = Path.Combine(directoryPath, "station-command-results.jsonl");
        Load();
    }

    public int PendingCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Estimated canonical pending payload bytes, not unbounded append-log bytes.</summary>
    public long PendingBytes
    {
        get
        {
            lock (_syncRoot)
            {
                return EstimatePendingBytesLocked();
            }
        }
    }

    public StationCommandResultSpoolHealth GetHealth()
    {
        lock (_syncRoot)
        {
            try
            {
                var trimmed = TrimPendingLocked();
                if (trimmed > 0)
                {
                    RewriteLocked();
                }

                _degraded = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to clean Station command-result spool.");
                _degraded = true;
            }

            var ordered = OrderedPendingLocked();
            return new StationCommandResultSpoolHealth(
                ordered.Count,
                ordered.Sum(EstimateResultBytes),
                ordered.Count == 0 ? null : EffectiveCreatedAtUtc(ordered[0]),
                _trimmedCount,
                _gapDetected,
                _degraded,
                _lastSuccessfulCleanupAtUtc);
        }
    }

    public void Enqueue(StationCommandResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_syncRoot)
        {
            var clone = Clone(result);
            if (clone.ReportedAtUtc == default)
            {
                clone.ReportedAtUtc = _utcNow();
            }

            if (clone.CreatedAtUtc == default)
            {
                clone.CreatedAtUtc = clone.ReportedAtUtc;
            }

            try
            {
                UpsertLocked(clone);
                AppendRecordLocked(StationCommandResultSpoolRecord.Upsert(clone));
                var trimmed = TrimPendingLocked();
                if (trimmed > 0)
                {
                    RewriteLocked();
                }
                else
                {
                    CompactIfNeededLocked();
                    MarkCleanupSucceededLocked();
                }
            }
            catch
            {
                _degraded = true;
                throw;
            }
        }
    }

    public IReadOnlyList<StationCommandResultDto> GetPendingBatch(int take)
    {
        lock (_syncRoot)
        {
            return OrderedPendingLocked()
                .Take(Math.Max(1, take))
                .Select(Clone)
                .ToList();
        }
    }

    public void Acknowledge(string commandId, StationCommandStatus status)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return;
        }

        lock (_syncRoot)
        {
            try
            {
                var removed = _pending.RemoveAll(item =>
                    string.Equals(item.CommandId, commandId, StringComparison.OrdinalIgnoreCase) &&
                    item.Status == status);
                if (removed > 0)
                {
                    AppendRecordLocked(StationCommandResultSpoolRecord.Acknowledge(commandId.Trim(), status));
                    CompactIfNeededLocked();
                    MarkCleanupSucceededLocked();
                }
            }
            catch
            {
                _degraded = true;
                throw;
            }
        }
    }

    private void Load()
    {
        lock (_syncRoot)
        {
            _pending.Clear();
            if (!File.Exists(_filePath))
            {
                MarkCleanupSucceededLocked();
                return;
            }

            var loadedLineCount = 0;
            foreach (var line in File.ReadLines(_filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    ApplyLoadedLineLocked(line);
                    loadedLineCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Station command-result spool line.");
                    _gapDetected = true;
                    _trimmedCount++;
                }
            }

            var trimmed = TrimPendingLocked();
            if (trimmed > 0 ||
                loadedLineCount >= OperationCompactionThreshold ||
                GetFileLength(_filePath) > _maxPendingBytes)
            {
                RewriteLocked();
            }
            else
            {
                MarkCleanupSucceededLocked();
            }
        }
    }

    private void ApplyLoadedLineLocked(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("operation", out var operationProperty))
        {
            var operation = operationProperty.GetString();
            if (string.Equals(operation, OperationUpsert, StringComparison.OrdinalIgnoreCase) &&
                document.RootElement.TryGetProperty("result", out var resultProperty))
            {
                var result = resultProperty.Deserialize<StationCommandResultDto>(_jsonOptions);
                if (result != null && !string.IsNullOrWhiteSpace(result.CommandId))
                {
                    if (result.ReportedAtUtc == default)
                    {
                        result.ReportedAtUtc = _utcNow();
                    }

                    if (result.CreatedAtUtc == default)
                    {
                        result.CreatedAtUtc = result.ReportedAtUtc;
                    }

                    UpsertLocked(result);
                }

                return;
            }

            if (string.Equals(operation, OperationAcknowledge, StringComparison.OrdinalIgnoreCase))
            {
                var commandId = document.RootElement.TryGetProperty("commandId", out var commandIdProperty)
                    ? commandIdProperty.GetString()
                    : string.Empty;
                var status = document.RootElement.TryGetProperty("status", out var statusProperty)
                    ? statusProperty.Deserialize<StationCommandStatus>(_jsonOptions)
                    : default;
                if (!string.IsNullOrWhiteSpace(commandId))
                {
                    _pending.RemoveAll(item =>
                        string.Equals(item.CommandId, commandId, StringComparison.OrdinalIgnoreCase) &&
                        item.Status == status);
                }
            }

            return;
        }

        var legacyResult = JsonSerializer.Deserialize<StationCommandResultDto>(line, _jsonOptions);
        if (legacyResult != null && !string.IsNullOrWhiteSpace(legacyResult.CommandId))
        {
            if (legacyResult.ReportedAtUtc == default)
            {
                legacyResult.ReportedAtUtc = _utcNow();
            }

            if (legacyResult.CreatedAtUtc == default)
            {
                legacyResult.CreatedAtUtc = legacyResult.ReportedAtUtc;
            }

            UpsertLocked(legacyResult);
        }
    }

    private void UpsertLocked(StationCommandResultDto result)
    {
        var existingIndex = _pending.FindIndex(item =>
            string.Equals(item.CommandId, result.CommandId, StringComparison.OrdinalIgnoreCase) &&
            item.Status == result.Status);
        if (existingIndex >= 0)
        {
            _pending[existingIndex] = Clone(result);
        }
        else
        {
            _pending.Add(Clone(result));
        }
    }

    private int TrimPendingLocked()
    {
        var ordered = OrderedPendingLocked();
        var cutoff = _utcNow() - _maxPendingAge;
        var removed = 0;

        while (ordered.Count > 0 && EffectiveCreatedAtUtc(ordered[0]) < cutoff)
        {
            _pending.Remove(ordered[0]);
            ordered.RemoveAt(0);
            removed++;
        }

        while (ordered.Count > _maxPendingRecords)
        {
            _pending.Remove(ordered[0]);
            ordered.RemoveAt(0);
            removed++;
        }

        var bytes = ordered.Sum(EstimateResultBytes);
        while (ordered.Count > 0 && bytes > _maxPendingBytes)
        {
            var oldest = ordered[0];
            bytes -= EstimateResultBytes(oldest);
            _pending.Remove(oldest);
            ordered.RemoveAt(0);
            removed++;
        }

        if (removed > 0)
        {
            _trimmedCount += removed;
            _gapDetected = true;
            _logger.LogWarning(
                "Trimmed Station command-result spool records due to retention. Trimmed={Trimmed}, Pending={Pending}, Bytes={Bytes}",
                removed,
                ordered.Count,
                bytes);
        }

        return removed;
    }

    private List<StationCommandResultDto> OrderedPendingLocked() => _pending
        .OrderBy(EffectiveCreatedAtUtc)
        .ThenBy(item => item.CommandId, StringComparer.Ordinal)
        .ThenBy(item => item.Status)
        .ToList();

    private long EstimatePendingBytesLocked() => OrderedPendingLocked().Sum(EstimateResultBytes);

    private long EstimateResultBytes(StationCommandResultDto result) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, _jsonOptions)) + Environment.NewLine.Length;

    private static DateTimeOffset EffectiveCreatedAtUtc(StationCommandResultDto result) =>
        result.CreatedAtUtc != default ? result.CreatedAtUtc : result.ReportedAtUtc;

    private void AppendRecordLocked(StationCommandResultSpoolRecord record)
    {
        var line = JsonSerializer.Serialize(record, _jsonOptions);
        File.AppendAllText(_filePath, line + Environment.NewLine, new UTF8Encoding(false));
        _operationCountSinceCompaction++;
    }

    private void CompactIfNeededLocked()
    {
        if (_operationCountSinceCompaction >= OperationCompactionThreshold ||
            GetFileLength(_filePath) > _maxPendingBytes)
        {
            RewriteLocked();
        }
    }

    private void RewriteLocked()
    {
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (var result in OrderedPendingLocked())
                {
                    writer.WriteLine(JsonSerializer.Serialize(result, _jsonOptions));
                }
            }

            File.Move(tempPath, _filePath, overwrite: true);
            _operationCountSinceCompaction = 0;
            MarkCleanupSucceededLocked();
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void MarkCleanupSucceededLocked()
    {
        _degraded = false;
        _lastSuccessfulCleanupAtUtc = _utcNow();
    }

    private static long GetFileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static StationCommandResultDto Clone(StationCommandResultDto result)
    {
        return new StationCommandResultDto
        {
            SchemaVersion = result.SchemaVersion,
            CommandId = result.CommandId,
            StationId = result.StationId,
            Status = result.Status,
            ProgressPercent = result.ProgressPercent,
            Message = result.Message,
            ErrorCode = result.ErrorCode,
            ErrorDetail = result.ErrorDetail,
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc,
            ReportedAtUtc = result.ReportedAtUtc,
            CreatedAtUtc = result.CreatedAtUtc
        };
    }

    private sealed class StationCommandResultSpoolRecord
    {
        public string Operation { get; init; } = string.Empty;

        public StationCommandResultDto? Result { get; init; }

        public string CommandId { get; init; } = string.Empty;

        public StationCommandStatus Status { get; init; }

        public static StationCommandResultSpoolRecord Upsert(StationCommandResultDto result)
        {
            return new StationCommandResultSpoolRecord
            {
                Operation = OperationUpsert,
                Result = Clone(result)
            };
        }

        public static StationCommandResultSpoolRecord Acknowledge(string commandId, StationCommandStatus status)
        {
            return new StationCommandResultSpoolRecord
            {
                Operation = OperationAcknowledge,
                CommandId = commandId,
                Status = status
            };
        }
    }
}
