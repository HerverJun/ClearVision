using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using System.Globalization;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Resolves execution targets exclusively from the current server configuration.
/// Rejections deliberately omit credentials and physical device identifiers.
/// </summary>
public sealed class ServerExecutionResourceProfileResolver : IExecutionResourceProfileResolver
{
    private readonly IConfigurationService? _configurationService;

    public ServerExecutionResourceProfileResolver(IConfigurationService? configurationService)
    {
        _configurationService = configurationService;
    }

    public ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource> ResolveDatabase(
        string profileId,
        string requestedTableName)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return RejectDatabase(
                "RESOURCE_DATABASE_PROFILE_REQUIRED",
                "Database execution requires a server database profile id.");
        }

        if (!TryGetConfiguration(out var config))
        {
            return RejectDatabase(
                "RESOURCE_CONFIGURATION_UNAVAILABLE",
                "Authoritative database resource configuration is unavailable.");
        }

        var matches = config.ExecutionResources.DatabaseProfiles
            .Where(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return RejectDatabase(
                "RESOURCE_DATABASE_PROFILE_NOT_FOUND",
                "The requested server database profile does not exist.");
        }

        if (matches.Length > 1)
        {
            return RejectDatabase(
                "RESOURCE_DATABASE_PROFILE_AMBIGUOUS",
                "The requested server database profile id is ambiguous.");
        }

        var profile = matches[0];
        if (!profile.Enabled)
        {
            return RejectDatabase(
                "RESOURCE_DATABASE_PROFILE_DISABLED",
                "The requested server database profile is disabled.");
        }

        if (string.IsNullOrWhiteSpace(profile.ConnectionString) ||
            !IsSupportedDatabaseType(profile.DbType) ||
            profile.AllowedTableNames.Count == 0)
        {
            return RejectDatabase(
                "RESOURCE_DATABASE_PROFILE_INVALID",
                "The requested server database profile is incomplete.");
        }

        var authorizedTableName = profile.AllowedTableNames.FirstOrDefault(tableName =>
            string.Equals(tableName, requestedTableName, StringComparison.OrdinalIgnoreCase));
        if (authorizedTableName == null)
        {
            return RejectDatabase(
                "RESOURCE_DATABASE_TABLE_NOT_AUTHORIZED",
                "The referenced server database profile does not authorize the requested table.");
        }

        return ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource>.Allow(
            new ResolvedDatabaseExecutionResource(
                profile.Id,
                profile.DbType,
                profile.ConnectionString,
                authorizedTableName));
    }

    public ExecutionResourceProfileResolution<ResolvedSerialExecutionResource> ResolveSerial(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return RejectSerial(
                "RESOURCE_SERIAL_PROFILE_REQUIRED",
                "Serial execution requires a server serial profile id.");
        }

        if (!TryGetConfiguration(out var config))
        {
            return RejectSerial(
                "RESOURCE_CONFIGURATION_UNAVAILABLE",
                "Authoritative serial resource configuration is unavailable.");
        }

        var matches = config.ExecutionResources.SerialProfiles
            .Where(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return RejectSerial(
                "RESOURCE_SERIAL_PROFILE_NOT_FOUND",
                "The requested server serial profile does not exist.");
        }

        if (matches.Length > 1)
        {
            return RejectSerial(
                "RESOURCE_SERIAL_PROFILE_AMBIGUOUS",
                "The requested server serial profile id is ambiguous.");
        }

        var profile = matches[0];
        if (!profile.Enabled)
        {
            return RejectSerial(
                "RESOURCE_SERIAL_PROFILE_DISABLED",
                "The requested server serial profile is disabled.");
        }

        if (!IsValidSerialProfile(profile))
        {
            return RejectSerial(
                "RESOURCE_SERIAL_PROFILE_INVALID",
                "The requested server serial profile is incomplete.");
        }

        return ExecutionResourceProfileResolution<ResolvedSerialExecutionResource>.Allow(
            new ResolvedSerialExecutionResource(
                profile.Id,
                profile.PortName,
                profile.BaudRate,
                profile.DataBits,
                profile.StopBits,
                profile.Parity));
    }

    public ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolvePlc(
        string profileId,
        PlcExecutionResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_REQUIRED",
                "PLC execution requires a server PLC profile id.");
        }

        if (!TryNormalizePlcProtocol(request.Protocol, out var requestedProtocol))
        {
            return RejectPlc(
                "RESOURCE_PLC_PROTOCOL_INVALID",
                "PLC execution requires a supported server protocol binding.");
        }

        if (request.ElementCount < 1)
        {
            return RejectPlc(
                "RESOURCE_PLC_COUNT_NOT_AUTHORIZED",
                "The requested PLC element count is not authorized.");
        }

        if (!TryGetConfiguration(out var config))
        {
            return RejectPlc(
                "RESOURCE_CONFIGURATION_UNAVAILABLE",
                "Authoritative PLC resource configuration is unavailable.");
        }

        var normalizedProfileId = profileId.Trim();
        var commonMatches = config.ExecutionResources.PlcProfiles
            .Where(profile => string.Equals(profile.Id, normalizedProfileId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        var compatibilityMatch = IsCompatibilityProfileId(normalizedProfileId);
        if (commonMatches.Length + (compatibilityMatch ? 1 : 0) == 0)
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_NOT_FOUND",
                "The requested server PLC profile does not exist.");
        }

        if (commonMatches.Length + (compatibilityMatch ? 1 : 0) > 1)
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_AMBIGUOUS",
                "The requested server PLC profile id is ambiguous.");
        }

        return compatibilityMatch
            ? ResolveCompatibilityPlc(normalizedProfileId, requestedProtocol, request, config)
            : ResolveCommonPlc(commonMatches[0], requestedProtocol, request);
    }

    private bool TryGetConfiguration(out AppConfig config)
    {
        config = null!;
        try
        {
            if (_configurationService == null)
            {
                return false;
            }

            config = _configurationService.GetCurrent();
            config.Normalize();
            return true;
        }
        catch
        {
            return false;
        }
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

    private static ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolveCommonPlc(
        PlcExecutionResourceProfile profile,
        string requestedProtocol,
        PlcExecutionResourceRequest request)
    {
        if (!profile.Enabled)
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_DISABLED",
                "The requested server PLC profile is disabled.");
        }

        if (!TryNormalizePlcProtocol(profile.Protocol, out var profileProtocol) ||
            !string.Equals(profileProtocol, requestedProtocol, StringComparison.Ordinal))
        {
            return RejectPlc(
                "RESOURCE_PLC_PROTOCOL_MISMATCH",
                "The requested PLC protocol does not match the server profile.");
        }

        if (string.IsNullOrWhiteSpace(profile.Host) ||
            profile.Port is < 1 or > 65535 ||
            profile.Bindings.Count == 0)
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_INVALID",
                "The requested server PLC profile is incomplete.");
        }

        if (profileProtocol == ExecutionPlcProtocols.SiemensS7 &&
            (!TryNormalizeS7CpuType(profile.CpuType, out _) ||
             profile.Rack is < 0 or > 15 ||
             profile.Slot is < 0 or > 15))
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_INVALID",
                "The requested server PLC profile is incomplete.");
        }

        if (profileProtocol == ExecutionPlcProtocols.ModbusTcp && profile.UnitId is < 1 or > 255)
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_INVALID",
                "The requested server PLC profile is incomplete.");
        }

        var bindingResolution = ResolveBinding(profile.Bindings, profileProtocol, request);
        if (!bindingResolution.Resolved || bindingResolution.Resource == null)
        {
            return bindingResolution;
        }

        var binding = bindingResolution.Resource;
        TryNormalizeS7CpuType(profile.CpuType, out var cpuType);
        return ExecutionResourceProfileResolution<ResolvedPlcExecutionResource>.Allow(
            new ResolvedPlcExecutionResource(
                profile.Id,
                profileProtocol,
                profile.Host,
                profile.Port,
                profileProtocol == ExecutionPlcProtocols.SiemensS7 ? cpuType : string.Empty,
                profileProtocol == ExecutionPlcProtocols.SiemensS7 ? profile.Rack : 0,
                profileProtocol == ExecutionPlcProtocols.SiemensS7 ? profile.Slot : 0,
                profileProtocol == ExecutionPlcProtocols.ModbusTcp ? profile.UnitId : 0,
                binding.Address,
                binding.Operation,
                binding.DataType,
                binding.MaxElementCount));
    }

    private static ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolveCompatibilityPlc(
        string profileId,
        string requestedProtocol,
        PlcExecutionResourceRequest request,
        AppConfig config)
    {
        var expectedProtocol = profileId.ToLowerInvariant() switch
        {
            ExecutionResourceCompatibilityProfileIds.CommunicationS7 => ExecutionPlcProtocols.SiemensS7,
            ExecutionResourceCompatibilityProfileIds.CommunicationMc => ExecutionPlcProtocols.MitsubishiMc,
            ExecutionResourceCompatibilityProfileIds.CommunicationFins => ExecutionPlcProtocols.OmronFins,
            _ => string.Empty
        };
        if (!string.Equals(expectedProtocol, requestedProtocol, StringComparison.Ordinal))
        {
            return RejectPlc(
                "RESOURCE_PLC_PROTOCOL_MISMATCH",
                "The requested PLC protocol does not match the server profile.");
        }

        var communicationProfile = config.Communication.GetProfile(expectedProtocol);
        if (string.IsNullOrWhiteSpace(communicationProfile.IpAddress) ||
            communicationProfile.Port is < 1 or > 65535 ||
            communicationProfile.Mappings.Count == 0)
        {
            return RejectPlc(
                "RESOURCE_PLC_PROFILE_INVALID",
                "The requested server PLC profile is incomplete.");
        }

        var matches = communicationProfile.Mappings
            .Where(mapping => string.Equals(mapping.Address, request.Address, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return RejectPlc(
                "RESOURCE_PLC_ADDRESS_NOT_AUTHORIZED",
                "The requested PLC address is not authorized by the server profile.");
        }

        if (matches.Length > 1)
        {
            return RejectPlc(
                "RESOURCE_PLC_ADDRESS_AMBIGUOUS",
                "The requested PLC address is ambiguous in the server profile.");
        }

        if (!TryNormalizeReadWriteOperation(request.Operation, out var operation) ||
            (operation == "Write" && !matches[0].CanWrite))
        {
            return RejectPlc(
                "RESOURCE_PLC_OPERATION_NOT_AUTHORIZED",
                "The requested PLC operation is not authorized by the server profile.");
        }

        var cpuType = string.Empty;
        var rack = 0;
        var slot = 0;
        if (communicationProfile is S7CommunicationProfile s7Profile)
        {
            if (!TryNormalizeS7CpuType(s7Profile.CpuType, out cpuType) ||
                s7Profile.Rack is < 0 or > 15 ||
                s7Profile.Slot is < 0 or > 15)
            {
                return RejectPlc(
                    "RESOURCE_PLC_PROFILE_INVALID",
                    "The requested server PLC profile is incomplete.");
            }

            rack = s7Profile.Rack;
            slot = s7Profile.Slot;
        }

        return ExecutionResourceProfileResolution<ResolvedPlcExecutionResource>.Allow(
            new ResolvedPlcExecutionResource(
                profileId,
                expectedProtocol,
                communicationProfile.IpAddress,
                communicationProfile.Port,
                cpuType,
                rack,
                slot,
                0,
                matches[0].Address,
                operation,
                matches[0].DataType,
                ushort.MaxValue));
    }

    private static ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolveBinding(
        IReadOnlyList<PlcExecutionResourceBinding> bindings,
        string protocol,
        PlcExecutionResourceRequest request)
    {
        var matches = bindings
            .Where(binding => BindingAddressMatches(binding.Address, request.Address, protocol))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return RejectPlc(
                "RESOURCE_PLC_ADDRESS_NOT_AUTHORIZED",
                "The requested PLC address is not authorized by the server profile.");
        }

        if (matches.Length > 1)
        {
            return RejectPlc(
                "RESOURCE_PLC_ADDRESS_AMBIGUOUS",
                "The requested PLC address is ambiguous in the server profile.");
        }

        var binding = matches[0];
        if (binding.MaxElementCount < 1 || request.ElementCount > binding.MaxElementCount)
        {
            return RejectPlc(
                "RESOURCE_PLC_COUNT_NOT_AUTHORIZED",
                "The requested PLC element count is not authorized.");
        }

        string operation;
        if (protocol == ExecutionPlcProtocols.ModbusTcp)
        {
            if (!TryNormalizeModbusFunction(request.Operation, out operation) ||
                !binding.AllowedFunctionCodes.Contains(operation, StringComparer.OrdinalIgnoreCase) ||
                (IsModbusWrite(operation) ? !binding.CanWrite : !binding.CanRead))
            {
                return RejectPlc(
                    "RESOURCE_PLC_OPERATION_NOT_AUTHORIZED",
                    "The requested PLC operation is not authorized by the server profile.");
            }
        }
        else if (!TryNormalizeReadWriteOperation(request.Operation, out operation) ||
                 (operation == "Read" ? !binding.CanRead : !binding.CanWrite))
        {
            return RejectPlc(
                "RESOURCE_PLC_OPERATION_NOT_AUTHORIZED",
                "The requested PLC operation is not authorized by the server profile.");
        }

        var canonicalAddress = protocol == ExecutionPlcProtocols.ModbusTcp
            ? int.Parse(binding.Address, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : binding.Address;
        return ExecutionResourceProfileResolution<ResolvedPlcExecutionResource>.Allow(
            new ResolvedPlcExecutionResource(
                string.Empty,
                protocol,
                string.Empty,
                0,
                string.Empty,
                0,
                0,
                0,
                canonicalAddress,
                operation,
                binding.DataType,
                binding.MaxElementCount));
    }

    private static bool BindingAddressMatches(string configuredAddress, string requestedAddress, string protocol)
    {
        if (protocol != ExecutionPlcProtocols.ModbusTcp)
        {
            return string.Equals(configuredAddress, requestedAddress, StringComparison.OrdinalIgnoreCase);
        }

        return int.TryParse(configuredAddress, NumberStyles.None, CultureInfo.InvariantCulture, out var configured) &&
               int.TryParse(requestedAddress, NumberStyles.None, CultureInfo.InvariantCulture, out var requested) &&
               configured is >= 0 and <= ushort.MaxValue &&
               requested is >= 0 and <= ushort.MaxValue &&
               configured == requested;
    }

    private static bool IsCompatibilityProfileId(string profileId) =>
        profileId.Equals(ExecutionResourceCompatibilityProfileIds.CommunicationS7, StringComparison.OrdinalIgnoreCase) ||
        profileId.Equals(ExecutionResourceCompatibilityProfileIds.CommunicationMc, StringComparison.OrdinalIgnoreCase) ||
        profileId.Equals(ExecutionResourceCompatibilityProfileIds.CommunicationFins, StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizePlcProtocol(string value, out string protocol)
    {
        protocol = (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "S7" or "SIEMENSS7" => ExecutionPlcProtocols.SiemensS7,
            "MC" or "MITSUBISHIMC" => ExecutionPlcProtocols.MitsubishiMc,
            "FINS" or "OMRONFINS" => ExecutionPlcProtocols.OmronFins,
            "MODBUS" or "MODBUSTCP" or "MODBUS_TCP" => ExecutionPlcProtocols.ModbusTcp,
            _ => string.Empty
        };
        return protocol.Length > 0;
    }

    private static bool TryNormalizeReadWriteOperation(string value, out string operation)
    {
        operation = (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "READ" => "Read",
            "WRITE" => "Write",
            _ => string.Empty
        };
        return operation.Length > 0;
    }

    private static bool TryNormalizeModbusFunction(string value, out string functionCode)
    {
        functionCode = (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "READCOILS" => "ReadCoils",
            "READHOLDING" => "ReadHolding",
            "WRITESINGLE" => "WriteSingle",
            "WRITEMULTIPLE" => "WriteMultiple",
            _ => string.Empty
        };
        return functionCode.Length > 0;
    }

    private static bool IsModbusWrite(string functionCode) =>
        functionCode is "WriteSingle" or "WriteMultiple";

    private static bool TryNormalizeS7CpuType(string value, out string cpuType)
    {
        cpuType = (value ?? string.Empty)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant() switch
        {
            "S7200" => "S7200",
            "S7200SMART" => "S7200Smart",
            "S7300" => "S7300",
            "S7400" => "S7400",
            "S71200" => "S71200",
            "S71500" => "S71500",
            _ => string.Empty
        };
        return cpuType.Length > 0;
    }

    private static ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource> RejectDatabase(
        string code,
        string message) =>
        ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource>.Reject(code, message);

    private static ExecutionResourceProfileResolution<ResolvedSerialExecutionResource> RejectSerial(
        string code,
        string message) =>
        ExecutionResourceProfileResolution<ResolvedSerialExecutionResource>.Reject(code, message);

    private static ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> RejectPlc(
        string code,
        string message) =>
        ExecutionResourceProfileResolution<ResolvedPlcExecutionResource>.Reject(code, message);
}
