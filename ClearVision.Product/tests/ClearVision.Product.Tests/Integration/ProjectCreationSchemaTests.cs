// ProjectCreationSchemaTests.cs
// ProjectCreationSchemaTests测试
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.General, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "product")]
public class ProjectCreationSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly VisionDbContext _context;
    private readonly ProjectRepository _repository;

    public ProjectCreationSchemaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new VisionDbContext(options);
        _context.Database.EnsureCreated();

        _repository = new ProjectRepository(_context);
    }

    [Fact]
    public async Task CreateProject_ShouldSaveToDatabase()
    {
        // Arrange
        var project = new Project("Test Project", "Description");

        // Ensure properties are set correctly
        project.Flow.Should().NotBeNull();
        project.Flow.Name.Should().Be("默认流程");
        project.Flow.Id.Should().Be(project.Id);

        // Act
        await _repository.AddAsync(project);

        // Assert
        var savedProject = await _repository.GetWithFlowAsync(project.Id);
        savedProject.Should().NotBeNull();
        savedProject!.Name.Should().Be("Test Project");
        savedProject.Flow.Should().NotBeNull();
        savedProject.Flow.Name.Should().Be("默认流程");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
