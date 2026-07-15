using System.Reflection;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Desktop.Triggers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Desktop.Tests;

public class SerialPhotoelectricTriggerInputServiceTests
{
    private static readonly TimeSpan TestCompletionTimeout = TimeSpan.FromSeconds(2);

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

        sut.GetDiagnostics().PendingWaiterCount.Should().Be(
            1,
            "the COM3 waiter must be registered before the matching signal is published");
        waitTask.IsCompleted.Should().BeFalse();

        PublishBlockSignal(sut, "COM3");
        var triggerEvent = await WaitForTriggerAsync(
            sut,
            waitTask,
            nameof(WaitForSerialPhotoelectricAsync_ShouldNotMatchDifferentComPort));

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

        sut.GetDiagnostics().PendingWaiterCount.Should().Be(
            1,
            "busy protection must ignore the pending signal and register a fresh waiter");
        waitTask.IsCompleted.Should().BeFalse();

        PublishBlockSignal(sut, "COM3");
        var triggerEvent = await WaitForTriggerAsync(
            sut,
            waitTask,
            nameof(WaitForSerialPhotoelectricAsync_ShouldIgnorePendingSignalWhenBusyProtectionEnabled));

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

    private static async Task<TriggerInputEvent> WaitForTriggerAsync(
        SerialPhotoelectricTriggerInputService service,
        Task<TriggerInputEvent> waitTask,
        string testName)
    {
        try
        {
            return await waitTask.WaitAsync(TestCompletionTimeout);
        }
        catch (TimeoutException ex)
        {
            var diagnostics = service.GetDiagnostics();
            throw new TimeoutException(
                $"{testName} did not receive the published serial trigger within {TestCompletionTimeout}. " +
                $"WaitTaskStatus={waitTask.Status}; PendingWaiterCount={diagnostics.PendingWaiterCount}; " +
                $"IsAvailable={diagnostics.IsAvailable}; ListenerType={diagnostics.ListenerType}; " +
                $"LastDeviceId={diagnostics.LastDeviceId ?? "<none>"}; LastError={diagnostics.LastError ?? "<none>"}.",
                ex);
        }
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
