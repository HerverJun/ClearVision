namespace Acme.Product.Core.Cameras;

public interface ITriggerInputService
{
    bool IsAvailable { get; }

    Task<TriggerInputEvent> WaitForEnterPhotoelectricAsync(
        EnterPhotoelectricTriggerOptions options,
        CancellationToken cancellationToken = default);

    Task<TriggerDeviceLearnResult> LearnEnterPhotoelectricDeviceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    TriggerInputDiagnostics GetDiagnostics();
}

public sealed record EnterPhotoelectricTriggerOptions(
    string CameraBindingId,
    string DisplayName,
    string DeviceId,
    int DebounceMs,
    int TimeoutMs,
    bool IgnoreWhileBusy)
{
    public DateTime? AcceptPendingSignalsAfterUtc { get; init; }
}

public sealed record TriggerInputEvent(
    string Source,
    string CameraBindingId,
    string DeviceId,
    DateTime TimestampUtc);

public sealed record TriggerDeviceLearnResult(
    string DeviceId,
    DateTime TimestampUtc);

public sealed record TriggerInputDiagnostics(
    bool IsAvailable,
    string ListenerType,
    int PendingWaiterCount,
    string? AttachedWindowHandle,
    string? LastDeviceId,
    DateTime? LastSignalUtc,
    string? LastError);
