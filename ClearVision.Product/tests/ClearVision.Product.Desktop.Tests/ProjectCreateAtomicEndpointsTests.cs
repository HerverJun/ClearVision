using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Repositories;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
[TestClassification(TestDomain.Desktop, TestPurpose.Integration, TestLane.Pr, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]
public sealed class ProjectCreateAtomicEndpointsTests
{
    [Fact]
    public async Task ProjectCreate_WhenSuccessful_ShouldBeFullyReadableBeforeApiReturns()
    {
        var root = CreateTempPath();
        try
        {
            await using var host = await AtomicCreateHost.CreateAsync(root);
            var variableId = Guid.NewGuid();

            using var response = await host.Client.PostAsJsonAsync(
                "/api/projects",
                CreateRequest("complete-project", variableId));

            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var created = await response.Content.ReadFromJsonAsync<ProjectDto>();
            created.Should().NotBeNull();
            created!.PersistenceRevision.Should().Be(0);
            using var detail = await host.Client.GetAsync($"/api/projects/{created.Id}");
            detail.StatusCode.Should().Be(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync());
            var reloaded = await detail.Content.ReadFromJsonAsync<ProjectDto>();
            reloaded.Should().NotBeNull();
            reloaded!.Id.Should().Be(created.Id);
            reloaded.Name.Should().Be("complete-project");
            reloaded.Flow!.Name.Should().Be("create-flow");
            reloaded.GlobalVariables.Variables.Should().ContainSingle(item => item.Id == variableId);
            (await host.FlowStorage.LoadMetadataAsync(created.Id))!.PersistenceRevision.Should().Be(0);
            (await host.AssetStorage.LoadMetadataAsync(created.Id))!.PersistenceRevision.Should().Be(0);
            host.Registry.LoadStateMetadata(created.Id)!.PersistenceRevision.Should().Be(0);
            EnumerateManifests(root).Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Theory]
    [InlineData(ProjectSaveFailurePoint.AfterProjectApply)]
    [InlineData(ProjectSaveFailurePoint.AfterFlowApply)]
    [InlineData(ProjectSaveFailurePoint.AfterVariableStateApply)]
    [InlineData(ProjectSaveFailurePoint.BeforeComplete)]
    public async Task ProjectCreate_WhenCommitStageFails_ShouldReturnFailureAndRemainInvisible(
        ProjectSaveFailurePoint failurePoint)
    {
        var root = CreateTempPath();
        try
        {
            var injector = new CapturingFailureInjector(failurePoint);
            await using var host = await AtomicCreateHost.CreateAsync(root, injector: injector);

            using var response = await host.Client.PostAsJsonAsync(
                "/api/projects",
                CreateRequest($"failed-{failurePoint}", Guid.NewGuid()));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            injector.ProjectId.Should().NotBeNull();
            await AssertProjectInvisibleAsync(host, injector.ProjectId!.Value);
            (await host.FlowStorage.LoadFlowJsonAsync(injector.ProjectId.Value)).Should().BeNull();
            (await host.FlowStorage.LoadMetadataAsync(injector.ProjectId.Value)).Should().BeNull();
            (await host.AssetStorage.LoadMetadataAsync(injector.ProjectId.Value)).Should().BeNull();
            host.Registry.LoadStateMetadata(injector.ProjectId.Value).Should().BeNull();
            EnumerateManifests(root).Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ProjectCreate_WhenDatabaseInsertFails_ShouldReturnFailureAndRemainInvisible()
    {
        var root = CreateTempPath();
        try
        {
            var injector = new CapturingFailureInjector();
            await using var host = await AtomicCreateHost.CreateAsync(
                root,
                injector: injector,
                failDatabaseInsert: true);

            using var response = await host.Client.PostAsJsonAsync(
                "/api/projects",
                CreateRequest("database-failure", Guid.NewGuid()));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            injector.ProjectId.Should().NotBeNull();
            await AssertProjectInvisibleAsync(host, injector.ProjectId!.Value);
            (await host.FlowStorage.LoadFlowJsonAsync(injector.ProjectId.Value)).Should().BeNull();
            EnumerateManifests(root).Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Theory]
    [InlineData(CreateFlowFailureMode.BeforeWrite)]
    [InlineData(CreateFlowFailureMode.AfterBodyAndMetadataWrite)]
    public async Task ProjectCreate_WhenFlowPersistenceFails_ShouldRemoveDatabaseAndFlowArtifacts(
        CreateFlowFailureMode failureMode)
    {
        var root = CreateTempPath();
        try
        {
            var injector = new CapturingFailureInjector();
            await using var host = await AtomicCreateHost.CreateAsync(
                root,
                injector: injector,
                flowFailureMode: failureMode);

            using var response = await host.Client.PostAsJsonAsync(
                "/api/projects",
                CreateRequest($"flow-failure-{failureMode}", Guid.NewGuid()));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            injector.ProjectId.Should().NotBeNull();
            await AssertProjectInvisibleAsync(host, injector.ProjectId!.Value);
            (await host.FlowStorage.LoadFlowJsonAsync(injector.ProjectId.Value)).Should().BeNull();
            (await host.FlowStorage.LoadMetadataAsync(injector.ProjectId.Value)).Should().BeNull();
            EnumerateManifests(root).Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ProjectCreate_WhenRollbackIsInterrupted_ShouldStayOpaqueAndRollbackDuringRestartRecovery()
    {
        var root = CreateTempPath();
        Guid projectId;
        try
        {
            var injector = new CapturingFailureInjector(
                ProjectSaveFailurePoint.AfterProjectApply,
                ProjectSaveFailurePoint.BeforeCreateRollback);
            await using (var firstHost = await AtomicCreateHost.CreateAsync(root, injector: injector))
            {
                using var response = await firstHost.Client.PostAsJsonAsync(
                    "/api/projects",
                    CreateRequest("interrupted-create", Guid.NewGuid()));

                response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
                injector.ProjectId.Should().NotBeNull();
                projectId = injector.ProjectId!.Value;
                await AssertProjectInvisibleAsync(firstHost, projectId);
                (await firstHost.CountDatabaseRowsAsync(projectId)).Should().Be(1);
                EnumerateManifests(root).Should().ContainSingle();
            }

            ProjectSaveCoordinator.ResetStaticStateForTests();
            await using (var restartedHost = await AtomicCreateHost.CreateAsync(root))
            {
                await AssertProjectInvisibleAsync(restartedHost, projectId);
                (await restartedHost.CountDatabaseRowsAsync(projectId)).Should().Be(0);
                (await restartedHost.FlowStorage.LoadFlowJsonAsync(projectId)).Should().BeNull();
                (await restartedHost.AssetStorage.LoadMetadataAsync(projectId)).Should().BeNull();
                restartedHost.Registry.LoadStateMetadata(projectId).Should().BeNull();
                EnumerateManifests(root).Should().BeEmpty();
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ProjectCreate_WhenSameNameIsConcurrent_ShouldCommitAtMostOne()
    {
        var root = CreateTempPath();
        try
        {
            await using var host = await AtomicCreateHost.CreateAsync(root);

            var first = host.Client.PostAsJsonAsync(
                "/api/projects",
                CreateRequest("same-name", Guid.NewGuid()));
            var second = host.Client.PostAsJsonAsync(
                "/api/projects",
                CreateRequest("same-name", Guid.NewGuid()));
            using var firstResponse = await first;
            using var secondResponse = await second;

            new[] { firstResponse.StatusCode, secondResponse.StatusCode }
                .Count(code => code == HttpStatusCode.Created)
                .Should().Be(1);
            new[] { firstResponse.StatusCode, secondResponse.StatusCode }
                .Count(code => code == HttpStatusCode.Conflict)
                .Should().Be(1);
            using var list = await host.Client.GetAsync("/api/projects");
            var projects = await list.Content.ReadFromJsonAsync<List<ProjectDto>>();
            projects.Should().ContainSingle(item => item.Name == "same-name");
            EnumerateManifests(root).Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static CreateProjectRequest CreateRequest(string name, Guid variableId) => new()
    {
        Name = name,
        Description = "atomic create",
        Flow = new OperatorFlowDto
        {
            Name = "create-flow",
            Operators = [],
            Connections = []
        },
        GlobalVariables = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(1L),
                    ManualWriteAllowed = true
                }
            ]
        }
    };

    private static async Task AssertProjectInvisibleAsync(AtomicCreateHost host, Guid projectId)
    {
        using var listResponse = await host.Client.GetAsync("/api/projects");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, await listResponse.Content.ReadAsStringAsync());
        var projects = await listResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        projects.Should().NotContain(item => item.Id == projectId);

        using var detailResponse = await host.Client.GetAsync($"/api/projects/{projectId}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await detailResponse.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    private static IReadOnlyList<string> EnumerateManifests(string root)
    {
        var transactionRoot = Path.Combine(root, "transactions");
        return Directory.Exists(transactionRoot)
            ? Directory.EnumerateFiles(transactionRoot, "manifest.json", SearchOption.AllDirectories).ToList()
            : [];
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), "ClearVision.ProjectCreateAtomicEndpoints", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string root)
    {
        ProjectSaveCoordinator.ResetStaticStateForTests();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CapturingFailureInjector : IProjectSaveFailureInjector
    {
        private readonly HashSet<ProjectSaveFailurePoint> _failurePoints;

        public CapturingFailureInjector(params ProjectSaveFailurePoint[] failurePoints)
        {
            _failurePoints = failurePoints.ToHashSet();
        }

        public Guid? ProjectId { get; private set; }

        public Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest)
        {
            ProjectId ??= manifest.ProjectId;
            if (_failurePoints.Contains(point))
            {
                throw new InvalidOperationException($"injected create failure at {point}");
            }

            return Task.CompletedTask;
        }
    }

    public enum CreateFlowFailureMode
    {
        None = 0,
        BeforeWrite = 1,
        AfterBodyAndMetadataWrite = 2
    }

    private sealed class FaultingCreateFlowStorage : IProjectFlowStorage
    {
        private readonly IProjectFlowStorage _inner;
        private readonly CreateFlowFailureMode _failureMode;
        private int _failuresRemaining = 1;

        public FaultingCreateFlowStorage(IProjectFlowStorage inner, CreateFlowFailureMode failureMode)
        {
            _inner = inner;
            _failureMode = failureMode;
        }

        public async Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            if (_failureMode == CreateFlowFailureMode.BeforeWrite && Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
            {
                throw new IOException("flow temp write failed");
            }

            await _inner.SaveFlowJsonAsync(projectId, flowJson);
            if (_failureMode == CreateFlowFailureMode.AfterBodyAndMetadataWrite &&
                Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
            {
                throw new IOException("flow metadata failed after body write");
            }
        }

        public async Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
        {
            if (_failureMode == CreateFlowFailureMode.BeforeWrite && Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
            {
                throw new IOException("flow temp write failed");
            }

            await _inner.SaveFlowJsonAsync(projectId, flowJson, persistenceRevision);
            if (_failureMode == CreateFlowFailureMode.AfterBodyAndMetadataWrite &&
                Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
            {
                throw new IOException("flow metadata failed after body write");
            }
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId) => _inner.LoadFlowJsonAsync(projectId);

        public Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId) => _inner.LoadMetadataAsync(projectId);

        public Task DeleteFlowJsonAsync(Guid projectId) => _inner.DeleteFlowJsonAsync(projectId);
    }

    private sealed class FailingAddProjectRepository : IProjectRepository
    {
        private readonly IProjectRepository _inner;
        private int _failuresRemaining = 1;

        public FailingAddProjectRepository(IProjectRepository inner)
        {
            _inner = inner;
        }

        public Task<Project?> GetByIdAsync(Guid id) => _inner.GetByIdAsync(id);
        public Task<Project?> GetByIdFreshAsync(Guid id) => _inner.GetByIdFreshAsync(id);
        public Task<Project?> GetByIdForUpdateAsync(Guid id) => _inner.GetByIdForUpdateAsync(id);
        public Task<IEnumerable<Project>> GetAllAsync() => _inner.GetAllAsync();
        public Task<IEnumerable<Project>> FindAsync(Expression<Func<Project, bool>> predicate) => _inner.FindAsync(predicate);

        public Task<Project> AddAsync(Project entity)
        {
            if (Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
            {
                throw new IOException("database insert failed");
            }

            return _inner.AddAsync(entity);
        }

        public Task UpdateAsync(Project entity) => _inner.UpdateAsync(entity);
        public Task DeleteAsync(Project entity) => _inner.DeleteAsync(entity);
        public Task DeleteByIdAsync(Guid id) => _inner.DeleteByIdAsync(id);
        public Task<bool> ExistsAsync(Guid id) => _inner.ExistsAsync(id);
        public Task<Project?> GetByNameAsync(string name) => _inner.GetByNameAsync(name);
        public Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10) => _inner.GetRecentlyOpenedAsync(count);
        public Task<IEnumerable<Project>> SearchAsync(string keyword) => _inner.SearchAsync(keyword);
        public Task<Project?> GetWithFlowAsync(Guid id) => _inner.GetWithFlowAsync(id);
        public Task UpdateFlowAsync(Project project) => _inner.UpdateFlowAsync(project);
    }

    private sealed class AtomicCreateHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private AtomicCreateHost(
            WebApplication app,
            IProjectFlowStorage flowStorage,
            IProjectAssetStorage assetStorage,
            ProjectVariableSessionRegistry registry)
        {
            _app = app;
            Client = app.GetTestClient();
            FlowStorage = flowStorage;
            AssetStorage = assetStorage;
            Registry = registry;
        }

        public HttpClient Client { get; }

        public IProjectFlowStorage FlowStorage { get; }

        public IProjectAssetStorage AssetStorage { get; }

        public ProjectVariableSessionRegistry Registry { get; }

        public static async Task<AtomicCreateHost> CreateAsync(
            string root,
            CapturingFailureInjector? injector = null,
            bool failDatabaseInsert = false,
            CreateFlowFailureMode flowFailureMode = CreateFlowFailureMode.None)
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "vision.db");
            var transactionRoot = Path.Combine(root, "transactions");
            var innerFlowStorage = new JsonFileProjectFlowStorage(Path.Combine(root, "flows"));
            IProjectFlowStorage flowStorage = flowFailureMode == CreateFlowFailureMode.None
                ? innerFlowStorage
                : new FaultingCreateFlowStorage(innerFlowStorage, flowFailureMode);
            IProjectAssetStorage assetStorage = new JsonFileProjectAssetStorage(Path.Combine(root, "assets"));
            var registry = new ProjectVariableSessionRegistry(
                new JsonFileProjectVariableStateStore(Path.Combine(root, "variable-state")));
            var runtimeCoordinator = Substitute.For<IInspectionRuntimeCoordinator>();
            runtimeCoordinator
                .TryAcquireMutationLeaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<ProjectMutationLease?>(new ProjectMutationLease(
                    call.ArgAt<Guid>(0),
                    call.ArgAt<string>(1),
                    () => ValueTask.CompletedTask)));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<VisionDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
            builder.Services.AddScoped<IProjectRepository>(services =>
            {
                IProjectRepository repository = new ProjectRepository(services.GetRequiredService<VisionDbContext>());
                return failDatabaseInsert ? new FailingAddProjectRepository(repository) : repository;
            });
            builder.Services.AddSingleton(flowStorage);
            builder.Services.AddSingleton(assetStorage);
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton(runtimeCoordinator);
            builder.Services.AddSingleton<IOperatorFactory>(new OperatorFactory());
            builder.Services.AddSingleton<ILogger<ProjectService>>(NullLogger<ProjectService>.Instance);
            builder.Services.AddScoped(services => new ProjectSaveCoordinator(
                services.GetRequiredService<IProjectRepository>(),
                flowStorage,
                registry,
                transactionRoot,
                injector,
                assetStorage));
            builder.Services.AddScoped<ProjectService>();
            builder.Services.AddScoped(services => new RuntimePackageExporter(
                services.GetRequiredService<IOperatorFactory>(),
                NullLogger<RuntimePackageExporter>.Instance));
            builder.Services.AddSingleton(services => new StationPackageStore(
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StationPackageStore>.Instance));
            builder.Services.AddHostedService<ProjectSaveRecoveryHostedService>();

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = "atomic-create-admin",
                    Username = "atomic-create-admin",
                    Role = UserRole.Admin.ToString(),
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
                await next();
            });
            MapProjectEndpoints(app);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
            }

            await app.StartAsync();
            return new AtomicCreateHost(app, flowStorage, assetStorage, registry);
        }

        public async Task<int> CountDatabaseRowsAsync(Guid projectId)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<VisionDbContext>()
                .Projects
                .CountAsync(project => project.Id == projectId);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            SqliteConnection.ClearAllPools();
        }

        private static void MapProjectEndpoints(IEndpointRouteBuilder app)
        {
            var method = typeof(ApiEndpoints).GetMethod(
                "MapProjectEndpoints",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();
            method!.Invoke(null, [app]);
        }
    }
}
