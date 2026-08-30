using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Services;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

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
            new() { Id = "cam-connected", DisplayName = "Connected", SerialNumber = "SN-CONNECTED", IpAddress = "10.0.0.1", IsEnabled = true, PixelFormat = "RGB8", TriggerMode = "Hardware", HardwareTriggerSource = "Line2" },
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
        GetProperty(FindById(items, "cam-connected"), "PixelFormat", "pixelFormat").GetString().Should().Be("RGB8");
        GetProperty(FindById(items, "cam-connected"), "TargetFrameRateFps", "targetFrameRateFps").GetInt32().Should().Be(30);
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

        var configService = new InMemoryAppConfigAuthority(new AppConfig
        {
            Cameras = [existingBinding],
            ActiveCameraId = existingBinding.Id
        });

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, streamCoordinator, configService);
        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            expectedRevision = configService.GetCurrent().Revision,
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
        (await response.Content.ReadAsStringAsync()).Should().Contain("CAMERA_RUNTIME_CONFLICT");
        cameraManager.DidNotReceive().UpdateBindings(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
        await cameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
        configService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateCameraBindings_ShouldRejectOperator()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        var configService = new InMemoryAppConfigAuthority(new AppConfig());

        await using var host = await CameraBindingsTestHost.CreateAsync(
            cameraManager,
            configService: configService,
            role: "Operator");

        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            expectedRevision = configService.GetCurrent().Revision,
            activeCameraId = "",
            bindings = Array.Empty<CameraBindingConfig>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cameraManager.DidNotReceive().UpdateBindings(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
        await cameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
        configService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCameraBindings_ShouldRejectOperator()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, role: "Operator");

        using var response = await host.Client.GetAsync("/api/cameras/bindings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cameraManager.DidNotReceive().GetBindings();
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

        var configService = new InMemoryAppConfigAuthority(new AppConfig
        {
            Cameras = existingBindings,
            ActiveCameraId = "cam-active"
        });

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, streamCoordinator, configService);
        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            expectedRevision = configService.GetCurrent().Revision,
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
        (await response.Content.ReadAsStringAsync()).Should().Contain("CAMERA_RUNTIME_CONFLICT");
        cameraManager.DidNotReceive().UpdateBindings(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
        await cameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
        configService.MutationCount.Should().Be(0);
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

        var configService = new InMemoryAppConfigAuthority(new AppConfig
        {
            Cameras = existingBindings,
            ActiveCameraId = "cam-removed"
        });

        await using var host = await CameraBindingsTestHost.CreateAsync(cameraManager, streamCoordinator, configService);
        var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            expectedRevision = configService.GetCurrent().Revision,
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
        await cameraManager.Received(1).ApplyBindingsAsync(
            Arg.Is<List<CameraBindingConfig>>(bindings =>
                bindings.Count == 1 &&
                bindings[0].Id == "cam-kept"),
            "cam-kept");
        await streamCoordinator.Received(1).ReleaseIdleStreamAsync("cam-removed");
        host.SerialTriggerService.Received(1).ConfigureBindings(
            Arg.Is<IEnumerable<CameraBindingConfig>>(bindings =>
                bindings.Count() == 1 && bindings.Single().Id == "cam-kept"));
        var committed = configService.GetCurrent();
        committed.ActiveCameraId.Should().Be("cam-kept");
        committed.Cameras.Should().ContainSingle(binding => binding.Id == "cam-kept");
        committed.Revision.Should().Be(1);
    }

    [Fact]
    public async Task UpdateCameraBindings_WhenPersistFails_ShouldReturnStructuredServiceUnavailableWithoutRuntimeApply()
    {
        var existing = new CameraBindingConfig
        {
            Id = "cam-main",
            DisplayName = "Main",
            SerialNumber = "SN-OLD",
            ExposureTimeUs = 5000
        };
        var authority = new InMemoryAppConfigAuthority(new AppConfig
        {
            Cameras = [existing],
            ActiveCameraId = existing.Id
        })
        {
            FailPersist = true
        };
        var cameraManager = Substitute.For<ICameraManager>();
        await using var host = await CameraBindingsTestHost.CreateAsync(
            cameraManager,
            configService: authority);

        using var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            expectedRevision = 0,
            activeCameraId = "cam-main",
            bindings = new[]
            {
                new CameraBindingConfig
                {
                    Id = "cam-main",
                    DisplayName = "Main",
                    SerialNumber = "SN-NEW",
                    ExposureTimeUs = 5000
                }
            }
        });

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, responseJson);
        responseJson.Should().Contain("APP_CONFIG_PERSIST_FAILED");
        responseJson.Should().Contain("\"configStatus\":\"StorageFailure\"");
        responseJson.Should().Contain("\"hasLastGood\":true");
        authority.GetCurrent().Cameras.Should().ContainSingle(binding => binding.SerialNumber == "SN-OLD");
        await cameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateCameraBindings_WhenRuntimeApplyFails_ShouldReturn500AndRestorePersistedSnapshot()
    {
        var existing = new CameraBindingConfig
        {
            Id = "cam-main",
            DisplayName = "Main",
            SerialNumber = "SN-OLD",
            ExposureTimeUs = 5000
        };
        var authority = new InMemoryAppConfigAuthority(new AppConfig
        {
            Cameras = [existing],
            ActiveCameraId = existing.Id
        });
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.ApplyBindingsAsync(
                Arg.Any<List<CameraBindingConfig>>(),
                Arg.Any<string>())
            .Returns(
                Task.FromException(new InvalidOperationException("injected apply failure")),
                Task.CompletedTask);
        await using var host = await CameraBindingsTestHost.CreateAsync(
            cameraManager,
            configService: authority,
            configureDefaultApply: false);

        using var response = await host.Client.PutAsJsonAsync("/api/cameras/bindings", new
        {
            expectedRevision = 0,
            activeCameraId = "cam-main",
            bindings = new[]
            {
                new CameraBindingConfig
                {
                    Id = "cam-main",
                    DisplayName = "Main",
                    SerialNumber = "SN-NEW",
                    ExposureTimeUs = 5000
                }
            }
        });

        var responseJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, responseJson);
        responseJson.Should().Contain("APP_CONFIG_RUNTIME_APPLY_FAILED");
        responseJson.Should().Contain("\"configStatus\":\"ApplyFailed\"");
        authority.GetCurrent().Revision.Should().Be(0);
        authority.GetCurrent().Cameras.Should().ContainSingle(binding => binding.SerialNumber == "SN-OLD");
        await cameraManager.Received(2).ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
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

        var configService = new InMemoryAppConfigAuthority(currentConfig);

        await using var host = await CameraBindingsTestHost.CreateAsync(
            cameraManager,
            configService: configService);
        var response = await host.Client.PutAsJsonAsync("/api/settings", new
        {
            expectedRevision = currentConfig.Revision,
            saveScope = "general",
            general = new GeneralConfig { SoftwareTitle = "Updated Title" },
            cameras = new List<CameraBindingConfig>
            {
                new()
                {
                    Id = "cam-request",
                    DisplayName = "Request Camera",
                    SerialNumber = "SN-REQUEST",
                    TriggerMode = "Software"
                }
            },
            activeCameraId = "cam-request"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var committed = configService.GetCurrent();
        committed.General.SoftwareTitle.Should().Be("Updated Title");
        committed.ActiveCameraId.Should().Be("cam-current");
        committed.Cameras.Should().ContainSingle(binding =>
            binding.Id == "cam-current" && binding.HardwareTriggerSource == "Line2");
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

        private CameraBindingsTestHost(
            WebApplication app,
            InMemoryAppConfigAuthority configurationService,
            ISerialPhotoelectricTriggerInputService serialTriggerService)
        {
            _app = app;
            ConfigurationService = configurationService;
            SerialTriggerService = serialTriggerService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public InMemoryAppConfigAuthority ConfigurationService { get; }

        public ISerialPhotoelectricTriggerInputService SerialTriggerService { get; }

        public static async Task<CameraBindingsTestHost> CreateAsync(
            ICameraManager cameraManager,
            ICameraFrameStreamCoordinator? streamCoordinator = null,
            InMemoryAppConfigAuthority? configService = null,
            string role = "Admin",
            bool configureDefaultApply = true)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            if (configureDefaultApply)
            {
                cameraManager.ApplyBindingsAsync(
                    Arg.Any<List<CameraBindingConfig>>(),
                    Arg.Any<string>()).Returns(Task.CompletedTask);
            }
            streamCoordinator ??= Substitute.For<ICameraFrameStreamCoordinator>();
            streamCoordinator.ReleaseIdleStreamAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
            var serialTriggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
            builder.Services.AddSingleton(cameraManager);
            builder.Services.AddSingleton(streamCoordinator);
            builder.Services.AddSingleton(Substitute.For<ITriggerInputService>());
            builder.Services.AddSingleton(serialTriggerService);

            configService ??= new InMemoryAppConfigAuthority(new AppConfig());
            builder.Services.AddSingleton<IConfigurationService>(configService);
            builder.Services.AddSingleton<CameraConfigurationCoordinator>();
            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<CameraConfigurationCoordinator>>(
                NullLogger<CameraConfigurationCoordinator>.Instance);

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
                    UserId = role.ToLowerInvariant(),
                    Username = role.ToLowerInvariant(),
                    Role = role
                };
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new CameraBindingsTestHost(app, configService, serialTriggerService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
