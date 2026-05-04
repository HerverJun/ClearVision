using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationSpoolStore
{
    private readonly object _syncRoot = new();
    private readonly string _directoryPath;
    private readonly string _spoolFilePath;
    private readonly string _stateFilePath;
    private readonly int _maxBufferedResults;
    private readonly ILogger<StationSpoolStore> _logger;
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

    private readonly List<StationResultSummaryDto> _pendingResults = [];
    private StationSpoolState _state = new();

    public StationSpoolStore(
        IOptions<StationSyncOptions> options,
        ILogger<StationSpoolStore> logger)
    {
        _logger = logger;
        _maxBufferedResults = Math.Max(100, options.Value.MaxBufferedResults);
        _directoryPath = string.IsNullOrWhiteSpace(options.Value.SpoolDirectoryPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation",
                "spool")
            : options.Value.SpoolDirectoryPath;
        _spoolFilePath = Path.Combine(_directoryPath, "station-results.jsonl");
        _stateFilePath = Path.Combine(_directoryPath, "station-sync-state.json");

        Directory.CreateDirectory(_directoryPath);
        Load();
    }

    public long AckedSequenceId
    {
        get
        {
            lock (_syncRoot)
            {
                return _state.AckedSequenceId;
            }
        }
    }

    public StationResultSummaryDto Enqueue(StationResultSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        lock (_syncRoot)
        {
            var nextSequenceId = Math.Max(_state.NextSequenceId + 1, _state.AckedSequenceId + 1);
            summary.SequenceId = nextSequenceId;
            _state.NextSequenceId = nextSequenceId;
            _pendingResults.Add(Clone(summary));

            var trimmedOverflow = TrimOverflowLocked();
            if (trimmedOverflow)
            {
                RewriteSpoolLocked();
            }
            else
            {
                AppendResultLocked(summary);
            }

            SaveStateLocked();

            return Clone(summary);
        }
    }

    public IReadOnlyList<StationResultSummaryDto> GetPendingBatch(int take)
    {
        lock (_syncRoot)
        {
            return _pendingResults
                .Where(result => result.SequenceId > _state.AckedSequenceId)
                .OrderBy(result => result.SequenceId)
                .Take(Math.Max(1, take))
                .Select(Clone)
                .ToList();
        }
    }

    public void Acknowledge(long ackedSequenceId)
    {
        lock (_syncRoot)
        {
            if (ackedSequenceId <= _state.AckedSequenceId)
            {
                return;
            }

            _state.AckedSequenceId = ackedSequenceId;
            _pendingResults.RemoveAll(result => result.SequenceId <= ackedSequenceId);
            RewriteSpoolLocked();
            SaveStateLocked();
        }
    }

    private void Load()
    {
        lock (_syncRoot)
        {
            _state = LoadState();
            _pendingResults.Clear();

            if (File.Exists(_spoolFilePath))
            {
                foreach (var line in File.ReadLines(_spoolFilePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var summary = JsonSerializer.Deserialize<StationResultSummaryDto>(line, _jsonOptions);
                        if (summary == null || summary.SequenceId <= _state.AckedSequenceId)
                        {
                            continue;
                        }

                        _pendingResults.Add(summary);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load Station spool line.");
                    }
                }
            }

            if (_pendingResults.Count > 0)
            {
                _pendingResults.Sort((left, right) => left.SequenceId.CompareTo(right.SequenceId));
                _state.NextSequenceId = Math.Max(
                    _state.NextSequenceId,
                    _pendingResults[^1].SequenceId);
            }

            TrimOverflowLocked();
            RewriteSpoolLocked();
            SaveStateLocked();
        }
    }

    private StationSpoolState LoadState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new StationSpoolState();
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<StationSpoolState>(json, _jsonOptions) ?? new StationSpoolState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Station spool state.");
            return new StationSpoolState();
        }
    }

    private void AppendResultLocked(StationResultSummaryDto summary)
    {
        var line = JsonSerializer.Serialize(summary, _jsonOptions);
        File.AppendAllText(_spoolFilePath, line + Environment.NewLine, Encoding.UTF8);
    }

    private void RewriteSpoolLocked()
    {
        var tempPath = _spoolFilePath + ".tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            foreach (var result in _pendingResults.OrderBy(result => result.SequenceId))
            {
                writer.WriteLine(JsonSerializer.Serialize(result, _jsonOptions));
            }
        }

        File.Move(tempPath, _spoolFilePath, overwrite: true);
    }

    private void SaveStateLocked()
    {
        var json = JsonSerializer.Serialize(_state, _jsonOptions);
        File.WriteAllText(_stateFilePath, json, Encoding.UTF8);
    }

    private bool TrimOverflowLocked()
    {
        if (_pendingResults.Count <= _maxBufferedResults)
        {
            return false;
        }

        _pendingResults.Sort((left, right) => left.SequenceId.CompareTo(right.SequenceId));
        var overflowCount = _pendingResults.Count - _maxBufferedResults;
        var cutoffSequenceId = _pendingResults[overflowCount - 1].SequenceId;
        var droppedFromId = _pendingResults[0].SequenceId;

        _pendingResults.RemoveRange(0, overflowCount);

        _logger.LogWarning(
            "Dropped Station spool records due to capacity limit. Range={FromSequenceId}-{ToSequenceId}",
            droppedFromId,
            cutoffSequenceId);

        return true;
    }

    private static StationResultSummaryDto Clone(StationResultSummaryDto summary)
    {
        return new StationResultSummaryDto
        {
            SchemaVersion = summary.SchemaVersion,
            StationId = summary.StationId,
            LineName = summary.LineName,
            SequenceId = summary.SequenceId,
            RunId = summary.RunId,
            PackageId = summary.PackageId,
            PackageName = summary.PackageName,
            FlowHash = summary.FlowHash,
            ImageId = summary.ImageId,
            Outcome = summary.Outcome,
            InspectionStatus = summary.InspectionStatus,
            ExecutionTimeMs = summary.ExecutionTimeMs,
            DiagnosticCode = summary.DiagnosticCode,
            DiagnosticMessage = summary.DiagnosticMessage,
            StartedAtUtc = summary.StartedAtUtc,
            CompletedAtUtc = summary.CompletedAtUtc
        };
    }

    private sealed class StationSpoolState
    {
        public long NextSequenceId { get; set; }

        public long AckedSequenceId { get; set; }
    }
}
