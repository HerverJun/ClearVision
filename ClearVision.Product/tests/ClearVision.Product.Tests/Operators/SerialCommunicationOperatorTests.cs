using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;

namespace ClearVision.Product.Tests.Operators;

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
            Opened = true;
            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
