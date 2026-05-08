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
    private readonly long _maxSpoolBytes;
    private readonly TimeSpan _maxSpoolAge;
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
        _maxBufferedResults = Math.Max(1, options.Value.MaxBufferedResults);
        _maxSpoolBytes = Math.Max(1, options.Value.MaxSpoolMb) * 1024L * 1024L;
        _maxSpoolAge = TimeSpan.FromDays(Math.Max(1, options.Value.MaxSpoolDays));
        _directoryPath = string.IsNullOrWhiteSpace(options.Value.ResolvedSpoolDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation",
                "spool")
            : options.Value.ResolvedSpoolDirectory;
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

    public int PendingCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _pendingResults.Count(result => result.SequenceId > _state.AckedSequenceId);
            }
        }
    }

    public long SpoolBytes
    {
        get
        {
            lock (_syncRoot)
            {
                return Directory.Exists(_directoryPath)
                    ? Directory.EnumerateFiles(_directoryPath, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
                    : 0;
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
            if (string.IsNullOrWhiteSpace(summary.MessageId))
            {
                summary.MessageId = $"result_{summary.StationId}_{nextSequenceId}_{Guid.NewGuid():N}";
            }

            if (summary.CreatedAtUtc == default)
            {
                summary.CreatedAtUtc = DateTimeOffset.UtcNow;
            }

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
                var loadedSequences = new HashSet<long>();
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

                        if (!loadedSequences.Add(summary.SequenceId))
                        {
                            _logger.LogWarning(
                                "Skipped duplicate Station spool sequence while loading. SequenceId={SequenceId}",
                                summary.SequenceId);
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
        var tempPath = _stateFilePath + ".tmp";
        var json = JsonSerializer.Serialize(_state, _jsonOptions);
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        File.Move(tempPath, _stateFilePath, overwrite: true);
    }

    private bool TrimOverflowLocked()
    {
        _pendingResults.Sort((left, right) => left.SequenceId.CompareTo(right.SequenceId));
        var dropped = new List<StationResultSummaryDto>();
        var cutoffUtc = DateTimeOffset.UtcNow - _maxSpoolAge;

        while (_pendingResults.Count > 0 &&
               _pendingResults[0].CreatedAtUtc != default &&
               _pendingResults[0].CreatedAtUtc < cutoffUtc)
        {
            dropped.Add(_pendingResults[0]);
            _pendingResults.RemoveAt(0);
        }

        if (_pendingResults.Count > _maxBufferedResults)
        {
            var overflowCount = _pendingResults.Count - _maxBufferedResults;
            dropped.AddRange(_pendingResults.Take(overflowCount));
            _pendingResults.RemoveRange(0, overflowCount);
        }

        var estimatedBytes = EstimateSpoolBytes(_pendingResults);
        while (_pendingResults.Count > 0 && estimatedBytes > _maxSpoolBytes)
        {
            var removed = _pendingResults[0];
            dropped.Add(removed);
            _pendingResults.RemoveAt(0);
            estimatedBytes -= EstimateSpoolLineBytes(removed);
        }

        if (dropped.Count == 0)
        {
            return false;
        }

        var droppedFromId = dropped.Min(result => result.SequenceId);
        var cutoffSequenceId = dropped.Max(result => result.SequenceId);
        _state.AckedSequenceId = Math.Max(_state.AckedSequenceId, cutoffSequenceId);

        _logger.LogWarning(
            "Dropped Station spool records due to capacity limit. Range={FromSequenceId}-{ToSequenceId}",
            droppedFromId,
            cutoffSequenceId);

        return true;
    }

    private long EstimateSpoolBytes(IEnumerable<StationResultSummaryDto> results)
    {
        return results.Sum(EstimateSpoolLineBytes);
    }

    private long EstimateSpoolLineBytes(StationResultSummaryDto result)
    {
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, _jsonOptions)) + Environment.NewLine.Length;
    }

    private static StationResultSummaryDto Clone(StationResultSummaryDto summary)
    {
        return new StationResultSummaryDto
        {
            SchemaVersion = summary.SchemaVersion,
            StationId = summary.StationId,
            LineName = summary.LineName,
            SequenceId = summary.SequenceId,
            MessageId = summary.MessageId,
            RunId = summary.RunId,
            PackageId = summary.PackageId,
            PackageName = summary.PackageName,
            PackageVersion = summary.PackageVersion,
            FlowHash = summary.FlowHash,
            ImageId = summary.ImageId,
            Outcome = summary.Outcome,
            InspectionStatus = summary.InspectionStatus,
            ExecutionTimeMs = summary.ExecutionTimeMs,
            DiagnosticCode = summary.DiagnosticCode,
            DiagnosticMessage = summary.DiagnosticMessage,
            PrimaryOutputsPreview = new Dictionary<string, string?>(summary.PrimaryOutputsPreview, StringComparer.OrdinalIgnoreCase),
            StartedAtUtc = summary.StartedAtUtc,
            CompletedAtUtc = summary.CompletedAtUtc,
            CreatedAtUtc = summary.CreatedAtUtc
        };
    }

    private sealed class StationSpoolState
    {
        public long NextSequenceId { get; set; }

        public long AckedSequenceId { get; set; }
    }
}
