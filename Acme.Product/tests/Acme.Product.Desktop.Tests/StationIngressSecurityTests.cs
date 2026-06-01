using System.Net;
using Acme.Product.Desktop.Hubs;
using Acme.Product.Desktop.Middleware;
using Acme.Product.Desktop.Station;
using Acme.Product.Runtime.Abstractions;
using Acme.Product.Station.Sync;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Acme.Product.Desktop.Tests;

public sealed class StationIngressSecurityTests
{
    [Fact]
    public async Task LanIsolation_ShouldBlockRemoteStudioSurfaceButAllowHealthAndStationHubPath()
    {
        await using var host = await IngressIsolationTestHost.CreateAsync(
            IPAddress.Parse("192.168.1.25"),
            new StationIngressOptions
            {
                Enabled = true,
                ListenMode = StationIngressListenMode.Lan,
                SharedToken = "station-secret"
            });

        using var uiResponse = await host.Client.GetAsync("/");
        using var stationsResponse = await host.Client.GetAsync("/api/stations");
        using var healthResponse = await host.Client.GetAsync("/health");
        using var hubPathResponse = await host.Client.GetAsync(StationSyncContractDefaults.HubPath + "/negotiate");

        uiResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        stationsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        hubPathResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LanIsolation_ShouldKeepLoopbackStudioSurfaceReachable()
    {
        await using var host = await IngressIsolationTestHost.CreateAsync(
            IPAddress.Loopback,
            new StationIngressOptions
            {
                Enabled = true,
                ListenMode = StationIngressListenMode.Lan,
                SharedToken = "station-secret"
            });

        using var response = await host.Client.GetAsync("/api/stations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StationHub_ShouldRejectRegistrationWithoutStationToken()
    {
        var options = CreateEnabledIngressOptions();
        await using var host = await StationHubTestHost.CreateAsync(options);
        await using var connection = host.CreateConnection(accessToken: null);

        await connection.StartAsync();
        var act = () => connection.InvokeAsync<StationReplayCursorDto>(
            StationHubMethods.RegisterStationAsync,
            BuildRegistration("station-no-token"));

        var exception = await Assert.ThrowsAsync<HubException>(act);
        exception.Message.Should().Contain("token is invalid");
        host.Registry.GetStation("station-no-token").Should().BeNull();
    }

    [Fact]
    public async Task StationHubProbe_ShouldRejectWithoutStationToken()
    {
        var options = CreateEnabledIngressOptions();
        await using var host = await StationHubTestHost.CreateAsync(options);
        await using var connection = host.CreateConnection(accessToken: null);

        await connection.StartAsync();
        var act = () => connection.InvokeAsync<StationProbeAckDto>(StationHubMethods.Probe);

        var exception = await Assert.ThrowsAsync<HubException>(act);
        exception.Message.Should().Contain("token is invalid");
        host.Registry.GetStations().Should().BeEmpty();
    }

    [Fact]
    public async Task StationHubProbe_ShouldAcceptBearerTokenWithoutRegisteringStation()
    {
        var options = CreateEnabledIngressOptions();
        await using var host = await StationHubTestHost.CreateAsync(options);
        await using var connection = host.CreateConnection("station-secret");

        await connection.StartAsync();
        var ack = await connection.InvokeAsync<StationProbeAckDto>(StationHubMethods.Probe);

        ack.Accepted.Should().BeTrue();
        ack.Message.Should().Contain("accepted");
        host.Registry.GetStations().Should().BeEmpty();
    }

    [Fact]
    public async Task StationConnectionTester_ShouldProbeHubWithoutRegisteringStation()
    {
        var options = CreateEnabledIngressOptions();
        await using var host = await StationConnectionTestHost.CreateAsync(options);
        var tester = new StationStudioConnectionTester();

        var result = await tester.TestAsync(
            new StationSyncConnectionSettings
            {
                Enabled = true,
                StudioBaseUrl = host.BaseUrl,
                SharedToken = "station-secret"
            });

        result.Success.Should().BeTrue(result.Message);
        result.HealthReachable.Should().BeTrue();
        result.HubReachable.Should().BeTrue();
        host.Registry.GetStations().Should().BeEmpty();
    }

    [Fact]
    public async Task StationHub_ShouldAcceptBearerTokenAndRegisterStation_WhenIngressEnabled()
    {
        var options = CreateEnabledIngressOptions();
        await using var host = await StationHubTestHost.CreateAsync(options);
        await using var connection = host.CreateConnection("station-secret");

        await connection.StartAsync();
        var cursor = await connection.InvokeAsync<StationReplayCursorDto>(
            StationHubMethods.RegisterStationAsync,
            BuildRegistration("station-token"));

        cursor.StationId.Should().Be("station-token");
        cursor.AckedSequenceId.Should().Be(0);
        var station = host.Registry.GetStation("station-token");
        station.Should().NotBeNull();
        station!.IsOnline.Should().BeTrue();
    }

    [Fact]
    public async Task StationHub_ShouldRejectStationToken_WhenIngressDisabledByDefault()
    {
        var options = new StationIngressOptions
        {
            Enabled = false,
            ListenMode = StationIngressListenMode.Loopback,
            SharedToken = "station-secret"
        };
        await using var host = await StationHubTestHost.CreateAsync(options);
        await using var connection = host.CreateConnection("station-secret");

        await connection.StartAsync();
        var act = () => connection.InvokeAsync<StationReplayCursorDto>(
            StationHubMethods.RegisterStationAsync,
            BuildRegistration("station-disabled"));

        var exception = await Assert.ThrowsAsync<HubException>(act);
        exception.Message.Should().Contain("disabled");
        host.Registry.GetStation("station-disabled").Should().BeNull();
    }

    private static StationIngressOptions CreateEnabledIngressOptions()
    {
        return new StationIngressOptions
        {
            Enabled = true,
            ListenMode = StationIngressListenMode.Lan,
            SharedToken = "station-secret",
            OfflineThresholdSeconds = 15,
            ResultBufferPerStation = 20,
            EventBufferSize = 50
        };
    }

    private static StationRegistryService CreateRegistry(StationIngressOptions options)
    {
        return new StationRegistryService(
            Options.Create(options),
            NullLogger<StationRegistryService>.Instance);
    }

    private static StationRegistrationDto BuildRegistration(string stationId)
    {
        return new StationRegistrationDto
        {
            StationId = stationId,
            LineName = "line-a",
            MachineName = "station-pc",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class IngressIsolationTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private IngressIsolationTestHost(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<IngressIsolationTestHost> CreateAsync(
            IPAddress remoteIpAddress,
            StationIngressOptions options)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(Options.Create(options));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Connection.RemoteIpAddress = remoteIpAddress;
                await next();
            });
            app.UseMiddleware<StationIngressIsolationMiddleware>();
            app.MapGet("/", () => Results.Ok("studio-ui"));
            app.MapGet("/api/stations", () => Results.Ok("stations"));
            app.MapGet("/health", () => Results.Ok("healthy"));
            app.MapGet(StationSyncContractDefaults.HubPath + "/negotiate", () => Results.Ok("hub-path"));

            await app.StartAsync();
            return new IngressIsolationTestHost(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class StationHubTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private StationHubTestHost(WebApplication app, StationRegistryService registry)
        {
            _app = app;
            Registry = registry;
        }

        public StationRegistryService Registry { get; }

        public HubConnection CreateConnection(string? accessToken)
        {
            var testServer = _app.GetTestServer();
            return new HubConnectionBuilder()
                .WithUrl(
                    "http://localhost" + StationSyncContractDefaults.HubPath,
                    options =>
                    {
                        options.Transports = HttpTransportType.LongPolling;
                        options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
                        if (accessToken != null)
                        {
                            options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                        }
                    })
                .Build();
        }

        public static async Task<StationHubTestHost> CreateAsync(StationIngressOptions options)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Services.AddSignalR();
            builder.Services.AddSingleton(Options.Create(options));
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
            await app.StartAsync();

            return new StationHubTestHost(app, app.Services.GetRequiredService<StationRegistryService>());
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class StationConnectionTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private StationConnectionTestHost(WebApplication app, StationRegistryService registry, string baseUrl)
        {
            _app = app;
            Registry = registry;
            BaseUrl = baseUrl;
        }

        public StationRegistryService Registry { get; }

        public string BaseUrl { get; }

        public static async Task<StationConnectionTestHost> CreateAsync(StationIngressOptions options)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddSignalR();
            builder.Services.AddSingleton(Options.Create(options));
            builder.Services.AddSingleton<StationIngressAuthService>(sp =>
                new StationIngressAuthService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationIngressAuthService>.Instance));
            builder.Services.AddSingleton<StationRegistryService>(sp =>
                new StationRegistryService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationRegistryService>.Instance));

            var app = builder.Build();
            app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
            app.MapHub<StationHub>(StationSyncContractDefaults.HubPath);
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>();
            var baseUrl = addresses?.Addresses.Single()
                ?? throw new InvalidOperationException("Unable to resolve Station connection test URL.");

            return new StationConnectionTestHost(
                app,
                app.Services.GetRequiredService<StationRegistryService>(),
                baseUrl);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

}
