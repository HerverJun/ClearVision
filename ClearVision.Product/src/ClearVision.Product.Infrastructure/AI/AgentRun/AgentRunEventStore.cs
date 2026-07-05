using System.Text;
using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public sealed class AgentRunEventStore
{
    public const string StorageVersion = "agent-run-events.jsonl.v1";

    private readonly object _gate = new();
    private readonly AgentRunEventRedactor _redactor;
    private readonly string _directoryPath;
    private readonly AgentRunEventStoreOptions _options;
    private int _appendCountSinceCompaction;

    public AgentRunEventStore()
        : this(GetDefaultDirectory(), new AgentRunEventRedactor())
    {
    }

    public AgentRunEventStore(string directoryPath)
        : this(directoryPath, new AgentRunEventRedactor())
    {
    }

    public AgentRunEventStore(
        string directoryPath,
        AgentRunEventRedactor redactor,
        AgentRunEventStoreOptions? options = null)
    {
        _directoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? GetDefaultDirectory()
            : Path.GetFullPath(directoryPath);
        _redactor = redactor;
        _options = NormalizeOptions(options ?? AgentRunEventStoreOptions.Default);
        Directory.CreateDirectory(_directoryPath);
    }

    public string DirectoryPath => _directoryPath;

    public string EventPath => Path.Combine(_directoryPath, "agent_run_events.jsonl");

    public string SummaryPath => Path.Combine(_directoryPath, "agent_run_summary.jsonl");

    public void AppendEvent(AgentRunEvent evt)
    {
        Append(EventPath, evt);
    }

    public void AppendSummary(AgentRunSummary summary)
    {
        Append(SummaryPath, summary);
    }

    public void AppendEventWithSummary(AgentRunEvent evt, AgentRunSummary summary)
    {
        var eventLine = SerializeSafe(evt);
        var summaryLine = SerializeSafe(summary);

        lock (_gate)
        {
            Directory.CreateDirectory(_directoryPath);
            AppendLineCore(EventPath, eventLine);
            AppendLineCore(SummaryPath, summaryLine);
            TrackAppendAndCompactCore(appendedRecords: 2);
        }
    }

    public IReadOnlyList<AgentRunEvent> LoadEvents(string runId)
    {
        lock (_gate)
        {
            return ReadJsonLines<AgentRunEvent>(EventPath)
                .Where(evt => string.Equals(evt.RunId, runId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(evt => evt.Sequence)
                .ToList();
        }
    }

    public IReadOnlyList<AgentRunEvent> LoadEvents()
    {
        lock (_gate)
        {
            return ReadJsonLines<AgentRunEvent>(EventPath)
                .OrderBy(evt => evt.RunId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(evt => evt.Sequence)
                .ToList();
        }
    }

    public AgentRunSummary? LoadSummary(string runId)
    {
        lock (_gate)
        {
            return SelectLatestSummaries(ReadJsonLines<AgentRunSummary>(SummaryPath))
                .FirstOrDefault(summary => string.Equals(summary.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<AgentRunSummary> LoadSummaries()
    {
        lock (_gate)
        {
            return SelectLatestSummaries(ReadJsonLines<AgentRunSummary>(SummaryPath))
                .OrderByDescending(summary => summary.UpdatedAt)
                .ToList();
        }
    }

    private void Append<T>(string path, T item)
    {
        var line = SerializeSafe(item);

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AppendLineCore(path, line);
            TrackAppendAndCompactCore(appendedRecords: 1);
        }
    }

    private string SerializeSafe<T>(T item)
    {
        var line = JsonSerializer.Serialize(item, AgentRunEventJson.Options);
        if (!_redactor.IsRedactionSafe(item))
        {
            throw new InvalidOperationException("AgentRun event storage rejected an unsafe metadata payload.");
        }

        return line;
    }

    private static void AppendLineCore(string path, string line)
    {
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }

    private void TrackAppendAndCompactCore(int appendedRecords)
    {
        if (!_options.EnableCompaction)
        {
            return;
        }

        _appendCountSinceCompaction += Math.Max(1, appendedRecords);
        if (_appendCountSinceCompaction < _options.CompactionAppendThreshold &&
            !IsCompactionSizeThresholdExceededCore())
        {
            return;
        }

        CompactCore();
        _appendCountSinceCompaction = 0;
    }

    private bool IsCompactionSizeThresholdExceededCore()
    {
        var threshold = _options.CompactionSizeThresholdBytes;
        if (threshold <= 0)
        {
            return true;
        }

        return GetLengthCore(EventPath) >= threshold ||
               GetLengthCore(SummaryPath) >= threshold;
    }

    private static long GetLengthCore(string path)
    {
        return File.Exists(path)
            ? new FileInfo(path).Length
            : 0;
    }

    private static AgentRunEventStoreOptions NormalizeOptions(AgentRunEventStoreOptions options)
    {
        return options with
        {
            CompactionAppendThreshold = options.CompactionAppendThreshold <= 0 ? 512 : options.CompactionAppendThreshold,
            CompactionSizeThresholdBytes = options.CompactionSizeThresholdBytes <= 0 ? 1 : options.CompactionSizeThresholdBytes,
            MaxSummaryRuns = options.MaxSummaryRuns <= 0 ? 1 : options.MaxSummaryRuns,
            MaxEventsPerRun = options.MaxEventsPerRun <= 0 ? 1 : options.MaxEventsPerRun
        };
    }

    private void CompactCore()
    {
        var latestSummaries = SelectLatestSummaries(ReadJsonLines<AgentRunSummary>(SummaryPath))
            .OrderByDescending(summary => summary.UpdatedAt)
            .Take(_options.MaxSummaryRuns)
            .ToList();
        var summarizedRunIds = latestSummaries
            .Select(summary => summary.RunId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownSummaryRunIds = ReadJsonLines<AgentRunSummary>(SummaryPath)
            .Select(summary => summary.RunId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedEvents = ReadJsonLines<AgentRunEvent>(EventPath)
            .Where(evt => summarizedRunIds.Contains(evt.RunId) || !knownSummaryRunIds.Contains(evt.RunId))
            .GroupBy(evt => evt.RunId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => TrimEvents(group, _options.MaxEventsPerRun))
            .OrderBy(evt => evt.RunId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(evt => evt.Sequence)
            .ToList();

        RewriteCore(SummaryPath, latestSummaries);
        RewriteCore(EventPath, retainedEvents);
    }

    private static IReadOnlyList<AgentRunEvent> TrimEvents(
        IEnumerable<AgentRunEvent> events,
        int maxEventsPerRun)
    {
        var ordered = events
            .OrderBy(evt => evt.Sequence)
            .ToList();
        if (maxEventsPerRun <= 0 || ordered.Count <= maxEventsPerRun)
        {
            return ordered;
        }

        if (maxEventsPerRun == 1)
        {
            return [ordered[^1]];
        }

        var retained = new List<AgentRunEvent>(maxEventsPerRun)
        {
            ordered[0]
        };
        retained.AddRange(ordered.Skip(Math.Max(1, ordered.Count - (maxEventsPerRun - 1))));
        return retained;
    }

    private void RewriteCore<T>(string path, IReadOnlyList<T> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var lines = items.Select(SerializeSafe).ToList();
            File.WriteAllLines(tempPath, lines, Encoding.UTF8);
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

    private static IReadOnlyList<AgentRunSummary> SelectLatestSummaries(IReadOnlyList<AgentRunSummary> summaries)
    {
        return summaries
            .Select((summary, index) => new { Summary = summary, Index = index })
            .GroupBy(item => item.Summary.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Summary.UpdatedAt)
                .ThenByDescending(item => item.Index)
                .First()
                .Summary)
            .ToList();
    }

    private static IReadOnlyList<T> ReadJsonLines<T>(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var results = new List<T>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<T>(line, AgentRunEventJson.Options);
                if (item != null)
                {
                    results.Add(item);
                }
            }
            catch (JsonException)
            {
                // Append-only storage remains useful even if a previous line is corrupt.
            }
        }

        return results;
    }

    public static string GetDefaultDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CV_AGENT_RUN_EVENT_STORE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var governanceStore = Environment.GetEnvironmentVariable("CV_RUNTIME_PREVIEW_GOVERNANCE_STORE");
        if (!string.IsNullOrWhiteSpace(governanceStore))
        {
            return governanceStore;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ClearVision", "RuntimePreviewGovernance");
    }
}

public sealed record AgentRunEventStoreOptions
{
    public static AgentRunEventStoreOptions Default { get; } = new();

    public bool EnableCompaction { get; init; } = true;

    public int CompactionAppendThreshold { get; init; } = 512;

    public long CompactionSizeThresholdBytes { get; init; } = 2 * 1024 * 1024;

    public int MaxSummaryRuns { get; init; } = 2000;

    public int MaxEventsPerRun { get; init; } = 4096;
}
