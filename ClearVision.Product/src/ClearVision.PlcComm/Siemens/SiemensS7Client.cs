using ClearVision.PlcComm.Common;
using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Interfaces;
using HslCommunication.Profinet.Siemens;
using Microsoft.Extensions.Logging;

namespace ClearVision.PlcComm.Siemens;

public class SiemensS7Client : HaoPlcClientBase
{
    private readonly SiemensS7Net _client;
    private readonly S7AddressParser _addressParser = new();
    private readonly SiemensCpuType _cpuType;
    private readonly int _rack;
    private readonly int _slot;

    public SiemensS7Client(
        string ipAddress,
        SiemensCpuType cpuType = SiemensCpuType.S71200,
        int rack = 0,
        int slot = 1,
        ILogger? logger = null)
        : base(ipAddress, 102, logger)
    {
        _cpuType = cpuType;
        _rack = rack;
        _slot = slot;
        _client = new SiemensS7Net(MapCpuType(cpuType), ipAddress)
        {
            Rack = (byte)rack,
            Slot = (byte)slot
        };
        ByteTransform = BigEndianTransform.Instance;
    }

    public override int DefaultPort => 102;
    public SiemensCpuType CpuType => _cpuType;
    public int Rack => _rack;
    public int Slot => _slot;

    protected override void ApplyConnectionSettings()
    {
        _client.IpAddress = IpAddress;
        _client.Port = Port;
        _client.Rack = (byte)_rack;
        _client.Slot = (byte)_slot;
        _client.ConnectTimeOut = ConnectTimeout;
        _client.ReceiveTimeOut = ReadTimeout;
    }

    protected override async Task<OperateResult> ConnectCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _client.ConnectServerAsync();
        return ToClearVisionResult(result);
    }

    protected override async Task DisconnectCoreAsync()
    {
        await _client.ConnectCloseAsync();
    }

    protected override async Task<OperateResult<byte[]>> ReadCoreAsync(string address, ushort length, CancellationToken ct)
    {
        var parsed = _addressParser.Parse(address);
        if (!parsed.IsSuccess || parsed.Content == null)
        {
            return OperateResult<byte[]>.Failure(parsed.ErrorCode, parsed.Message);
        }

        ct.ThrowIfCancellationRequested();
        var plcAddress = parsed.Content;
        if (plcAddress.DataType == PlcDataType.Bit)
        {
            var result = length <= 1
                ? await ReadSingleBoolAsync(address, ct)
                : await ReadBoolArrayAsync(address, length, ct);
            return result;
        }

        var byteCount = GetByteCount(plcAddress.DataType, length);
        return await ReadBytesProtocolAsync(address, byteCount, ct);
    }

    protected override async Task<OperateResult> WriteCoreAsync(string address, byte[] value, CancellationToken ct)
    {
        var parsed = _addressParser.Parse(address);
        if (!parsed.IsSuccess || parsed.Content == null)
        {
            return OperateResult.Failure(parsed.ErrorCode, parsed.Message);
        }

        ct.ThrowIfCancellationRequested();
        if (parsed.Content.DataType == PlcDataType.Bit)
        {
            return await WriteBoolProtocolAsync(address, value.Length > 0 && value[0] != 0, ct);
        }

        return await WriteBytesProtocolAsync(address, value, ct);
    }

    protected override async Task<bool> PingCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await ReadBytesProtocolAsync(GetHeartbeatAddress(), 2, ct);
        return result.IsSuccess;
    }

    protected virtual string GetHeartbeatAddress() => "MW0";

    protected virtual async Task<OperateResult<byte[]>> ReadBytesProtocolAsync(
        string address,
        ushort byteCount,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ToClearVisionResult(await _client.ReadAsync(address, byteCount));
    }

    protected virtual async Task<OperateResult<bool>> ReadBoolProtocolAsync(string address, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _client.ReadBoolAsync(address);
        return result.IsSuccess
            ? OperateResult<bool>.Success(result.Content)
            : OperateResult<bool>.Failure(result.ErrorCode, result.Message);
    }

    protected virtual async Task<OperateResult<bool[]>> ReadBoolProtocolAsync(
        string address,
        ushort length,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _client.ReadBoolAsync(address, length);
        return result.IsSuccess
            ? OperateResult<bool[]>.Success(result.Content)
            : OperateResult<bool[]>.Failure(result.ErrorCode, result.Message);
    }

    protected virtual async Task<OperateResult> WriteBoolProtocolAsync(
        string address,
        bool value,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ToClearVisionResult(await _client.WriteAsync(address, value));
    }

    protected virtual async Task<OperateResult> WriteBytesProtocolAsync(
        string address,
        byte[] value,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ToClearVisionResult(await _client.WriteAsync(address, value));
    }

    private async Task<OperateResult<byte[]>> ReadSingleBoolAsync(string address, CancellationToken ct)
    {
        var result = await ReadBoolProtocolAsync(address, ct);
        return result.IsSuccess
            ? OperateResult<byte[]>.Success(new[] { result.Content ? (byte)1 : (byte)0 })
            : OperateResult<byte[]>.Failure(result.ErrorCode, result.Message);
    }

    private async Task<OperateResult<byte[]>> ReadBoolArrayAsync(string address, ushort length, CancellationToken ct)
    {
        var result = await ReadBoolProtocolAsync(address, length, ct);
        return result.IsSuccess
            ? OperateResult<byte[]>.Success((result.Content ?? Array.Empty<bool>()).Select(value => value ? (byte)1 : (byte)0).ToArray())
            : OperateResult<byte[]>.Failure(result.ErrorCode, result.Message);
    }

    private static ushort GetByteCount(PlcDataType dataType, ushort count)
    {
        var typeSize = dataType switch
        {
            PlcDataType.Byte => 1,
            PlcDataType.Word or PlcDataType.Int16 => 2,
            PlcDataType.DWord or PlcDataType.Int32 or PlcDataType.Float => 4,
            PlcDataType.LWord or PlcDataType.Double => 8,
            _ => 2
        };
        return checked((ushort)(typeSize * count));
    }

    private static SiemensPLCS MapCpuType(SiemensCpuType cpuType)
    {
        return cpuType switch
        {
            SiemensCpuType.S7200 => SiemensPLCS.S200,
            SiemensCpuType.S7200Smart => SiemensPLCS.S200Smart,
            SiemensCpuType.S7300 => SiemensPLCS.S300,
            SiemensCpuType.S7400 => SiemensPLCS.S400,
            SiemensCpuType.S71200 => SiemensPLCS.S1200,
            SiemensCpuType.S71500 => SiemensPLCS.S1500,
            _ => SiemensPLCS.S1200
        };
    }
}
