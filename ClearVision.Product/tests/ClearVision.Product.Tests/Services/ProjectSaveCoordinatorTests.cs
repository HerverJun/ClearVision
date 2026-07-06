using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestData;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
public sealed class ProjectSaveCoordinatorTests
{
    [Fact]
    public async Task SaveExistingProjectAsync_WhenFlowApplyFailsOnceAfterCommitIntent_ShouldRecoverForward()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var previousFlow = SerializeFlow("old");
            var nextFlow = CreateFlow("new");
            var nextFlowJson = JsonSerializer.Serialize(nextFlow);
            await flowStorage.SaveFlowJsonAsync(project.Id, previousFlow, 0);
            flowStorage.ResetCounts();
            flowStorage.FailRevisionedSaves = 1;
            var sut = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root);

            var result = await sut.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                "updated",
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                previousFlow,
                nextFlow,
                nextFlowJson));

            result.Changed.Should().BeTrue();
            project.Name.Should().Be("renamed");
            project.Description.Should().Be("updated");
            project.PersistenceRevision.Should().Be(1);
            flowStorage.FlowJson.Should().Be(nextFlowJson);
            flowStorage.Metadata!.PersistenceRevision.Should().Be(1);
            flowStorage.RevisionedSaveCount.Should().Be(2);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenAfterCommitIntentFailsOnce_ShouldRecoverImmediatelyAndReturnSuccess()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var previousFlow = SerializeFlow("old");
            var nextFlow = CreateFlow("new");
            var nextFlowJson = JsonSerializer.Serialize(nextFlow);
            await flowStorage.SaveFlowJsonAsync(project.Id, previousFlow, 0);
            var crash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterCommitIntent, failAlways: false);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: crash);

            var result = await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                previousFlow,
                nextFlow,
                nextFlowJson));

            result.Changed.Should().BeTrue();
            project.Name.Should().Be("renamed");
            project.PersistenceRevision.Should().Be(1);
            flowStorage.FlowJson.Should().Be(nextFlowJson);
            flowStorage.Metadata!.PersistenceRevision.Should().Be(1);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task EnsureProjectAvailable_WhenCommittedRecoveryFails_ShouldFenceProjectAccess()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: failure);

            var act = async () => await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*injected*");
            coordinator.Invoking(item => item.EnsureProjectAvailable(project.Id))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*PSV001*");

            var service = new ProjectService(repository, flowStorage, new OperatorFactory(), null, null, coordinator);
            var read = async () => await service.GetByIdAsync(project.Id);
            await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV001*");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task ClearProjectState_WhenProjectsAreFenced_ShouldClearOnlyRequestedProject()
    {
        var root = CreateTempPath();
        ProjectSaveCoordinator.ResetStaticStateForTests();
        try
        {
            var first = new Project("first");
            var second = new Project("second");
            var repository = new InMemoryProjectRepository(first, second);
            var flowStorage = new InMemoryProjectFlowStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: failure);

            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                first,
                first.PersistenceRevision,
                "first-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                second,
                second.PersistenceRevision,
                "second-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null)));

            ProjectSaveCoordinator.HasRecoveryRequiredForTests(first.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasRecoveryRequiredForTests(second.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectStateForTests(first.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectStateForTests(second.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectGateForTests(first.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectGateForTests(second.Id).Should().BeTrue();

            coordinator.ClearProjectState(first.Id);

            coordinator.Invoking(item => item.EnsureProjectAvailable(first.Id)).Should().NotThrow();
            coordinator.Invoking(item => item.EnsureProjectAvailable(second.Id)).Should().Throw<InvalidOperationException>().WithMessage("*PSV001*");
            ProjectSaveCoordinator.HasRecoveryRequiredForTests(first.Id).Should().BeFalse();
            ProjectSaveCoordinator.HasRecoveryRequiredForTests(second.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectStateForTests(first.Id).Should().BeFalse();
            ProjectSaveCoordinator.HasProjectStateForTests(second.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectGateForTests(first.Id).Should().BeFalse();
            ProjectSaveCoordinator.HasProjectGateForTests(second.Id).Should().BeTrue();
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task ClearProjectState_WhenLeaseIsHeld_ShouldPreserveSerialGateUntilWaitersRelease()
    {
        ProjectSaveCoordinator.ResetStaticStateForTests();
        var project = new Project("demo");
        var repository = new InMemoryProjectRepository(project);
        var flowStorage = new InMemoryProjectFlowStorage();
        var coordinator = new ProjectSaveCoordinator(repository, flowStorage);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var access = await coordinator.AcquireProjectAccessAsync(project.Id);
        var competingAccess = Task.Run(async () =>
        {
            await using var lease = await coordinator.AcquireProjectAccessAsync(project.Id);
            entered.TrySetResult();
        });

        try
        {
            await Task.Delay(75);
            entered.Task.IsCompleted.Should().BeFalse();
            ProjectSaveCoordinator.HasProjectGateForTests(project.Id).Should().BeTrue();

            coordinator.ClearProjectState(project.Id);

            ProjectSaveCoordinator.HasProjectGateForTests(project.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectStateForTests(project.Id).Should().BeFalse();
            ProjectSaveCoordinator.HasRecoveryRequiredForTests(project.Id).Should().BeFalse();
            await Task.Delay(75);
            entered.Task.IsCompleted.Should().BeFalse();

            await access.DisposeAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await competingAccess.WaitAsync(TimeSpan.FromSeconds(5));
            ProjectSaveCoordinator.HasProjectGateForTests(project.Id).Should().BeFalse();
        }
        finally
        {
            await access.DisposeAsync();
            await Task.WhenAny(competingAccess, Task.Delay(TimeSpan.FromSeconds(5)));
            ProjectSaveCoordinator.ResetStaticStateForTests();
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenExpectedRevisionIsStale_ShouldFailBeforeCommitIntent()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            project.SetPersistenceRevision(1);
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var sut = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root);

            var act = async () => await sut.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                0,
                "stale",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV011*");
            project.Name.Should().Be("demo");
            project.PersistenceRevision.Should().Be(1);
            Directory.Exists(root).Should().BeFalse();
            sut.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().NotThrow();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenProjectAssetCandidateProvided_ShouldPersistAssetsAtProjectRevision()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var assetStorage = new InMemoryProjectAssetStorage();
            var coordinator = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                projectAssetStorage: assetStorage);
            var assetCandidate = CreateAssetCandidate(0, 1);

            var result = await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null,
                assetCandidate));

            result.Changed.Should().BeTrue();
            project.PersistenceRevision.Should().Be(1);
            assetStorage.Metadata.Should().NotBeNull();
            assetStorage.Metadata!.PersistenceRevision.Should().Be(1);
            assetStorage.Metadata.AssetsHash.Should().Be(assetCandidate.AssetsHash);
            assetStorage.Assets.CalibrationAssets.Should().ContainSingle()
                .Which.ProjectRevision.Should().Be(1);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenPreparedFailsWithProjectAssets_ShouldRollbackWithoutAuthorityPollution()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var assetStorage = new InMemoryProjectAssetStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterPrepared, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                failureInjector: failure,
                projectAssetStorage: assetStorage);
            var assetCandidate = CreateAssetCandidate(0, 1);

            var act = async () => await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null,
                assetCandidate));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*injected*");
            project.PersistenceRevision.Should().Be(0);
            assetStorage.Metadata.Should().BeNull();
            assetStorage.Assets.CalibrationAssets.Should().BeEmpty();
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
            coordinator.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().NotThrow();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenProjectHasAssetsAndMetadataChanges_ShouldCarryAssetsForward()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var assetStorage = new InMemoryProjectAssetStorage();
            var coordinator = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                projectAssetStorage: assetStorage);
            await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null,
                CreateAssetCandidate(0, 1)));
            var firstHash = assetStorage.Assets.CalibrationAssets.Single().ContentHash;

            await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            project.PersistenceRevision.Should().Be(2);
            assetStorage.Metadata!.PersistenceRevision.Should().Be(2);
            var asset = assetStorage.Assets.CalibrationAssets.Single();
            asset.ProjectRevision.Should().Be(2);
            asset.ContentHash.Should().Be(firstHash);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenProjectAssetApplyFailsOnce_ShouldRecoverForward()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var assetStorage = new InMemoryProjectAssetStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeProjectAssetsApply, failAlways: false);
            var coordinator = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                failureInjector: failure,
                projectAssetStorage: assetStorage);
            var assetCandidate = CreateAssetCandidate(0, 1);

            var result = await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null,
                assetCandidate));

            result.Changed.Should().BeTrue();
            project.PersistenceRevision.Should().Be(1);
            assetStorage.SaveCount.Should().Be(1);
            assetStorage.Metadata!.PersistenceRevision.Should().Be(1);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenProjectAssetWriteFailsOnce_ShouldRecoverForward()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var assetStorage = new InMemoryProjectAssetStorage { FailSaves = 1 };
            var coordinator = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                projectAssetStorage: assetStorage);
            var assetCandidate = CreateAssetCandidate(0, 1);

            var result = await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null,
                assetCandidate));

            result.Changed.Should().BeTrue();
            project.PersistenceRevision.Should().Be(1);
            assetStorage.SaveAttempts.Should().Be(2);
            assetStorage.Metadata!.PersistenceRevision.Should().Be(1);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RecoverAllAsync_WhenProjectAssetsCandidateHashIsTampered_ShouldFenceProject()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var assetStorage = new InMemoryProjectAssetStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                failureInjector: failure,
                projectAssetStorage: assetStorage);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null,
                CreateAssetCandidate(0, 1))));
            var assetArtifact = Directory
                .EnumerateFiles(Path.Combine(root, project.Id.ToString("D")), "project-assets.json", SearchOption.AllDirectories)
                .Single();
            File.WriteAllText(assetArtifact, "{\"schemaVersion\":1,\"calibrationAssets\":[],\"spatialAssets\":[]}", Encoding.UTF8);
            ProjectSaveCoordinator.ResetStaticStateForTests();
            var recovery = new ProjectSaveCoordinator(
                repository,
                flowStorage,
                transactionRoot: root,
                projectAssetStorage: assetStorage);

            var summary = await recovery.RecoverAllAsync();

            summary.RecoveredCount.Should().Be(0);
            summary.RecoveryRequiredProjectIds.Should().Contain(project.Id);
            summary.Failures.Should().ContainSingle(item =>
                item.ProjectId == project.Id &&
                item.Error.Contains("PSV010", StringComparison.Ordinal));
            recovery.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().Throw<InvalidOperationException>().WithMessage("*PSV001*");
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenPreparedFails_ShouldDiscardJournalWithoutFencingProject()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterPrepared, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: failure);

            var act = async () => await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*injected*");
            project.Name.Should().Be("demo");
            project.PersistenceRevision.Should().Be(0);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
            coordinator.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().NotThrow();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task AcquireProjectAccessAsync_WhenSaveIsInProgress_ShouldWaitForProjectGate()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var blocker = new BlockingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: blocker);

            var saveTask = coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));
            await blocker.WaitUntilHitAsync();

            ProjectSaveCoordinator.HasProjectGateForTests(project.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectStateForTests(project.Id).Should().BeTrue();
            coordinator.ClearProjectState(project.Id);
            ProjectSaveCoordinator.HasProjectGateForTests(project.Id).Should().BeTrue();
            ProjectSaveCoordinator.HasProjectStateForTests(project.Id).Should().BeTrue();

            var accessTask = coordinator.AcquireProjectAccessAsync(project.Id);
            await Task.Delay(75);
            accessTask.IsCompleted.Should().BeFalse();

            blocker.Release();
            await saveTask;
            await using var access = await accessTask;
            project.Name.Should().Be("renamed");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RunStartupRecoveryAsync_WhenRecoveryIsInProgress_ShouldBlockProjectAccessUntilReady()
    {
        var root = CreateTempPath();
        ProjectSaveCoordinator.ResetStaticStateForTests();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var crash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply, failAlways: true);
            var crashCoordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: crash);
            var crashAct = async () => await crashCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));
            await crashAct.Should().ThrowAsync<InvalidOperationException>();

            ProjectSaveCoordinator.ResetStaticStateForTests();
            var blocker = new BlockingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply, project.Id);
            var startupCoordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: blocker);
            var startupTask = startupCoordinator.RunStartupRecoveryAsync();
            await blocker.WaitUntilHitAsync();

            var accessTask = startupCoordinator.AcquireProjectAccessAsync(project.Id);
            await Task.Delay(75);
            accessTask.IsCompleted.Should().BeFalse();

            blocker.Release();
            await startupTask;
            await using var access = await accessTask;
            project.PersistenceRevision.Should().Be(1);
            startupCoordinator.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().NotThrow();
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenDifferentProjectSaveIsInProgress_ShouldNotBlock()
    {
        var root = CreateTempPath();
        try
        {
            var first = new Project("first");
            var second = new Project("second");
            var repository = new InMemoryProjectRepository(first, second);
            var flowStorage = new InMemoryProjectFlowStorage();
            var blocker = new BlockingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply, first.Id);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: blocker);

            var firstSave = coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                first,
                first.PersistenceRevision,
                "first-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));
            await blocker.WaitUntilHitAsync();

            var secondSave = await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                second,
                second.PersistenceRevision,
                "second-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            secondSave.Changed.Should().BeTrue();
            second.Name.Should().Be("second-renamed");
            firstSave.IsCompleted.Should().BeFalse();

            blocker.Release();
            await firstSave;
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenVariableStateIsAlreadyAtTargetDuringRecovery_ShouldNotRewriteState()
    {
        var root = CreateTempPath();
        var stateRoot = CreateTempPath();
        try
        {
            var variableId = Guid.NewGuid();
            var project = new Project("demo");
            var previousSchema = CreateSchema(variableId, 1, "stats.count");
            var nextSchema = CreateSchema(variableId, 5, "stats.renamed");
            project.UpdateGlobalVariables(previousSchema);
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var stateStore = new CountingProjectVariableStateStore(new JsonFileProjectVariableStateStore(stateRoot));
            var registry = new ProjectVariableSessionRegistry(stateStore);
            var session = registry.GetOrCreate(project.Id, previousSchema);
            session.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, registry, root, failure);

            var act = async () => await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                previousSchema,
                nextSchema,
                null,
                null,
                null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV012*");
            stateStore.RevisionedSaveCount.Should().Be(1);
            var metadata = stateStore.LoadMetadata($"project:{project.Id:D}");
            metadata.Should().NotBeNull();
            metadata!.PersistenceRevision.Should().Be(1);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
            DeleteDirectoryIfExists(stateRoot);
        }
    }

    [Fact]
    public async Task RecoverAllAsync_WhenVariableStateAtTargetHasDifferentHash_ShouldFailClosed()
    {
        var root = CreateTempPath();
        var stateRoot = CreateTempPath();
        try
        {
            var variableId = Guid.NewGuid();
            var project = new Project("demo");
            var previousSchema = CreateSchema(variableId, 1, "stats.count");
            var nextSchema = CreateSchema(variableId, 5, "stats.renamed");
            project.UpdateGlobalVariables(previousSchema);
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var stateStore = new JsonFileProjectVariableStateStore(stateRoot);
            var registry = new ProjectVariableSessionRegistry(stateStore);
            registry.GetOrCreate(project.Id, previousSchema).SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, registry, root, failure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                previousSchema,
                nextSchema,
                null,
                null,
                null)));

            stateStore.Save(
                $"project:{project.Id:D}",
                nextSchema,
                [
                    new ProjectVariableValueSnapshot(
                        variableId,
                        JsonSerializer.SerializeToElement(123L),
                        7,
                        DateTimeOffset.UtcNow,
                        ProjectVariableUpdatedBy.StudioManual,
                        null,
                        null)
                ],
                persistenceRevision: 1,
                saveId: Guid.NewGuid());
            var recovery = new ProjectSaveCoordinator(repository, flowStorage, registry, root);

            var summary = await recovery.RecoverAllAsync();

            summary.RecoveredCount.Should().Be(0);
            summary.RecoveryRequiredProjectIds.Should().Contain(project.Id);
            summary.Failures.Should().ContainSingle(item =>
                item.ProjectId == project.Id &&
                item.Error.Contains("PSV013", StringComparison.Ordinal));
            recovery.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().Throw<InvalidOperationException>().WithMessage("*PSV001*");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
            DeleteDirectoryIfExists(stateRoot);
        }
    }

    [Fact]
    public async Task RecoverAllAsync_WhenVariableStateRevisionIsUnknown_ShouldRejectOverwrite()
    {
        var root = CreateTempPath();
        var stateRoot = CreateTempPath();
        try
        {
            var variableId = Guid.NewGuid();
            var project = new Project("demo");
            var previousSchema = CreateSchema(variableId, 1, "stats.count");
            var nextSchema = CreateSchema(variableId, 5, "stats.renamed");
            project.UpdateGlobalVariables(previousSchema);
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var stateStore = new JsonFileProjectVariableStateStore(stateRoot);
            var registry = new ProjectVariableSessionRegistry(stateStore);
            registry.GetOrCreate(project.Id, previousSchema).SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, registry, root, failure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                previousSchema,
                nextSchema,
                null,
                null,
                null)));

            stateStore.Save(
                $"project:{project.Id:D}",
                nextSchema,
                [
                    new ProjectVariableValueSnapshot(
                        variableId,
                        JsonSerializer.SerializeToElement(9L),
                        1,
                        DateTimeOffset.UtcNow,
                        ProjectVariableUpdatedBy.StudioManual,
                        null,
                        null)
                ],
                persistenceRevision: 9,
                saveId: Guid.NewGuid());
            var recovery = new ProjectSaveCoordinator(repository, flowStorage, registry, root);

            var summary = await recovery.RecoverAllAsync();

            summary.RecoveredCount.Should().Be(0);
            summary.RecoveryRequiredProjectIds.Should().Contain(project.Id);
            summary.Failures.Should().ContainSingle(item =>
                item.ProjectId == project.Id &&
                item.Error.Contains("PSV014", StringComparison.Ordinal));
            recovery.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().Throw<InvalidOperationException>().WithMessage("*PSV001*");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
            DeleteDirectoryIfExists(stateRoot);
        }
    }

    [Fact]
    public async Task RecoverAllAsync_WhenVariableStateFileHashIsTampered_ShouldFenceProject()
    {
        var root = CreateTempPath();
        var stateRoot = CreateTempPath();
        try
        {
            var variableId = Guid.NewGuid();
            var project = new Project("demo");
            var previousSchema = CreateSchema(variableId, 1, "stats.count");
            var nextSchema = CreateSchema(variableId, 5, "stats.renamed");
            project.UpdateGlobalVariables(previousSchema);
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var stateStore = new JsonFileProjectVariableStateStore(stateRoot);
            var registry = new ProjectVariableSessionRegistry(stateStore);
            registry.GetOrCreate(project.Id, previousSchema).SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, registry, root, failure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo",
                null,
                previousSchema,
                nextSchema,
                null,
                null,
                null)));
            var stateFile = Directory.EnumerateFiles(stateRoot, "*.json").Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
            var node = JsonNode.Parse(File.ReadAllText(stateFile, Encoding.UTF8))!.AsObject();
            node["variables"]!.AsArray()[0]!.AsObject()["value"] = "123";
            File.WriteAllText(stateFile, node.ToJsonString(ProjectVariableJson.Options), Encoding.UTF8);
            var recovery = new ProjectSaveCoordinator(repository, flowStorage, registry, root);

            var summary = await recovery.RecoverAllAsync();

            summary.RecoveredCount.Should().Be(0);
            summary.RecoveryRequiredProjectIds.Should().Contain(project.Id);
            summary.Failures.Should().ContainSingle(item =>
                item.ProjectId == project.Id &&
                item.Error.Contains("GV041", StringComparison.Ordinal));
            recovery.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().Throw<InvalidOperationException>().WithMessage("*PSV001*");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
            DeleteDirectoryIfExists(stateRoot);
        }
    }

    [Fact]
    public async Task RecoverAllAsync_WhenOneProjectRecoveryFails_ShouldFenceOnlyThatProjectAndContinueOthers()
    {
        var root = CreateTempPath();
        try
        {
            var badProject = new Project("bad");
            var healthyProject = new Project("healthy");
            var repository = new InMemoryProjectRepository(badProject, healthyProject);
            var flowStorage = new InMemoryProjectFlowStorage();
            await flowStorage.SaveFlowJsonAsync(badProject.Id, SerializeFlow("bad-old"), 0);
            await flowStorage.SaveFlowJsonAsync(healthyProject.Id, SerializeFlow("healthy-old"), 0);
            var badCrash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var badCoordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: badCrash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => badCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                badProject,
                badProject.PersistenceRevision,
                "bad-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                SerializeFlow("bad-old"),
                CreateFlow("bad-new"),
                SerializeFlow("bad-new"))));
            var healthyCrash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var healthyCoordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: healthyCrash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => healthyCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                healthyProject,
                healthyProject.PersistenceRevision,
                "healthy-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                SerializeFlow("healthy-old"),
                CreateFlow("healthy-new"),
                SerializeFlow("healthy-new"))));
            var badFlowArtifact = Directory
                .EnumerateFiles(Path.Combine(root, badProject.Id.ToString("D")), "flow.json", SearchOption.AllDirectories)
                .Single();
            File.WriteAllText(badFlowArtifact, "{\"tampered\":true}", Encoding.UTF8);
            ProjectSaveCoordinator.ResetStaticStateForTests();

            var recovery = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root);
            var summary = await recovery.RecoverAllAsync();

            summary.RecoveredCount.Should().Be(1);
            summary.RecoveryRequiredProjectIds.Should().ContainSingle().Which.Should().Be(badProject.Id);
            summary.SystemFailure.Should().BeNull();
            recovery.Invoking(item => item.EnsureProjectAvailable(badProject.Id)).Should().Throw<InvalidOperationException>().WithMessage("*PSV001*");
            recovery.Invoking(item => item.EnsureProjectAvailable(healthyProject.Id)).Should().NotThrow();
            healthyProject.Name.Should().Be("healthy-renamed");
            Directory.EnumerateFiles(Path.Combine(root, healthyProject.Id.ToString("D")), "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
            Directory.EnumerateFiles(Path.Combine(root, badProject.Id.ToString("D")), "manifest.json", SearchOption.AllDirectories).Should().NotBeEmpty();
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RunStartupRecoveryAsync_WhenOneProjectRecoveryFails_ShouldLeaveBarrierReadyAndFenceOnlyThatProject()
    {
        var root = CreateTempPath();
        try
        {
            var badProject = new Project("bad");
            var healthyProject = new Project("healthy");
            var repository = new InMemoryProjectRepository(badProject, healthyProject);
            var flowStorage = new InMemoryProjectFlowStorage();
            await flowStorage.SaveFlowJsonAsync(badProject.Id, SerializeFlow("bad-old"), 0);
            await flowStorage.SaveFlowJsonAsync(healthyProject.Id, SerializeFlow("healthy-old"), 0);
            var badCrash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var badCoordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: badCrash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => badCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                badProject,
                badProject.PersistenceRevision,
                "bad-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                SerializeFlow("bad-old"),
                CreateFlow("bad-new"),
                SerializeFlow("bad-new"))));
            var healthyCrash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.BeforeComplete, failAlways: true);
            var healthyCoordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: healthyCrash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => healthyCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                healthyProject,
                healthyProject.PersistenceRevision,
                "healthy-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                SerializeFlow("healthy-old"),
                CreateFlow("healthy-new"),
                SerializeFlow("healthy-new"))));
            var badFlowArtifact = Directory
                .EnumerateFiles(Path.Combine(root, badProject.Id.ToString("D")), "flow.json", SearchOption.AllDirectories)
                .Single();
            File.WriteAllText(badFlowArtifact, "{\"tampered\":true}", Encoding.UTF8);
            ProjectSaveCoordinator.ResetStaticStateForTests();

            var recovery = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root);
            await recovery.RunStartupRecoveryAsync();

            await using var healthyAccess = await recovery.AcquireProjectAccessAsync(healthyProject.Id);
            healthyProject.Name.Should().Be("healthy-renamed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.AcquireProjectAccessAsync(badProject.Id));
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            DeleteDirectoryIfExists(root);
        }
    }

    private static ProjectAssetSaveCandidate CreateAssetCandidate(long fromRevision, long toRevision, string assetId = "calibration-a")
    {
        var payload = CreateCalibrationPayload();
        var now = DateTimeOffset.UtcNow;
        var assets = new ProjectAssetsDto
        {
            CalibrationAssets =
            [
                new ProjectCalibrationAssetDto
                {
                    AssetId = assetId,
                    Kind = "CalibrationBundleV2",
                    Version = "1",
                    Producer = "ProjectSaveCoordinatorTests",
                    SourceDraftSessionId = "draft-session",
                    ContentHash = ProjectAssetJson.ComputePayloadHash(payload),
                    ProjectRevision = toRevision,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Status = "authority",
                    Payload = payload
                }
            ]
        };
        return ProjectAssetSaveCandidate.Create(assets, fromRevision, toRevision, "test");
    }

    private static JsonElement CreateCalibrationPayload()
    {
        using var document = JsonDocument.Parse(CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson());
        return document.RootElement.Clone();
    }

    private static OperatorFlowDto CreateFlow(string name) => new()
    {
        Name = name,
        Operators = [],
        Connections = []
    };

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue, string name) =>
        new()
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = name,
                    DisplayName = name,
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };

    private static string SerializeFlow(string name) => JsonSerializer.Serialize(CreateFlow(name));

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), "ClearVision.ProjectSaveCoordinator.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ThrowingProjectSaveFailureInjector : IProjectSaveFailureInjector
    {
        private readonly ProjectSaveFailurePoint _point;
        private readonly bool _failAlways;
        private bool _hasFailed;

        public ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint point, bool failAlways)
        {
            _point = point;
            _failAlways = failAlways;
        }

        public Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest)
        {
            if (point == _point && (_failAlways || !_hasFailed))
            {
                _hasFailed = true;
                throw new InvalidOperationException($"injected failure at {point}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingProjectSaveFailureInjector : IProjectSaveFailureInjector
    {
        private readonly ProjectSaveFailurePoint _point;
        private readonly TaskCompletionSource _hit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Guid? _projectId;

        public BlockingProjectSaveFailureInjector(ProjectSaveFailurePoint point, Guid? projectId = null)
        {
            _point = point;
            _projectId = projectId;
        }

        public Task WaitUntilHitAsync() => _hit.Task;

        public void Release() => _release.TrySetResult();

        public async Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest)
        {
            if (point != _point || (_projectId.HasValue && manifest.ProjectId != _projectId.Value))
            {
                return;
            }

            _hit.TrySetResult();
            await _release.Task;
        }
    }

    private sealed class InMemoryProjectAssetStorage : IProjectAssetStorage
    {
        public ProjectAssetsDto Assets { get; private set; } = new();

        public ProjectAssetStorageMetadata? Metadata { get; private set; }

        public int SaveCount { get; private set; }

        public int SaveAttempts { get; private set; }

        public int FailSaves { get; set; }

        public Task<ProjectAssetsDto> LoadAssetsAsync(Guid projectId) =>
            Task.FromResult(ProjectAssetJson.Clone(Assets));

        public Task<ProjectAssetStorageMetadata?> LoadMetadataAsync(Guid projectId) =>
            Task.FromResult(Metadata);

        public Task SaveAssetsAsync(
            Guid projectId,
            ProjectAssetsDto assets,
            long persistenceRevision,
            Guid saveId,
            string assetsHash)
        {
            SaveAttempts += 1;
            if (FailSaves > 0)
            {
                FailSaves -= 1;
                throw new IOException("asset save failed");
            }

            SaveCount += 1;
            Assets = ProjectAssetJson.Clone(assets);
            Metadata = new ProjectAssetStorageMetadata(
                1,
                projectId,
                persistenceRevision,
                assetsHash,
                saveId,
                DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task DeleteAssetsAsync(Guid projectId)
        {
            Assets = new ProjectAssetsDto();
            Metadata = null;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProjectFlowStorage : IProjectFlowStorage
    {
        private readonly Dictionary<Guid, string> _flowJsonByProject = new();
        private readonly Dictionary<Guid, ProjectFlowStorageMetadata> _metadataByProject = new();

        public string? FlowJson { get; private set; }

        public ProjectFlowStorageMetadata? Metadata { get; private set; }

        public int FailRevisionedSaves { get; set; }

        public int RevisionedSaveCount { get; private set; }

        public void ResetCounts()
        {
            RevisionedSaveCount = 0;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            _flowJsonByProject[projectId] = flowJson;
            FlowJson = flowJson;
            Metadata = CreateMetadata(projectId, flowJson, 0);
            _metadataByProject[projectId] = Metadata;
            return Task.CompletedTask;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
        {
            RevisionedSaveCount += 1;
            if (FailRevisionedSaves > 0)
            {
                FailRevisionedSaves -= 1;
                throw new IOException("flow save failed");
            }

            _flowJsonByProject[projectId] = flowJson;
            FlowJson = flowJson;
            Metadata = CreateMetadata(projectId, flowJson, persistenceRevision);
            _metadataByProject[projectId] = Metadata;
            return Task.CompletedTask;
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId) =>
            Task.FromResult(_flowJsonByProject.GetValueOrDefault(projectId));

        public Task DeleteFlowJsonAsync(Guid projectId)
        {
            _flowJsonByProject.Remove(projectId);
            _metadataByProject.Remove(projectId);
            FlowJson = null;
            Metadata = null;
            return Task.CompletedTask;
        }

        public Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId) =>
            Task.FromResult(_metadataByProject.GetValueOrDefault(projectId));

        private static ProjectFlowStorageMetadata CreateMetadata(Guid projectId, string flowJson, long revision) =>
            new(1, projectId, revision, ComputeSha256(flowJson), DateTimeOffset.UtcNow);

        private static string ComputeSha256(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly Dictionary<Guid, Project> _projects;

        public InMemoryProjectRepository(params Project[] projects)
        {
            _projects = projects.ToDictionary(project => project.Id);
        }

        public Task<Project?> GetByIdAsync(Guid id) => Task.FromResult(_projects.GetValueOrDefault(id));

        public Task<Project?> GetByIdFreshAsync(Guid id) => GetByIdAsync(id);

        public Task<Project?> GetByIdForUpdateAsync(Guid id) => GetByIdAsync(id);

        public Task<IEnumerable<Project>> GetAllAsync() => Task.FromResult<IEnumerable<Project>>(_projects.Values);

        public Task<IEnumerable<Project>> FindAsync(Expression<Func<Project, bool>> predicate) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values);

        public Task<Project> AddAsync(Project entity)
        {
            _projects[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(Project entity) => Task.CompletedTask;

        public Task DeleteAsync(Project entity) => Task.CompletedTask;

        public Task DeleteByIdAsync(Guid id) => Task.CompletedTask;

        public Task<bool> ExistsAsync(Guid id) => Task.FromResult(_projects.ContainsKey(id));

        public Task<Project?> GetByNameAsync(string name) =>
            Task.FromResult(_projects.Values.FirstOrDefault(project => string.Equals(name, project.Name, StringComparison.Ordinal)));

        public Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values.Take(count));

        public Task<IEnumerable<Project>> SearchAsync(string keyword) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values);

        public Task<Project?> GetWithFlowAsync(Guid id) => GetByIdAsync(id);

        public Task UpdateFlowAsync(Project project) => Task.CompletedTask;
    }

    private sealed class CountingProjectVariableStateStore : IProjectVariableStateStore
    {
        private readonly IProjectVariableStateStore _inner;

        public CountingProjectVariableStateStore(IProjectVariableStateStore inner)
        {
            _inner = inner;
        }

        public int RevisionedSaveCount { get; private set; }

        public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema) =>
            _inner.Load(scopeId, schema);

        public void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots) =>
            _inner.Save(scopeId, schema, snapshots);

        public void Save(
            string scopeId,
            ProjectGlobalVariableSchema schema,
            IReadOnlyList<ProjectVariableValueSnapshot> snapshots,
            long persistenceRevision,
            Guid? saveId)
        {
            RevisionedSaveCount += 1;
            _inner.Save(scopeId, schema, snapshots, persistenceRevision, saveId);
        }

        public ProjectVariableStateMetadata? LoadMetadata(string scopeId) => _inner.LoadMetadata(scopeId);

        public void Delete(string scopeId) => _inner.Delete(scopeId);
    }
}
