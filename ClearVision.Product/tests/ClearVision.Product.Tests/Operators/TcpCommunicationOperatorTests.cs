using System.Net;
using System.Net.Sockets;
using System.Text;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[Collection(RuntimeConcurrencyCollection.Name)]
public class TcpCommunicationOperatorTests
{
    private readonly TcpCommunicationOperator _operator;

    public TcpCommunicationOperatorTests()
    {
        _operator = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeTcpCommunication()
    {
        _operator.OperatorType.Should().Be(OperatorType.TcpCommunication);
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidPort_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(new(Guid.NewGuid(), "Port", "Port", "", "int", 70000, 0, 65535, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithServerModeAndNoProfile_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Mode", "Server", "string"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("全局 TCP 通讯页", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithUnsupportedInlineServerMode_ShouldReturnFailure()
    {
        var op = new Operator("test", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Mode", "Server", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("全局 TCP 通讯页");
    }

    [Fact]
    public async Task ExecuteAsync_WithProfileId_ShouldSendThroughGlobalManager()
    {
        var manager = Substitute.For<ITcpDeviceManager>();
        TcpDeviceSendRequest? capturedRequest = null;
        manager
            .SendAsync(
                "robot-main",
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-profile", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "robot-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("UseGlobalProfile", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("SendData", "from-send-data", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "from-input"
        });

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("from-input");
        await manager.Received(1).SendAsync("robot-main", Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().SendTransientAsync(Arg.Any<TcpCommunicationProfile>(), Arg.Any<TcpDeviceSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithInputData_ShouldOverrideSendDataForLegacyFallback()
    {
        var manager = Substitute.For<ITcpDeviceManager>();
        TcpDeviceSendRequest? capturedRequest = null;
        TcpCommunicationProfile? capturedProfile = null;
        manager
            .SendTransientAsync(
                Arg.Do<TcpCommunicationProfile>(profile => capturedProfile = profile),
                Arg.Do<TcpDeviceSendRequest>(request => capturedRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TcpDeviceSendResult.Ok("ok", "ACK")));
        var sut = new TcpCommunicationOperator(Substitute.For<ILogger<TcpCommunicationOperator>>(), manager);
        var op = new Operator("tcp-legacy", OperatorType.TcpCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("IpAddress", "127.0.0.1", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Port", 9100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("SendData", "from-send-data", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Data"] = "from-input"
        });

        result.IsSuccess.Should().BeTrue();
        capturedProfile.Should().NotBeNull();
        capturedProfile!.RemoteHost.Should().Be("127.0.0.1");
        capturedProfile.RemotePort.Should().Be(9100);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Payload.Should().Be("from-input");
    }

    [Fact]
    public async Task ExecuteAsync_WithLegacyIpPortSendData_ShouldRemainCompatible()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSingleEchoServerAsync(listener, cts.Token);

        try
        {
            var op = new Operator("tcp-legacy", OperatorType.TcpCommunication, 0, 0);
            op.AddParameter(TestHelpers.CreateParameter("Mode", "Client", "string"));
            op.AddParameter(TestHelpers.CreateParameter("IpAddress", "127.0.0.1", "string"));
            op.AddParameter(TestHelpers.CreateParameter("Port", port, "int"));
            op.AddParameter(TestHelpers.CreateParameter("SendData", "PING", "string"));
            op.AddParameter(TestHelpers.CreateParameter("Timeout", 2500, "int"));
            op.AddParameter(TestHelpers.CreateParameter("ResponseTimeoutMs", 2500, "int"));

            var result = await _operator.ExecuteAsync(op, cancellationToken: cts.Token);

            result.IsSuccess.Should().BeTrue();
            GetResponse(result).Should().Be("PONG");
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await IgnoreServerTerminationAsync(serverTask);
        }
    }

    private static string GetResponse(OperatorExecutionOutput result)
    {
        result.OutputData.Should().NotBeNull();
        var outputData = result.OutputData!;
        outputData.Should().ContainKey("Response");
        return outputData["Response"].Should().BeOfType<string>().Subject;
    }

    private static async Task RunSingleEchoServerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Be("PING");
        var response = Encoding.UTF8.GetBytes("PONG");
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task IgnoreServerTerminationAsync(Task serverTask)
    {
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
            // Expected on test cleanup.
        }
        catch (SocketException)
        {
            // Listener stop during cleanup can interrupt Accept.
        }
        catch (ObjectDisposedException)
        {
            // Listener/stream may already be disposed during cleanup.
        }
    }
}
