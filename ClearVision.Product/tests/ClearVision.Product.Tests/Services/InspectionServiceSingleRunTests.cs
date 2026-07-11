using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public class InspectionServiceSingleRunTests
{
    [Fact]
    public async Task HistoryReads_WhenProjectIsNotActive_ShouldReturnEmptyAndNotReadResults()
    {
        var projectId = Guid.NewGuid();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(Task.FromResult<Project?>(null));
        var service = new InspectionService(
            resultRepository,
            projectRepository,
            Substitute.For<IFlowExecutionService>(),
            Substitute.For<IImageAcquisitionService>(),
            Substitute.For<IConfigurationService>(),
            Substitute.For<IInspectionRuntimeCoordinator>(),
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            Substitute.For<IProjectFlowStorage>(),
            NullLogger<InspectionService>.Instance);

        var page = await service.GetInspectionHistoryAsync(projectId, null, null, null, null, 2, 25);
        var detail = await service.GetInspectionHistoryDetailAsync(projectId, Guid.NewGuid());
        var comparison = await service.CompareInspectionHistoryAsync(projectId, Guid.NewGuid(), Guid.NewGuid());
        var previous = await service.FindPreviousSuccessfulInspectionAsync(projectId, Guid.NewGuid());
        var statistics = await service.GetStatisticsAsync(projectId, null, null, null, null);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.PageIndex.Should().Be(2);
        page.PageSize.Should().Be(25);
        detail.Should().BeNull();
        comparison.Should().BeNull();
        previous.Should().BeNull();
        statistics.TotalCount.Should().Be(0);
        resultRepository.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithExplicitFlow_ShouldPreferClientFlow()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        OperatorFlow? executedFlow = null;
        Dictionary<string, object>? executedInputs = null;
        InspectionResult? persistedResult = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                executedInputs = callInfo.ArgAt<Dictionary<string, object>?>(1);
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 12,
                    OutputData = new Dictionary<string, object>
                    {
                        ["JudgmentResult"] = "OK",
                        ["CalibrationBundleId"] = "bundle-single-run"
                    }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo =>
            {
                persistedResult = callInfo.Arg<InspectionResult>();
                return Task.FromResult(persistedResult);
            });

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, explicitFlow);

        executedFlow.Should().NotBeNull();
        executedFlow!.Operators.Should().ContainSingle(operatorEntity => operatorEntity.Name == "client-flow");
        executedInputs.Should().NotBeNull();
        executedInputs!.Should().ContainKey("Image");
        persistedResult.Should().NotBeNull();
        persistedResult!.FlowVersionHash.Should().NotBeNullOrWhiteSpace();
        persistedResult.ExecutionSnapshotId.Should().NotBeNull();
        persistedResult.ExecutionSource.Should().Be(ExecutionSnapshotSource.Draft.ToString());
        persistedResult.ExecutionRunMode.Should().Be(ExecutionRunMode.FormalPrimary.ToString());
        persistedResult.DecisionConfigurationHash.Should().NotBeNullOrWhiteSpace();
        persistedResult.CalibrationBundleId.Should().Be("bundle-single-run");
        persistedResult.SessionId.Should().NotBeNull();
        persistedResult.OutputDataJson.Should().Contain("Traceability");
        await projectRepository.Received(1).GetByIdFreshAsync(projectId);
        _ = projectRepository.DidNotReceive().GetWithFlowAsync(Arg.Any<Guid>());
        _ = flowStorage.DidNotReceive().LoadFlowJsonAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithInlineFlowAndMissingProject_ShouldRejectWithoutPersistingResult()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var act = async () => await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, explicitFlow);

        await act.Should().ThrowAsync<ClearVision.Product.Core.Exceptions.ProjectNotFoundException>();
        await flowExecution.DidNotReceiveWithAnyArgs().ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await resultRepository.DidNotReceive().AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithoutDecisionBinding_ShouldRejectBeforeCoordinatorOrHistory()
    {
        var projectId = Guid.NewGuid();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var flow = new OperatorFlow("unbound-official-flow");
        flow.AddOperator(new Operator(Guid.NewGuid(), "Output", OperatorType.ResultOutput, 0, 0));
        var service = new InspectionService(
            resultRepository,
            projectRepository,
            Substitute.For<IFlowExecutionService>(),
            Substitute.For<IImageAcquisitionService>(),
            Substitute.For<IConfigurationService>(),
            coordinator,
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            Substitute.For<IProjectFlowStorage>(),
            NullLogger<InspectionService>.Instance);

        var act = async () => await service.ExecuteSingleAsync(projectId, new byte[] { 1 }, flow);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ADMISSION_DECISION_BINDING_REQUIRED:*");
        await coordinator.DidNotReceive().TryStartAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await resultRepository.DidNotReceive().AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithInlineFlowAndSoftDeletedProject_ShouldRejectWithoutPersistingResult()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        projectRepository.GetByIdFreshAsync(projectId).Returns((Project?)null);

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var act = async () => await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, CreateFlow("client-flow"));

        await act.Should().ThrowAsync<ClearVision.Product.Core.Exceptions.ProjectNotFoundException>();
        await flowExecution.DidNotReceiveWithAnyArgs().ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await resultRepository.DidNotReceive().AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithInlineSideEffectOperator_ShouldRunOfficiallyWithoutAdmissionBlock()
    {
        // 检测页“运行流程”属于正式运行：内联流程声明的 TextSave 等写盘算子必须放行，不再当预览副作用拦截。
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 10,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, CreateSideEffectFlow(OperatorType.TextSave));

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeNull();
        executedFlow!.Operators.Should().ContainSingle(op => op.Type == OperatorType.TextSave);
        await flowExecution.Received(1).ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await resultRepository.Received(1).AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithStoredSideEffectFlow_ShouldKeepProductionExecutionAllowed()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var project = new Project("stored-side-effect-project");
        project.UpdateFlow(CreateSideEffectFlow(OperatorType.TextSave));
        projectRepository.GetByIdFreshAsync(projectId).Returns(project);
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 8,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, flow: null);

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeNull();
        executedFlow!.Operators.Should().ContainSingle(op => op.Type == OperatorType.TextSave);
        await resultRepository.Received(1).AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenDatabaseFlowIsEmpty_ShouldRejectInsteadOfFallingBackToFileFlow()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var project = new Project("fallback-project");
        var fileFlowJson = SerializeFlowDto("file-flow");
        OperatorFlow? executedFlow = null;

        projectRepository.GetByIdFreshAsync(projectId).Returns(project);
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        flowStorage.LoadFlowJsonAsync(projectId).Returns(fileFlowJson);
        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 9,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var act = () => service.ExecuteSingleAsync(projectId, new byte[] { 9, 9, 9 }, flow: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not contain an executable flow*");
        executedFlow.Should().BeNull();
        _ = flowStorage.DidNotReceive().LoadFlowJsonAsync(projectId);
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenStoredFlowExists_ShouldUseDatabaseSnapshotWithoutReadingFileFlow()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var project = new Project("prefer-stored-flow");
        var databaseFlow = CreateFlow("db-flow");
        var fileFlowJson = SerializeFlowDto("file-flow");
        OperatorFlow? executedFlow = null;

        project.UpdateFlow(databaseFlow);
        projectRepository.GetByIdFreshAsync(projectId).Returns(project);
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        flowStorage.LoadFlowJsonAsync(projectId).Returns(fileFlowJson);
        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 7,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        await service.ExecuteSingleAsync(projectId, new byte[] { 5, 6, 7 }, flow: null);

        executedFlow.Should().NotBeNull();
        executedFlow!.Operators.Should().ContainSingle(operatorEntity => operatorEntity.Name == "db-flow");
        executedFlow.Operators.Should().NotContain(operatorEntity => operatorEntity.Name == "file-flow");
        _ = projectRepository.Received(1).GetWithFlowAsync(projectId);
        _ = flowStorage.DidNotReceive().LoadFlowJsonAsync(projectId);
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenJudgmentSignalMissing_ShouldReturnSucceededUndetermined()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 12,
                OutputData = new Dictionary<string, object>()
            }));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, explicitFlow);

        result.Status.Should().Be(InspectionStatus.NotInspected);
        result.ErrorMessage.Should().BeNull();
        result.GetOutcome().Execution.Should().Be(ExecutionOutcome.Succeeded);
        result.GetOutcome().Decision.Should().Be(DecisionOutcome.Undetermined);

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.TryGetProperty("MissingJudgmentSignal", out var missingSignal).Should().BeTrue();
        missingSignal.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenBoundResultOutputIsMissing_ShouldNotFallbackToResultJudgmentOk()
    {
        var result = await ExecuteResultSelectionInspectionAsync(
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-judgment",
                ["JudgmentResult"] = "OK"
            },
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-output",
                ["Payload"] = "business-only"
            });

        result.Status.Should().Be(InspectionStatus.NotInspected);
        result.ErrorMessage.Should().BeNull();

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("SelectedSource").GetString().Should().Be("result-output");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().Be("FinalDecisionBinding");
        doc.RootElement.GetProperty("StatusReason").GetString().Should().Be("DECISION_SIGNAL_MISSING");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenBoundResultOutputIsMissing_ShouldNotFallbackToResultJudgmentNg()
    {
        var result = await ExecuteResultSelectionInspectionAsync(
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-judgment",
                ["JudgmentResult"] = "NG"
            },
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-output",
                ["Payload"] = "business-only"
            });

        result.Status.Should().Be(InspectionStatus.NotInspected);
        result.ErrorMessage.Should().BeNull();

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("SelectedSource").GetString().Should().Be("result-output");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().Be("FinalDecisionBinding");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenResultOutputHasValidJudgment_ShouldPreferResultOutput()
    {
        var result = await ExecuteResultSelectionInspectionAsync(
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-judgment",
                ["JudgmentResult"] = "NG"
            },
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-output",
                ["Result"] = true
            });

        result.Status.Should().Be(InspectionStatus.OK);
        result.ErrorMessage.Should().BeNull();

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("SelectedSource").GetString().Should().Be("result-output");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().StartWith("FinalDecisionBinding:");
        doc.RootElement.GetProperty("StatusReason").GetString().Should().Be("DECISION_BOUND_VALUE_RESOLVED");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenResultOutputHasInvalidJudgment_ShouldNotFallbackToResultJudgment()
    {
        var result = await ExecuteResultSelectionInspectionAsync(
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-judgment",
                ["JudgmentResult"] = "OK"
            },
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-output",
                ["IsOk"] = "not-bool"
            });

        result.Status.Should().Be(InspectionStatus.Error);
        result.ErrorMessage.Should().Contain("not Boolean");

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("SelectedSource").GetString().Should().Be("result-output");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().StartWith("FinalDecisionBinding:");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenOnlyResultJudgmentExists_ShouldKeepUsingResultJudgment()
    {
        var result = await ExecuteResultSelectionInspectionAsync(
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-judgment",
                ["JudgmentResult"] = "OK"
            },
            resultOutputData: null);

        result.Status.Should().Be(InspectionStatus.OK);
        result.ErrorMessage.Should().BeNull();

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("SelectedSource").GetString().Should().Be("result-judgment");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().StartWith("FinalDecisionBinding:");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenResultOutputAndResultJudgmentBothMissingJudgment_ShouldKeepUndeterminedOutcome()
    {
        var result = await ExecuteResultSelectionInspectionAsync(
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-judgment",
                ["Payload"] = "judge-business-only"
            },
            new Dictionary<string, object>
            {
                ["SelectedSource"] = "result-output",
                ["Payload"] = "output-business-only"
            });

        result.Status.Should().Be(InspectionStatus.NotInspected);
        result.ErrorMessage.Should().BeNull();
        result.GetOutcome().Execution.Should().Be(ExecutionOutcome.Succeeded);
        result.GetOutcome().Decision.Should().Be(DecisionOutcome.Undetermined);

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("SelectedSource").GetString().Should().Be("result-output");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().Be("FinalDecisionBinding");
        doc.RootElement.GetProperty("StatusReason").GetString().Should().Be("DECISION_SIGNAL_MISSING");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenJudgmentFieldTypeInvalid_ShouldReturnError()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");
        explicitFlow.BindBooleanDecision(explicitFlow.Operators.Single(), "IsOk");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 9,
                OutputData = new Dictionary<string, object> { ["IsOk"] = "true" }
            }));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 9, 9, 9 }, explicitFlow);

        result.Status.Should().Be(InspectionStatus.Error);
        result.ErrorMessage.Should().Contain("not Boolean");
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenOnlyNestedLegacySignalExists_ShouldRemainUndetermined()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 15,
                OutputData = new Dictionary<string, object>
                {
                    ["Result"] = new Dictionary<string, object>
                    {
                        ["IsMatch"] = true,
                        ["Message"] = "Sequence matched."
                    }
                }
            }));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 7, 8, 9 }, explicitFlow);

        result.Status.Should().Be(InspectionStatus.NotInspected);
        result.ErrorMessage.Should().BeNull();

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().Be("FinalDecisionBinding");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenUnknownExplicitResultPrecedesNestedSignal_ShouldReturnInvalid()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");
        explicitFlow.BindStringDecision(explicitFlow.Operators.Single(), "Result");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 16,
                OutputData = new Dictionary<string, object>
                {
                    ["Result"] = "informational-text",
                    ["Data"] = new Dictionary<string, object>
                    {
                        ["IsAnomaly"] = true
                    }
                }
            }));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 3, 5 }, explicitFlow);

        result.Status.Should().Be(InspectionStatus.Error);
        result.GetOutcome().Decision.Should().Be(DecisionOutcome.Invalid);

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().StartWith("FinalDecisionBinding:");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithNgOutputImage_ShouldPersistResultImage()
    {
        var projectId = Guid.NewGuid();
        var outputImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var imagePersistence = Substitute.For<IInspectionImagePersistenceService>();
        var evidenceManifest = Substitute.For<IInspectionEvidenceManifestService>();
        var imageCache = Substitute.For<IImageCacheRepository>();
        var cachedImageId = Guid.NewGuid();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        InspectionResult? persistedResult = null;
        InspectionResult? capturedEvidenceResult = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 16,
                OutputData = new Dictionary<string, object>
                {
                    ["JudgmentResult"] = "NG",
                    ["Image"] = outputImage,
                    ["CalibrationBundleId"] = "bundle-followup"
                }
            }));
        imageCache.AddAsync(Arg.Any<byte[]>(), Arg.Any<string>()).Returns(Task.FromResult(cachedImageId));
        resultRepository
            .AddAsync(Arg.Do<InspectionResult>(item => persistedResult = item))
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));
        evidenceManifest
            .CaptureAsync(Arg.Do<InspectionResult>(item => capturedEvidenceResult = item), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            imageCache,
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance,
            imagePersistence,
            evidenceManifestService: evidenceManifest);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 3, 5 }, explicitFlow);

        result.Status.Should().Be(InspectionStatus.NG);
        result.OutputImage.Should().Equal(outputImage);
        result.ImageId.Should().Be(cachedImageId);
        result.CalibrationBundleId.Should().Be("bundle-followup");
        result.FlowVersionHash.Should().NotBeNullOrWhiteSpace();
        result.SessionId.Should().NotBeNull();
        result.OutputDataJson.Should().Contain("bundle-followup");

        persistedResult.Should().NotBeNull();
        persistedResult!.ProjectId.Should().Be(projectId);
        persistedResult.Id.Should().Be(result.Id);
        persistedResult.Status.Should().Be(InspectionStatus.NG);
        persistedResult.OutputImage.Should().BeNull();
        persistedResult.ImageId.Should().Be(cachedImageId);
        persistedResult.OutputDataJson.Should().Be(result.OutputDataJson);
        persistedResult.AnalysisDataJson.Should().Be(result.AnalysisDataJson);
        persistedResult.FlowVersionHash.Should().Be(result.FlowVersionHash);
        persistedResult.CalibrationBundleId.Should().Be("bundle-followup");
        persistedResult.SessionId.Should().Be(result.SessionId);

        capturedEvidenceResult.Should().BeSameAs(result);
        capturedEvidenceResult!.OutputImage.Should().Equal(outputImage);
        await imagePersistence.Received(1).PersistAsync(
            Arg.Is<InspectionResult>(item =>
                item.Status == InspectionStatus.NG &&
                item.OutputImage != null &&
                item.OutputImage.SequenceEqual(outputImage)),
            Arg.Any<CancellationToken>());
        await evidenceManifest.Received(1).CaptureAsync(
            Arg.Is<InspectionResult>(item =>
                ReferenceEquals(item, result) &&
                item.OutputImage != null &&
                item.OutputImage.SequenceEqual(outputImage)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenEvidenceCaptureFails_ShouldStillPersistSummaryOnlyResult()
    {
        var projectId = Guid.NewGuid();
        var outputImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 8, 9, 10, 11 };
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var imagePersistence = Substitute.For<IInspectionImagePersistenceService>();
        var evidenceManifest = Substitute.For<IInspectionEvidenceManifestService>();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        InspectionResult? persistedResult = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 16,
                OutputData = new Dictionary<string, object>
                {
                    ["JudgmentResult"] = "NG",
                    ["Image"] = outputImage,
                    ["CalibrationBundleId"] = "bundle-followup"
                }
            }));
        resultRepository
            .AddAsync(Arg.Do<InspectionResult>(item => persistedResult = item))
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));
        evidenceManifest
            .CaptureAsync(Arg.Any<InspectionResult>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("capture failed"));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance,
            imagePersistence,
            evidenceManifestService: evidenceManifest);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 3, 5 }, explicitFlow);

        result.OutputImage.Should().Equal(outputImage);
        persistedResult.Should().NotBeNull();
        persistedResult!.OutputImage.Should().BeNull();
        persistedResult.CalibrationBundleId.Should().Be("bundle-followup");
        await evidenceManifest.Received(1).CaptureAsync(
            Arg.Is<InspectionResult>(item =>
                item.OutputImage != null &&
                item.OutputImage.SequenceEqual(outputImage)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenLegacyImagePersistencePathIsInvalid_ShouldFallbackToLocalAppDataImages()
    {
        var projectId = Guid.NewGuid();
        var outputImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var imageCache = Substitute.For<IImageCacheRepository>();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        InspectionResult? result = null;

        configurationService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = "\0invalid-path",
                SavePolicy = "NgOnly"
            }
        });
        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 16,
                OutputData = new Dictionary<string, object>
                {
                    ["JudgmentResult"] = "NG",
                    ["Image"] = outputImage
                }
            }));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));
        imageCache
            .AddAsync(Arg.Any<byte[]>(), Arg.Any<string>())
            .Returns(Task.FromResult(Guid.Empty));
        var fallbackSnapshot = FallbackImageDirectorySnapshot.Capture();

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            imageCache,
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        try
        {
            result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 3, 5 }, explicitFlow);

            var savedPath = FindSavedImagePath(
                InspectionImagePersistencePaths.GetFallbackImageSaveRoot(),
                result,
                ".png");
            var savedBytes = await File.ReadAllBytesAsync(savedPath);

            Path.GetFileName(Path.GetDirectoryName(savedPath)).Should().Be("NG");
            savedBytes.Should().Equal(outputImage);
        }
        finally
        {
            if (result != null)
            {
                fallbackSnapshot.DeleteSavedFiles(result.ProjectId, result.Id);
            }
        }
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenFlowExecutionThrows_ShouldReturnPersistedErrorResult()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("client-flow");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FlowExecutionResult>(new InvalidOperationException("flow exploded")));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 2, 4, 6 }, explicitFlow);

        result.Status.Should().Be(InspectionStatus.Error);
        result.ErrorMessage.Should().Be("flow exploded");
        await resultRepository.Received(1).AddAsync(Arg.Is<InspectionResult>(item =>
            item.Status == InspectionStatus.Error &&
            item.ErrorMessage == "flow exploded"));
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenCameraAcquisitionThrows_ShouldReturnPersistedErrorResult()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateFlow("camera-flow");
        var project = new Project("active-inline-project");
        projectRepository.GetByIdFreshAsync(projectId).Returns(project);

        imageAcquisition
            .AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ImageDto>(new InvalidOperationException("camera offline")));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, "camera-1", explicitFlow);

        result.Status.Should().Be(InspectionStatus.Error);
        result.ErrorMessage.Should().Contain("camera offline");
        result.ExecutionSnapshotId.Should().NotBeNull();
        result.FlowVersionHash.Should().Be(ExecutionFlowIdentity.ComputeFlowHash(explicitFlow));
        result.ProjectPersistenceRevision.Should().Be(project.PersistenceRevision);
        result.DecisionConfigurationHash.Should().Be(
            ExecutionFlowIdentity.ComputeDecisionConfigurationHash(explicitFlow.DecisionConfiguration));
        result.ExecutionSource.Should().Be(ExecutionSnapshotSource.Draft.ToString());
        result.ExecutionRunMode.Should().Be(ExecutionRunMode.FormalPrimary.ToString());
        result.SessionId.Should().NotBeNull();
        await resultRepository.Received(1).AddAsync(Arg.Is<InspectionResult>(item =>
            item.Status == InspectionStatus.Error &&
            item.ExecutionSnapshotId == result.ExecutionSnapshotId &&
            item.FlowVersionHash == result.FlowVersionHash &&
            item.ProjectPersistenceRevision == result.ProjectPersistenceRevision &&
            item.DecisionConfigurationHash == result.DecisionConfigurationHash &&
            item.ExecutionSource == result.ExecutionSource &&
            item.ExecutionRunMode == result.ExecutionRunMode &&
            item.SessionId == result.SessionId &&
            item.ErrorMessage != null &&
            item.ErrorMessage.Contains("camera offline", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithCameraIdAndStoredResultOutputSaveToFileFlow_ShouldKeepProductionExecutionAllowed()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var storedFlow = CreateResultOutputSaveToFileFlow();
        var project = new Project("stored-camera-result-output-project");
        project.UpdateFlow(storedFlow);
        projectRepository.GetByIdFreshAsync(projectId).Returns(project);
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        imageAcquisition.AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCameraImageDto()));
        OperatorFlow? executedFlow = null;
        Dictionary<string, object>? executedInputs = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                executedInputs = callInfo.ArgAt<Dictionary<string, object>?>(1);
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 13,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, "camera-1", flow: null);

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeSameAs(storedFlow);
        ExecutionFlowIdentity.ComputeFlowHash(executedFlow!).Should().Be(ExecutionFlowIdentity.ComputeFlowHash(storedFlow));
        executedInputs.Should().NotBeNull();
        executedInputs!.Should().ContainKey("Image");
        await flowExecution.Received(1).ExecuteWithSnapshotAsync(
            Arg.Is<ExecutionSnapshot>(candidate => candidate.FlowHash == ExecutionFlowIdentity.ComputeFlowHash(storedFlow)),
            Arg.Any<Dictionary<string, object>?>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithCameraIdAndStoredImageAcquisitionCameraFlow_ShouldNotRunInlineAdmission()
    {
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var storedFlow = CreateImageAcquisitionFlow("Camera", string.Empty, "stored-camera-binding");
        var project = new Project("stored-camera-acquisition-project");
        project.UpdateFlow(storedFlow);
        projectRepository.GetByIdFreshAsync(projectId).Returns(project);
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        imageAcquisition.AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCameraImageDto()));
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 14,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, "camera-1", flow: null);

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeSameAs(storedFlow);
        ExecutionFlowIdentity.ComputeFlowHash(executedFlow!).Should().Be(ExecutionFlowIdentity.ComputeFlowHash(storedFlow));
        await imageAcquisition.Received(1).AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithCameraIdAndInlineTextSaveFlow_ShouldRunOfficiallyWithoutAdmissionBlock()
    {
        // 相机驱动的检测页正式运行：内联 TextSave 写盘算子放行，先采集相机再执行流程。
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        imageAcquisition.AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCameraImageDto()));
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 10,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(
            projectId,
            "camera-1",
            CreateSideEffectFlow(OperatorType.TextSave));

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeNull();
        executedFlow!.Operators.Should().ContainSingle(op => op.Type == OperatorType.TextSave);
        await flowExecution.Received(1).ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await imageAcquisition.Received(1).AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>());
        await resultRepository.Received(1).AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithCameraIdAndStoredGlobalVariables_ShouldPassProjectVariableContext()
    {
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var storedFlow = CreateFlow("stored-global-variable-flow");
        var schema = CreateGlobalVariableSchema(variableId);
        var project = new Project("stored-camera-global-variable-project");
        project.UpdateFlow(storedFlow);
        project.UpdateGlobalVariables(schema);
        projectRepository.GetByIdFreshAsync(projectId).Returns(project);
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        imageAcquisition.AcquireFromCameraAsync("camera-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCameraImageDto()));
        ProjectVariableExecutionContext? capturedContext = null;

        flowExecution
            .ExecuteWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ProjectVariableExecutionContext>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<ProjectVariableExecutionContext>();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 15,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance,
            projectVariableSessions: new ProjectVariableSessionRegistry());

        var result = await service.ExecuteSingleAsync(projectId, "camera-1", flow: null);

        result.Status.Should().Be(InspectionStatus.OK);
        capturedContext.Should().NotBeNull();
        capturedContext!.Session.Schema.Variables.Should().ContainSingle(variable => variable.Id == variableId);
        await flowExecution.Received(1).ExecuteWithSnapshotAsync(
            Arg.Is<ExecutionSnapshot>(snapshot => snapshot.FlowHash == ExecutionFlowIdentity.ComputeFlowHash(storedFlow)),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<ProjectVariableExecutionContext>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithCameraIdAndFileSourceInlineFlow_ShouldRunFromFileWithoutAdmissionBlock()
    {
        // 复现并修复缺陷：检测页文件样张流程正式运行时，ImageAcquisition FilePath 不再被准入拦截。
        // File 图源会跳过相机预采集，直接由算子读本地文件执行。
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var explicitFlow = CreateImageAcquisitionFlow("File", @"C:\images\latest.png", "stale-camera");
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 11,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, "camera-1", explicitFlow);

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeSameAs(explicitFlow);
        ExecutionFlowIdentity.ComputeFlowHash(executedFlow!).Should().Be(ExecutionFlowIdentity.ComputeFlowHash(explicitFlow));
        await flowExecution.Received(1).ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        // File 图源跳过相机预采集。
        _ = imageAcquisition.DidNotReceive().AcquireFromCameraAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await resultRepository.Received(1).AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithInlineFileImageAcquisitionImageSaveAndResultOutputFlow_ShouldRunOfficially()
    {
        // 检测页文件样张流程正式运行：ImageAcquisition(File) + ImageSave + ResultOutput(SaveToFile) 全链路放行，可跑到写盘算子。
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        var inlineFlow = CreateDetectionRunFlow(
            (OperatorType.ImageAcquisition, [("SourceType", "File"), ("FilePath", @"C:\images\sample.png")]),
            (OperatorType.ImageSave, []),
            (OperatorType.ResultOutput, [("SaveToFile", true)]));
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 12,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, inlineFlow);

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeSameAs(inlineFlow);
        ExecutionFlowIdentity.ComputeFlowHash(executedFlow!).Should().Be(ExecutionFlowIdentity.ComputeFlowHash(inlineFlow));
        executedFlow!.Operators.Select(op => op.Type).Should().Contain(new[]
        {
            OperatorType.ImageAcquisition, OperatorType.ImageSave, OperatorType.ResultOutput
        });
        await resultRepository.Received(1).AddAsync(Arg.Any<InspectionResult>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithInlineTcpAndPlcFlow_ShouldRunOfficiallyWithoutTouchingDevices()
    {
        // 检测页正式运行：TCP/PLC 通讯算子在准入层放行；算子被 mock，不真实连设备。
        var projectId = Guid.NewGuid();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var imageAcquisition = Substitute.For<IImageAcquisitionService>();
        var configurationService = Substitute.For<IConfigurationService>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var worker = Substitute.For<IInspectionWorker>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        var inlineFlow = CreateDetectionRunFlow(
            (OperatorType.TcpCommunication, []),
            (OperatorType.SiemensS7Communication, []));
        OperatorFlow? executedFlow = null;

        flowExecution
            .ExecuteWithSnapshotAsync(Arg.Any<ExecutionSnapshot>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<ExecutionSnapshot>().CreateExecutionFlow();
                return Task.FromResult(new FlowExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 12,
                    OutputData = new Dictionary<string, object> { ["JudgmentResult"] = "OK" }
                });
            });
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            imageAcquisition,
            configurationService,
            coordinator,
            worker,
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance);

        var result = await service.ExecuteSingleAsync(projectId, new byte[] { 1, 2, 3 }, inlineFlow);

        result.Status.Should().Be(InspectionStatus.OK);
        executedFlow.Should().NotBeSameAs(inlineFlow);
        ExecutionFlowIdentity.ComputeFlowHash(executedFlow!).Should().Be(ExecutionFlowIdentity.ComputeFlowHash(inlineFlow));
        await flowExecution.Received(1).ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await resultRepository.Received(1).AddAsync(Arg.Any<InspectionResult>());
    }

    private static OperatorFlow CreateDetectionRunFlow(
        params (OperatorType Type, (string Name, object Value)[] Parameters)[] operators)
    {
        var flow = new OperatorFlow("detection-run-flow");
        foreach (var (type, parameters) in operators)
        {
            var op = new Operator(Guid.NewGuid(), type.ToString(), type, 0, 0);
            foreach (var (name, value) in parameters)
            {
                var dataType = value switch
                {
                    bool => "bool",
                    int => "int",
                    _ => "string"
                };
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value));
            }

            flow.AddOperator(op);
        }

        return flow.BindStringDecision(flow.Operators.Last());
    }

    private static string FindSavedImagePath(string root, InspectionResult result, string extension)
    {
        Directory.Exists(root).Should().BeTrue();

        return Directory
            .EnumerateFiles(root, $"{result.ProjectId:N}_{result.Id:N}_*{extension}", SearchOption.AllDirectories)
            .Should()
            .ContainSingle()
            .Subject;
    }

    private static async Task<InspectionResult> ExecuteResultSelectionInspectionAsync(
        Dictionary<string, object> resultJudgmentData,
        Dictionary<string, object>? resultOutputData)
    {
        var projectId = Guid.NewGuid();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        var executors = new List<IOperatorExecutor>
        {
            CreateStaticOutputExecutor(OperatorType.ResultJudgment, resultJudgmentData)
        };

        if (resultOutputData != null)
        {
            executors.Add(CreateStaticOutputExecutor(OperatorType.ResultOutput, resultOutputData));
        }

        using var flowExecution = new FlowExecutionService(
            executors,
            NullLogger<FlowExecutionService>.Instance,
            Substitute.For<IVariableContext>());

        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("active-inline-project"));
        coordinator
            .TryStartAsync(projectId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(StartResult.Success));
        resultRepository
            .AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            new GovernedFlowExecutionService(flowExecution),
            Substitute.For<IImageAcquisitionService>(),
            Substitute.For<IConfigurationService>(),
            coordinator,
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            Substitute.For<IProjectFlowStorage>(),
            NullLogger<InspectionService>.Instance);

        return await service.ExecuteSingleAsync(
            projectId,
            new byte[] { 1, 2, 3 },
            CreateResultSelectionFlow(resultOutputData));
    }

    private static IOperatorExecutor CreateStaticOutputExecutor(
        OperatorType operatorType,
        IReadOnlyDictionary<string, object> outputData)
    {
        var executor = Substitute.For<IOperatorExecutor>();
        executor.OperatorType.Returns(operatorType);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        executor
            .ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(OperatorExecutionOutput.Success(
                new Dictionary<string, object>(outputData, StringComparer.OrdinalIgnoreCase),
                executionTimeMs: 1)));

        return executor;
    }

    private static OperatorFlow CreateResultSelectionFlow(Dictionary<string, object>? resultOutputData)
    {
        var flow = new OperatorFlow("result-selection-flow");
        var judgment = CreateResultSelectionOperator("Judge", OperatorType.ResultJudgment);
        flow.AddOperator(judgment);

        if (resultOutputData == null)
        {
            return flow.BindStringDecision(judgment, "JudgmentResult");
        }

        var output = CreateResultSelectionOperator("Output", OperatorType.ResultOutput);
        flow.AddOperator(output);
        flow.AddConnection(new OperatorConnection(
            judgment.Id,
            judgment.OutputPorts.Single().Id,
            output.Id,
            output.InputPorts.Single().Id));
        if (resultOutputData?.TryGetValue("Result", out var resultValue) == true && resultValue is bool)
        {
            return flow.BindBooleanDecision(output, "Result");
        }
        if (resultOutputData?.ContainsKey("IsOk") == true)
        {
            return flow.BindBooleanDecision(output, "IsOk");
        }
        return flow.BindStringDecision(output, "JudgmentResult");
    }

    private static Operator CreateResultSelectionOperator(string name, OperatorType operatorType)
    {
        var op = new Operator(Guid.NewGuid(), name, operatorType, 0, 0);
        op.AddInputPort("Input", PortDataType.Any, isRequired: false);
        op.AddOutputPort("Output", PortDataType.Any);
        return op;
    }

    private static OperatorFlow CreateFlow(string operatorName)
    {
        var flow = new OperatorFlow("test-flow");
        var op = new Operator(Guid.NewGuid(), operatorName, OperatorType.ResultOutput, 0, 0);
        flow.AddOperator(op);
        return flow.BindStringDecision(op);
    }

    private static OperatorFlow CreateResultOutputSaveToFileFlow()
    {
        var flow = new OperatorFlow("result-output-save-to-file-flow");
        var output = new Operator(Guid.NewGuid(), "ResultOutput", OperatorType.ResultOutput, 0, 0);
        output.AddParameter(new Parameter(Guid.NewGuid(), "SaveToFile", "SaveToFile", string.Empty, "bool", true));
        flow.AddOperator(output);
        return flow.BindStringDecision(output);
    }

    private static OperatorFlow CreateSideEffectFlow(OperatorType operatorType)
    {
        var flow = new OperatorFlow("side-effect-flow");
        var op = new Operator(Guid.NewGuid(), operatorType.ToString(), operatorType, 0, 0);
        flow.AddOperator(op);
        return flow.BindStringDecision(op);
    }

    private static ImageDto CreateCameraImageDto()
    {
        return new ImageDto
        {
            Id = Guid.NewGuid(),
            DataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        };
    }

    private static ProjectGlobalVariableSchema CreateGlobalVariableSchema(Guid variableId)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "BatchCount",
                    DisplayName = "Batch Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(0L)
                }
            ]
        };
    }

    private static OperatorFlow CreateImageAcquisitionFlow(string sourceType, string filePath, string cameraId)
    {
        var flow = new OperatorFlow("file-source-flow");
        var acquisition = new Operator(Guid.NewGuid(), "Acquire", OperatorType.ImageAcquisition, 0, 0);
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", sourceType));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", filePath));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", cameraId));
        flow.AddOperator(acquisition);
        return flow.BindStringDecision(acquisition);
    }

    private static string SerializeFlowDto(string operatorName)
    {
        var operatorId = Guid.NewGuid();
        var outputPortId = Guid.NewGuid();
        var dto = new OperatorFlowDto
        {
            Name = "stored-flow",
            DecisionConfiguration = new ClearVision.Product.Core.Decisions.DecisionConfiguration
            {
                FinalDecisionBinding = new ClearVision.Product.Core.Decisions.FinalDecisionBinding
                {
                    SourceOperatorId = operatorId,
                    SourceOutputPortId = outputPortId,
                    SourceOutputName = "JudgmentResult",
                    DataType = ClearVision.Product.Core.Decisions.DecisionValueType.String,
                    Rule = ClearVision.Product.Core.Decisions.DecisionInterpretationRule.StringMap,
                    OkValue = "OK",
                    NgValue = "NG"
                }
            },
            Operators = new List<OperatorDto>
            {
                new()
                {
                    Id = operatorId,
                    Name = operatorName,
                    Type = OperatorType.ResultOutput,
                    X = 0,
                    Y = 0,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = outputPortId,
                            Name = "JudgmentResult",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.String
                        }
                    ]
                }
            }
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }
}
