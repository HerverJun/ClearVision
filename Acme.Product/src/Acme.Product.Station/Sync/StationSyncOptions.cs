using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Station.Sync;

public sealed class StationSyncOptions
{
    public const string SectionName = "StationSync";

    public bool Enabled { get; set; }

    public string StudioBaseUrl { get; set; } = string.Empty;

    public string StudioHubUrl { get; set; } = string.Empty;

    public string SharedToken { get; set; } = string.Empty;

    public int HeartbeatIntervalSeconds { get; set; } = 5;

    public int HealthIntervalSeconds { get; set; } = 15;

    public int SnapshotDebounceMilliseconds { get; set; } = 750;

    public int PendingBatchSize { get; set; } = 100;

    public int MaxBufferedResults { get; set; } = 10_000;

    public string SpoolDirectoryPath { get; set; } = string.Empty;

    public string SpoolDirectory { get; set; } = "%LocalAppData%\\ClearVisionStation\\spool";

    public int MaxSpoolMb { get; set; } = 512;

    public int MaxSpoolDays { get; set; } = 7;

    public int OutboundQueueCapacity { get; set; } = 1000;

    public int LogQueueCapacity { get; set; } = 500;

    public int MaxLogSummariesPerMinute { get; set; } = 60;

    public string LogDirectory { get; set; } = "%LocalAppData%\\ClearVisionStation\\logs";

    public int MaxCollectLogsMb { get; set; } = 64;

    public int MaxCollectLogsHours { get; set; } = 24;

    public string PackageDirectory { get; set; } = "%LocalAppData%\\ClearVisionStation\\packages";

    public string ResolvedStudioHubUrl => ResolveStudioHubUrl(StudioBaseUrl, StudioHubUrl);

    public string ResolvedSpoolDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SpoolDirectoryPath))
            {
                return ExpandPath(SpoolDirectoryPath);
            }

            return ExpandPath(SpoolDirectory);
        }
    }

    public string ResolvedPackageDirectory => ExpandPath(PackageDirectory);

    public string ResolvedLogDirectory => ExpandPath(LogDirectory);

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return path.Replace("%LocalAppData%", localAppData, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveStudioHubUrl(string? studioBaseUrl, string? studioHubUrl)
    {
        if (!string.IsNullOrWhiteSpace(studioHubUrl))
        {
            return studioHubUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(studioBaseUrl))
        {
            return string.Empty;
        }

        var normalizedBaseUrl = studioBaseUrl.Trim().TrimEnd('/');
        if (normalizedBaseUrl.EndsWith(StationSyncContractDefaults.HubPath, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedBaseUrl;
        }

        return normalizedBaseUrl + StationSyncContractDefaults.HubPath;
    }
}
