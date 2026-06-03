using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Station.Sync;

public sealed class StationCommandResultSpoolStore
{
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

            var existingIndex = _pending.FindIndex(item =>
                string.Equals(item.CommandId, clone.CommandId, StringComparison.OrdinalIgnoreCase) &&
                item.Status == clone.Status);
            if (existingIndex >= 0)
            {
                _pending[existingIndex] = clone;
            }
            else
            {
                _pending.Add(clone);
            }

            RewriteLocked();
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
                RewriteLocked();
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

            foreach (var line in File.ReadLines(_filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var result = JsonSerializer.Deserialize<StationCommandResultDto>(line, _jsonOptions);
                    if (result == null || string.IsNullOrWhiteSpace(result.CommandId))
                    {
                        continue;
                    }

                    _pending.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Station command result spool line.");
                }
            }
        }
    }

    private void RewriteLocked()
    {
        var tempPath = _filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            foreach (var result in _pending)
            {
                writer.WriteLine(JsonSerializer.Serialize(result, _jsonOptions));
            }
        }

        File.Move(tempPath, _filePath, overwrite: true);
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
}
