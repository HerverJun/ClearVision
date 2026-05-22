using Acme.Product.Application.Services;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Acme.Product.Infrastructure.Services;
using Acme.Product.Tests.TestSupport;
using FluentAssertions;
using NSubstitute;

namespace Acme.Product.Tests.Services;

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
}
