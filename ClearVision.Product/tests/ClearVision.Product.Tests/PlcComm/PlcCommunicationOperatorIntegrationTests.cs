using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ClearVision.PlcComm.Interfaces;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.PlcComm;

[TestClassification(TestDomain.Plc, TestPurpose.Integration, TestLane.ReleaseManual, TestEvidenceType.IntegrationEvidence, TestOracleType.ExternalSystem, TestResourceRequirement.VirtualPlc, TestExpectedDuration.Long, TestFlakyPolicy.Blocking, "plc", Suites = "PlcRegression")]
[Collection("PLC Operator Integration")]
public class PlcCommunicationOperatorIntegrationTests : IDisposable
{
    private const string McProfileId = "test-mc-profile";
    private const string FinsProfileId = "test-fins-profile";

    public PlcCommunicationOperatorIntegrationTests()
    {
        ResetPlcOperatorState();
    }

    public void Dispose()
    {
        ResetPlcOperatorState();
    }

    [Fact]
    public async Task MitsubishiMcCommunicationOperator_ReadAsync_ShouldReturnConvertedWordValue()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = ServeMcReadAsync(listener, cts.Token, 0x12, 0x34);
        var sut = new MitsubishiMcCommunicationOperator(
            NullLogger<MitsubishiMcCommunicationOperator>.Instance,
            CreatePlcResolver(McProfileId, ExecutionPlcProtocols.MitsubishiMc, port, "D100"));
        var @operator = CreateOperator(
            "MC Read",
            OperatorType.MitsubishiMcCommunication,
            ("ProfileId", McProfileId, "string"),
            ("Address", "D100", "string"),
            ("Length", 1, "int"),
            ("Operation", "Read", "string"));

        var result = await sut.ExecuteAsync(@operator, cancellationToken: cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["DataType"].Should().Be("Word");
        result.OutputData["Value"].Should().Be((ushort)0x3412);

        await serverTask;
    }

    [Fact]
    public async Task MitsubishiMcCommunicationOperator_WriteAsync_ShouldUseUpstreamInputAndLittleEndianPayload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = ServeMcWriteAndCaptureAsync(listener, cts.Token);
        var sut = new MitsubishiMcCommunicationOperator(
            NullLogger<MitsubishiMcCommunicationOperator>.Instance,
            CreatePlcResolver(McProfileId, ExecutionPlcProtocols.MitsubishiMc, port, "D100"));
        var @operator = CreateOperator(
            "MC Write",
            OperatorType.MitsubishiMcCommunication,
            ("ProfileId", McProfileId, "string"),
            ("Address", "D100", "string"),
            ("Operation", "Write", "string"),
            ("WriteValue", string.Empty, "string"));

        var result = await sut.ExecuteAsync(
            @operator,
            new Dictionary<string, object> { ["Value"] = 4660 },
            cts.Token);

        var requestFrame = await serverTask;

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["Value"].Should().Be("4660");
        requestFrame[21].Should().Be(0x34);
        requestFrame[22].Should().Be(0x12);
    }

    [Fact]
    public void MitsubishiMcCommunicationOperator_WriteValidation_ShouldIgnoreDisabledReadAndPollingValues()
    {
        var sut = new MitsubishiMcCommunicationOperator(
            NullLogger<MitsubishiMcCommunicationOperator>.Instance,
            CreatePlcResolver(McProfileId, ExecutionPlcProtocols.MitsubishiMc, 5002, "D100"));
        var @operator = CreateOperator(
            "MC Write Validation",
            OperatorType.MitsubishiMcCommunication,
            ("ProfileId", McProfileId, "string"),
            ("Address", "D100", "string"),
            ("Operation", "Write", "string"),
            ("Length", 0, "int"),
            ("PollingMode", "<pending-polling-mode>", "string"),
            ("WriteValue", "1", "string"));

        sut.ValidateParameters(@operator).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task MitsubishiMcCommunicationOperator_RawEndpoint_ShouldFailBeforeConnectionFactory()
    {
        var factoryCalls = 0;
        var sut = new MitsubishiMcCommunicationOperator(
            NullLogger<MitsubishiMcCommunicationOperator>.Instance,
            CreatePlcResolver(McProfileId, ExecutionPlcProtocols.MitsubishiMc, 5002, "D100"),
            (_, _) =>
            {
                factoryCalls++;
                throw new InvalidOperationException("Raw target must be rejected before the connection factory.");
            });
        var @operator = CreateOperator(
            "MC Raw Endpoint",
            OperatorType.MitsubishiMcCommunication,
            ("ProfileId", McProfileId, "string"),
            ("IpAddress", "203.0.113.77", "string"),
            ("Address", "D100", "string"),
            ("Operation", "Read", "string"));

        sut.ValidateParameters(@operator).IsValid.Should().BeFalse();
        var result = await sut.ExecuteAsync(@operator);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("PLC_RAW_TARGET_FORBIDDEN:");
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task MitsubishiMcCommunicationOperator_ReadAsync_WaitForValue_ShouldPollUntilMatched()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = ServeMcReadSequenceAsync(
            listener,
            cts.Token,
            new byte[] { 0x00, 0x00 },
            new byte[] { 0x01, 0x00 });

        var sut = new MitsubishiMcCommunicationOperator(
            NullLogger<MitsubishiMcCommunicationOperator>.Instance,
            CreatePlcResolver(McProfileId, ExecutionPlcProtocols.MitsubishiMc, port, "D100"));
        var @operator = CreateOperator(
            "MC Read Polling",
            OperatorType.MitsubishiMcCommunication,
            ("ProfileId", McProfileId, "string"),
            ("Address", "D100", "string"),
            ("Length", 1, "int"),
            ("Operation", "Read", "string"),
            ("PollingMode", "WaitForValue", "string"),
            ("PollingCondition", "Equal", "string"),
            ("PollingValue", "1", "string"),
            ("PollingTimeout", 3000, "int"),
            ("PollingInterval", 20, "int"));

        var result = await sut.ExecuteAsync(@operator, cancellationToken: cts.Token);
        var requestCount = await serverTask;

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["Value"].Should().Be((ushort)1);
        result.OutputData["PollingMatched"].Should().Be(true);
        result.OutputData["PollingReadCount"].Should().Be(2);
        result.OutputData["ConnectionSource"].Should().Be("ServerProfile");
        requestCount.Should().Be(2);
    }

    [Fact]
    public async Task OmronFinsCommunicationOperator_ReadAsync_ShouldReturnConvertedWordValue()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = ServeFinsReadAsync(listener, cts.Token, 0x12, 0x34);
        var sut = new OmronFinsCommunicationOperator(
            NullLogger<OmronFinsCommunicationOperator>.Instance,
            CreatePlcResolver(FinsProfileId, ExecutionPlcProtocols.OmronFins, port, "DM100"));
        var @operator = CreateOperator(
            "FINS Read",
            OperatorType.OmronFinsCommunication,
            ("ProfileId", FinsProfileId, "string"),
            ("Address", "DM100", "string"),
            ("Length", 1, "int"),
            ("Operation", "Read", "string"));

        var result = await sut.ExecuteAsync(@operator, cancellationToken: cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["DataType"].Should().Be("Word");
        result.OutputData["Value"].Should().Be((ushort)0x1234);

        await serverTask;
    }

    [Fact]
    public async Task OmronFinsCommunicationOperator_WriteAsync_ShouldUseUpstreamInputAndBigEndianPayload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = ServeFinsWriteAndCaptureAsync(listener, cts.Token);
        var sut = new OmronFinsCommunicationOperator(
            NullLogger<OmronFinsCommunicationOperator>.Instance,
            CreatePlcResolver(FinsProfileId, ExecutionPlcProtocols.OmronFins, port, "DM100"));
        var @operator = CreateOperator(
            "FINS Write",
            OperatorType.OmronFinsCommunication,
            ("ProfileId", FinsProfileId, "string"),
            ("Address", "DM100", "string"),
            ("Operation", "Write", "string"),
            ("WriteValue", string.Empty, "string"));

        var result = await sut.ExecuteAsync(
            @operator,
            new Dictionary<string, object> { ["Data"] = 4660 },
            cts.Token);

        var requestFrame = await serverTask;

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Status"].Should().Be(true);
        result.OutputData["Value"].Should().Be("4660");
        requestFrame[^2].Should().Be(0x12);
        requestFrame[^1].Should().Be(0x34);
    }

    private static Operator CreateOperator(
        string name,
        OperatorType operatorType,
        params (string Name, object Value, string DataType)[] parameters)
    {
        var @operator = new Operator(name, operatorType, 0, 0);
        foreach (var (parameterName, value, dataType) in parameters)
        {
            @operator.AddParameter(new Parameter(Guid.NewGuid(), parameterName, parameterName, string.Empty, dataType, value));
        }

        return @operator;
    }

    private static IExecutionResourceProfileResolver CreatePlcResolver(
        string profileId,
        string protocol,
        int port,
        string address)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig
        {
            ExecutionResources = new ExecutionResourceProfilesConfig
            {
                PlcProfiles =
                [
                    new PlcExecutionResourceProfile
                    {
                        Id = profileId,
                        Enabled = true,
                        Protocol = protocol,
                        Host = IPAddress.Loopback.ToString(),
                        Port = port,
                        CpuType = "S71200",
                        Rack = 0,
                        Slot = 1,
                        UnitId = 1,
                        Bindings =
                        [
                            new PlcExecutionResourceBinding
                            {
                                Address = address,
                                DataType = "Word",
                                CanRead = true,
                                CanWrite = true,
                                MaxElementCount = 999,
                                AllowedFunctionCodes =
                                [
                                    "ReadCoils",
                                    "ReadHolding",
                                    "WriteSingle",
                                    "WriteMultiple"
                                ]
                            }
                        ]
                    }
                ]
            }
        });
        return new ServerExecutionResourceProfileResolver(configurationService);
    }

    private static void ResetPlcOperatorState()
    {
        PlcCommunicationOperatorBase.StopHeartbeat();
        PlcCommunicationOperatorBase.ClearConnectionPoolAsync().GetAwaiter().GetResult();
    }

    private static async Task<byte[]> ServeMcWriteAndCaptureAsync(TcpListener listener, CancellationToken ct)
    {
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var stream = server.GetStream();

        var request = await ReadMcFrameAsync(stream, ct);
        await WriteInChunksAsync(stream, BuildMcWriteResponse(), ct, 1, 2, 3);
        return request;
    }

    private static async Task ServeMcReadAsync(TcpListener listener, CancellationToken ct, params byte[] data)
    {
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var stream = server.GetStream();

        _ = await ReadMcFrameAsync(stream, ct);
        await WriteInChunksAsync(stream, BuildMcReadResponse(data), ct, 2, 2, 3, 1);
    }

    private static async Task<int> ServeMcReadSequenceAsync(TcpListener listener, CancellationToken ct, params byte[][] responses)
    {
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var stream = server.GetStream();

        var readCount = 0;
        foreach (var responsePayload in responses)
        {
            _ = await ReadMcFrameAsync(stream, ct);
            readCount++;
            await WriteInChunksAsync(stream, BuildMcReadResponse(responsePayload), ct, 2, 2, 3, 1);
        }

        return readCount;
    }

    private static async Task<byte[]> ServeFinsWriteAndCaptureAsync(TcpListener listener, CancellationToken ct)
    {
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var stream = server.GetStream();

        var handshakeRequest = new byte[20];
        await ReadExactAsync(stream, handshakeRequest, ct);
        await WriteInChunksAsync(stream, BuildFinsNodeAddressResponse(clientNode: 0x22, serverNode: 0x11), ct, 3, 5, 4);

        var request = await ReadFinsWrappedFrameAsync(stream, ct);
        await WriteInChunksAsync(stream, BuildFinsWriteResponse(request), ct, 2, 6, 4, 3);
        return request;
    }

    private static async Task ServeFinsReadAsync(TcpListener listener, CancellationToken ct, params byte[] data)
    {
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var stream = server.GetStream();

        var handshakeRequest = new byte[20];
        await ReadExactAsync(stream, handshakeRequest, ct);
        await WriteInChunksAsync(stream, BuildFinsNodeAddressResponse(clientNode: 0x22, serverNode: 0x11), ct, 4, 4, 4);

        var request = await ReadFinsWrappedFrameAsync(stream, ct);
        await WriteInChunksAsync(stream, BuildFinsReadResponse(request, data), ct, 5, 5, 5, 5);
    }

    private static async Task<byte[]> ReadMcFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[9];
        await ReadExactAsync(stream, header, ct);
        var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7, 2));

        var trailingLength = dataLength;
        var frame = new byte[header.Length + trailingLength];
        Array.Copy(header, frame, header.Length);

        if (trailingLength > 0)
        {
            await ReadExactAsync(stream, frame.AsMemory(header.Length, trailingLength), ct);
        }

        return frame;
    }

    private static async Task<byte[]> ReadFinsWrappedFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[16];
        await ReadExactAsync(stream, header, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));
        length.Should().BeGreaterOrEqualTo(0);
        var payloadLength = Math.Max(0, length - 8);

        var frame = new byte[16 + payloadLength];
        Array.Copy(header, frame, header.Length);

        if (payloadLength > 0)
        {
            await ReadExactAsync(stream, frame.AsMemory(16, payloadLength), ct);
        }

        return frame;
    }

    private static async Task WriteInChunksAsync(NetworkStream stream, byte[] payload, CancellationToken ct, params int[] chunkSizes)
    {
        var offset = 0;
        foreach (var size in chunkSizes)
        {
            if (offset >= payload.Length)
            {
                break;
            }

            var toWrite = Math.Min(size, payload.Length - offset);
            await stream.WriteAsync(payload.AsMemory(offset, toWrite), ct);
            await stream.FlushAsync(ct);
            offset += toWrite;
            await Task.Delay(10, ct);
        }

        if (offset < payload.Length)
        {
            await stream.WriteAsync(payload.AsMemory(offset), ct);
            await stream.FlushAsync(ct);
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (read == 0)
            {
                throw new IOException("Connection closed before expected bytes were received.");
            }

            totalRead += read;
        }
    }

    private static byte[] BuildMcWriteResponse()
    {
        var response = new byte[11];
        response[0] = 0xD0;
        response[1] = 0x00;
        response[2] = 0x00;
        response[3] = 0xFF;
        response[4] = 0xFF;
        response[5] = 0x03;
        response[6] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(7, 2), 2);
        response[9] = 0x00;
        response[10] = 0x00;
        return response;
    }

    private static byte[] BuildMcReadResponse(params byte[] data)
    {
        var response = new byte[11 + data.Length];
        response[0] = 0xD0;
        response[1] = 0x00;
        response[2] = 0x00;
        response[3] = 0xFF;
        response[4] = 0xFF;
        response[5] = 0x03;
        response[6] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(7, 2), (ushort)(2 + data.Length));
        response[9] = 0x00;
        response[10] = 0x00;
        if (data.Length > 0)
        {
            data.CopyTo(response, 11);
        }

        return response;
    }

    private static byte[] BuildFinsNodeAddressResponse(byte clientNode, byte serverNode)
    {
        var response = new byte[24];
        response[0] = 0x46;
        response[1] = 0x49;
        response[2] = 0x4E;
        response[3] = 0x53;
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4, 4), 16u);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(8, 4), 1u);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(12, 4), 0u);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(16, 4), serverNode);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(20, 4), clientNode);
        return response;
    }

    private static byte[] BuildFinsWriteResponse(byte[] request)
    {
        var finsFrame = new byte[14];
        finsFrame[0] = 0xC0;
        finsFrame[1] = 0x00;
        finsFrame[2] = 0x02;
        finsFrame[3] = 0x00;
        finsFrame[4] = GetFinsRequestClientNode(request);
        finsFrame[5] = 0x00;
        finsFrame[6] = 0x00;
        finsFrame[7] = GetFinsRequestServerNode(request);
        finsFrame[8] = 0x00;
        finsFrame[9] = GetFinsRequestSid(request);
        finsFrame[10] = 0x01;
        finsFrame[11] = 0x02;
        finsFrame[12] = 0x00;
        finsFrame[13] = 0x00;
        return WrapFinsTcpFrame(finsFrame);
    }

    private static byte[] BuildFinsReadResponse(byte[] request, byte[] data)
    {
        var finsFrame = new byte[14 + data.Length];
        finsFrame[0] = 0xC0;
        finsFrame[1] = 0x00;
        finsFrame[2] = 0x02;
        finsFrame[3] = 0x00;
        finsFrame[4] = GetFinsRequestClientNode(request);
        finsFrame[5] = 0x00;
        finsFrame[6] = 0x00;
        finsFrame[7] = GetFinsRequestServerNode(request);
        finsFrame[8] = 0x00;
        finsFrame[9] = GetFinsRequestSid(request);
        finsFrame[10] = 0x01;
        finsFrame[11] = 0x01;
        finsFrame[12] = 0x00;
        finsFrame[13] = 0x00;
        data.CopyTo(finsFrame, 14);
        return WrapFinsTcpFrame(finsFrame);
    }

    private static byte GetFinsRequestClientNode(byte[] request) => request[23];

    private static byte GetFinsRequestServerNode(byte[] request) => request[20];

    private static byte GetFinsRequestSid(byte[] request) => request[25];

    private static byte[] WrapFinsTcpFrame(byte[] finsFrame)
    {
        var response = new byte[16 + finsFrame.Length];
        response[0] = 0x46;
        response[1] = 0x49;
        response[2] = 0x4E;
        response[3] = 0x53;
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4, 4), (uint)(8 + finsFrame.Length));
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(8, 4), 2u);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(12, 4), 0u);
        finsFrame.CopyTo(response, 16);
        return response;
    }
}

[CollectionDefinition("PLC Operator Integration", DisableParallelization = true)]
public sealed class PlcOperatorIntegrationCollectionDefinition
{
}
