using System.Globalization;
using System.Net;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Production resource broker preflight.  It validates server-issued resource
/// fingerprints and rejects unsafe paths or unbound device/network targets
/// before an operator executor is selected.
/// </summary>
public sealed class ServerExecutionResourceAuthority : IExecutionResourceAuthority
{
    private static readonly HashSet<OperatorType> CalibrationWriters =
    [
        OperatorType.CameraCalibration,
        OperatorType.FisheyeCalibration,
        OperatorType.StereoCalibration,
        OperatorType.NPointCalibration,
        OperatorType.TranslationRotationCalibration,
        OperatorType.HandEyeCalibration
    ];

    private readonly IConfigurationService? _configurationService;
    private readonly IExecutionResourceProfileResolver _resourceProfileResolver;

    public ServerExecutionResourceAuthority(IConfigurationService? configurationService)
        : this(configurationService, new ServerExecutionResourceProfileResolver(configurationService))
    {
    }

    public ServerExecutionResourceAuthority(
        IConfigurationService? configurationService,
        IExecutionResourceProfileResolver resourceProfileResolver)
    {
        _configurationService = configurationService;
        _resourceProfileResolver = resourceProfileResolver ??
            throw new ArgumentNullException(nameof(resourceProfileResolver));
    }

    public ExecutionResourceAuthorityResult Validate(ExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Source == ExecutionSnapshotSource.Draft &&
            snapshot.ExternalCapabilities == ExecutionSideEffect.None)
        {
            return ExecutionResourceAuthorityResult.Allow();
        }

        var authorityName = snapshot.Source switch
        {
            ExecutionSnapshotSource.RuntimePackage => "RuntimePackage",
            ExecutionSnapshotSource.Draft => "Draft",
            _ => "StoredProject"
        };
        var flow = snapshot.CreateExecutionFlow();
        var seed = snapshot.ResourceBindings
            .Where(pair =>
                !pair.Key.StartsWith("Resource:", StringComparison.Ordinal) &&
                !pair.Key.StartsWith("ExternalResource:", StringComparison.Ordinal) &&
                !pair.Key.Equals("ResourceManifestHash", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        ExecutionExternalResourceManifest? externalResources = null;
        if (snapshot.ExternalCapabilities.HasFlag(ExecutionSideEffect.DeviceRead))
        {
            if (!snapshot.ResourceBindings.TryGetValue("CameraBindingId", out var cameraBindingId) ||
                string.IsNullOrWhiteSpace(cameraBindingId))
            {
                return ExecutionResourceAuthorityResult.Reject(
                    "RESOURCE_EXTERNAL_CAMERA_BINDING_REQUIRED",
                    "External camera acquisition requires a server-issued CameraBindingId.");
            }

            externalResources = new ExecutionExternalResourceManifest(cameraBindingId);
        }

        var expected = ExecutionResourceBindingManifest.Build(
            flow,
            authorityName,
            seed,
            externalResources);
        var actualResourceBindings = snapshot.ResourceBindings
            .Where(pair =>
                pair.Key.StartsWith("Resource:", StringComparison.Ordinal) ||
                pair.Key.StartsWith("ExternalResource:", StringComparison.Ordinal) ||
                pair.Key.Equals("ResourceManifestHash", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var expectedResourceBindings = expected
            .Where(pair =>
                pair.Key.StartsWith("Resource:", StringComparison.Ordinal) ||
                pair.Key.StartsWith("ExternalResource:", StringComparison.Ordinal) ||
                pair.Key.Equals("ResourceManifestHash", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (actualResourceBindings.Count != expectedResourceBindings.Count ||
            expectedResourceBindings.Any(pair =>
                !actualResourceBindings.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal)))
        {
            return ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_BINDING_MANIFEST_INVALID",
                "Execution resources are missing a matching server-issued manifest.");
        }

        AppConfig config;
        try
        {
            if (_configurationService == null)
            {
                return ExecutionResourceAuthorityResult.Reject(
                    "RESOURCE_CONFIGURATION_UNAVAILABLE",
                    "Authoritative resource configuration is unavailable.");
            }

            config = _configurationService.GetCurrent();
            config.Normalize();
        }
        catch
        {
            return ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_CONFIGURATION_UNAVAILABLE",
                "Authoritative resource configuration is unavailable.");
        }

        if (snapshot.ExternalCapabilities.HasFlag(ExecutionSideEffect.DeviceRead))
        {
            var cameraResult = ValidateExternalCamera(snapshot, config);
            if (!cameraResult.Allowed)
            {
                return cameraResult;
            }
        }

        var approvedRoots = BuildApprovedRoots(snapshot, config);
        foreach (var @operator in NestedExecutionFlowCatalog.EnumerateEnabledOperators(flow))
        {
            var result = ValidateOperator(@operator, config, approvedRoots, _resourceProfileResolver);
            if (!result.Allowed)
            {
                return result;
            }
        }

        return ExecutionResourceAuthorityResult.Allow();
    }

    private static ExecutionResourceAuthorityResult ValidateExternalCamera(
        ExecutionSnapshot snapshot,
        AppConfig config)
    {
        var bindingId = snapshot.ResourceBindings["CameraBindingId"];
        var binding = config.Cameras.FirstOrDefault(item =>
            item.IsEnabled && string.Equals(item.Id, bindingId, StringComparison.OrdinalIgnoreCase));
        return binding != null && !string.IsNullOrWhiteSpace(binding.SerialNumber)
            ? ExecutionResourceAuthorityResult.Allow()
            : ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_EXTERNAL_CAMERA_BINDING_INVALID",
                "CameraBindingId must reference an enabled server camera binding.");
    }

    private static ExecutionResourceAuthorityResult ValidateOperator(
        Operator @operator,
        AppConfig config,
        IReadOnlyList<string> approvedRoots,
        IExecutionResourceProfileResolver resourceProfileResolver)
    {
        if (@operator.Type == OperatorType.ModbusRtuCommunication ||
            (@operator.Type == OperatorType.ModbusCommunication &&
             ExecutionResourceBindingManifest.UsesModbusRtuAuthority(@operator)))
        {
            return ExecutionResourceAuthorityResult.Reject(
                "MODBUS_RTU_UNSUPPORTED",
                "Modbus RTU is a retired compatibility type and is not supported by this package operator.");
        }

        foreach (var pathField in ExecutionResourceBindingManifest.AuthorityFieldNames(@operator)
                     .Where(ExecutionResourceBindingManifest.IsPathFieldName))
        {
            var path = Read(@operator, pathField);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!CanonicalPathSafety.TryValidateWithinRoots(
                    path,
                    approvedRoots,
                    out _,
                    out var code,
                    out var message))
            {
                return ExecutionResourceAuthorityResult.Reject(code, $"{@operator.Type}.{pathField}: {message}");
            }
        }

        if (CalibrationWriters.Contains(@operator.Type))
        {
            var calibrationAssetId = Read(@operator, "CalibrationAssetId");
            if (string.IsNullOrWhiteSpace(calibrationAssetId))
            {
                return ExecutionResourceAuthorityResult.Reject(
                    "RESOURCE_CALIBRATION_ASSET_REQUIRED",
                    "Calibration writes must target a project calibration asset.");
            }
        }

        return @operator.Type switch
        {
            OperatorType.ImageAcquisition => ValidateImageAcquisition(@operator, config),
            OperatorType.HttpRequest => ValidateHttp(@operator),
            OperatorType.DatabaseWrite => ValidateDatabase(@operator, resourceProfileResolver),
            OperatorType.SerialCommunication => ValidateSerial(@operator, resourceProfileResolver),
            OperatorType.TcpCommunication => ValidateTcp(@operator, config),
            OperatorType.ModbusCommunication when HasSerialTargetShape(@operator) =>
                ValidateCanonicalModbusRtu(@operator, resourceProfileResolver),
            OperatorType.ModbusCommunication or
            OperatorType.SiemensS7Communication or
            OperatorType.MitsubishiMcCommunication or
            OperatorType.OmronFinsCommunication => ValidatePlc(@operator, resourceProfileResolver),
            _ => ExecutionResourceAuthorityResult.Allow()
        };
    }

    private static ExecutionResourceAuthorityResult ValidateImageAcquisition(Operator @operator, AppConfig config)
    {
        if (!string.Equals(Read(@operator, "SourceType"), "Camera", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionResourceAuthorityResult.Allow();
        }

        var bindingId = Read(@operator, "CameraId");
        var binding = config.Cameras.FirstOrDefault(item =>
            item.IsEnabled && string.Equals(item.Id, bindingId, StringComparison.OrdinalIgnoreCase));
        return binding != null && !string.IsNullOrWhiteSpace(binding.SerialNumber)
            ? ExecutionResourceAuthorityResult.Allow()
            : ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_CAMERA_BINDING_REQUIRED",
                "CameraId must reference an enabled server camera binding.");
    }

    private static ExecutionResourceAuthorityResult ValidateTcp(Operator @operator, AppConfig config)
    {
        var profileId = Read(@operator, "ProfileId");
        var profile = config.TcpCommunication.FindProfile(profileId);
        return profile?.Enabled == true
            ? ExecutionResourceAuthorityResult.Allow()
            : ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_TCP_PROFILE_REQUIRED",
                "TCP execution requires an enabled server profile id.");
    }

    private static ExecutionResourceAuthorityResult ValidateDatabase(
        Operator @operator,
        IExecutionResourceProfileResolver resourceProfileResolver)
    {
        var profileId = Read(@operator, "ProfileId");
        var tableName = Read(@operator, "TableName");
        return ToAuthority(resourceProfileResolver.ResolveDatabase(profileId, tableName));
    }

    private static ExecutionResourceAuthorityResult ValidateSerial(
        Operator @operator,
        IExecutionResourceProfileResolver resourceProfileResolver)
    {
        var profileId = Read(@operator, "ProfileId");
        return ToAuthority(resourceProfileResolver.ResolveSerial(profileId));
    }

    private static bool IsSupportedDatabaseType(string value) =>
        value.Equals("SQLite", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("SQLServer", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("MySQL", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidSerialProfile(SerialExecutionResourceProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.PortName) &&
        profile.BaudRate > 0 &&
        profile.DataBits is >= 5 and <= 8 &&
        profile.StopBits is not null &&
        (profile.StopBits.Equals("One", StringComparison.OrdinalIgnoreCase) ||
         profile.StopBits.Equals("OnePointFive", StringComparison.OrdinalIgnoreCase) ||
         profile.StopBits.Equals("Two", StringComparison.OrdinalIgnoreCase)) &&
        profile.Parity is not null &&
        (profile.Parity.Equals("None", StringComparison.OrdinalIgnoreCase) ||
         profile.Parity.Equals("Odd", StringComparison.OrdinalIgnoreCase) ||
         profile.Parity.Equals("Even", StringComparison.OrdinalIgnoreCase));

    private static bool HasSerialTargetShape(Operator @operator) =>
        !string.IsNullOrWhiteSpace(Read(@operator, "PortName")) ||
        !string.IsNullOrWhiteSpace(Read(@operator, "BaudRate")) ||
        !string.IsNullOrWhiteSpace(Read(@operator, "DataBits")) ||
        !string.IsNullOrWhiteSpace(Read(@operator, "StopBits")) ||
        !string.IsNullOrWhiteSpace(Read(@operator, "Parity"));

    private static ExecutionResourceAuthorityResult ValidateCanonicalModbusRtu(
        Operator @operator,
        IExecutionResourceProfileResolver resourceProfileResolver)
    {
        // Legacy ModbusRtuCommunication snapshots use the canonical
        // ModbusCommunication enum. Require an unambiguous RTU declaration;
        // otherwise adding a serial-looking field to a TCP operator could
        // bypass the PLC target authority branch.
        if (!string.Equals(Read(@operator, "Protocol"), "RTU", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(Read(@operator, "IpAddress")) ||
            !string.IsNullOrWhiteSpace(Read(@operator, "Port")))
        {
            return ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_MODBUS_RTU_BINDING_INVALID",
                "A canonical Modbus RTU binding cannot contain a TCP endpoint.");
        }

        return ValidateSerial(@operator, resourceProfileResolver);
    }

    private static ExecutionResourceAuthorityResult ValidatePlc(
        Operator @operator,
        IExecutionResourceProfileResolver resourceProfileResolver)
    {
        var profileId = Read(@operator, "ProfileId");
        var protocol = @operator.Type switch
        {
            OperatorType.SiemensS7Communication => ExecutionPlcProtocols.SiemensS7,
            OperatorType.MitsubishiMcCommunication => ExecutionPlcProtocols.MitsubishiMc,
            OperatorType.OmronFinsCommunication => ExecutionPlcProtocols.OmronFins,
            _ => ExecutionPlcProtocols.ModbusTcp
        };
        var isModbus = @operator.Type == OperatorType.ModbusCommunication;
        var address = isModbus ? Read(@operator, "RegisterAddress") : Read(@operator, "Address");
        var operation = isModbus ? Read(@operator, "FunctionCode") : Read(@operator, "Operation");
        var countName = isModbus ? "RegisterCount" : "Length";
        _ = int.TryParse(Read(@operator, countName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);
        count = operation is "Write" or "WriteSingle" ? 1 : Math.Max(1, count);
        return ToAuthority(resourceProfileResolver.ResolvePlc(
            profileId,
            new PlcExecutionResourceRequest(protocol, address, operation, count)));
    }

    private static ExecutionResourceAuthorityResult ToAuthority<TResource>(
        ExecutionResourceProfileResolution<TResource> resolution)
        where TResource : class =>
        resolution.Resolved && resolution.Resource != null
            ? ExecutionResourceAuthorityResult.Allow()
            : ExecutionResourceAuthorityResult.Reject(resolution.Code, resolution.Message);

    private static ExecutionResourceAuthorityResult ValidateHttp(Operator @operator)
    {
        var value = Read(@operator, "Url");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Port is < 1 or > 65535)
        {
            return ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_HTTP_DESTINATION_INVALID",
                "HTTP destination requires an absolute http/https URI with a valid host and port.");
        }

        if (IPAddress.TryParse(uri.Host, out var address) && IsForbiddenAddress(address))
        {
            return ExecutionResourceAuthorityResult.Reject(
                "RESOURCE_HTTP_CIDR_FORBIDDEN",
                "HTTP destination cannot use loopback, link-local, multicast, or unspecified CIDRs.");
        }

        return ExecutionResourceAuthorityResult.Allow();
    }

    internal static bool IsForbiddenAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None) || address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.MapToIPv6().GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var ipv4 = address.GetAddressBytes();
            return ipv4[0] == 0 || ipv4[0] >= 224 || (ipv4[0] == 169 && ipv4[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || bytes.All(value => value == 0);
    }

    private static IReadOnlyList<string> BuildApprovedRoots(ExecutionSnapshot snapshot, AppConfig config)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.Storage.ImageSavePath))
        {
            roots.Add(config.Storage.ImageSavePath);
        }

        roots.Add(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "App_Data",
            "ProjectAssets",
            snapshot.ProjectId.ToString("N")));
        if (snapshot.ResourceBindings.TryGetValue("PackageRoot", out var packageRoot) &&
            !string.IsNullOrWhiteSpace(packageRoot))
        {
            roots.Add(packageRoot);
        }

        return roots;
    }

    private static string Read(Operator @operator, string name) =>
        @operator.Parameters.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))?.GetValue()?.ToString()?.Trim() ?? string.Empty;
}
