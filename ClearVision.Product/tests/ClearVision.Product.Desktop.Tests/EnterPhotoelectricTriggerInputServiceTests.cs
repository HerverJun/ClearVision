using System.Reflection;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Desktop.Triggers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public class EnterPhotoelectricTriggerInputServiceTests
{
    [Fact]
    public async Task WaitForEnterPhotoelectricAsync_ShouldConsumePendingSignalAfterPreviewCutoff_WhenBusySignalsAreIgnored()
    {
        using var sut = new EnterPhotoelectricTriggerInputService(
            NullLogger<EnterPhotoelectricTriggerInputService>.Instance);
        var acceptAfterUtc = DateTime.UtcNow.AddSeconds(-1);

        PublishEnterSignal(sut, "device-1");

        var triggerEvent = await sut.WaitForEnterPhotoelectricAsync(
            new EnterPhotoelectricTriggerOptions(
                "binding-1",
                "Camera 1",
                "device-1",
                0,
                1000,
                IgnoreWhileBusy: true)
            {
                AcceptPendingSignalsAfterUtc = acceptAfterUtc
            });

        triggerEvent.DeviceId.Should().Be("device-1");
        triggerEvent.CameraBindingId.Should().Be("binding-1");
    }

    [Fact]
    public async Task WaitForEnterPhotoelectricAsync_ShouldNotConsumePendingSignalBeforePreviewCutoff()
    {
        using var sut = new EnterPhotoelectricTriggerInputService(
            NullLogger<EnterPhotoelectricTriggerInputService>.Instance);

        PublishEnterSignal(sut, "device-1");
        var acceptAfterUtc = DateTime.UtcNow.AddMilliseconds(250);
        var waitTask = sut.WaitForEnterPhotoelectricAsync(
            new EnterPhotoelectricTriggerOptions(
                "binding-1",
                "Camera 1",
                "device-1",
                0,
                5000,
                IgnoreWhileBusy: true)
            {
                AcceptPendingSignalsAfterUtc = acceptAfterUtc
            });

        await Task.Delay(50);
        waitTask.IsCompleted.Should().BeFalse();

        var remainingCutoffDelay = acceptAfterUtc - DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
        if (remainingCutoffDelay > TimeSpan.Zero)
        {
            await Task.Delay(remainingCutoffDelay);
        }

        PublishEnterSignal(sut, "device-1");
        var triggerEvent = await waitTask;

        triggerEvent.TimestampUtc.Should().BeOnOrAfter(acceptAfterUtc);
    }

    private static void PublishEnterSignal(
        EnterPhotoelectricTriggerInputService service,
        string deviceId)
    {
        var method = typeof(EnterPhotoelectricTriggerInputService).GetMethod(
            "PublishEnterSignal",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(service, new object[] { deviceId });
    }
}
