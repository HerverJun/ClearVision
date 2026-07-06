using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestData;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
public class ProjectServiceTests
{
    [Fact]
    public async Task ListSearchAndRecent_ShouldSkipRecoveryRequiredProjectAndReturnHealthyProjects()
    {
        var root = CreateTempPath();
        ProjectSaveCoordinator.ResetStaticStateForTests();
        try
        {
            var repository = Substitute.For<IProjectRepository>();
            var storage = Substitute.For<IProjectFlowStorage>();
            var factory = new OperatorFactory();
            var badProject = new Project("bad");
            var healthyProject = new Project("healthy");
            repository.GetByIdAsync(badProject.Id).Returns(Task.FromResult<Project?>(badProject));
            repository.GetByIdAsync(healthyProject.Id).Returns(Task.FromResult<Project?>(healthyProject));
            repository.GetByIdFreshAsync(badProject.Id).Returns(Task.FromResult<Project?>(badProject));
            repository.GetByIdFreshAsync(healthyProject.Id).Returns(Task.FromResult<Project?>(healthyProject));
            repository.GetByIdForUpdateAsync(badProject.Id).Returns(Task.FromResult<Project?>(badProject));
            repository.GetByIdForUpdateAsync(healthyProject.Id).Returns(Task.FromResult<Project?>(healthyProject));
            repository.GetAllAsync().Returns(Task.FromResult<IEnumerable<Project>>([badProject, healthyProject]));
            repository.SearchAsync("project").Returns(Task.FromResult<IEnumerable<Project>>([badProject, healthyProject]));
            repository.GetRecentlyOpenedAsync(10).Returns(Task.FromResult<IEnumerable<Project>>([badProject, healthyProject]));
            repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
            storage.LoadFlowJsonAsync(Arg.Any<Guid>()).Returns(Task.FromResult<string?>(null));
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, storage, transactionRoot: root, failureInjector: failure);
            var service = new ProjectService(repository, storage, factory, null, null, coordinator);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                badProject,
                badProject.PersistenceRevision,
                "bad-renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null)));

            var all = (await service.GetAllAsync()).ToList();
            var search = (await service.SearchAsync("project")).ToList();
            var recent = (await service.GetRecentlyOpenedAsync()).ToList();

            all.Should().ContainSingle(item => item.Id == healthyProject.Id);
            search.Should().ContainSingle(item => item.Id == healthyProject.Id);
            recent.Should().ContainSingle(item => item.Id == healthyProject.Id);
            all.Should().NotContain(item => item.Id == badProject.Id);
            search.Should().NotContain(item => item.Id == badProject.Id);
            recent.Should().NotContain(item => item.Id == badProject.Id);
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetByIdAsync_WhenFlowJsonIsCorrupt_ShouldLogWarningAndFallBackToDatabase()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var logger = new RecordingLogger<ProjectService>();
        var factory = new OperatorFactory();
        var project = new Project("demo");

        repository.GetByIdAsync(Arg.Any<Guid>()).Returns(Task.FromResult<Project?>(project));
        storage.LoadFlowJsonAsync(Arg.Any<Guid>()).Returns(Task.FromResult<string?>("{ invalid json"));

        var sut = new ProjectService(repository, storage, factory, logger);

        var dto = await sut.GetByIdAsync(project.Id);

        dto.Should().NotBeNull();
        dto!.Flow.Should().NotBeNull();
        logger.Entries.Should().Contain(entry =>
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            entry.Message.Contains("Failed to deserialize flow JSON"));
    }

    [Fact]
    public async Task GetByIdAsync_WhenProjectHasNoAssets_ShouldReturnEmptyProjectAssets()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var assets = new RecordingProjectAssetStorage();
        var project = new Project("demo");
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, null, projectAssetStorage: assets);

        var dto = await sut.GetByIdAsync(project.Id);

        dto.Should().NotBeNull();
        dto!.Assets.Should().NotBeNull();
        dto.Assets.CalibrationAssets.Should().BeEmpty();
        dto.Assets.SpatialAssets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_WhenProjectIsNotActive_ShouldNotLoadFlowJsonOrAssets()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var assets = Substitute.For<IProjectAssetStorage>();
        var projectId = Guid.NewGuid();
        repository.GetByIdAsync(projectId).Returns(Task.FromResult<Project?>(null));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, null, projectAssetStorage: assets);

        var dto = await sut.GetByIdAsync(projectId);

        dto.Should().BeNull();
        await storage.DidNotReceive().LoadFlowJsonAsync(projectId);
        await assets.DidNotReceive().LoadAssetsAsync(projectId);
    }

    [Fact]
    public async Task ProjectJsonSerializer_WhenAssetsFieldIsMissing_ShouldDeserializeEmptyAssets()
    {
        var serializer = new ProjectJsonSerializer();
        var bytes = Encoding.UTF8.GetBytes("""
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "name": "legacy",
              "version": "1.0.0",
              "persistenceRevision": 7,
              "globalSettings": {},
              "globalVariables": { "variables": [] },
              "createdAt": "2026-07-04T00:00:00Z"
            }
            """);

        var dto = await serializer.DeserializeAsync(bytes);

        dto.Should().NotBeNull();
        dto!.Assets.Should().NotBeNull();
        dto.Assets.CalibrationAssets.Should().BeEmpty();
        dto.Assets.SpatialAssets.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCalibrationAssetAsync_WhenDraftCandidateIsAccepted_ShouldPersistAuthorityAsset()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var assetStorage = new RecordingProjectAssetStorage();
        var project = new Project("demo");
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var coordinator = new ProjectSaveCoordinator(repository, storage, projectAssetStorage: assetStorage);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, null, coordinator, assetStorage);
        var payload = CreateCalibrationPayload();

        var response = await sut.SaveCalibrationAssetAsync(project.Id, new ProjectCalibrationAssetSaveRequest
        {
            ExpectedPersistenceRevision = 0,
            SourceDraftSessionId = "draft-1",
            TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ImageIdentity = "image-hash",
            Payload = payload
        });

        response.PersistenceRevision.Should().Be(1);
        response.Asset.AssetId.Should().NotBeNullOrWhiteSpace();
        response.Asset.SourceDraftSessionId.Should().Be("draft-1");
        response.Asset.ContentHash.Should().Be(ProjectAssetJson.ComputePayloadHash(payload));
        assetStorage.Metadata!.PersistenceRevision.Should().Be(1);
        assetStorage.Assets.CalibrationAssets.Should().ContainSingle()
            .Which.Status.Should().Be("authority");
    }

    [Fact]
    public async Task SaveCalibrationAssetAsync_WhenChecksumDoesNotMatch_ShouldFailWithoutChangingAuthority()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var assetStorage = new RecordingProjectAssetStorage();
        var project = new Project("demo");
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var coordinator = new ProjectSaveCoordinator(repository, storage, projectAssetStorage: assetStorage);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, null, coordinator, assetStorage);

        var act = async () => await sut.SaveCalibrationAssetAsync(project.Id, new ProjectCalibrationAssetSaveRequest
        {
            ExpectedPersistenceRevision = 0,
            ExpectedContentHash = "sha256:0000",
            Payload = CreateCalibrationPayload()
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV019*");
        project.PersistenceRevision.Should().Be(0);
        assetStorage.Metadata.Should().BeNull();
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task UpdateAsync_WhenCompatibleGlobalVariableSchemaChanges_ShouldMigrateCurrentValue()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var factory = new OperatorFactory();
        using var registryScope = CreateRegistryWithStateStore();
        var registry = registryScope.Registry;
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));

        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(callInfo => Task.FromResult(callInfo.Arg<Project>()));
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));

        var existingSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        existingSession.SetValue(variableId, 7L, ProjectVariableUpdatedBy.StudioManual);

        var sut = new ProjectService(repository, storage, factory, null, registry);

        await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "demo",
            GlobalVariables = CreateSchema(variableId, 3)
        });

        var refreshedSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        refreshedSession.Should().NotBeSameAs(existingSession);
        refreshedSession.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)).Should().Be(7L);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenRepositorySaveFails_ShouldFenceAndKeepExistingSession()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository
            .When(item => item.UpdateAsync(Arg.Any<Project>()))
            .Do(_ => throw new InvalidOperationException("save failed"));
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var oldSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        var act = async () => await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 5));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*save failed*");
        project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        registry.GetOrCreate(project.Id, project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var value).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(value).Should().Be(9L);
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenStatePersistFailsAfterRepositorySave_ShouldFenceAndKeepExistingSession()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry(new FailingProjectVariableStateStore());
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var oldSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        var act = async () => await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 5, "stats.renamed"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV012*state failed*");
        project.GlobalVariables.Variables.Single().Name.Should().Be("stats.renamed");
        project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(5L);
        project.PersistenceRevision.Should().Be(1);
        await repository.Received(1).UpdateAsync(project);
        registry.GetOrCreate(project.Id, project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var value).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(value).Should().Be(9L);
    }

    [Fact]
    public async Task UpdateAsync_WhenNewFlowReferencesNewSchemaVariable_ShouldSaveTogether()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        var factory = new OperatorFactory();
        using var registryScope = CreateRegistryWithStateStore();
        var registry = registryScope.Registry;
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        var sut = new ProjectService(repository, storage, factory, null, registry);
        var schema = CreateSchema(variableId, 3);
        var flow = CreateVariableReadFlow(variableId, "stats.count");

        var saved = await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "demo",
            Description = "updated",
            Flow = flow,
            GlobalVariables = schema
        });

        saved.Flow.Should().NotBeNull();
        project.Description.Should().Be("updated");
        project.GlobalVariables.Variables.Single().Id.Should().Be(variableId);
        storage.LastSavedFlowJson.Should().Contain("VariableRead");
        storage.LastPersistenceRevision.Should().Be(1);
        await repository.Received(1).UpdateAsync(project);
    }

    [Fact]
    public async Task UpdateAsync_WhenExpectedPersistenceRevisionIsStale_ShouldReturnPsv011WithoutWrite()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        var factory = new OperatorFactory();
        var project = new Project("demo");
        project.SetPersistenceRevision(2);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        var sut = new ProjectService(repository, storage, factory);

        var act = async () => await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "renamed",
            Description = "stale",
            ExpectedPersistenceRevision = 1
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV011*");
        project.Name.Should().Be("demo");
        project.Description.Should().BeNull();
        project.PersistenceRevision.Should().Be(2);
        storage.SaveCount.Should().Be(0);
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task UpdateAsync_WhenExpectedPersistenceRevisionOmitted_ShouldUseCurrentRevisionForCompatibility()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        var factory = new OperatorFactory();
        var project = new Project("demo");
        project.SetPersistenceRevision(2);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        var sut = new ProjectService(repository, storage, factory);

        var saved = await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "renamed",
            Description = "compatible"
        });

        saved.PersistenceRevision.Should().Be(3);
        project.Name.Should().Be("renamed");
        project.Description.Should().Be("compatible");
        await repository.Received(1).UpdateAsync(project);
    }

    [Fact]
    public async Task UpdateFlowAsync_WhenSchemaUnchanged_ShouldKeepCurrentVariableValueAndVersion()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        using var registryScope = CreateRegistryWithStateStore();
        var registry = registryScope.Registry;
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        var session = registry.GetOrCreate(project.Id, project.GlobalVariables);
        session.SetValue(variableId, 128L, ProjectVariableUpdatedBy.StudioManual);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        await sut.UpdateFlowAsync(project.Id, new UpdateFlowRequest
        {
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "ReadCount",
                    Type = ClearVision.Product.Core.Enums.OperatorType.VariableRead,
                    X = 40,
                    Y = 20
                }
            ],
            Connections = []
        });

        var after = registry.GetOrCreate(project.Id, project.GlobalVariables);
        after.Should().BeSameAs(session);
        after.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(128L);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenVariableIsRenamed_ShouldNormalizeStoredFlowVariableName()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        using var registryScope = CreateRegistryWithStateStore();
        var registry = registryScope.Registry;
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1, "stats.old"));
        var storedFlow = CreateVariableReadFlow(variableId, "stats.old");
        var storedFlowJson = JsonSerializer.Serialize(storedFlow);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.Seed(project.Id, storedFlowJson, 0);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 1, "stats.current"));

        storage.LastSavedFlowJson.Should().NotBeNull();
        storage.LastSavedFlowJson.Should().Contain("stats.current");
        storage.LastSavedFlowJson.Should().NotContain("stats.old");
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenStoredFlowUsesOnlyVariableName_ShouldNormalizeVariableId()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1, "stats.count"));
        var storedFlow = CreateLegacyVariableReadFlow("stats.count");
        var storedFlowJson = JsonSerializer.Serialize(storedFlow);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.Seed(project.Id, storedFlowJson, 0);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 1, "stats.count"));

        storage.LastSavedFlowJson.Should().NotBeNull();
        storage.LastSavedFlowJson.Should().Contain(variableId.ToString("D"));
        storage.LastSavedFlowJson.Should().Contain("stats.count");
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenStoredFlowNeedsNoNormalization_ShouldNotRewriteFlow()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1, "stats.count"));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);
        await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "demo",
            Flow = CreateVariableReadFlow(variableId, "stats.count")
        });
        var saveCount = storage.SaveCount;
        storage.LastSavedFlowJson.Should().NotBeNull();
        repository.ClearReceivedCalls();

        await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 1, "stats.count"));

        storage.SaveCount.Should().Be(saveCount);
        await repository.DidNotReceive().UpdateAsync(project);
    }

    [Fact]
    public async Task UpdateAsync_WhenVariableIdAndNamePointToDifferentVariables_ShouldReturnGv026()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var firstVariableId = Guid.NewGuid();
        var secondVariableId = Guid.NewGuid();
        var schema = CreateTwoVariableSchema(firstVariableId, secondVariableId);
        var project = new Project("demo");
        project.UpdateGlobalVariables(schema);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        var act = async () => await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "demo",
            GlobalVariables = schema,
            Flow = CreateVariableReadFlow(firstVariableId, "stats.second")
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GV026*");
        await storage.DidNotReceive().SaveFlowJsonAsync(project.Id, Arg.Any<string>());
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task UpdateAsync_WhenRepositoryFailsAfterCommitIntent_ShouldFenceWithoutSavingFlowOrPublishingSession()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = new RecordingProjectFlowStorage();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        const string oldFlowJson = "{\"name\":\"old\",\"operators\":[],\"connections\":[]}";
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository
            .When(item => item.UpdateAsync(Arg.Any<Project>()))
            .Do(_ => throw new InvalidOperationException("db failed"));
        storage.Seed(project.Id, oldFlowJson, 0);
        var oldSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        oldSession.SetValue(variableId, 8L, ProjectVariableUpdatedBy.StudioManual);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        var act = async () => await sut.UpdateAsync(project.Id, new UpdateProjectRequest
        {
            Name = "renamed",
            Flow = CreateVariableReadFlow(variableId, "stats.count"),
            GlobalVariables = CreateSchema(variableId, 5)
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*db failed*");
        project.Name.Should().Be("demo");
        project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        storage.LastSavedFlowJson.Should().Be(oldFlowJson);
        storage.SaveCount.Should().Be(0);
        registry.GetOrCreate(project.Id, project.GlobalVariables).Should().BeSameAs(oldSession);
    }

    [Fact]
    public async Task UpdateEntrypoints_WhenProjectIsNotActive_ShouldRejectWithoutLoadingDerivedState()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var projectId = Guid.NewGuid();
        repository.GetByIdAsync(projectId).Returns(Task.FromResult<Project?>(null));
        var sut = new ProjectService(repository, storage, new OperatorFactory());

        var update = async () => await sut.UpdateAsync(projectId, new UpdateProjectRequest { Name = "deleted" });
        var updateFlow = async () => await sut.UpdateFlowAsync(projectId, new UpdateFlowRequest());
        var updateGlobalVariables = async () => await sut.UpdateGlobalVariablesAsync(projectId, new ProjectGlobalVariableSchema());

        await update.Should().ThrowAsync<ProjectNotFoundException>();
        await updateFlow.Should().ThrowAsync<ProjectNotFoundException>();
        await updateGlobalVariables.Should().ThrowAsync<ProjectNotFoundException>();
        await storage.DidNotReceive().LoadFlowJsonAsync(projectId);
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task DeleteAsync_WhenProjectVariableStateExists_ShouldDeletePersistedStateFilesAndDropRegistrySession()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectDeleteState", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = Substitute.For<IProjectRepository>();
            var storage = new RecordingProjectFlowStorage();
            var assetStorage = new RecordingProjectAssetStorage();
            var registry = new ProjectVariableSessionRegistry(new JsonFileProjectVariableStateStore(root));
            var variableId = Guid.NewGuid();
            var project = new Project("demo");
            project.UpdateGlobalVariables(CreateSchema(variableId, 1));

            repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
            repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
            repository.UpdateAsync(Arg.Any<Project>()).Returns(callInfo => Task.FromResult(callInfo.Arg<Project>()));
            storage.Seed(project.Id, JsonSerializer.Serialize(CreateVariableReadFlow(variableId, "stats.count")), 0);
            var assets = new ProjectAssetsDto
            {
                CalibrationAssets =
                [
                    new ProjectCalibrationAssetDto
                    {
                        AssetId = "calibration-a",
                        Kind = "CalibrationBundleV2",
                        Version = "1",
                        Status = "authority",
                        Payload = CreateCalibrationPayload()
                    }
                ]
            };
            await assetStorage.SaveAssetsAsync(
                project.Id,
                assets,
                0,
                Guid.NewGuid(),
                ProjectAssetJson.ComputeAssetsHash(assets));

            var authoritative = registry.GetOrCreate(project.Id, project.GlobalVariables);
            authoritative.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
            registry.TryMutateAndPersist(
                    project.Id,
                    project.GlobalVariables,
                    session => session.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual),
                    out authoritative,
                    out var seedError)
                .Should()
                .BeTrue(seedError);
            Directory.EnumerateFileSystemEntries(root).Should().NotBeEmpty();

            var coordinator = new ProjectSaveCoordinator(repository, storage, registry, projectAssetStorage: assetStorage);
            await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "demo-renamed",
                null,
                project.GlobalVariables,
                project.GlobalVariables,
                storage.LastSavedFlowJson,
                null,
                null));
            ProjectSaveCoordinator.HasProjectStateForTests(project.Id).Should().BeTrue();
            repository.ClearReceivedCalls();
            var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry, coordinator, assetStorage);

            await sut.DeleteAsync(project.Id);

            project.IsDeleted.Should().BeTrue();
            registry.GetOrCreate(project.Id, project.GlobalVariables).Should().NotBeSameAs(authoritative);
            registry.GetOrCreate(project.Id, project.GlobalVariables).TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
            Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)).Should().Be(1L);
            snapshot.Version.Should().Be(0);
            Directory.EnumerateFileSystemEntries(root).Should().BeEmpty();
            storage.DeleteCount.Should().Be(1);
            storage.LastSavedFlowJson.Should().BeNull();
            assetStorage.DeleteCount.Should().Be(1);
            assetStorage.Assets.CalibrationAssets.Should().BeEmpty();
            ProjectSaveCoordinator.HasProjectStateForTests(project.Id).Should().BeFalse();
            ProjectSaveCoordinator.HasRecoveryRequiredForTests(project.Id).Should().BeFalse();
            ProjectSaveCoordinator.HasProjectGateForTests(project.Id).Should().BeFalse();
            await repository.Received(1).UpdateAsync(project);
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenCleanupFails_ShouldThrowAndNotReportSuccess()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var assets = Substitute.For<IProjectAssetStorage>();
        var project = new Project("demo");
        repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.DeleteFlowJsonAsync(project.Id).Returns(_ => Task.FromException(new IOException("flow cleanup failed")));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, null, projectAssetStorage: assets);

        var act = async () => await sut.DeleteAsync(project.Id);

        await act.Should().ThrowAsync<IOException>().WithMessage("*flow cleanup failed*");
        project.IsDeleted.Should().BeTrue();
        await repository.Received(1).UpdateAsync(project);
        await assets.DidNotReceive().DeleteAssetsAsync(project.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenProjectAlreadyDeleted_ShouldReturnProjectNotFoundWithoutCleanup()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var assets = Substitute.For<IProjectAssetStorage>();
        var projectId = Guid.NewGuid();
        repository.GetByIdForUpdateAsync(projectId).Returns(Task.FromResult<Project?>(null));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, null, projectAssetStorage: assets);

        var act = async () => await sut.DeleteAsync(projectId);

        await act.Should().ThrowAsync<ProjectNotFoundException>();
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
        await storage.DidNotReceive().DeleteFlowJsonAsync(projectId);
        await assets.DidNotReceive().DeleteAssetsAsync(projectId);
    }

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue, string name = "stats.count")
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = name,
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static ProjectGlobalVariableSchema CreateTwoVariableSchema(Guid firstVariableId, Guid secondVariableId)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = firstVariableId,
                    Name = "stats.first",
                    DisplayName = "First",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(1L),
                    ManualWriteAllowed = true
                },
                new ProjectGlobalVariableDefinition
                {
                    Id = secondVariableId,
                    Name = "stats.second",
                    DisplayName = "Second",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(2L),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static OperatorFlowDto CreateVariableReadFlow(Guid variableId, string variableName)
    {
        return new OperatorFlowDto
        {
            Name = "MainFlow",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "ReadCount",
                    Type = ClearVision.Product.Core.Enums.OperatorType.VariableRead,
                    Parameters =
                    [
                        new ParameterDto { Id = Guid.NewGuid(), Name = "Scope", DisplayName = "Scope", DataType = "enum", Value = "Project" },
                        new ParameterDto { Id = Guid.NewGuid(), Name = "VariableId", DisplayName = "VariableId", DataType = "string", Value = variableId.ToString() },
                        new ParameterDto { Id = Guid.NewGuid(), Name = "VariableName", DisplayName = "VariableName", DataType = "string", Value = variableName },
                        new ParameterDto { Id = Guid.NewGuid(), Name = "DataType", DisplayName = "DataType", DataType = "enum", Value = "Int" }
                    ]
                }
            ],
            Connections = []
        };
    }

    private static OperatorFlowDto CreateLegacyVariableReadFlow(string variableName)
    {
        return new OperatorFlowDto
        {
            Name = "MainFlow",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "ReadCount",
                    Type = ClearVision.Product.Core.Enums.OperatorType.VariableRead,
                    Parameters =
                    [
                        new ParameterDto { Id = Guid.NewGuid(), Name = "Scope", DisplayName = "Scope", DataType = "enum", Value = "Project" },
                        new ParameterDto { Id = Guid.NewGuid(), Name = "VariableName", DisplayName = "VariableName", DataType = "string", Value = variableName },
                        new ParameterDto { Id = Guid.NewGuid(), Name = "DataType", DisplayName = "DataType", DataType = "enum", Value = "Int" }
                    ]
                }
            ],
            Connections = []
        };
    }

    private static JsonElement CreateCalibrationPayload()
    {
        using var document = JsonDocument.Parse(CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson());
        return document.RootElement.Clone();
    }

    private sealed class FailingProjectVariableStateStore : IProjectVariableStateStore
    {
        public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema) => [];

        public void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
        {
            throw new IOException("state failed");
        }

        public void Delete(string scopeId)
        {
        }
    }

    private sealed class RecordingProjectAssetStorage : IProjectAssetStorage
    {
        public ProjectAssetsDto Assets { get; private set; } = new();

        public ProjectAssetStorageMetadata? Metadata { get; private set; }

        public int DeleteCount { get; private set; }

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
            DeleteCount += 1;
            Assets = new ProjectAssetsDto();
            Metadata = null;
            return Task.CompletedTask;
        }
    }

    private static RegistryScope CreateRegistryWithStateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.ProjectServiceTests.VariableState", Guid.NewGuid().ToString("N"));
        return new RegistryScope(root);
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), "ClearVision.ProjectServiceTests", Guid.NewGuid().ToString("N"));

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

    private sealed class RegistryScope : IDisposable
    {
        private readonly string _root;

        public RegistryScope(string root)
        {
            _root = root;
            Registry = new ProjectVariableSessionRegistry(new JsonFileProjectVariableStateStore(root));
        }

        public ProjectVariableSessionRegistry Registry { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class RecordingProjectFlowStorage : IProjectFlowStorage
    {
        private string? _flowJson;
        private ProjectFlowStorageMetadata? _metadata;

        public string? LastSavedFlowJson { get; private set; }

        public long LastPersistenceRevision { get; private set; }

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        public void Seed(Guid projectId, string flowJson, long persistenceRevision)
        {
            _flowJson = flowJson;
            LastSavedFlowJson = flowJson;
            LastPersistenceRevision = persistenceRevision;
            _metadata = CreateMetadata(projectId, flowJson, persistenceRevision);
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            Seed(projectId, flowJson, 0);
            return Task.CompletedTask;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
        {
            SaveCount += 1;
            Seed(projectId, flowJson, persistenceRevision);
            return Task.CompletedTask;
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId) => Task.FromResult(_flowJson);

        public Task DeleteFlowJsonAsync(Guid projectId)
        {
            DeleteCount += 1;
            _flowJson = null;
            _metadata = null;
            LastSavedFlowJson = null;
            LastPersistenceRevision = 0;
            return Task.CompletedTask;
        }

        public Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId) => Task.FromResult(_metadata);

        private static ProjectFlowStorageMetadata CreateMetadata(Guid projectId, string flowJson, long revision) =>
            new(1, projectId, revision, ComputeSha256(flowJson), DateTimeOffset.UtcNow);

        private static string ComputeSha256(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
