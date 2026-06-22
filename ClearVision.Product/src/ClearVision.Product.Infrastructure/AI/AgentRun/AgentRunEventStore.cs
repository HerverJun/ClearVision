using System.Text;
using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public sealed class AgentRunEventStore
{
    public const string StorageVersion = "agent-run-events.jsonl.v1";

    private readonly object _gate = new();
    private readonly AgentRunEventRedactor _redactor;
    private readonly string _directoryPath;

    public AgentRunEventStore()
        : this(GetDefaultDirectory(), new AgentRunEventRedactor())
    {
    }

    public AgentRunEventStore(string directoryPath)
        : this(directoryPath, new AgentRunEventRedactor())
    {
    }

    public AgentRunEventStore(string directoryPath, AgentRunEventRedactor redactor)
    {
        _directoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? GetDefaultDirectory()
            : Path.GetFullPath(directoryPath);
        _redactor = redactor;
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

    public IReadOnlyList<AgentRunEvent> LoadEvents(string runId)
    {
        return ReadJsonLines<AgentRunEvent>(EventPath)
            .Where(evt => string.Equals(evt.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(evt => evt.Sequence)
            .ToList();
    }

    public IReadOnlyList<AgentRunEvent> LoadEvents()
    {
        return ReadJsonLines<AgentRunEvent>(EventPath)
            .OrderBy(evt => evt.RunId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(evt => evt.Sequence)
            .ToList();
    }

    public AgentRunSummary? LoadSummary(string runId)
    {
        return ReadJsonLines<AgentRunSummary>(SummaryPath)
            .Where(summary => string.Equals(summary.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(summary => summary.UpdatedAt)
            .FirstOrDefault();
    }

    public IReadOnlyList<AgentRunSummary> LoadSummaries()
    {
        return ReadJsonLines<AgentRunSummary>(SummaryPath)
            .GroupBy(summary => summary.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(summary => summary.UpdatedAt).First())
            .OrderByDescending(summary => summary.UpdatedAt)
            .ToList();
    }

    private void Append<T>(string path, T item)
    {
        var line = JsonSerializer.Serialize(item, AgentRunEventJson.Options);
        if (!_redactor.IsRedactionSafe(item))
        {
            throw new InvalidOperationException("AgentRun event storage rejected an unsafe metadata payload.");
        }

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
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

    private static string GetDefaultDirectory()
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
