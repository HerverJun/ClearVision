using System.Text;
using Acme.Product.Desktop.Endpoints;
using Acme.Product.Desktop.Station;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
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

        var liveChunk = await ReadUntilContainsAsync(stream, "event: stationUpserted", TimeSpan.FromSeconds(2));
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

        private StationEndpointTestHost(WebApplication app, StationRegistryService registry)
        {
            _app = app;
            Registry = registry;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public StationRegistryService Registry { get; }

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

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
