using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class SerialCommunicationOperatorTests
{
    private readonly SerialCommunicationOperator _operator;

    public SerialCommunicationOperatorTests()
    {
        _operator = new SerialCommunicationOperator(Substitute.For<ILogger<SerialCommunicationOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeSerialCommunication()
    {
        _operator.OperatorType.Should().Be(OperatorType.SerialCommunication);
    }

    [Fact]
    public void ValidateParameters_WithInvalidBaudRate_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.SerialCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("PortName", "COM1", "string"));
        op.AddParameter(TestHelpers.CreateParameter("BaudRate", "invalid", "string"));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithDataBitsOutOfRange_ShouldFailBeforeOpeningPort()
    {
        var op = new Operator("test", OperatorType.SerialCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("PortName", "COM_DO_NOT_OPEN", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataBits", 4, "int"));

        var result = await _operator.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("数据位必须在 5-8 之间");
    }

    [Fact]
    public async Task ExecuteAsync_WithAsciiResponse_ShouldUseAsyncConnectionAndReturnBytesReceived()
    {
        var fakeConnection = new FakeSerialPortConnection(Encoding.ASCII.GetBytes("OK"));
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(
            ("SendData", "PING"),
            ("Encoding", "ASCII"),
            ("ResponseWaitMs", 1000));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        fakeConnection.Opened.Should().BeTrue();
        Encoding.ASCII.GetString(fakeConnection.WrittenBytes.ToArray()).Should().Be("PING");
        result.OutputData!["Response"].Should().Be("OK");
        result.OutputData["BytesReceived"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithHexResponse_ShouldReportRawByteCount()
    {
        var fakeConnection = new FakeSerialPortConnection([0x01, 0xA0]);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(
            ("SendData", "0A FF"),
            ("Encoding", "HEX"),
            ("ResponseWaitMs", 1000));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        fakeConnection.WrittenBytes.Should().Equal(0x0A, 0xFF);
        result.OutputData!["Response"].Should().Be("01 A0");
        result.OutputData["BytesReceived"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWaitingForResponse_ShouldHonorCancellation()
    {
        using var cts = new CancellationTokenSource();
        var fakeConnection = new FakeSerialPortConnection(
            [],
            onBytesToRead: cts.Cancel);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(("ResponseWaitMs", 30000));
        var execution = sut.ExecuteAsync(op, cancellationToken: cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ExecuteAsync_WithWiredDataInput_ShouldSendUpstreamValueInsteadOfStaticSendData()
    {
        var fakeConnection = new FakeSerialPortConnection(Encoding.ASCII.GetBytes("OK"));
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(
            ("SendData", "STATIC"),
            ("Encoding", "ASCII"),
            ("ResponseWaitMs", 1000));

        var inputs = new Dictionary<string, object>
        {
            ["Data"] = "DYN-42"
        };

        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        // 上游连线到 "发送数据" 端口的动态值应覆盖参数面板中的静态内容。
        Encoding.ASCII.GetString(fakeConnection.WrittenBytes.ToArray()).Should().Be("DYN-42");
    }

    [Fact]
    public async Task ExecuteAsync_WithJudgmentValueInput_ShouldSendJudgmentValue()
    {
        var fakeConnection = new FakeSerialPortConnection(Encoding.ASCII.GetBytes("OK"));
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(
            ("SendData", ""),
            ("Encoding", "ASCII"),
            ("ResponseWaitMs", 1000));

        // 模拟 ResultJudgment 上游输出展平合并进 inputs 的键。
        var inputs = new Dictionary<string, object>
        {
            ["JudgmentValue"] = "1"
        };

        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        Encoding.ASCII.GetString(fakeConnection.WrittenBytes.ToArray()).Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutWiredData_ShouldFallBackToStaticSendData()
    {
        var fakeConnection = new FakeSerialPortConnection(Encoding.ASCII.GetBytes("OK"));
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(
            ("SendData", "STATIC"),
            ("Encoding", "ASCII"),
            ("ResponseWaitMs", 1000));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue();
        // 无上游连线时保持原有行为，回退静态 SendData。
        Encoding.ASCII.GetString(fakeConnection.WrittenBytes.ToArray()).Should().Be("STATIC");
    }

    [Theory]
    [InlineData("One")]
    [InlineData("OnePointFive")]
    [InlineData("Two")]
    public void ValidateParameters_WithSupportedStopBits_ShouldBeValid(string stopBits)
    {
        var op = CreateOperator(("StopBits", stopBits));

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Odd")]
    [InlineData("Even")]
    public void ValidateParameters_WithSupportedParity_ShouldBeValid(string parity)
    {
        var op = CreateOperator(("Parity", parity));

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("UTF8")]
    [InlineData("ASCII")]
    [InlineData("HEX")]
    public void ValidateParameters_WithSupportedEncoding_ShouldBeValid(string encoding)
    {
        var op = CreateOperator(("Encoding", encoding));

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("One ")]
    [InlineData("one")]
    [InlineData("1")]
    [InlineData("Zero")]
    public async Task InvalidStopBits_ShouldFailValidationAndExecuteWithoutOpeningPort(string stopBits)
    {
        var fakeConnection = new FakeSerialPortConnection([]);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(("StopBits", stopBits), ("SendData", "PING"));

        var validation = sut.ValidateParameters(op);
        var result = await sut.ExecuteAsync(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith(SerialCommunicationOperator.InvalidStopBitsCode);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(SerialCommunicationOperator.InvalidStopBitsCode);
        fakeConnection.OpenCalls.Should().Be(0);
        fakeConnection.WriteCalls.Should().Be(0);
        fakeConnection.WrittenBytes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Even ")]
    [InlineData("even")]
    [InlineData("2")]
    [InlineData("Mark")]
    public async Task InvalidParity_ShouldFailValidationAndExecuteWithoutOpeningPort(string parity)
    {
        var fakeConnection = new FakeSerialPortConnection([]);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(("Parity", parity), ("SendData", "PING"));

        var validation = sut.ValidateParameters(op);
        var result = await sut.ExecuteAsync(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith(SerialCommunicationOperator.InvalidParityCode);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(SerialCommunicationOperator.InvalidParityCode);
        fakeConnection.OpenCalls.Should().Be(0);
        fakeConnection.WriteCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HEX ")]
    [InlineData("utf8")]
    [InlineData("Binary")]
    [InlineData("1")]
    public async Task InvalidEncoding_ShouldFailValidationAndExecuteWithoutOpeningPort(string encoding)
    {
        var fakeConnection = new FakeSerialPortConnection([]);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(("Encoding", encoding), ("SendData", "01 02"));

        var validation = sut.ValidateParameters(op);
        var result = await sut.ExecuteAsync(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith(SerialCommunicationOperator.InvalidEncodingCode);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(SerialCommunicationOperator.InvalidEncodingCode);
        fakeConnection.OpenCalls.Should().Be(0);
        fakeConnection.WriteCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("0AFF", new byte[] { 0x0A, 0xFF })]
    [InlineData("0A FF", new byte[] { 0x0A, 0xFF })]
    [InlineData("0A-FF", new byte[] { 0x0A, 0xFF })]
    [InlineData("00 7f-A5", new byte[] { 0x00, 0x7F, 0xA5 })]
    public async Task SupportedHexRepresentations_ShouldDispatchExpectedBytes(
        string payload,
        byte[] expected)
    {
        var fakeConnection = new FakeSerialPortConnection([]);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(
            ("Encoding", "HEX"),
            ("SendData", payload),
            ("ResponseWaitMs", 0));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        fakeConnection.OpenCalls.Should().Be(1);
        fakeConnection.WriteCalls.Should().Be(1);
        fakeConnection.WrittenBytes.Should().Equal(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("ABC")]
    [InlineData("0G")]
    [InlineData("0x01")]
    [InlineData("01:02")]
    [InlineData("01 ZZ")]
    public async Task InvalidHexPayload_ShouldFailValidationAndExecuteWithoutOpeningOrWriting(string payload)
    {
        var fakeConnection = new FakeSerialPortConnection([]);
        var sut = CreateWithConnection(fakeConnection);
        var op = CreateOperator(("Encoding", "HEX"), ("SendData", payload));

        var validation = sut.ValidateParameters(op);
        var result = await sut.ExecuteAsync(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith(SerialCommunicationOperator.InvalidHexCode);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(SerialCommunicationOperator.InvalidHexCode);
        fakeConnection.OpenCalls.Should().Be(0);
        fakeConnection.WriteCalls.Should().Be(0);
        fakeConnection.WrittenBytes.Should().BeEmpty();
    }

    private static SerialCommunicationOperator CreateWithConnection(FakeSerialPortConnection connection)
    {
        return new SerialCommunicationOperator(
            Substitute.For<ILogger<SerialCommunicationOperator>>(),
            _ => connection);
    }

    private static Operator CreateOperator(params (string Name, object Value)[] parameters)
    {
        var op = new Operator("test", OperatorType.SerialCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("PortName", "COM_TEST", "string"));
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 3000, "int"));
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(TestHelpers.CreateParameter(name, value, value.GetType().Name));
        }

        return op;
    }

    private sealed class FakeSerialPortConnection : SerialCommunicationOperator.ISerialPortConnection
    {
        private readonly byte[] _responseBytes;
        private readonly Action? _onBytesToRead;
        private int _readOffset;

        public FakeSerialPortConnection(byte[] responseBytes, Action? onBytesToRead = null)
        {
            _responseBytes = responseBytes;
            _onBytesToRead = onBytesToRead;
        }

        public bool Opened { get; private set; }

        public int OpenCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public List<byte> WrittenBytes { get; } = [];

        public int BytesToRead
        {
            get
            {
                _onBytesToRead?.Invoke();
                return _responseBytes.Length - _readOffset;
            }
        }

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCalls++;
            Opened = true;
            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCalls++;
            WrittenBytes.AddRange(bytes);
            return Task.CompletedTask;
        }

        public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesToCopy = Math.Min(buffer.Length, BytesToRead);
            Array.Copy(_responseBytes, _readOffset, buffer, 0, bytesToCopy);
            _readOffset += bytesToCopy;
            return Task.FromResult(bytesToCopy);
        }

        public void Dispose()
        {
        }
    }
}
