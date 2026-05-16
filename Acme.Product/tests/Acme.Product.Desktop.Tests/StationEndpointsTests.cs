using System.Net;
using System.Net.Http.Json;
using System.Text;
using Acme.Product.Desktop.Endpoints;
using Acme.Product.Desktop.Middleware;
using Acme.Product.Desktop.Station;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Acme.Product.Desktop.Tests;

public sealed class StationEndpointsTests
{
    [Fact]
    public async Task EventsEndpoint_ShouldStreamInitialSnapshotAndLiveStationUpdates()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-1", new StationRegistrationDto
        {
            StationId = "station-a",
            LineName = "line-1",
            MachineName = "machine-a",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stations/events"),
            HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var initialChunk = await ReadUntilContainsAsync(stream, "event: initialState", TimeSpan.FromSeconds(2));
        initialChunk.Should().Contain("event: initialState");
        initialChunk.Should().Contain("\"stationId\":\"station-a\"");

        host.Registry.UpsertHeartbeat("conn-1", new StationHeartbeatDto
        {
            StationId = "station-a",
            LineName = "line-1",
            State = RuntimeHostState.Running,
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:abc",
            CurrentRunId = "run-1",
            SessionOkCount = 12,
            SessionNgCount = 1,
            SessionErrorCount = 0
        });

        var liveChunk = await ReadUntilContainsAsync(stream, "\"state\":\"Running\"", TimeSpan.FromSeconds(2));
        liveChunk.Should().Contain("event: stationUpserted");
        liveChunk.Should().Contain("\"state\":\"Running\"");
        liveChunk.Should().Contain("\"stationId\":\"station-a\"");
    }

    [Fact]
    public async Task EventsEndpoint_ShouldReplayStoredEventsAfterLastEventId()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-2", new StationRegistrationDto
        {
            StationId = "station-b",
            MachineName = "machine-b",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        var checkpoint = host.Registry.GetEventsAfter(0).Max(evt => evt.SequenceId);

        host.Registry.UpsertResultSummary("conn-2", new StationResultSummaryDto
        {
            StationId = "station-b",
            SequenceId = 9,
            RunId = "run-9",
            PackageId = "pkg-9",
            PackageName = "Package 9",
            FlowHash = "sha256:def",
            ImageId = "image-9",
            Outcome = RuntimeRunOutcome.Ok,
            ExecutionTimeMs = 18,
            DiagnosticCode = "OK",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-18),
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stations/events");
        request.Headers.Add("Last-Event-ID", checkpoint.ToString());

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var replayChunk = await ReadUntilContainsAsync(stream, "event: stationResultAdded", TimeSpan.FromSeconds(2));
        replayChunk.Should().Contain($"id: {checkpoint + 1}");
        replayChunk.Should().Contain("\"stationId\":\"station-b\"");
        replayChunk.Should().Contain("\"diagnosticCode\":\"OK\"");
    }

    [Fact]
    public async Task CreateCommand_ShouldRejectInvalidPayloadJson()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.Ping,
                PayloadJson = "{not-json",
                IssuedBy = "unit-test"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        (await db.StationCommandRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateCommand_ShouldRejectAnonymousUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: null);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.Ping,
                PayloadJson = "{}"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateIdentity_ShouldRejectNonAdminUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        using var response = await host.Client.PatchAsJsonAsync(
            "/api/stations/station-a/identity",
            new StationIdentityUpdateRequest
            {
                StationName = "Line station A"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectNonAdminUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "package-a"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TestPackage_ShouldRejectNonAdminUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        using var response = await host.Client.PostAsync("/api/station-packages/test", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectBlankPackageId()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "   ",
                IssuedBy = "unit-test"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        (await db.StationCommandRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DownloadPackage_ShouldRejectStoredPathOutsidePackageDirectory()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        var externalPath = Path.Combine(Path.GetTempPath(), "ClearVisionStationEndpointTests", Guid.NewGuid().ToString("N"), "outside.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(externalPath)!);
        await File.WriteAllTextAsync(externalPath, "not a package");

        try
        {
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
                db.StationPackageRecords.Add(new StationPackageRecordEntity
                {
                    PackageId = "package-outside",
                    PackageName = "Outside",
                    PackageVersion = "1.0.0",
                    FlowHash = "sha256:test",
                    FileName = Path.GetFileName(externalPath),
                    FilePath = externalPath,
                    SizeBytes = new FileInfo(externalPath).Length,
                    Sha256 = "test",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            using var response = await host.Client.GetAsync("/api/station-packages/package-outside/download");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(externalPath)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<string> ReadUntilContainsAsync(Stream stream, string marker, TimeSpan timeout)
    {
        var buffer = new byte[1024];
        var builder = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);

        while (!builder.ToString().Contains(marker, StringComparison.Ordinal))
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            bytesRead.Should().BeGreaterThan(0);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        return builder.ToString();
    }

    private sealed class StationEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string? _tempRoot;

        private StationEndpointTestHost(WebApplication app, StationRegistryService registry, string? tempRoot = null)
        {
            _app = app;
            _tempRoot = tempRoot;
            Registry = registry;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public StationRegistryService Registry { get; }

        public IServiceProvider Services => _app.Services;

        public static async Task<StationEndpointTestHost> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Services.AddSingleton(Options.Create(new StationIngressOptions
            {
                Enabled = true,
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 20,
                EventBufferSize = 50
            }));
            builder.Services.AddSingleton<StationRegistryService>(sp =>
                new StationRegistryService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationRegistryService>.Instance));

            var app = builder.Build();
            app.MapStationEndpoints();
            await app.StartAsync();

            return new StationEndpointTestHost(app, app.Services.GetRequiredService<StationRegistryService>());
        }

        public static async Task<StationEndpointTestHost> CreateWithCentralStoreAsync(string? role = "Admin")
        {
            var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationEndpointTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Services.AddDbContext<VisionDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(root, "vision.db")}"));
            builder.Services.AddSingleton(Options.Create(new StationIngressOptions
            {
                Enabled = true,
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 20,
                EventBufferSize = 50
            }));
            builder.Services.AddSingleton<StationRegistryService>(sp =>
                new StationRegistryService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationRegistryService>.Instance));
            builder.Services.AddSingleton<StationCentralStore>(sp =>
                new StationCentralStore(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<StationCentralStore>.Instance));
            builder.Services.AddSingleton<StationPackageStore>(sp =>
                new StationPackageStore(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<StationPackageStore>.Instance));

            var app = builder.Build();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
            }

            app.Use(async (context, next) =>
            {
                if (role is not null)
                {
                    context.Items["CurrentUser"] = new UserSession
                    {
                        UserId = role.ToLowerInvariant(),
                        Username = role.ToLowerInvariant(),
                        Role = role
                    };
                }

                await next();
            });
            app.MapStationEndpoints();
            await app.StartAsync();

            return new StationEndpointTestHost(app, app.Services.GetRequiredService<StationRegistryService>(), root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();

            if (!string.IsNullOrWhiteSpace(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
