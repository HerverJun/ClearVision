using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Repositories;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.General, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "product")]
[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
public sealed class ProjectPersistenceConcurrencyTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly string _flowRoot;
    private readonly string _stateRoot;
    private readonly string _transactionRoot;
    private readonly DbContextOptions<VisionDbContext> _options;

    public ProjectPersistenceConcurrencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ClearVision.ProjectPersistenceConcurrency", Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_root, "vision.db");
        _flowRoot = Path.Combine(_root, "flows");
        _stateRoot = Path.Combine(_root, "state");
        _transactionRoot = Path.Combine(_root, "transactions");
        Directory.CreateDirectory(_root);

        _options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UpdateAsync_WhenDbContextTracksStaleProject_ShouldRejectStaleExpectedRevisionWithoutOverwrite()
    {
        ProjectSaveCoordinator.ResetStaticStateForTests();
        try
        {
            var variableId = Guid.NewGuid();
            var initialSchema = CreateSchema(variableId, 1, "stats.count");
            var remoteSchema = CreateSchema(variableId, 2, "stats.remote");
            var retrySchema = CreateSchema(variableId, 3, "stats.retry");
            var projectId = await SeedProjectAsync(initialSchema);
            var flowStorage = new JsonFileProjectFlowStorage(_flowRoot);
            await flowStorage.SaveFlowJsonAsync(projectId, SerializeFlow("initial-flow"), 0);
            var stateStore = new JsonFileProjectVariableStateStore(_stateRoot);
            var registry = new ProjectVariableSessionRegistry(stateStore);
            registry.GetOrCreate(projectId, initialSchema)
                .SetValue(variableId, 11L, ProjectVariableUpdatedBy.StudioManual);

            await using var staleContext = CreateContext();
            var staleRepository = new ProjectRepository(staleContext);
            var tracked = await staleRepository.GetByIdAsync(projectId);
            tracked.Should().NotBeNull();
            tracked!.PersistenceRevision.Should().Be(0);

            await using (var remoteContext = CreateContext())
            {
                var remoteService = CreateService(remoteContext, flowStorage, registry);
                var schemaSaved = await remoteService.UpdateGlobalVariablesAsync(
                    projectId,
                    new UpdateProjectGlobalVariablesRequest
                    {
                        Schema = remoteSchema,
                        ExpectedPersistenceRevision = 0
                    });
                var remote = await remoteService.UpdateAsync(projectId, new UpdateProjectRequest
                {
                    Name = "remote",
                    Description = "committed elsewhere",
                    Flow = CreateFlow("remote-flow"),
                    ExpectedPersistenceRevision = schemaSaved.PersistenceRevision
                });

                remote.PersistenceRevision.Should().Be(2);
            }

            var staleService = CreateService(staleContext, flowStorage, registry);
            var staleSave = async () => await staleService.UpdateAsync(projectId, new UpdateProjectRequest
            {
                Name = "stale",
                Description = "must not win",
                Flow = CreateFlow("stale-flow"),
                ExpectedPersistenceRevision = 0
            });

            await staleSave.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV011*");
            await AssertPersistedProjectAsync(projectId, "remote", "committed elsewhere", 2, remoteSchema);
            (await flowStorage.LoadFlowJsonAsync(projectId)).Should().Contain("remote-flow").And.NotContain("stale-flow");
            AssertVariableState(projectId, remoteSchema, 1, 11L);
            EnumerateManifests().Should().BeEmpty();

            var reread = await staleService.GetByIdAsync(projectId);
            reread.Should().NotBeNull();
            reread!.PersistenceRevision.Should().Be(2);
            var retrySchemaSaved = await staleService.UpdateGlobalVariablesAsync(
                projectId,
                new UpdateProjectGlobalVariablesRequest
                {
                    Schema = retrySchema,
                    ExpectedPersistenceRevision = 2
                });
            var retry = await staleService.UpdateAsync(projectId, new UpdateProjectRequest
            {
                Name = "retry",
                Description = "after reread",
                Flow = CreateFlow("retry-flow"),
                ExpectedPersistenceRevision = retrySchemaSaved.PersistenceRevision
            });

            retry.PersistenceRevision.Should().Be(4);
            await AssertPersistedProjectAsync(projectId, "retry", "after reread", 4, retrySchema);
            (await flowStorage.LoadFlowJsonAsync(projectId)).Should().Contain("retry-flow");
            AssertVariableState(projectId, retrySchema, 3, 11L);
            EnumerateManifests().Should().BeEmpty();
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenFirstMutationHoldsProjectAccess_ShouldSerializeSecondAndRejectItsStaleRevision()
    {
        ProjectSaveCoordinator.ResetStaticStateForTests();
        try
        {
            var variableId = Guid.NewGuid();
            var initialSchema = CreateSchema(variableId, 1, "stats.count");
            var projectId = await SeedProjectAsync(initialSchema);
            var flowStorage = new JsonFileProjectFlowStorage(_flowRoot);
            await flowStorage.SaveFlowJsonAsync(projectId, SerializeFlow("initial-flow"), 0);
            var registry = new ProjectVariableSessionRegistry(new JsonFileProjectVariableStateStore(_stateRoot));
            var blocker = new BlockingOperatorFactory(new OperatorFactory());

            await using var firstContext = CreateContext();
            var firstRepository = new ProjectRepository(firstContext);
            var firstCoordinator = new ProjectSaveCoordinator(
                firstRepository,
                flowStorage,
                registry,
                _transactionRoot);
            var firstService = new ProjectService(
                firstRepository,
                flowStorage,
                blocker,
                null,
                registry,
                firstCoordinator);

            var firstSave = firstService.UpdateAsync(projectId, new UpdateProjectRequest
            {
                Name = "first",
                Description = "stale after pause",
                Flow = CreateBlockingFlow("first-flow"),
                ExpectedPersistenceRevision = 0
            });
            await blocker.WaitUntilBlockedAsync();

            await using var secondContext = CreateContext();
            var secondService = CreateService(secondContext, flowStorage, registry);
            var secondSave = secondService.UpdateAsync(projectId, new UpdateProjectRequest
            {
                Name = "second",
                Description = "must become stale",
                Flow = CreateFlow("second-flow"),
                ExpectedPersistenceRevision = 0
            });

            secondSave.IsCompleted.Should().BeFalse(
                "the first mutation retains project access while preparing its authoritative candidate");

            blocker.Release();
            var first = await firstSave.WaitAsync(TimeSpan.FromSeconds(3));
            first.PersistenceRevision.Should().Be(1);
            var secondAct = async () => await secondSave.WaitAsync(TimeSpan.FromSeconds(3));
            await secondAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV011*");
            await AssertPersistedProjectAsync(projectId, "first", "stale after pause", 1, initialSchema);
            (await flowStorage.LoadFlowJsonAsync(projectId)).Should().Contain("first-flow").And.NotContain("second-flow");
            EnumerateManifests().Should().BeEmpty();
        }
        finally
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }

    private VisionDbContext CreateContext() => new(_options);

    private async Task<Guid> SeedProjectAsync(ProjectGlobalVariableSchema schema)
    {
        await using var context = CreateContext();
        var repository = new ProjectRepository(context);
        var project = new Project("initial", "seed");
        project.UpdateGlobalVariables(schema);
        await repository.AddAsync(project);
        return project.Id;
    }

    private ProjectService CreateService(
        VisionDbContext context,
        IProjectFlowStorage flowStorage,
        ProjectVariableSessionRegistry registry)
    {
        var repository = new ProjectRepository(context);
        var coordinator = new ProjectSaveCoordinator(
            repository,
            flowStorage,
            registry,
            _transactionRoot);
        return new ProjectService(repository, flowStorage, new OperatorFactory(), null, registry, coordinator);
    }

    private async Task AssertPersistedProjectAsync(
        Guid projectId,
        string name,
        string? description,
        long revision,
        ProjectGlobalVariableSchema schema)
    {
        await using var context = CreateContext();
        var repository = new ProjectRepository(context);
        var project = await repository.GetByIdFreshAsync(projectId);
        project.Should().NotBeNull();
        project!.Name.Should().Be(name);
        project.Description.Should().Be(description);
        project.PersistenceRevision.Should().Be(revision);
        ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(project.GlobalVariables)
            .Should()
            .Be(ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema));
    }

    private void AssertVariableState(
        Guid projectId,
        ProjectGlobalVariableSchema schema,
        long persistenceRevision,
        long value)
    {
        var store = new JsonFileProjectVariableStateStore(_stateRoot);
        var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(projectId);
        var metadata = store.LoadMetadata(scopeId);
        metadata.Should().NotBeNull();
        metadata!.PersistenceRevision.Should().Be(persistenceRevision);
        var snapshot = store.Load(scopeId, schema).Should().ContainSingle().Subject;
        Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)).Should().Be(value);
    }

    private IEnumerable<string> EnumerateManifests() =>
        Directory.Exists(_transactionRoot)
            ? Directory.EnumerateFiles(_transactionRoot, "manifest.json", SearchOption.AllDirectories)
            : [];

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue, string name)
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

    private static OperatorFlowDto CreateFlow(string name) => new()
    {
        Name = name,
        Operators = [],
        Connections = []
    };

    private static OperatorFlowDto CreateBlockingFlow(string name) => new()
    {
        Name = name,
        Operators =
        [
            new OperatorDto
            {
                Id = Guid.NewGuid(),
                Name = "BlockingThreshold",
                Type = OperatorType.Thresholding
            }
        ],
        Connections = []
    };

    private static string SerializeFlow(string name) => JsonSerializer.Serialize(CreateFlow(name));

    private sealed class BlockingOperatorFactory : IOperatorFactory
    {
        private readonly IOperatorFactory _inner;
        private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hasBlocked;

        public BlockingOperatorFactory(IOperatorFactory inner)
        {
            _inner = inner;
        }

        public Operator CreateOperator(OperatorType type, string name, double x, double y) =>
            _inner.CreateOperator(type, name, x, y);

        public OperatorMetadata? GetMetadata(OperatorType type)
        {
            if (Interlocked.Exchange(ref _hasBlocked, 1) == 0)
            {
                _blocked.TrySetResult();
                _release.Task.GetAwaiter().GetResult();
            }

            return _inner.GetMetadata(type);
        }

        public IEnumerable<OperatorMetadata> GetAllMetadata() => _inner.GetAllMetadata();

        public IEnumerable<OperatorType> GetSupportedOperatorTypes() => _inner.GetSupportedOperatorTypes();

        public void RegisterOperator(OperatorMetadata metadata) => _inner.RegisterOperator(metadata);

        public Task WaitUntilBlockedAsync() => _blocked.Task;

        public void Release() => _release.TrySetResult();
    }
}
