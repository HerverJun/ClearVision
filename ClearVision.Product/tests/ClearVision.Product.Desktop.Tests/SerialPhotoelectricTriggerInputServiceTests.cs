using System.Reflection;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Desktop.Triggers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Desktop.Tests;

[Collection(PhotoelectricTriggerTestCollections.TriggerInput)]
[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public class SerialPhotoelectricTriggerInputServiceTests
{
    [Fact]
    public async Task WaitForSerialPhotoelectricAsync_ShouldConsumeSerialComPendingSignal()
    {
        using var sut = new SerialPhotoelectricTriggerInputService(
            NullLogger<SerialPhotoelectricTriggerInputService>.Instance);

        PublishBlockSignal(sut, "COM3");

        var triggerEvent = await sut.WaitForSerialPhotoelectricAsync(
            new SerialPhotoelectricTriggerOptions(
                "binding-1",
                "Camera 1",
                "COM3",
                9600,
                0,
                1000,
                IgnoreWhileBusy: false));

        triggerEvent.DeviceId.Should().Be("COM3");
        triggerEvent.Source.Should().Be("SerialPhotoelectric");
    }

    [Fact]
    public async Task WaitForSerialPhotoelectricAsync_ShouldNotMatchDifferentComPort()
    {
        using var sut = new SerialPhotoelectricTriggerInputService(
            NullLogger<SerialPhotoelectricTriggerInputService>.Instance);

        PublishBlockSignal(sut, "COM4");

        var waitTask = sut.WaitForSerialPhotoelectricAsync(
            new SerialPhotoelectricTriggerOptions(
                "binding-1",
                "Camera 1",
                "COM3",
                9600,
                0,
                5000,
                IgnoreWhileBusy: false));

        await Task.Delay(50);
        waitTask.IsCompleted.Should().BeFalse();

        PublishBlockSignal(sut, "COM3");
        var triggerEvent = await waitTask;

        triggerEvent.DeviceId.Should().Be("COM3");
    }

    [Fact]
    public async Task WaitForSerialPhotoelectricAsync_ShouldIgnorePendingSignalWhenBusyProtectionEnabled()
    {
        using var sut = new SerialPhotoelectricTriggerInputService(
            NullLogger<SerialPhotoelectricTriggerInputService>.Instance);

        PublishBlockSignal(sut, "COM3");

        var waitTask = sut.WaitForSerialPhotoelectricAsync(
            new SerialPhotoelectricTriggerOptions(
                "binding-1",
                "Camera 1",
                "COM3",
                9600,
                0,
                5000,
                IgnoreWhileBusy: true));

        await Task.Delay(50);
        waitTask.IsCompleted.Should().BeFalse();

        PublishBlockSignal(sut, "COM3");
        var triggerEvent = await waitTask;

        triggerEvent.DeviceId.Should().Be("COM3");
        triggerEvent.Source.Should().Be("SerialPhotoelectric");
    }

    [Fact]
    public void SerialPhotoelectricPortListener_ShouldPublishOnlyOnClearToBlockedTransition()
    {
        var publishedPorts = new List<string>();
        var listener = CreatePortListener(portName => publishedPorts.Add(portName));

        ProcessBytes(listener, new byte[] { 0x01, 0x11, 0x01, 0x11 });
        publishedPorts.Should().Equal("COM3");

        ProcessBytes(listener, new byte[] { 0x01, 0x22, 0x01, 0x11 });
        publishedPorts.Should().Equal("COM3", "COM3");
    }

    private static void PublishBlockSignal(
        SerialPhotoelectricTriggerInputService service,
        string portName)
    {
        var method = typeof(SerialPhotoelectricTriggerInputService).GetMethod(
            "PublishBlockSignal",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(service, new object[] { portName });
    }

    private static object CreatePortListener(Action<string> publish)
    {
        var serviceType = typeof(SerialPhotoelectricTriggerInputService);
        var optionsType = serviceType.GetNestedType("SerialPhotoelectricConnectionOptions", BindingFlags.NonPublic);
        var listenerType = serviceType.GetNestedType("SerialPhotoelectricPortListener", BindingFlags.NonPublic);

        optionsType.Should().NotBeNull();
        listenerType.Should().NotBeNull();

        var options = Activator.CreateInstance(
            optionsType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { "COM3", 9600 },
            culture: null);
        options.Should().NotBeNull();

        var listener = Activator.CreateInstance(
            listenerType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { options!, publish, NullLogger.Instance },
            culture: null);
        listener.Should().NotBeNull();
        return listener!;
    }

    private static void ProcessBytes(object listener, byte[] bytes)
    {
        var method = listener.GetType().GetMethod(
            "ProcessBytes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(listener, new object[] { bytes, bytes.Length });
    }
}
