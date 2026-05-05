using System.Text.Json;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Station;

public sealed class StationLocalSettingsStore
{
    private readonly object _syncRoot = new();
    private readonly string _settingsPath;
    private readonly string _crashMarkerPath;
    private StationLocalSettings _settings;

    public StationLocalSettingsStore()
        : this(null)
    {
    }

    public StationLocalSettingsStore(string? rootPath)
    {
        var root = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation")
            : rootPath;
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "station-settings.json");
        _crashMarkerPath = Path.Combine(root, ".session.lock");
        _settings = Load();
    }

    public StationLocalSettings Current
    {
        get
        {
            lock (_syncRoot)
            {
                return new StationLocalSettings
                {
                    StationId = _settings.StationId,
                    StationName = _settings.StationName,
                    LineName = _settings.LineName,
                    AreaName = _settings.AreaName,
                    WorkcellName = _settings.WorkcellName,
                    InspectionNodeName = _settings.InspectionNodeName,
                    CameraAlias = _settings.CameraAlias,
                    StationRole = _settings.StationRole,
                    Owner = _settings.Owner,
                    LastGoodPackagePath = _settings.LastGoodPackagePath,
                    LastRunId = _settings.LastRunId,
                    CurrentPackageVersion = _settings.CurrentPackageVersion,
                    LastHealthSequenceId = _settings.LastHealthSequenceId,
                    LastLogSequenceId = _settings.LastLogSequenceId,
                    LastUnexpectedExitAtUtc = _settings.LastUnexpectedExitAtUtc
                };
            }
        }
    }

    public void MarkStartup()
    {
        lock (_syncRoot)
        {
            if (File.Exists(_crashMarkerPath))
            {
                _settings.LastUnexpectedExitAtUtc = DateTimeOffset.UtcNow;
            }

            File.WriteAllText(_crashMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
            SaveLocked();
        }
    }

    public void MarkCleanExit()
    {
        lock (_syncRoot)
        {
            if (File.Exists(_crashMarkerPath))
            {
                File.Delete(_crashMarkerPath);
            }

            SaveLocked();
        }
    }

    public void UpdateLastGoodPackage(string packagePath)
    {
        lock (_syncRoot)
        {
            _settings.LastGoodPackagePath = packagePath;
            SaveLocked();
        }
    }

    public void UpdateLastRun(string runId)
    {
        lock (_syncRoot)
        {
            _settings.LastRunId = runId;
            SaveLocked();
        }
    }

    public long NextHealthSequenceId()
    {
        lock (_syncRoot)
        {
            _settings.LastHealthSequenceId++;
            SaveLocked();
            return _settings.LastHealthSequenceId;
        }
    }

    public long NextLogSequenceId()
    {
        lock (_syncRoot)
        {
            _settings.LastLogSequenceId++;
            SaveLocked();
            return _settings.LastLogSequenceId;
        }
    }

    public void UpdateStationIdentity(string? stationId, string? lineName)
    {
        lock (_syncRoot)
        {
            _settings.StationId = string.IsNullOrWhiteSpace(stationId) ? null : stationId.Trim();
            _settings.LineName = string.IsNullOrWhiteSpace(lineName) ? null : lineName.Trim();
            SaveLocked();
        }
    }

    private StationLocalSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new StationLocalSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<StationLocalSettings>(json, CreateJsonOptions())
                ?? new StationLocalSettings();
        }
        catch
        {
            return new StationLocalSettings();
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(_settings, CreateJsonOptions());
        File.WriteAllText(_settingsPath, json);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            }
        };
    }
}
