using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Handlers;
using ClearVision.Product.Desktop.PreviewArtifacts;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
[TestClassification(TestDomain.Calibration, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class PlanarCalibrationWebMessageTests
{
    [Fact]
    public async Task ValidSolveArtifact_ShouldSaveProjectAssetAtExpectedRevision()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();

        var payload = await harness.SaveAsync(artifactId);

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("persistenceRevision").GetInt64().Should().Be(1);
        payload.GetProperty("asset").GetProperty("assetId").GetString().Should().Be(PlanarHarness.AssetId);
        harness.AssetStorage.Metadata.Should().NotBeNull();
        harness.AssetStorage.Metadata!.PersistenceRevision.Should().Be(1);
        harness.AssetStorage.Assets.CalibrationAssets.Should().ContainSingle(asset =>
            asset.AssetId == PlanarHarness.AssetId &&
            asset.SourceDraftSessionId == PlanarHarness.SessionId &&
            asset.ImageIdentity == PlanarHarness.CameraBindingId);
    }

    [Fact]
    public async Task ForgedResultAndArtifactId_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        await using var harness = await PlanarHarness.CreateAsync();

        var payload = await harness.DispatchForPayloadAsync(
            "planar2d:save",
            "token-a",
            new
            {
                solveArtifactId = new string('A', 43),
                projectId = harness.Project.Id,
                expectedPersistenceRevision = 0,
                assetId = PlanarHarness.AssetId,
                sessionId = PlanarHarness.SessionId,
                cameraBindingId = PlanarHarness.CameraBindingId,
                fileName = "forged.json",
                result = new { success = true, accepted = true, scaleX = 999 }
            });

        AssertNotFoundWithoutSave(harness, payload);
        await harness.RuntimeCoordinator.DidNotReceiveWithAnyArgs().TryAcquireMutationLeaseAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SolveArtifact_WrongOwner_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();

        var payload = await harness.SaveAsync(artifactId, token: "token-b", bindingId: "binding-b");

        AssertNotFoundWithoutSave(harness, payload);
    }

    [Fact]
    public async Task SolveArtifact_WrongProject_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();

        var payload = await harness.SaveAsync(artifactId, projectId: Guid.NewGuid());

        AssertNotFoundWithoutSave(harness, payload);
    }

    [Fact]
    public async Task SolveArtifact_Expired_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(2));

        var payload = await harness.SaveAsync(artifactId);

        AssertNotFoundWithoutSave(harness, payload);
        harness.ArtifactStore.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("wrong-session", PlanarHarness.AssetId, PlanarHarness.CameraBindingId)]
    [InlineData(PlanarHarness.SessionId, "wrong-asset", PlanarHarness.CameraBindingId)]
    [InlineData(PlanarHarness.SessionId, PlanarHarness.AssetId, "wrong-camera")]
    public async Task SolveArtifact_WrongAssetContext_ShouldReturnOpaqueNotFound(
        string sessionId,
        string assetId,
        string cameraBindingId)
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();

        var payload = await harness.SaveAsync(
            artifactId,
            sessionId: sessionId,
            assetId: assetId,
            cameraBindingId: cameraBindingId);

        AssertNotFoundWithoutSave(harness, payload);
    }

    [Fact]
    public async Task SolveArtifact_InvalidAcceptedBundle_ShouldReturnValidationWithoutSaving()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var bundle = PlanarScaleOffsetCalibrationService.CreateCalibrationBundle(
            PlanarHarness.AcceptedResult());
        bundle.Quality.Accepted = false;
        var artifactId = harness.AddArtifact(new
        {
            schemaVersion = "calibration-solve-provenance.v1",
            solveKind = "planar2d",
            projectId = harness.Project.Id,
            sessionId = PlanarHarness.SessionId,
            assetId = PlanarHarness.AssetId,
            cameraBindingId = PlanarHarness.CameraBindingId,
            bundle
        });

        var payload = await harness.SaveAsync(artifactId);

        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.Validation);
        harness.AssetStorage.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task SolveArtifact_RevisionConflict_ShouldReturnConflictWithoutWritingAsset()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();

        var payload = await harness.SaveAsync(artifactId, expectedRevision: 99);

        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.Conflict);
        harness.Project.PersistenceRevision.Should().Be(0);
        harness.AssetStorage.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task FormalSave_WithoutExpectedRevision_ShouldReturnValidationBeforeLease()
    {
        await using var harness = await PlanarHarness.CreateAsync();
        var artifactId = await harness.SolveAsync();

        var payload = await harness.DispatchForPayloadAsync(
            "planar2d:save",
            "token-a",
            new
            {
                solveArtifactId = artifactId,
                projectId = harness.Project.Id,
                assetId = PlanarHarness.AssetId,
                sessionId = PlanarHarness.SessionId,
                cameraBindingId = PlanarHarness.CameraBindingId
            });

        payload.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.Validation);
        harness.AssetStorage.Metadata.Should().BeNull();
        await harness.RuntimeCoordinator.DidNotReceiveWithAnyArgs().TryAcquireMutationLeaseAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static void AssertNotFoundWithoutSave(PlanarHarness harness, JsonElement payload)
    {
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.NotFound);
        harness.Project.PersistenceRevision.Should().Be(0);
        harness.AssetStorage.Metadata.Should().BeNull();
    }

    private sealed class PlanarHarness : IAsyncDisposable
    {
        public const string SessionId = "planar-session";
        public const string AssetId = "planar-asset";
        public const string CameraBindingId = "camera-binding";

        private readonly ServiceProvider _services;
        private readonly WebMessageHandler _handler;

        private PlanarHarness(
            ServiceProvider services,
            WebMessageHandler handler,
            Project project,
            RecordingAssetStorage assetStorage,
            PreviewArtifactStore artifactStore,
            FakeClock clock,
            IInspectionRuntimeCoordinator runtimeCoordinator)
        {
            _services = services;
            _handler = handler;
            Project = project;
            AssetStorage = assetStorage;
            ArtifactStore = artifactStore;
            Clock = clock;
            RuntimeCoordinator = runtimeCoordinator;
        }

        public Project Project { get; }
        public RecordingAssetStorage AssetStorage { get; }
        public PreviewArtifactStore ArtifactStore { get; }
        public FakeClock Clock { get; }
        public IInspectionRuntimeCoordinator RuntimeCoordinator { get; }

        public static Task<PlanarHarness> CreateAsync()
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            var project = new Project("planar provenance");
            var repository = new InMemoryProjectRepository(project);
            var assetStorage = new RecordingAssetStorage();
            var flowStorage = Substitute.For<IProjectFlowStorage>();
            flowStorage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
            var runtimeCoordinator = Substitute.For<IInspectionRuntimeCoordinator>();
            runtimeCoordinator.TryAcquireMutationLeaseAsync(
                    project.Id,
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<ProjectMutationLease?>(new ProjectMutationLease(
                    project.Id,
                    "test",
                    () => ValueTask.CompletedTask)));
            var clock = new FakeClock(new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero));
            var artifactStore = new PreviewArtifactStore(new PreviewArtifactStoreOptions
            {
                Ttl = TimeSpan.FromMinutes(1),
                MaxEntries = 32,
                MaxTotalBytes = 1024 * 1024,
                MaxEntryBytes = 512 * 1024
            }, clock);
            var authService = Substitute.For<IAuthService>();
            authService.GetSessionAsync("token-a").Returns(Task.FromResult<UserSession?>(new UserSession
            {
                UserId = "user-a",
                Username = "user-a",
                Role = "Engineer"
            }));
            authService.GetSessionAsync("token-b").Returns(Task.FromResult<UserSession?>(new UserSession
            {
                UserId = "user-b",
                Username = "user-b",
                Role = "Engineer"
            }));

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddSingleton(authService);
            serviceCollection.AddSingleton<IProjectRepository>(repository);
            serviceCollection.AddSingleton(flowStorage);
            serviceCollection.AddSingleton<IProjectAssetStorage>(assetStorage);
            serviceCollection.AddSingleton<IOperatorFactory>(new OperatorFactory());
            serviceCollection.AddSingleton(new ProjectVariableSessionRegistry());
            serviceCollection.AddSingleton(runtimeCoordinator);
            serviceCollection.AddSingleton(artifactStore);
            serviceCollection.AddSingleton<IPlanarScaleOffsetCalibrationService, PlanarScaleOffsetCalibrationService>();
            serviceCollection.AddSingleton(NullLogger<ProjectService>.Instance);
            serviceCollection.AddScoped<ProjectSaveCoordinator>();
            serviceCollection.AddScoped<ProjectService>();
            var services = serviceCollection.BuildServiceProvider();
            var eventStore = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
            var eventBus = new InMemoryInspectionEventBus(
                NullLogger<InMemoryInspectionEventBus>.Instance,
                eventStore);
            var handler = new WebMessageHandler(
                services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<IOperatorFactory>(),
                eventBus,
                NullLogger<WebMessageHandler>.Instance,
                NullLoggerFactory.Instance);
            return Task.FromResult(new PlanarHarness(
                services,
                handler,
                project,
                assetStorage,
                artifactStore,
                clock,
                runtimeCoordinator));
        }

        public async Task<string> SolveAsync()
        {
            var payload = await DispatchForPayloadAsync("planar2d:solve", "token-a", new
            {
                points = new[]
                {
                    new { pixelX = 100d, pixelY = 200d, physicalX = 10d, physicalY = 20d },
                    new { pixelX = 300d, pixelY = 500d, physicalX = 30d, physicalY = 50d },
                    new { pixelX = 600d, pixelY = 800d, physicalX = 60d, physicalY = 80d }
                },
                projectId = Project.Id,
                sessionId = SessionId,
                assetId = AssetId,
                cameraBindingId = CameraBindingId
            });
            payload.GetProperty("accepted").GetBoolean().Should().BeTrue();
            payload.GetProperty("canFormalSave").GetBoolean().Should().BeTrue();
            return payload.GetProperty("solveArtifact").GetProperty("artifactId").GetString()!;
        }

        public Task<JsonElement> SaveAsync(
            string artifactId,
            string token = "token-a",
            Guid? projectId = null,
            long expectedRevision = 0,
            string sessionId = SessionId,
            string assetId = AssetId,
            string cameraBindingId = CameraBindingId,
            string bindingId = "binding-a") =>
            DispatchForPayloadAsync("planar2d:save", token, new
            {
                solveArtifactId = artifactId,
                projectId = projectId ?? Project.Id,
                expectedPersistenceRevision = expectedRevision,
                assetId,
                sessionId,
                cameraBindingId
            }, bindingId);

        public async Task<JsonElement> DispatchForPayloadAsync(
            string messageType,
            string token,
            object payload,
            string bindingId = "binding-a")
        {
            var response = await _handler.DispatchWebMessageAsync(
                JsonSerializer.Serialize(new
                {
                    messageType,
                    requestId = $"wm-{Guid.NewGuid():N}",
                    payload,
                    bridge = new { token, bindingId, navigationEpoch = 1 }
                }),
                WebMessageAdmissionService.TrustedOrigin);
            response.Success.Should().BeTrue();
            var pending = PendingMessages();
            pending.Should().NotBeEmpty();
            using var document = JsonDocument.Parse(pending.Last());
            document.RootElement.GetProperty("messageType").GetString().Should().Be(messageType + ":result");
            return document.RootElement.GetProperty("payload").Clone();
        }

        public string AddArtifact(object provenance)
        {
            using var batch = ArtifactStore.CreateBatch(new PreviewArtifactOwnerScope(
                Project.Id,
                Guid.Empty,
                Guid.NewGuid(),
                null,
                null,
                "user-a"));
            var reference = batch.Add(
                "planar2dSolveBundle",
                "calibration-solve-provenance.v1",
                "$.Planar2D.SolveProvenance",
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(provenance));
            batch.Commit();
            return reference.ArtifactId;
        }

        public static PlanarScaleOffsetCalibrationResult AcceptedResult() => new()
        {
            Success = true,
            Accepted = true,
            OriginX = 1,
            OriginY = 2,
            ScaleX = 0.02,
            ScaleY = 0.03,
            MeanErrorX = 0.01,
            MeanErrorY = 0.01,
            Rmse = 0.02,
            PointCount = 4
        };

        private IReadOnlyList<string> PendingMessages()
        {
            var field = typeof(WebMessageHandler).GetField(
                "_pendingWebMessages",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var queue = (ConcurrentQueue<PendingWebMessage>)field.GetValue(_handler)!;
            return queue.Select(message => message.Json).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            _handler.Dispose();
            ArtifactStore.Dispose();
            await _services.DisposeAsync();
            ProjectSaveCoordinator.ResetStaticStateForTests();
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IPreviewArtifactClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan elapsed) => UtcNow = UtcNow.Add(elapsed);
    }

    private sealed class InMemoryProjectRepository(params Project[] projects) : IProjectRepository
    {
        private readonly Dictionary<Guid, Project> _projects = projects.ToDictionary(project => project.Id);

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
            Task.FromResult(_projects.Values.FirstOrDefault(project => project.Name == name));
        public Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values.Take(count));
        public Task<IEnumerable<Project>> SearchAsync(string keyword) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values);
        public Task<Project?> GetWithFlowAsync(Guid id) => GetByIdAsync(id);
        public Task UpdateFlowAsync(Project project) => Task.CompletedTask;
    }

    private sealed class RecordingAssetStorage : IProjectAssetStorage
    {
        public ProjectAssetsDto Assets { get; private set; } = new();
        public ProjectAssetStorageMetadata? Metadata { get; private set; }

        public Task<ProjectAssetsDto> LoadAssetsAsync(Guid projectId) =>
            Task.FromResult(ProjectAssetJson.Clone(Assets));
        public Task<ProjectAssetStorageMetadata?> LoadMetadataAsync(Guid projectId) => Task.FromResult(Metadata);
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
            Assets = new ProjectAssetsDto();
            Metadata = null;
            return Task.CompletedTask;
        }
    }
}
