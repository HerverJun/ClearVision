using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// Durable dispatch stages for operators that can touch files, networks,
/// databases, process state, or industrial devices.
/// </summary>
public enum OperatorDispatchStage
{
    Prepared = 0,
    Dispatched = 1,
    Confirmed = 2
}

public enum OperatorDispatchOutcome
{
    Pending = 0,
    Indeterminate = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>
/// Authority identity persisted before a side-effecting executor is invoked.
/// Resource targets are represented only by a one-way fingerprint: raw paths,
/// URLs, connection strings, device targets, secrets, and tokens are never
/// journal fields.
/// </summary>
public sealed record OperatorDispatchIdentity(
    Guid ProjectId,
    Guid SessionId,
    Guid FlowId,
    Guid RunId,
    Guid OperatorId,
    ExecutionSnapshotSource Source,
    ExecutionRunMode RunMode,
    ExecutionSideEffect Capabilities,
    string ResourceBindingFingerprint)
{
    public static OperatorDispatchIdentity Capture(Operator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        var authority = ExecutionAuthorityContext.Current;
        return new OperatorDispatchIdentity(
            authority?.ProjectId ?? @operator.ProjectId,
            authority?.SessionId ?? Guid.Empty,
            authority?.FlowId ?? @operator.ProjectId,
            authority?.RunId ?? Guid.Empty,
            @operator.Id,
            authority?.Source ?? ExecutionSnapshotSource.Draft,
            authority?.RunMode ?? ExecutionRunMode.Debug,
            ExecutionSideEffectCatalog.GetCapabilities(@operator),
            CreateResourceBindingFingerprint(authority?.ResourceBindings));
    }

    public static string CreateResourceBindingFingerprint(
        IReadOnlyDictionary<string, string>? resourceBindings)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in (resourceBindings ?? new Dictionary<string, string>())
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AppendLengthPrefixed(hash, pair.Key);
            AppendLengthPrefixed(hash, pair.Value);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}

public sealed record OperatorDispatchJournalEntry(
    Guid CorrelationId,
    Guid ProjectId,
    Guid SessionId,
    Guid FlowId,
    Guid RunId,
    Guid OperatorId,
    ExecutionSnapshotSource Source,
    ExecutionRunMode RunMode,
    ExecutionSideEffect Capabilities,
    string ResourceBindingFingerprint,
    OperatorDispatchStage Stage,
    OperatorDispatchOutcome Outcome,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? FailureCode = null);

public interface IOperatorDispatchJournal
{
    ValueTask<OperatorDispatchJournalEntry> PrepareAsync(
        OperatorDispatchIdentity identity,
        CancellationToken cancellationToken = default);

    ValueTask<OperatorDispatchJournalEntry> MarkDispatchedAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);

    ValueTask<OperatorDispatchJournalEntry> MarkPreparedFailedAsync(
        Guid correlationId,
        string failureCode,
        CancellationToken cancellationToken = default);

    ValueTask<OperatorDispatchJournalEntry> MarkIndeterminateAsync(
        Guid correlationId,
        string failureCode,
        CancellationToken cancellationToken = default);

    ValueTask<OperatorDispatchJournalEntry> ConfirmAsync(
        Guid correlationId,
        OperatorDispatchOutcome outcome,
        string? failureCode = null,
        CancellationToken cancellationToken = default);

    ValueTask<OperatorDispatchJournalEntry?> TryGetAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic injectable journal for core-only hosts and unit tests. Product
/// registrations replace this with a durable implementation.
/// </summary>
public sealed class InMemoryOperatorDispatchJournal : IOperatorDispatchJournal
{
    private readonly ConcurrentDictionary<Guid, OperatorDispatchJournalEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryOperatorDispatchJournal(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyCollection<OperatorDispatchJournalEntry> Entries => _entries.Values.ToArray();

    public ValueTask<OperatorDispatchJournalEntry> PrepareAsync(
        OperatorDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
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
        if (!_entries.TryAdd(entry.CorrelationId, entry))
        {
            throw new InvalidOperationException("DISPATCH_JOURNAL_CORRELATION_COLLISION");
        }

        return ValueTask.FromResult(entry);
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

    public ValueTask<OperatorDispatchJournalEntry?> TryGetAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryGetValue(correlationId, out var entry);
        return ValueTask.FromResult(entry);
    }

    private ValueTask<OperatorDispatchJournalEntry> TransitionAsync(
        Guid correlationId,
        OperatorDispatchStage expectedStage,
        OperatorDispatchOutcome expectedOutcome,
        OperatorDispatchStage nextStage,
        OperatorDispatchOutcome nextOutcome,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
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
            if (_entries.TryUpdate(correlationId, updated, current))
            {
                return ValueTask.FromResult(updated);
            }
        }
    }

    internal static string NormalizeFailureCode(string failureCode)
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
