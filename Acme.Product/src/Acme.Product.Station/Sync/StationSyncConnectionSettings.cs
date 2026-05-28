namespace Acme.Product.Station.Sync;

public sealed record StationSyncConnectionSettings
{
    public bool Enabled { get; init; }

    public string StudioBaseUrl { get; init; } = string.Empty;

    public string StudioHubUrl { get; init; } = string.Empty;

    public string SharedToken { get; init; } = string.Empty;

    public string SettingsPath { get; init; } = string.Empty;

    public string ResolvedStudioHubUrl => StationSyncOptions.ResolveStudioHubUrl(StudioBaseUrl, StudioHubUrl);

    public bool HasSharedToken => !string.IsNullOrWhiteSpace(SharedToken);

    public static StationSyncConnectionSettings FromOptions(StationSyncOptions options, string settingsPath)
    {
        return new StationSyncConnectionSettings
        {
            Enabled = options.Enabled,
            StudioBaseUrl = options.StudioBaseUrl ?? string.Empty,
            StudioHubUrl = options.StudioHubUrl ?? string.Empty,
            SharedToken = options.SharedToken ?? string.Empty,
            SettingsPath = settingsPath
        };
    }
}
