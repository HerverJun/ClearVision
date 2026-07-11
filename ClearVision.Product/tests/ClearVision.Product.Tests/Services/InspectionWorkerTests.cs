using System.Collections.Concurrent;
using System.Reflection;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Continuous;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Metrics;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public class InspectionWorkerTests
{
    [Fact]
    public async Task StopAsync_CancelsRunningTask_AndPublishesStoppedState()
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
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            resultRepository,
            projectRepository);

        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var bus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, store);
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var analysisDataBuilder = new AnalysisDataBuilder();
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            coordinator,
            bus,
            NullLogger<InspectionWorker>.Instance,
            lifetime,
            new InspectionMetrics(),
            analysisDataBuilder);

        var stateChanges = new ConcurrentQueue<InspectionStateChangedEvent>();
        using var subscription = bus.Subscribe<InspectionStateChangedEvent>((evt, _) =>
        {
            stateChanges.Enqueue(evt);
            return Task.CompletedTask;
        });

        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None)).Should().Be(StartResult.Success);
        (await worker.TryStartRunAsync(projectId, sessionId, new OperatorFlow("Test"), null)).Should().BeTrue();

        await WaitUntilAsync(
            () => stateChanges.Any(evt => evt.NewState == "Running"),
            TimeSpan.FromSeconds(2));

        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        await WaitUntilAsync(
            () => stateChanges.Any(evt => evt.NewState == "Stopped"),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAsync_WhenStoppedEventSubscriberThrows_ShouldStillReleaseRunState()
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
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            resultRepository,
            projectRepository);

        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var bus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, store);
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            coordinator,
            bus,
            NullLogger<InspectionWorker>.Instance,
            lifetime,
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        var stateChanges = new ConcurrentQueue<InspectionStateChangedEvent>();
        using var captureSubscription = bus.Subscribe<InspectionStateChangedEvent>((evt, _) =>
        {
            stateChanges.Enqueue(evt);
            return Task.CompletedTask;
        });
        using var throwingSubscription = bus.Subscribe<InspectionStateChangedEvent>((evt, _) =>
            evt.NewState == "Stopped"
                ? Task.FromException(new InvalidOperationException("subscriber failed"))
                : Task.CompletedTask);

        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None)).Should().Be(StartResult.Success);
        (await worker.TryStartRunAsync(projectId, sessionId, new OperatorFlow("Test"), null)).Should().BeTrue();

        await WaitUntilAsync(
            () => stateChanges.Any(evt => evt.NewState == "Running"),
            TimeSpan.FromSeconds(2));

        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        (await worker.WaitForRunExitAsync(projectId, sessionId, TimeSpan.FromSeconds(2))).Should().BeTrue();
        coordinator.GetState(projectId)?.Status.Should().Be(RuntimeStatus.Stopped);
        await WaitUntilAsync(
            () => coordinator.GetState(projectId) == null,
            TimeSpan.FromSeconds(2));
        stateChanges.Should().Contain(evt => evt.NewState == "Stopped");
    }

    [Fact]
    public async Task TryStartRunAsync_ShouldRegisterRunBeforeBackgroundExecutionPublishesRunning()
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
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            resultRepository,
            projectRepository);

        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var bus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, store);
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            coordinator,
            bus,
            NullLogger<InspectionWorker>.Instance,
            lifetime,
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var runningObserved = false;
        using var runningSubscription = bus.Subscribe<InspectionStateChangedEvent>(async (evt, _) =>
        {
            if (evt.NewState != "Running")
            {
                return;
            }

            runningObserved = true;
            (await worker.WaitForRunExitAsync(projectId, sessionId, TimeSpan.FromMilliseconds(25)))
                .Should()
                .BeFalse("the run must already be registered before background execution publishes Running");
        });

        (await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None)).Should().Be(StartResult.Success);
        (await worker.TryStartRunAsync(projectId, sessionId, new OperatorFlow("Registered-Before-Running"), null)).Should().BeTrue();

        await WaitUntilAsync(() => runningObserved, TimeSpan.FromSeconds(2));
        (await coordinator.TryStopAsync(projectId, CancellationToken.None)).Should().BeTrue();
        (await worker.WaitForRunExitAsync(projectId, sessionId, TimeSpan.FromSeconds(2))).Should().BeTrue();
    }

    [Fact]
    public async Task WaitForRunExitAsync_WhenProjectHasReplacementSession_TreatsOriginalSessionAsExited()
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
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            resultRepository,
            projectRepository);

        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var bus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, store);
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var analysisDataBuilder = new AnalysisDataBuilder();
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            coordinator,
            bus,
            NullLogger<InspectionWorker>.Instance,
            lifetime,
            new InspectionMetrics(),
            analysisDataBuilder);

        var projectId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, firstSessionId, CancellationToken.None)).Should().Be(StartResult.Success);
        (await worker.TryStartRunAsync(projectId, firstSessionId, new OperatorFlow("First"), null)).Should().BeTrue();

        await WaitUntilAsync(
            () => coordinator.GetState(projectId)?.Status == RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        (await coordinator.TryStopAsync(projectId, CancellationToken.None)).Should().BeTrue();
        (await worker.WaitForRunExitAsync(projectId, firstSessionId, TimeSpan.FromSeconds(2))).Should().BeTrue();

        await WaitUntilAsync(
            () => coordinator.GetState(projectId) == null,
            TimeSpan.FromSeconds(2));

        (await coordinator.TryStartAsync(projectId, secondSessionId, CancellationToken.None)).Should().Be(StartResult.Success);
        (await worker.TryStartRunAsync(projectId, secondSessionId, new OperatorFlow("Second"), null)).Should().BeTrue();

        await WaitUntilAsync(
            () => coordinator.GetState(projectId)?.SessionId == secondSessionId
                && coordinator.GetState(projectId)?.Status == RuntimeStatus.Running,
            TimeSpan.FromSeconds(2));

        (await worker.WaitForRunExitAsync(projectId, firstSessionId, TimeSpan.FromMilliseconds(100))).Should().BeTrue();

        (await coordinator.TryStopAsync(projectId, CancellationToken.None)).Should().BeTrue();
        (await worker.WaitForRunExitAsync(projectId, secondSessionId, TimeSpan.FromSeconds(2))).Should().BeTrue();
    }

    [Fact]
    public async Task RunWithScopeAsync_WhenRegistrySessionIsReplaced_CurrentRunKeepsOldSessionAndNextRunGetsMigratedSession()
    {
        var registry = new ProjectVariableSessionRegistry();
        var sessionId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var flowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFlowToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ProjectVariableExecutionContext? capturedContext = null;

        var schema = CreateProjectVariableSchema(variableId, 1);
        var project = new Project("demo");
        var projectId = project.Id;
        project.UpdateGlobalVariables(schema);

        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ProjectVariableExecutionContext>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                capturedContext = callInfo.ArgAt<ProjectVariableExecutionContext>(2);
                flowStarted.SetResult();
                await allowFlowToFinish.Task.WaitAsync(callInfo.ArgAt<CancellationToken>(4));
                return new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 1,
                    OutputData = new Dictionary<string, object> { ["Status"] = "OK" }
                };
            });

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var resultChannelWriter = Substitute.For<IInspectionResultChannelWriter>();
        resultChannelWriter.WriteAsync(Arg.Any<InspectionResult>(), Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdAsync(projectId).Returns(project);
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            resultRepository,
            projectRepository,
            projectVariableSessionRegistry: registry);

        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var bus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, store);
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            coordinator,
            bus,
            NullLogger<InspectionWorker>.Instance,
            lifetime,
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        (await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None)).Should().Be(StartResult.Success);
        (await worker.TryStartRunAsync(projectId, sessionId, new OperatorFlow("Test"), null)).Should().BeTrue();
        await flowStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        capturedContext.Should().NotBeNull();
        registry.TryPublishSchemaAndPersist(projectId, CreateProjectVariableSchema(variableId, 5), out var newSession, out var publishError)
            .Should()
            .BeTrue(publishError);

        capturedContext!.Session.SetValue(variableId, 2L, ProjectVariableUpdatedBy.VariableWrite);
        capturedContext.Session.TryGetSnapshot(variableId, out var oldSnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(oldSnapshot.Value).Should().Be(2L);

        registry.GetOrCreate(projectId, CreateProjectVariableSchema(variableId, 5)).Should().BeSameAs(newSession);
        newSession.TryGetSnapshot(variableId, out var newSnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(newSnapshot.Value).Should().Be(1L);

        allowFlowToFinish.SetResult();
        (await coordinator.TryStopAsync(projectId, CancellationToken.None)).Should().BeTrue();
        (await worker.WaitForRunExitAsync(projectId, sessionId, TimeSpan.FromSeconds(10))).Should().BeTrue();
        coordinator.GetState(projectId)?.Status.Should().Be(RuntimeStatus.Stopped);
    }

    [Fact]
    public void IsFrameDrivenExecution_WithPipeDelimitedCameraSource_ShouldUseBindingTriggerMode()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-1",
                TriggerMode = "External"
            }
        });

        var services = new ServiceCollection();
        services.AddSingleton(cameraManager);
        using var serviceProvider = services.BuildServiceProvider();

        var flow = new OperatorFlow("FrameDrivenFlow");
        var acquisition = new Operator("Acquire", OperatorType.ImageAcquisition, 0, 0);
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Camera|相机"));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "cam-1"));
        flow.AddOperator(acquisition);

        InvokeIsFrameDrivenExecution(flow, null, serviceProvider).Should().BeTrue();
    }

    [Fact]
    public void IsBlockingSoftwareTriggerExecution_WithSerialPhotoelectricCamera_ShouldReturnTrue()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-serial",
                TriggerMode = "Software",
                SoftwareTriggerSource = "SerialPhotoelectric",
                SerialPhotoelectricPortName = "COM3"
            }
        });

        var services = new ServiceCollection();
        services.AddSingleton(cameraManager);
        using var serviceProvider = services.BuildServiceProvider();

        InvokeIsBlockingSoftwareTriggerExecution(new OperatorFlow("SerialTriggerFlow"), "cam-serial", serviceProvider)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsBlockingSoftwareTriggerExecution_WithFlowCameraOperator_ShouldUseBindingTriggerMode()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-flow-serial",
                TriggerMode = "Software",
                SoftwareTriggerSource = "SerialPhotoelectric",
                SerialPhotoelectricPortName = "COM5"
            }
        });

        var services = new ServiceCollection();
        services.AddSingleton(cameraManager);
        using var serviceProvider = services.BuildServiceProvider();

        var flow = new OperatorFlow("FlowCameraSerialTrigger");
        var acquisition = new Operator("Acquire", OperatorType.ImageAcquisition, 0, 0);
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Camera|相机"));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "cam-flow-serial"));
        flow.AddOperator(acquisition);

        InvokeIsBlockingSoftwareTriggerExecution(flow, null, serviceProvider).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteCycleAsync_WithCameraPreload_ShouldPassCancellationTokenToImageAcquisition()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 1,
                OutputData = new Dictionary<string, object>()
            });

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var observedToken = CancellationToken.None;
        imageAcquisition.AcquireFromCameraAsync("cam-ct", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                observedToken = callInfo.ArgAt<CancellationToken>(1);
                return Task.FromResult(new ImageDto
                {
                    Id = Guid.NewGuid(),
                    DataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })
                });
            });
        imageAcquisition.ReleaseImageAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            Substitute.For<IInspectionResultChannelWriter>(),
            Substitute.For<IInspectionResultRepository>(),
            Substitute.For<IProjectRepository>());

        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance),
            new InMemoryInspectionEventBus(
                NullLogger<InMemoryInspectionEventBus>.Instance,
                new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance)),
            NullLogger<InspectionWorker>.Instance,
            Substitute.For<IHostApplicationLifetime>(),
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        using var cts = new CancellationTokenSource();
        var result = await InvokeExecuteCycleAsync(
            worker,
            new OperatorFlow("TokenFlow"),
            "cam-ct",
            flowExecution,
            imageAcquisition,
            cts.Token);

        observedToken.Should().Be(cts.Token);
        result.Status.Should().Be(InspectionStatus.NotInspected);
        result.ErrorMessage.Should().BeNull();
        result.GetOutcome().Execution.Should().Be(ExecutionOutcome.Succeeded);
        result.GetOutcome().Decision.Should().Be(DecisionOutcome.Undetermined);
        await imageAcquisition.Received(1).AcquireFromCameraAsync("cam-ct", cts.Token);
    }

    [Fact]
    public async Task ExecuteCycleAsync_WithFileSourceFlow_ShouldSkipCameraPreload()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        Dictionary<string, object>? executedInputs = null;
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedInputs = callInfo.ArgAt<Dictionary<string, object>?>(1);
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 1,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var flow = new OperatorFlow("FileSourceFlow");
        var acquisition = new Operator("Acquire", OperatorType.ImageAcquisition, 0, 0);
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", @"C:\images\latest.png"));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "stale-camera"));
        flow.AddOperator(acquisition);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            Substitute.For<IInspectionResultChannelWriter>(),
            Substitute.For<IInspectionResultRepository>(),
            Substitute.For<IProjectRepository>());

        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance),
            new InMemoryInspectionEventBus(
                NullLogger<InMemoryInspectionEventBus>.Instance,
                new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance)),
            NullLogger<InspectionWorker>.Instance,
            Substitute.For<IHostApplicationLifetime>(),
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        await InvokeExecuteCycleAsync(
            worker,
            flow,
            "cam-stale",
            flowExecution,
            imageAcquisition,
            CancellationToken.None);

        executedInputs.Should().NotBeNull();
        executedInputs!.Should().NotContainKey("Image");
        _ = imageAcquisition.DidNotReceive().AcquireFromCameraAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteCycleAsync_WithNgOutputImage_ShouldPersistResultImage()
    {
        var outputImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 5, 6, 7, 8 };
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 1,
                OutputData = new Dictionary<string, object>
                {
                    ["JudgmentResult"] = "NG",
                    ["Image"] = outputImage
                }
            }));

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var imageCache = Substitute.For<IImageCacheRepository>();
        imageCache.AddAsync(Arg.Any<byte[]>(), Arg.Any<string>()).Returns(Task.FromResult(Guid.NewGuid()));
        var imagePersistence = Substitute.For<IInspectionImagePersistenceService>();
        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            Substitute.For<IInspectionResultChannelWriter>(),
            Substitute.For<IInspectionResultRepository>(),
            Substitute.For<IProjectRepository>());

        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance),
            new InMemoryInspectionEventBus(
                NullLogger<InMemoryInspectionEventBus>.Instance,
                new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance)),
            NullLogger<InspectionWorker>.Instance,
            Substitute.For<IHostApplicationLifetime>(),
            new InspectionMetrics(),
            imageCache,
            new AnalysisDataBuilder(),
            imagePersistence);

        var result = await InvokeExecuteCycleAsync(
            worker,
            CreateDecisionFlow("NgOutputImageFlow", "JudgmentResult"),
            null,
            flowExecution,
            imageAcquisition,
            CancellationToken.None);

        result.Status.Should().Be(InspectionStatus.NG);
        await imagePersistence.Received(1).PersistAsync(
            Arg.Is<InspectionResult>(item =>
                item.Status == InspectionStatus.NG &&
                item.OutputImage != null &&
                item.OutputImage.SequenceEqual(outputImage)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunRealtimeLoopAsync_WithDefaultRuntimeProtection_DoesNotStopAfterSixConsecutiveNg()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 1,
                OutputData = new Dictionary<string, object>
                {
                    ["Result"] = "NG"
                }
            }));

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var resultChannelWriter = Substitute.For<IInspectionResultChannelWriter>();
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig
        {
            Runtime = new RuntimeConfig
            {
                ApplyProtectionRules = true,
                StopOnConsecutiveNg = 0
            }
        });

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            Substitute.For<IInspectionResultRepository>(),
            Substitute.For<IProjectRepository>(),
            configurationService);

        var eventBus = new InMemoryInspectionEventBus(
            NullLogger<InMemoryInspectionEventBus>.Instance,
            new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance));
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance),
            eventBus,
            NullLogger<InspectionWorker>.Instance,
            Substitute.For<IHostApplicationLifetime>(),
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var resultCount = 0;
        using var subscription = eventBus.Subscribe<InspectionResultEvent>((_, _) =>
        {
            if (Interlocked.Increment(ref resultCount) >= 6)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        });

        await InvokeRunRealtimeLoopAsync(
            worker,
            CreateDecisionFlow("ConsecutiveNg", "Result"),
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            configurationService,
            cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        resultCount.Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task RunRealtimeLoopAsync_WhenFrameDrivenExecutionStops_ShouldReleaseIdleCameraStream()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 1,
                OutputData = new Dictionary<string, object>
                {
                    ["JudgmentResult"] = "OK"
                }
            }));

        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        imageAcquisition.AcquireFromCameraAsync("cam-release", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImageDto
            {
                Id = Guid.NewGuid(),
                DataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            }));
        imageAcquisition.ReleaseImageAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

        var resultChannelWriter = Substitute.For<IInspectionResultChannelWriter>();
        resultChannelWriter.WriteAsync(Arg.Any<InspectionResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig());
        var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
        streamCoordinator.ReleaseIdleStreamAsync("cam-release").Returns(Task.CompletedTask);

        using var serviceProvider = BuildScopedServices(
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            Substitute.For<IInspectionResultRepository>(),
            Substitute.For<IProjectRepository>(),
            configurationService);

        var eventBus = new InMemoryInspectionEventBus(
            NullLogger<InMemoryInspectionEventBus>.Instance,
            new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance));
        var worker = new InspectionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance),
            eventBus,
            NullLogger<InspectionWorker>.Instance,
            Substitute.For<IHostApplicationLifetime>(),
            new InspectionMetrics(),
            new AnalysisDataBuilder());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var subscription = eventBus.Subscribe<InspectionProgressEvent>((_, _) =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        });

        await InvokeRunRealtimeLoopAsync(
            worker,
            CreateDecisionFlow("FrameDrivenRelease", "JudgmentResult"),
            flowExecution,
            imageAcquisition,
            resultChannelWriter,
            configurationService,
            cts.Token,
            streamCoordinator,
            "cam-release").WaitAsync(TimeSpan.FromSeconds(5));

        await streamCoordinator.Received(1).ReleaseIdleStreamAsync("cam-release");
    }

    private static async Task<FlowExecutionResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new FlowExecutionResult { IsSuccess = true };
    }

    private static OperatorFlow CreateDecisionFlow(string name, string outputName)
    {
        var flow = new OperatorFlow(name);
        var op = new Operator(Guid.NewGuid(), "Decision", OperatorType.ResultOutput, 0, 0);
        flow.AddOperator(op);
        return flow.BindStringDecision(op, outputName);
    }

    private static ServiceProvider BuildScopedServices(
        IFlowExecutionService flowExecution,
        IImageAcquisitionService imageAcquisition,
        IInspectionResultChannelWriter resultChannelWriter,
        IInspectionResultRepository resultRepository,
        IProjectRepository projectRepository,
        IConfigurationService? configurationService = null,
        ProjectVariableSessionRegistry? projectVariableSessionRegistry = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => flowExecution);
        services.AddScoped(_ => imageAcquisition);
        services.AddScoped(_ => resultChannelWriter);
        services.AddScoped(_ => resultRepository);
        services.AddScoped(_ => projectRepository);
        if (configurationService != null)
        {
            services.AddScoped(_ => configurationService);
        }

        if (projectVariableSessionRegistry != null)
        {
            services.AddSingleton(projectVariableSessionRegistry);
        }

        return services.BuildServiceProvider();
    }

    private static ProjectGlobalVariableSchema CreateProjectVariableSchema(Guid variableId, long initialValue)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "counter",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = System.Text.Json.JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static bool InvokeIsFrameDrivenExecution(
        OperatorFlow flow,
        string? cameraId,
        IServiceProvider serviceProvider)
    {
        var method = typeof(InspectionWorker).GetMethod(
            "IsFrameDrivenExecution",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object?[] { flow, cameraId, serviceProvider })!;
    }

    private static bool InvokeIsBlockingSoftwareTriggerExecution(
        OperatorFlow flow,
        string? cameraId,
        IServiceProvider serviceProvider)
    {
        var method = typeof(InspectionWorker).GetMethod(
            "IsBlockingSoftwareTriggerExecution",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object?[] { flow, cameraId, serviceProvider })!;
    }

    private static async Task<InspectionResult> InvokeExecuteCycleAsync(
        InspectionWorker worker,
        OperatorFlow flow,
        string? cameraId,
        IFlowExecutionService flowExecution,
        IImageAcquisitionService imageAcquisition,
        CancellationToken cancellationToken)
    {
        var method = typeof(InspectionWorker).GetMethod(
            "ExecuteCycleAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = (Task<InspectionResult>)method!.Invoke(
            worker,
            new object?[]
            {
                Guid.NewGuid(),
                Guid.NewGuid(),
                flow,
                0L,
                cameraId,
                ContinuousInspectionMode.Disabled,
                null,
                flowExecution,
                imageAcquisition,
                null,
                null,
                null,
                cancellationToken
        })!;
        return await task;
    }

    private static Task InvokeRunRealtimeLoopAsync(
        InspectionWorker worker,
        OperatorFlow flow,
        IFlowExecutionService flowExecution,
        IImageAcquisitionService imageAcquisition,
        IInspectionResultChannelWriter resultChannelWriter,
        IConfigurationService configurationService,
        CancellationToken cancellationToken,
        ICameraFrameStreamCoordinator? streamCoordinator = null,
        string? cameraId = null,
        bool frameDrivenExecution = true)
    {
        var method = typeof(InspectionWorker).GetMethod(
            "RunRealtimeLoopAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        return (Task)method!.Invoke(
            worker,
            new object?[]
            {
                Guid.NewGuid(),
                Guid.NewGuid(),
                flow,
                0L,
                cameraId,
                ContinuousInspectionMode.Disabled,
                streamCoordinator,
                frameDrivenExecution,
                false,
                flowExecution,
                imageAcquisition,
                resultChannelWriter,
                configurationService,
                null,
                null,
                null,
                cancellationToken
            })!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > timeout)
            {
                throw new TimeoutException("Condition was not met within the expected timeout.");
            }

            await Task.Delay(25);
        }
    }
}
