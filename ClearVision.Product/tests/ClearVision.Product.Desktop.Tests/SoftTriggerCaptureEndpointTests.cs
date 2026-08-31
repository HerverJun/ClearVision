using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
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

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public class SoftTriggerCaptureEndpointTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task SoftTriggerCapture_WithEmptyCameraBindingId_ShouldReturnBadRequest()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CameraBindingId is required.");
    }

    [Fact]
    public async Task SoftTriggerCapture_WithUnknownBinding_ShouldReturnNotFound()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "known-binding",
                SerialNumber = "SN-001",
                ExposureTimeUs = 4000,
                GainDb = 1.2
            }
        });

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = "missing-binding" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Camera binding not found");
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HardwareOperationEndpoints_ShouldRejectOperator()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        var serialTriggerInput = Substitute.For<ISerialPhotoelectricTriggerInputService>();

        await using var host = await SoftTriggerTestHost.CreateAsync(
            cameraManager,
            serialPhotoelectricTriggerInputService: serialTriggerInput,
            role: "Operator");

        var captureResponse = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = "cam-a" });
        var serialTestResponse = await host.Client.PostAsJsonAsync("/api/trigger-input/test-serial-photoelectric", new
        {
            portName = "COM3",
            baudRate = 9600
        });
        var serialPortsResponse = await host.Client.GetAsync("/api/trigger-input/serial-photoelectric-ports");

        captureResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        serialTestResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        serialPortsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await serialTriggerInput.DidNotReceive().WaitForSerialPhotoelectricAsync(
            Arg.Any<SerialPhotoelectricTriggerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoftTriggerCapture_WithValidRequest_ShouldReturnPngAndHeaders()
    {
        const string bindingId = "cam-bind-1";
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            SerialNumber = "SN-001",
            ExposureTimeUs = 5500.5,
            GainDb = 3.2
        };

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(ValidPngBytes));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        var cameraLease = ConfigureCameraLease(cameraManager, bindingId, camera);

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = bindingId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        response.Headers.GetValues("X-Image-Width").Single().Should().Be("1");
        response.Headers.GetValues("X-Image-Height").Single().Should().Be("1");
        response.Headers.GetValues("X-Camera-Id").Single().Should().Be(bindingId);
        response.Headers.GetValues("X-Trigger-Mode").Single().Should().Be("Software");

        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        bodyBytes.Should().Equal(ValidPngBytes);

        await cameraManager.Received(1).AcquireByBindingLeaseAsync(
            bindingId,
            Arg.Any<CancellationToken>());
        cameraLease.DisposeCallCount.Should().Be(1);
        await camera.Received(1).SetExposureTimeAsync(binding.ExposureTimeUs);
        await camera.Received(1).SetGainAsync(binding.GainDb);
        await camera.Received(1).AcquireSingleFrameAsync();
    }

    [Fact]
    public async Task SoftTriggerCapture_WithEnterPhotoelectricSource_ShouldWaitForEnterSignal()
    {
        const string bindingId = "cam-enter-trigger";
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            DisplayName = "Enter Trigger",
            SerialNumber = "SN-ENTER",
            TriggerMode = "Software",
            SoftwareTriggerSource = "EnterPhotoelectric",
            EnterPhotoelectricDebounceMs = 250,
            EnterPhotoelectricTimeoutMs = 15000,
            EnterPhotoelectricDeviceId = @"\\?\HID#VID_ENTER",
            IgnoreEnterTriggerWhileBusy = false
        };

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(ValidPngBytes));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        ConfigureCameraLease(cameraManager, bindingId, camera);

        var triggerInput = Substitute.For<ITriggerInputService>();
        triggerInput
            .WaitForEnterPhotoelectricAsync(Arg.Any<EnterPhotoelectricTriggerOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.ArgAt<EnterPhotoelectricTriggerOptions>(0);
                return Task.FromResult(new TriggerInputEvent(
                    "EnterPhotoelectric",
                    options.CameraBindingId,
                    options.DeviceId,
                    DateTime.UtcNow));
            });

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager, triggerInput);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = bindingId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Trigger-Source").Single().Should().Be("EnterPhotoelectric");
        await triggerInput.Received(1).WaitForEnterPhotoelectricAsync(
            Arg.Is<EnterPhotoelectricTriggerOptions>(options =>
                options.CameraBindingId == bindingId &&
                options.DeviceId == binding.EnterPhotoelectricDeviceId &&
                options.DebounceMs == 250 &&
                options.TimeoutMs == 15000 &&
                options.IgnoreWhileBusy == false),
            Arg.Any<CancellationToken>());
        await camera.Received(1).AcquireSingleFrameAsync();
    }

    [Fact]
    public async Task SoftTriggerCapture_WithSerialPhotoelectricSource_ShouldWaitForSerialSignal()
    {
        const string bindingId = "cam-serial-trigger";
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            DisplayName = "Serial Trigger",
            SerialNumber = "SN-SERIAL",
            TriggerMode = "Software",
            SoftwareTriggerSource = "SerialPhotoelectric",
            SerialPhotoelectricPortName = "COM3",
            SerialPhotoelectricBaudRate = 9600,
            SerialPhotoelectricDebounceMs = 300,
            SerialPhotoelectricTimeoutMs = 12000,
            IgnoreSerialPhotoelectricTriggerWhileBusy = false
        };

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(ValidPngBytes));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        ConfigureCameraLease(cameraManager, bindingId, camera);

        var serialTriggerInput = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        serialTriggerInput
            .WaitForSerialPhotoelectricAsync(Arg.Any<SerialPhotoelectricTriggerOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.ArgAt<SerialPhotoelectricTriggerOptions>(0);
                return Task.FromResult(new TriggerInputEvent(
                    "SerialPhotoelectric",
                    options.CameraBindingId,
                    options.PortName,
                    DateTime.UtcNow));
            });

        await using var host = await SoftTriggerTestHost.CreateAsync(
            cameraManager,
            serialPhotoelectricTriggerInputService: serialTriggerInput);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = bindingId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Trigger-Source").Single().Should().Be("SerialPhotoelectric");
        await serialTriggerInput.Received(1).WaitForSerialPhotoelectricAsync(
            Arg.Is<SerialPhotoelectricTriggerOptions>(options =>
                options.CameraBindingId == bindingId &&
                options.PortName == "COM3" &&
                options.BaudRate == 9600 &&
                options.DebounceMs == 300 &&
                options.TimeoutMs == 12000 &&
                options.IgnoreWhileBusy == false),
            Arg.Any<CancellationToken>());
        await camera.Received(1).AcquireSingleFrameAsync();
    }

    [Fact]
    public async Task SerialPhotoelectricTest_WithValidRequest_ShouldWaitForSerialSignalWithoutCamera()
    {
        const string bindingId = "saved-serial-binding";
        const long revision = 17;
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        var appConfig = new AppConfig
        {
            Revision = revision,
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = bindingId,
                    DisplayName = "Saved Serial Trigger",
                    SerialNumber = "SN-SERIAL",
                    IsEnabled = true,
                    TriggerMode = "Software",
                    SoftwareTriggerSource = "SerialPhotoelectric",
                    SerialPhotoelectricPortName = "COM3",
                    SerialPhotoelectricBaudRate = 9600,
                    SerialPhotoelectricDebounceMs = 120,
                    SerialPhotoelectricTimeoutMs = 5000,
                    IgnoreSerialPhotoelectricTriggerWhileBusy = false
                }
            ]
        };

        var serialTriggerInput = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        serialTriggerInput
            .WaitForSerialPhotoelectricAsync(Arg.Any<SerialPhotoelectricTriggerOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.ArgAt<SerialPhotoelectricTriggerOptions>(0);
                return Task.FromResult(new TriggerInputEvent(
                    "SerialPhotoelectric",
                    options.CameraBindingId,
                    options.PortName,
                    DateTime.UtcNow));
            });

        await using var host = await SoftTriggerTestHost.CreateAsync(
            cameraManager,
            serialPhotoelectricTriggerInputService: serialTriggerInput,
            appConfig: appConfig);
        var response = await host.Client.PostAsJsonAsync("/api/trigger-input/test-serial-photoelectric", new
        {
            cameraBindingId = bindingId,
            expectedRevision = revision
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("COM3");
        await serialTriggerInput.Received(1).WaitForSerialPhotoelectricAsync(
            Arg.Is<SerialPhotoelectricTriggerOptions>(options =>
                options.CameraBindingId == bindingId &&
                options.PortName == "COM3" &&
                options.BaudRate == 9600 &&
                options.DebounceMs == 120 &&
                options.TimeoutMs == 5000 &&
                options.IgnoreWhileBusy == false &&
                options.AcceptPendingSignalsAfterUtc.HasValue),
            Arg.Any<CancellationToken>());
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SerialPhotoelectricPorts_ShouldReturnDetectedPortPayload()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.GetAsync("/api/trigger-input/serial-photoelectric-ports");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SoftTriggerCapture_WithEnterPhotoelectricSource_ShouldPassPreviewPendingCutoff()
    {
        const string bindingId = "cam-enter-trigger-cutoff";
        var acceptPendingAfterUtc = new DateTime(2026, 5, 14, 8, 30, 0, DateTimeKind.Utc);
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            DisplayName = "Enter Trigger",
            SerialNumber = "SN-ENTER-CUTOFF",
            TriggerMode = "Software",
            SoftwareTriggerSource = "EnterPhotoelectric",
            EnterPhotoelectricDebounceMs = 250,
            EnterPhotoelectricTimeoutMs = 15000,
            IgnoreEnterTriggerWhileBusy = true
        };

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(ValidPngBytes));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        ConfigureCameraLease(cameraManager, bindingId, camera);

        var triggerInput = Substitute.For<ITriggerInputService>();
        triggerInput
            .WaitForEnterPhotoelectricAsync(Arg.Any<EnterPhotoelectricTriggerOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.ArgAt<EnterPhotoelectricTriggerOptions>(0);
                return Task.FromResult(new TriggerInputEvent(
                    "EnterPhotoelectric",
                    options.CameraBindingId,
                    options.DeviceId,
                    DateTime.UtcNow));
            });

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager, triggerInput);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new
        {
            cameraBindingId = bindingId,
            acceptPendingEnterSignalAfterUtc = acceptPendingAfterUtc
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await triggerInput.Received(1).WaitForEnterPhotoelectricAsync(
            Arg.Is<EnterPhotoelectricTriggerOptions>(options =>
                options.CameraBindingId == bindingId &&
                options.IgnoreWhileBusy &&
                options.AcceptPendingSignalsAfterUtc == acceptPendingAfterUtc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoftTriggerCapture_WithExternalTriggerBinding_ShouldReturnBadRequest()
    {
        const string bindingId = "cam-external";
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            SerialNumber = "SN-EXT",
            TriggerMode = "External"
        };

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = bindingId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("不是 Software 触发模式");
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoftTriggerCapture_WithInvalidPngBytes_ShouldReturnBadRequest()
    {
        const string bindingId = "cam-bind-2";
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            SerialNumber = "SN-002",
            ExposureTimeUs = 1000,
            GainDb = 2.0
        };

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5 }));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        var cameraLease = ConfigureCameraLease(cameraManager, bindingId, camera);

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = bindingId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Camera frame metadata parse failed.");
        cameraLease.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SoftTriggerCapture_WhenCameraThrows_ShouldReturnBadRequest()
    {
        const string bindingId = "cam-bind-3";
        var binding = new CameraBindingConfig
        {
            Id = bindingId,
            SerialNumber = "SN-003",
            ExposureTimeUs = 7000,
            GainDb = 4.4
        };

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns<Task<byte[]>>(_ => throw new TimeoutException("相机超时"));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        var cameraLease = ConfigureCameraLease(cameraManager, bindingId, camera);

        await using var host = await SoftTriggerTestHost.CreateAsync(cameraManager);
        var response = await host.Client.PostAsJsonAsync("/api/cameras/soft-trigger-capture", new { cameraBindingId = bindingId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("相机超时");
        cameraLease.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SerialPhotoelectricTest_WithClientRawTarget_ShouldFailClosedBeforeHandler()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        var serialTriggerInput = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var appConfig = new AppConfig { Revision = 9 };

        await using var host = await SoftTriggerTestHost.CreateAsync(
            cameraManager,
            serialPhotoelectricTriggerInputService: serialTriggerInput,
            appConfig: appConfig);
        var response = await host.Client.PostAsJsonAsync("/api/trigger-input/test-serial-photoelectric", new
        {
            cameraBindingId = "saved-binding",
            expectedRevision = 9,
            portName = "COM99",
            baudRate = 115200
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SERIAL_PHOTOELECTRIC_RAW_TARGET_FORBIDDEN");
        await serialTriggerInput.DidNotReceive().WaitForSerialPhotoelectricAsync(
            Arg.Any<SerialPhotoelectricTriggerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SerialPhotoelectricTest_WithStaleRevision_ShouldFailClosedBeforeHandler()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        var serialTriggerInput = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var appConfig = new AppConfig { Revision = 12 };

        await using var host = await SoftTriggerTestHost.CreateAsync(
            cameraManager,
            serialPhotoelectricTriggerInputService: serialTriggerInput,
            appConfig: appConfig);
        var response = await host.Client.PostAsJsonAsync("/api/trigger-input/test-serial-photoelectric", new
        {
            cameraBindingId = "saved-binding",
            expectedRevision = 11
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SERIAL_PHOTOELECTRIC_BINDING_REVISION_STALE");
        await serialTriggerInput.DidNotReceive().WaitForSerialPhotoelectricAsync(
            Arg.Any<SerialPhotoelectricTriggerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SerialPhotoelectricTest_WithUnknownBinding_ShouldFailClosedBeforeHandler()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        var serialTriggerInput = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var appConfig = new AppConfig { Revision = 12, Cameras = [] };

        await using var host = await SoftTriggerTestHost.CreateAsync(
            cameraManager,
            serialPhotoelectricTriggerInputService: serialTriggerInput,
            appConfig: appConfig);
        var response = await host.Client.PostAsJsonAsync("/api/trigger-input/test-serial-photoelectric", new
        {
            cameraBindingId = "forged-binding",
            expectedRevision = 12
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SERIAL_PHOTOELECTRIC_BINDING_NOT_FOUND");
        await serialTriggerInput.DidNotReceive().WaitForSerialPhotoelectricAsync(
            Arg.Any<SerialPhotoelectricTriggerOptions>(),
            Arg.Any<CancellationToken>());
    }

    private static TrackingCameraLease ConfigureCameraLease(
        ICameraManager cameraManager,
        string bindingId,
        ICamera camera)
    {
        var lease = new TrackingCameraLease(camera);
        cameraManager
            .AcquireByBindingLeaseAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ICameraLease>(lease));
        return lease;
    }

    private sealed class TrackingCameraLease : ICameraLease
    {
        private int _disposeCallCount;

        public TrackingCameraLease(ICamera camera)
        {
            Camera = camera;
        }

        public ICamera Camera { get; }
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SoftTriggerTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SoftTriggerTestHost(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<SoftTriggerTestHost> CreateAsync(
            ICameraManager cameraManager,
            ITriggerInputService? triggerInputService = null,
            ISerialPhotoelectricTriggerInputService? serialPhotoelectricTriggerInputService = null,
            string role = "Engineer",
            AppConfig? appConfig = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(cameraManager);
            builder.Services.AddSingleton(Substitute.For<ICameraFrameStreamCoordinator>());
            builder.Services.AddSingleton(triggerInputService ?? Substitute.For<ITriggerInputService>());
            builder.Services.AddSingleton(serialPhotoelectricTriggerInputService ?? Substitute.For<ISerialPhotoelectricTriggerInputService>());

            var currentConfig = appConfig ?? new AppConfig();
            var configService = Substitute.For<IConfigurationService>();
            configService.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(
                new AppConfigReadResult(AppConfigReadStatus.Healthy, currentConfig)));
            configService.LoadAsync().Returns(Task.FromResult(currentConfig));
            configService.GetCurrent().Returns(currentConfig);
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
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
                    UserId = role.ToLowerInvariant(),
                    Username = role.ToLowerInvariant(),
                    Role = role
                };
                await next();
            });
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new SoftTriggerTestHost(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
