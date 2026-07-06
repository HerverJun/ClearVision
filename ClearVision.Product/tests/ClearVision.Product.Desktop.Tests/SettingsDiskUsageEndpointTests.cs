using System.Net;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.AI;
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

public class SettingsDiskUsageEndpointTests
{
    [Fact]
    public async Task GetDiskUsage_WhenCurrentConfiguredPathIsInvalid_ShouldReturnFallbackDiskUsage()
    {
        var config = new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = "\0invalid-path"
            }
        };
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(config);

        using var response = await host.Client.GetAsync("/api/settings/disk-usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sourcePath = document.RootElement.GetProperty("sourcePath").GetString();
        sourcePath.Should().Be(Path.GetFullPath(InspectionImagePersistencePaths.GetFallbackImageSaveRoot()));
        document.RootElement.GetProperty("driveName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDiskUsage_WhenCurrentConfiguredPathIsCreatable_ShouldReturnConfiguredPathBeforeFallback()
    {
        var directory = CreateTempDirectory();
        var configuredPath = Path.Combine(directory, "configured-images");
        var config = new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = configuredPath
            }
        };
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(config);

        try
        {
            using var response = await host.Client.GetAsync("/api/settings/disk-usage");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            Directory.Exists(configuredPath).Should().BeFalse();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("sourcePath").GetString().Should().Be(Path.GetFullPath(configuredPath));
            document.RootElement.GetProperty("isAccessible").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("canWrite").GetBoolean().Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task GetDiskUsage_WhenCurrentConfiguredPathIsFile_ShouldReturnFallbackDiskUsage()
    {
        var directory = CreateTempDirectory();
        var configuredPath = Path.Combine(directory, "configured-images");
        await File.WriteAllTextAsync(configuredPath, "not a directory");
        var config = new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = configuredPath
            }
        };
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(config);

        try
        {
            using var response = await host.Client.GetAsync("/api/settings/disk-usage");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("sourcePath").GetString()
                .Should()
                .Be(Path.GetFullPath(InspectionImagePersistencePaths.GetFallbackImageSaveRoot()));
            document.RootElement.GetProperty("canWrite").GetBoolean().Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task GetDiskUsage_WithExplicitUnavailablePath_ShouldReturnBadRequest()
    {
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(new AppConfig());
        var unavailablePath = CreateUnavailableRootPath();
        var requestUri = "/api/settings/disk-usage?path=" + Uri.EscapeDataString(unavailablePath);

        using var response = await host.Client.GetAsync(requestUri);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDiskUsage_WithExplicitWritableDirectory_ShouldReturnAccessFlags()
    {
        var directory = CreateTempDirectory();
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(new AppConfig());
        var requestUri = "/api/settings/disk-usage?path=" + Uri.EscapeDataString(directory);

        try
        {
            using var response = await host.Client.GetAsync(requestUri);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("sourcePath").GetString().Should().Be(Path.GetFullPath(directory));
            document.RootElement.GetProperty("isAccessible").GetBoolean().Should().BeTrue();
            document.RootElement.GetProperty("canWrite").GetBoolean().Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task GetDiskUsage_WithExplicitFilePath_ShouldReturnUnwritableDirectoryFlags()
    {
        var directory = CreateTempDirectory();
        var filePath = Path.Combine(directory, "image-root.txt");
        await File.WriteAllTextAsync(filePath, "not a directory");
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(new AppConfig());
        var requestUri = "/api/settings/disk-usage?path=" + Uri.EscapeDataString(filePath);

        try
        {
            using var response = await host.Client.GetAsync(requestUri);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("sourcePath").GetString().Should().Be(Path.GetFullPath(filePath));
            document.RootElement.GetProperty("isAccessible").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("canWrite").GetBoolean().Should().BeFalse();
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task GetDiskUsage_WithExplicitCreatableDirectory_ShouldReturnWritableButNotAccessible()
    {
        var directory = CreateTempDirectory();
        var targetPath = Path.Combine(directory, "new-root", "images");
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(new AppConfig());
        var requestUri = "/api/settings/disk-usage?path=" + Uri.EscapeDataString(targetPath);

        try
        {
            using var response = await host.Client.GetAsync(requestUri);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            Directory.Exists(targetPath).Should().BeFalse();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("sourcePath").GetString().Should().Be(Path.GetFullPath(targetPath));
            document.RootElement.GetProperty("isAccessible").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("canWrite").GetBoolean().Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task GetDiskUsage_ShouldRejectOperator()
    {
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(new AppConfig(), role: "Operator");

        using var response = await host.Client.GetAsync("/api/settings/disk-usage");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDiskUsage_WithFileInParentPath_ShouldReturnUnwritableDirectoryFlags()
    {
        var directory = CreateTempDirectory();
        var fileBlocker = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(fileBlocker, "not a directory");
        var targetPath = Path.Combine(fileBlocker, "images");
        await using var host = await SettingsDiskUsageTestHost.CreateAsync(new AppConfig());
        var requestUri = "/api/settings/disk-usage?path=" + Uri.EscapeDataString(targetPath);

        try
        {
            using var response = await host.Client.GetAsync(requestUri);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("sourcePath").GetString().Should().Be(Path.GetFullPath(targetPath));
            document.RootElement.GetProperty("isAccessible").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("canWrite").GetBoolean().Should().BeFalse();
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static string CreateUnavailableRootPath()
    {
        foreach (var letter in Enumerable.Range(0, 26).Select(offset => (char)('Z' - offset)))
        {
            var root = $"{letter}:\\";
            if (!Directory.Exists(root))
            {
                return Path.Combine(root, "ClearVisionDiskUsageTests", Guid.NewGuid().ToString("N"));
            }
        }

        return @"\\127.0.0.1\ClearVisionMissingShare\DiskUsage";
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(GetTempRootDirectory(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        var tempRoot = GetTempRootDirectory();
        if (Directory.Exists(tempRoot) && !Directory.EnumerateFileSystemEntries(tempRoot).Any())
        {
            Directory.Delete(tempRoot);
        }
    }

    private static string GetTempRootDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "ClearVisionDiskUsageTests");
    }

    private sealed class SettingsDiskUsageTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _aiConfigRoot;

        private SettingsDiskUsageTestHost(WebApplication app, string aiConfigRoot)
        {
            _app = app;
            _aiConfigRoot = aiConfigRoot;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<SettingsDiskUsageTestHost> CreateAsync(AppConfig config, string role = "Admin")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var configService = Substitute.For<IConfigurationService>();
            configService.LoadAsync().Returns(Task.FromResult(config));
            configService.GetCurrent().Returns(config);
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(configService);
            builder.Services.AddSingleton(Substitute.For<ClearVision.Product.Core.Cameras.ICameraManager>());

            var aiConfigRoot = Path.Combine(Path.GetTempPath(), $"cv-settings-disk-usage-{Guid.NewGuid():N}");
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
                aiConfigRoot);
            builder.Services.AddSingleton(aiConfigStore);
            builder.Services.AddSingleton(new AiApiClient(new HttpClient(), aiConfigStore));

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
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new SettingsDiskUsageTestHost(app, aiConfigRoot);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_aiConfigRoot))
            {
                Directory.Delete(_aiConfigRoot, recursive: true);
            }
        }
    }
}
