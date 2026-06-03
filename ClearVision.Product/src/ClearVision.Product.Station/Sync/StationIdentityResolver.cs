using System.Reflection;

namespace ClearVision.Product.Station.Sync;

public sealed class StationIdentityResolver
{
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly object _syncRoot = new();
    private StationIdentityContext? _cachedIdentity;

    public StationIdentityResolver(StationLocalSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public StationIdentityContext GetOrCreate()
    {
        lock (_syncRoot)
        {
            var current = _settingsStore.Current;
            var stationId = string.IsNullOrWhiteSpace(current.StationId)
                ? BuildGeneratedStationId()
                : current.StationId.Trim();
            var lineName = string.IsNullOrWhiteSpace(current.LineName)
                ? null
                : current.LineName.Trim();

            if (!string.Equals(stationId, current.StationId, StringComparison.Ordinal) ||
                !string.Equals(lineName, current.LineName, StringComparison.Ordinal))
            {
                _settingsStore.UpdateStationIdentity(stationId, lineName);
            }

            _cachedIdentity ??= new StationIdentityContext
            {
                StationId = stationId,
                MachineName = Environment.MachineName.Trim(),
                ClientVersion = ResolveClientVersion(),
                StartedAtUtc = DateTimeOffset.UtcNow
            };

            _cachedIdentity = _cachedIdentity with
            {
                StationId = stationId,
                LineName = lineName,
                StationName = current.StationName,
                StationRole = current.StationRole,
                AreaName = current.AreaName,
                WorkcellName = current.WorkcellName,
                InspectionNodeName = current.InspectionNodeName,
                CameraAlias = current.CameraAlias,
                Owner = current.Owner,
                CurrentPackageVersion = current.CurrentPackageVersion
            };

            return _cachedIdentity;
        }
    }

    private static string BuildGeneratedStationId()
    {
        var machineName = SanitizeSegment(Environment.MachineName, "station");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{machineName}-{suffix}".ToLowerInvariant();
    }

    private static string ResolveClientVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string SanitizeSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var chars = value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}

public sealed record StationIdentityContext
{
    public string StationId { get; init; } = string.Empty;

    public string? LineName { get; init; }

    public string? StationName { get; init; }

    public string? AreaName { get; init; }

    public string? WorkcellName { get; init; }

    public string? InspectionNodeName { get; init; }

    public string? CameraAlias { get; init; }

    public string? StationRole { get; init; }

    public string? Owner { get; init; }

    public string MachineName { get; init; } = string.Empty;

    public string ClientVersion { get; init; } = string.Empty;

    public string? CurrentPackageVersion { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }
}
