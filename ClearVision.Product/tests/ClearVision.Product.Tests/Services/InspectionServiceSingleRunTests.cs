using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public class InspectionServiceSingleRunTests
{
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
        OperatorFlow? executedFlow = null;
        Dictionary<string, object>? executedInputs = null;
        InspectionResult? persistedResult = null;

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<OperatorFlow>();
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
        persistedResult.CalibrationBundleId.Should().Be("bundle-single-run");
        persistedResult.SessionId.Should().NotBeNull();
        persistedResult.OutputDataJson.Should().Contain("Traceability");
        _ = projectRepository.DidNotReceive().GetWithFlowAsync(Arg.Any<Guid>());
        _ = flowStorage.DidNotReceive().LoadFlowJsonAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenDatabaseFlowIsEmpty_ShouldFallbackToFileFlow()
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

        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        flowStorage.LoadFlowJsonAsync(projectId).Returns(fileFlowJson);
        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<OperatorFlow>();
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

        await service.ExecuteSingleAsync(projectId, new byte[] { 9, 9, 9 }, flow: null);

        executedFlow.Should().NotBeNull();
        executedFlow!.Operators.Should().ContainSingle(operatorEntity => operatorEntity.Name == "file-flow");
        _ = flowStorage.Received(1).LoadFlowJsonAsync(projectId);
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenStoredFlowExists_ShouldPreferFileFlowOverDatabaseSnapshot()
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
        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        flowStorage.LoadFlowJsonAsync(projectId).Returns(fileFlowJson);
        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedFlow = callInfo.Arg<OperatorFlow>();
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
        executedFlow!.Operators.Should().ContainSingle(operatorEntity => operatorEntity.Name == "file-flow");
        executedFlow.Operators.Should().NotContain(operatorEntity => operatorEntity.Name == "db-flow");
        _ = projectRepository.Received(1).GetWithFlowAsync(projectId);
        _ = flowStorage.Received(1).LoadFlowJsonAsync(projectId);
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenJudgmentSignalMissing_ShouldFailClosed()
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

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

        result.Status.Should().Be(InspectionStatus.Error);
        result.ErrorMessage.Should().Be("MissingJudgmentSignal");

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.TryGetProperty("MissingJudgmentSignal", out var missingSignal).Should().BeTrue();
        missingSignal.GetBoolean().Should().BeTrue();
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

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
        result.ErrorMessage.Should().Contain("InvalidJudgmentType:IsOk");
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenWrappedResultContainsIsMatch_ShouldTreatAsOk()
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

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

        result.Status.Should().Be(InspectionStatus.OK);
        result.ErrorMessage.Should().BeNull();

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().Be("Result.IsMatch");
        doc.RootElement.GetProperty("MissingJudgmentSignal").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSingleAsync_WhenDataWrapperContainsIsAnomaly_ShouldTreatAsNg()
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

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

        result.Status.Should().Be(InspectionStatus.NG);

        using var doc = JsonDocument.Parse(result.OutputDataJson ?? "{}");
        doc.RootElement.GetProperty("JudgmentSource").GetString().Should().Be("Data.IsAnomaly");
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
        InspectionResult? persistedResult = null;
        InspectionResult? capturedEvidenceResult = null;

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
        InspectionResult? persistedResult = null;

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
        await resultRepository.Received(1).AddAsync(Arg.Is<InspectionResult>(item =>
            item.Status == InspectionStatus.Error &&
            item.ErrorMessage != null &&
            item.ErrorMessage.Contains("camera offline", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExecuteSingleAsync_WithCameraIdAndFileSourceFlow_ShouldSkipCameraPreAcquire()
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
        var explicitFlow = CreateImageAcquisitionFlow("File", @"C:\images\latest.png", "stale-camera");
        Dictionary<string, object>? executedInputs = null;

        flowExecution
            .ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executedInputs = callInfo.ArgAt<Dictionary<string, object>?>(1);
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
        executedInputs.Should().NotBeNull();
        executedInputs!.Should().NotContainKey("Image");
        _ = imageAcquisition.DidNotReceive().AcquireFromCameraAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
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

    private static OperatorFlow CreateFlow(string operatorName)
    {
        var flow = new OperatorFlow("test-flow");
        flow.AddOperator(new Operator(Guid.NewGuid(), operatorName, OperatorType.ResultOutput, 0, 0));
        return flow;
    }

    private static OperatorFlow CreateImageAcquisitionFlow(string sourceType, string filePath, string cameraId)
    {
        var flow = new OperatorFlow("file-source-flow");
        var acquisition = new Operator(Guid.NewGuid(), "Acquire", OperatorType.ImageAcquisition, 0, 0);
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", sourceType));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", filePath));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", cameraId));
        flow.AddOperator(acquisition);
        return flow;
    }

    private static string SerializeFlowDto(string operatorName)
    {
        var dto = new OperatorFlowDto
        {
            Name = "stored-flow",
            Operators = new List<OperatorDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = operatorName,
                    Type = OperatorType.ResultOutput,
                    X = 0,
                    Y = 0
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
