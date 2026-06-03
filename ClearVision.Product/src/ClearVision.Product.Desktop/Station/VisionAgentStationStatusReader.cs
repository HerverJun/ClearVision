using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Desktop.Station;

public sealed class VisionAgentStationStatusReader : IVisionAgentStationStatusReader
{
    private readonly StationRegistryService _registryService;

    public VisionAgentStationStatusReader(StationRegistryService registryService)
    {
        _registryService = registryService;
    }

    public Task<IReadOnlyList<VisionAgentStationStatus>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stations = _registryService.GetStations()
            .Select(station => new VisionAgentStationStatus
            {
                StationId = station.StationId,
                DisplayName = string.IsNullOrWhiteSpace(station.StationName)
                    ? station.MachineName
                    : station.StationName,
                Online = station.IsOnline,
                Version = station.ClientVersion,
                LastHeartbeatUtc = station.LastSeenAtUtc,
                CurrentPackageId = station.PackageId,
                CurrentPackageName = station.PackageName,
                State = station.State.ToString()
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<VisionAgentStationStatus>>(stations);
    }
}

