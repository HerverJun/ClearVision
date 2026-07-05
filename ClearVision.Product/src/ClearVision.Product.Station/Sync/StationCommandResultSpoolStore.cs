using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Station.Sync;

public sealed class StationCommandResultSpoolStore
{
    private const int OperationCompactionThreshold = 512;
    private const string OperationUpsert = "upsert";
    private const string OperationAcknowledge = "ack";

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private readonly ILogger<StationCommandResultSpoolStore> _logger;
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

    public StationCommandResultSpoolStore(
        IOptions<StationSyncOptions> options,
        ILogger<StationCommandResultSpoolStore> logger)
    {
        _logger = logger;
        var directoryPath = string.IsNullOrWhiteSpace(options.Value.ResolvedSpoolDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation",
                "spool")
            : options.Value.ResolvedSpoolDirectory;
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

    public void Enqueue(StationCommandResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_syncRoot)
        {
            var clone = Clone(result);
            if (clone.ReportedAtUtc == default)
            {
                clone.ReportedAtUtc = DateTimeOffset.UtcNow;
            }

            if (clone.CreatedAtUtc == default)
            {
                clone.CreatedAtUtc = clone.ReportedAtUtc;
            }

            UpsertLocked(clone);
            AppendRecordLocked(StationCommandResultSpoolRecord.Upsert(clone));
            CompactIfNeededLocked();
        }
    }

    public IReadOnlyList<StationCommandResultDto> GetPendingBatch(int take)
    {
        lock (_syncRoot)
        {
            return _pending
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
            var removed = _pending.RemoveAll(item =>
                string.Equals(item.CommandId, commandId, StringComparison.OrdinalIgnoreCase) &&
                item.Status == status);
            if (removed > 0)
            {
                AppendRecordLocked(StationCommandResultSpoolRecord.Acknowledge(commandId.Trim(), status));
                CompactIfNeededLocked();
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
                    _logger.LogWarning(ex, "Failed to load Station command result spool line.");
                }
            }

            if (loadedLineCount >= OperationCompactionThreshold)
            {
                RewriteLocked();
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

    private void AppendRecordLocked(StationCommandResultSpoolRecord record)
    {
        var line = JsonSerializer.Serialize(record, _jsonOptions);
        File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
        _operationCountSinceCompaction++;
    }

    private void CompactIfNeededLocked()
    {
        if (_operationCountSinceCompaction < OperationCompactionThreshold)
        {
            return;
        }

        RewriteLocked();
    }

    private void RewriteLocked()
    {
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            foreach (var result in _pending)
            {
                writer.WriteLine(JsonSerializer.Serialize(result, _jsonOptions));
            }
        }

        File.Move(tempPath, _filePath, overwrite: true);
        _operationCountSinceCompaction = 0;
    }

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
