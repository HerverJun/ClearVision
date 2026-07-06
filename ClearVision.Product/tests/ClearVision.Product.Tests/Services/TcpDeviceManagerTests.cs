using System.Net;
using System.Net.Sockets;
using System.Text;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public class TcpDeviceManagerTests
{
    [Fact]
    public async Task SendAsync_ClientProfile_ShouldConnectSendAndReceiveLoopbackResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSingleEchoServerAsync(listener, "PING", "PONG", cts.Token);
        await using var manager = CreateManager(CreateClientProfile("client", port));

        try
        {
            var result = await manager.SendAsync(
                "client",
                new TcpDeviceSendRequest("PING", WaitResponse: true, ResponseTimeoutMs: 2500),
                cts.Token);

            result.Success.Should().BeTrue(result.Message);
            result.Response.Should().Be("PONG");

            var frames = await manager.GetFramesAsync("client", cts.Token);
            frames.Should().Contain(frame => frame.Direction == "Tx" && frame.Text == "PING");
            frames.Should().Contain(frame => frame.Direction == "Rx" && frame.Text == "PONG");
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await IgnoreServerTerminationAsync(serverTask);
        }
    }

    [Fact]
    public async Task StartServerAsync_ShouldReceiveFromClientAndSendManualReply()
    {
        var port = GetFreeLoopbackPort();
        await using var manager = CreateManager(CreateServerProfile("server", port));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var start = await manager.StartServerAsync("server", cts.Token);
        start.Success.Should().BeTrue(start.Message);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("PING"), cts.Token);
        await stream.FlushAsync(cts.Token);

        var receivedFrames = await WaitForFramesAsync(manager, "server", frame => frame.Direction == "Rx", cts.Token);
        receivedFrames.Should().Contain(frame => frame.Text == "PING");

        var send = await manager.SendAsync(
            "server",
            new TcpDeviceSendRequest("PONG", WaitResponse: false, ResponseTimeoutMs: 2500),
            cts.Token);
        send.Success.Should().BeTrue(send.Message);

        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Be("PONG");

        var status = await manager.GetStatusAsync("server", cts.Token);
        status.IsListening.Should().BeTrue();
        status.ConnectedClients.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SendAsync_InvalidHexPayload_ShouldReturnControlledErrorWithoutConnecting()
    {
        await using var manager = CreateManager(new TcpCommunicationProfile
        {
            Id = "hex",
            Name = "Hex",
            Enabled = true,
            Mode = TcpCommunicationProfile.ModeClient,
            RemoteHost = "127.0.0.1",
            RemotePort = 65000,
            Encoding = TcpCommunicationProfile.EncodingHex,
            FrameMode = TcpCommunicationProfile.FrameModeHex,
            TimeoutMs = 500
        });

        var result = await manager.SendAsync(
            "hex",
            new TcpDeviceSendRequest("ABC", IsHex: true, WaitResponse: false));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("HEX");
    }

    [Fact]
    public async Task StartServerAsync_WhenPortOccupied_ShouldReturnControlledError()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        await using var manager = CreateManager(CreateServerProfile("server", port));

        var result = await manager.StartServerAsync("server");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("监听失败");
    }

    private static TcpDeviceManager CreateManager(TcpCommunicationProfile profile)
    {
        var config = new AppConfig
        {
            TcpCommunication = new TcpCommunicationConfig
            {
                Profiles = [profile]
            }
        };
        config.Normalize();

        var configService = Substitute.For<IConfigurationService>();
        configService.LoadAsync().Returns(Task.FromResult(config));
        configService.GetCurrent().Returns(config);
        configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
        return new TcpDeviceManager(configService, Substitute.For<ILogger<TcpDeviceManager>>());
    }

    private static TcpCommunicationProfile CreateClientProfile(string id, int port)
    {
        return new TcpCommunicationProfile
        {
            Id = id,
            Name = id,
            Enabled = true,
            Mode = TcpCommunicationProfile.ModeClient,
            RemoteHost = "127.0.0.1",
            RemotePort = port,
            Encoding = TcpCommunicationProfile.EncodingUtf8,
            FrameMode = TcpCommunicationProfile.FrameModeRaw,
            TimeoutMs = 2500
        };
    }

    private static TcpCommunicationProfile CreateServerProfile(string id, int port)
    {
        return new TcpCommunicationProfile
        {
            Id = id,
            Name = id,
            Enabled = true,
            Mode = TcpCommunicationProfile.ModeServer,
            LocalHost = "127.0.0.1",
            LocalPort = port,
            Encoding = TcpCommunicationProfile.EncodingUtf8,
            FrameMode = TcpCommunicationProfile.FrameModeRaw,
            TimeoutMs = 2500
        };
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RunSingleEchoServerAsync(
        TcpListener listener,
        string expectedRequest,
        string responseText,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var buffer = new byte[expectedRequest.Length];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Be(expectedRequest);
        var response = Encoding.UTF8.GetBytes(responseText);
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TcpFrameLogEntry>> WaitForFramesAsync(
        TcpDeviceManager manager,
        string profileId,
        Func<TcpFrameLogEntry, bool> predicate,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var frames = await manager.GetFramesAsync(profileId, cancellationToken);
            if (frames.Any(predicate))
            {
                return frames;
            }

            await Task.Delay(25, cancellationToken);
        }

        return await manager.GetFramesAsync(profileId, cancellationToken);
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
