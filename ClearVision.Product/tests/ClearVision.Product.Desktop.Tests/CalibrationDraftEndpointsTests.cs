using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Linq.Expressions;
using System.Security.Claims;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.PreviewArtifacts;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]
public sealed class CalibrationDraftEndpointsTests
{
    [Fact]
    public void SolveDraft_UsesEnabledValidSamplesAndReturnsDraftArtifacts()
    {
        using var store = new PreviewArtifactStore();
        var request = new NPointCalibrationDraftSolveRequest
        {
            SessionId = "session-affine",
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DebugSessionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ClientRequestSequence = 9,
            FlowRevision = 12,
            ImageIdentity = "image-hash",
            Mode = "Affine",
            Unit = "mm",
            SolverOptions = new NPointCalibrationDraftSolverOptionsDto
            {
                MaxAcceptedReprojectionError = 0.05,
                MinInlierCount = 4,
                MinInlierRatio = 1.0
            },
            Samples =
            [
                CreateSample("s1", 1, 0, 0, 5, -4),
                CreateSample("s2", 2, 10, 0, 25, -4),
                CreateSample("s3", 3, 0, 20, 5, 56),
                CreateSample("s4", 4, 10, 20, 25, 56),
                CreateSample("disabled", 5, 100, 100, 999, 999, enabled: false),
                CreateSample("invalid", 6, double.NaN, 30, 0, 0)
            ]
        };

        var response = CalibrationDraftEndpoints.SolveDraft(request, store);

        response.Success.Should().BeTrue();
        response.Status.Should().Be("Solved");
        response.DraftOnly.Should().BeTrue();
        response.NotSavedToProjectAssets.Should().BeTrue();
        response.LastSolveResult.Should().NotBeNull();
        response.LastSolveResult!.TransformModel.Should().Be("Affine");
        response.LastSolveResult.TotalSampleCount.Should().Be(4);
        response.LastSolveResult.InlierCount.Should().Be(4);
        response.LastSolveResult.Accepted.Should().BeTrue();
        response.CandidateBundle.Should().NotBeNull();
        response.CandidateBundle!.Quality.Accepted.Should().BeTrue();
        response.CandidateBundleJson.Should().Contain("NPointCalibrationDraftWorkbench");

        response.Samples.Should().HaveCount(6);
        response.Samples.Where(sample => sample.SampleId is "s1" or "s2" or "s3" or "s4")
            .Should()
            .OnlyContain(sample =>
                sample.Inlier == true &&
                sample.Error.HasValue &&
                sample.Error.Value < 1e-6);
        response.Samples.Single(sample => sample.SampleId == "disabled").Inlier.Should().BeNull();
        response.Samples.Single(sample => sample.SampleId == "invalid").ValidationMessage.Should().Contain("finite");

        response.Artifacts.Select(artifact => artifact.Role)
            .Should()
            .BeEquivalentTo(
                "calibration-draft-session.v1",
                "calibration-sample-table.v1",
                "calibration-solve-provenance.v1");
        response.Artifacts.Should().OnlyContain(artifact => artifact.ContentType == "application/json");
        response.Artifacts.Should().OnlyContain(artifact => artifact.Length <= 512 * 1024);

        var draftArtifact = response.Artifacts.Single(artifact => artifact.Role == "calibration-draft-session.v1");
        store.TryRead(draftArtifact.ArtifactId, string.Empty, out var draftRead).Should().BeTrue();
        Encoding.UTF8.GetString(draftRead!.Bytes).Should().Contain("\"notSavedToProjectAssets\":true");

        response.Observation.Should().NotBeNull();
        response.Observation!.VisualScene.Should().NotBeNull();
        response.Observation.VisualScene!.Primitives.Should().Contain(primitive =>
            primitive.Layer == "calibration-reprojection" &&
            primitive.ResultPath == "$[\"samples\"][0]");
    }

    [Fact]
    public async Task FormalSaveEndpoint_WhenCandidateIsAccepted_ShouldPersistProjectAsset()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();
        var solveArtifactId = await SolveAcceptedArtifactAsync(host);

        var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id:D}/calibration-assets/from-draft",
            new
            {
                expectedPersistenceRevision = 0,
                assetId = "npoint-asset",
                sessionId = "draft-1",
                targetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                imageIdentity = "image-hash",
                solveArtifactId
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProjectCalibrationAssetSaveResponse>();
        body.Should().NotBeNull();
        body!.ProjectId.Should().Be(host.Project.Id);
        body.PersistenceRevision.Should().Be(1);
        body.Asset.AssetId.Should().Be("npoint-asset");
        body.Asset.SourceDraftSessionId.Should().Be("draft-1");
        body.Asset.ProjectRevision.Should().Be(1);
        body.Assets.CalibrationAssets.Should().ContainSingle();
        host.AssetStorage.Metadata!.PersistenceRevision.Should().Be(1);
    }

    [Fact]
    public async Task FormalSaveEndpoint_LegacyCandidateAndClientHash_ShouldNotBeAuthority()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();

        var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id:D}/calibration-assets/from-draft",
            new
            {
                expectedPersistenceRevision = 0,
                sessionId = "draft-1",
                candidateBundleJson = CreateAcceptedBundleJson(),
                expectedContentHash = "sha256:0000"
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body.Should().NotBeNull();
        body!["code"].Should().Be("not-found");
        host.Project.PersistenceRevision.Should().Be(0);
        host.AssetStorage.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task FormalSaveEndpoint_ShouldRejectOperator()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync(role: UserRole.Operator.ToString());

        var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id:D}/calibration-assets/from-draft",
            new
            {
                expectedPersistenceRevision = 0,
                sessionId = "draft-1",
                targetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                imageIdentity = "image-hash",
                solveArtifactId = new string('A', 43)
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.AssetStorage.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task FormalSaveEndpoint_ForgedArtifactId_ShouldReturnOpaqueNotFoundWithoutLeaseOrSave()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();

        var response = await PostFormalSaveAsync(host, new string('A', 43));

        await AssertOpaqueNotFoundWithoutSaveAsync(host, response);
        await host.RuntimeCoordinator.DidNotReceiveWithAnyArgs().TryAcquireMutationLeaseAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FormalSaveEndpoint_WrongOwner_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync(userId: "user-a");
        var solveArtifactId = await SolveAcceptedArtifactAsync(host);
        host.SetUser("user-b", UserRole.Engineer.ToString());

        var response = await PostFormalSaveAsync(host, solveArtifactId);

        await AssertOpaqueNotFoundWithoutSaveAsync(host, response);
    }

    [Fact]
    public async Task FormalSaveEndpoint_WrongProject_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();
        var solveArtifactId = await SolveAcceptedArtifactAsync(host);

        var response = await PostFormalSaveAsync(host, solveArtifactId, projectId: Guid.NewGuid());

        await AssertOpaqueNotFoundWithoutSaveAsync(host, response);
    }

    [Fact]
    public async Task FormalSaveEndpoint_ExpiredArtifact_ShouldReturnOpaqueNotFoundWithoutSaving()
    {
        var clock = new FakePreviewArtifactClock(
            new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero));
        await using var host = await CalibrationEndpointHost.CreateAsync(clock: clock);
        var solveArtifactId = await SolveAcceptedArtifactAsync(host);
        clock.Advance(TimeSpan.FromMinutes(2));

        var response = await PostFormalSaveAsync(host, solveArtifactId);

        await AssertOpaqueNotFoundWithoutSaveAsync(host, response);
        host.ArtifactStore.TryReadScoped(
                solveArtifactId,
                host.CurrentUser.UserId,
                host.Project.Id,
                "calibrationSolveBundle",
                out _)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("wrong-session", "22222222-2222-2222-2222-222222222222", "image-hash")]
    [InlineData("draft-1", "33333333-3333-3333-3333-333333333333", "image-hash")]
    [InlineData("draft-1", "22222222-2222-2222-2222-222222222222", "wrong-image")]
    public async Task FormalSaveEndpoint_WrongSolveContext_ShouldReturnOpaqueNotFound(
        string sessionId,
        string targetNodeId,
        string imageIdentity)
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();
        var solveArtifactId = await SolveAcceptedArtifactAsync(host);

        var response = await PostFormalSaveAsync(
            host,
            solveArtifactId,
            sessionId: sessionId,
            targetNodeId: Guid.Parse(targetNodeId),
            imageIdentity: imageIdentity);

        await AssertOpaqueNotFoundWithoutSaveAsync(host, response);
    }

    [Fact]
    public async Task FormalSaveEndpoint_UnacceptedServerBundle_ShouldReturn422WithoutSaving()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();
        var solveArtifactId = await SolveArtifactAsync(host, minInlierCount: 10);

        var response = await PostFormalSaveAsync(host, solveArtifactId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["code"].Should().Be("validation-error");
        host.Project.PersistenceRevision.Should().Be(0);
        host.AssetStorage.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task FormalSaveEndpoint_RevisionConflict_ShouldReturn409WithoutSaving()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();
        var solveArtifactId = await SolveAcceptedArtifactAsync(host);

        var response = await PostFormalSaveAsync(host, solveArtifactId, expectedRevision: 99);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        host.Project.PersistenceRevision.Should().Be(0);
        host.AssetStorage.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task FormalSaveEndpoint_MissingRevision_ShouldReturn422BeforeArtifactRead()
    {
        await using var host = await CalibrationEndpointHost.CreateAsync();

        var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id:D}/calibration-assets/from-draft",
            new
            {
                sessionId = "draft-1",
                targetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                imageIdentity = "image-hash",
                solveArtifactId = new string('A', 43)
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["code"].Should().Be("validation-error");
        host.AssetStorage.Metadata.Should().BeNull();
    }

    private static Task<string> SolveAcceptedArtifactAsync(CalibrationEndpointHost host) =>
        SolveArtifactAsync(host, minInlierCount: 4);

    private static async Task<string> SolveArtifactAsync(
        CalibrationEndpointHost host,
        int minInlierCount)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/calibration/npoint-draft/solve",
            new NPointCalibrationDraftSolveRequest
            {
                SessionId = "draft-1",
                ProjectId = host.Project.Id,
                TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                DebugSessionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ClientRequestSequence = 1,
                FlowRevision = 0,
                ImageIdentity = "image-hash",
                Mode = "Affine",
                Unit = "mm",
                SolverOptions = new NPointCalibrationDraftSolverOptionsDto
                {
                    MaxAcceptedReprojectionError = 0.05,
                    MinInlierCount = minInlierCount,
                    MinInlierRatio = 1.0
                },
                Samples =
                [
                    CreateSample("s1", 1, 0, 0, 5, -4),
                    CreateSample("s2", 2, 10, 0, 25, -4),
                    CreateSample("s3", 3, 0, 20, 5, 56),
                    CreateSample("s4", 4, 10, 20, 25, 56)
                ]
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<NPointCalibrationDraftSolveResponse>();
        body.Should().NotBeNull();
        var artifact = body!.Artifacts.Single(item => item.Kind == "calibrationSolveBundle");
        return artifact.ArtifactId;
    }

    private static Task<HttpResponseMessage> PostFormalSaveAsync(
        CalibrationEndpointHost host,
        string solveArtifactId,
        Guid? projectId = null,
        long expectedRevision = 0,
        string sessionId = "draft-1",
        Guid? targetNodeId = null,
        string imageIdentity = "image-hash") =>
        host.Client.PostAsJsonAsync(
            $"/api/projects/{projectId ?? host.Project.Id:D}/calibration-assets/from-draft",
            new
            {
                expectedPersistenceRevision = expectedRevision,
                assetId = "npoint-asset",
                sessionId,
                targetNodeId = targetNodeId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
                imageIdentity,
                solveArtifactId
            });

    private static async Task AssertOpaqueNotFoundWithoutSaveAsync(
        CalibrationEndpointHost host,
        HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["code"].Should().Be("not-found");
        host.Project.PersistenceRevision.Should().Be(0);
        host.AssetStorage.Metadata.Should().BeNull();
    }

    private static NPointCalibrationDraftSampleDto CreateSample(
        string sampleId,
        int order,
        double pixelX,
        double pixelY,
        double worldX,
        double worldY,
        bool enabled = true) =>
        new()
        {
            SampleId = sampleId,
            Order = order,
            PixelX = pixelX,
            PixelY = pixelY,
            WorldX = worldX,
            WorldY = worldY,
            Enabled = enabled
        };

    private static string CreateAcceptedBundleJson() =>
        """
        {
          "schemaVersion": 2,
          "bundleId": "bundle-1",
          "calibrationVersion": "1",
          "calibrationKind": "rigidTransform2D",
          "transformModel": "scaleOffset",
          "sourceFrame": "image",
          "targetFrame": "world",
          "unit": "mm",
          "transform2D": {
            "model": "scaleOffset",
            "matrix": [
              [0.02, 0.0, 0.0],
              [0.0, 0.02, 0.0]
            ],
            "pixelSizeX": 0.02,
            "pixelSizeY": 0.02
          },
          "quality": {
            "accepted": true,
            "meanError": 0.05,
            "maxError": 0.09,
            "inlierCount": 8,
            "totalSampleCount": 8,
            "diagnostics": []
          },
          "producerOperator": "CalibrationDraftEndpointsTests"
        }
        """;

    private sealed class CalibrationEndpointHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private CalibrationEndpointHost(
            WebApplication app,
            Project project,
            RecordingProjectAssetStorage assetStorage,
            PreviewArtifactStore artifactStore,
            IInspectionRuntimeCoordinator runtimeCoordinator,
            UserSession currentUser)
        {
            _app = app;
            Project = project;
            AssetStorage = assetStorage;
            ArtifactStore = artifactStore;
            RuntimeCoordinator = runtimeCoordinator;
            CurrentUser = currentUser;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public Project Project { get; }

        public RecordingProjectAssetStorage AssetStorage { get; }

        public PreviewArtifactStore ArtifactStore { get; }

        public IInspectionRuntimeCoordinator RuntimeCoordinator { get; }

        public UserSession CurrentUser { get; }

        public static async Task<CalibrationEndpointHost> CreateAsync(
            string role = "Engineer",
            string? userId = null,
            FakePreviewArtifactClock? clock = null)
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = Substitute.For<IProjectFlowStorage>();
            flowStorage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(null));
            var assetStorage = new RecordingProjectAssetStorage();
            var runtimeCoordinator = Substitute.For<IInspectionRuntimeCoordinator>();
            runtimeCoordinator
                .TryAcquireMutationLeaseAsync(project.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<ProjectMutationLease?>(
                    new ProjectMutationLease(project.Id, "test", () => ValueTask.CompletedTask)));
            var artifactStore = new PreviewArtifactStore(new PreviewArtifactStoreOptions
            {
                Ttl = TimeSpan.FromMinutes(1),
                MaxEntries = 64,
                MaxTotalBytes = 4 * 1024 * 1024,
                MaxEntryBytes = 512 * 1024
            }, clock);
            var currentUser = new UserSession
            {
                UserId = userId ?? role.ToLowerInvariant(),
                Username = userId ?? role.ToLowerInvariant(),
                Role = role
            };

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IProjectRepository>(repository);
            builder.Services.AddSingleton(flowStorage);
            builder.Services.AddSingleton<IProjectAssetStorage>(assetStorage);
            builder.Services.AddSingleton<IOperatorFactory>(new OperatorFactory());
            builder.Services.AddSingleton(new ProjectVariableSessionRegistry());
            builder.Services.AddSingleton(runtimeCoordinator);
            builder.Services.AddSingleton(Options.Create(new StudioOptions { NPointCalibrationWorkbenchEnabled = true }));
            builder.Services.AddSingleton(NullLogger<ProjectService>.Instance);
            builder.Services.AddSingleton(artifactStore);
            builder.Services.AddScoped<ProjectSaveCoordinator>();
            builder.Services.AddScoped<ProjectService>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = currentUser;
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, currentUser.UserId),
                    new Claim(ClaimTypes.Name, currentUser.Username),
                    new Claim(ClaimTypes.Role, currentUser.Role)
                ], "CalibrationEndpointTests"));
                await next();
            });
            app.MapCalibrationDraftEndpoints();
            await app.StartAsync();
            return new CalibrationEndpointHost(
                app,
                project,
                assetStorage,
                artifactStore,
                runtimeCoordinator,
                currentUser);
        }

        public void SetUser(string userId, string role)
        {
            CurrentUser.UserId = userId;
            CurrentUser.Username = userId;
            CurrentUser.Role = role;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            ProjectSaveCoordinator.ResetStaticStateForTests();
        }
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly Dictionary<Guid, Project> _projects;

        public InMemoryProjectRepository(params Project[] projects)
        {
            _projects = projects.ToDictionary(project => project.Id);
        }

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
            Task.FromResult(_projects.Values.FirstOrDefault(project => string.Equals(project.Name, name, StringComparison.Ordinal)));

        public Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values.Take(count));

        public Task<IEnumerable<Project>> SearchAsync(string keyword) =>
            Task.FromResult<IEnumerable<Project>>(_projects.Values);

        public Task<Project?> GetWithFlowAsync(Guid id) => GetByIdAsync(id);

        public Task UpdateFlowAsync(Project project) => Task.CompletedTask;
    }

    public sealed class FakePreviewArtifactClock(DateTimeOffset utcNow) : IPreviewArtifactClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan elapsed) => UtcNow = UtcNow.Add(elapsed);
    }

    public sealed class RecordingProjectAssetStorage : IProjectAssetStorage
    {
        public ProjectAssetsDto Assets { get; private set; } = new();

        public ProjectAssetStorageMetadata? Metadata { get; private set; }

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
            Assets = new ProjectAssetsDto();
            Metadata = null;
            return Task.CompletedTask;
        }
    }
}
