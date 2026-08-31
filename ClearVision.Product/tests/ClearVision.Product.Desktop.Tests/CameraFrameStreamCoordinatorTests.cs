using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.Cameras;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public class CameraFrameStreamCoordinatorTests
{
    private const string PreviewOwner = "owner-a";

    [Theory]
    [InlineData("missing-binding")]
    [InlineData("SN-AUTHORIZED")]
    [InlineData("disabled-binding")]
    [InlineData("unbound-camera")]
    public async Task ExecutionSurfaces_WhenBindingAuthorityRejectsTarget_ShouldNotTouchCameraManager(
        string requestedId)
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "authorized-binding",
                SerialNumber = "SN-AUTHORIZED",
                IsEnabled = true,
                TriggerMode = "External"
            },
            new()
            {
                Id = "disabled-binding",
                SerialNumber = "SN-DISABLED",
                IsEnabled = false,
                TriggerMode = "External"
            },
            new()
            {
                Id = "unbound-camera",
                SerialNumber = string.Empty,
                IsEnabled = true,
                TriggerMode = "External"
            }
        });
        await using var sut = new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance);

        var acquire = async () => await sut.AcquireFrameAsync(requestedId);
        var stream = async () => await sut.AcquireStreamLeaseAsync(requestedId);
        var preview = async () => await sut.StartPreviewSessionAsync(requestedId, PreviewOwner);

        await acquire.Should().ThrowAsync<InvalidOperationException>();
        await stream.Should().ThrowAsync<InvalidOperationException>();
        await preview.Should().ThrowAsync<InvalidOperationException>();
        cameraManager.DidNotReceive().GetCamera(Arg.Any<string>());
        await cameraManager.DidNotReceive().GetOrCreateCameraAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcquireFrameAsync_WhenProducerHasCachedFrame_ShouldWaitForNewerFrame()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = Substitute.For<IIndustrialCamera>();
        camera.IsConnected.Returns(true);
        camera.IsAcquiring.Returns(true);
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
        ConfigureCameraLeases(cameraManager, binding.Id, camera);
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(binding.Id, PreviewOwner);

        var firstFrame = CreatePngBytes(new Scalar(0, 0, 255));
        var secondFrame = CreatePngBytes(new Scalar(0, 255, 0));
        await frameCallback!(firstFrame);

        var acquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(100);
        acquireTask.IsCompleted.Should().BeFalse();

        await frameCallback(secondFrame);
        var frame = await acquireTask;

        frame.ImageData.Should().Equal(secondFrame);

        await sut.StopPreviewSessionAsync(previewSession.SessionId, PreviewOwner);
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
        ConfigureCameraLeases(cameraManager, binding.Id, camera);
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(binding.Id, PreviewOwner);

        var waitTask = sut.WaitForPreviewFrameAsync(previewSession.SessionId, PreviewOwner);
        await Task.Delay(100);
        waitTask.IsCompleted.Should().BeFalse();

        await sut.StopPreviewSessionAsync(previewSession.SessionId, PreviewOwner);

        var act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PreviewProducer_ShouldHoldCameraLeaseUntilStopAndReleaseItExactlyOnce()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();
        var binding = new CameraBindingConfig
        {
            Id = "binding-lease-lifetime",
            SerialNumber = "SN-LEASE-LIFETIME",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        var leases = ConfigureCameraLeases(cameraManager, binding.Id, camera);
        var sut = new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance);

        var previewSession = await sut.StartPreviewSessionAsync(binding.Id, PreviewOwner);

        leases.Should().ContainSingle();
        leases[0].DisposeCallCount.Should().Be(0);

        await sut.StopPreviewSessionAsync(previewSession.SessionId, PreviewOwner);
        leases[0].DisposeCallCount.Should().Be(1);

        await sut.DisposeAsync();
        leases[0].DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReleaseStreamLeaseAsync_WhenSameLeaseIsRepeated_ShouldNotStopOtherActiveLease()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();
        var binding = new CameraBindingConfig
        {
            Id = "binding-concurrent-leases",
            SerialNumber = "SN-CONCURRENT-LEASES",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        var cameraLeases = ConfigureCameraLeases(cameraManager, binding.Id, camera);
        var sut = new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance);

        var first = await sut.AcquireStreamLeaseAsync(binding.Id);
        var second = await sut.AcquireStreamLeaseAsync(binding.Id);
        await Task.WhenAll(
            sut.ReleaseStreamLeaseAsync(first),
            sut.ReleaseStreamLeaseAsync(first));

        var usage = sut.SnapshotStreamUsage(binding.Id);
        usage.IsRunning.Should().BeTrue();
        usage.LeaseCount.Should().Be(1);
        cameraLeases.Should().ContainSingle();
        cameraLeases[0].DisposeCallCount.Should().Be(0);

        await sut.ReleaseStreamLeaseAsync(second);
        await sut.ReleaseStreamLeaseAsync(second);
        cameraLeases[0].DisposeCallCount.Should().Be(1);

        await sut.DisposeAsync();
        cameraLeases[0].DisposeCallCount.Should().Be(1);
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
        ConfigureCameraLeases(cameraManager, binding.Id, camera);
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(binding.Id, PreviewOwner);

        await camera.Received(1).SetTriggerModeAsync(CameraTriggerMode.External, "Line2");
        await sut.StopPreviewSessionAsync(previewSession.SessionId, PreviewOwner);
    }

    [Fact]
    public async Task PreviewSession_WithRepeatedHeartbeats_ShouldRemainActivePastOriginalTtl()
    {
        var startUtc = new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(startUtc);
        await using var sut = CreatePreviewTtlCoordinator(timeProvider, out var camera);

        var session = await sut.StartPreviewSessionAsync("binding-preview-ttl", PreviewOwner);

        session.ExpiresAtUtc.Should().Be(startUtc.AddSeconds(30));
        session.HeartbeatIntervalMs.Should().Be(10_000);

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        var firstHeartbeat = await sut.HeartbeatPreviewSessionAsync(session.SessionId, PreviewOwner);
        firstHeartbeat.Should().NotBeNull();
        firstHeartbeat!.ExpiresAtUtc.Should().Be(startUtc.AddSeconds(50));

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        var secondHeartbeat = await sut.HeartbeatPreviewSessionAsync(session.SessionId, PreviewOwner);
        secondHeartbeat.Should().NotBeNull();
        secondHeartbeat!.ExpiresAtUtc.Should().Be(startUtc.AddSeconds(70));

        timeProvider.Advance(TimeSpan.FromSeconds(20));

        var usage = sut.SnapshotStreamUsage("binding-preview-ttl");
        usage.IsRunning.Should().BeTrue();
        usage.PreviewSessionCount.Should().Be(1);
        await camera.DidNotReceive().StopContinuousAcquisitionAsync();

        (await sut.StopPreviewSessionAsync(session.SessionId, PreviewOwner)).Should().BeTrue();
        await camera.Received(1).StopContinuousAcquisitionAsync();
    }

    [Fact]
    public async Task AbandonedPreviewSession_WhenTtlElapses_ShouldCancelItsWaiterWithoutStoppingActiveSession()
    {
        var startUtc = new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(startUtc);
        await using var sut = CreatePreviewTtlCoordinator(timeProvider, out var camera);

        var abandonedSession = await sut.StartPreviewSessionAsync("binding-preview-ttl", PreviewOwner);
        var activeSession = await sut.StartPreviewSessionAsync("binding-preview-ttl", "owner-b");
        var abandonedWaiter = sut.WaitForPreviewFrameAsync(abandonedSession.SessionId, PreviewOwner);

        sut.SnapshotStreamUsage("binding-preview-ttl").PendingFrameWaiters.Should().Be(1);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        (await sut.HeartbeatPreviewSessionAsync(activeSession.SessionId, "owner-b"))
            .Should().NotBeNull();

        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var waitAct = async () => await abandonedWaiter;
        await waitAct.Should().ThrowAsync<OperationCanceledException>();
        var usage = sut.SnapshotStreamUsage("binding-preview-ttl");
        usage.IsRunning.Should().BeTrue();
        usage.PreviewSessionCount.Should().Be(1);
        usage.PendingFrameWaiters.Should().Be(0);
        (await sut.HeartbeatPreviewSessionAsync(abandonedSession.SessionId, PreviewOwner))
            .Should().BeNull();
        await camera.DidNotReceive().StopContinuousAcquisitionAsync();

        (await sut.StopPreviewSessionAsync(activeSession.SessionId, "owner-b")).Should().BeTrue();
        await camera.Received(1).StopContinuousAcquisitionAsync();
    }

    [Fact]
    public async Task PreviewSession_WithWrongOwner_ShouldRejectAllOperationsWithoutMutatingSession()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero));
        await using var sut = CreatePreviewTtlCoordinator(timeProvider, out var camera);
        var session = await sut.StartPreviewSessionAsync("binding-preview-ttl", PreviewOwner);
        var before = sut.SnapshotStreamUsage("binding-preview-ttl");

        var frameAct = async () =>
            await sut.WaitForPreviewFrameAsync(session.SessionId, "owner-b");

        await frameAct.Should().ThrowAsync<KeyNotFoundException>();
        (await sut.HeartbeatPreviewSessionAsync(session.SessionId, "owner-b")).Should().BeNull();
        (await sut.StopPreviewSessionAsync(session.SessionId, "owner-b")).Should().BeFalse();

        sut.SnapshotStreamUsage("binding-preview-ttl").Should().Be(before);
        await camera.DidNotReceive().StopContinuousAcquisitionAsync();
        (await sut.HeartbeatPreviewSessionAsync(session.SessionId, PreviewOwner)).Should().NotBeNull();
        (await sut.StopPreviewSessionAsync(session.SessionId, PreviewOwner)).Should().BeTrue();
    }

    [Fact]
    public async Task PreviewSession_ExpiryRacingOwnerStop_ShouldReleaseProducerExactlyOnce()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero));
        await using var sut = CreatePreviewTtlCoordinator(timeProvider, out var camera);
        var stopBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        camera.StopContinuousAcquisitionAsync().Returns(_ =>
        {
            stopInvoked.TrySetResult();
            return stopBarrier.Task;
        });
        try
        {
            var session = await sut.StartPreviewSessionAsync("binding-preview-ttl", PreviewOwner);

            timeProvider.Advance(TimeSpan.FromSeconds(30));

            (await sut.StopPreviewSessionAsync(session.SessionId, PreviewOwner)).Should().BeFalse();
            await stopInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1));
            _ = camera.Received(1).StopContinuousAcquisitionAsync();
            stopBarrier.Task.IsCompleted.Should().BeFalse();
            stopBarrier.TrySetResult();
            await WaitUntilAsync(() => !sut.SnapshotStreamUsage("binding-preview-ttl").IsRunning);

            _ = camera.Received(1).StopContinuousAcquisitionAsync();
            sut.SnapshotStreamUsage("binding-preview-ttl").PreviewSessionCount.Should().Be(0);
        }
        finally
        {
            stopBarrier.TrySetResult();
        }
    }

    [Fact]
    public async Task PreviewSession_ExpiryWhileProducerStopIsBlocked_ConcurrentStartShouldUseCurrentProducer()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero));
        await using var sut = CreatePreviewTtlCoordinator(timeProvider, out var camera);
        var stopBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        camera.StopContinuousAcquisitionAsync().Returns(stopBarrier.Task);
        try
        {
            await sut.StartPreviewSessionAsync("binding-preview-ttl", PreviewOwner);

            timeProvider.Advance(TimeSpan.FromSeconds(30));

            _ = camera.Received(1).StopContinuousAcquisitionAsync();
            stopBarrier.Task.IsCompleted.Should().BeFalse();
            var replacementTask = sut.StartPreviewSessionAsync("binding-preview-ttl", "owner-b");
            replacementTask.IsCompleted.Should().BeFalse();

            stopBarrier.TrySetResult();
            var replacement = await replacementTask.WaitAsync(TimeSpan.FromSeconds(1));

            var usage = sut.SnapshotStreamUsage("binding-preview-ttl");
            usage.IsRunning.Should().BeTrue();
            usage.PreviewSessionCount.Should().Be(1);
            await camera.Received(2).StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>());
            await camera.Received(1).StopContinuousAcquisitionAsync();

            (await sut.StopPreviewSessionAsync(replacement.SessionId, "owner-b")).Should().BeTrue();
            await camera.Received(2).StopContinuousAcquisitionAsync();
            sut.SnapshotStreamUsage("binding-preview-ttl").IsRunning.Should().BeFalse();
        }
        finally
        {
            stopBarrier.TrySetResult();
        }
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
        ConfigureCameraLeases(cameraManager, originalBinding.Id, camera);
        cameraManager.GetCamera(originalBinding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(cameraManager, NullLogger<CameraFrameStreamCoordinator>.Instance);
        var previewSession = await sut.StartPreviewSessionAsync(originalBinding.Id, PreviewOwner);
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { changedBinding });

        var act = async () => await sut.AcquireFrameAsync(originalBinding.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different configuration*");
        await sut.StopPreviewSessionAsync(previewSession.SessionId, PreviewOwner);
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
        var leases = ConfigureCameraLeases(cameraManager, binding.Id, camera);
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
        leases.Should().ContainSingle();
        leases[0].DisposeCallCount.Should().Be(1);

        isAcquiring = true;
        var secondAcquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(50);

        var frameBytes = CreatePngBytes(new Scalar(64, 128, 192));
        await frameCallback!(frameBytes);
        var frame = await secondAcquireTask.WaitAsync(TimeSpan.FromSeconds(1));

        frame.ImageData.Should().Equal(frameBytes);
        await camera.Received(2).StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>());
        await sut.ReleaseIdleStreamAsync(binding.Id);
        leases.Should().HaveCount(2);
        leases[1].DisposeCallCount.Should().Be(1);
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
        var leases = ConfigureCameraLeases(cameraManager, binding.Id, camera);
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
        leases.Should().ContainSingle();
        leases[0].DisposeCallCount.Should().Be(1);

        var secondAcquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(50);

        var frameBytes = CreatePngBytes(new Scalar(8, 16, 32));
        await frameCallback!(frameBytes);
        var frame = await secondAcquireTask.WaitAsync(TimeSpan.FromSeconds(1));

        frame.ImageData.Should().Equal(frameBytes);
        await camera.Received(2).StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>());
        await sut.ReleaseIdleStreamAsync(binding.Id);
        leases.Should().HaveCount(2);
        leases[1].DisposeCallCount.Should().Be(1);
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
        ConfigureCameraLeases(cameraManager, binding.Id, camera);
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

    [Fact]
    public async Task AcquireFrameAsync_AfterDirectAcquire_ShouldStopProducerWhenIdleTimeoutElapses()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var camera = CreateIndustrialCamera();
        Func<byte[], Task>? frameCallback = null;

        camera.When(x => x.StartContinuousAcquisitionAsync(Arg.Any<Func<byte[], Task>>()))
            .Do(callInfo => frameCallback = callInfo.Arg<Func<byte[], Task>>());

        var binding = new CameraBindingConfig
        {
            Id = "binding-idle-timeout",
            SerialNumber = "SN-IDLE-TIMEOUT",
            TriggerMode = "Continuous"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        ConfigureCameraLeases(cameraManager, binding.Id, camera);
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        await using var sut = new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance,
            NoOpTriggerInputService.Instance,
            NoOpSerialPhotoelectricTriggerInputService.Instance,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(20));
        var acquireTask = sut.AcquireFrameAsync(binding.Id);
        await Task.Delay(50);

        var frameBytes = CreatePngBytes(new Scalar(96, 32, 64));
        await frameCallback!(frameBytes);
        var frame = await acquireTask.WaitAsync(TimeSpan.FromSeconds(1));

        frame.ImageData.Should().Equal(frameBytes);
        for (var attempt = 0; attempt < 20 && sut.SnapshotStreamUsage(binding.Id).IsRunning; attempt++)
        {
            await Task.Delay(20);
        }

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

    private static List<TrackingCameraLease> ConfigureCameraLeases(
        ICameraManager cameraManager,
        string bindingId,
        ICamera camera)
    {
        var leases = new List<TrackingCameraLease>();
        cameraManager.AcquireByBindingLeaseAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var lease = new TrackingCameraLease(camera);
                leases.Add(lease);
                return Task.FromResult<ICameraLease>(lease);
            });
        return leases;
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

    private static CameraFrameStreamCoordinator CreatePreviewTtlCoordinator(
        ManualTimeProvider timeProvider,
        out IIndustrialCamera camera)
    {
        var cameraManager = Substitute.For<ICameraManager>();
        camera = CreateIndustrialCamera();
        var binding = new CameraBindingConfig
        {
            Id = "binding-preview-ttl",
            SerialNumber = "SN-PREVIEW-TTL",
            TriggerMode = "External",
            HardwareTriggerSource = "Line1"
        };

        cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { binding });
        ConfigureCameraLeases(cameraManager, binding.Id, camera);
        cameraManager.GetCamera(binding.SerialNumber).Returns(camera);

        return new CameraFrameStreamCoordinator(
            cameraManager,
            NullLogger<CameraFrameStreamCoordinator>.Instance,
            NoOpTriggerInputService.Instance,
            NoOpSerialPhotoelectricTriggerInputService.Instance,
            TimeSpan.FromHours(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10),
            timeProvider);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        condition().Should().BeTrue();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
            {
                _timers.Add(timer);
                timer.ChangeCore(dueTime, period, _utcNow);
            }

            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_gate)
            {
                _utcNow = _utcNow.Add(elapsed);
                foreach (var timer in _timers)
                {
                    if (timer.TryTakeDueCallback(_utcNow, out var callback))
                    {
                        callbacks.Add(callback);
                    }
                }
            }

            foreach (var callback in callbacks)
            {
                callback.Callback(callback.State);
            }
        }

        private bool Change(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                return timer.ChangeCore(dueTime, period, _utcNow);
            }
        }

        private void Dispose(ManualTimer timer)
        {
            lock (_gate)
            {
                timer.DisposeCore();
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private DateTimeOffset? _dueAtUtc;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private bool _disposed;

            public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                _owner.Change(this, dueTime, period);

            public void Dispose() => _owner.Dispose(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool ChangeCore(TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
            {
                if (_disposed)
                {
                    return false;
                }

                ValidateTimeout(dueTime, nameof(dueTime));
                ValidateTimeout(period, nameof(period));
                _period = period;
                _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : now.Add(dueTime);
                return true;
            }

            public bool TryTakeDueCallback(
                DateTimeOffset now,
                out (TimerCallback Callback, object? State) callback)
            {
                callback = default;
                if (_disposed || !_dueAtUtc.HasValue || _dueAtUtc.Value > now)
                {
                    return false;
                }

                callback = (_callback, _state);
                _dueAtUtc = _period == Timeout.InfiniteTimeSpan
                    ? null
                    : now.Add(_period);
                return true;
            }

            public void DisposeCore()
            {
                _disposed = true;
                _dueAtUtc = null;
            }

            private static void ValidateTimeout(TimeSpan value, string parameterName)
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(parameterName);
                }
            }
        }
    }
}
