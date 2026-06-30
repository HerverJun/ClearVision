using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Repositories;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ClearVision.Product.Tests.Integration;

public sealed class RepositoryBaseValidationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<VisionDbContext> _options;
    private readonly VisionDbContext _context;
    private readonly ProjectRepository _repository;

    public RepositoryBaseValidationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new VisionDbContext(_options);
        _context.Database.EnsureCreated();

        _repository = new ProjectRepository(_context);
    }

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        Action act = () => new ProjectRepository(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("context");
    }

    [Fact]
    public async Task FindAsync_NullPredicate_ThrowsArgumentNullException()
    {
        Func<Task> act = async () => await _repository.FindAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("predicate");
    }

    [Fact]
    public async Task AddAsync_NullEntity_ThrowsArgumentNullException()
    {
        Func<Task> act = async () => await _repository.AddAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entity");
    }

    [Fact]
    public async Task GetByIdFreshAsync_WhenContextTracksOldProject_ShouldReturnDatabaseState()
    {
        var project = new Project("demo");
        await _repository.AddAsync(project);
        var tracked = await _repository.GetByIdAsync(project.Id);
        tracked.Should().NotBeNull();
        tracked!.PersistenceRevision.Should().Be(0);
        await using (var updateContext = new VisionDbContext(_options))
        {
            var updateRepository = new ProjectRepository(updateContext);
            var latest = await updateRepository.GetByIdAsync(project.Id);
            latest.Should().NotBeNull();
            latest!.SetPersistenceRevision(1);
            await updateRepository.UpdateAsync(latest);
        }

        var staleRead = await _repository.GetByIdAsync(project.Id);
        var freshRead = await _repository.GetByIdFreshAsync(project.Id);

        staleRead.Should().BeSameAs(tracked);
        staleRead!.PersistenceRevision.Should().Be(0);
        freshRead.Should().NotBeNull();
        freshRead.Should().NotBeSameAs(tracked);
        freshRead!.PersistenceRevision.Should().Be(1);
    }

    [Fact]
    public async Task ProjectServiceLists_WhenContextTracksOldProject_ShouldMapFreshEntityInsideProjectGate()
    {
        ProjectSaveCoordinator.ResetStaticStateForTests();
        try
        {
            var project = new Project("demo");
            project.RecordOpen();
            await _repository.AddAsync(project);
            var tracked = await _repository.GetByIdAsync(project.Id);
            tracked.Should().NotBeNull();
            tracked!.PersistenceRevision.Should().Be(0);
            await using (var updateContext = new VisionDbContext(_options))
            {
                var updateRepository = new ProjectRepository(updateContext);
                var latest = await updateRepository.GetByIdAsync(project.Id);
                latest.Should().NotBeNull();
                latest!.SetPersistenceRevision(1);
                await updateRepository.UpdateAsync(latest);
            }

            var staleRead = await _repository.GetByIdAsync(project.Id);
            staleRead.Should().BeSameAs(tracked);
            staleRead!.PersistenceRevision.Should().Be(0);
            var flowStorage = Substitute.For<IProjectFlowStorage>();
            flowStorage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
            var service = new ProjectService(_repository, flowStorage, new OperatorFactory());

            var all = (await service.GetAllAsync()).ToList();
            var search = (await service.SearchAsync("demo")).ToList();
            var recent = (await service.GetRecentlyOpenedAsync()).ToList();

            all.Should().ContainSingle(item => item.Id == project.Id).Which.PersistenceRevision.Should().Be(1);
            search.Should().ContainSingle(item => item.Id == project.Id).Which.PersistenceRevision.Should().Be(1);
            recent.Should().ContainSingle(item => item.Id == project.Id).Which.PersistenceRevision.Should().Be(1);
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
        }
    }

    [Fact]
    public async Task UpdateAsync_NullEntity_ThrowsArgumentNullException()
    {
        Func<Task> act = async () => await _repository.UpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entity");
    }

    [Fact]
    public async Task DeleteAsync_NullEntity_ThrowsArgumentNullException()
    {
        Func<Task> act = async () => await _repository.DeleteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entity");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
