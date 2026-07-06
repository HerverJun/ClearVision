using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Linq.Expressions;
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
                "calibration-candidate-bundle.v1");
        response.Artifacts.Should().OnlyContain(artifact => artifact.ContentType == "application/json");
        response.Artifacts.Should().OnlyContain(artifact => artifact.Length <= 512 * 1024);

        var draftArtifact = response.Artifacts.Single(artifact => artifact.Role == "calibration-draft-session.v1");
        store.TryRead(draftArtifact.ArtifactId, out var draftRead).Should().BeTrue();
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

        var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id:D}/calibration-assets/from-draft",
            new
            {
                expectedPersistenceRevision = 0,
                sessionId = "draft-1",
                targetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                imageIdentity = "image-hash",
                candidateBundleJson = CreateAcceptedBundleJson()
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProjectCalibrationAssetSaveResponse>();
        body.Should().NotBeNull();
        body!.ProjectId.Should().Be(host.Project.Id);
        body.PersistenceRevision.Should().Be(1);
        body.Asset.SourceDraftSessionId.Should().Be("draft-1");
        body.Asset.ProjectRevision.Should().Be(1);
        body.Assets.CalibrationAssets.Should().ContainSingle();
        host.AssetStorage.Metadata!.PersistenceRevision.Should().Be(1);
    }

    [Fact]
    public async Task FormalSaveEndpoint_WhenChecksumMismatches_ShouldReturnPsv019WithoutWritingAsset()
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body.Should().NotBeNull();
        body!["code"].Should().Be("PSV019");
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
                candidateBundleJson = CreateAcceptedBundleJson()
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
            RecordingProjectAssetStorage assetStorage)
        {
            _app = app;
            Project = project;
            AssetStorage = assetStorage;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public Project Project { get; }

        public RecordingProjectAssetStorage AssetStorage { get; }

        public static async Task<CalibrationEndpointHost> CreateAsync(string role = "Engineer")
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
            builder.Services.AddSingleton<PreviewArtifactStore>();
            builder.Services.AddScoped<ProjectSaveCoordinator>();
            builder.Services.AddScoped<ProjectService>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = role.ToLowerInvariant(),
                    Username = role.ToLowerInvariant(),
                    Role = role
                };
                await next();
            });
            app.MapCalibrationDraftEndpoints();
            await app.StartAsync();
            return new CalibrationEndpointHost(app, project, assetStorage);
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
