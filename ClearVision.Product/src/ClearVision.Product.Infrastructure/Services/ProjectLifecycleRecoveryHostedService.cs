using ClearVision.Product.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class ProjectLifecycleRecoveryHostedService : BackgroundService
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProjectLifecycleRecoveryHostedService> _logger;

    public ProjectLifecycleRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProjectLifecycleRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(RecoveryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RecoverOnceAsync(stoppingToken);
        }
    }

    private async Task RecoverOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<ProjectLifecycleCoordinator>();
            await coordinator.RunRecoveryAndRetentionAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project lifecycle recovery pass failed; durable operations remain queued.");
        }
    }
}
