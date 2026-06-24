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
    public async Task UpdateAsync_WhenGlobalVariablesChange_ShouldRefreshProjectSessionRegistry()
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
        refreshedSession.TryGetValue(variableId, out var value).Should().BeTrue();
        Convert.ToInt64(ProjectVariableValueConverter.ToObject(value)).Should().Be(3L);
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

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue)
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
}
