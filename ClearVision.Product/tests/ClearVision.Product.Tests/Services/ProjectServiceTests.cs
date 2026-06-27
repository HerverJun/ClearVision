using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public class ProjectServiceTests
{
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
    public async Task UpdateAsync_WhenCompatibleGlobalVariableSchemaChanges_ShouldMigrateCurrentValue()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var factory = new OperatorFactory();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));

        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
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
    public async Task UpdateGlobalVariablesAsync_WhenRepositorySaveFails_ShouldKeepExistingSession()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository
            .When(item => item.UpdateAsync(Arg.Any<Project>()))
            .Do(_ => throw new InvalidOperationException("save failed"));
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var oldSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        var act = async () => await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 5));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*save failed*");
        registry.GetOrCreate(project.Id, project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var value).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(value).Should().Be(9L);
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenStatePersistFailsAfterRepositorySave_ShouldRollbackProjectAndKeepSession()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry(new FailingProjectVariableStateStore());
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
        var oldSession = registry.GetOrCreate(project.Id, project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        var act = async () => await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 5, "stats.renamed"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("GV030:*state failed*");
        project.GlobalVariables.Variables.Single().Name.Should().Be("stats.count");
        project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        await repository.Received(2).UpdateAsync(project);
        registry.GetOrCreate(project.Id, project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var value).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(value).Should().Be(9L);
    }

    [Fact]
    public async Task UpdateAsync_WhenNewFlowReferencesNewSchemaVariable_ShouldSaveTogether()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var factory = new OperatorFactory();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
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
        await storage.Received(1).SaveFlowJsonAsync(project.Id, Arg.Is<string>(json => json.Contains("VariableRead")));
        await repository.Received(1).UpdateAsync(project);
    }

    [Fact]
    public async Task UpdateFlowAsync_WhenSchemaUnchanged_ShouldKeepCurrentVariableValueAndVersion()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
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
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1, "stats.old"));
        var storedFlow = CreateVariableReadFlow(variableId, "stats.old");
        var storedFlowJson = JsonSerializer.Serialize(storedFlow);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(storedFlowJson));
        string? savedFlowJson = null;
        storage
            .When(item => item.SaveFlowJsonAsync(project.Id, Arg.Any<string>()))
            .Do(callInfo => savedFlowJson = callInfo.ArgAt<string>(1));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 1, "stats.current"));

        savedFlowJson.Should().NotBeNull();
        savedFlowJson.Should().Contain("stats.current");
        savedFlowJson.Should().NotContain("stats.old");
    }

    [Fact]
    public async Task UpdateGlobalVariablesAsync_WhenStoredFlowUsesOnlyVariableName_ShouldNormalizeVariableId()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1, "stats.count"));
        var storedFlow = CreateLegacyVariableReadFlow("stats.count");
        var storedFlowJson = JsonSerializer.Serialize(storedFlow);
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(storedFlowJson));
        string? savedFlowJson = null;
        storage
            .When(item => item.SaveFlowJsonAsync(project.Id, Arg.Any<string>()))
            .Do(callInfo => savedFlowJson = callInfo.ArgAt<string>(1));
        var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

        await sut.UpdateGlobalVariablesAsync(project.Id, CreateSchema(variableId, 1, "stats.count"));

        savedFlowJson.Should().NotBeNull();
        savedFlowJson.Should().Contain(variableId.ToString("D"));
        savedFlowJson.Should().Contain("stats.count");
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
    public async Task UpdateAsync_WhenRepositoryFailsAfterFlowSave_ShouldRestoreFlowSchemaAndSession()
    {
        var repository = Substitute.For<IProjectRepository>();
        var storage = Substitute.For<IProjectFlowStorage>();
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var project = new Project("demo");
        project.UpdateGlobalVariables(CreateSchema(variableId, 1));
        const string oldFlowJson = "{\"name\":\"old\",\"operators\":[],\"connections\":[]}";
        repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        repository
            .When(item => item.UpdateAsync(Arg.Any<Project>()))
            .Do(_ => throw new InvalidOperationException("db failed"));
        storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(oldFlowJson));
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
        await storage.Received().SaveFlowJsonAsync(project.Id, oldFlowJson);
        registry.GetOrCreate(project.Id, project.GlobalVariables).Should().BeSameAs(oldSession);
    }

    [Fact]
    public async Task DeleteAsync_WhenProjectVariableStateExists_ShouldDeletePersistedStateFilesAndDropRegistrySession()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectDeleteState", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = Substitute.For<IProjectRepository>();
            var storage = Substitute.For<IProjectFlowStorage>();
            var registry = new ProjectVariableSessionRegistry(new JsonFileProjectVariableStateStore(root));
            var variableId = Guid.NewGuid();
            var project = new Project("demo");
            project.UpdateGlobalVariables(CreateSchema(variableId, 1));

            repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
            repository.UpdateAsync(Arg.Any<Project>()).Returns(callInfo => Task.FromResult(callInfo.Arg<Project>()));
            storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));

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

            var sut = new ProjectService(repository, storage, new OperatorFactory(), null, registry);

            await sut.DeleteAsync(project.Id);

            project.IsDeleted.Should().BeTrue();
            registry.GetOrCreate(project.Id, project.GlobalVariables).Should().NotBeSameAs(authoritative);
            registry.GetOrCreate(project.Id, project.GlobalVariables).TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
            Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)).Should().Be(1L);
            snapshot.Version.Should().Be(0);
            Directory.EnumerateFileSystemEntries(root).Should().BeEmpty();
            await repository.Received(1).UpdateAsync(project);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
}
