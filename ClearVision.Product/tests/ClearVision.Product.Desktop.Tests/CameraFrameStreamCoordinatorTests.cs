using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.Cameras;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Tests;

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

    [Fact]
    public async Task AcquireFrameAsync_WhenCameraAcquisitionStops_ShouldFaultProducerAndAllowNextAcquireToRestart()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();
        var isAcquiring = false;
        Func<byte[], Task>? frameCallback = null;

        camera.IsAcquiring.Returns(_ => isAcquiring);
        camera.When(x => x.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()))
            .Do(callInfo => frameCallback = callInfo.Arg<Func<byte[], Task>>());

        var binding = new CameraBindingConfig
        {
            Id = "binding-restart",
            SerialNumber = "SN-RESTART",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        cameraManager.GetOrCreateByBindingAsync(binding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance,
            NoOpTriggerInputService.Instance,
            NoOpSerialPhotoelectricTriggerInputService.Instance,
            TimeSpan.FromMilliseconds(20));

        var firstAcquire = async () => await sut.AcquireFrameAsync(binding.Id);

        await firstAcquire.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*acquisition loop is no longer running*");
        sut.SnapshotStreamUsage(binding.Id).IsRunning.Should().BeFalse();

        isAcquiring = true;
        var secondAcquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(50);

        var frameBytes = CreatePngBytes(new Scalar(64, 128, 192));
        await frameCallback!(frameBytes);
        var frame = await secondAcquireTask.WaitAsync(TimeSpan.FromSeconds(1));

        frame.ImageData.Should().Equal(frameBytes);
        await camera.Received(2).StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>());
    }

    [Fact]
    public async Task AcquireFrameAsync_WhenStartContinuousAcquisitionThrows_ShouldResetProducerStateAndAllowRetry()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();
        var attempt = 0;
        Func<byte[], Task>? frameCallback = null;

        camera.When(x => x.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()))
            .Do(callInfo =>
            {
                attempt++;
                frameCallback = callInfo.Arg<Func<byte[], Task>>();
                if (attempt == 1)
                {
                    throw new InvalidOperationException("synthetic startup failure");
                }
            });

        var binding = new CameraBindingConfig
        {
            Id = "binding-start-failure",
            SerialNumber = "SN-START-FAIL",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        cameraManager.GetOrCreateByBindingAsync(binding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance,
            NoOpTriggerInputService.Instance,
            NoOpSerialPhotoelectricTriggerInputService.Instance,
            TimeSpan.FromMilliseconds(20));

        var firstAcquire = async () => await sut.AcquireFrameAsync(binding.Id);

        await firstAcquire.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*synthetic startup failure*");
        sut.SnapshotStreamUsage(binding.Id).IsRunning.Should().BeFalse();

        var secondAcquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(50);

        var frameBytes = CreatePngBytes(new Scalar(8, 16, 32));
        await frameCallback!(frameBytes);
        var frame = await secondAcquireTask.WaitAsync(TimeSpan.FromSeconds(1));

        frame.ImageData.Should().Equal(frameBytes);
        await camera.Received(2).StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>());
    }

    [Fact]
    public async Task ReleaseIdleStreamAsync_AfterDirectAcquire_ShouldStopProducerImmediately()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();
        Func<byte[], Task>? frameCallback = null;

        camera.When(x => x.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()))
            .Do(callInfo => frameCallback = callInfo.Arg<Func<byte[], Task>>());

        var binding = new CameraBindingConfig
        {
            Id = "binding-idle",
            SerialNumber = "SN-IDLE",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        cameraManager.GetOrCreateByBindingAsync(binding.Id).Returns(Task.FromResult<ICamera>(camera));
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var acquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(50);

        var frameBytes = CreatePngBytes(new Scalar(32, 64, 96));
        await frameCallback!(frameBytes);
        var frame = await acquireTask.WaitAsync(TimeSpan.FromSeconds(1));

        frame.ImageData.Should().Equal(frameBytes);
        sut.SnapshotStreamUsage(binding.Id).IsRunning.Should().BeTrue();

        await sut.ReleaseIdleStreamAsync(binding.Id);

        sut.SnapshotStreamUsage(binding.Id).IsRunning.Should().BeFalse();
        await camera.Received(1).StopContinuousAcquisitionAsync();
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
        camera.IsAcquiring.Returns(true);
        camera.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()).Returns(Task.CompletedTask);
        camera.StopContinuousAcquisitionAsync().Returns(Task.CompletedTask);
        return camera;
    }
}
