using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net;
using ClearVision.Product.Core.Continuous;

namespace ClearVision.Product.Core.Entities;

public class AppConfig
{
    public long Revision { get; set; }

    public GeneralConfig General { get; set; } = new();

    public CommunicationConfig Communication { get; set; } = new();

    public TcpCommunicationConfig TcpCommunication { get; set; } = new();

    public ExecutionResourceProfilesConfig ExecutionResources { get; set; } = new();

    public StorageConfig Storage { get; set; } = new();

    public RuntimeConfig Runtime { get; set; } = new();

    public FeatureConfig Features { get; set; } = new();

    public List<CameraBindingConfig> Cameras { get; set; } = new();

    public SecurityConfig Security { get; set; } = new();

    public string ActiveCameraId { get; set; } = string.Empty;

    public void Normalize()
    {
        General ??= new GeneralConfig();
        General.Normalize();
        Communication ??= new CommunicationConfig();
        Communication.Normalize();
        TcpCommunication ??= new TcpCommunicationConfig();
        TcpCommunication.Normalize();
        ExecutionResources ??= new ExecutionResourceProfilesConfig();
        ExecutionResources.Normalize();
        Storage ??= new StorageConfig();
        Runtime ??= new RuntimeConfig();
        Runtime.Normalize();
        Features ??= new FeatureConfig();
        Features.Normalize();
        Cameras ??= new List<CameraBindingConfig>();
        foreach (var camera in Cameras)
        {
            camera.Normalize();
        }
        Security ??= new SecurityConfig();
        ActiveCameraId ??= string.Empty;
    }
}

public class GeneralConfig
{
    public const string ThemeDark = "dark";
    public const string ThemeLight = "light";

    public string SoftwareTitle { get; set; } = "ClearVision 检测站";

    public string Theme { get; set; } = ThemeDark;

    public bool AutoStart { get; set; }

    public void Normalize()
    {
        Theme = NormalizeTheme(Theme);
    }

    public static string NormalizeTheme(string? theme)
    {
        var candidate = (theme ?? string.Empty).Trim().ToLowerInvariant();
        return candidate switch
        {
            ThemeLight => ThemeLight,
            ThemeDark => ThemeDark,
            _ => ThemeDark
        };
    }
}

public class FeatureConfig
{
    public ContinuousInspectionFeatureConfig ContinuousInspection { get; set; } = new();

    public void Normalize()
    {
        ContinuousInspection ??= new ContinuousInspectionFeatureConfig();
        ContinuousInspection.Normalize();
    }
}

public class ContinuousInspectionFeatureConfig
{
    public bool Enabled { get; set; }

    public bool EmergencyRollback { get; set; }

    public Dictionary<string, ContinuousInspectionConfig> HardwareProfiles { get; set; } =
        ContinuousInspectionConfigTemplates.CreateDefaults().ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        HardwareProfiles ??= new Dictionary<string, ContinuousInspectionConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in ContinuousInspectionConfigTemplates.CreateDefaults())
        {
            HardwareProfiles.TryAdd(template.Key, template.Value);
        }

        foreach (var profile in HardwareProfiles.Values)
        {
            profile.Normalize();
        }
    }
}

public class CommunicationConfig
{
    public const string ProtocolS7 = "S7";
    public const string ProtocolMc = "MC";
    public const string ProtocolFins = "FINS";

    private const int DefaultHeartbeatIntervalMs = 1000;
    private const int DefaultS7Port = 102;
    private const int DefaultMcPort = 5002;
    private const int DefaultFinsPort = 9600;

    public string ActiveProtocol { get; set; } = ProtocolS7;

    public int HeartbeatIntervalMs { get; set; } = DefaultHeartbeatIntervalMs;

    public S7CommunicationProfile S7 { get; set; } = S7CommunicationProfile.CreateDefault();

    public PlcCommunicationProfile Mc { get; set; } = PlcCommunicationProfile.CreateDefault(DefaultMcPort);

    public PlcCommunicationProfile Fins { get; set; } = PlcCommunicationProfile.CreateDefault(DefaultFinsPort);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? PlcIpAddress { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PlcPort { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Protocol { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? IpAddress { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Port { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<PlcAddressMapping>? Mappings { get; set; }

    public void Normalize()
    {
        ActiveProtocol = NormalizeProtocolKey(ActiveProtocol, Protocol);
        HeartbeatIntervalMs = HeartbeatIntervalMs > 0 ? HeartbeatIntervalMs : DefaultHeartbeatIntervalMs;

        S7 ??= S7CommunicationProfile.CreateDefault();
        S7.Normalize(DefaultS7Port);

        Mc ??= PlcCommunicationProfile.CreateDefault(DefaultMcPort);
        Mc.Normalize(DefaultMcPort);

        Fins ??= PlcCommunicationProfile.CreateDefault(DefaultFinsPort);
        Fins.Normalize(DefaultFinsPort);

        ApplyLegacyMigration();
    }

    public PlcCommunicationProfile GetProfile(string? protocol = null)
    {
        return NormalizeProtocolKey(protocol, ActiveProtocol) switch
        {
            ProtocolMc => Mc,
            ProtocolFins => Fins,
            _ => S7
        };
    }

    public List<PlcAddressMapping> GetMappings(string? protocol = null)
    {
        return GetProfile(protocol).Mappings;
    }

    public void SetMappings(string? protocol, IEnumerable<PlcAddressMapping>? mappings)
    {
        GetProfile(protocol).Mappings = NormalizeMappings(mappings);
    }

    public static string NormalizeProtocolKey(string? protocol, string? fallback = null)
    {
        var candidate = (protocol ?? fallback ?? ProtocolS7).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return ProtocolS7;
        }

        return candidate.ToUpperInvariant() switch
        {
            "S7" or "SIEMENSS7" => ProtocolS7,
            "MC" or "MITSUBISHIMC" => ProtocolMc,
            "FINS" or "OMRONFINS" => ProtocolFins,
            _ => ProtocolS7
        };
    }

    public static int GetDefaultPort(string protocol)
    {
        return NormalizeProtocolKey(protocol) switch
        {
            ProtocolMc => DefaultMcPort,
            ProtocolFins => DefaultFinsPort,
            _ => DefaultS7Port
        };
    }

    public static List<PlcAddressMapping> NormalizeMappings(IEnumerable<PlcAddressMapping>? mappings)
    {
        if (mappings == null)
        {
            return new List<PlcAddressMapping>();
        }

        var normalized = new List<PlcAddressMapping>();
        foreach (var item in mappings)
        {
            if (item == null)
            {
                continue;
            }

            var mapping = item.Normalize();
            if (mapping.IsEmpty())
            {
                continue;
            }

            normalized.Add(mapping);
        }

        return normalized;
    }

    private void ApplyLegacyMigration()
    {
        var legacyIpAddress = FirstNonEmpty(PlcIpAddress, IpAddress);
        var legacyPort = PlcPort > 0 ? PlcPort : Port;
        var hasLegacyMappings = Mappings is { Count: > 0 };
        var hasLegacyConnection = !string.IsNullOrWhiteSpace(legacyIpAddress)
            || legacyPort > 0
            || !string.IsNullOrWhiteSpace(Protocol);

        if (!hasLegacyConnection && !hasLegacyMappings)
        {
            ClearLegacyFields();
            return;
        }

        var targetProtocol = NormalizeProtocolKey(Protocol, ActiveProtocol);
        var targetProfile = GetProfile(targetProtocol);

        if (!string.IsNullOrWhiteSpace(legacyIpAddress))
        {
            targetProfile.IpAddress = legacyIpAddress.Trim();
        }

        if (legacyPort > 0 && legacyPort <= 65535)
        {
            targetProfile.Port = legacyPort;
        }

        if (hasLegacyMappings)
        {
            targetProfile.Mappings = NormalizeMappings(Mappings);
        }

        targetProfile.Normalize(GetDefaultPort(targetProtocol));
        ActiveProtocol = targetProtocol;
        ClearLegacyFields();
    }

    private void ClearLegacyFields()
    {
        PlcIpAddress = null;
        PlcPort = 0;
        Protocol = null;
        IpAddress = null;
        Port = 0;
        Mappings = null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

public class PlcAddressMapping
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = "Bool";
    public string Description { get; set; } = string.Empty;
    public bool CanWrite { get; set; }

    public PlcAddressMapping Normalize()
    {
        return new PlcAddressMapping
        {
            Name = (Name ?? string.Empty).Trim(),
            Address = (Address ?? string.Empty).Trim(),
            DataType = string.IsNullOrWhiteSpace(DataType) ? "Bool" : DataType.Trim(),
            Description = (Description ?? string.Empty).Trim(),
            CanWrite = CanWrite
        };
    }

    public bool IsEmpty()
    {
        return string.IsNullOrWhiteSpace(Name)
            && string.IsNullOrWhiteSpace(Address)
            && string.IsNullOrWhiteSpace(Description);
    }
}

public class PlcCommunicationProfile
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public List<PlcAddressMapping> Mappings { get; set; } = new();

    public virtual void Normalize(int defaultPort)
    {
        IpAddress = (IpAddress ?? string.Empty).Trim();
        Port = Port == 0 ? defaultPort : Port;
        Mappings = CommunicationConfig.NormalizeMappings(Mappings);
    }

    public static PlcCommunicationProfile CreateDefault(int defaultPort)
    {
        return new PlcCommunicationProfile
        {
            Port = defaultPort,
            Mappings = new List<PlcAddressMapping>()
        };
    }
}

public sealed class S7CommunicationProfile : PlcCommunicationProfile
{
    public string CpuType { get; set; } = "S7-1200";
    public int Rack { get; set; }
    public int Slot { get; set; } = 1;

    public override void Normalize(int defaultPort)
    {
        base.Normalize(defaultPort);
        CpuType = string.IsNullOrWhiteSpace(CpuType) ? "S7-1200" : CpuType.Trim();
    }

    public static S7CommunicationProfile CreateDefault()
    {
        return new S7CommunicationProfile
        {
            Port = 102,
            CpuType = "S7-1200",
            Rack = 0,
            Slot = 1,
            Mappings = new List<PlcAddressMapping>()
        };
    }
}

public class TcpCommunicationConfig
{
    public List<TcpCommunicationProfile> Profiles { get; set; } = new();

    public void Normalize()
    {
        Profiles ??= new List<TcpCommunicationProfile>();

        var normalizedProfiles = new List<TcpCommunicationProfile>();
        foreach (var profile in Profiles)
        {
            if (profile == null)
            {
                continue;
            }

            profile.Normalize();
            normalizedProfiles.Add(profile);
        }

        Profiles = normalizedProfiles;
    }

    public TcpCommunicationProfile? FindProfile(string? id)
    {
        Normalize();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public class TcpCommunicationProfile
{
    public const string ModeClient = "Client";
    public const string ModeServer = "Server";
    public const string EncodingUtf8 = "UTF8";
    public const string EncodingAscii = "ASCII";
    public const string EncodingGbk = "GBK";
    public const string EncodingHex = "HEX";
    public const string FrameModeRaw = "Raw";
    public const string FrameModeLine = "Line";
    public const string FrameModeFixedLength = "FixedLength";
    public const string FrameModeHex = "Hex";
    public const string LineEndingNone = "None";
    public const string LineEndingCr = "CR";
    public const string LineEndingLf = "LF";
    public const string LineEndingCrlf = "CRLF";
    public const int DefaultTimeoutMs = 5000;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 600000;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = "TCP Profile";

    public bool Enabled { get; set; }

    public string Mode { get; set; } = ModeClient;

    public string RemoteHost { get; set; } = "127.0.0.1";

    public int RemotePort { get; set; }

    public string LocalHost { get; set; } = IPAddress.Loopback.ToString();

    public int LocalPort { get; set; }

    public string Encoding { get; set; } = EncodingUtf8;

    public string FrameMode { get; set; } = FrameModeRaw;

    public int FixedLength { get; set; }

    public string LineEnding { get; set; } = LineEndingNone;

    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    public bool KeepAlive { get; set; }

    public bool Reconnect { get; set; } = true;

    public bool ConnectOnStartup { get; set; }

    public string Description { get; set; } = string.Empty;

    public void Normalize()
    {
        Id = NormalizeId(Id);
        Name = string.IsNullOrWhiteSpace(Name) ? "TCP Profile" : Name.Trim();
        Mode = NormalizeMode(Mode);
        RemoteHost = string.IsNullOrWhiteSpace(RemoteHost) ? IPAddress.Loopback.ToString() : RemoteHost.Trim();
        LocalHost = string.IsNullOrWhiteSpace(LocalHost) ? IPAddress.Loopback.ToString() : LocalHost.Trim();
        RemotePort = NormalizePort(RemotePort);
        LocalPort = NormalizePort(LocalPort);
        Encoding = NormalizeEncoding(Encoding);
        FrameMode = NormalizeFrameMode(FrameMode);
        FixedLength = FixedLength < 0 ? 0 : FixedLength;
        LineEnding = NormalizeLineEnding(LineEnding);
        TimeoutMs = NormalizeTimeout(TimeoutMs);
        Description = Description?.Trim() ?? string.Empty;
    }

    public TcpCommunicationProfile CloneNormalized()
    {
        var clone = new TcpCommunicationProfile
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            Mode = Mode,
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            LocalHost = LocalHost,
            LocalPort = LocalPort,
            Encoding = Encoding,
            FrameMode = FrameMode,
            FixedLength = FixedLength,
            LineEnding = LineEnding,
            TimeoutMs = TimeoutMs,
            KeepAlive = KeepAlive,
            Reconnect = Reconnect,
            ConnectOnStartup = ConnectOnStartup,
            Description = Description
        };
        clone.Normalize();
        return clone;
    }

    public static string NormalizeId(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? $"tcp_{Guid.NewGuid():N}"[..12]
            : candidate;
    }

    public static string NormalizeMode(string? mode)
    {
        return string.Equals(mode?.Trim(), ModeServer, StringComparison.OrdinalIgnoreCase)
            ? ModeServer
            : ModeClient;
    }

    public static string NormalizeEncoding(string? encoding)
    {
        var candidate = (encoding ?? string.Empty).Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return candidate.ToUpperInvariant() switch
        {
            EncodingAscii => EncodingAscii,
            EncodingGbk => EncodingGbk,
            EncodingHex => EncodingHex,
            _ => EncodingUtf8
        };
    }

    public static string NormalizeFrameMode(string? frameMode)
    {
        var candidate = (frameMode ?? string.Empty).Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return candidate.ToUpperInvariant() switch
        {
            "LINE" => FrameModeLine,
            "FIXEDLENGTH" => FrameModeFixedLength,
            "HEX" => FrameModeHex,
            _ => FrameModeRaw
        };
    }

    public static string NormalizeLineEnding(string? lineEnding)
    {
        return (lineEnding ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            LineEndingCr => LineEndingCr,
            LineEndingLf => LineEndingLf,
            LineEndingCrlf => LineEndingCrlf,
            _ => LineEndingNone
        };
    }

    public static int NormalizeTimeout(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return DefaultTimeoutMs;
        }

        return Math.Clamp(timeoutMs, MinTimeoutMs, MaxTimeoutMs);
    }

    public static int NormalizePort(int port)
    {
        return port is >= 0 and <= 65535 ? port : 0;
    }
}

public static class TcpCommunicationConfigValidator
{
    public static TcpCommunicationValidationResult Validate(TcpCommunicationConfig? config)
    {
        config ??= new TcpCommunicationConfig();
        config.Normalize();

        var issues = new List<TcpCommunicationValidationIssue>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < config.Profiles.Count; index++)
        {
            var profile = config.Profiles[index];
            ValidateProfile(profile, index, issues);
            if (!seenIds.Add(profile.Id))
            {
                issues.Add(new TcpCommunicationValidationIssue(
                    profile.Id,
                    "profile",
                    "id",
                    index,
                    "Profile Id 不能重复。"));
            }
        }

        return new TcpCommunicationValidationResult(issues.Count == 0, issues);
    }

    public static TcpCommunicationValidationResult ValidateProfileForOperation(TcpCommunicationProfile? profile)
    {
        if (profile == null)
        {
            return new TcpCommunicationValidationResult(
                false,
                [new TcpCommunicationValidationIssue(string.Empty, "profile", "id", null, "TCP Profile 不存在。")]);
        }

        profile.Normalize();
        var issues = new List<TcpCommunicationValidationIssue>();
        ValidateProfile(profile, null, issues);
        if (!profile.Enabled)
        {
            issues.Add(new TcpCommunicationValidationIssue(
                profile.Id,
                "profile",
                "enabled",
                null,
                "TCP Profile 已禁用。"));
        }

        return new TcpCommunicationValidationResult(issues.Count == 0, issues);
    }

    private static void ValidateProfile(
        TcpCommunicationProfile profile,
        int? index,
        ICollection<TcpCommunicationValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            issues.Add(new TcpCommunicationValidationIssue(profile.Id, "profile", "id", index, "Profile Id 不能为空。"));
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            issues.Add(new TcpCommunicationValidationIssue(profile.Id, "profile", "name", index, "Profile 名称不能为空。"));
        }

        if (profile.Mode == TcpCommunicationProfile.ModeClient)
        {
            ValidateIp(profile.Id, "connection", "remoteHost", index, profile.RemoteHost, "远端 IP", issues);
            ValidatePort(profile.Id, "connection", "remotePort", index, profile.RemotePort, "远端端口", issues);
        }
        else
        {
            ValidateIp(profile.Id, "connection", "localHost", index, profile.LocalHost, "本地监听 IP", issues);
            ValidatePort(profile.Id, "connection", "localPort", index, profile.LocalPort, "本地监听端口", issues);
        }

        if (profile.FrameMode == TcpCommunicationProfile.FrameModeFixedLength && profile.FixedLength <= 0)
        {
            issues.Add(new TcpCommunicationValidationIssue(
                profile.Id,
                "frame",
                "fixedLength",
                index,
                "FixedLength 报文模式需要配置正整数长度。"));
        }

        if (profile.TimeoutMs is < TcpCommunicationProfile.MinTimeoutMs or > TcpCommunicationProfile.MaxTimeoutMs)
        {
            issues.Add(new TcpCommunicationValidationIssue(
                profile.Id,
                "connection",
                "timeoutMs",
                index,
                $"超时时间必须在 {TcpCommunicationProfile.MinTimeoutMs}-{TcpCommunicationProfile.MaxTimeoutMs} ms 之间。"));
        }
    }

    private static void ValidateIp(
        string profileId,
        string section,
        string field,
        int? index,
        string? value,
        string label,
        ICollection<TcpCommunicationValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            (!IPAddress.TryParse(value.Trim(), out _) &&
             !string.Equals(value.Trim(), "localhost", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new TcpCommunicationValidationIssue(
                profileId,
                section,
                field,
                index,
                $"{label} 必须是有效 IP 地址。"));
        }
    }

    private static void ValidatePort(
        string profileId,
        string section,
        string field,
        int? index,
        int value,
        string label,
        ICollection<TcpCommunicationValidationIssue> issues)
    {
        if (value is < 1 or > 65535)
        {
            issues.Add(new TcpCommunicationValidationIssue(
                profileId,
                section,
                field,
                index,
                $"{label} 必须在 1-65535 之间。"));
        }
    }
}

public sealed record TcpCommunicationValidationResult(
    bool IsValid,
    IReadOnlyList<TcpCommunicationValidationIssue> Errors);

public sealed record TcpCommunicationValidationIssue(
    string ProfileId,
    string Section,
    string Field,
    int? Index,
    string Message);

/// <summary>
/// Server-owned profiles for execution resources whose raw targets must never
/// be accepted as client authority. Existing configuration files omit this
/// section and therefore normalize to deny-by-default empty profile lists.
/// </summary>
public sealed class ExecutionResourceProfilesConfig
{
    public List<DatabaseExecutionResourceProfile> DatabaseProfiles { get; set; } = new();

    public List<SerialExecutionResourceProfile> SerialProfiles { get; set; } = new();

    public List<PlcExecutionResourceProfile> PlcProfiles { get; set; } = new();

    public void Normalize()
    {
        DatabaseProfiles = (DatabaseProfiles ?? new List<DatabaseExecutionResourceProfile>())
            .OfType<DatabaseExecutionResourceProfile>()
            .ToList();
        foreach (var profile in DatabaseProfiles)
        {
            profile.Normalize();
        }

        SerialProfiles = (SerialProfiles ?? new List<SerialExecutionResourceProfile>())
            .OfType<SerialExecutionResourceProfile>()
            .ToList();
        foreach (var profile in SerialProfiles)
        {
            profile.Normalize();
        }

        PlcProfiles = (PlcProfiles ?? new List<PlcExecutionResourceProfile>())
            .OfType<PlcExecutionResourceProfile>()
            .ToList();
        foreach (var profile in PlcProfiles)
        {
            profile.Normalize();
        }
    }
}

public sealed class DatabaseExecutionResourceProfile
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string DbType { get; set; } = string.Empty;

    public string ConnectionString { get; set; } = string.Empty;

    public List<string> AllowedTableNames { get; set; } = new();

    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        DbType = (DbType ?? string.Empty).Trim();
        ConnectionString = (ConnectionString ?? string.Empty).Trim();
        AllowedTableNames = (AllowedTableNames ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class SerialExecutionResourceProfile
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; }

    public int DataBits { get; set; } = 8;

    public string StopBits { get; set; } = "One";

    public string Parity { get; set; } = "None";

    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        PortName = (PortName ?? string.Empty).Trim();
        StopBits = (StopBits ?? string.Empty).Trim();
        Parity = (Parity ?? string.Empty).Trim();
    }
}

public sealed class PlcExecutionResourceProfile
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string Protocol { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string CpuType { get; set; } = "S71200";

    public int Rack { get; set; }

    public int Slot { get; set; } = 1;

    public int UnitId { get; set; } = 1;

    public List<PlcExecutionResourceBinding> Bindings { get; set; } = new();

    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        Protocol = (Protocol ?? string.Empty).Trim();
        Host = (Host ?? string.Empty).Trim();
        CpuType = (CpuType ?? string.Empty).Trim();
        Bindings = (Bindings ?? new List<PlcExecutionResourceBinding>())
            .OfType<PlcExecutionResourceBinding>()
            .ToList();
        foreach (var binding in Bindings)
        {
            binding.Normalize();
        }
    }
}

public sealed class PlcExecutionResourceBinding
{
    public string Address { get; set; } = string.Empty;

    public string DataType { get; set; } = "Word";

    public bool CanRead { get; set; } = true;

    public bool CanWrite { get; set; }

    public int MaxElementCount { get; set; } = 1;

    public List<string> AllowedFunctionCodes { get; set; } = new();

    public void Normalize()
    {
        Address = (Address ?? string.Empty).Trim();
        DataType = string.IsNullOrWhiteSpace(DataType) ? "Word" : DataType.Trim();
        AllowedFunctionCodes = (AllowedFunctionCodes ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public class StorageConfig
{
    public string ImageSavePath { get; set; } = @"D:\VisionData\Images";

    public string SavePolicy { get; set; } = "NgOnly";

    public int RetentionDays { get; set; } = 30;

    public int MinFreeSpaceGb { get; set; } = 5;
}

public class RuntimeConfig
{
    public const int DefaultMissingMaterialTimeoutSeconds = 120;
    private const int LegacyMissingMaterialTimeoutSeconds = 30;

    public bool AutoRun { get; set; }

    public int StopOnConsecutiveNg { get; set; }

    public int MissingMaterialTimeoutSeconds { get; set; } = DefaultMissingMaterialTimeoutSeconds;

    public bool ApplyProtectionRules { get; set; } = true;

    public RuntimePreviewPilotConfig RuntimePreviewPilot { get; set; } = new();

    public void Normalize()
    {
        if (MissingMaterialTimeoutSeconds == LegacyMissingMaterialTimeoutSeconds)
        {
            MissingMaterialTimeoutSeconds = DefaultMissingMaterialTimeoutSeconds;
        }

        if (MissingMaterialTimeoutSeconds < 0)
        {
            MissingMaterialTimeoutSeconds = DefaultMissingMaterialTimeoutSeconds;
        }

        RuntimePreviewPilot ??= new RuntimePreviewPilotConfig();
        RuntimePreviewPilot.Normalize();
    }
}

public sealed class RuntimePreviewPilotConfig
{
    public const string ModeMetadataOnly = "metadata_only";
    public const int DefaultMaxPreviewArtifacts = 8;
    public const int DefaultMaxMetadataBytes = 16 * 1024;

    public bool Enabled { get; set; }

    public string Mode { get; set; } = ModeMetadataOnly;

    public List<string> AllowedCameraBindingIds { get; set; } = new();

    public List<string> AllowedModelIds { get; set; } = new();

    public List<string> AllowedTemplateIds { get; set; } = new();

    public List<string> AllowedFlowIds { get; set; } = new();

    public List<string> AllowedResourceRoots { get; set; } = new();

    public int MaxPreviewArtifacts { get; set; } = DefaultMaxPreviewArtifacts;

    public int MaxMetadataBytes { get; set; } = DefaultMaxMetadataBytes;

    public bool FallbackToOffline { get; set; } = true;

    public bool DenyExternalPath { get; set; } = true;

    public bool DenyImageBytes { get; set; } = true;

    public void Normalize()
    {
        Mode = NormalizeMode(Mode);
        AllowedCameraBindingIds = NormalizeAllowlist(AllowedCameraBindingIds);
        AllowedModelIds = NormalizeAllowlist(AllowedModelIds);
        AllowedTemplateIds = NormalizeAllowlist(AllowedTemplateIds);
        AllowedFlowIds = NormalizeAllowlist(AllowedFlowIds);
        AllowedResourceRoots = NormalizeAllowlist(AllowedResourceRoots);
        MaxPreviewArtifacts = MaxPreviewArtifacts <= 0
            ? DefaultMaxPreviewArtifacts
            : Math.Min(MaxPreviewArtifacts, 50);
        MaxMetadataBytes = MaxMetadataBytes <= 0
            ? DefaultMaxMetadataBytes
            : Math.Min(MaxMetadataBytes, 512 * 1024);
    }

    public RuntimePreviewPilotConfig CloneNormalized()
    {
        var clone = new RuntimePreviewPilotConfig
        {
            Enabled = Enabled,
            Mode = Mode,
            AllowedCameraBindingIds = AllowedCameraBindingIds.ToList(),
            AllowedModelIds = AllowedModelIds.ToList(),
            AllowedTemplateIds = AllowedTemplateIds.ToList(),
            AllowedFlowIds = AllowedFlowIds.ToList(),
            AllowedResourceRoots = AllowedResourceRoots.ToList(),
            MaxPreviewArtifacts = MaxPreviewArtifacts,
            MaxMetadataBytes = MaxMetadataBytes,
            FallbackToOffline = FallbackToOffline,
            DenyExternalPath = DenyExternalPath,
            DenyImageBytes = DenyImageBytes
        };
        clone.Normalize();
        return clone;
    }

    public static string NormalizeMode(string? mode)
    {
        return string.Equals(mode?.Trim(), ModeMetadataOnly, StringComparison.OrdinalIgnoreCase)
            ? ModeMetadataOnly
            : ModeMetadataOnly;
    }

    public static List<string> NormalizeAllowlist(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return new List<string>();
        }

        return values
            .Select(NormalizeResourceKey)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static string? NormalizeResourceKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (IsWildcard(trimmed) || LooksUnsafeResourceKey(trimmed))
        {
            return null;
        }

        return trimmed.ToLowerInvariant();
    }

    public static bool IsAllowedToken(string? value)
    {
        return NormalizeResourceKey(value) != null;
    }

    public static bool LooksUnsafeResourceKey(string value)
    {
        return value.Contains("..", StringComparison.Ordinal) ||
               value.Contains("*", StringComparison.Ordinal) ||
               value.Contains(":\\", StringComparison.Ordinal) ||
               value.Contains(":/", StringComparison.Ordinal) ||
               value.Contains("\\", StringComparison.Ordinal) ||
               value.Contains("/", StringComparison.Ordinal) ||
               value.Contains("base64", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("http", StringComparison.OrdinalIgnoreCase) ||
               RuntimePreviewPilotConfigRegexes.IpAddressLikeRegex.IsMatch(value);
    }

    private static bool IsWildcard(string value)
    {
        return value == "*" ||
               value.Equals("all", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("any", StringComparison.OrdinalIgnoreCase);
    }
}

file static class RuntimePreviewPilotConfigRegexes
{
    public static readonly Regex IpAddressLikeRegex = new(
        @"^(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

public static class RuntimePreviewPilotConfigValidator
{
    public static IReadOnlyList<string> Validate(RuntimePreviewPilotConfig? config)
    {
        if (config == null)
        {
            return ["RuntimePreviewPilot config is required."];
        }

        var failures = new List<string>();
        if (!string.Equals(config.Mode, RuntimePreviewPilotConfig.ModeMetadataOnly, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("RuntimePreviewPilot:Mode must be metadata_only.");
        }

        ValidateAllowlist(config.AllowedCameraBindingIds, "AllowedCameraBindingIds", failures);
        ValidateAllowlist(config.AllowedModelIds, "AllowedModelIds", failures);
        ValidateAllowlist(config.AllowedTemplateIds, "AllowedTemplateIds", failures);
        ValidateAllowlist(config.AllowedFlowIds, "AllowedFlowIds", failures);
        ValidateAllowlist(config.AllowedResourceRoots, "AllowedResourceRoots", failures);

        if (config.MaxPreviewArtifacts < 1)
        {
            failures.Add("RuntimePreviewPilot:MaxPreviewArtifacts must be positive.");
        }

        if (config.MaxMetadataBytes < 1)
        {
            failures.Add("RuntimePreviewPilot:MaxMetadataBytes must be positive.");
        }

        if (!config.DenyExternalPath)
        {
            failures.Add("RuntimePreviewPilot:DenyExternalPath must remain true for v0.8.");
        }

        if (!config.DenyImageBytes)
        {
            failures.Add("RuntimePreviewPilot:DenyImageBytes must remain true for v0.8.");
        }

        return failures;
    }

    private static void ValidateAllowlist(
        IEnumerable<string>? values,
        string fieldName,
        ICollection<string> failures)
    {
        foreach (var value in values ?? [])
        {
            if (!RuntimePreviewPilotConfig.IsAllowedToken(value))
            {
                failures.Add($"RuntimePreviewPilot:{fieldName} contains an unsafe or wildcard resource token.");
            }
        }
    }
}

public class SecurityConfig
{
    public int PasswordMinLength { get; set; } = 6;

    public int SessionTimeoutMinutes { get; set; } = 30;

    public int LoginFailureLockoutCount { get; set; } = 5;
}

public class CameraBindingConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string DisplayName { get; set; } = "Camera";

    public string SerialNumber { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = "Huaray";

    public string ModelName { get; set; } = string.Empty;

    public string InterfaceType { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public double ExposureTimeUs { get; set; } = 5000.0;

    public double GainDb { get; set; } = 1.0;

    public string PixelFormat { get; set; } = ClearVision.Product.Core.Cameras.CameraPixelFormatExtensions.DefaultPixelFormat;

    public string TriggerMode { get; set; } = "Software";

    public string HardwareTriggerSource { get; set; } = "Line0";

    public string SoftwareTriggerSource { get; set; } = "Manual";

    public int EnterPhotoelectricDebounceMs { get; set; } = 200;

    public int EnterPhotoelectricTimeoutMs { get; set; } = 30000;

    public bool IgnoreEnterTriggerWhileBusy { get; set; } = true;

    public string EnterPhotoelectricDeviceId { get; set; } = string.Empty;

    public string SerialPhotoelectricPortName { get; set; } = string.Empty;

    public int SerialPhotoelectricBaudRate { get; set; } = 9600;

    public int SerialPhotoelectricDebounceMs { get; set; } = 200;

    public int SerialPhotoelectricTimeoutMs { get; set; } = 30000;

    public bool IgnoreSerialPhotoelectricTriggerWhileBusy { get; set; } = true;

    public int TargetFrameRateFps { get; set; } = ClearVision.Product.Core.Cameras.CameraTriggerModeExtensions.DefaultTargetFrameRateFps;

    public ContinuousInspectionConfig ContinuousInspection { get; set; } = new();

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N")[..8] : Id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "Camera" : DisplayName.Trim();
        SerialNumber = SerialNumber?.Trim() ?? string.Empty;
        IpAddress = IpAddress?.Trim() ?? string.Empty;
        Manufacturer = string.IsNullOrWhiteSpace(Manufacturer) ? "Huaray" : Manufacturer.Trim();
        ModelName = ModelName?.Trim() ?? string.Empty;
        InterfaceType = InterfaceType?.Trim() ?? string.Empty;
        PixelFormat = ClearVision.Product.Core.Cameras.CameraPixelFormatExtensions.ToConfigValue(
            ClearVision.Product.Core.Cameras.CameraPixelFormatExtensions.Normalize(PixelFormat));
        TriggerMode = ClearVision.Product.Core.Cameras.CameraTriggerModeExtensions.ToConfigValue(
            ClearVision.Product.Core.Cameras.CameraTriggerModeExtensions.Normalize(TriggerMode));
        HardwareTriggerSource = ClearVision.Product.Core.Cameras.CameraHardwareTriggerSourceExtensions.Normalize(HardwareTriggerSource);
        SoftwareTriggerSource = ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.ToConfigValue(
            ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.Normalize(SoftwareTriggerSource));
        EnterPhotoelectricDebounceMs = ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.NormalizeEnterPhotoelectricDebounceMs(EnterPhotoelectricDebounceMs);
        EnterPhotoelectricTimeoutMs = ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.NormalizeEnterPhotoelectricTimeoutMs(EnterPhotoelectricTimeoutMs);
        EnterPhotoelectricDeviceId = EnterPhotoelectricDeviceId?.Trim() ?? string.Empty;
        SerialPhotoelectricPortName = SerialPhotoelectricPortName?.Trim() ?? string.Empty;
        SerialPhotoelectricBaudRate = ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricBaudRate(SerialPhotoelectricBaudRate);
        SerialPhotoelectricDebounceMs = ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricDebounceMs(SerialPhotoelectricDebounceMs);
        SerialPhotoelectricTimeoutMs = ClearVision.Product.Core.Cameras.CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricTimeoutMs(SerialPhotoelectricTimeoutMs);
        TargetFrameRateFps = ClearVision.Product.Core.Cameras.CameraTriggerModeExtensions.NormalizeTargetFrameRate(TargetFrameRateFps);
        ContinuousInspection ??= new ContinuousInspectionConfig();
        ContinuousInspection.Normalize();
    }
}
