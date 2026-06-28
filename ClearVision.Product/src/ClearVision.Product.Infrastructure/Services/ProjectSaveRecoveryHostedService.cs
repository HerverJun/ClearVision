using ClearVision.Product.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class ProjectSaveRecoveryHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProjectSaveRecoveryHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<ProjectSaveCoordinator>();
        await coordinator.RunStartupRecoveryAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
