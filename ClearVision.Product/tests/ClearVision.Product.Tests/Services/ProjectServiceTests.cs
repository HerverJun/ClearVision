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
}
