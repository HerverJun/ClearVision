using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression,ServicesCoverageSensitive")]
[Collection(RuntimeConcurrencyCollection.Name)]
public class TcpDeviceManagerTests
{
    [Fact]
    public async Task SendTransientAsync_WithSameIdAsPersistentSession_ShouldNotCloseOrAffectPersistentConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunConcurrentEchoServerAsync(listener, cts.Token);

        // 同一个 profile.Id ("robot") 既用于持久连接，也用于 transient 发送，
        // 用来覆盖 transient 误关闭同 Id 持久连接的隔离边界。
        var profile = CreateClientProfile("robot", port);
        await using var manager = CreateManager(profile);

        try
        {
            // 1. 建立持久连接。
            var connect = await manager.ConnectAsync("robot", cts.Token);
            connect.Success.Should().BeTrue(connect.Message);

            var connectedStatus = await manager.GetStatusAsync("robot", cts.Token);
            connectedStatus.IsConnected.Should().BeTrue();

            // 2. 使用相同 profile.Id 的 transient 发送。
            var transient = await manager.SendTransientAsync(
                CreateClientProfile("robot", port),
                new TcpDeviceSendRequest("PING", WaitResponse: true, ResponseTimeoutMs: 2500),
                cts.Token);
            transient.Success.Should().BeTrue(transient.Message);
            transient.Response.Should().Be("PING");

            // 3. transient 成功后持久连接仍应存在，不被误关闭。
            var statusAfterTransient = await manager.GetStatusAsync("robot", cts.Token);
            statusAfterTransient.IsConnected.Should().BeTrue("transient 发送不得关闭同 profile.Id 的持久连接");
            statusAfterTransient.RemoteEndpoint.Should().NotBeNull();

            // 4. 持久连接后续 SendAsync 仍可继续发送成功。
            var persistentSend = await manager.SendAsync(
                "robot",
                new TcpDeviceSendRequest("PONG", WaitResponse: true, ResponseTimeoutMs: 2500),
                cts.Token);
            persistentSend.Success.Should().BeTrue(persistentSend.Message);
            persistentSend.Response.Should().Be("PONG");
            persistentSend.Status.Should().NotBeNull();
            persistentSend.Status!.IsConnected.Should().BeTrue();
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await IgnoreServerTerminationAsync(serverTask);
        }
    }

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
    public async Task SendTransientAsync_LegacyProfileWithoutId_ShouldNotRetainReusableSessionAfterSuccess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serverTask = RunSingleEchoServerAsync(listener, "PING", "PONG", serverCts.Token);
        await using var manager = CreateManager();
        var profile = CreateTransientClientProfile(port);

        try
        {
            var result = await manager.SendTransientAsync(
                profile,
                new TcpDeviceSendRequest("PING", WaitResponse: true, ResponseTimeoutMs: 2500),
                CancellationToken.None);

            result.Success.Should().BeTrue(result.Message);
            result.Response.Should().Be("PONG");

            // 返回的状态不得表现为可复用连接。
            result.Status.Should().NotBeNull();
            result.Status!.IsConnected.Should().BeFalse();
            result.Status.RemoteEndpoint.Should().BeNull();

            // 后续查询状态同样不得残留可复用连接。
            var status = await manager.GetStatusAsync(profile.Id, CancellationToken.None);
            status.IsConnected.Should().BeFalse();
            status.RemoteEndpoint.Should().BeNull();
        }
        finally
        {
            serverCts.Cancel();
            listener.Stop();
            await IgnoreServerTerminationAsync(serverTask);
        }
    }

    [Fact]
    public async Task SendTransientAsync_WhenSendFails_ShouldReleaseSessionAndReportDisconnected()
    {
        // 指向一个未监听的本地端口，确保连接/通信失败。
        var port = GetFreeLoopbackPort();
        await using var manager = CreateManager();
        var profile = CreateTransientClientProfile(port);
        profile.TimeoutMs = 500;

        var result = await manager.SendTransientAsync(
            profile,
            new TcpDeviceSendRequest("PING", WaitResponse: true, ResponseTimeoutMs: 500));

        result.Success.Should().BeFalse();

        var status = await manager.GetStatusAsync(profile.Id);
        status.IsConnected.Should().BeFalse();
        status.RemoteEndpoint.Should().BeNull();
    }

    [Fact]
    public async Task SendTransientAsync_TwiceInSequence_ShouldSucceedWithoutStaleConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunEchoServerLoopAsync(listener, cts.Token);
        await using var manager = CreateManager();
        var profile = CreateTransientClientProfile(port);

        try
        {
            var first = await manager.SendTransientAsync(
                profile,
                new TcpDeviceSendRequest("PING", WaitResponse: true, ResponseTimeoutMs: 2500),
                cts.Token);
            first.Success.Should().BeTrue(first.Message);
            first.Response.Should().Be("PING");

            // 第一次已释放连接，第二次应重新建立连接并成功。
            var second = await manager.SendTransientAsync(
                profile,
                new TcpDeviceSendRequest("PONG", WaitResponse: true, ResponseTimeoutMs: 2500),
                cts.Token);
            second.Success.Should().BeTrue(second.Message);
            second.Response.Should().Be("PONG");

            var status = await manager.GetStatusAsync(profile.Id, cts.Token);
            status.IsConnected.Should().BeFalse();
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await IgnoreServerTerminationAsync(serverTask);
        }
    }

    [Fact]
    public async Task RawResponseReader_WithDeterministicChunks_ShouldReturnFullResponseAcrossTwentyRuns()
    {
        for (var iteration = 1; iteration <= 20; iteration++)
        {
            await using var stream = await ChunkedNetworkStream.CreateAsync(
                Encoding.UTF8.GetBytes("ACK:"),
                Encoding.UTF8.GetBytes("OK;score=98.5"));

            var response = await InvokeRawFrameReaderAsync(stream);

            Encoding.UTF8.GetString(response).Should().Be("ACK:OK;score=98.5");
            stream.ReadCount.Should().Be(2, $"run {iteration} exposes exactly two deterministic chunks");
        }
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

    // Transient 发送不依赖已保存的 Profile，使用一个没有配置服务的 manager 即可，
    // 模拟 legacy / 无 ProfileId 的现场调用路径。
    private static TcpDeviceManager CreateManager()
    {
        return new TcpDeviceManager(null, Substitute.For<ILogger<TcpDeviceManager>>());
    }

    private static TcpCommunicationProfile CreateTransientClientProfile(int port)
    {
        var profile = new TcpCommunicationProfile
        {
            Id = $"legacy-127.0.0.1-{port}",
            Name = $"Legacy 127.0.0.1:{port}",
            Enabled = true,
            Mode = TcpCommunicationProfile.ModeClient,
            RemoteHost = "127.0.0.1",
            RemotePort = port,
            Encoding = TcpCommunicationProfile.EncodingUtf8,
            FrameMode = TcpCommunicationProfile.FrameModeRaw,
            TimeoutMs = 2500,
            Reconnect = true
        };
        profile.Normalize();
        return profile;
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
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        Encoding.UTF8.GetString(buffer, 0, offset).Should().Be(expectedRequest);
        var response = Encoding.UTF8.GetBytes(responseText);
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        client.Client.Shutdown(SocketShutdown.Send);
    }

    // 每个连接读取一次请求并原样回显，用于验证连续 transient 发送都能重新建立连接。
    private static async Task RunEchoServerLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                continue;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }

    // 并发 echo 服务器：每个连接在独立任务里读取一次请求并原样回显。
    // 与 RunEchoServerLoopAsync 不同，它不会在单个连接上阻塞，
    // 因此允许持久连接与 transient 连接同时在线（覆盖同 profile.Id 隔离场景）。
    private static async Task RunConcurrentEchoServerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    using (client)
                    await using (var stream = client.GetStream())
                    {
                        var buffer = new byte[256];
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                            if (read == 0)
                            {
                                break;
                            }

                            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            await stream.FlushAsync(cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on test cleanup.
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    // Connection torn down during cleanup.
                }
            }, CancellationToken.None);
        }
    }

    private static async Task<byte[]> InvokeRawFrameReaderAsync(NetworkStream stream)
    {
        var method = typeof(TcpDeviceManager).GetMethod(
            "ReadRawFrameAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var invocation = method!.Invoke(null, [stream, null, cts.Token]);
        invocation.Should().BeAssignableTo<Task<byte[]>>();
        return await (Task<byte[]>)invocation!;
    }

    private sealed class ChunkedNetworkStream : NetworkStream
    {
        private readonly TcpClient _owner;
        private readonly byte[][] _chunks;
        private int _nextChunk;
        private int _readCount;

        private ChunkedNetworkStream(TcpClient owner, byte[][] chunks)
            : base(owner.Client, ownsSocket: true)
        {
            _owner = owner;
            _chunks = chunks;
        }

        public int ReadCount => Volatile.Read(ref _readCount);

        public override bool DataAvailable => Volatile.Read(ref _nextChunk) < _chunks.Length;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Interlocked.Increment(ref _nextChunk) - 1;
            if (index >= _chunks.Length)
            {
                return ValueTask.FromResult(0);
            }

            var chunk = _chunks[index];
            if (chunk.Length > buffer.Length)
            {
                throw new InvalidOperationException("The deterministic test buffer is smaller than the next chunk.");
            }

            chunk.AsMemory().CopyTo(buffer);
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(chunk.Length);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _owner.Dispose();
            }
        }

        public static async Task<ChunkedNetworkStream> CreateAsync(params byte[][] chunks)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient { NoDelay = true };
            try
            {
                var endpoint = (IPEndPoint)listener.LocalEndpoint;
                var connectTask = client.ConnectAsync(endpoint.Address, endpoint.Port);
                using var peer = await listener.AcceptTcpClientAsync();
                await connectTask;
                listener.Stop();
                return new ChunkedNetworkStream(client, chunks);
            }
            catch
            {
                client.Dispose();
                listener.Stop();
                throw;
            }
        }
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
