using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationCommandExecutionJournalStore
{
    private const int MaxEntries = 512;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private readonly ILogger<StationCommandExecutionJournalStore> _logger;
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
    private readonly List<JournalEntry> _entries = [];

    public StationCommandExecutionJournalStore(
        IOptions<StationSyncOptions> options,
        ILogger<StationCommandExecutionJournalStore> logger)
    {
        _logger = logger;
        var directoryPath = string.IsNullOrWhiteSpace(options.Value.ResolvedSpoolDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation",
                "spool")
            : options.Value.ResolvedSpoolDirectory;
        Directory.CreateDirectory(directoryPath);
        _filePath = Path.Combine(directoryPath, "station-command-execution-journal.jsonl");
        Load();
    }

    public bool TryGetTerminalResult(StationCommandDto command, out StationCommandResultDto result)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_syncRoot)
        {
            TrimLocked(DateTimeOffset.UtcNow);
            var payloadHash = ComputeCommandPayloadHash(command);
            var entry = _entries
                .Where(item =>
                    string.Equals(item.CommandId, command.CommandId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.StationId, command.StationId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.PayloadSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CompletedAtUtc)
                .FirstOrDefault();

            if (entry?.Result == null || !IsTerminal(entry.Result.Status))
            {
                result = new StationCommandResultDto();
                return false;
            }

            result = Clone(entry.Result);
            result.StationId = command.StationId;
            result.CommandId = command.CommandId;
            return true;
        }
    }

    public void RecordTerminalResult(StationCommandDto command, StationCommandResultDto result)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        if (!IsTerminal(result.Status) || string.IsNullOrWhiteSpace(command.CommandId))
        {
            return;
        }

        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var clone = Clone(result);
            clone.CommandId = command.CommandId;
            clone.StationId = command.StationId;
            clone.ProgressPercent = Math.Clamp(clone.ProgressPercent, 0, 100);
            clone.ReportedAtUtc = clone.ReportedAtUtc == default ? now : clone.ReportedAtUtc;
            clone.CreatedAtUtc = clone.CreatedAtUtc == default ? clone.ReportedAtUtc : clone.CreatedAtUtc;
            clone.CompletedAtUtc ??= now;

            var payloadHash = ComputeCommandPayloadHash(command);
            var existingIndex = _entries.FindIndex(item =>
                string.Equals(item.CommandId, command.CommandId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.StationId, command.StationId, StringComparison.OrdinalIgnoreCase));
            var entry = new JournalEntry
            {
                CommandId = command.CommandId,
                StationId = command.StationId,
                CommandType = command.CommandType,
                PayloadSha256 = payloadHash,
                CompletedAtUtc = clone.CompletedAtUtc.Value,
                Result = clone
            };

            if (existingIndex >= 0)
            {
                _entries[existingIndex] = entry;
            }
            else
            {
                _entries.Add(entry);
            }

            TrimLocked(now);
            RewriteLocked();
        }
    }

    private void Load()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
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
                    var entry = JsonSerializer.Deserialize<JournalEntry>(line, _jsonOptions);
                    if (entry == null ||
                        string.IsNullOrWhiteSpace(entry.CommandId) ||
                        string.IsNullOrWhiteSpace(entry.StationId) ||
                        entry.Result == null ||
                        !IsTerminal(entry.Result.Status))
                    {
                        continue;
                    }

                    _entries.Add(entry);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Station command execution journal line.");
                }
            }

            TrimLocked(DateTimeOffset.UtcNow);
        }
    }

    private void TrimLocked(DateTimeOffset now)
    {
        var minCompletedAt = now - Retention;
        _entries.RemoveAll(item => item.CompletedAtUtc < minCompletedAt);
        if (_entries.Count <= MaxEntries)
        {
            return;
        }

        var trimmed = _entries
            .OrderByDescending(item => item.CompletedAtUtc)
            .Take(MaxEntries)
            .ToList();
        _entries.Clear();
        _entries.AddRange(trimmed);
    }

    private void RewriteLocked()
    {
        var tempPath = _filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            foreach (var entry in _entries.OrderBy(item => item.CompletedAtUtc))
            {
                writer.WriteLine(JsonSerializer.Serialize(entry, _jsonOptions));
            }
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static bool IsTerminal(StationCommandStatus status)
    {
        return status is StationCommandStatus.Succeeded
            or StationCommandStatus.Failed
            or StationCommandStatus.TimedOut
            or StationCommandStatus.Cancelled
            or StationCommandStatus.Rejected;
    }

    private static string ComputeCommandPayloadHash(StationCommandDto command)
    {
        var material = $"{command.CommandType}\n{command.PayloadJson ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private sealed class JournalEntry
    {
        public string CommandId { get; set; } = string.Empty;

        public string StationId { get; set; } = string.Empty;

        public StationCommandType CommandType { get; set; }

        public string PayloadSha256 { get; set; } = string.Empty;

        public DateTimeOffset CompletedAtUtc { get; set; }

        public StationCommandResultDto? Result { get; set; }
    }
}
