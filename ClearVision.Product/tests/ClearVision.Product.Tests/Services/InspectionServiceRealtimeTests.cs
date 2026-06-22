using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Metrics;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using ClearVision.Product.Tests.Runtime;

namespace ClearVision.Product.Tests.Services;

[Collection(RuntimeConcurrencyCollection.Name)]
public class InspectionServiceRealtimeTests
{
    [Fact]
    public async Task StopRealtimeInspectionAsync_WaitsForWorkerExit_AndReleasesRuntimeState()
    {
        var context = CreateContext();
        var projectId = Guid.NewGuid();

        await context.Service.StartRealtimeInspectionFlowAsync(
            projectId,
            new OperatorFlow("Realtime"),
            cameraId: null,
            CancellationToken.None);

        await WaitUntilAsync(
            () => context.Coordinator.GetState(projectId)?.Status == RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        await context.Service.StopRealtimeInspectionAsync(projectId);

        context.Coordinator.GetState(projectId).Should().BeNull();
        (await context.Worker.WaitForRunExitAsync(projectId, TimeSpan.FromMilliseconds(100))).Should().BeTrue();
    }

    [Fact]
    public async Task StopRealtimeInspectionAsync_AllowsSameProjectToStartAgain()
    {
        var context = CreateContext();
        var projectId = Guid.NewGuid();

        await context.Service.StartRealtimeInspectionFlowAsync(
            projectId,
            new OperatorFlow("Realtime"),
            cameraId: null,
            CancellationToken.None);

        await WaitUntilAsync(
            () => context.Coordinator.GetState(projectId)?.Status == RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        await context.Service.StopRealtimeInspectionAsync(projectId);

        await context.Service.StartRealtimeInspectionFlowAsync(
            projectId,
            new OperatorFlow("Realtime-Restart"),
            cameraId: null,
            CancellationToken.None);

        await WaitUntilAsync(
            () => context.Coordinator.GetState(projectId)?.Status is RuntimeStatus.Starting or RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        await context.Service.StopRealtimeInspectionAsync(projectId);
        context.Coordinator.GetState(projectId).Should().BeNull();
    }

    private static TestContext CreateContext()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => WaitForCancellationAsync(callInfo.ArgAt<CancellationToken>(3)));

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var resultChannelWriter = Substitute.For<IInspectionResultChannelWriter>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var configurationService = Substitute.For<IConfigurationService>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);
        configurationService.GetCurrent().Returns(new AppConfig());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => flowExecution);
        services.AddScoped(_ => imageAcquisition);
        services.AddScoped(_ => resultChannelWriter);
        services.AddScoped(_ => resultRepository);
        services.AddScoped(_ => projectRepository);
        var provider = services.BuildServiceProvider();

        var eventStore = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var eventBus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, eventStore);
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var analysisDataBuilder = new AnalysisDataBuilder();
        var worker = new InspectionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            coordinator,
            eventBus,
            NullLogger<InspectionWorker>.Instance,
            lifetime,
            new InspectionMetrics(),
            analysisDataBuilder);

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            analysisDataBuilder,
            NullLogger<InspectionService>.Instance);

        return new TestContext(provider, service, worker, coordinator, flowExecution);
    }

    private static async Task<FlowExecutionResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new FlowExecutionResult { IsSuccess = true };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt > timeout)
            {
                throw new TimeoutException("Condition was not met within the expected timeout.");
            }

            await Task.Delay(25);
        }
    }

    private sealed record TestContext(
        ServiceProvider Provider,
        InspectionService Service,
        InspectionWorker Worker,
        InspectionRuntimeCoordinator Coordinator,
        IFlowExecutionService FlowExecution);
}
