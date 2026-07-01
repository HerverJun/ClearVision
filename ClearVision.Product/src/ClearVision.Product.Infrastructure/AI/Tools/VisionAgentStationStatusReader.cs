namespace ClearVision.Product.Infrastructure.AI.Tools;

public interface IVisionAgentStationStatusReader
{
    Task<VisionAgentStationStatus?> TryReadAsync(
        string targetStationId,
        CancellationToken cancellationToken);
}

public sealed record VisionAgentStationStatus
{
    public string StationId { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class NoOpVisionAgentStationStatusReader : IVisionAgentStationStatusReader
{
    public Task<VisionAgentStationStatus?> TryReadAsync(
        string targetStationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<VisionAgentStationStatus?>(null);
    }
}
