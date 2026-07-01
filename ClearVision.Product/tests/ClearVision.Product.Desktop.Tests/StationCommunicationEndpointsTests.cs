using System.Net;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Station;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationCommunicationEndpointsTests
{
    [Fact]
    public async Task GetSettings_ShouldRejectNonAdminUser()
    {
        await using var host = await StationCommunicationTestHost.CreateAsync(role: "Engineer");

        using var response = await host.Client.GetAsync("/api/station-communication/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutSettings_ShouldRejectNonAdminUser()
    {
        await using var host = await StationCommunicationTestHost.CreateAsync(role: "Operator");

        using var response = await host.Client.PutAsync(
            "/api/station-communication/settings",
            JsonContent(new { mode = "LocalLoopback", port = 5010 }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        File.Exists(host.Store.StudioSettingsPath).Should().BeFalse();
    }

    [Fact]
    public async Task PutSettings_ShouldPersistLanControllerAndReturnRestartHints()
    {
        await using var host = await StationCommunicationTestHost.CreateAsync();

        using var response = await host.Client.PutAsync(
            "/api/station-communication/settings",
            JsonContent(new
            {
                mode = "LanController",
                port = 5033,
                lanHost = "192.168.50.10",
                localStationSyncEnabled = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("mode").GetString().Should().Be("LanController");
        root.GetProperty("remoteStationBaseUrl").GetString().Should().Be("http://192.168.50.10:5033");
        root.GetProperty("token").GetProperty("hasToken").GetBoolean().Should().BeTrue();
        root.GetProperty("requiresRestart").GetProperty("studio").GetBoolean().Should().BeTrue();
        root.GetProperty("requiresRestart").GetProperty("localStation").GetBoolean().Should().BeTrue();
        File.Exists(host.Store.StudioSettingsPath).Should().BeTrue();
        File.Exists(host.Store.StationSyncSettingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task TokenEndpoints_ShouldRevealAndRegenerateForAdminOnly()
    {
        await using var adminHost = await StationCommunicationTestHost.CreateAsync();
        using var saveResponse = await adminHost.Client.PutAsync(
            "/api/station-communication/settings",
            JsonContent(new { mode = "LocalLoopback", port = 5011 }));
        saveResponse.EnsureSuccessStatusCode();

        using var revealResponse = await adminHost.Client.PostAsync(
            "/api/station-communication/token",
            JsonContent(new { operation = "reveal" }));
        revealResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var revealDocument = JsonDocument.Parse(await revealResponse.Content.ReadAsStringAsync());
        var revealedToken = revealDocument.RootElement.GetProperty("token").GetString();
        revealedToken.Should().MatchRegex(@"^\d{6}$");

        using var regenerateResponse = await adminHost.Client.PostAsync(
            "/api/station-communication/token",
            JsonContent(new { operation = "regenerate" }));
        regenerateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var regenerateDocument = JsonDocument.Parse(await regenerateResponse.Content.ReadAsStringAsync());
        var regeneratedToken = regenerateDocument.RootElement.GetProperty("token").GetString();
        regeneratedToken.Should().MatchRegex(@"^\d{6}$");
        regeneratedToken.Should().NotBe(revealedToken);

        await using var operatorHost = await StationCommunicationTestHost.CreateAsync(role: "Operator");
        using var forbiddenResponse = await operatorHost.Client.PostAsync(
            "/api/station-communication/token",
            JsonContent(new { operation = "reveal" }));
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static StringContent JsonContent(object payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private sealed class StationCommunicationTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _root;

        private StationCommunicationTestHost(WebApplication app, StationCommunicationSettingsStore store, string root)
        {
            _app = app;
            Store = store;
            _root = root;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public StationCommunicationSettingsStore Store { get; }

        public static async Task<StationCommunicationTestHost> CreateAsync(string? role = "Admin")
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "clearvision-station-communication-endpoint-tests",
                Guid.NewGuid().ToString("N"));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(new StationCommunicationSettingsStore(root));
            builder.Services.AddSingleton(Options.Create(new StationIngressOptions
            {
                Enabled = false,
                ListenMode = StationIngressListenMode.Loopback,
                Port = 5000,
                SharedToken = string.Empty
            }));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (role is not null)
                {
                    context.Items["CurrentUser"] = new ClearVision.Product.Application.Services.UserSession
                    {
                        UserId = role.ToLowerInvariant(),
                        Username = role.ToLowerInvariant(),
                        Role = role
                    };
                }

                await next();
            });
            app.MapStationCommunicationEndpoints();
            await app.StartAsync();

            return new StationCommunicationTestHost(
                app,
                app.Services.GetRequiredService<StationCommunicationSettingsStore>(),
                root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
