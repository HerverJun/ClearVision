using Acme.PlcComm.Common;
using Acme.PlcComm.Core;
using HslCommunication.Profinet.Melsec;
using Microsoft.Extensions.Logging;

namespace Acme.PlcComm.Mitsubishi;

public class MitsubishiMcClient : HaoPlcClientBase
{
    private readonly MelsecMcNet _client;
    private readonly McAddressParser _addressParser = new();

    public MitsubishiMcClient(string ipAddress, ILogger? logger = null)
        : base(ipAddress, 5002, logger)
    {
        _client = new MelsecMcNet(ipAddress, 5002);
        ByteTransform = LittleEndianTransform.Instance;
    }

    public override int DefaultPort => 5002;

    protected override void ApplyConnectionSettings()
    {
        _client.IpAddress = IpAddress;
        _client.Port = Port;
        _client.ConnectTimeOut = ConnectTimeout;
        _client.ReceiveTimeOut = ReadTimeout;
    }

    protected override async Task<OperateResult> ConnectCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var hslTask = _client.ConnectServerAsync();
        var result = await WithCancellationAsync(hslTask, ct);
        return ToAcmeResult(result);
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
        if (parsed.Content.DataType == PlcDataType.Bit)
        {
            var result = length <= 1
                ? await ReadSingleBoolAsync(address, ct)
                : await ReadBoolArrayAsync(address, length, ct);
            return result;
        }

        var hslTask = _client.ReadAsync(address, length);
        var readResult = await WithCancellationAsync(hslTask, ct);
        return ToAcmeResult(readResult);
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
            if (value.Length <= 1)
            {
                var writeTask = _client.WriteAsync(address, value.Length > 0 && value[0] != 0);
                var writeResult = await WithCancellationAsync(writeTask, ct);
                return ToAcmeResult(writeResult);
            }

            var batchWriteTask = _client.WriteAsync(address, value.Select(item => item != 0).ToArray());
            var batchWriteResult = await WithCancellationAsync(batchWriteTask, ct);
            return ToAcmeResult(batchWriteResult);
        }

        var genericWriteTask = _client.WriteAsync(address, value);
        var genericWriteResult = await WithCancellationAsync(genericWriteTask, ct);
        return ToAcmeResult(genericWriteResult);
    }

    protected override async Task<bool> PingCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pingTask = _client.ReadAsync("D0", 1);
        var result = await WithCancellationAsync(pingTask, ct);
        return result.IsSuccess;
    }

    private async Task<OperateResult<byte[]>> ReadSingleBoolAsync(string address, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var readTask = _client.ReadBoolAsync(address);
        var result = await WithCancellationAsync(readTask, ct);
        return result.IsSuccess
            ? OperateResult<byte[]>.Success(new[] { result.Content ? (byte)1 : (byte)0 })
            : OperateResult<byte[]>.Failure(result.ErrorCode, result.Message);
    }

    private async Task<OperateResult<byte[]>> ReadBoolArrayAsync(string address, ushort length, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var readTask = _client.ReadBoolAsync(address, length);
        var result = await WithCancellationAsync(readTask, ct);
        return result.IsSuccess
            ? OperateResult<byte[]>.Success((result.Content ?? Array.Empty<bool>()).Select(item => item ? (byte)1 : (byte)0).ToArray())
            : OperateResult<byte[]>.Failure(result.ErrorCode, result.Message);
    }

    protected override void ForcePhysicalClose()
    {
        try
        {
            _client.ConnectClose();
        }
        catch
        {
        }
    }
}
