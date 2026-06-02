using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Desktop.Station;

public enum StationCommunicationMode
{
    Disabled = 0,
    LocalLoopback = 1,
    LanController = 2
}

public sealed class StationCommunicationSettingsStore
{
    private const int GeneratedTokenUpperBound = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static StationCommunicationSettingsStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public StationCommunicationSettingsStore()
        : this(null)
    {
    }

    public StationCommunicationSettingsStore(string? localAppDataRoot)
        : this(
            StationSettingsPaths.GetStudioCommunicationSettingsPath(localAppDataRoot),
            StationSettingsPaths.GetStationSyncSettingsPath(localAppDataRoot))
    {
    }

    public StationCommunicationSettingsStore(string studioSettingsPath, string stationSyncSettingsPath)
    {
        StudioSettingsPath = studioSettingsPath;
        StationSyncSettingsPath = stationSyncSettingsPath;
        StationSyncAppliedMarkerPath = Path.Combine(
            Path.GetDirectoryName(stationSyncSettingsPath) ?? string.Empty,
            StationSettingsPaths.StationSyncSettingsAppliedMarkerFileName);
    }

    public string StudioSettingsPath { get; }

    public string StationSyncSettingsPath { get; }

    public string StationSyncAppliedMarkerPath { get; }

    public StationCommunicationSettingsView GetSettings(StationIngressOptions runningIngress)
    {
        var snapshot = ReadSnapshot(runningIngress);
        return BuildView(snapshot, runningIngress, null, null, "Station communication settings loaded.");
    }

    public StationCommunicationSaveResult SaveSettings(
        StationCommunicationSettingsUpdateRequest request,
        StationIngressOptions runningIngress)
    {
        var snapshot = ReadSnapshot(runningIngress);
        if (!TryBuildTarget(request, snapshot, out var target, out var errors))
        {
            return StationCommunicationSaveResult.Failed("Station communication settings are invalid.", errors);
        }

        var requiresStudioRestart = !AreIngressOptionsEquivalent(target.Ingress, runningIngress);
        var requiresLocalStationRestart = !AreStationSyncOptionsEquivalent(
            target.StationSync,
            snapshot.StationSync ?? new LocalStationSyncOptions());

        WriteStudioDocument(target.StudioDocument);
        WriteStationSyncDocument(target.StationSyncDocument);

        var savedSnapshot = ReadSnapshot(runningIngress);
        var view = BuildView(
            savedSnapshot,
            runningIngress,
            requiresStudioRestart,
            requiresLocalStationRestart,
            "Station communication settings saved. Restart Studio or local Station where indicated.");
        return StationCommunicationSaveResult.Succeeded(view);
    }

    public StationCommunicationTokenResult RevealToken(StationIngressOptions runningIngress)
    {
        var snapshot = ReadSnapshot(runningIngress);
        var token = ResolveToken(snapshot);
        return new StationCommunicationTokenResult
        {
            Success = true,
            Operation = "reveal",
            Token = token,
            TokenInfo = BuildTokenInfo(token),
            Settings = BuildView(snapshot, runningIngress, null, null, "Station token revealed.")
        };
    }

    public StationCommunicationTokenResult RegenerateToken(StationIngressOptions runningIngress)
    {
        var snapshot = ReadSnapshot(runningIngress);
        var generatedToken = GenerateToken(ResolveToken(snapshot));
        var mode = InferMode(snapshot.Ingress, snapshot.Metadata);
        var request = new StationCommunicationSettingsUpdateRequest
        {
            Mode = mode.ToString(),
            Port = snapshot.Ingress.Port,
            LanHost = snapshot.Metadata.LanHost,
            LocalStationSyncEnabled = snapshot.StationSync?.Enabled ?? mode != StationCommunicationMode.Disabled,
            SharedToken = generatedToken
        };

        var saveResult = SaveSettings(request, runningIngress);
        return new StationCommunicationTokenResult
        {
            Success = saveResult.Success,
            Operation = "regenerate",
            Token = generatedToken,
            TokenInfo = BuildTokenInfo(generatedToken),
            Settings = saveResult.Settings,
            Message = saveResult.Message,
            Errors = saveResult.Errors
        };
    }

    private PersistedStationCommunicationSnapshot ReadSnapshot(StationIngressOptions runningIngress)
    {
        var studioDocument = ReadStudioDocument();
        var stationSyncDocument = ReadStationSyncDocument();
        var ingress = CloneIngress(studioDocument.StationIngress ?? runningIngress);
        var metadata = studioDocument.StationCommunication ?? BuildMetadata(InferMode(ingress, null), null, null);
        var stationSync = stationSyncDocument.StationSync == null
            ? null
            : CloneStationSync(stationSyncDocument.StationSync);

        return new PersistedStationCommunicationSnapshot(studioDocument, stationSyncDocument, metadata, ingress, stationSync);
    }

    private bool TryBuildTarget(
        StationCommunicationSettingsUpdateRequest request,
        PersistedStationCommunicationSnapshot snapshot,
        out StationCommunicationTarget target,
        out IReadOnlyList<StationCommunicationValidationError> errors)
    {
        var validationErrors = new List<StationCommunicationValidationError>();
        target = default!;

        if (!TryParseMode(request.Mode, out var mode))
        {
            validationErrors.Add(new StationCommunicationValidationError("mode", "Mode must be Disabled, LocalLoopback, or LanController."));
        }

        var requestedPort = request.Port ?? snapshot.Ingress.Port;
        if (requestedPort is < 1 or > 65535)
        {
            validationErrors.Add(new StationCommunicationValidationError("port", "Port must be between 1 and 65535."));
        }

        if (validationErrors.Count > 0)
        {
            errors = validationErrors;
            return false;
        }

        var port = requestedPort <= 0 ? 5000 : requestedPort;
        if (!TryNormalizeLanHost(request.LanHost, snapshot.Metadata.LanHost, out var lanHost, out var lanHostError))
        {
            errors = new[]
            {
                new StationCommunicationValidationError("lanHost", lanHostError)
            };
            return false;
        }
        var localStationSyncEnabled = mode != StationCommunicationMode.Disabled &&
            (request.LocalStationSyncEnabled ?? snapshot.StationSync?.Enabled ?? true);
        var token = !string.IsNullOrWhiteSpace(request.SharedToken)
            ? request.SharedToken.Trim()
            : ResolveToken(snapshot);

        if (mode != StationCommunicationMode.Disabled && string.IsNullOrWhiteSpace(token))
        {
            token = GenerateToken();
        }

        var ingress = CloneIngress(snapshot.Ingress);
        ingress.Enabled = mode != StationCommunicationMode.Disabled;
        ingress.ListenMode = mode == StationCommunicationMode.LanController
            ? StationIngressListenMode.Lan
            : StationIngressListenMode.Loopback;
        ingress.Port = port;
        ingress.SharedToken = token;
        ingress.AllowInsecureDevelopment = false;

        var stationSync = CloneStationSync(snapshot.StationSync ?? new LocalStationSyncOptions());
        stationSync.Enabled = localStationSyncEnabled;
        stationSync.StudioBaseUrl = mode == StationCommunicationMode.Disabled
            ? string.Empty
            : $"http://127.0.0.1:{port}";
        stationSync.StudioHubUrl = string.Empty;
        stationSync.SharedToken = token;

        var metadata = BuildMetadata(mode, lanHost, localStationSyncEnabled);
        var studioDocument = new StudioStationCommunicationSettingsDocument
        {
            StationCommunication = metadata,
            StationIngress = ingress
        };
        var stationSyncDocument = new StationSyncSettingsDocument
        {
            StationSync = stationSync
        };

        target = new StationCommunicationTarget(studioDocument, stationSyncDocument, ingress, stationSync);
        errors = Array.Empty<StationCommunicationValidationError>();
        return true;
    }

    private StationCommunicationSettingsView BuildView(
        PersistedStationCommunicationSnapshot snapshot,
        StationIngressOptions runningIngress,
        bool? requiresStudioRestart,
        bool? requiresLocalStationRestart,
        string message)
    {
        var mode = InferMode(snapshot.Ingress, snapshot.Metadata);
        var port = snapshot.Ingress.Port <= 0 ? 5000 : snapshot.Ingress.Port;
        var lanAddresses = DiscoverLanAddresses();
        var lanHost = NormalizeLanHost(snapshot.Metadata.LanHost, lanAddresses.FirstOrDefault());
        var localStationSyncEnabled = snapshot.StationSync?.Enabled ?? mode != StationCommunicationMode.Disabled;
        var token = ResolveToken(snapshot);
        var remoteBaseUrl = mode == StationCommunicationMode.LanController
            ? $"http://{FormatHostForUrl(lanHost)}:{port}"
            : string.Empty;
        var localBaseUrl = mode == StationCommunicationMode.Disabled
            ? string.Empty
            : $"http://127.0.0.1:{port}";
        var studioRestart = requiresStudioRestart ?? !AreIngressOptionsEquivalent(snapshot.Ingress, runningIngress);
        var stationRestart = requiresLocalStationRestart ?? IsStationSyncRestartRequired();

        return new StationCommunicationSettingsView
        {
            Success = true,
            Message = message,
            Mode = mode.ToString(),
            Port = port,
            LanHost = lanHost,
            LanAddresses = lanAddresses,
            LocalStationSyncEnabled = localStationSyncEnabled,
            Token = BuildTokenInfo(token),
            Paths = new StationCommunicationPathView
            {
                Studio = StudioSettingsPath,
                LocalStation = StationSyncSettingsPath
            },
            CurrentRunning = new StationCommunicationRunningView
            {
                StudioEnabled = runningIngress.Enabled,
                StudioListenMode = runningIngress.ListenMode.ToString(),
                StudioPort = runningIngress.Port,
                StudioToken = BuildTokenInfo(runningIngress.SharedToken)
            },
            RequiresRestart = new StationCommunicationRestartView
            {
                Studio = studioRestart,
                LocalStation = stationRestart
            },
            LocalStationBaseUrl = localBaseUrl,
            RemoteStationBaseUrl = remoteBaseUrl,
            RemoteStationHubUrl = string.IsNullOrWhiteSpace(remoteBaseUrl)
                ? string.Empty
                : remoteBaseUrl.TrimEnd('/') + StationSyncContractDefaults.HubPath,
            LocalStationHubUrl = string.IsNullOrWhiteSpace(localBaseUrl)
                ? string.Empty
                : localBaseUrl.TrimEnd('/') + StationSyncContractDefaults.HubPath,
            Diagnostics = BuildDiagnostics(mode, token, studioRestart, stationRestart, remoteBaseUrl)
        };
    }

    private StudioStationCommunicationSettingsDocument ReadStudioDocument()
    {
        if (!File.Exists(StudioSettingsPath))
        {
            return new StudioStationCommunicationSettingsDocument();
        }

        try
        {
            var json = File.ReadAllText(StudioSettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<StudioStationCommunicationSettingsDocument>(json, JsonOptions)
                ?? new StudioStationCommunicationSettingsDocument();
        }
        catch
        {
            return new StudioStationCommunicationSettingsDocument();
        }
    }

    private StationSyncSettingsDocument ReadStationSyncDocument()
    {
        if (!File.Exists(StationSyncSettingsPath))
        {
            return new StationSyncSettingsDocument();
        }

        try
        {
            var json = File.ReadAllText(StationSyncSettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<StationSyncSettingsDocument>(json, JsonOptions)
                ?? new StationSyncSettingsDocument();
        }
        catch
        {
            return new StationSyncSettingsDocument();
        }
    }

    private void WriteStudioDocument(StudioStationCommunicationSettingsDocument document)
    {
        WriteJsonFile(StudioSettingsPath, document);
    }

    private void WriteStationSyncDocument(StationSyncSettingsDocument document)
    {
        WriteJsonFile(StationSyncSettingsPath, document);
    }

    private static void WriteJsonFile<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        File.Move(tempPath, path, true);
    }

    private bool IsStationSyncRestartRequired()
    {
        if (!File.Exists(StationSyncSettingsPath))
        {
            return false;
        }

        if (!File.Exists(StationSyncAppliedMarkerPath))
        {
            return true;
        }

        try
        {
            return File.GetLastWriteTimeUtc(StationSyncSettingsPath) >
                File.GetLastWriteTimeUtc(StationSyncAppliedMarkerPath);
        }
        catch
        {
            return true;
        }
    }

    private static StationCommunicationMetadata BuildMetadata(
        StationCommunicationMode mode,
        string? lanHost,
        bool? localStationSyncEnabled)
    {
        return new StationCommunicationMetadata
        {
            Mode = mode,
            LanHost = NormalizeLanHost(lanHost, null),
            LocalStationSyncEnabled = localStationSyncEnabled ?? mode != StationCommunicationMode.Disabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool TryParseMode(string? value, out StationCommunicationMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = StationCommunicationMode.Disabled;
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out mode) &&
            Enum.IsDefined(typeof(StationCommunicationMode), mode);
    }

    private static StationCommunicationMode InferMode(
        StationIngressOptions ingress,
        StationCommunicationMetadata? metadata)
    {
        if (!ingress.Enabled)
        {
            return StationCommunicationMode.Disabled;
        }

        if (ingress.ListenMode == StationIngressListenMode.Lan)
        {
            return StationCommunicationMode.LanController;
        }

        if (metadata?.Mode == StationCommunicationMode.LanController)
        {
            return StationCommunicationMode.LanController;
        }

        return StationCommunicationMode.LocalLoopback;
    }

    private static string ResolveToken(PersistedStationCommunicationSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Ingress.SharedToken))
        {
            return snapshot.Ingress.SharedToken.Trim();
        }

        return snapshot.StationSync?.SharedToken?.Trim() ?? string.Empty;
    }

    private static StationCommunicationTokenView BuildTokenInfo(string? token)
    {
        token = token?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new StationCommunicationTokenView
            {
                HasToken = false,
                Mask = string.Empty,
                Last4 = string.Empty
            };
        }

        var last4 = token.Length <= 4 ? token : token[^4..];
        return new StationCommunicationTokenView
        {
            HasToken = true,
            Mask = "****" + last4,
            Last4 = last4
        };
    }

    private static string GenerateToken(string? excludedToken = null)
    {
        excludedToken = excludedToken?.Trim();
        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            var token = FormatToken(RandomNumberGenerator.GetInt32(GeneratedTokenUpperBound));
            if (!string.Equals(token, excludedToken, StringComparison.Ordinal))
            {
                return token;
            }
        }

        if (int.TryParse(excludedToken, NumberStyles.None, CultureInfo.InvariantCulture, out var excludedValue) &&
            excludedValue is >= 0 and < GeneratedTokenUpperBound)
        {
            return FormatToken((excludedValue + 1) % GeneratedTokenUpperBound);
        }

        return FormatToken(RandomNumberGenerator.GetInt32(GeneratedTokenUpperBound));
    }

    private static string FormatToken(int value)
    {
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static bool TryNormalizeLanHost(
        string? value,
        string? fallback,
        out string lanHost,
        out string errorMessage)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            lanHost = ResolveMachineHostName();
            errorMessage = string.Empty;
            return true;
        }

        candidate = candidate.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out _) ||
            candidate.Any(char.IsWhiteSpace) ||
            candidate.IndexOfAny(new[] { '/', '\\', '?', '#', '@' }) >= 0)
        {
            lanHost = string.Empty;
            errorMessage = "LAN host must be a host name or IP address without scheme, path, or spaces.";
            return false;
        }

        var unbracketed = candidate;
        if (candidate.StartsWith("[", StringComparison.Ordinal) &&
            candidate.EndsWith("]", StringComparison.Ordinal) &&
            candidate.Length > 2)
        {
            unbracketed = candidate[1..^1];
        }

        if (IPAddress.TryParse(unbracketed, out var address))
        {
            lanHost = address.ToString();
            errorMessage = string.Empty;
            return true;
        }

        if (Uri.CheckHostName(candidate) == UriHostNameType.Dns)
        {
            lanHost = candidate;
            errorMessage = string.Empty;
            return true;
        }

        lanHost = string.Empty;
        errorMessage = "LAN host must be a valid host name or IP address.";
        return false;
    }

    private static string NormalizeLanHost(string? value, string? fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate.Trim();
        }

        return ResolveMachineHostName();
    }

    private static string ResolveMachineHostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static string FormatHostForUrl(string host)
    {
        return host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
    }

    private static IReadOnlyList<string> DiscoverLanAddresses()
    {
        try
        {
            var host = Dns.GetHostName();
            return Dns.GetHostEntry(host)
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> BuildDiagnostics(
        StationCommunicationMode mode,
        string token,
        bool requiresStudioRestart,
        bool requiresLocalStationRestart,
        string remoteBaseUrl)
    {
        var diagnostics = new List<string>();
        if (mode == StationCommunicationMode.Disabled)
        {
            diagnostics.Add("Station 通讯已关闭：本机 Studio 不接收 Station 注册，本机 Station 也不会主动同步。");
        }
        else
        {
            diagnostics.Add(requiresStudioRestart
                ? "需要重启本机 Studio：保存的监听模式、端口或 token 尚未被当前 Studio 进程读取。"
                : "本机 Studio 已按当前保存的监听模式、端口和 token 运行。");
            diagnostics.Add(requiresLocalStationRestart
                ? "需要重启本机 Station：保存的本机 Station 同步地址或 token 尚未被本机 Station 读取。"
                : "本机 Station 配置文件已被最近一次本机 Station 启动读取。");
        }

        if (mode == StationCommunicationMode.Disabled && (requiresStudioRestart || requiresLocalStationRestart))
        {
            diagnostics.Add("要完全停止通讯，请按上面的提示重启对应进程。");
        }

        if (mode == StationCommunicationMode.LanController)
        {
            diagnostics.Add(string.IsNullOrWhiteSpace(remoteBaseUrl)
                ? "局域网总控模式需要填写一个其他电脑能访问到的本机局域网 IP。"
                : $"另一台电脑的 Station 应填写 StudioBaseUrl={remoteBaseUrl}，并使用同一个 token。");
        }

        if (mode != StationCommunicationMode.Disabled && string.IsNullOrWhiteSpace(token))
        {
            diagnostics.Add("必须先生成共享 token，Station 才能注册到 Studio。");
        }

        if (!requiresStudioRestart && !requiresLocalStationRestart)
        {
            diagnostics.Add("当前页面保存值与本机已知运行值一致；这不代表远端 Station 已连接成功。");
        }

        return diagnostics;
    }

    private static bool AreIngressOptionsEquivalent(StationIngressOptions left, StationIngressOptions right)
    {
        return left.Enabled == right.Enabled &&
            left.ListenMode == right.ListenMode &&
            left.Port == right.Port &&
            string.Equals(left.SharedToken ?? string.Empty, right.SharedToken ?? string.Empty, StringComparison.Ordinal) &&
            left.AllowInsecureDevelopment == right.AllowInsecureDevelopment;
    }

    private static bool AreStationSyncOptionsEquivalent(LocalStationSyncOptions left, LocalStationSyncOptions right)
    {
        return left.Enabled == right.Enabled &&
            string.Equals(left.StudioBaseUrl ?? string.Empty, right.StudioBaseUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.StudioHubUrl ?? string.Empty, right.StudioHubUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.SharedToken ?? string.Empty, right.SharedToken ?? string.Empty, StringComparison.Ordinal);
    }

    private static StationIngressOptions CloneIngress(StationIngressOptions source)
    {
        return new StationIngressOptions
        {
            Enabled = source.Enabled,
            ListenMode = source.ListenMode,
            Port = source.Port,
            SharedToken = source.SharedToken ?? string.Empty,
            AllowInsecureDevelopment = source.AllowInsecureDevelopment,
            AllowMessagePack = source.AllowMessagePack,
            OfflineThresholdSeconds = source.OfflineThresholdSeconds,
            ResultBufferPerStation = source.ResultBufferPerStation,
            EventBufferSize = source.EventBufferSize,
            HealthBufferPerStation = source.HealthBufferPerStation,
            LogBufferPerStation = source.LogBufferPerStation,
            CommandBufferPerStation = source.CommandBufferPerStation
        };
    }

    private static LocalStationSyncOptions CloneStationSync(LocalStationSyncOptions source)
    {
        return new LocalStationSyncOptions
        {
            Enabled = source.Enabled,
            StudioBaseUrl = source.StudioBaseUrl ?? string.Empty,
            StudioHubUrl = source.StudioHubUrl ?? string.Empty,
            SharedToken = source.SharedToken ?? string.Empty,
            ExtensionData = CloneExtensionData(source.ExtensionData)
        };
    }

    private static Dictionary<string, JsonElement>? CloneExtensionData(Dictionary<string, JsonElement>? source)
    {
        if (source == null)
        {
            return null;
        }

        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PersistedStationCommunicationSnapshot(
        StudioStationCommunicationSettingsDocument StudioDocument,
        StationSyncSettingsDocument StationSyncDocument,
        StationCommunicationMetadata Metadata,
        StationIngressOptions Ingress,
        LocalStationSyncOptions? StationSync);

    private sealed record StationCommunicationTarget(
        StudioStationCommunicationSettingsDocument StudioDocument,
        StationSyncSettingsDocument StationSyncDocument,
        StationIngressOptions Ingress,
        LocalStationSyncOptions StationSync);
}

public sealed class StudioStationCommunicationSettingsDocument
{
    public StationCommunicationMetadata? StationCommunication { get; set; }

    public StationIngressOptions? StationIngress { get; set; }
}

public sealed class StationCommunicationMetadata
{
    public StationCommunicationMode Mode { get; set; } = StationCommunicationMode.Disabled;

    public string LanHost { get; set; } = string.Empty;

    public bool LocalStationSyncEnabled { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StationSyncSettingsDocument
{
    public LocalStationSyncOptions? StationSync { get; set; }
}

public sealed class LocalStationSyncOptions
{
    public bool Enabled { get; set; }

    public string StudioBaseUrl { get; set; } = string.Empty;

    public string StudioHubUrl { get; set; } = string.Empty;

    public string SharedToken { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class StationCommunicationSettingsUpdateRequest
{
    public string? Mode { get; set; }

    public int? Port { get; set; }

    public string? LanHost { get; set; }

    public bool? LocalStationSyncEnabled { get; set; }

    public string? SharedToken { get; set; }
}

public sealed class StationCommunicationTokenRequest
{
    public string? Operation { get; set; }

    public string? Action { get; set; }
}

public sealed class StationCommunicationSettingsView
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string Mode { get; set; } = StationCommunicationMode.Disabled.ToString();

    public int Port { get; set; }

    public string LanHost { get; set; } = string.Empty;

    public IReadOnlyList<string> LanAddresses { get; set; } = Array.Empty<string>();

    public bool LocalStationSyncEnabled { get; set; }

    public StationCommunicationTokenView Token { get; set; } = new();

    public StationCommunicationPathView Paths { get; set; } = new();

    public StationCommunicationRunningView CurrentRunning { get; set; } = new();

    public StationCommunicationRestartView RequiresRestart { get; set; } = new();

    public string LocalStationBaseUrl { get; set; } = string.Empty;

    public string RemoteStationBaseUrl { get; set; } = string.Empty;

    public string LocalStationHubUrl { get; set; } = string.Empty;

    public string RemoteStationHubUrl { get; set; } = string.Empty;

    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class StationCommunicationTokenView
{
    public bool HasToken { get; set; }

    public string Mask { get; set; } = string.Empty;

    public string Last4 { get; set; } = string.Empty;
}

public sealed class StationCommunicationPathView
{
    public string Studio { get; set; } = string.Empty;

    public string LocalStation { get; set; } = string.Empty;
}

public sealed class StationCommunicationRunningView
{
    public bool StudioEnabled { get; set; }

    public string StudioListenMode { get; set; } = string.Empty;

    public int StudioPort { get; set; }

    public StationCommunicationTokenView StudioToken { get; set; } = new();
}

public sealed class StationCommunicationRestartView
{
    public bool Studio { get; set; }

    public bool LocalStation { get; set; }
}

public sealed class StationCommunicationValidationError
{
    public StationCommunicationValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; }

    public string Message { get; }
}

public sealed class StationCommunicationSaveResult
{
    public bool Success { get; private init; }

    public string Message { get; private init; } = string.Empty;

    public StationCommunicationSettingsView? Settings { get; private init; }

    public IReadOnlyList<StationCommunicationValidationError> Errors { get; private init; } =
        Array.Empty<StationCommunicationValidationError>();

    public static StationCommunicationSaveResult Succeeded(StationCommunicationSettingsView settings)
    {
        return new StationCommunicationSaveResult
        {
            Success = true,
            Message = settings.Message,
            Settings = settings
        };
    }

    public static StationCommunicationSaveResult Failed(
        string message,
        IReadOnlyList<StationCommunicationValidationError> errors)
    {
        return new StationCommunicationSaveResult
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}

public sealed class StationCommunicationTokenResult
{
    public bool Success { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public StationCommunicationTokenView TokenInfo { get; set; } = new();

    public StationCommunicationSettingsView? Settings { get; set; }

    public string Message { get; set; } = string.Empty;

    public IReadOnlyList<StationCommunicationValidationError> Errors { get; set; } =
        Array.Empty<StationCommunicationValidationError>();
}
