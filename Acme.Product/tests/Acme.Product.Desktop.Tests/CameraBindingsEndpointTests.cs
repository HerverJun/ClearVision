using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Acme.Product.Desktop.Endpoints;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Acme.Product.Desktop.Tests;

public class CameraBindingsEndpointTests
{
    [Fact]
    public async Task GetCameraBindings_ShouldProjectRuntimeConnectionStatus()
    {
        var connectedCamera = Substitute.For<ICamera>();
        connectedCamera.IsConnected.Returns(true);

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new() { Id = "cam-connected", DisplayName = "Connected", SerialNumber = "SN-CONNECTED", IpAddress = "10.0.0.1", IsEnabled = true, TriggerMode = "Hardware", HardwareTriggerSource = "Line2" },
            new()
            {
                Id = "cam-online",
                DisplayName = "Online",
                SerialNumber = "SN-ONLINE",
                IpAddress = "10.0.0.2",
                IsEnabled = true,
                TriggerMode = "Software",
                SoftwareTriggerSource = "KeyboardEnter",
                EnterPhotoelectricDebounceMs = 75,
                EnterPhotoelectricTimeoutMs = 12000,
                EnterPhotoelectricDeviceId = @"\\?\HID#VID_TEST",
                IgnoreEnterTriggerWhileBusy = false
            },
            new() { Id = "cam-offline", DisplayName = "Offline", SerialNumber = "SN-OFFLINE", IpAddress = "10.0.0.3", IsEnabled = true },
            new() { Id = "cam-disabled", DisplayName = "Disabled", SerialNumber = "SN-DISABLED", IpAddress = "10.0.0.4", IsEnabled = false },
            new() { Id = "cam-unbound", DisplayName = "Unbound", SerialNumber = "", IpAddress = "", IsEnabled = true }
        });
        cameraManager.GetCamera("SN-CONNECTED").Returns(connectedCamera);
        cameraManager.GetCamera("SN-ONLINE").Returns((ICamera?)null);
        cameraManager.GetCamera("SN-OFFLINE").Returns((ICamera?)null);
        cameraManager.GetCamera("SN-DISABLED").Returns((ICamera?)null);
        cameraManager.EnumerateCamerasAsync().Returns(Task.FromResult<IEnumerable<CameraInfo>>(new[]
        {
            new CameraInfo { CameraId = "SN-ONLINE", Name = "Discovered Online", IsConnected = false }
        }));

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager);
        using var response = await host.Client.GetAsync("/api/cameras/bindings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.EnumerateArray().ToList();
        items.Should().HaveCount(5);

        GetProperty(FindById(items, "cam-connected"), "ConnectionStatus", "connectionStatus").GetString().Should().Be("Connected");
        GetProperty(FindById(items, "cam-connected"), "DeviceId", "deviceId").GetString().Should().Be("SN-CONNECTED");
        GetProperty(FindById(items, "cam-connected"), "IpAddress", "ipAddress").GetString().Should().Be("10.0.0.1");
        GetProperty(FindById(items, "cam-connected"), "TriggerMode", "triggerMode").GetString().Should().Be("External");
        GetProperty(FindById(items, "cam-connected"), "HardwareTriggerSource", "hardwareTriggerSource").GetString().Should().Be("Line2");
        GetProperty(FindById(items, "cam-connected"), "TargetFrameRateFps", "targetFrameRateFps").GetInt32().Should().Be(10);
        GetProperty(FindById(items, "cam-online"), "SoftwareTriggerSource", "softwareTriggerSource").GetString().Should().Be("EnterPhotoelectric");
        GetProperty(FindById(items, "cam-online"), "EnterPhotoelectricDebounceMs", "enterPhotoelectricDebounceMs").GetInt32().Should().Be(75);
        GetProperty(FindById(items, "cam-online"), "EnterPhotoelectricTimeoutMs", "enterPhotoelectricTimeoutMs").GetInt32().Should().Be(12000);
        GetProperty(FindById(items, "cam-online"), "EnterPhotoelectricDeviceId", "enterPhotoelectricDeviceId").GetString().Should().Be(@"\\?\HID#VID_TEST");
        GetProperty(FindById(items, "cam-online"), "IgnoreEnterTriggerWhileBusy", "ignoreEnterTriggerWhileBusy").GetBoolean().Should().BeFalse();
        GetProperty(FindById(items, "cam-online"), "ConnectionStatus", "connectionStatus").GetString().Should().Be("Online");
        GetProperty(FindById(items, "cam-offline"), "ConnectionStatus", "connectionStatus").GetString().Should().Be("Offline");
        GetProperty(FindById(items, "cam-disabled"), "ConnectionStatus", "connectionStatus").GetString().Should().Be("Disabled");
        GetProperty(FindById(items, "cam-unbound"), "ConnectionStatus", "connectionStatus").GetString().Should().Be("Unbound");
    }

    [Fact]
    public async Task UpdateCameraBindings_WhenActiveStreamRuntimeSettingsChange_ShouldReturnConflict()
    {
        var existingBinding = new CameraBindingConfig
        {
            Id = "cam-active",
            DisplayName = "Active Camera",
            SerialNumber = "SN-ACTIVE",
            TriggerMode = "Continuous",
            ExposureTimeUs = 5000
        };

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { existingBinding });

        var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
        streamCoordinator.SnapshotStreamUsage("cam-active").Returns(new CameraStreamUsageSnapshot(
            "cam-active",
            true,
            0,
            1,
            0,
            CameraTriggerMode.Continuous,
            10));

        var configService = Substitute.For<IConfigurationService>();
        configService.LoadAsync().Returns(Task.FromResult(new AppConfig()));
        configService.GetCurrent().Returns(new AppConfig());
        configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, streamCoordinator, configService);
        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            activeCameraId = "cam-active",
            bindings = new[]
            {
                new CameraBindingConfig
                {
                    Id = "cam-active",
                    DisplayName = "Active Camera",
                    SerialNumber = "SN-ACTIVE",
                    TriggerMode = "Continuous",
                    ExposureTimeUs = 8000
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("相机流正在运行");
        cameraManager.DidNotReceive().UpdateBindings(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
        await configService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    [Fact]
    public async Task UpdateCameraBindings_WhenActiveStreamBindingIsRemoved_ShouldReturnConflict()
    {
        var existingBindings = new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-active",
                DisplayName = "Active Camera",
                SerialNumber = "SN-ACTIVE",
                TriggerMode = "Continuous"
            },
            new()
            {
                Id = "cam-kept",
                DisplayName = "Kept Camera",
                SerialNumber = "SN-KEPT",
                TriggerMode = "Software"
            }
        };

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(existingBindings);

        var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
        streamCoordinator.SnapshotStreamUsage("cam-active").Returns(new CameraStreamUsageSnapshot(
            "cam-active",
            true,
            1,
            0,
            0,
            CameraTriggerMode.Continuous,
            10));

        var configService = Substitute.For<IConfigurationService>();
        configService.LoadAsync().Returns(Task.FromResult(new AppConfig()));
        configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, streamCoordinator, configService);
        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            activeCameraId = "cam-kept",
            bindings = new[]
            {
                new CameraBindingConfig
                {
                    Id = "cam-kept",
                    DisplayName = "Kept Camera",
                    SerialNumber = "SN-KEPT",
                    TriggerMode = "Software"
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("相机流正在运行");
        cameraManager.DidNotReceive().UpdateBindings(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
        await configService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    [Fact]
    public async Task UpdateCameraBindings_WhenInactiveBindingIsRemoved_ShouldSave()
    {
        var existingBindings = new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-removed",
                DisplayName = "Removed Camera",
                SerialNumber = "SN-REMOVED",
                TriggerMode = "Continuous"
            },
            new()
            {
                Id = "cam-kept",
                DisplayName = "Kept Camera",
                SerialNumber = "SN-KEPT",
                TriggerMode = "Software"
            }
        };

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(existingBindings);

        var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
        streamCoordinator.SnapshotStreamUsage("cam-removed").Returns(new CameraStreamUsageSnapshot(
            "cam-removed",
            false,
            0,
            0,
            0,
            CameraTriggerMode.Continuous,
            10));

        var configService = Substitute.For<IConfigurationService>();
        configService.LoadAsync().Returns(Task.FromResult(new AppConfig()));
        configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, streamCoordinator, configService);
        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            activeCameraId = "cam-kept",
            bindings = new[]
            {
                new CameraBindingConfig
                {
                    Id = "cam-kept",
                    DisplayName = "Kept Camera",
                    SerialNumber = "SN-KEPT",
                    TriggerMode = "Software"
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cameraManager.Received(1).UpdateBindings(
            Arg.Is<List<CameraBindingConfig>>(bindings =>
                bindings.Count == 1 &&
                bindings[0].Id == "cam-kept"),
            "cam-kept");
        await configService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.ActiveCameraId == "cam-kept" &&
            config.Cameras.Count == 1 &&
            config.Cameras[0].Id == "cam-kept"));
    }

    [Fact]
    public async Task UpdateSettings_ShouldPreserveCurrentCameraBindings()
    {
        var currentBinding = new CameraBindingConfig
        {
            Id = "cam-current",
            DisplayName = "Current Camera",
            SerialNumber = "SN-CURRENT",
            TriggerMode = "External",
            HardwareTriggerSource = "Line2"
        };
        var currentConfig = new AppConfig
        {
            Cameras = new List<CameraBindingConfig> { currentBinding },
            ActiveCameraId = "cam-current"
        };

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());

        var configService = Substitute.For<IConfigurationService>();
        configService.LoadAsync().Returns(Task.FromResult(currentConfig));
        configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);

        await using var host = await CameraBindingsTestHost.CreateAsync(
            cameraManager,
            configService: configService);
        var response = await host.Client.PutAsJsonAsync("/api/settings", new AppConfig
        {
            General = new GeneralConfig { SoftwareTitle = "Updated Title" },
            Cameras = new List<CameraBindingConfig>
            {
                new()
                {
                    Id = "cam-request",
                    DisplayName = "Request Camera",
                    SerialNumber = "SN-REQUEST",
                    TriggerMode = "Software"
                }
            },
            ActiveCameraId = "cam-request"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await configService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.General.SoftwareTitle == "Updated Title" &&
            config.ActiveCameraId == "cam-current" &&
            config.Cameras.Count == 1 &&
            config.Cameras[0].Id == "cam-current" &&
            config.Cameras[0].HardwareTriggerSource == "Line2"));
    }

    private static JsonElement FindById(List<JsonElement> items, string id)
    {
        return items.Single(item => GetProperty(item, "Id", "id").GetString() == id);
    }

    private static JsonElement GetProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                return property;
            }
        }

        throw new KeyNotFoundException(string.Join(", ", propertyNames));
    }

    private sealed class CameraBindingsTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private CameraBindingsTestHost(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<CameraBindingsTestHost> CreateAsync(
            ICameraManager cameraManager,
            ICameraFrameStreamCoordinator? streamCoordinator = null,
            IConfigurationService? configService = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(cameraManager);
            builder.Services.AddSingleton(streamCoordinator ?? Substitute.For<ICameraFrameStreamCoordinator>());
            builder.Services.AddSingleton(Substitute.For<ITriggerInputService>());

            if (configService == null)
            {
                configService = Substitute.For<IConfigurationService>();
                configService.LoadAsync().Returns(Task.FromResult(new AppConfig()));
                configService.GetCurrent().Returns(new AppConfig());
                configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
            }

            builder.Services.AddSingleton(configService);

            var aiConfigStore = new AiConfigStore(
                Options.Create(new AiGenerationOptions
                {
                    Provider = "openai",
                    Model = "gpt-4o-mini",
                    ApiKey = "test-key"
                }),
                NullLogger<AiConfigStore>.Instance);
            builder.Services.AddSingleton(aiConfigStore);
            builder.Services.AddSingleton(new AiApiClient(new HttpClient(), aiConfigStore));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = "admin",
                    Username = "admin",
                    Role = "Admin"
                };
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new CameraBindingsTestHost(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
