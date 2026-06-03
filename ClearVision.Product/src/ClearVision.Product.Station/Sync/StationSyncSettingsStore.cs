using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Station.Sync;

public sealed class StationSyncSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly StationSyncOptions _options;
    private readonly string _appliedMarkerPath;

    public StationSyncSettingsStore(IOptions<StationSyncOptions> options)
        : this(
            StationSettingsPaths.GetStationSyncSettingsPath(),
            StationSettingsPaths.GetStationSyncSettingsAppliedMarkerPath(),
            options.Value)
    {
    }

    public StationSyncSettingsStore(string settingsPath, StationSyncOptions? options = null)
        : this(
            settingsPath,
            Path.Combine(
                Path.GetDirectoryName(settingsPath) ?? string.Empty,
                StationSettingsPaths.StationSyncSettingsAppliedMarkerFileName),
            options ?? new StationSyncOptions())
    {
    }

    private StationSyncSettingsStore(
        string settingsPath,
        string appliedMarkerPath,
        StationSyncOptions options)
    {
        SettingsPath = settingsPath;
        _appliedMarkerPath = appliedMarkerPath;
        _options = options;
    }

    public event EventHandler<StationSyncConnectionSettings>? ConnectionSettingsChanged;

    public string SettingsPath { get; }

    public StationSyncConnectionSettings Current
    {
        get
        {
            lock (_gate)
            {
                return LoadConnectionSettingsCore();
            }
        }
    }

    public StationSyncConnectionSettings SaveConnectionSettings(StationSyncConnectionSettings settings)
    {
        StationSyncConnectionSettings saved;
        lock (_gate)
        {
            saved = NormalizeForSave(settings);
            var root = ReadDocumentCore();
            var stationSync = GetOrCreateStationSyncObject(root);
            stationSync["Enabled"] = saved.Enabled;
            stationSync["StudioBaseUrl"] = saved.StudioBaseUrl;
            stationSync["StudioHubUrl"] = saved.StudioHubUrl;
            stationSync["SharedToken"] = saved.SharedToken;

            WriteDocumentCore(root);
            ApplyToLiveOptions(saved);
            MarkSettingsAppliedCore();
        }

        ConnectionSettingsChanged?.Invoke(this, saved);
        return saved;
    }

    public StationSyncConnectionSettings ReloadConnectionSettings()
    {
        StationSyncConnectionSettings settings;
        lock (_gate)
        {
            settings = LoadConnectionSettingsCore();
            ApplyToLiveOptions(settings);
            MarkSettingsAppliedCore();
        }

        ConnectionSettingsChanged?.Invoke(this, settings);
        return settings;
    }

    private StationSyncConnectionSettings LoadConnectionSettingsCore()
    {
        var settings = StationSyncConnectionSettings.FromOptions(_options, SettingsPath);
        if (!File.Exists(SettingsPath))
        {
            return settings;
        }

        var root = ReadDocumentCore();
        if (root[StationSyncOptions.SectionName] is not JsonObject stationSync)
        {
            return settings;
        }

        return new StationSyncConnectionSettings
        {
            Enabled = ReadBoolean(stationSync, nameof(StationSyncOptions.Enabled), settings.Enabled),
            StudioBaseUrl = ReadString(stationSync, nameof(StationSyncOptions.StudioBaseUrl), settings.StudioBaseUrl),
            StudioHubUrl = ReadString(stationSync, nameof(StationSyncOptions.StudioHubUrl), settings.StudioHubUrl),
            SharedToken = ReadString(stationSync, nameof(StationSyncOptions.SharedToken), settings.SharedToken),
            SettingsPath = SettingsPath
        };
    }

    private StationSyncConnectionSettings NormalizeForSave(StationSyncConnectionSettings settings)
    {
        var studioBaseUrl = NormalizeUrl(settings.StudioBaseUrl);
        var studioHubUrl = NormalizeUrl(settings.StudioHubUrl);
        studioBaseUrl = TryResolveBaseUrlFromHubUrl(studioBaseUrl) ?? studioBaseUrl;

        if (string.IsNullOrWhiteSpace(studioHubUrl))
        {
            studioHubUrl = StationSyncOptions.ResolveStudioHubUrl(studioBaseUrl, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(studioBaseUrl) && !string.IsNullOrWhiteSpace(studioHubUrl))
        {
            studioBaseUrl = TryResolveBaseUrlFromHubUrl(studioHubUrl) ?? string.Empty;
        }

        return new StationSyncConnectionSettings
        {
            Enabled = settings.Enabled,
            StudioBaseUrl = studioBaseUrl,
            StudioHubUrl = studioHubUrl,
            SharedToken = settings.SharedToken.Trim(),
            SettingsPath = SettingsPath
        };
    }

    private JsonObject ReadDocumentCore()
    {
        if (!File.Exists(SettingsPath))
        {
            return new JsonObject();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private void WriteDocumentCore(JsonObject root)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, root.ToJsonString(JsonOptions), Encoding.UTF8);
    }

    private void MarkSettingsAppliedCore()
    {
        var directory = Path.GetDirectoryName(_appliedMarkerPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_appliedMarkerPath, DateTimeOffset.UtcNow.ToString("O"), Encoding.UTF8);
    }

    private void ApplyToLiveOptions(StationSyncConnectionSettings settings)
    {
        _options.Enabled = settings.Enabled;
        _options.StudioBaseUrl = settings.StudioBaseUrl;
        _options.StudioHubUrl = settings.StudioHubUrl;
        _options.SharedToken = settings.SharedToken;
    }

    private static JsonObject GetOrCreateStationSyncObject(JsonObject root)
    {
        if (root[StationSyncOptions.SectionName] is JsonObject stationSync)
        {
            return stationSync;
        }

        stationSync = new JsonObject();
        root[StationSyncOptions.SectionName] = stationSync;
        return stationSync;
    }

    private static string ReadString(JsonObject source, string propertyName, string fallback)
    {
        return source.TryGetPropertyValue(propertyName, out var node) && node != null
            ? node.GetValue<string>() ?? string.Empty
            : fallback;
    }

    private static bool ReadBoolean(JsonObject source, string propertyName, bool fallback)
    {
        return source.TryGetPropertyValue(propertyName, out var node) && node != null
            ? node.GetValue<bool>()
            : fallback;
    }

    private static string NormalizeUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('/');
    }

    private static string? TryResolveBaseUrlFromHubUrl(string hubUrl)
    {
        var index = hubUrl.IndexOf(StationSyncContractDefaults.HubPath, StringComparison.OrdinalIgnoreCase);
        return index <= 0 ? null : hubUrl[..index].TrimEnd('/');
    }
}
