using Acme.Product.Core.Cameras;

namespace Acme.Product.Infrastructure.Cameras;

public sealed class NoOpTriggerInputService : ITriggerInputService
{
    public static NoOpTriggerInputService Instance { get; } = new();

    public bool IsAvailable => false;

    private NoOpTriggerInputService()
    {
    }

    public Task<TriggerInputEvent> WaitForEnterPhotoelectricAsync(
        EnterPhotoelectricTriggerOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromException<TriggerInputEvent>(
            new InvalidOperationException("Enter photoelectric trigger input service is not available."));

    public Task<TriggerDeviceLearnResult> LearnEnterPhotoelectricDeviceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromException<TriggerDeviceLearnResult>(
            new InvalidOperationException("Enter photoelectric trigger input service is not available."));

    public TriggerInputDiagnostics GetDiagnostics() =>
        new(false, "None", 0, null, null, null, "Trigger input service is not registered.");
}
