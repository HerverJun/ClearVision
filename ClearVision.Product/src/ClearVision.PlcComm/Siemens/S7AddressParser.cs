using System.Text.RegularExpressions;
using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Interfaces;

namespace ClearVision.PlcComm.Siemens;

public class S7AddressParser : IAddressParser
{
    private static readonly Regex DbAddressRegex = new(
        @"^DB(?<db>\d+)\.(?<type>DB[XBWDR])(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DbByteAddressRegex = new(
        @"^DB(?<db>\d+)\.(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SimpleAddressRegex = new(
        @"^(?<prefix>[MIQETC])(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TypedSimpleAddressRegex = new(
        @"^(?<prefix>[MIQEA])(?<type>[BWD])(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public OperateResult<PlcAddress> Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return OperateResult<PlcAddress>.Failure("Address cannot be empty.");
        }

        address = address.Trim().ToUpperInvariant();

        var dbMatch = DbAddressRegex.Match(address);
        if (dbMatch.Success)
        {
            return ParseDbAddress(dbMatch);
        }

        var dbByteMatch = DbByteAddressRegex.Match(address);
        if (dbByteMatch.Success)
        {
            return ParseDbByteAddress(dbByteMatch);
        }

        var simpleMatch = SimpleAddressRegex.Match(address);
        if (simpleMatch.Success)
        {
            return ParseSimpleAddress(simpleMatch);
        }

        var typedSimpleMatch = TypedSimpleAddressRegex.Match(address);
        if (typedSimpleMatch.Success)
        {
            return ParseTypedSimpleAddress(typedSimpleMatch);
        }

        return OperateResult<PlcAddress>.Failure($"Unsupported S7 address format: {address}");
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
        return address.ToString();
    }

    public bool IsValidAddress(string address)
    {
        return Parse(address).IsSuccess;
    }

    private static OperateResult<PlcAddress> ParseDbAddress(Match match)
    {
        try
        {
            var typeCode = match.Groups["type"].Value.ToUpperInvariant();
            var bitOffset = match.Groups["bit"].Success ? int.Parse(match.Groups["bit"].Value) : -1;

            if (bitOffset > 7)
            {
                return OperateResult<PlcAddress>.Failure("Bit offset must be between 0 and 7.");
            }

            if (typeCode == "DBX" && bitOffset < 0)
            {
                return OperateResult<PlcAddress>.Failure("DBX bit address must include a bit offset, for example DB1.DBX10.3.");
            }

            if (typeCode != "DBX" && bitOffset >= 0)
            {
                return OperateResult<PlcAddress>.Failure("Only DBX addresses can include a bit offset; use DBW or DBD without .bit.");
            }

            return OperateResult<PlcAddress>.Success(new PlcAddress
            {
                AreaType = "DB",
                DbNumber = int.Parse(match.Groups["db"].Value),
                StartAddress = int.Parse(match.Groups["offset"].Value),
                BitOffset = bitOffset,
                DataType = typeCode switch
                {
                    "DBX" => PlcDataType.Bit,
                    "DBB" => PlcDataType.Byte,
                    "DBW" => PlcDataType.Word,
                    "DBD" => PlcDataType.DWord,
                    "DBR" => PlcDataType.Float,
                    _ => PlcDataType.Word
                },
                DeviceCode = 0x84
            });
        }
        catch (Exception ex)
        {
            return OperateResult<PlcAddress>.Failure($"Failed to parse DB address: {ex.Message}");
        }
    }

    private static OperateResult<PlcAddress> ParseDbByteAddress(Match match)
    {
        try
        {
            return OperateResult<PlcAddress>.Success(new PlcAddress
            {
                AreaType = "DB",
                DbNumber = int.Parse(match.Groups["db"].Value),
                StartAddress = int.Parse(match.Groups["offset"].Value),
                BitOffset = -1,
                DataType = PlcDataType.Byte,
                DeviceCode = 0x84
            });
        }
        catch (Exception ex)
        {
            return OperateResult<PlcAddress>.Failure($"Failed to parse DB byte address: {ex.Message}");
        }
    }

    private static OperateResult<PlcAddress> ParseSimpleAddress(Match match)
    {
        try
        {
            var prefix = match.Groups["prefix"].Value.ToUpperInvariant();
            var bitOffset = match.Groups["bit"].Success ? int.Parse(match.Groups["bit"].Value) : -1;
            if (bitOffset > 7)
            {
                return OperateResult<PlcAddress>.Failure("Bit offset must be between 0 and 7.");
            }

            var address = new PlcAddress
            {
                AreaType = prefix,
                StartAddress = int.Parse(match.Groups["offset"].Value),
                BitOffset = bitOffset
            };

            (address.DeviceCode, address.DataType) = prefix switch
            {
                "M" => ((byte)0x83, bitOffset >= 0 ? PlcDataType.Bit : PlcDataType.Word),
                "I" or "E" => ((byte)0x81, bitOffset >= 0 ? PlcDataType.Bit : PlcDataType.Word),
                "Q" or "A" => ((byte)0x82, bitOffset >= 0 ? PlcDataType.Bit : PlcDataType.Word),
                "T" => ((byte)0x1F, PlcDataType.Word),
                "C" => ((byte)0x1E, PlcDataType.Word),
                _ => ((byte)0x00, PlcDataType.Word)
            };

            return OperateResult<PlcAddress>.Success(address);
        }
        catch (Exception ex)
        {
            return OperateResult<PlcAddress>.Failure($"Failed to parse S7 address: {ex.Message}");
        }
    }

    private static OperateResult<PlcAddress> ParseTypedSimpleAddress(Match match)
    {
        try
        {
            var prefix = match.Groups["prefix"].Value.ToUpperInvariant();
            var typeCode = match.Groups["type"].Value.ToUpperInvariant();
            var address = new PlcAddress
            {
                AreaType = prefix,
                StartAddress = int.Parse(match.Groups["offset"].Value),
                BitOffset = -1
            };

            (address.DeviceCode, address.DataType) = prefix switch
            {
                "M" => ((byte)0x83, ToSimpleDataType(typeCode)),
                "I" or "E" => ((byte)0x81, ToSimpleDataType(typeCode)),
                "Q" or "A" => ((byte)0x82, ToSimpleDataType(typeCode)),
                _ => ((byte)0x00, PlcDataType.Word)
            };

            return OperateResult<PlcAddress>.Success(address);
        }
        catch (Exception ex)
        {
            return OperateResult<PlcAddress>.Failure($"Failed to parse typed S7 address: {ex.Message}");
        }
    }

    private static PlcDataType ToSimpleDataType(string typeCode)
    {
        return typeCode switch
        {
            "B" => PlcDataType.Byte,
            "W" => PlcDataType.Word,
            "D" => PlcDataType.DWord,
            _ => PlcDataType.Word
        };
    }
}
