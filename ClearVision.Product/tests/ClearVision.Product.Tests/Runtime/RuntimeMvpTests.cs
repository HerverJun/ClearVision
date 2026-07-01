using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Runtime;

public class RuntimeMvpTests
{
    private static readonly TimeSpan RuntimeHostAsyncSignalTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task PackageExporterAndLoader_ShouldRoundTripValidPackage()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("roundtrip")
            });

            Directory.Exists(export.PackageRootPath).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "package.json")).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "flow.json")).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "runtime-profile.json")).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "quality", "validation-report.json")).Should().BeTrue();

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);

            package.Manifest.PackageId.Should().Be(export.Manifest.PackageId);
            package.Manifest.FlowHash.Should().Be(export.Manifest.FlowHash);
            package.Flow.Operators.Should().HaveCount(1);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task PackageLoader_ShouldRejectFlowHashMismatch()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("invalid-hash"),
                TargetRootDirectory = root
            });

            var manifestPath = Path.Combine(export.PackageRootPath, "package.json");
            var manifest = JsonSerializer.Deserialize<RuntimePackageManifest>(
                await File.ReadAllTextAsync(manifestPath),
                CreateJsonOptions())!;
            manifest.FlowHash = "sha256:deadbeef";
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, CreateJsonOptions()));

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var act = async () => await loader.LoadAsync(export.PackageRootPath);

            await act.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*FlowHashMismatch*");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ShouldMatchInspectionServiceSingleRunOutcome()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectDto("consistency");
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var imageBytes = new byte[] { 2, 4, 6, 8 };
            var imagePath = Path.Combine(root, "input.png");
            await File.WriteAllBytesAsync(imagePath, imageBytes);

            var flowExecutionService = CreateFlowExecutionService(new DeterministicJudgmentExecutor());
            var inspectionService = CreateInspectionService(flowExecutionService);

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);

            var studioResult = await inspectionService.ExecuteSingleAsync(project.Id, imageBytes, project.Flow!.ToEntity());
            var stationResult = await runtimeHost.RunSingleAsync(imagePath);

            studioResult.Status.Should().Be(InspectionStatus.OK);
            stationResult.Outcome.Should().Be(RuntimeRunOutcome.Ok);
            stationResult.InspectionStatus.Should().Be(studioResult.Status);
            stationResult.PrimaryOutputs["JudgmentResult"]?.ToString().Should().Be("OK");
            stationResult.PrimaryOutputs["DecisionByte"]?.ToString().Should().Be("2");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_RunPackageConfiguredSingleAsync_ShouldUsePackageConfiguredInputs()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreatePackageConfiguredImageProject("package-configured");
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var flowExecutionService = CreateFlowExecutionService(
                new PackageConfiguredImageAcquisitionExecutor([4, 2, 1]),
                new DeterministicJudgmentExecutor());

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);

            var result = await runtimeHost.RunPackageConfiguredSingleAsync();

            result.Outcome.Should().Be(RuntimeRunOutcome.Ok);
            result.SourceImagePath.Should().BeNull();
            result.ImageId.Should().StartWith("package-configured-");
            result.PrimaryOutputs["DecisionByte"]?.ToString().Should().Be("4");
            result.SourceImageBytes.Should().BeNull();
            result.OutputImageBytes.Should().Equal([4, 2, 1]);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ProjectVariables_ShouldAllowIdleStationEditAndReset()
    {
        var root = CreateTempDirectory();
        try
        {
            var editableId = Guid.NewGuid();
            var lockedId = Guid.NewGuid();
            var project = CreateProjectDto("station-global-variable-edit");
            project.GlobalVariables = new ProjectGlobalVariableSchema
            {
                Variables =
                [
                    new ProjectGlobalVariableDefinition
                    {
                        Id = editableId,
                        Name = "judge.expected_count",
                        DisplayName = "Expected count",
                        ValueType = ProjectGlobalVariableValueType.Int64,
                        InitialValue = JsonSerializer.SerializeToElement(4L),
                        ManualWriteAllowed = true
                    },
                    new ProjectGlobalVariableDefinition
                    {
                        Id = lockedId,
                        Name = "stats.locked_count",
                        DisplayName = "Locked count",
                        ValueType = ProjectGlobalVariableValueType.Int64,
                        InitialValue = JsonSerializer.SerializeToElement(1L),
                        ManualWriteAllowed = false
                    }
                ]
            };
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);

            runtimeHost.GetProjectVariableSnapshots().Should().Contain(item =>
                item.VariableId == editableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(item.Value)) == 4L &&
                item.UpdatedBy == ProjectVariableUpdatedBy.Initial);

            var edited = await runtimeHost.SetProjectVariableValueAsync(editableId, 6L);
            Convert.ToInt64(ProjectVariableValueConverter.ToObject(edited.Value)).Should().Be(6L);
            edited.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StationManual);

            var reset = await runtimeHost.ResetProjectVariableAsync(editableId);
            Convert.ToInt64(ProjectVariableValueConverter.ToObject(reset.Value)).Should().Be(4L);
            reset.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.Reset);

            var lockedEdit = async () => await runtimeHost.SetProjectVariableValueAsync(lockedId, 2L);
            await lockedEdit.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*does not allow manual Station writes*");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ProjectVariables_ShouldPersistManualWritesAcrossHostReloads()
    {
        var root = CreateTempDirectory();
        try
        {
            var stateRoot = Path.Combine(root, "state");
            var variableId = Guid.NewGuid();
            var project = CreateProjectDto("station-global-variable-persist");
            project.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            await using (var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance,
                projectVariableStateStore: new JsonFileProjectVariableStateStore(stateRoot)))
            {
                await runtimeHost.LoadPackageAsync(export.PackageRootPath);
                await runtimeHost.SetProjectVariableValueAsync(variableId, 9L);
            }

            await using var reloadedHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance,
                projectVariableStateStore: new JsonFileProjectVariableStateStore(stateRoot));

            await reloadedHost.LoadPackageAsync(export.PackageRootPath);

            reloadedHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 9L);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ProjectVariables_WhenStateStoreSaveFails_ShouldKeepManualWriteOutOfMemory()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var project = CreateProjectDto("station-global-variable-save-failure");
            project.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });
            var stateStore = new FailingRuntimeProjectVariableStateStore();

            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance,
                projectVariableStateStore: stateStore);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            stateStore.FailSaves = true;

            var edit = async () => await runtimeHost.SetProjectVariableValueAsync(variableId, 9L);

            await edit.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*GV030*");
            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 4L &&
                snapshot.Version == 0);
            stateStore.SavedSnapshots.Should().BeEmpty();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ProjectVariables_WhenStationExpectedVersionIsStale_ShouldRejectAndKeepCurrentValue()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var project = CreateProjectDto("station-global-variable-stale-version");
            project.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            var initial = runtimeHost.GetProjectVariableSnapshots()
                .Single(snapshot => snapshot.VariableId == variableId);
            initial.Version.Should().Be(0);
            var edited = await runtimeHost.SetProjectVariableValueAsync(variableId, 6L, initial.Version);
            edited.Version.Should().Be(1);

            var staleEdit = async () => await runtimeHost.SetProjectVariableValueAsync(variableId, 9L, initial.Version);
            var staleReset = async () => await runtimeHost.ResetProjectVariableAsync(variableId, initial.Version);

            await staleEdit.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*GV025*changed from version 0 to 1*");
            await staleReset.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*GV025*changed from version 0 to 1*");
            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 6L &&
                snapshot.Version == 1);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ProjectVariables_ShouldRejectStationEditAndResetWhileRunning()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var project = CreateProjectDto("station-global-variable-running-guard");
            project.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);

            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var replayRoot = Path.Combine(root, "replay");
            Directory.CreateDirectory(replayRoot);
            await File.WriteAllBytesAsync(Path.Combine(replayRoot, "input.png"), new byte[] { 2, 4, 6, 8 });

            var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new BlockingResultOutputExecutor(started, release)),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await runtimeHost.StartFolderRunAsync(replayRoot);
            await started.Task.WaitAsync(RuntimeHostAsyncSignalTimeout);

            var edit = async () => await runtimeHost.SetProjectVariableValueAsync(variableId, 8L);
            var reset = async () => await runtimeHost.ResetProjectVariableAsync(variableId);

            await edit.Should().ThrowAsync<RuntimePackageException>();
            await reset.Should().ThrowAsync<RuntimePackageException>();

            release.TrySetResult(null);
            await WaitForStateAsync(runtimeHost, RuntimeHostState.Loaded, RuntimeHostAsyncSignalTimeout);
            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 4L);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_LoadPackageAsync_WhenReloadFails_ShouldKeepExistingPackageAndProjectVariableSession()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var project = CreateProjectDto("station-global-variable-reload-failure");
            project.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);

            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await runtimeHost.SetProjectVariableValueAsync(variableId, 9L);

            var invalidExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("station-global-variable-invalid-reload"),
                TargetRootDirectory = Path.Combine(root, "invalid")
            });
            var manifestPath = Path.Combine(invalidExport.PackageRootPath, "package.json");
            var manifest = JsonSerializer.Deserialize<RuntimePackageManifest>(
                await File.ReadAllTextAsync(manifestPath),
                CreateJsonOptions())!;
            manifest.FlowHash = "sha256:invalid";
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, CreateJsonOptions()));

            var reload = async () => await runtimeHost.LoadPackageAsync(invalidExport.PackageRootPath);
            await reload.Should().ThrowAsync<RuntimePackageException>();

            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Loaded);
            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 9L);

            var imagePath = Path.Combine(root, "old-package-input.png");
            await File.WriteAllBytesAsync(imagePath, new byte[] { 2, 4, 6, 8 });
            var runResult = await runtimeHost.RunSingleAsync(imagePath);
            runResult.Outcome.Should().Be(RuntimeRunOutcome.Ok);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_LoadPackageAsync_WhenWriterPreparationFails_ShouldRollbackToExistingPackage()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var project = CreateProjectDto("writer-rollback-old");
            project.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var oldExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = Path.Combine(root, "old")
            });
            var newExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("writer-rollback-new"),
                TargetRootDirectory = Path.Combine(root, "new")
            });

            var imageFactoryCalls = 0;
            var resultWriters = new List<TrackingRuntimeResultWriter>();
            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance,
                writerFactory: null,
                resultWriterFactory: (_, _) =>
                {
                    var writer = new TrackingRuntimeResultWriter();
                    resultWriters.Add(writer);
                    return writer;
                },
                imageWriterFactory: (_, _) =>
                {
                    imageFactoryCalls++;
                    if (imageFactoryCalls == 2)
                    {
                        throw new IOException("writer preparation failed");
                    }

                    return new TrackingRuntimeImageWriter();
                });

            await runtimeHost.LoadPackageAsync(oldExport.PackageRootPath);
            await runtimeHost.SetProjectVariableValueAsync(variableId, 9L);

            var reload = async () => await runtimeHost.LoadPackageAsync(newExport.PackageRootPath);

            await reload.Should().ThrowAsync<IOException>().WithMessage("*writer preparation failed*");
            resultWriters.Should().HaveCount(2);
            resultWriters[1].DisposeCount.Should().Be(1);
            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Loaded);
            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 9L);

            var imagePath = Path.Combine(root, "rollback-input.png");
            await File.WriteAllBytesAsync(imagePath, new byte[] { 2, 4, 6, 8 });
            var run = await runtimeHost.RunSingleAsync(imagePath);
            run.Outcome.Should().Be(RuntimeRunOutcome.Ok);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_LoadPackageAsync_WhenReloadSucceeds_ShouldPublishNewSession()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var oldProject = CreateProjectDto("reload-success-old");
            oldProject.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);
            var newProject = CreateProjectDto("reload-success-new");
            newProject.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 6L, manualWriteAllowed: true);
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var oldExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = oldProject,
                TargetRootDirectory = Path.Combine(root, "old")
            });
            var newExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = newProject,
                TargetRootDirectory = Path.Combine(root, "new")
            });

            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(oldExport.PackageRootPath);
            await runtimeHost.SetProjectVariableValueAsync(variableId, 9L);
            await runtimeHost.LoadPackageAsync(newExport.PackageRootPath);

            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 6L);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_LoadPackageAsync_WhenOldWriterDisposeFailsAfterCommit_ShouldKeepNewPackageLoaded()
    {
        var root = CreateTempDirectory();
        try
        {
            var variableId = Guid.NewGuid();
            var oldProject = CreateProjectDto("cleanup-old");
            oldProject.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 4L, manualWriteAllowed: true);
            var newProject = CreateProjectDto("cleanup-new");
            newProject.GlobalVariables = CreateSingleInt64GlobalVariableSchema(variableId, 6L, manualWriteAllowed: true);
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var oldExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = oldProject,
                TargetRootDirectory = Path.Combine(root, "old-cleanup")
            });
            var newExport = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = newProject,
                TargetRootDirectory = Path.Combine(root, "new-cleanup")
            });
            var resultWriters = new List<TrackingRuntimeResultWriter>();
            var imageWriters = new List<TrackingRuntimeImageWriter>();

            await using var runtimeHost = new RuntimeHost(
                CreateFlowExecutionService(new DeterministicJudgmentExecutor()),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance,
                writerFactory: null,
                resultWriterFactory: (_, _) =>
                {
                    var writer = new TrackingRuntimeResultWriter();
                    resultWriters.Add(writer);
                    return writer;
                },
                imageWriterFactory: (_, _) =>
                {
                    var writer = new TrackingRuntimeImageWriter();
                    imageWriters.Add(writer);
                    return writer;
                });

            await runtimeHost.LoadPackageAsync(oldExport.PackageRootPath);
            resultWriters[0].ThrowOnDispose = true;

            await runtimeHost.LoadPackageAsync(newExport.PackageRootPath);

            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Loaded);
            runtimeHost.GetSnapshot().PackageName.Should().Be(newProject.Name);
            runtimeHost.GetProjectVariableSnapshots().Should().Contain(snapshot =>
                snapshot.VariableId == variableId &&
                Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)) == 6L);
            resultWriters[0].DisposeCount.Should().Be(1);
            imageWriters[0].DisposeCount.Should().Be(1);

            var imagePath = Path.Combine(root, "new-package-input.png");
            await File.WriteAllBytesAsync(imagePath, new byte[] { 2, 4, 6, 8 });
            var run = await runtimeHost.RunSingleAsync(imagePath);
            run.Outcome.Should().Be(RuntimeRunOutcome.Ok);
            resultWriters[1].EnqueueCount.Should().BeGreaterThan(0);
            resultWriters[0].EnqueueCount.Should().Be(0);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_WhenImageSaveIsEnabledButNoImageExists_ShouldNotPublishSavedImagePath()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectDto("no-image-save-path");
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var flowExecutionService = CreateFlowExecutionService(new NoImageNgExecutor());
            var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);
            RuntimeNormalizedResult? published = null;
            runtimeHost.ResultAvailable += result => published = result;

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            var result = await runtimeHost.RunPackageConfiguredSingleAsync();
            await runtimeHost.DisposeAsync();

            result.Outcome.Should().Be(RuntimeRunOutcome.Ng);
            result.SavedImagePath.Should().BeNull();
            published.Should().NotBeNull();
            published!.SavedImagePath.Should().BeNull();
            FindRuntimeResultRecord(result).GetProperty("savedImagePath").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_StopAsync_ShouldCancelFolderReplayAndBeIdempotent()
    {
        var root = CreateTempDirectory();
        try
        {
            var replayRoot = Path.Combine(root, "replay");
            Directory.CreateDirectory(replayRoot);
            for (var index = 0; index < 5; index += 1)
            {
                await File.WriteAllBytesAsync(Path.Combine(replayRoot, $"input-{index}.png"), new byte[] { (byte)(index + 1), 1, 1 });
            }

            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("stop-replay"),
                TargetRootDirectory = root
            });

            var flowExecutionService = CreateFlowExecutionService(new DeterministicJudgmentExecutor(delayMs: 250));

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await runtimeHost.StartFolderRunAsync(replayRoot);
            await Task.Delay(100);

            var firstStop = await runtimeHost.StopAsync();
            var secondStop = await runtimeHost.StopAsync();

            firstStop.WasRunning.Should().BeTrue();
            firstStop.TimedOut.Should().BeFalse();
            secondStop.WasRunning.Should().BeFalse();
            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Loaded);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_StopAsync_WhenCancellationTokenIsCanceledAfterStopping_ShouldStillFinalize()
    {
        var root = CreateTempDirectory();
        try
        {
            var replayRoot = Path.Combine(root, "replay");
            Directory.CreateDirectory(replayRoot);
            await File.WriteAllBytesAsync(Path.Combine(replayRoot, "input-1.png"), new byte[] { 2, 4, 6, 8 });

            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("stop-cancel"),
                TargetRootDirectory = root
            });

            var profilePath = Path.Combine(export.PackageRootPath, "runtime-profile.json");
            var profile = JsonSerializer.Deserialize<RuntimeProfile>(
                await File.ReadAllTextAsync(profilePath),
                CreateJsonOptions())!;
            profile.StopTimeoutMs = 100;
            await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile, CreateJsonOptions()));

            var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var flowExecutionService = CreateFlowExecutionService(new BlockingResultOutputExecutor(started, release));

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await runtimeHost.StartFolderRunAsync(replayRoot);
            await started.Task.WaitAsync(RuntimeHostAsyncSignalTimeout);

            var stopCts = new CancellationTokenSource();
            var stopTask = runtimeHost.StopAsync(stopCts.Token);
            await WaitForStateAsync(runtimeHost, RuntimeHostState.Stopping, RuntimeHostAsyncSignalTimeout);
            stopCts.Cancel();

            var stopSummary = await stopTask.WaitAsync(RuntimeHostAsyncSignalTimeout);
            stopSummary.WasRunning.Should().BeTrue();
            stopSummary.TimedOut.Should().BeTrue();
            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Faulted);

            var reloadWhileOldRunIsAlive = async () => await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await reloadWhileOldRunIsAlive.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*运行引擎当前正忙*");

            release.TrySetResult(null);
            await WaitForStateAsync(runtimeHost, RuntimeHostState.Loaded, RuntimeHostAsyncSignalTimeout);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void ArchitectureGuard_ShouldKeepRuntimeAndStationFreeFromDesktopWebDependencies()
    {
        var repoRoot = FindRepositoryRoot();
        var runtimeAndStationFiles = new[]
        {
            Path.Combine(repoRoot, "ClearVision.Product", "src", "ClearVision.Product.Runtime"),
            Path.Combine(repoRoot, "ClearVision.Product", "src", "ClearVision.Product.Station")
        }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        runtimeAndStationFiles.Should().NotBeEmpty();

        var bannedPatterns = new[]
        {
            "Microsoft.Web.WebView2",
            "wwwroot",
            "Kestrel",
            "WebApplication",
            "MapVisionApiEndpoints",
            "ClearVision.Product.Desktop"
        };

        foreach (var path in runtimeAndStationFiles)
        {
            var content = File.ReadAllText(path);
            foreach (var bannedPattern in bannedPatterns)
            {
                content.Should().NotContain(
                    bannedPattern,
                    $"'{bannedPattern}' must not leak into {path}");
            }
        }
    }

    private static IFlowExecutionService CreateFlowExecutionService(params IOperatorExecutor[] executors)
    {
        return new FlowExecutionService(
            executors,
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext());
    }

    private sealed class FailingRuntimeProjectVariableStateStore : IProjectVariableStateStore
    {
        public bool FailSaves { get; set; }

        public IReadOnlyList<ProjectVariableValueSnapshot> SavedSnapshots { get; private set; } = [];

        public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema)
        {
            return SavedSnapshots.Select(CloneSnapshot).ToList();
        }

        public void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
        {
            if (FailSaves)
            {
                throw new IOException("simulated runtime state-store failure");
            }

            SavedSnapshots = snapshots.Select(CloneSnapshot).ToList();
        }

        public void Delete(string scopeId)
        {
            SavedSnapshots = [];
        }

        private static ProjectVariableValueSnapshot CloneSnapshot(ProjectVariableValueSnapshot snapshot)
        {
            return snapshot with { Value = snapshot.Value.Clone() };
        }
    }

    private static async Task WaitForStateAsync(RuntimeHost runtimeHost, RuntimeHostState state, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (runtimeHost.GetSnapshot().State == state)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"RuntimeHost did not reach state {state} within {timeout}.");
    }

    private static JsonElement FindRuntimeResultRecord(RuntimeNormalizedResult result)
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVisionStation");
        var runDate = result.CompletedAtUtc.LocalDateTime.ToString("yyyyMMdd");
        var resultFile = Path.Combine(dataRoot, "runs", runDate, "runtime-results.jsonl");

        File.Exists(resultFile).Should().BeTrue();

        foreach (var line in File.ReadLines(resultFile).Reverse())
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("runId", out var runId) &&
                string.Equals(runId.GetString(), result.RunId, StringComparison.Ordinal))
            {
                return document.RootElement.Clone();
            }
        }

        throw new InvalidOperationException($"Runtime result record not found for run {result.RunId}.");
    }

    private static InspectionService CreateInspectionService(IFlowExecutionService flowExecutionService)
    {
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        resultRepository.AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                SavePolicy = "None",
                ImageSavePath = Path.GetTempPath()
            }
        });

        return new InspectionService(
            resultRepository,
            Substitute.For<IProjectRepository>(),
            flowExecutionService,
            Substitute.For<IImageAcquisitionService>(),
            configurationService,
            Substitute.For<IInspectionRuntimeCoordinator>(),
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new ClearVision.Product.Application.Analysis.AnalysisDataBuilder(),
            Substitute.For<IProjectFlowStorage>(),
            NullLogger<InspectionService>.Instance);
    }

    private static ProjectDto CreateProjectDto(string name)
    {
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "runtime-flow",
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput,
                        X = 0,
                        Y = 0
                    }
                ]
            }
        };
    }

    private static ProjectDto CreatePackageConfiguredImageProject(string name)
    {
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPortId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var resultInputPortId = Guid.NewGuid();
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "package-configured-flow",
                Operators =
                [
                    new OperatorDto
                    {
                        Id = acquisitionId,
                        Name = "PackageConfiguredImage",
                        Type = OperatorType.ImageAcquisition,
                        OutputPorts =
                        [
                            new PortDto
                            {
                                Id = acquisitionOutputPortId,
                                Name = "Image",
                                DataType = PortDataType.Image,
                                IsRequired = true
                            }
                        ]
                    },
                    new OperatorDto
                    {
                        Id = resultId,
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput,
                        InputPorts =
                        [
                            new PortDto
                            {
                                Id = resultInputPortId,
                                Name = "Image",
                                DataType = PortDataType.Image,
                                IsRequired = false
                            }
                        ]
                    }
                ],
                Connections =
                [
                    new OperatorConnectionDto
                    {
                        Id = Guid.NewGuid(),
                        SourceOperatorId = acquisitionId,
                        SourcePortId = acquisitionOutputPortId,
                        TargetOperatorId = resultId,
                        TargetPortId = resultInputPortId
                    }
                ]
            }
        };
    }

    private static ProjectGlobalVariableSchema CreateSingleInt64GlobalVariableSchema(
        Guid variableId,
        long initialValue,
        bool manualWriteAllowed)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = manualWriteAllowed
                }
            ]
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            }
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ClearVision.Product", "ClearVision.Product.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class DeterministicJudgmentExecutor : IOperatorExecutor
    {
        private readonly int _delayMs;

        public DeterministicJudgmentExecutor(int delayMs = 0)
        {
            _delayMs = delayMs;
        }

        public OperatorType OperatorType => OperatorType.ResultOutput;

        public async Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }

            var imageBytes = inputs?["Image"] as byte[] ?? [];
            var decisionByte = imageBytes.Length == 0 ? 0 : imageBytes[0];
            var isOk = decisionByte % 2 == 0;

            return OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["JudgmentResult"] = isOk ? "OK" : "NG",
                ["DecisionByte"] = decisionByte,
                ["Image"] = imageBytes
            });
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class PackageConfiguredImageAcquisitionExecutor : IOperatorExecutor
    {
        private readonly byte[] _imageBytes;

        public PackageConfiguredImageAcquisitionExecutor(byte[] imageBytes)
        {
            _imageBytes = imageBytes;
        }

        public OperatorType OperatorType => OperatorType.ImageAcquisition;

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            if (inputs?.ContainsKey("Image") == true)
            {
                throw new InvalidOperationException("Package-configured runs must not inject an external image.");
            }

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Image"] = _imageBytes
            }));
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class NoImageNgExecutor : IOperatorExecutor
    {
        public OperatorType OperatorType => OperatorType.ResultOutput;

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["JudgmentResult"] = "NG",
                ["Score"] = 0.01
            }));
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class BlockingResultOutputExecutor : IOperatorExecutor
    {
        private readonly TaskCompletionSource<object?> _started;
        private readonly TaskCompletionSource<object?> _release;

        public BlockingResultOutputExecutor(TaskCompletionSource<object?> started, TaskCompletionSource<object?> release)
        {
            _started = started;
            _release = release;
        }

        public OperatorType OperatorType => OperatorType.ResultOutput;

        public async Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult(null);
            await _release.Task.ConfigureAwait(false);
            return OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["JudgmentResult"] = "OK",
                ["Image"] = new byte[] { 2, 4, 6, 8 }
            });
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class TrackingRuntimeResultWriter : IRuntimeResultRecordWriter
    {
        public int DroppedCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int EnqueueCount { get; private set; }

        public bool ThrowOnDispose { get; set; }

        public ValueTask<bool> EnqueueAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken)
        {
            EnqueueCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new IOException("old result writer cleanup failed");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingRuntimeImageWriter : IRuntimeImageWriter
    {
        public int DroppedCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int EnqueueCount { get; private set; }

        public bool ThrowOnDispose { get; set; }

        public bool ShouldPersist(RuntimeNormalizedResult result)
        {
            return false;
        }

        public string PlanPath(RuntimeNormalizedResult result)
        {
            return Path.Combine(Path.GetTempPath(), $"{result.RunId:N}.png");
        }

        public ValueTask<bool> EnqueueAsync(RuntimeNormalizedResult result, CancellationToken cancellationToken)
        {
            EnqueueCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new IOException("old image writer cleanup failed");
            }

            return ValueTask.CompletedTask;
        }
    }
}
