using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Acme.Product.Infrastructure.Cameras;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace Acme.Product.Desktop.Tests;

public class CameraFrameStreamCoordinatorTests
{
    [Fact]
    public async Task AcquireFrameAsync_WhenProducerHasCachedFrame_ShouldWaitForNewerFrame()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = Substitute.For<IIndustrialCamera>();
        camera.IsConnected.Returns(true);
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetPixelFormatAsync(Arg.Any<CameraPixelFormat>()).Returns(Task.CompletedTask);
        camera.SetTriggerModeAsync(Arg.Any<CameraTriggerMode>(), Arg.Any<string?>()).Returns(Task.CompletedTask);
        camera.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()).Returns(Task.CompletedTask);
        camera.StopContinuousAcquisitionAsync().Returns(Task.CompletedTask);

        Func<byte[], Task>? frameCallback = null;
        camera.When(x => x.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()))
            .Do(callInfo => frameCallback = callInfo.Arg<Func<byte[], Task>>());

        var binding = new CameraBindingConfig
        {
            Id = "binding-1",
            SerialNumber = "SN-001",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        cameraManager.GetOrCreateByBindingAsync(binding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(binding.Id);

        var firstFrame = CreatePngBytes(new Scalar(0, 0, 255));
        var secondFrame = CreatePngBytes(new Scalar(0, 255, 0));
        await frameCallback!(firstFrame);

        var acquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(100);
        acquireTask.IsCompleted.Should().BeFalse();

        await frameCallback(secondFrame);
        var frame = await acquireTask;

        frame.ImageData.Should().Equal(secondFrame);

        await sut.StopPreviewSessionAsync(previewSession.SessionId);
    }

    [Fact]
    public async Task StopPreviewSessionAsync_WithPendingFrameWaiter_ShouldWakeWaiter()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();

        var binding = new CameraBindingConfig
        {
            Id = "binding-stop",
            SerialNumber = "SN-STOP",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        cameraManager.GetOrCreateByBindingAsync(binding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(binding.Id);

        var waitTask = sut.WaitForPreviewFrameAsync(previewSession.SessionId);
        await Task.Delay(100);
        waitTask.IsCompleted.Should().BeFalse();

        await sut.StopPreviewSessionAsync(previewSession.SessionId);

        var act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StartPreviewSessionAsync_WithExternalTrigger_ShouldApplyHardwareTriggerSource()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();

        var binding = new CameraBindingConfig
        {
            Id = "binding-external",
            SerialNumber = "SN-EXT",
            TriggerMode = "External",
            HardwareTriggerSource = "Line2"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        cameraManager.GetOrCreateByBindingAsync(binding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(binding.Id);

        await camera.Received(1).SetTriggerModeAsync(CameraTriggerMode.External, "Line2");
        await sut.StopPreviewSessionAsync(previewSession.SessionId);
    }

    [Fact]
    public async Task AcquireFrameAsync_WhenActiveProducerConfigurationChanges_ShouldThrow()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();

        var originalBinding = new CameraBindingConfig
        {
            Id = "binding-active",
            SerialNumber = "SN-ACTIVE",
            TriggerMode = "Continuous",
            ExposureTimeUs = 5000
        };
        var changedBinding = new CameraBindingConfig
        {
            Id = "binding-active",
            SerialNumber = "SN-ACTIVE",
            TriggerMode = "Continuous",
            ExposureTimeUs = 8000
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { originalBinding });
        cameraManager.GetOrCreateByBindingAsync(originalBinding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(originalBinding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(originalBinding.Id);
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { changedBinding });

        var act = async () => await sut.AcquireFrameAsync(originalBinding.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different configuration*");
        await sut.StopPreviewSessionAsync(previewSession.SessionId);
    }

    private static byte[] CreatePngBytes(Scalar color)
    {
        using var mat = new Mat(2, 2, MatType.CV_8UC3, color);
        return mat.ToBytes(".png");
    }

    private static IIndustrialCamera CreateIndustrialCamera()
    {
        var camera = Substitute.For<IIndustrialCamera>();
        camera.IsConnected.Returns(true);
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetPixelFormatAsync(Arg.Any<CameraPixelFormat>()).Returns(Task.CompletedTask);
        camera.SetTriggerModeAsync(Arg.Any<CameraTriggerMode>(), Arg.Any<string?>()).Returns(Task.CompletedTask);
        camera.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()).Returns(Task.CompletedTask);
        camera.StopContinuousAcquisitionAsync().Returns(Task.CompletedTask);
        return camera;
    }
}
