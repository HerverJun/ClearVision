using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Metrics;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
[Collection(RuntimeConcurrencyCollection.Name)]
public class InspectionServiceRealtimeTests
{
    [Fact]
    public async Task StopRealtimeInspectionAsync_WaitsForWorkerExit_AndReleasesRuntimeState()
    {
        var context = CreateContext();
        var projectId = Guid.NewGuid();
        var flow = CreateDecisionFlow("Realtime");

        await context.Service.StartRealtimeInspectionFlowAsync(
            projectId,
            flow,
            cameraId: null,
            authority: CreateDraftAuthority(flow),
            cancellationToken: CancellationToken.None);

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
        var firstFlow = CreateDecisionFlow("Realtime");

        await context.Service.StartRealtimeInspectionFlowAsync(
            projectId,
            firstFlow,
            cameraId: null,
            authority: CreateDraftAuthority(firstFlow),
            cancellationToken: CancellationToken.None);

        await WaitUntilAsync(
            () => context.Coordinator.GetState(projectId)?.Status == RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        await context.Service.StopRealtimeInspectionAsync(projectId);

        var restartFlow = CreateDecisionFlow("Realtime-Restart");
        await context.Service.StartRealtimeInspectionFlowAsync(
            projectId,
            restartFlow,
            cameraId: null,
            authority: CreateDraftAuthority(restartFlow),
            cancellationToken: CancellationToken.None);

        await WaitUntilAsync(
            () => context.Coordinator.GetState(projectId)?.Status is RuntimeStatus.Starting or RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        await context.Service.StopRealtimeInspectionAsync(projectId);
        context.Coordinator.GetState(projectId).Should().BeNull();
    }

    [Fact]
    public async Task StartRealtimeInspectionFlowAsync_WhenWorkerStartFailsAfterRequestCancellation_ShouldRollbackRuntimeState()
    {
        var cancellation = new CancellationTokenSource();
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = Substitute.For<IInspectionWorker>();
        worker.TryStartRunAsync(
                Arg.Any<Guid>(),
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<string?>(),
                Arg.Any<ExecutionSnapshot?>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(false);
            });
        var service = CreateService(coordinator, worker);
        var projectId = Guid.NewGuid();
        var flow = CreateDecisionFlow("Realtime-Start-Failure");

        var act = async () => await service.StartRealtimeInspectionFlowAsync(
            projectId,
            flow,
            cameraId: null,
            authority: CreateDraftAuthority(flow),
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("实时检测启动失败，请重试");
        await WaitUntilAsync(
            () => coordinator.GetState(projectId) == null,
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartRealtimeInspectionFlowAsync_LegacyInlineDraft_ShouldFailClosedBeforeWorkerDispatch()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = Substitute.For<IInspectionWorker>();
        var service = CreateService(coordinator, worker);
        var projectId = Guid.NewGuid();

        var act = () => service.StartRealtimeInspectionFlowAsync(
            projectId,
            CreateDecisionFlow("legacy-inline-draft"),
            cameraId: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ExecutionAdmissionService.ExecutionAdmissionRejectedException>()
            .WithMessage("*ADMISSION_DRAFT_REVISION_REQUIRED*");
        await worker.DidNotReceiveWithAnyArgs().TryStartRunAsync(
            default,
            default!,
            default,
            default);
        coordinator.GetState(projectId).Should().BeNull();
    }

    [Fact]
    public async Task StartRealtimeInspectionAsync_WithStoredSideEffectFlow_ShouldStartOfficiallyWithoutAdmissionBlock()
    {
        // Industrial side effects remain available through the authoritative stored-project source.
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = Substitute.For<IInspectionWorker>();
        worker.TryStartRunAsync(
                Arg.Any<Guid>(),
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<string?>(),
                Arg.Any<ExecutionSnapshot?>())
            .Returns(Task.FromResult(true));
        var projectId = Guid.NewGuid();
        var storedProject = new Project("stored-side-effect-realtime-project");
        storedProject.UpdateFlow(CreateSideEffectFlow(OperatorType.TextSave));
        var service = CreateService(coordinator, worker, storedProject);

        await service.StartRealtimeInspectionAsync(
            projectId,
            cameraId: null,
            authority: CreateStoredOperatorAuthority(),
            cancellationToken: CancellationToken.None);

        await worker.Received(1).TryStartRunAsync(
            Arg.Any<Guid>(),
            Arg.Is<ExecutionSnapshot>(snapshot =>
                snapshot.ProjectId == projectId &&
                snapshot.Source == ExecutionSnapshotSource.PersistedProject &&
                snapshot.Principal.IsOperator &&
                snapshot.CreateExecutionFlow().Operators.Any(op => op.Type == OperatorType.TextSave)),
            Arg.Any<string?>(),
            Arg.Any<ExecutionSnapshot?>());
    }

    [Fact]
    public async Task StartRealtimeInspectionFlowAsync_ShouldUseSameDecisionConfigurationInExecutionSnapshot()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = Substitute.For<IInspectionWorker>();
        ExecutionSnapshot? captured = null;
        worker.TryStartRunAsync(
                Arg.Any<Guid>(),
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<string?>(),
                Arg.Any<ExecutionSnapshot?>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<ExecutionSnapshot>(1);
                return Task.FromResult(true);
            });
        var service = CreateService(coordinator, worker);
        var projectId = Guid.NewGuid();
        var flow = CreateDecisionFlow("Realtime-Decision-Identity");

        await service.StartRealtimeInspectionFlowAsync(
            projectId,
            flow,
            cameraId: null,
            authority: CreateDraftAuthority(flow),
            cancellationToken: CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.DecisionConfigurationHash.Should().Be(
            ExecutionFlowIdentity.ComputeDecisionConfigurationHash(flow.DecisionConfiguration));
        captured.CreateExecutionFlow().DecisionConfiguration.Should().BeEquivalentTo(flow.DecisionConfiguration);
    }

    private static TestContext CreateContext()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ValidateSnapshot(Arg.Any<ExecutionSnapshot>()).Returns(new FlowValidationResult
        {
            IsValid = true
        });
        flowExecution.ValidateSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowValidationResult { IsValid = true }));
        flowExecution.ExecuteWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => WaitForCancellationAsync(callInfo.ArgAt<CancellationToken>(3)));

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var resultChannelWriter = Substitute.For<IInspectionResultChannelWriter>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var project = new Project("active-realtime-project");
        projectRepository.GetByIdFreshAsync(Arg.Any<Guid>()).Returns(project);
        projectRepository.GetWithFlowAsync(Arg.Any<Guid>()).Returns(project);
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

    private static InspectionService CreateService(
        IInspectionRuntimeCoordinator coordinator,
        IInspectionWorker worker,
        Project? project = null)
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        project ??= new Project("active-realtime-project");
        projectRepository.GetByIdFreshAsync(Arg.Any<Guid>()).Returns(project);
        projectRepository.GetWithFlowAsync(Arg.Any<Guid>()).Returns(project);
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig());

        return new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            new AnalysisDataBuilder(),
            NullLogger<InspectionService>.Instance);
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

    private static OperatorFlow CreateSideEffectFlow(OperatorType operatorType)
    {
        var flow = new OperatorFlow("side-effect-realtime-flow");
        var op = new Operator(Guid.NewGuid(), operatorType.ToString(), operatorType, 0, 0);
        flow.AddOperator(op);
        return flow.BindStringDecision(op);
    }

    private static OperatorFlow CreateDecisionFlow(string name)
    {
        var flow = new OperatorFlow(name);
        var op = new Operator(Guid.NewGuid(), "ResultJudgment", OperatorType.ResultJudgment, 0, 0);
        flow.AddOperator(op);
        return flow.BindStringDecision(op);
    }

    private static ExecutionRequestAuthority CreateDraftAuthority(OperatorFlow flow) => new(
        new ExecutionPrincipal("engineer-realtime-tests", "Realtime Tests", "Engineer", IsAuthenticated: true),
        expectedProjectRevision: 0,
        capabilityManifest: ExecutionCapabilityManifest.Derive(flow, isExplicit: true),
        confirmationId: $"confirmation-{Guid.NewGuid():N}",
        auditId: $"audit-{Guid.NewGuid():N}");

    private static ExecutionRequestAuthority CreateStoredOperatorAuthority() => new(
        new ExecutionPrincipal("operator-realtime-tests", "Realtime Operator", "Operator", IsAuthenticated: true));

    private sealed record TestContext(
        ServiceProvider Provider,
        InspectionService Service,
        InspectionWorker Worker,
        InspectionRuntimeCoordinator Coordinator,
        IFlowExecutionService FlowExecution);
}
