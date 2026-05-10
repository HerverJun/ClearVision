using System.Collections.Concurrent;
using Acme.Product.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Infrastructure.Events;

public interface IEventStore
{
    long Append(Guid projectId, IInspectionEvent evt);
    IReadOnlyList<StoredInspectionEvent> GetEventsAfter(Guid projectId, long sequenceId);
    void Cleanup(Guid projectId);
}

public sealed record StoredInspectionEvent(long SequenceId, IInspectionEvent Event, DateTime StoredAt);

public sealed class InMemoryEventStoreOptions
{
    public int MaxEventsPerProject { get; set; } = 100;

    public int MaxProjects { get; set; } = 50;
}

public sealed class InMemoryEventStore : IEventStore, IDisposable
{
    private readonly ILogger<InMemoryEventStore> _logger;
    private readonly int _maxEventsPerProject;
    private readonly int _maxProjects;
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<StoredInspectionEvent>> _store = new();
    private readonly ConcurrentDictionary<IInspectionEvent, StoredInspectionEvent> _eventIndex =
        new(ReferenceEqualityComparer.Instance);
    private long _globalSequenceId;
    private long _droppedEventCount;
    private long _replayedEventCount;

    public InMemoryEventStore(ILogger<InMemoryEventStore> logger)
        : this(logger, Options.Create(new InMemoryEventStoreOptions()))
    {
    }

    public InMemoryEventStore(ILogger<InMemoryEventStore> logger, IOptions<InMemoryEventStoreOptions> options)
    {
        _logger = logger;
        var configured = options.Value;
        _maxEventsPerProject = Math.Clamp(configured.MaxEventsPerProject, 1, 10_000);
        _maxProjects = Math.Clamp(configured.MaxProjects, 1, 1_000);
    }

    public long DroppedEventCount => Volatile.Read(ref _droppedEventCount);

    public long ReplayedEventCount => Volatile.Read(ref _replayedEventCount);

    public long Append(Guid projectId, IInspectionEvent evt)
    {
        if (_eventIndex.TryGetValue(evt, out var existing))
        {
            return existing.SequenceId;
        }

        var queue = _store.GetOrAdd(projectId, _ => new ConcurrentQueue<StoredInspectionEvent>());
        var stored = new StoredInspectionEvent(
            Interlocked.Increment(ref _globalSequenceId),
            evt,
            DateTime.UtcNow);

        if (!_eventIndex.TryAdd(evt, stored))
        {
            return _eventIndex[evt].SequenceId;
        }

        queue.Enqueue(stored);

        while (queue.Count > _maxEventsPerProject && queue.TryDequeue(out var removed))
        {
            _eventIndex.TryRemove(removed.Event, out _);
            Interlocked.Increment(ref _droppedEventCount);
        }

        if (_store.Count > _maxProjects)
        {
            _ = Task.Run(CleanupOldProjects);
        }

        _logger.LogDebug(
            "[EventStore] Stored event {EventType} seq={SequenceId} project={ProjectId}",
            evt.GetType().Name,
            stored.SequenceId,
            projectId);

        return stored.SequenceId;
    }

    public IReadOnlyList<StoredInspectionEvent> GetEventsAfter(Guid projectId, long sequenceId)
    {
        if (!_store.TryGetValue(projectId, out var queue))
        {
            return Array.Empty<StoredInspectionEvent>();
        }

        var replay = queue
            .Where(e => e.SequenceId > sequenceId)
            .OrderBy(e => e.SequenceId)
            .ToList();
        Interlocked.Add(ref _replayedEventCount, replay.Count);
        return replay;
    }

    public void Cleanup(Guid projectId)
    {
        if (!_store.TryRemove(projectId, out var queue))
        {
            return;
        }

        while (queue.TryDequeue(out var stored))
        {
            _eventIndex.TryRemove(stored.Event, out _);
        }

        _logger.LogDebug("[EventStore] Cleaned project history {ProjectId}", projectId);
    }

    public void Dispose()
    {
        foreach (var projectId in _store.Keys.ToArray())
        {
            Cleanup(projectId);
        }
    }

    private void CleanupOldProjects()
    {
        try
        {
            var toRemove = _store
                .OrderBy(kvp => kvp.Value.LastOrDefault()?.StoredAt ?? DateTime.MinValue)
                .Take(Math.Max(0, _store.Count - _maxProjects / 2))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var projectId in toRemove)
            {
                Cleanup(projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EventStore] Cleanup failed");
        }
    }
}
