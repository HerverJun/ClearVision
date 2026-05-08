using System.Globalization;
using System.Text.RegularExpressions;
using Acme.PlcComm.Core;
using Acme.PlcComm.Interfaces;

namespace Acme.PlcComm.Mitsubishi;

public class McAddressParser : IAddressParser
{
    private sealed record DeviceInfo(byte Code, int NumberBase, PlcDataType DataType);

    private static readonly Dictionary<string, DeviceInfo> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["D"] = new(0xA8, 10, PlcDataType.Word),
        ["W"] = new(0xB4, 16, PlcDataType.Word),
        ["R"] = new(0xAF, 10, PlcDataType.Word),
        ["ZR"] = new(0xB0, 10, PlcDataType.Word),
        ["SD"] = new(0xA9, 10, PlcDataType.Word),
        ["SW"] = new(0xB5, 16, PlcDataType.Word),
        ["TN"] = new(0xC2, 10, PlcDataType.Word),
        ["CN"] = new(0xC5, 10, PlcDataType.Word),
        ["Z"] = new(0xCC, 10, PlcDataType.Word),

        ["M"] = new(0x90, 10, PlcDataType.Bit),
        ["L"] = new(0x92, 10, PlcDataType.Bit),
        ["B"] = new(0xA0, 16, PlcDataType.Bit),
        ["X"] = new(0x9C, 16, PlcDataType.Bit),
        ["Y"] = new(0x9D, 16, PlcDataType.Bit),
        ["F"] = new(0x93, 10, PlcDataType.Bit),
        ["V"] = new(0x94, 10, PlcDataType.Bit),
        ["S"] = new(0x98, 10, PlcDataType.Bit),
        ["SM"] = new(0x91, 10, PlcDataType.Bit),
        ["SB"] = new(0xA1, 16, PlcDataType.Bit),
        ["TS"] = new(0xC1, 10, PlcDataType.Bit),
        ["TC"] = new(0xC0, 10, PlcDataType.Bit),
        ["CS"] = new(0xC4, 10, PlcDataType.Bit),
        ["CC"] = new(0xC3, 10, PlcDataType.Bit),
        ["DX"] = new(0xA2, 16, PlcDataType.Bit),
        ["DY"] = new(0xA3, 16, PlcDataType.Bit)
    };

    private static readonly Regex AddressRegex = new(
        @"^(?<prefix>[A-Z]+)(?<address>[0-9A-F]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public OperateResult<PlcAddress> Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return OperateResult<PlcAddress>.Failure("Address cannot be empty.");
        }

        address = address.Trim().ToUpperInvariant();
        var match = AddressRegex.Match(address);
        if (!match.Success)
        {
            return OperateResult<PlcAddress>.Failure($"Invalid MC address format: {address}");
        }

        var prefix = match.Groups["prefix"].Value;
        var numberText = match.Groups["address"].Value;
        if (!Devices.TryGetValue(prefix, out var device))
        {
            return OperateResult<PlcAddress>.Failure($"Unsupported MC device type: {prefix}");
        }

        if (!TryParseAddress(numberText, device.NumberBase, out var startAddress))
        {
            return OperateResult<PlcAddress>.Failure($"Invalid MC address number for {prefix}: {numberText}");
        }

        return OperateResult<PlcAddress>.Success(new PlcAddress
        {
            AreaType = prefix,
            StartAddress = startAddress,
            BitOffset = -1,
            DataType = device.DataType,
            DeviceCode = device.Code
        });
    }

    public bool TryParse(string address, out PlcAddress result)
    {
        var parseResult = Parse(address);
        if (parseResult.IsSuccess && parseResult.Content != null)
        {
            result = parseResult.Content;
            return true;
        }

        result = new PlcAddress();
        return false;
    }

    public string ToAddressString(PlcAddress address)
    {
        var prefix = address.AreaType.ToUpperInvariant();
        if (!Devices.TryGetValue(prefix, out var device))
        {
            return address.ToString();
        }

        var number = device.NumberBase == 16
            ? address.StartAddress.ToString("X", CultureInfo.InvariantCulture)
            : address.StartAddress.ToString(CultureInfo.InvariantCulture);
        return prefix + number;
    }

    public bool IsValidAddress(string address)
    {
        return Parse(address).IsSuccess;
    }

    public static byte GetDeviceCode(string prefix)
    {
        return Devices.TryGetValue(prefix.ToUpperInvariant(), out var device) ? device.Code : (byte)0;
    }

    private static bool TryParseAddress(string value, int numberBase, out int address)
    {
        try
        {
            address = Convert.ToInt32(value, numberBase);
            return true;
        }
        catch
        {
            address = 0;
            return false;
        }
    }
}
