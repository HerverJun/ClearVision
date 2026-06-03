namespace ClearVision.Product.Core.AI.Tools;

public interface IVisionAgentStationStatusReader
{
    Task<IReadOnlyList<VisionAgentStationStatus>> GetStationsAsync(CancellationToken cancellationToken = default);
}

public sealed record VisionAgentStationStatus
{
    public string StationId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Online { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
    public string? CurrentPackageId { get; init; }
    public string? CurrentPackageName { get; init; }
    public string? CurrentPackageVersion { get; init; }
    public string? State { get; init; }
}

