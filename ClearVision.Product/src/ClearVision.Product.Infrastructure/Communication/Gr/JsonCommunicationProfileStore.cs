using System.Text.Json;

namespace ClearVision.Product.Infrastructure.Communication.Gr;

public sealed class JsonCommunicationProfileStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public JsonCommunicationProfileStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVision",
            "Communication",
            "modbus-profiles.json"))
    {
    }

    public JsonCommunicationProfileStore(string path)
    {
        _path = path;
    }

    public string StoragePath => _path;

    public IReadOnlyList<ModbusDeviceProfile> GetAll()
    {
        lock (_sync)
        {
            return ReadUnsafe();
        }
    }

    public ModbusDeviceProfile? Get(string id) =>
        GetAll().FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public ModbusDeviceProfile Save(ModbusDeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);
        lock (_sync)
        {
            var profiles = ReadUnsafe().ToList();
            var normalized = profile with
            {
                Id = profile.Id.Trim(),
                Name = profile.Name.Trim(),
                Host = profile.Host.Trim(),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ReadOnly = true
            };
            profiles.RemoveAll(item => item.Id.Equals(normalized.Id, StringComparison.OrdinalIgnoreCase));
            profiles.Add(normalized);
            WriteUnsafe(profiles.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());
            return normalized;
        }
    }

    public bool Delete(string id)
    {
        lock (_sync)
        {
            var profiles = ReadUnsafe().ToList();
            var removed = profiles.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                WriteUnsafe(profiles);
            }
            return removed;
        }
    }

    private IReadOnlyList<ModbusDeviceProfile> ReadUnsafe()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<ModbusDeviceProfile>>(json, JsonOptions) ?? [];
    }

    private void WriteUnsafe(IReadOnlyList<ModbusDeviceProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Communication profile path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profiles, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Validate(ModbusDeviceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new ArgumentException("Profile Id, Name and Host are required.");
        }
        if (profile.Port is < 1 or > 65535 || profile.UnitId is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Profile Port or UnitId is outside the Modbus TCP range.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed record ModbusDeviceProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 502;
    public int UnitId { get; init; } = 1;
    public string TemplateId { get; init; } = string.Empty;
    public string TemplateVersion { get; init; } = string.Empty;
    public string TemplateHash { get; init; } = string.Empty;
    public bool ReadOnly { get; init; } = true;
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
