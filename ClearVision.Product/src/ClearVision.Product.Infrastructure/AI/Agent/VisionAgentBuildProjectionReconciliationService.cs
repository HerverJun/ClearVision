using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentBuildProjectionReconciliationService : IHostedService
{
    private readonly AgentRunEventStore _eventStore;
    private readonly IAgentRunEventStreamService _streamService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildProjectionReconciliationService> _logger;

    public VisionAgentBuildProjectionReconciliationService(
        AgentRunEventStore eventStore,
        IAgentRunEventStreamService streamService,
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildProjectionReconciliationService> logger)
    {
        _eventStore = eventStore;
        _streamService = streamService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return ReconcileAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
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
                if (replay == null)
                {
                    continue;
                }

                projector.ProjectRecovered(replay);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                ex,
                "Vision Agent Build terminal projection reconciliation failed.");
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
