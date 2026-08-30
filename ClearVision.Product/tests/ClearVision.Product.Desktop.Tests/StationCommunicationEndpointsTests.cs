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

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

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

    [Fact]
    public async Task PutSettings_PersistencePermissionFailure_ShouldReturnStructured503WithoutFalseSuccess()
    {
        var fault = new ArmableFaultInjector();
        fault.FailNext(
            StationCommunicationPersistenceStage.StudioCandidateWrite,
            static () => new UnauthorizedAccessException("injected endpoint permission failure"));
        await using var host = await StationCommunicationTestHost.CreateAsync(
            persistenceFaultInjector: fault);

        using var response = await host.Client.PutAsync(
            "/api/station-communication/settings",
            JsonContent(new { mode = "LocalLoopback", port = 5040 }));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().NotContain("injected endpoint permission failure");
        payload.Should().NotContain(host.Store.StudioSettingsPath);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("errorCode").GetString().Should().Be("STATION_COMMUNICATION_PERMISSION_DENIED");
        root.GetProperty("publicMessage").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("stage").GetString().Should().Be("candidate");
        root.GetProperty("retryable").GetBoolean().Should().BeTrue();
        root.GetProperty("settings").ValueKind.Should().Be(JsonValueKind.Null);
        File.Exists(host.Store.StudioSettingsPath).Should().BeFalse();
        File.Exists(host.Store.StationSyncSettingsPath).Should().BeFalse();
    }

    [Fact]
    public async Task RegenerateToken_PersistenceFailure_ShouldReturn503AndNotExposeCandidateToken()
    {
        var fault = new ArmableFaultInjector();
        await using var host = await StationCommunicationTestHost.CreateAsync(
            persistenceFaultInjector: fault);
        using var saveResponse = await host.Client.PutAsync(
            "/api/station-communication/settings",
            JsonContent(new { mode = "LocalLoopback", port = 5041 }));
        saveResponse.EnsureSuccessStatusCode();
        using var revealResponse = await host.Client.PostAsync(
            "/api/station-communication/token",
            JsonContent(new { operation = "reveal" }));
        using var revealDocument = JsonDocument.Parse(await revealResponse.Content.ReadAsStringAsync());
        var previousToken = revealDocument.RootElement.GetProperty("token").GetString()!;

        fault.FailNext(
            StationCommunicationPersistenceStage.StationPublish,
            static () => new IOException("injected endpoint publish failure"));
        using var response = await host.Client.PostAsync(
            "/api/station-communication/token",
            JsonContent(new { operation = "regenerate" }));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().NotContain("injected endpoint publish failure");
        payload.Should().NotContain(previousToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("errorCode").GetString().Should().Be("STATION_COMMUNICATION_IO_FAILED");
        root.GetProperty("publicMessage").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("stage").GetString().Should().Be("station-publish");
        root.GetProperty("token").GetString().Should().BeEmpty();
        root.GetProperty("tokenInfo").GetProperty("hasToken").GetBoolean().Should().BeFalse();

        using var revealAfterFailure = await host.Client.PostAsync(
            "/api/station-communication/token",
            JsonContent(new { operation = "reveal" }));
        using var revealAfterFailureDocument = JsonDocument.Parse(
            await revealAfterFailure.Content.ReadAsStringAsync());
        revealAfterFailureDocument.RootElement.GetProperty("token").GetString().Should().Be(previousToken);
    }

    [Fact]
    public async Task GetSettings_MalformedAuthoritativeFile_ShouldReturnStructured503WithoutRewritingIt()
    {
        await using var host = await StationCommunicationTestHost.CreateAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(host.Store.StudioSettingsPath)!);
        const string malformed = "{ malformed-station-settings";
        File.WriteAllText(host.Store.StudioSettingsPath, malformed, Encoding.UTF8);

        using var response = await host.Client.GetAsync("/api/station-communication/settings");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("errorCode").GetString().Should().Be("STATION_COMMUNICATION_RECOVERY_REQUIRED");
        root.GetProperty("stage").GetString().Should().Be("authoritative-read");
        root.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        File.ReadAllText(host.Store.StudioSettingsPath, Encoding.UTF8).Should().Be(malformed);
        File.Exists(host.Store.StationSyncSettingsPath).Should().BeFalse();
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

        public static async Task<StationCommunicationTestHost> CreateAsync(
            string? role = "Admin",
            IStationCommunicationPersistenceFaultInjector? persistenceFaultInjector = null)
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
            var defaultStore = new StationCommunicationSettingsStore(root);
            var store = persistenceFaultInjector == null
                ? defaultStore
                : new StationCommunicationSettingsStore(
                    defaultStore.StudioSettingsPath,
                    defaultStore.StationSyncSettingsPath,
                    persistenceFaultInjector);
            builder.Services.AddSingleton(store);
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

    private sealed class ArmableFaultInjector : IStationCommunicationPersistenceFaultInjector
    {
        private readonly object _gate = new();
        private StationCommunicationPersistenceStage? _stage;
        private Func<Exception>? _exceptionFactory;

        public void FailNext(
            StationCommunicationPersistenceStage stage,
            Func<Exception> exceptionFactory)
        {
            lock (_gate)
            {
                _stage = stage;
                _exceptionFactory = exceptionFactory;
            }
        }

        public void OnStage(StationCommunicationPersistenceStage stage, string generationId)
        {
            Func<Exception>? exceptionFactory = null;
            lock (_gate)
            {
                if (_stage == stage)
                {
                    exceptionFactory = _exceptionFactory;
                    _stage = null;
                    _exceptionFactory = null;
                }
            }

            if (exceptionFactory != null)
            {
                throw exceptionFactory();
            }
        }
    }
}
