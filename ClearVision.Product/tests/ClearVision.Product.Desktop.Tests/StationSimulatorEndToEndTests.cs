using System.Text;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Hubs;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationSimulatorEndToEndTests
{
    [Fact]
    public async Task SimulatorFlow_ShouldCoverRegistrationHeartbeatHealthReplayDuplicatesAndSse()
    {
        await using var host = await StationSimulatorTestHost.CreateAsync();
        await using var connection = host.CreateConnection();

        await connection.StartAsync();
        var cursor = await connection.InvokeAsync<StationReplayCursorDto>(
            StationHubMethods.RegisterStationAsync,
            BuildRegistration("sim-1"));
        cursor.StationId.Should().Be("sim-1");

        var heartbeatCursor = await connection.InvokeAsync<StationReplayCursorDto>(
            StationHubMethods.PushHeartbeatAsync,
            new StationHeartbeatDto
            {
                StationId = "sim-1",
                SequenceId = 1,
                MessageId = "hb-1",
                LineName = "line-sim",
                RuntimeState = StationRuntimeState.Running,
                State = RuntimeHostState.Running,
                SentAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        heartbeatCursor.StationId.Should().Be("sim-1");

        var healthAck = await connection.InvokeAsync<StationAckDto>(
            StationHubMethods.PushHealth,
            new StationHealthSnapshotDto
            {
                StationId = "sim-1",
                SequenceId = 2,
                MessageId = "health-2",
                RuntimeState = StationRuntimeState.Running,
                SpoolPendingCount = 20,
                WorkingSetMb = 128,
                DiskFreeMb = 2048,
                DiskTotalMb = 4096,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        healthAck.StationId.Should().Be("sim-1");

        await connection.StopAsync();
        await connection.StartAsync();
        await connection.InvokeAsync<StationReplayCursorDto>(
            StationHubMethods.RegisterStationAsync,
            BuildRegistration("sim-1"));

        for (var sequence = 1; sequence <= 20; sequence++)
        {
            var ack = await connection.InvokeAsync<StationAckDto>(StationHubMethods.PushResult, BuildResult(sequence));
            ack.LastPersistedSequenceId.Should().Be(sequence);
        }

        for (var i = 0; i < 10; i++)
        {
            var duplicateAck = await connection.InvokeAsync<StationAckDto>(
                StationHubMethods.PushResult,
                BuildResult(20, $"DUP-{i}"));
            duplicateAck.LastPersistedSequenceId.Should().Be(20);
        }

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stations/events"),
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var initialState = await ReadUntilContainsAsync(stream, "event: initialState", TimeSpan.FromSeconds(2));
        initialState.Should().Contain("\"stationId\":\"sim-1\"");

        var station = host.Registry.GetStation("sim-1")!;
        station.LastSequenceId.Should().Be(20);
        station.SpoolPendingCount.Should().Be(20);
        station.RecentResults.Should().HaveCount(20);
        station.RecentResults.Count(result => result.SequenceId == 20).Should().Be(1);
        station.RecentHealth.Should().ContainSingle(item => item.MessageId == "health-2");
    }

    private static StationRegistrationDto BuildRegistration(string stationId)
    {
        return new StationRegistrationDto
        {
            StationId = stationId,
            StationName = "Simulator 1",
            LineName = "line-sim",
            MachineName = "sim-pc",
            ClientVersion = "test",
            StartedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static StationResultSummaryDto BuildResult(long sequenceId, string diagnosticCode = "OK")
    {
        return new StationResultSummaryDto
        {
            StationId = "sim-1",
            LineName = "line-sim",
            SequenceId = sequenceId,
            MessageId = $"result-{sequenceId}",
            RunId = $"run-{sequenceId}",
            PackageId = "pkg-sim",
            PackageName = "Simulator Package",
            PackageVersion = "1.0.0",
            FlowHash = "sha256:sim",
            Outcome = RuntimeRunOutcome.Ok,
            ExecutionTimeMs = 10,
            DiagnosticCode = diagnosticCode,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task<string> ReadUntilContainsAsync(Stream stream, string marker, TimeSpan timeout)
    {
        var buffer = new byte[1024];
        var builder = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);

        while (true)
        {
            var current = builder.ToString();
            var markerIndex = current.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0 && current.IndexOf("\n\n", markerIndex, StringComparison.Ordinal) >= 0)
            {
                break;
            }

            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            bytesRead.Should().BeGreaterThan(0);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        return builder.ToString();
    }

    private sealed class StationSimulatorTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private StationSimulatorTestHost(WebApplication app, StationRegistryService registry)
        {
            _app = app;
            Registry = registry;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public StationRegistryService Registry { get; }

        public HubConnection CreateConnection()
        {
            var testServer = _app.GetTestServer();
            return new HubConnectionBuilder()
                .WithUrl(
                    "http://localhost" + StationSyncContractDefaults.HubPath,
                    options =>
                    {
                        options.Transports = HttpTransportType.LongPolling;
                        options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
                        options.AccessTokenProvider = () => Task.FromResult<string?>("station-secret");
                    })
                .Build();
        }

        public static async Task<StationSimulatorTestHost> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSignalR();
            builder.Services.AddSingleton(Options.Create(new StationIngressOptions
            {
                Enabled = true,
                SharedToken = "station-secret",
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 50,
                EventBufferSize = 200,
                HealthBufferPerStation = 20
            }));
            builder.Services.AddSingleton<StationIngressAuthService>(sp =>
                new StationIngressAuthService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationIngressAuthService>.Instance));
            builder.Services.AddSingleton<StationRegistryService>(sp =>
                new StationRegistryService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationRegistryService>.Instance));

            var app = builder.Build();
            app.MapHub<StationHub>(StationSyncContractDefaults.HubPath);
            app.MapStationEndpoints();
            await app.StartAsync();

            return new StationSimulatorTestHost(app, app.Services.GetRequiredService<StationRegistryService>());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
