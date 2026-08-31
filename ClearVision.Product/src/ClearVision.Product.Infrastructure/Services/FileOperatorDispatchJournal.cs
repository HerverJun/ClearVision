using System.Text.Json;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Append-only, write-through production journal for side-effect dispatches.
/// Each line is a complete sanitized state transition, which makes a dispatched
/// operation recoverable as indeterminate after a process interruption.
/// </summary>
public sealed class FileOperatorDispatchJournal : IOperatorDispatchJournal
{
    private const string RestartFailureCode = "PROCESS_RESTART_OUTCOME_UNKNOWN";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] NewLine = [(byte)'\n'];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, OperatorDispatchJournalEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private bool _loaded;

    public FileOperatorDispatchJournal(string path, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A dispatch journal path is required.", nameof(path));
        }

        Path = System.IO.Path.GetFullPath(path);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Path { get; }

    public static string GetDefaultPath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException(
                "DISPATCH_JOURNAL_PATH_UNAVAILABLE: LocalApplicationData is required for the production dispatch journal.");
        }

        return System.IO.Path.Combine(localData, "ClearVision", "execution-dispatch-journal.jsonl");
    }

    public async ValueTask<OperatorDispatchJournalEntry> PrepareAsync(
        OperatorDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureLoadedLocked();
            cancellationToken.ThrowIfCancellationRequested();
            var now = _timeProvider.GetUtcNow();
            var entry = new OperatorDispatchJournalEntry(
                Guid.NewGuid(),
                identity.ProjectId,
                identity.SessionId,
                identity.FlowId,
                identity.RunId,
                identity.OperatorId,
                identity.Source,
                identity.RunMode,
                identity.Capabilities,
                identity.ResourceBindingFingerprint,
                OperatorDispatchStage.Prepared,
                OperatorDispatchOutcome.Pending,
                now,
                now);
            AppendLocked(entry);
            _entries.Add(entry.CorrelationId, entry);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<OperatorDispatchJournalEntry> MarkDispatchedAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            correlationId,
            OperatorDispatchStage.Prepared,
            OperatorDispatchOutcome.Pending,
            OperatorDispatchStage.Dispatched,
            OperatorDispatchOutcome.Pending,
            failureCode: null,
            cancellationToken);

    public ValueTask<OperatorDispatchJournalEntry> MarkPreparedFailedAsync(
        Guid correlationId,
        string failureCode,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            correlationId,
            OperatorDispatchStage.Prepared,
            OperatorDispatchOutcome.Pending,
            OperatorDispatchStage.Prepared,
            OperatorDispatchOutcome.Failed,
            NormalizeFailureCode(failureCode),
            cancellationToken);

    public ValueTask<OperatorDispatchJournalEntry> MarkIndeterminateAsync(
        Guid correlationId,
        string failureCode,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            correlationId,
            OperatorDispatchStage.Dispatched,
            OperatorDispatchOutcome.Pending,
            OperatorDispatchStage.Dispatched,
            OperatorDispatchOutcome.Indeterminate,
            NormalizeFailureCode(failureCode),
            cancellationToken);

    public ValueTask<OperatorDispatchJournalEntry> ConfirmAsync(
        Guid correlationId,
        OperatorDispatchOutcome outcome,
        string? failureCode = null,
        CancellationToken cancellationToken = default)
    {
        if (outcome is not (OperatorDispatchOutcome.Succeeded or OperatorDispatchOutcome.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "Confirmed dispatches require a succeeded or failed outcome.");
        }

        return TransitionAsync(
            correlationId,
            OperatorDispatchStage.Dispatched,
            OperatorDispatchOutcome.Pending,
            OperatorDispatchStage.Confirmed,
            outcome,
            outcome == OperatorDispatchOutcome.Failed
                ? NormalizeFailureCode(failureCode ?? "EXECUTOR_REPORTED_FAILURE")
                : null,
            cancellationToken);
    }

    public async ValueTask<OperatorDispatchJournalEntry?> TryGetAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureLoadedLocked();
            cancellationToken.ThrowIfCancellationRequested();
            return _entries.GetValueOrDefault(correlationId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<OperatorDispatchJournalEntry> TransitionAsync(
        Guid correlationId,
        OperatorDispatchStage expectedStage,
        OperatorDispatchOutcome expectedOutcome,
        OperatorDispatchStage nextStage,
        OperatorDispatchOutcome nextOutcome,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureLoadedLocked();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_entries.TryGetValue(correlationId, out var current))
            {
                throw new KeyNotFoundException($"Dispatch journal correlation '{correlationId:D}' was not found.");
            }

            if (current.Stage != expectedStage || current.Outcome != expectedOutcome)
            {
                throw new InvalidOperationException(
                    $"DISPATCH_JOURNAL_TRANSITION_INVALID: {current.Stage}/{current.Outcome} cannot transition to {nextStage}/{nextOutcome}.");
            }

            var updated = current with
            {
                Stage = nextStage,
                Outcome = nextOutcome,
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                FailureCode = failureCode
            };
            AppendLocked(updated);
            _entries[correlationId] = updated;
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded)
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("DISPATCH_JOURNAL_DIRECTORY_INVALID");
        }

        Directory.CreateDirectory(directory);
        if (File.Exists(Path))
        {
            var lines = File.ReadAllLines(Path);
            var lastNonEmptyLine = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
            for (var index = 0; index <= lastNonEmptyLine; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<OperatorDispatchJournalEntry>(line, JsonOptions)
                        ?? throw new JsonException("The journal record was empty.");
                    _entries[entry.CorrelationId] = entry;
                }
                catch (JsonException) when (index == lastNonEmptyLine)
                {
                    // A process can terminate between writing a record and its newline.
                    // The last partial record is ignored; corruption earlier in the file
                    // remains a fail-closed startup error.
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        $"DISPATCH_JOURNAL_CORRUPT: record {index + 1} could not be read.",
                        exception);
                }
            }
        }

        _loaded = true;
        var abandonedDispatches = _entries.Values
            .Where(entry =>
                entry.Stage == OperatorDispatchStage.Dispatched &&
                entry.Outcome == OperatorDispatchOutcome.Pending)
            .ToArray();
        foreach (var abandoned in abandonedDispatches)
        {
            var recovered = abandoned with
            {
                Outcome = OperatorDispatchOutcome.Indeterminate,
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                FailureCode = RestartFailureCode
            };
            AppendLocked(recovered);
            _entries[recovered.CorrelationId] = recovered;
        }
    }

    private void AppendLocked(OperatorDispatchJournalEntry entry)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        using var stream = new FileStream(
            Path,
            new FileStreamOptions
            {
                Mode = FileMode.Append,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = 4096,
                Options = FileOptions.WriteThrough
            });
        stream.Write(bytes);
        stream.Write(NewLine);
        stream.Flush(flushToDisk: true);
    }

    private static string NormalizeFailureCode(string failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("A non-empty controlled failure code is required.", nameof(failureCode));
        }

        var normalized = failureCode.Trim();
        if (normalized.Length > 128 || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
        {
            throw new ArgumentException(
                "Failure codes may contain only ASCII letters, digits, underscore, dash, and dot.",
                nameof(failureCode));
        }

        return normalized;
    }
}
