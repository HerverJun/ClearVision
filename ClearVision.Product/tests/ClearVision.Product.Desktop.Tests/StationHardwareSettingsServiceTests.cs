using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Station;
using FluentAssertions;
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
    public async Task TestPlcConnectionAsync_WhenIpAddressIsEmpty_ShouldReturnValidationFailure()
    {
        var configService = Substitute.For<IConfigurationService>();
        var cameraManager = Substitute.For<ICameraManager>();
        var sut = CreateService(configService, cameraManager);
        var communication = new CommunicationConfig
        {
            ActiveProtocol = CommunicationConfig.ProtocolS7,
            S7 = new S7CommunicationProfile
            {
                IpAddress = "",
                Port = 102,
                Rack = 0,
                Slot = 1
            }
        };

        var result = await sut.TestPlcConnectionAsync(communication, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("PLC IP 地址不能为空");
    }

    private static StationHardwareSettingsService CreateService(
        IConfigurationService configurationService,
        ICameraManager cameraManager)
    {
        return new StationHardwareSettingsService(
            configurationService,
            cameraManager,
            NullLogger<StationHardwareSettingsService>.Instance);
    }
}
