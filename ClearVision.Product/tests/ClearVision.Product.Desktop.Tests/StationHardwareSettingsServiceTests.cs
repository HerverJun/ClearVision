using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Station;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationHardwareSettingsServiceTests
{
    [Fact]
    public async Task ApplyCurrentAsync_ShouldLoadLocalCameraBindingsIntoRuntimeManager()
    {
        var configService = new InMemoryAppConfigAuthority(new AppConfig
        {
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-line-a",
                    DisplayName = "Line A",
                    SerialNumber = "SN-A",
                    ExposureTimeUs = 8200,
                    GainDb = 2.5
                }
            ],
            ActiveCameraId = "cam-line-a",
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolFins,
                Fins = new PlcCommunicationProfile
                {
                    IpAddress = "192.168.30.20",
                    Port = 9600
                }
            }
        });

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);

        await sut.ApplyCurrentAsync();

        cameraManager.Received(1).LoadBindings(
            Arg.Is<List<CameraBindingConfig>>(bindings =>
                bindings.Count == 1 &&
                bindings[0].Id == "cam-line-a" &&
                bindings[0].SerialNumber == "SN-A" &&
                Math.Abs(bindings[0].ExposureTimeUs - 8200) < 0.01),
            "cam-line-a");
        configService.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveCameraBindingsAsync_ShouldPersistAndApplyRuntimeBindings()
    {
        var currentConfig = new AppConfig
        {
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolS7,
                S7 = new S7CommunicationProfile
                {
                    IpAddress = "192.168.10.5",
                    Port = 102,
                    Rack = 0,
                    Slot = 1
                }
            }
        };
        var configService = new InMemoryAppConfigAuthority(currentConfig);

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);
        var inputBindings = new List<CameraBindingConfig>
        {
            new()
            {
                Id = " cam-disabled ",
                DisplayName = " Disabled Camera ",
                SerialNumber = " SN-DISABLED ",
                TriggerMode = "External",
                HardwareTriggerSource = "Line2",
                TargetFrameRateFps = 45,
                IsEnabled = false
            },
            new()
            {
                Id = "cam-enabled",
                DisplayName = "",
                SerialNumber = "SN-ENABLED",
                IsEnabled = true,
                TriggerMode = "Software",
                SoftwareTriggerSource = "SerialPhotoelectric",
                SerialPhotoelectricPortName = " COM7 ",
                SerialPhotoelectricBaudRate = 115200,
                SerialPhotoelectricDebounceMs = 120,
                SerialPhotoelectricTimeoutMs = 45000,
                IgnoreSerialPhotoelectricTriggerWhileBusy = false
            }
        };

        var snapshot = await sut.SaveCameraBindingsAsync(inputBindings, "missing-active", 0);
        var savedConfig = configService.GetCurrent();

        savedConfig.ActiveCameraId.Should().Be("cam-enabled");
        savedConfig.Cameras.Should().HaveCount(2);
        savedConfig.Cameras[0].Id.Should().Be("cam-disabled");
        savedConfig.Cameras[0].DisplayName.Should().Be("Disabled Camera");
        savedConfig.Cameras[0].SerialNumber.Should().Be("SN-DISABLED");
        savedConfig.Cameras[0].TriggerMode.Should().Be("External");
        savedConfig.Cameras[0].HardwareTriggerSource.Should().Be("Line2");
        savedConfig.Cameras[0].TargetFrameRateFps.Should().Be(45);
        savedConfig.Cameras[1].DisplayName.Should().Be("Camera");
        savedConfig.Cameras[1].TriggerMode.Should().Be("Software");
        savedConfig.Cameras[1].SoftwareTriggerSource.Should().Be("SerialPhotoelectric");
        savedConfig.Cameras[1].SerialPhotoelectricPortName.Should().Be("COM7");
        savedConfig.Cameras[1].SerialPhotoelectricBaudRate.Should().Be(115200);
        savedConfig.Cameras[1].SerialPhotoelectricDebounceMs.Should().Be(120);
        savedConfig.Cameras[1].SerialPhotoelectricTimeoutMs.Should().Be(45000);
        savedConfig.Cameras[1].IgnoreSerialPhotoelectricTriggerWhileBusy.Should().BeFalse();
        savedConfig.Communication.S7.IpAddress.Should().Be("192.168.10.5");
        snapshot.ActiveCameraId.Should().Be("cam-enabled");
        snapshot.Cameras.Should().HaveCount(2);

        await cameraManager.Received(1).ApplyBindingsAsync(
            Arg.Is<List<CameraBindingConfig>>(bindings =>
                bindings.Count == 2 &&
                bindings[0].Id == "cam-disabled" &&
                bindings[1].Id == "cam-enabled"),
            "cam-enabled");
    }

    [Fact]
    public async Task SaveCameraBindingsAsync_WhenCameraIdsDuplicate_ShouldReject()
    {
        var configService = new InMemoryAppConfigAuthority();

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);
        var inputBindings = new List<CameraBindingConfig>
        {
            new() { Id = "cam-main", SerialNumber = "SN-1" },
            new() { Id = " CAM-MAIN ", SerialNumber = "SN-2" }
        };

        var act = async () => await sut.SaveCameraBindingsAsync(inputBindings, "cam-main", 0);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*相机绑定 ID 重复*");
        configService.MutationCount.Should().Be(0);
        await cameraManager.DidNotReceive().ApplyBindingsAsync(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SaveCameraBindingsAsync_WhenFrameDrivenCameraFrameRateIsOutOfRange_ShouldReject()
    {
        var configService = new InMemoryAppConfigAuthority();

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);
        var inputBindings = new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-external",
                SerialNumber = "SN-EXTERNAL",
                TriggerMode = "External",
                TargetFrameRateFps = 500
            }
        };

        var act = async () => await sut.SaveCameraBindingsAsync(inputBindings, "cam-external", 0);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*采集帧率必须在 1 - 120 fps 范围内*");
        configService.MutationCount.Should().Be(0);
        await cameraManager.DidNotReceive().ApplyBindingsAsync(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SaveCameraBindingsAsync_WhenSerialPhotoelectricPortIsMissing_ShouldReject()
    {
        var configService = new InMemoryAppConfigAuthority();

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);
        var inputBindings = new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-serial-trigger",
                DisplayName = "Serial Trigger Camera",
                SerialNumber = "SN-SERIAL",
                TriggerMode = "Software",
                SoftwareTriggerSource = "SerialPhotoelectric",
                SerialPhotoelectricPortName = ""
            }
        };

        var act = async () => await sut.SaveCameraBindingsAsync(inputBindings, "cam-serial-trigger", 0);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*串口号必须类似 COM3*");
        configService.MutationCount.Should().Be(0);
        await cameraManager.DidNotReceive().ApplyBindingsAsync(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SavePlcSettingsAsync_ShouldPersistCommunicationAndKeepCameraBindings()
    {
        var existingCamera = new CameraBindingConfig
        {
            Id = "cam-main",
            DisplayName = "Main",
            SerialNumber = "SN-MAIN"
        };
        var currentConfig = new AppConfig
        {
            Cameras = [existingCamera],
            ActiveCameraId = "cam-main",
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolS7,
                S7 = new S7CommunicationProfile
                {
                    IpAddress = "192.168.10.5",
                    Port = 102
                }
            }
        };
        var configService = new InMemoryAppConfigAuthority(currentConfig);

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);
        var communication = new CommunicationConfig
        {
            ActiveProtocol = CommunicationConfig.ProtocolMc,
            HeartbeatIntervalMs = 2500,
            Mc = new PlcCommunicationProfile
            {
                IpAddress = "10.20.30.40",
                Port = 5002,
                Mappings =
                [
                    new PlcAddressMapping
                    {
                        Name = "Trigger",
                        Address = "M100",
                        DataType = "Bool"
                    },
                    new PlcAddressMapping
                    {
                        Name = "Result",
                        Address = "D101",
                        DataType = "Int16",
                        CanWrite = true
                    }
                ]
            }
        };

        var snapshot = await sut.SavePlcSettingsAsync(communication, 0);
        var savedConfig = configService.GetCurrent();

        savedConfig.ActiveCameraId.Should().Be("cam-main");
        savedConfig.Cameras.Should().ContainSingle(camera => camera.Id == "cam-main");
        savedConfig.Communication.ActiveProtocol.Should().Be(CommunicationConfig.ProtocolMc);
        savedConfig.Communication.HeartbeatIntervalMs.Should().Be(2500);
        savedConfig.Communication.Mc.IpAddress.Should().Be("10.20.30.40");
        savedConfig.Communication.Mc.Port.Should().Be(5002);
        savedConfig.Communication.Mc.Mappings.Should().HaveCount(2);
        savedConfig.Communication.Mc.Mappings[1].CanWrite.Should().BeTrue();
        snapshot.Communication.Mc.IpAddress.Should().Be("10.20.30.40");
        snapshot.Cameras.Should().ContainSingle(camera => camera.Id == "cam-main");

        await cameraManager.DidNotReceive().ApplyBindingsAsync(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
        cameraManager.DidNotReceive().LoadBindings(Arg.Any<List<CameraBindingConfig>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task TestCameraAsync_WithConfiguredBinding_ShouldUseServerTargetAndSettings()
    {
        var configService = Substitute.For<IConfigurationService>();
        var cameraManager = Substitute.For<ICameraManager>();
        var authoritativeBinding = new CameraBindingConfig
        {
            Id = "cam-configured",
            SerialNumber = "SN-CONFIGURED",
            IsEnabled = true,
            ExposureTimeUs = 7200,
            GainDb = 2.25
        };
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { authoritativeBinding });
        var camera = Substitute.For<ICamera>();
        camera.IsConnected.Returns(true);
        camera.Name.Returns("Configured Camera");
        var cameraLease = new TrackingCameraLease(camera);
        cameraManager
            .AcquireByBindingLeaseAsync("cam-configured", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ICameraLease>(cameraLease));
        var sut = CreateService(configService, cameraManager);
        var clientCandidate = new CameraBindingConfig
        {
            Id = "cam-configured",
            SerialNumber = "SN-CONFIGURED",
            ExposureTimeUs = 999999,
            GainDb = 99
        };

        var result = await sut.TestCameraAsync(clientCandidate);

        result.Success.Should().BeTrue();
        await cameraManager.Received(1).AcquireByBindingLeaseAsync(
            "cam-configured",
            Arg.Any<CancellationToken>());
        cameraLease.DisposeCallCount.Should().Be(1);
        await camera.Received(1).SetExposureTimeAsync(7200);
        await camera.Received(1).SetGainAsync(2.25);
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().GetOrCreateCameraAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().OpenCameraAsync(Arg.Any<string>());
    }

    [Theory]
    [InlineData("missing-binding", "SN-MISSING")]
    [InlineData("SN-AUTHORIZED", "SN-AUTHORIZED")]
    [InlineData("disabled-binding", "SN-DISABLED")]
    [InlineData("unbound-camera", "")]
    [InlineData("authorized-binding", "SN-FORGED")]
    public async Task TestCameraAsync_WhenClientTargetIsNotAuthoritative_ShouldNotOpenCamera(
        string requestedBindingId,
        string requestedSerialNumber)
    {
        var configService = Substitute.For<IConfigurationService>();
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "authorized-binding",
                SerialNumber = "SN-AUTHORIZED",
                IsEnabled = true
            },
            new()
            {
                Id = "disabled-binding",
                SerialNumber = "SN-DISABLED",
                IsEnabled = false
            },
            new()
            {
                Id = "unbound-camera",
                SerialNumber = string.Empty,
                IsEnabled = true
            }
        });
        var sut = CreateService(configService, cameraManager);

        var result = await sut.TestCameraAsync(new CameraBindingConfig
        {
            Id = requestedBindingId,
            SerialNumber = requestedSerialNumber
        });

        result.Success.Should().BeFalse();
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().GetOrCreateCameraAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().OpenCameraAsync(Arg.Any<string>());
        cameraManager.DidNotReceive().GetCamera(Arg.Any<string>());
    }

    [Fact]
    public async Task TestPlcConnectionAsync_WhenIpAddressIsEmpty_ShouldReturnValidationFailure()
    {
        var configService = new InMemoryAppConfigAuthority(new AppConfig
        {
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolS7,
                S7 = new S7CommunicationProfile
                {
                    IpAddress = string.Empty,
                    Port = 102
                }
            }
        });
        var cameraManager = Substitute.For<ICameraManager>();
        var probeCalls = 0;
        var sut = CreateService(
            configService,
            cameraManager,
            (_, _, _) =>
            {
                probeCalls++;
                return Task.FromResult(true);
            });

        var result = await sut.TestPlcConnectionAsync(
            CommunicationConfig.ProtocolS7,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("PLC IP 地址不能为空");
        probeCalls.Should().Be(0);
    }

    [Fact]
    public async Task TestPlcConnectionAsync_ShouldUsePersistedProfileInsteadOfClientTarget()
    {
        var configService = new InMemoryAppConfigAuthority(new AppConfig
        {
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolS7,
                S7 = new S7CommunicationProfile
                {
                    IpAddress = "192.0.2.44",
                    Port = 1102,
                    CpuType = "S7-1500",
                    Rack = 2,
                    Slot = 3
                }
            }
        });
        var cameraManager = Substitute.For<ICameraManager>();
        string? dispatchedConnectionString = null;
        var sut = CreateService(
            configService,
            cameraManager,
            (connectionString, _, _) =>
            {
                dispatchedConnectionString = connectionString;
                return Task.FromResult(true);
            });

        var result = await sut.TestPlcConnectionAsync("S7", CancellationToken.None);

        result.Success.Should().BeTrue();
        dispatchedConnectionString.Should().Be("S7://192.0.2.44:1102?cpu=S7-1500&rack=2&slot=3");
    }

    [Theory]
    [InlineData("")]
    [InlineData("forged-profile")]
    [InlineData("SiemensS7")]
    public async Task TestPlcConnectionAsync_WhenProfileIdIsMissingOrForged_ShouldNotProbe(string profileId)
    {
        var configService = new InMemoryAppConfigAuthority(new AppConfig());
        var cameraManager = Substitute.For<ICameraManager>();
        var probeCalls = 0;
        var sut = CreateService(
            configService,
            cameraManager,
            (_, _, _) =>
            {
                probeCalls++;
                return Task.FromResult(true);
            });

        var result = await sut.TestPlcConnectionAsync(profileId, CancellationToken.None);

        result.Success.Should().BeFalse();
        probeCalls.Should().Be(0);
    }

    private static StationHardwareSettingsService CreateService(
        IConfigurationService configurationService,
        ICameraManager cameraManager,
        Func<string, ILogger, CancellationToken, Task<bool>>? plcConnectionProbe = null)
    {
        return plcConnectionProbe == null
            ? new StationHardwareSettingsService(
                configurationService,
                cameraManager,
                NullLogger<StationHardwareSettingsService>.Instance)
            : new StationHardwareSettingsService(
                configurationService,
                cameraManager,
                NullLogger<StationHardwareSettingsService>.Instance,
                plcConnectionProbe);
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
}
