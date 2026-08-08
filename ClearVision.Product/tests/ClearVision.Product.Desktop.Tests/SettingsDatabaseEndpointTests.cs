using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public sealed class SettingsDatabaseEndpointTests
{
    [Fact]
    public async Task DatabaseStatusAndRepairEndpoints_ShouldReturnHealthyStatus()
    {
        await using var host = await SettingsDatabaseEndpointTestHost.CreateAsync();

        using var statusResponse = await host.Client.GetAsync("/api/settings/database/status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadJsonAsync(statusResponse);
        GetProperty(status, "state").GetString().Should().Be(VisionDatabaseState.Healthy);
        GetProperty(status, "exists").GetBoolean().Should().BeTrue();
        GetProperty(status, "databasePath").GetString().Should().Be(host.DatabasePath);

        using var repairResponse = await host.Client.PostAsync("/api/settings/database/repair", null);
        repairResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var repair = await ReadJsonAsync(repairResponse);
        GetProperty(repair, "state").GetString().Should().Be(VisionDatabaseState.Healthy);
    }

    [Fact]
    public async Task DatabaseBackupRestoreAndCleanupEndpoints_ShouldBindRequestsAndReturnResults()
    {
        await using var host = await SettingsDatabaseEndpointTestHost.CreateAsync(retentionDays: 17);

        using var backupResponse = await host.Client.PostAsync("/api/settings/database/backup", null);
        backupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var backup = await ReadJsonAsync(backupResponse);
        var backupPath = GetProperty(backup, "backupPath").GetString();
        backupPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(backupPath).Should().BeTrue();

        using var restoreResponse = await host.Client.PostAsJsonAsync("/api/settings/database/restore", new VisionDatabaseRestoreRequest
        {
            BackupPath = backupPath
        });
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var restore = await ReadJsonAsync(restoreResponse);
        GetProperty(restore, "backupPath").GetString().Should().Be(backupPath);
        GetProperty(restore, "safetyBackupPath").GetString().Should().NotBeNullOrWhiteSpace();

        using var cleanupResponse = await host.Client.PostAsJsonAsync("/api/settings/database/cleanup", new VisionDatabaseCleanupRequest());
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cleanup = await ReadJsonAsync(cleanupResponse);
        GetProperty(cleanup, "retentionDays").GetInt32().Should().Be(17);
        GetProperty(cleanup, "deletedRows").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task DatabaseMaintenanceWriteEndpoints_ShouldRequireAdminRole()
    {
        await using var host = await SettingsDatabaseEndpointTestHost.CreateAsync(role: UserRole.Engineer.ToString());

        using var repairResponse = await host.Client.PostAsync("/api/settings/database/repair", null);
        using var backupResponse = await host.Client.PostAsync("/api/settings/database/backup", null);
        using var restoreResponse = await host.Client.PostAsJsonAsync("/api/settings/database/restore", new VisionDatabaseRestoreRequest
        {
            BackupPath = Path.Combine(host.RootDirectory, "missing.cvdbbak")
        });
        using var cleanupResponse = await host.Client.PostAsJsonAsync("/api/settings/database/cleanup", new VisionDatabaseCleanupRequest
        {
            RetentionDays = 7
        });

        repairResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        backupResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DatabaseRestoreEndpoint_ShouldMapInvalidBackupRequests()
    {
        await using var host = await SettingsDatabaseEndpointTestHost.CreateAsync();

        using var emptyPathResponse = await host.Client.PostAsJsonAsync("/api/settings/database/restore", new VisionDatabaseRestoreRequest());
        emptyPathResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var emptyPathPayload = await ReadJsonAsync(emptyPathResponse);
        GetProperty(emptyPathPayload, "error").GetString().Should().Be("BackupPath is required.");

        var missingBackupPath = Path.Combine(host.RootDirectory, "missing.cvdbbak");
        using var missingFileResponse = await host.Client.PostAsJsonAsync("/api/settings/database/restore", new VisionDatabaseRestoreRequest
        {
            BackupPath = missingBackupPath
        });
        missingFileResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missingFilePayload = await ReadJsonAsync(missingFileResponse);
        GetProperty(missingFilePayload, "error").GetString().Should().Contain("Database backup file was not found.");

        using var liveDatabaseResponse = await host.Client.PostAsJsonAsync("/api/settings/database/restore", new VisionDatabaseRestoreRequest
        {
            BackupPath = host.DatabasePath
        });
        liveDatabaseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var liveDatabasePayload = await ReadJsonAsync(liveDatabaseResponse);
        GetProperty(liveDatabasePayload, "error").GetString().Should().Contain("live database file");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new KeyNotFoundException(propertyName);
    }

    private sealed class SettingsDatabaseEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SettingsDatabaseEndpointTestHost(WebApplication app, string rootDirectory, string databasePath)
        {
            _app = app;
            RootDirectory = rootDirectory;
            DatabasePath = databasePath;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public string RootDirectory { get; }

        public string DatabasePath { get; }

        public static async Task<SettingsDatabaseEndpointTestHost> CreateAsync(
            string? role = "Admin",
            int retentionDays = 30)
        {
            var root = Path.Combine(Path.GetTempPath(), $"cv-settings-db-endpoints-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(root, "vision.db");
            var packageRoot = Path.Combine(root, "packages");
            var backupRoot = Path.Combine(root, "backups");
            Directory.CreateDirectory(root);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddSingleton(Substitute.For<ClearVision.Product.Core.Cameras.ICameraManager>());
            builder.Services.AddSingleton(sp => new VisionDatabaseMaintenanceService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<VisionDatabaseMaintenanceService>.Instance,
                new VisionDatabaseMaintenanceOptions
                {
                    BackupRootDirectory = backupRoot,
                    PackageRootDirectory = packageRoot
                }));

            var configService = Substitute.For<IConfigurationService>();
            configService.GetCurrent().Returns(new AppConfig
            {
                Storage = new StorageConfig
                {
                    RetentionDays = retentionDays
                }
            });
            builder.Services.AddSingleton(configService);

            var aiConfigStore = new AiConfigStore(
                Options.Create(new AiGenerationOptions
                {
                    Provider = "openai",
                    Model = "gpt-4o-mini",
                    ApiKey = "default-key",
                    BaseUrl = "https://api.openai.com/v1",
                    TimeoutSeconds = 90
                }),
                NullLogger<AiConfigStore>.Instance,
                Path.Combine(root, "ai"));
            builder.Services.AddSingleton(aiConfigStore);
            builder.Services.AddSingleton(new AiApiClient(new HttpClient(), aiConfigStore));
            builder.Services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
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
            app.MapSettingsEndpoints();
            await app.StartAsync();
            await InitializeDatabaseAsync(app.Services);
            return new SettingsDatabaseEndpointTestHost(app, root, databasePath);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(RootDirectory);
        }

        private static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            await VisionDatabaseInitializer.InitializeAsync(db);
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user")
            ], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
