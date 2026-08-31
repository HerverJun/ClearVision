using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.PlcComm;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Station;

public sealed class StationHardwareSettingsService
{
    private static readonly Regex SerialPortNameRegex = new(@"^COM\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions CloneJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfigurationService _configurationService;
    private readonly ICameraManager _cameraManager;
    private readonly ILogger<StationHardwareSettingsService> _logger;
    private readonly Func<string, ILogger, CancellationToken, Task<bool>> _plcConnectionProbe;
    private readonly SemaphoreSlim _cameraOperationGate = new(1, 1);

    public StationHardwareSettingsService(
        IConfigurationService configurationService,
        ICameraManager cameraManager,
        ILogger<StationHardwareSettingsService> logger)
        : this(configurationService, cameraManager, logger, ProbePlcConnectionAsync)
    {
    }

    internal StationHardwareSettingsService(
        IConfigurationService configurationService,
        ICameraManager cameraManager,
        ILogger<StationHardwareSettingsService> logger,
        Func<string, ILogger, CancellationToken, Task<bool>> plcConnectionProbe)
    {
        _configurationService = configurationService;
        _cameraManager = cameraManager;
        _logger = logger;
        _plcConnectionProbe = plcConnectionProbe ?? throw new ArgumentNullException(nameof(plcConnectionProbe));
    }

    public async Task<StationHardwareSettingsSnapshot> LoadAsync()
    {
        var config = await _configurationService.LoadAsync();
        config.Normalize();
        return new StationHardwareSettingsSnapshot(
            CloneCameraBindings(config.Cameras),
            config.ActiveCameraId,
            CloneCommunication(config.Communication),
            config.Revision);
    }

    public async Task ApplyCurrentAsync()
    {
        var snapshot = await LoadAsync();
        _cameraManager.LoadBindings(snapshot.Cameras, snapshot.ActiveCameraId);
        PlcCommunicationOperatorBase.InvalidateGlobalConfigurationCache();
        _logger.LogInformation(
            "Station hardware settings applied. Cameras={CameraCount}, ActiveCameraId={ActiveCameraId}, PlcProtocol={PlcProtocol}",
            snapshot.Cameras.Count,
            snapshot.ActiveCameraId,
            snapshot.Communication.ActiveProtocol);
    }

    public async Task<StationHardwareSettingsSnapshot> SaveCameraBindingsAsync(
        IEnumerable<CameraBindingConfig> cameraBindings,
        string? activeCameraId,
        long expectedRevision)
    {
        var normalizedBindings = NormalizeCameraBindings(cameraBindings).ToList();
        ValidateCameraBindings(normalizedBindings);
        foreach (var binding in normalizedBindings)
        {
            binding.Normalize();
        }

        var normalizedActiveCameraId = NormalizeActiveCameraId(normalizedBindings, activeCameraId);

        await _cameraOperationGate.WaitAsync();
        AppConfigMutationResult mutation;
        try
        {
            mutation = await _configurationService.MutateAndApplyAsync(
                expectedRevision,
                candidate =>
                {
                    candidate.Cameras = CloneCameraBindings(normalizedBindings);
                    candidate.ActiveCameraId = normalizedActiveCameraId;
                },
                validate: null,
                async (candidate, _) =>
                    await _cameraManager.ApplyBindingsAsync(CloneCameraBindings(candidate.Cameras), candidate.ActiveCameraId),
                async (previous, _) =>
                    await _cameraManager.ApplyBindingsAsync(CloneCameraBindings(previous.Cameras), previous.ActiveCameraId));
        }
        finally
        {
            _cameraOperationGate.Release();
        }

        EnsureMutationSucceeded(mutation);
        var config = mutation.Config!;
        return new StationHardwareSettingsSnapshot(
            CloneCameraBindings(config.Cameras),
            config.ActiveCameraId,
            CloneCommunication(config.Communication),
            config.Revision);
    }

    public async Task<StationHardwareSettingsSnapshot> SavePlcSettingsAsync(
        CommunicationConfig communication,
        long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(communication);
        communication.Normalize();

        var mutation = await _configurationService.MutateAndApplyAsync(
            expectedRevision,
            candidate => candidate.Communication = CloneCommunication(communication),
            validate: null,
            async (_, _) =>
            {
                await PlcCommunicationOperatorBase.ResetRuntimeConfigurationAsync();
                ModbusCommunicationOperator.ClearConnectionPool();
            },
            async (_, _) =>
            {
                await PlcCommunicationOperatorBase.ResetRuntimeConfigurationAsync();
                ModbusCommunicationOperator.ClearConnectionPool();
            });
        EnsureMutationSucceeded(mutation);
        var config = mutation.Config!;

        return new StationHardwareSettingsSnapshot(
            CloneCameraBindings(config.Cameras),
            config.ActiveCameraId,
            CloneCommunication(config.Communication),
            config.Revision);
    }

    public Task<IEnumerable<CameraInfo>> DiscoverCamerasAsync()
    {
        return _cameraManager.EnumerateCamerasAsync();
    }

    public async Task<StationHardwareTestResult> TestCameraAsync(
        CameraBindingConfig binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Normalize();

        try
        {
            var authoritativeBinding = _cameraManager.RequireEnabledBinding(binding.Id);
            if (!string.Equals(
                    authoritativeBinding.SerialNumber,
                    binding.SerialNumber,
                    StringComparison.OrdinalIgnoreCase))
            {
                return StationHardwareTestResult.Fail("相机绑定与当前服务端配置不一致。");
            }

            await using var cameraLease = await _cameraManager.AcquireByBindingLeaseAsync(
                authoritativeBinding.Id,
                cancellationToken);
            var camera = cameraLease.Camera;
            await camera.SetExposureTimeAsync(authoritativeBinding.ExposureTimeUs);
            await camera.SetGainAsync(authoritativeBinding.GainDb);

            return camera.IsConnected
                ? StationHardwareTestResult.Ok($"相机已连接：{camera.Name}")
                : StationHardwareTestResult.Fail("相机对象已创建，但当前未连接。");
        }
        catch (Exception ex)
        {
            return StationHardwareTestResult.Fail($"相机连接失败：{ex.Message}");
        }
    }

    public async Task<StationHardwareTestResult> TestPlcConnectionAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var requestedProfileId = (profileId ?? string.Empty).Trim();
        var protocol = CommunicationConfig.NormalizeProtocolKey(requestedProfileId);
        if (string.IsNullOrWhiteSpace(requestedProfileId) ||
            !requestedProfileId.Equals(protocol, StringComparison.OrdinalIgnoreCase))
        {
            return StationHardwareTestResult.Fail("PLC ProfileId 不存在或不受支持。");
        }

        var read = await _configurationService.ReadAsync(cancellationToken);
        if (!read.IsHealthy || read.Config == null)
        {
            return StationHardwareTestResult.Fail("无法读取已保存的 PLC Profile。");
        }

        var config = read.Config;
        config.Normalize();
        var profile = config.Communication.GetProfile(protocol);
        if (string.IsNullOrWhiteSpace(profile.IpAddress))
        {
            return StationHardwareTestResult.Fail("PLC IP 地址不能为空。");
        }

        if (profile.Port is < 1 or > 65535)
        {
            return StationHardwareTestResult.Fail("PLC 端口必须在 1-65535 之间。");
        }

        if (!TryBuildConnectionString(protocol, profile, out var connectionString, out var errorMessage))
        {
            return StationHardwareTestResult.Fail(errorMessage);
        }

        try
        {
            var pingOk = await _plcConnectionProbe(connectionString, _logger, cancellationToken);

            return pingOk
                ? StationHardwareTestResult.Ok("PLC 连接成功。")
                : StationHardwareTestResult.Fail("PLC 连接失败。");
        }
        catch (SocketException ex)
        {
            return StationHardwareTestResult.Fail($"PLC 连接失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            return StationHardwareTestResult.Fail($"PLC 测试失败：{ex.Message}");
        }
    }

    private static async Task<bool> ProbePlcConnectionAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var client = PlcClientFactory.CreateFromConnectionString(connectionString, logger);
        var connected = await client.ConnectAsync(cancellationToken);
        var pingOk = connected && await client.PingAsync(cancellationToken);
        if (connected)
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch
            {
                // Test already finished; ignore disconnect cleanup failures.
            }
        }

        return pingOk;
    }

    private static bool TryBuildConnectionString(
        string protocol,
        PlcCommunicationProfile profile,
        out string connectionString,
        out string errorMessage)
    {
        connectionString = string.Empty;
        errorMessage = string.Empty;
        var ipAddress = (profile.IpAddress ?? string.Empty).Trim();
        var port = profile.Port;

        switch (CommunicationConfig.NormalizeProtocolKey(protocol))
        {
            case CommunicationConfig.ProtocolS7:
            {
                var s7 = profile as S7CommunicationProfile ?? new S7CommunicationProfile();
                var rack = s7.Rack;
                var slot = s7.Slot;
                if (rack is < 0 or > 15)
                {
                    errorMessage = "Rack 必须在 0-15 之间。";
                    return false;
                }

                if (slot is < 0 or > 15)
                {
                    errorMessage = "Slot 必须在 0-15 之间。";
                    return false;
                }

                var cpuType = string.IsNullOrWhiteSpace(s7.CpuType) ? "S7-1200" : s7.CpuType.Trim();
                connectionString = $"S7://{ipAddress}:{port}?cpu={Uri.EscapeDataString(cpuType)}&rack={rack}&slot={slot}";
                return true;
            }
            case CommunicationConfig.ProtocolMc:
                connectionString = $"MC://{ipAddress}:{port}";
                return true;
            case CommunicationConfig.ProtocolFins:
                connectionString = $"FINS://{ipAddress}:{port}";
                return true;
            default:
                errorMessage = "仅支持 S7、MC、FINS 协议。";
                return false;
        }
    }

    private static IEnumerable<CameraBindingConfig> NormalizeCameraBindings(IEnumerable<CameraBindingConfig>? cameraBindings)
    {
        foreach (var binding in cameraBindings ?? Enumerable.Empty<CameraBindingConfig>())
        {
            if (binding == null)
            {
                continue;
            }

            var clone = CloneCameraBinding(binding);
            clone.Id = clone.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(clone.Id))
            {
                continue;
            }

            yield return clone;
        }
    }

    private static string NormalizeActiveCameraId(IReadOnlyCollection<CameraBindingConfig> bindings, string? activeCameraId)
    {
        var candidate = (activeCameraId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(candidate) &&
            bindings.Any(binding => binding.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return candidate;
        }

        return bindings.FirstOrDefault(binding => binding.IsEnabled)?.Id
            ?? bindings.FirstOrDefault()?.Id
            ?? string.Empty;
    }

    private static void ValidateCameraBindings(IReadOnlyCollection<CameraBindingConfig> bindings)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            if (!ids.Add(binding.Id))
            {
                throw new InvalidOperationException($"相机绑定 ID 重复：{binding.Id}");
            }

            if (binding.ExposureTimeUs is < 10 or > 1_000_000)
            {
                throw new InvalidOperationException($"相机“{binding.DisplayName}”曝光时间必须在 10 - 1000000 us 范围内。");
            }

            if (binding.GainDb is < 0 or > 24)
            {
                throw new InvalidOperationException($"相机“{binding.DisplayName}”增益必须在 0 - 24 dB 范围内。");
            }

            var triggerMode = CameraTriggerModeExtensions.Normalize(binding.TriggerMode);
            if (triggerMode != CameraTriggerMode.Software)
            {
                if (binding.TargetFrameRateFps is < CameraTriggerModeExtensions.MinTargetFrameRateFps
                    or > CameraTriggerModeExtensions.MaxTargetFrameRateFps)
                {
                    throw new InvalidOperationException($"相机“{binding.DisplayName}”采集帧率必须在 1 - 120 fps 范围内。");
                }
            }

            var softwareSource = CameraSoftwareTriggerSourceExtensions.Normalize(binding.SoftwareTriggerSource);
            if (triggerMode == CameraTriggerMode.Software &&
                softwareSource == CameraSoftwareTriggerSource.SerialPhotoelectric &&
                !SerialPortNameRegex.IsMatch((binding.SerialPhotoelectricPortName ?? string.Empty).Trim()))
            {
                throw new InvalidOperationException($"相机“{binding.DisplayName}”使用串口光电触发时，串口号必须类似 COM3。");
            }
        }
    }

    private static List<CameraBindingConfig> CloneCameraBindings(IEnumerable<CameraBindingConfig>? bindings)
    {
        return (bindings ?? Enumerable.Empty<CameraBindingConfig>())
            .Select(CloneCameraBinding)
            .ToList();
    }

    private static CameraBindingConfig CloneCameraBinding(CameraBindingConfig binding)
    {
        var json = JsonSerializer.Serialize(binding, CloneJsonOptions);
        return JsonSerializer.Deserialize<CameraBindingConfig>(json, CloneJsonOptions) ?? new CameraBindingConfig();
    }

    private static CommunicationConfig CloneCommunication(CommunicationConfig communication)
    {
        var json = JsonSerializer.Serialize(communication, CloneJsonOptions);
        var clone = JsonSerializer.Deserialize<CommunicationConfig>(json, CloneJsonOptions) ?? new CommunicationConfig();
        clone.Normalize();
        return clone;
    }

    private static void EnsureMutationSucceeded(AppConfigMutationResult mutation)
    {
        if (!mutation.IsSuccess)
        {
            throw new InvalidOperationException($"{mutation.ErrorCode}: {mutation.Message}");
        }
    }
}

public sealed record StationHardwareSettingsSnapshot(
    List<CameraBindingConfig> Cameras,
    string ActiveCameraId,
    CommunicationConfig Communication,
    long Revision);

public sealed record StationHardwareTestResult(bool Success, string Message)
{
    public static StationHardwareTestResult Ok(string message) => new(true, message);

    public static StationHardwareTestResult Fail(string message) => new(false, message);
}
