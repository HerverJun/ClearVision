using System.Globalization;
using System.Text.RegularExpressions;
using Acme.PlcComm.Core;
using Acme.PlcComm.Interfaces;

namespace Acme.PlcComm.Omron;

public class FinsAddressParser : IAddressParser
{
    private static readonly Dictionary<string, (byte wordCode, byte bitCode)> AreaCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CIO"] = (0xB0, 0x30),
            ["WR"] = (0xB1, 0x31),
            ["HR"] = (0xB2, 0x32),
            ["AR"] = (0xB3, 0x33),
            ["DM"] = (0x82, 0x02),
            ["D"] = (0x82, 0x02),
            ["TIM"] = (0x89, 0x09),
            ["CNT"] = (0x89, 0x09)
        };

    private const byte EmWordBase = 0xA0;
    private const byte EmBitBase = 0x20;

    private static readonly Regex AddressRegex = new(
        @"^(?:(?<prefix>EM|E)(?<bank>\d+)(?:\s+|\.)(?<address>\d+)|(?<prefix>CIO|WR|HR|AR|DM|D|TIM|CNT)\s*(?<address>\d+))(?:\.(?<bit>\d+))?$",
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
            return OperateResult<PlcAddress>.Failure($"Invalid FINS address format: {address}");
        }

        var prefix = match.Groups["prefix"].Value;
        var addressText = match.Groups["address"].Value;
        var bankText = match.Groups["bank"].Value;
        var hasBitOffset = match.Groups["bit"].Success;
        var bitOffset = hasBitOffset ? int.Parse(match.Groups["bit"].Value) : -1;

        if (!int.TryParse(addressText, out var startAddress))
        {
            return OperateResult<PlcAddress>.Failure($"Invalid FINS address number: {addressText}");
        }

        if (hasBitOffset && bitOffset > 15)
        {
            return OperateResult<PlcAddress>.Failure("Bit offset must be between 0 and 15.");
        }

        var normalizedPrefix = prefix.Equals("D", StringComparison.OrdinalIgnoreCase) ? "DM" : prefix;
        byte wordCode;
        byte bitCode;
        var bank = 0;

        if (normalizedPrefix is "EM" or "E")
        {
            normalizedPrefix = "EM";
            bank = int.Parse(bankText);
            if (bank is < 0 or > 15)
            {
                return OperateResult<PlcAddress>.Failure("EM bank must be between 0 and 15.");
            }

            wordCode = (byte)(EmWordBase + bank);
            bitCode = (byte)(EmBitBase + bank);
        }
        else if (AreaCodes.TryGetValue(normalizedPrefix, out var codes))
        {
            wordCode = codes.wordCode;
            bitCode = codes.bitCode;
        }
        else
        {
            return OperateResult<PlcAddress>.Failure($"Unsupported FINS memory area: {prefix}");
        }

        var rangeValidation = ValidateAddressRange(normalizedPrefix, startAddress);
        if (!string.IsNullOrEmpty(rangeValidation))
        {
            return OperateResult<PlcAddress>.Failure(rangeValidation);
        }

        return OperateResult<PlcAddress>.Success(new PlcAddress
        {
            AreaType = normalizedPrefix,
            DbNumber = bank,
            StartAddress = startAddress,
            BitOffset = bitOffset,
            DataType = hasBitOffset ? PlcDataType.Bit : PlcDataType.Word,
            DeviceCode = hasBitOffset ? bitCode : wordCode
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
        var bitSuffix = address.BitOffset >= 0 ? "." + address.BitOffset.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return address.AreaType.ToUpperInvariant() switch
        {
            "EM" => $"EM{address.DbNumber}.{address.StartAddress}{bitSuffix}",
            _ => $"{address.AreaType.ToUpperInvariant()}{address.StartAddress}{bitSuffix}"
        };
    }

    public bool IsValidAddress(string address)
    {
        return Parse(address).IsSuccess;
    }

    public static byte GetAreaCode(string prefix, bool isBitAccess, int bank = 0)
    {
        prefix = prefix.ToUpperInvariant();
        if (prefix is "EM" or "E")
        {
            return isBitAccess ? (byte)(EmBitBase + bank) : (byte)(EmWordBase + bank);
        }

        if (prefix == "D")
        {
            prefix = "DM";
        }

        return AreaCodes.TryGetValue(prefix, out var codes)
            ? isBitAccess ? codes.bitCode : codes.wordCode
            : (byte)0;
    }

    private static string? ValidateAddressRange(string prefix, int address)
    {
        var maxAddresses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["CIO"] = 6143,
            ["WR"] = 511,
            ["HR"] = 511,
            ["AR"] = 959,
            ["DM"] = 32767,
            ["TIM"] = 4095,
            ["CNT"] = 4095,
            ["EM"] = 32767
        };

        return maxAddresses.TryGetValue(prefix, out var maxAddress) && address > maxAddress
            ? $"{prefix} address exceeds maximum {maxAddress}."
            : null;
    }
}
