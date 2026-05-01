// ClarificationMemoryStore.cs
// Persists user-provided requirement answers across sessions, templates, and production lines.
// Enables the system to stop re-asking the same questions.
using System.Collections.Concurrent;
using System.Text.Json;

namespace Acme.Product.Infrastructure.AI;

public enum MemoryScope
{
    /// <summary>Only applies to the current session.</summary>
    Session,

    /// <summary>Saved as the default for this template.</summary>
    Template,

    /// <summary>Saved as the default for this production line / station.</summary>
    Line
}

public sealed class ClarificationMemoryEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public MemoryScope Scope { get; set; } = MemoryScope.Session;
    public string? TemplateId { get; set; }
    public string? LineId { get; set; }
    public string? SessionId { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public interface IClarificationMemoryStore
{
    /// <summary>Get the best matching memory value for a given field in context.</summary>
    string? Get(string field, string? sessionId = null, string? templateId = null, string? lineId = null);

    /// <summary>Get all matching entries for a field.</summary>
    IReadOnlyList<ClarificationMemoryEntry> GetAll(string field, string? sessionId = null, string? templateId = null, string? lineId = null);

    /// <summary>Save a memory entry.</summary>
    void Set(string field, string value, MemoryScope scope = MemoryScope.Session,
        string? sessionId = null, string? templateId = null, string? lineId = null);

    /// <summary>Apply all session-level memories to fill gaps in a requirement brief.</summary>
    void ApplyToBrief(Core.DTOs.AiRequirementBrief brief, string sessionId, string? templateId = null, string? lineId = null);

    /// <summary>Remove all entries for a session.</summary>
    void ClearSession(string sessionId);
}

public sealed class ClarificationMemoryStore : IClarificationMemoryStore
{
    private readonly ConcurrentDictionary<string, ClarificationMemoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly object _persistLock = new();

    private const int MaxEntries = 500;

    public ClarificationMemoryStore(string? storageRootPath = null)
    {
        var rootPath = string.IsNullOrWhiteSpace(storageRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearVision")
            : storageRootPath;

        Directory.CreateDirectory(rootPath);
        _storagePath = Path.Combine(rootPath, "clarification_memory.json");
        LoadFromDisk();
    }

    public string? Get(string field, string? sessionId = null, string? templateId = null, string? lineId = null)
    {
        // Priority: session > template > line > global
        var candidates = GetAll(field, sessionId, templateId, lineId);

        // Prefer most specific scope
        var byScope = candidates
            .OrderBy(e => e.Scope switch
            {
                MemoryScope.Session => 0,
                MemoryScope.Template => 1,
                MemoryScope.Line => 2,
                _ => 3
            })
            .ThenByDescending(e => e.SavedAtUtc);

        return byScope.FirstOrDefault()?.Value;
    }

    public IReadOnlyList<ClarificationMemoryEntry> GetAll(
        string field, string? sessionId = null, string? templateId = null, string? lineId = null)
    {
        var key = NormalizeKey(field);
        return _entries.Values
            .Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase) &&
                        (sessionId == null || string.Equals(e.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)) &&
                        (templateId == null || string.Equals(e.TemplateId, templateId, StringComparison.OrdinalIgnoreCase)) &&
                        (lineId == null || string.Equals(e.LineId, lineId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public void Set(string field, string value, MemoryScope scope = MemoryScope.Session,
        string? sessionId = null, string? templateId = null, string? lineId = null)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
            return;

        var key = NormalizeKey(field);
        var compositeKey = BuildCompositeKey(key, scope, sessionId, templateId, lineId);

        var entry = new ClarificationMemoryEntry
        {
            Key = key,
            Value = value.Trim(),
            Scope = scope,
            SessionId = sessionId?.Trim(),
            TemplateId = templateId?.Trim(),
            LineId = lineId?.Trim(),
            SavedAtUtc = DateTime.UtcNow
        };

        _entries[compositeKey] = entry;
        PruneIfNeeded();
        PersistToDisk();
    }

    public void ApplyToBrief(Core.DTOs.AiRequirementBrief brief, string sessionId,
        string? templateId = null, string? lineId = null)
    {
        // Apply session-level memories to fill missing brief fields
        var fields = new (string Field, Action<string> Apply)[]
        {
            ("modelResource", v => brief.ModelResource = v),
            ("imageSource", v => brief.ImageSource = v),
            ("triggerMode", v => brief.TriggerMode = v),
            ("outputTarget", v => brief.OutputTarget = v),
            ("roiRequirement", v => brief.RoiRequirement = v),
            ("calibrationRequirement", v => brief.CalibrationRequirement = v),
            ("decisionRule", v => { if (string.IsNullOrWhiteSpace(brief.DecisionRule)) brief.DecisionRule = v; }),
        };

        foreach (var (field, apply) in fields)
        {
            var value = Get(field, sessionId, templateId, lineId);
            if (!string.IsNullOrWhiteSpace(value))
            {
                apply(value);
                brief.MissingFields.Remove(field);
            }
        }
    }

    public void ClearSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var keysToRemove = _entries
            .Where(kvp => string.Equals(kvp.Value.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _entries.TryRemove(key, out _);

        PersistToDisk();
    }

    private static string NormalizeKey(string field)
    {
        return (field ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string BuildCompositeKey(string key, MemoryScope scope,
        string? sessionId, string? templateId, string? lineId)
    {
        return $"{key}|{scope}|{sessionId ?? "*"}|{templateId ?? "*"}|{lineId ?? "*"}";
    }

    private void PruneIfNeeded()
    {
        if (_entries.Count <= MaxEntries)
            return;

        var toRemove = _entries.Values
            .OrderBy(e => e.SavedAtUtc)
            .Take(_entries.Count - MaxEntries)
            .Select(e => BuildCompositeKey(e.Key, e.Scope, e.SessionId, e.TemplateId, e.LineId))
            .ToList();

        foreach (var key in toRemove)
            _entries.TryRemove(key, out _);
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_storagePath))
            return;

        try
        {
            var json = File.ReadAllText(_storagePath);
            var entries = JsonSerializer.Deserialize<List<ClarificationMemoryEntry>>(json);
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                var compositeKey = BuildCompositeKey(
                    entry.Key, entry.Scope,
                    entry.SessionId, entry.TemplateId, entry.LineId);
                _entries[compositeKey] = entry;
            }
        }
        catch
        {
            // Ignore corrupted persistence file
        }
    }

    private void PersistToDisk()
    {
        lock (_persistLock)
        {
            try
            {
                var snapshot = _entries.Values
                    .OrderByDescending(e => e.SavedAtUtc)
                    .Take(MaxEntries)
                    .ToList();

                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
            catch
            {
                // Swallow to avoid interrupting request flow
            }
        }
    }
}
