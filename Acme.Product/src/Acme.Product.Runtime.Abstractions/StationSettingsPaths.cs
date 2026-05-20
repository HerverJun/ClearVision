namespace Acme.Product.Runtime.Abstractions;

public static class StationSettingsPaths
{
    public const string StudioAppDataDirectoryName = "ClearVisionStudio";
    public const string StationAppDataDirectoryName = "ClearVisionStation";
    public const string StudioCommunicationFileName = "station-communication.json";
    public const string StationSyncSettingsFileName = "station-sync-settings.json";
    public const string StationSyncSettingsAppliedMarkerFileName = "station-sync-settings.applied";

    public static string GetStudioCommunicationSettingsPath(string? localAppDataRoot = null)
    {
        return Path.Combine(
            ResolveLocalAppDataRoot(localAppDataRoot),
            StudioAppDataDirectoryName,
            StudioCommunicationFileName);
    }

    public static string GetStationSyncSettingsPath(string? localAppDataRoot = null)
    {
        return Path.Combine(
            ResolveLocalAppDataRoot(localAppDataRoot),
            StationAppDataDirectoryName,
            StationSyncSettingsFileName);
    }

    public static string GetStationSyncSettingsAppliedMarkerPath(string? localAppDataRoot = null)
    {
        return Path.Combine(
            ResolveLocalAppDataRoot(localAppDataRoot),
            StationAppDataDirectoryName,
            StationSyncSettingsAppliedMarkerFileName);
    }

    private static string ResolveLocalAppDataRoot(string? localAppDataRoot)
    {
        return string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;
    }
}
