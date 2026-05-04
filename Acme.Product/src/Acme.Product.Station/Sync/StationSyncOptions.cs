namespace Acme.Product.Station.Sync;

public sealed class StationSyncOptions
{
    public const string SectionName = "StationSync";

    public bool Enabled { get; set; }

    public string StudioBaseUrl { get; set; } = string.Empty;

    public string SharedToken { get; set; } = string.Empty;

    public int HeartbeatIntervalSeconds { get; set; } = 5;

    public int SnapshotDebounceMilliseconds { get; set; } = 750;

    public int PendingBatchSize { get; set; } = 100;

    public int MaxBufferedResults { get; set; } = 10_000;

    public string SpoolDirectoryPath { get; set; } = string.Empty;
}
