using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentBuildProjectionReconciliationService : IHostedService
{
    private readonly AgentRunEventStore _eventStore;
    private readonly IAgentRunEventStreamService _streamService;
    private readonly IServiceScopeFactory _scopeFactory;

    public VisionAgentBuildProjectionReconciliationService(
        AgentRunEventStore eventStore,
        IAgentRunEventStreamService streamService,
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildProjectionReconciliationService> logger)
    {
        _eventStore = eventStore;
        _streamService = streamService;
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return ReconcileAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var terminalRunIds = _eventStore.LoadEvents()
            .Where(IsTerminalEvent)
            .GroupBy(evt => evt.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(evt => evt.Sequence)
                .First()
                .RunId)
            .ToList();

        if (terminalRunIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        using var scope = _scopeFactory.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<IVisionAgentBuildTerminalProjector>();
        foreach (var runId in terminalRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var replay = _streamService.ReplayRaw(runId);
            if (replay == null ||
                VisionAgentRunKindResolver.Resolve(replay) != VisionAgentRunKind.Build)
            {
                continue;
            }

            projector.ProjectRecovered(replay);
        }

        return Task.CompletedTask;
    }

    private static bool IsTerminalEvent(AgentRunEvent evt)
    {
        return string.Equals(evt.EventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase);
    }
}
