using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Desktop.Tests;

public sealed class ResultsExportEndpointsTests
{
    [Fact]
    public async Task ExportEndpoints_ShouldRejectUnauthenticatedAndOperatorSessions()
    {
        var service = new RecordingResultsExportJobService();
        await using var unauthenticated = await ExportEndpointTestHost.CreateAsync(service, role: null);
        using var unauthenticatedResponse = await unauthenticated.Client.GetAsync($"/api/results/exports/{Guid.NewGuid():D}");
        unauthenticatedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var operatorHost = await ExportEndpointTestHost.CreateAsync(service, "Operator");
        using var operatorResponse = await operatorHost.Client.GetAsync($"/api/results/exports/{Guid.NewGuid():D}");
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateEndpoint_ShouldMapFormatValidationToBadRequest()
    {
        var service = new RecordingResultsExportJobService();
        await using var host = await ExportEndpointTestHost.CreateAsync(service, "Engineer");
        using var response = await host.Client.PostAsJsonAsync("/api/results/exports", new
        {
            projectId = Guid.NewGuid(),
            source = "local",
            format = "xlsx",
            clientOperationId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("RESULTS_EXPORT_FORMAT_UNSUPPORTED");
        service.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task StatusReconcileCancelAndDownloadEndpoints_ShouldUseSameJobIdentity()
    {
        var exportId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snapshot = CompletedSnapshot(exportId, projectId, operationId);
        var service = new RecordingResultsExportJobService(snapshot, new ResultsExportArtifact(
            "csv-data"u8.ToArray(),
            "text/csv",
            "results.csv",
            "sha256",
            DateTimeOffset.UtcNow.AddMinutes(10)));
        await using var host = await ExportEndpointTestHost.CreateAsync(service, "Engineer");

        using var statusResponse = await host.Client.GetAsync($"/api/results/exports/{exportId:D}");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var statusJson = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        statusJson.RootElement.GetProperty("exportId").GetGuid().Should().Be(exportId);

        using var reconcileResponse = await host.Client.GetAsync($"/api/results/exports/by-operation/{operationId:D}");
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reconcileJson = JsonDocument.Parse(await reconcileResponse.Content.ReadAsStringAsync());
        reconcileJson.RootElement.GetProperty("clientOperationId").GetGuid().Should().Be(operationId);

        using var cancelResponse = await host.Client.PostAsync($"/api/results/exports/{exportId:D}/cancel", content: null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        service.CancelCalls.Should().ContainSingle(item => item == exportId);

        using var downloadResponse = await host.Client.GetAsync($"/api/results/exports/{exportId:D}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        downloadResponse.Headers.GetValues("X-Artifact-Sha256").Single().Should().Be("sha256");
        (await downloadResponse.Content.ReadAsByteArrayAsync()).Should().Equal("csv-data"u8.ToArray());
    }

    [Fact]
    public async Task DownloadEndpoint_ShouldReturnGoneWhenArtifactExpired()
    {
        var snapshot = CompletedSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with
        {
            ArtifactExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            DownloadAvailable = false
        };
        var service = new RecordingResultsExportJobService(snapshot, artifact: null);
        await using var host = await ExportEndpointTestHost.CreateAsync(service, "Engineer");

        using var response = await host.Client.GetAsync($"/api/results/exports/{snapshot.ExportId:D}/download");

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("RESULTS_EXPORT_ARTIFACT_EXPIRED");
    }

    private static ResultsExportJobSnapshot CompletedSnapshot(Guid exportId, Guid projectId, Guid operationId) =>
        new(
            exportId,
            projectId,
            "local",
            ResultsExportFormat.Csv,
            operationId,
            ResultsExportJobState.Completed,
            DateTimeOffset.UtcNow.AddSeconds(-2),
            DateTimeOffset.UtcNow,
            DateTime.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            "results.csv",
            null,
            null,
            true);

    private sealed class ExportEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ExportEndpointTestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ExportEndpointTestHost> CreateAsync(
            IResultsExportJobService service,
            string? role)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(service);

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (!string.IsNullOrWhiteSpace(role))
                {
                    context.Items["CurrentUser"] = new UserSession
                    {
                        UserId = role.ToLowerInvariant(),
                        Username = role.ToLowerInvariant(),
                        Role = role,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(30)
                    };
                }

                await next();
            });
            app.MapResultsExportEndpoints();
            await app.StartAsync();
            return new ExportEndpointTestHost(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class RecordingResultsExportJobService : IResultsExportJobService
    {
        private readonly ResultsExportJobSnapshot? _snapshot;
        private readonly ResultsExportArtifact? _artifact;

        public RecordingResultsExportJobService(
            ResultsExportJobSnapshot? snapshot = null,
            ResultsExportArtifact? artifact = null)
        {
            _snapshot = snapshot;
            _artifact = artifact;
        }

        public int CreateCalls { get; private set; }
        public List<Guid> CancelCalls { get; } = [];

        public Task<ResultsExportJobStartResult> CreateAsync(
            ResultsExportRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            if (_snapshot is null)
            {
                throw new InvalidOperationException("The test did not configure a create response.");
            }

            return Task.FromResult(new ResultsExportJobStartResult(_snapshot, false));
        }

        public ResultsExportJobSnapshot? Get(Guid exportId) =>
            _snapshot?.ExportId == exportId ? _snapshot : null;

        public ResultsExportJobSnapshot? FindByClientOperationId(Guid clientOperationId) =>
            _snapshot?.ClientOperationId == clientOperationId ? _snapshot : null;

        public ResultsExportJobSnapshot? Cancel(Guid exportId)
        {
            if (_snapshot?.ExportId != exportId)
            {
                return null;
            }

            CancelCalls.Add(exportId);
            return _snapshot;
        }

        public bool TryReadArtifact(Guid exportId, out ResultsExportArtifact? artifact)
        {
            artifact = _snapshot?.ExportId == exportId ? _artifact : null;
            return artifact is not null;
        }
    }
}
