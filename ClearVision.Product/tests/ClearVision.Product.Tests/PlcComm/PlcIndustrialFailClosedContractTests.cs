using ClearVision.PlcComm;
using ClearVision.PlcComm.Common;
using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Interfaces;
using ClearVision.PlcComm.Siemens;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.PlcComm;

[TestClassification(TestDomain.Plc, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "plc", Suites = "PlcRegression")]
[Collection("PLC Operator Integration")]
public sealed class PlcIndustrialFailClosedContractTests : IAsyncLifetime
{
    private static readonly string[] ExpectedPollingConditions =
    [
        "Equal",
        "NotEqual",
        "GreaterThan",
        "LessThan",
        "GreaterOrEqual",
        "LessOrEqual"
    ];

    public static IEnumerable<object[]> SupportedPollingConditions =>
        ExpectedPollingConditions.Select(condition => new object[] { condition });

    public static IEnumerable<object[]> SupportedPollingConditionExecutionCases =>
        new[]
        {
            new object[] { "Equal", (ushort)1, "1" },
            new object[] { "NotEqual", (ushort)1, "2" },
            new object[] { "GreaterThan", (ushort)2, "1" },
            new object[] { "LessThan", (ushort)1, "2" },
            new object[] { "GreaterOrEqual", (ushort)2, "2" },
            new object[] { "LessOrEqual", (ushort)2, "2" }
        }
        .SelectMany(testCase => new[] { "S7", "MC", "FINS" }
            .Select(protocol => new[] { protocol, testCase[0], testCase[1], testCase[2] }));

    public static IEnumerable<object[]> InvalidPollingConditionCases =>
        new[] { "", " ", "equal", "Equal ", "GreaterThanOrEqual", "Unknown" }
            .SelectMany(condition => new[] { "S7", "MC", "FINS" }
                .Select(protocol => new object[] { protocol, condition }));

    public Task InitializeAsync() => ResetAsync();

    public Task DisposeAsync() => ResetAsync();

    [Theory]
    [MemberData(nameof(SupportedPollingConditions))]
    public void AllPlcValidators_ShouldAcceptTheSamePollingConditionMatrix(string condition)
    {
        foreach (var protocol in new[] { "S7", "MC", "FINS" })
        {
            var client = new SpyPlcClient(BigEndianTransform.Instance, [[0x00, 0x01]]);
            var sut = CreateSut(protocol, client, static () => { });
            var @operator = CreateOperator(protocol, "Read", "WaitForValue", condition);

            sut.ValidateParameters(@operator).IsValid.Should().BeTrue(
                $"{protocol} should accept polling condition {condition}");
        }
    }

    [Theory]
    [InlineData("Equal", 1, 1, true)]
    [InlineData("NotEqual", 1, 2, true)]
    [InlineData("GreaterThan", 2, 1, true)]
    [InlineData("LessThan", 1, 2, true)]
    [InlineData("GreaterOrEqual", 2, 2, true)]
    [InlineData("LessOrEqual", 2, 2, true)]
    public void SharedPollingContract_ShouldEvaluateEverySupportedCondition(
        string condition,
        int current,
        int target,
        bool expected)
    {
        PlcOperatorParameterContract.EvaluatePollingCondition(current, condition, target.ToString())
            .Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(SupportedPollingConditionExecutionCases))]
    public async Task AllPlcDirectExecutors_ShouldAcceptAndDispatchEverySupportedPollingCondition(
        string protocol,
        string condition,
        ushort current,
        string target)
    {
        IByteTransform byteTransform = protocol == "MC"
            ? LittleEndianTransform.Instance
            : BigEndianTransform.Instance;
        var bytes = protocol == "MC"
            ? new[] { (byte)(current & 0xff), (byte)(current >> 8) }
            : new[] { (byte)(current >> 8), (byte)(current & 0xff) };
        var client = new SpyPlcClient(byteTransform, [bytes]);
        var factoryCalls = 0;
        var sut = CreateSut(protocol, client, () => factoryCalls++);
        var @operator = CreateOperator(protocol, "Read", "WaitForValue", condition);
        ReplaceParameter(@operator, "PollingValue", target);

        var result = await sut.ExecuteAsync(@operator);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["PollingMatched"].Should().Be(true);
        factoryCalls.Should().Be(1);
        client.ConnectCalls.Should().Be(1);
        client.ReadCalls.Should().Be(1);
        client.WriteCalls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(InvalidPollingConditionCases))]
    public async Task InvalidPollingCondition_ShouldFailBothLayersWithZeroDeviceIo(
        string protocol,
        string condition)
    {
        var client = new SpyPlcClient(BigEndianTransform.Instance, [[0x00, 0x01]]);
        var factoryCalls = 0;
        var sut = CreateSut(protocol, client, () => factoryCalls++);
        var @operator = CreateOperator(protocol, "Read", "WaitForValue", condition);

        var validation = sut.ValidateParameters(@operator);
        var result = await sut.ExecuteAsync(@operator);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith("PLC_POLLING_CONDITION_INVALID:");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("PLC_POLLING_CONDITION_INVALID:");
        factoryCalls.Should().Be(0);
        client.ConnectCalls.Should().Be(0);
        client.ReadCalls.Should().Be(0);
        client.WriteCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("S7", "")]
    [InlineData("S7", " ")]
    [InlineData("S7", "Read ")]
    [InlineData("S7", "read")]
    [InlineData("S7", "write")]
    [InlineData("S7", "WaitForValue")]
    [InlineData("FINS", "")]
    [InlineData("FINS", " ")]
    [InlineData("FINS", "Write ")]
    [InlineData("FINS", "read")]
    [InlineData("FINS", "write")]
    [InlineData("FINS", "ReadOnce")]
    public async Task InvalidS7OrFinsOperation_ShouldFailBeforeWriteValueResolutionOrDeviceIo(
        string protocol,
        string operation)
    {
        var client = new SpyPlcClient(BigEndianTransform.Instance, [[0x00, 0x01]]);
        var factoryCalls = 0;
        var spyValue = new ToStringSpy();
        var sut = CreateSut(protocol, client, () => factoryCalls++);
        var @operator = CreateOperator(protocol, operation, "None", "Equal");

        var validation = sut.ValidateParameters(@operator);
        var result = await sut.ExecuteAsync(
            @operator,
            new Dictionary<string, object> { ["Data"] = spyValue });

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith("PLC_OPERATION_INVALID:");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("PLC_OPERATION_INVALID:");
        spyValue.ToStringCalls.Should().Be(0);
        factoryCalls.Should().Be(0);
        client.ConnectCalls.Should().Be(0);
        client.ReadCalls.Should().Be(0);
        client.WriteCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void AllPlcValidators_ShouldAcceptReadAndWrite(string operation)
    {
        foreach (var protocol in new[] { "S7", "MC", "FINS" })
        {
            var client = new SpyPlcClient(BigEndianTransform.Instance, [[0x00, 0x01]]);
            var sut = CreateSut(protocol, client, static () => { });
            var @operator = CreateOperator(protocol, operation, "None", "Equal");
            if (operation == "Write")
            {
                @operator.AddParameter(CreateParameter("WriteValue", "1"));
            }

            sut.ValidateParameters(@operator).IsValid.Should().BeTrue(
                $"{protocol} should preserve the supported {operation} operation");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Read ")]
    [InlineData("Unknown")]
    public async Task MitsubishiInvalidOperationRegression_ShouldKeepZeroDeviceIo(string operation)
    {
        var client = new SpyPlcClient(LittleEndianTransform.Instance, [[0x01, 0x00]]);
        var factoryCalls = 0;
        var sut = CreateSut("MC", client, () => factoryCalls++);
        var @operator = CreateOperator("MC", operation, "None", "Equal");

        var validation = sut.ValidateParameters(@operator);
        var result = await sut.ExecuteAsync(@operator);

        validation.IsValid.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Operation must be Read or Write.");
        factoryCalls.Should().Be(0);
        client.ConnectCalls.Should().Be(0);
        client.ReadCalls.Should().Be(0);
        client.WriteCalls.Should().Be(0);
    }

    [Fact]
    public async Task FinsPollingNone_ShouldPreserveSingleReadBehavior()
    {
        var client = new SpyPlcClient(BigEndianTransform.Instance, [[0x00, 0x07], [0x00, 0x08]]);
        var sut = CreateSut("FINS", client, static () => { });
        var @operator = CreateOperator("FINS", "Read", "None", "Equal");

        var result = await sut.ExecuteAsync(@operator);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be((ushort)7);
        client.ReadCalls.Should().Be(1);
    }

    [Fact]
    public async Task FinsWaitForValue_ShouldPollUntilMatched()
    {
        var client = new SpyPlcClient(
            BigEndianTransform.Instance,
            [[0x00, 0x00], [0x00, 0x01]]);
        var sut = CreateSut("FINS", client, static () => { });
        var @operator = CreateOperator("FINS", "Read", "WaitForValue", "Equal");
        ReplaceParameter(@operator, "PollingValue", "1");
        ReplaceParameter(@operator, "PollingInterval", 10);
        ReplaceParameter(@operator, "PollingTimeout", 1000);

        var result = await sut.ExecuteAsync(@operator);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be((ushort)1);
        result.OutputData["PollingMatched"].Should().Be(true);
        result.OutputData["PollingReadCount"].Should().Be(2);
        client.ReadCalls.Should().Be(2);
    }

    [Fact]
    public async Task FinsWaitForValue_ShouldReturnStableTimeoutWithoutBusyLoop()
    {
        var client = new SpyPlcClient(BigEndianTransform.Instance, [[0x00, 0x00]]);
        var sut = CreateSut("FINS", client, static () => { });
        var @operator = CreateOperator("FINS", "Read", "WaitForValue", "Equal");
        ReplaceParameter(@operator, "PollingValue", "1");
        ReplaceParameter(@operator, "PollingInterval", 20);
        ReplaceParameter(@operator, "PollingTimeout", 100);

        var result = await sut.ExecuteAsync(@operator);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("FINS_POLLING_TIMEOUT:");
        client.ReadCalls.Should().BeInRange(2, 8);
    }

    [Fact]
    public async Task FinsWaitForValue_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new SpyPlcClient(
            BigEndianTransform.Instance,
            [[0x00, 0x00]],
            onRead: cancellation.Cancel);
        var sut = CreateSut("FINS", client, static () => { });
        var @operator = CreateOperator("FINS", "Read", "WaitForValue", "Equal");
        ReplaceParameter(@operator, "PollingValue", "1");
        ReplaceParameter(@operator, "PollingInterval", 100);
        ReplaceParameter(@operator, "PollingTimeout", 1000);

        var execution = sut.ExecuteAsync(@operator, cancellationToken: cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        client.ReadCalls.Should().Be(1);
    }

    private static IOperatorExecutor CreateSut(
        string protocol,
        SpyPlcClient client,
        Action onFactoryCall)
    {
        return protocol switch
        {
            "S7" => new SiemensS7CommunicationOperator(
                NullLogger<SiemensS7CommunicationOperator>.Instance,
                (_, _, _, _, _) =>
                {
                    onFactoryCall();
                    return client;
                }),
            "MC" => new MitsubishiMcCommunicationOperator(
                NullLogger<MitsubishiMcCommunicationOperator>.Instance,
                (_, _) =>
                {
                    onFactoryCall();
                    return client;
                }),
            "FINS" => new OmronFinsCommunicationOperator(
                NullLogger<OmronFinsCommunicationOperator>.Instance,
                (_, _) =>
                {
                    onFactoryCall();
                    return client;
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null)
        };
    }

    private static Operator CreateOperator(
        string protocol,
        string operation,
        string pollingMode,
        string pollingCondition)
    {
        var (operatorType, address) = protocol switch
        {
            "S7" => (OperatorType.SiemensS7Communication, "DB1.DBW100"),
            "MC" => (OperatorType.MitsubishiMcCommunication, "D100"),
            "FINS" => (OperatorType.OmronFinsCommunication, "DM100"),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null)
        };

        var @operator = new Operator($"{protocol} Contract", operatorType, 0, 0);
        @operator.AddParameter(CreateParameter("IpAddress", "127.0.0.1"));
        @operator.AddParameter(CreateParameter("Port", 32001));
        @operator.AddParameter(CreateParameter("Address", address));
        @operator.AddParameter(CreateParameter("Length", 1));
        @operator.AddParameter(CreateParameter("DataType", "Word"));
        @operator.AddParameter(CreateParameter("Operation", operation));
        @operator.AddParameter(CreateParameter("PollingMode", pollingMode));
        @operator.AddParameter(CreateParameter("PollingCondition", pollingCondition));
        @operator.AddParameter(CreateParameter("PollingValue", "1"));
        @operator.AddParameter(CreateParameter("PollingTimeout", 1000));
        @operator.AddParameter(CreateParameter("PollingInterval", 10));
        return @operator;
    }

    private static Parameter CreateParameter(string name, object value) =>
        new(Guid.NewGuid(), name, name, string.Empty, "string", value);

    private static void ReplaceParameter(Operator @operator, string name, object value)
    {
        var existing = @operator.Parameters.Single(parameter => parameter.Name == name);
        existing.SetValue(value);
    }

    private static async Task ResetAsync()
    {
        PlcCommunicationOperatorBase.StopHeartbeat();
        await PlcCommunicationOperatorBase.ClearConnectionPoolAsync();
    }

    private sealed class ToStringSpy
    {
        public int ToStringCalls { get; private set; }

        public override string ToString()
        {
            ToStringCalls++;
            return "1";
        }
    }

    private sealed class SpyPlcClient : IPlcClient
    {
        private readonly Queue<byte[]> _readValues;
        private readonly byte[] _fallbackRead;
        private readonly Action? _onRead;

        public SpyPlcClient(
            IByteTransform byteTransform,
            IEnumerable<byte[]> readValues,
            Action? onRead = null)
        {
            ByteTransform = byteTransform;
            _readValues = new Queue<byte[]>(readValues.Select(value => value.ToArray()));
            _fallbackRead = _readValues.LastOrDefault()?.ToArray() ?? [0x00, 0x00];
            _onRead = onRead;
        }

        public int ConnectCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public string IpAddress => "127.0.0.1";
        public int Port => 32001;
        public bool IsConnected { get; private set; }
        public int ConnectTimeout { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public ReconnectPolicy ReconnectPolicy { get; set; } = new();
        public IByteTransform ByteTransform { get; }
        public event EventHandler<ConnectionEventArgs>? Connected { add { } remove { } }
        public event EventHandler<DisconnectionEventArgs>? Disconnected { add { } remove { } }
        public event EventHandler<PlcErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ConnectCalls++;
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<OperateResult<byte[]>> ReadAsync(
            string address,
            ushort length,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ReadCalls++;
            _onRead?.Invoke();
            var value = _readValues.Count > 0 ? _readValues.Dequeue() : _fallbackRead;
            return Task.FromResult(OperateResult<byte[]>.Success(value.ToArray()));
        }

        public Task<OperateResult> WriteAsync(
            string address,
            byte[] value,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            WriteCalls++;
            return Task.FromResult(OperateResult.Success());
        }

        public Task<OperateResult<T>> ReadAsync<T>(string address, CancellationToken ct = default)
            where T : struct => throw new NotSupportedException();

        public Task<OperateResult> WriteAsync<T>(string address, T value, CancellationToken ct = default)
            where T : struct => throw new NotSupportedException();

        public Task<OperateResult<Dictionary<string, byte[]>>> ReadBatchAsync(
            string[] addresses,
            ushort[] lengths,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OperateResult<string>> ReadStringAsync(
            string address,
            ushort length,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OperateResult> WriteStringAsync(
            string address,
            string value,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);

        public void Dispose()
        {
            IsConnected = false;
        }
    }
}
