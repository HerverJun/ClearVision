using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class TcpDeviceManager : ITcpDeviceManager
{
    private const int MaxFrameBytes = 1024 * 1024;
    private const int MaxFramesPerProfile = 300;
    private const int DefaultReadBufferSize = 4096;

    // Raw/无定界响应的收敛间隔：某段数据到达后，若对端在该时间内没有继续发送，
    // 就把已缓冲的数据视为一个完整帧返回，避免"只读一次"截断跨 TCP 分段的响应。
    // 该值需大于现场典型的分段到达间隔（TCP 分片通常在个位数毫秒内送达），
    // 同时保持足够小以免每次读取都引入过多尾部延迟。
    private const int DefaultReadIdleGapMs = 100;

    private static readonly JsonSerializerOptions CloneJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfigurationService? _configurationService;
    private readonly ILogger<TcpDeviceManager> _logger;
    private readonly ConcurrentDictionary<string, ClientSession> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ServerSession> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TcpFrameLogEntry>> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TcpProfileStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    public TcpDeviceManager(IConfigurationService? configurationService, ILogger<TcpDeviceManager> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task<TcpCommunicationConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        if (_configurationService == null)
        {
            return new TcpCommunicationConfig();
        }

        var config = await _configurationService.LoadAsync();
        cancellationToken.ThrowIfCancellationRequested();
        config.Normalize();
        return CloneConfig(config.TcpCommunication);
    }

    public async Task<AppConfigMutationResult> SaveConfigAsync(
        TcpCommunicationConfig config,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (_configurationService == null)
        {
            throw new InvalidOperationException("TCP configuration service is not available.");
        }

        config ??= new TcpCommunicationConfig();
        config.Normalize();
        var validation = TcpCommunicationConfigValidator.Validate(config);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("TCP configuration is invalid.");
        }

        return await _configurationService.MutateAsync(
            expectedRevision,
            candidate => candidate.TcpCommunication = CloneConfig(config),
            candidate => TcpCommunicationConfigValidator.Validate(candidate.TcpCommunication).Errors
                .Select(issue => new AppConfigValidationError(
                    $"tcpCommunication.{issue.ProfileId}.{issue.Section}.{issue.Field}",
                    issue.Message))
                .ToArray(),
            cancellationToken);
    }

    public async Task<TcpDeviceOperationResult> ConnectAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveProfileAsync(profileId, cancellationToken);
        if (!resolved.Success)
        {
            return TcpDeviceOperationResult.Fail(resolved.Message, errors: resolved.Errors);
        }

        return await ConnectClientAsync(resolved.Profile!, cancellationToken);
    }

    public async Task<TcpDeviceOperationResult> DisconnectAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetProfileLock(profileId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryRemove(profileId, out var session))
            {
                await session.DisposeAsync();
            }

            var status = UpdateStatus(profileId, TcpCommunicationProfile.ModeClient, current => current with
            {
                IsConnected = false,
                RemoteEndpoint = null,
                LastError = string.Empty
            });

            return TcpDeviceOperationResult.Ok("TCP 客户端连接已断开。", status);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TcpDeviceSendResult> SendAsync(
        string profileId,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveProfileAsync(profileId, cancellationToken);
        if (!resolved.Success)
        {
            return TcpDeviceSendResult.Fail(resolved.Message, errors: resolved.Errors);
        }

        return await SendWithProfileAsync(resolved.Profile!, request, persistSession: true, cancellationToken);
    }

    public async Task<TcpDeviceSendResult> SendTransientAsync(
        TcpCommunicationProfile profile,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken = default)
    {
        // Transient 语义：连接仅服务这一次发送/接收，成功或失败后都必须释放，
        // 不得在 _clients / 状态里残留可复用连接。需要复用连接的调用方应改走
        // 持久化路径（ConnectAsync + SendAsync(profileId)），而不是让 Transient 悄悄持久化。
        profile ??= new TcpCommunicationProfile();
        profile.Normalize();
        return await SendWithProfileAsync(profile, request, persistSession: false, cancellationToken);
    }

    public async Task<TcpDeviceOperationResult> StartServerAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveProfileAsync(profileId, cancellationToken);
        if (!resolved.Success)
        {
            return TcpDeviceOperationResult.Fail(resolved.Message, errors: resolved.Errors);
        }

        var profile = resolved.Profile!;
        if (profile.Mode != TcpCommunicationProfile.ModeServer)
        {
            return TcpDeviceOperationResult.Fail("当前 Profile 是 Client 模式，请使用连接操作。");
        }

        var validation = TcpCommunicationConfigValidator.ValidateProfileForOperation(profile);
        if (!validation.IsValid)
        {
            return TcpDeviceOperationResult.Fail("TCP Profile 校验失败。", errors: validation.Errors);
        }

        var gate = GetProfileLock(profile.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(profile.Id, out var existing) && existing.IsListening)
            {
                return TcpDeviceOperationResult.Ok("TCP Server 已在监听。", BuildServerStatus(profile, existing));
            }

            if (_servers.TryRemove(profile.Id, out existing))
            {
                await existing.DisposeAsync();
            }

            var address = ResolveListenAddress(profile.LocalHost);
            var listener = new TcpListener(address, profile.LocalPort);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                var status = UpdateStatus(profile.Id, profile.Mode, current => current with
                {
                    IsListening = false,
                    LastError = $"监听失败: {ex.Message}"
                });
                return TcpDeviceOperationResult.Fail($"监听失败: {ex.Message}", status);
            }

            var session = new ServerSession(profile.CloneNormalized(), listener);
            _servers[profile.Id] = session;
            session.AcceptLoopTask = Task.Run(() => AcceptLoopAsync(session), CancellationToken.None);

            var startedStatus = BuildServerStatus(profile, session);
            _statuses[profile.Id] = startedStatus;
            return TcpDeviceOperationResult.Ok("TCP Server 监听已启动。", startedStatus);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TcpDeviceOperationResult> StopServerAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetProfileLock(profileId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryRemove(profileId, out var session))
            {
                await session.DisposeAsync();
            }

            var status = UpdateStatus(profileId, TcpCommunicationProfile.ModeServer, current => current with
            {
                IsListening = false,
                ConnectedClients = 0,
                LocalEndpoint = null,
                RemoteEndpoint = null,
                LastError = string.Empty
            });

            return TcpDeviceOperationResult.Ok("TCP Server 监听已停止。", status);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<TcpProfileStatus> GetStatusAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_clients.TryGetValue(profileId, out var clientSession))
        {
            return Task.FromResult(BuildClientStatus(clientSession.Profile, clientSession));
        }

        if (_servers.TryGetValue(profileId, out var serverSession))
        {
            return Task.FromResult(BuildServerStatus(serverSession.Profile, serverSession));
        }

        return Task.FromResult(_statuses.GetOrAdd(profileId, id => CreateStatus(id, TcpCommunicationProfile.ModeClient)));
    }

    public Task<IReadOnlyList<TcpFrameLogEntry>> GetFramesAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_frames.TryGetValue(profileId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<TcpFrameLogEntry>>(Array.Empty<TcpFrameLogEntry>());
        }

        return Task.FromResult<IReadOnlyList<TcpFrameLogEntry>>(queue.ToArray());
    }

    public Task ClearFramesAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _frames[profileId] = new ConcurrentQueue<TcpFrameLogEntry>();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _clients.Values)
        {
            await session.DisposeAsync();
        }

        foreach (var session in _servers.Values)
        {
            await session.DisposeAsync();
        }

        foreach (var gate in _profileLocks.Values)
        {
            gate.Dispose();
        }

        _clients.Clear();
        _servers.Clear();
        _profileLocks.Clear();
        _frames.Clear();
        _statuses.Clear();
    }

    private async Task<TcpDeviceOperationResult> ConnectClientAsync(
        TcpCommunicationProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Mode != TcpCommunicationProfile.ModeClient)
        {
            return TcpDeviceOperationResult.Fail("当前 Profile 是 Server 模式，请在全局 TCP 通讯页启动监听。");
        }

        var validation = TcpCommunicationConfigValidator.ValidateProfileForOperation(profile);
        if (!validation.IsValid)
        {
            return TcpDeviceOperationResult.Fail("TCP Profile 校验失败。", errors: validation.Errors);
        }

        var gate = GetProfileLock(profile.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryGetValue(profile.Id, out var existing) && existing.IsConnected)
            {
                return TcpDeviceOperationResult.Ok("TCP 客户端已连接。", BuildClientStatus(profile, existing));
            }

            if (_clients.TryRemove(profile.Id, out existing))
            {
                await existing.DisposeAsync();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(profile.TimeoutMs);

            var client = new TcpClient
            {
                NoDelay = true,
                ReceiveTimeout = profile.TimeoutMs,
                SendTimeout = profile.TimeoutMs
            };
            ApplyKeepAlive(client, profile.KeepAlive);

            try
            {
                await client.ConnectAsync(profile.RemoteHost, profile.RemotePort, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                var timeoutStatus = UpdateStatus(profile.Id, profile.Mode, current => current with
                {
                    IsConnected = false,
                    LastError = "连接超时"
                });
                return TcpDeviceOperationResult.Fail("连接超时。", timeoutStatus);
            }
            catch (SocketException ex)
            {
                client.Dispose();
                var socketStatus = UpdateStatus(profile.Id, profile.Mode, current => current with
                {
                    IsConnected = false,
                    LastError = $"连接失败: {ex.Message}"
                });
                return TcpDeviceOperationResult.Fail($"连接失败: {ex.Message}", socketStatus);
            }

            var session = new ClientSession(profile.CloneNormalized(), client);
            _clients[profile.Id] = session;
            var status = BuildClientStatus(profile, session) with
            {
                LastConnectedAtUtc = DateTimeOffset.UtcNow,
                LastError = string.Empty
            };
            _statuses[profile.Id] = status;
            return TcpDeviceOperationResult.Ok("TCP 客户端连接已建立。", status);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TcpDeviceSendResult> SendWithProfileAsync(
        TcpCommunicationProfile profile,
        TcpDeviceSendRequest request,
        bool persistSession,
        CancellationToken cancellationToken)
    {
        request ??= new TcpDeviceSendRequest(string.Empty);
        profile.Normalize();
        if (profile.Mode == TcpCommunicationProfile.ModeServer)
        {
            return await SendServerAsync(profile, request, cancellationToken);
        }

        // Transient 与 persistent 走完全独立的客户端路径：
        // - persistent 复用 / 建立 _clients 中的持久连接（ConnectAsync + SendAsync）；
        // - transient 使用一次性临时连接，绝不读取、复用或删除 _clients 中同 profile.Id
        //   的持久连接，因此即便二者 profile.Id 相同也不会互相影响。
        return persistSession
            ? await SendPersistentClientAsync(profile, request, cancellationToken)
            : await SendTransientClientAsync(profile, request, cancellationToken);
    }

    private async Task<TcpDeviceSendResult> SendPersistentClientAsync(
        TcpCommunicationProfile profile,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken)
    {
        var payloadBytesResult = TryBuildPayloadBytes(profile, request.Payload, request.IsHex, out var payloadBytes, out var payloadError);
        if (!payloadBytesResult)
        {
            return TcpDeviceSendResult.Fail(payloadError, await GetStatusAsync(profile.Id, cancellationToken));
        }

        var connectResult = await ConnectClientAsync(profile, cancellationToken);
        if (!connectResult.Success)
        {
            return TcpDeviceSendResult.Fail(connectResult.Message, connectResult.Status, connectResult.Errors);
        }

        if (!_clients.TryGetValue(profile.Id, out var session))
        {
            return TcpDeviceSendResult.Fail("TCP 客户端连接不可用。", connectResult.Status);
        }

        await session.RequestLock.WaitAsync(cancellationToken);
        var removeSessionAfterRelease = false;
        try
        {
            if (!session.IsConnected)
            {
                removeSessionAfterRelease = true;
                return TcpDeviceSendResult.Fail("TCP 客户端连接已断开。", BuildClientStatus(profile, session));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResolveOperationTimeout(profile, request.ResponseTimeoutMs));

            try
            {
                var stream = session.Stream;
                await stream.WriteAsync(payloadBytes, timeoutCts.Token);
                await stream.FlushAsync(timeoutCts.Token);
                AddFrame(profile, "Tx", payloadBytes, session.RemoteEndpoint);
                TouchStatus(profile.Id, profile.Mode, current => current with { LastSentAtUtc = DateTimeOffset.UtcNow, LastError = string.Empty });

                var response = string.Empty;
                if (request.WaitResponse)
                {
                    var responseBytes = await ReadOneFrameAsync(stream, profile, timeoutCts.Token);
                    if (responseBytes.Length == 0)
                    {
                        removeSessionAfterRelease = true;
                        return TcpDeviceSendResult.Fail("连接已关闭。", BuildClientStatus(profile, session));
                    }

                    AddFrame(profile, "Rx", responseBytes, session.RemoteEndpoint);
                    TouchStatus(profile.Id, profile.Mode, current => current with { LastReceivedAtUtc = DateTimeOffset.UtcNow, LastError = string.Empty });
                    response = DecodeFrameText(profile, responseBytes);
                }

                // Persistent 发送成功后保留连接，返回真实连接状态。
                var successStatus = BuildClientStatus(profile, session);
                return TcpDeviceSendResult.Ok("发送成功。", response, successStatus);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                removeSessionAfterRelease = true;
                var status = UpdateStatus(profile.Id, profile.Mode, current => current with
                {
                    IsConnected = false,
                    LastError = "通信超时"
                });
                return TcpDeviceSendResult.Fail("通信超时。", status);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                removeSessionAfterRelease = true;
                _logger.LogError(ex, "TCP client send failed for profile {ProfileId}", profile.Id);
                var status = UpdateStatus(profile.Id, profile.Mode, current => current with
                {
                    IsConnected = false,
                    LastError = $"通信错误: {ex.Message}"
                });
                return TcpDeviceSendResult.Fail($"通信错误: {ex.Message}", status);
            }
        }
        finally
        {
            session.RequestLock.Release();
            if (removeSessionAfterRelease)
            {
                await RemoveClientSessionAsync(profile.Id);
                // 释放后把存储的状态也收敛为"未连接"，确保后续 GetStatusAsync 不会
                // 报告一个已不存在的可复用连接。
                MarkReleasedStatus(profile.Id, profile.Mode);
            }
        }
    }

    // Transient 客户端发送：使用独立的一次性 TcpClient，完成后只释放自己创建的连接。
    // 关键约束：绝不读取、复用或删除 _clients 中同 profile.Id 的持久连接，也不覆盖其
    // _statuses 记录。因此即便 transient profile.Id 与已有 persistent profile.Id 相同，
    // transient 发送也不会误关闭持久连接或篡改其状态。
    private async Task<TcpDeviceSendResult> SendTransientClientAsync(
        TcpCommunicationProfile profile,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken)
    {
        if (profile.Mode != TcpCommunicationProfile.ModeClient)
        {
            return TcpDeviceSendResult.Fail("当前 Profile 是 Server 模式，请在全局 TCP 通讯页启动监听。", CreateStatus(profile.Id, profile.Mode));
        }

        var validation = TcpCommunicationConfigValidator.ValidateProfileForOperation(profile);
        if (!validation.IsValid)
        {
            return TcpDeviceSendResult.Fail("TCP Profile 校验失败。", CreateStatus(profile.Id, profile.Mode), validation.Errors);
        }

        var payloadBytesResult = TryBuildPayloadBytes(profile, request.Payload, request.IsHex, out var payloadBytes, out var payloadError);
        if (!payloadBytesResult)
        {
            return TcpDeviceSendResult.Fail(payloadError, CreateStatus(profile.Id, profile.Mode));
        }

        var client = new TcpClient
        {
            NoDelay = true,
            ReceiveTimeout = profile.TimeoutMs,
            SendTimeout = profile.TimeoutMs
        };
        ApplyKeepAlive(client, profile.KeepAlive);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(profile.TimeoutMs);

            try
            {
                await client.ConnectAsync(profile.RemoteHost, profile.RemotePort, connectCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TcpDeviceSendResult.Fail("连接超时。", CreateStatus(profile.Id, profile.Mode));
            }
            catch (SocketException ex)
            {
                return TcpDeviceSendResult.Fail($"连接失败: {ex.Message}", CreateStatus(profile.Id, profile.Mode));
            }

            var remoteEndpoint = client.Client.RemoteEndPoint?.ToString();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResolveOperationTimeout(profile, request.ResponseTimeoutMs));

            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(payloadBytes, timeoutCts.Token);
                await stream.FlushAsync(timeoutCts.Token);
                AddFrame(profile, "Tx", payloadBytes, remoteEndpoint);

                var response = string.Empty;
                if (request.WaitResponse)
                {
                    var responseBytes = await ReadOneFrameAsync(stream, profile, timeoutCts.Token);
                    if (responseBytes.Length == 0)
                    {
                        return TcpDeviceSendResult.Fail("连接已关闭。", CreateStatus(profile.Id, profile.Mode));
                    }

                    AddFrame(profile, "Rx", responseBytes, remoteEndpoint);
                    response = DecodeFrameText(profile, responseBytes);
                }

                // Transient 成功后即释放连接，返回的状态必须体现"未连接"，不得暗示可复用连接。
                return TcpDeviceSendResult.Ok("发送成功。", response, CreateStatus(profile.Id, profile.Mode));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TcpDeviceSendResult.Fail("通信超时。", CreateStatus(profile.Id, profile.Mode));
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                _logger.LogError(ex, "TCP transient send failed for profile {ProfileId}", profile.Id);
                return TcpDeviceSendResult.Fail($"通信错误: {ex.Message}", CreateStatus(profile.Id, profile.Mode));
            }
        }
        finally
        {
            // 只释放本方法创建的临时连接，不触碰 _clients / _statuses。
            try
            {
                client.Close();
                client.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }
        }
    }

    private async Task<TcpDeviceSendResult> SendServerAsync(
        TcpCommunicationProfile profile,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken)
    {
        if (!_servers.TryGetValue(profile.Id, out var session) || !session.IsListening)
        {
            return TcpDeviceSendResult.Fail("TCP Server 未监听，请先在全局 TCP 通讯页启动监听。", await GetStatusAsync(profile.Id, cancellationToken));
        }

        var client = session.GetLatestClient();
        if (client == null || !client.IsConnected)
        {
            return TcpDeviceSendResult.Fail("TCP Server 当前没有已连接客户端。", BuildServerStatus(profile, session));
        }

        var payloadBytesResult = TryBuildPayloadBytes(profile, request.Payload, request.IsHex, out var payloadBytes, out var payloadError);
        if (!payloadBytesResult)
        {
            return TcpDeviceSendResult.Fail(payloadError, BuildServerStatus(profile, session));
        }

        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResolveOperationTimeout(profile, request.ResponseTimeoutMs));
            await client.Stream.WriteAsync(payloadBytes, timeoutCts.Token);
            await client.Stream.FlushAsync(timeoutCts.Token);
            AddFrame(profile, "Tx", payloadBytes, client.RemoteEndpoint);
            TouchStatus(profile.Id, profile.Mode, current => current with { LastSentAtUtc = DateTimeOffset.UtcNow, LastError = string.Empty });
            return TcpDeviceSendResult.Ok("Server 回复已发送。", string.Empty, BuildServerStatus(profile, session));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TcpDeviceSendResult.Fail("发送超时。", BuildServerStatus(profile, session));
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogError(ex, "TCP server send failed for profile {ProfileId}", profile.Id);
            return TcpDeviceSendResult.Fail($"发送失败: {ex.Message}", BuildServerStatus(profile, session));
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    private async Task AcceptLoopAsync(ServerSession session)
    {
        while (!session.Cancellation.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await session.Listener.AcceptTcpClientAsync(session.Cancellation);
                client.NoDelay = true;
                client.ReceiveTimeout = session.Profile.TimeoutMs;
                client.SendTimeout = session.Profile.TimeoutMs;
                ApplyKeepAlive(client, session.Profile.KeepAlive);

                var acceptedClient = new ServerClientSession(client);
                session.AddClient(acceptedClient);
                _statuses[session.Profile.Id] = BuildServerStatus(session.Profile, session) with
                {
                    LastConnectedAtUtc = DateTimeOffset.UtcNow,
                    LastError = string.Empty
                };

                _ = Task.Run(() => ServerClientReadLoopAsync(session, acceptedClient), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                _logger.LogError(ex, "TCP server accept failed for profile {ProfileId}", session.Profile.Id);
                TouchStatus(session.Profile.Id, session.Profile.Mode, current => current with { LastError = $"Accept 失败: {ex.Message}" });
            }
        }
    }

    private async Task ServerClientReadLoopAsync(ServerSession session, ServerClientSession client)
    {
        try
        {
            while (!session.Cancellation.IsCancellationRequested && client.IsConnected)
            {
                var bytes = await ReadOneFrameAsync(client.Stream, session.Profile, session.Cancellation);
                if (bytes.Length == 0)
                {
                    break;
                }

                AddFrame(session.Profile, "Rx", bytes, client.RemoteEndpoint);
                TouchStatus(session.Profile.Id, session.Profile.Mode, current => current with
                {
                    LastReceivedAtUtc = DateTimeOffset.UtcNow,
                    LastError = string.Empty
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping the listener.
        }
        catch (ObjectDisposedException)
        {
            // Expected when stopping the listener.
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "TCP server client read ended for profile {ProfileId}", session.Profile.Id);
            TouchStatus(session.Profile.Id, session.Profile.Mode, current => current with { LastError = $"接收失败: {ex.Message}" });
        }
        finally
        {
            session.RemoveClient(client.Id);
            await client.DisposeAsync();
            _statuses[session.Profile.Id] = BuildServerStatus(session.Profile, session);
        }
    }

    private async Task<ResolvedProfile> ResolveProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        var config = await GetConfigAsync(cancellationToken);
        var profile = config.FindProfile(profileId);
        if (profile == null)
        {
            return ResolvedProfile.Fail("TCP Profile 不存在。");
        }

        return ResolvedProfile.Ok(profile.CloneNormalized());
    }

    private async Task RemoveClientSessionAsync(string profileId)
    {
        if (_clients.TryRemove(profileId, out var session))
        {
            await session.DisposeAsync();
        }
    }

    private SemaphoreSlim GetProfileLock(string profileId)
    {
        var key = string.IsNullOrWhiteSpace(profileId) ? "__default" : profileId.Trim();
        return _profileLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }

    private void AddFrame(
        TcpCommunicationProfile profile,
        string direction,
        byte[] bytes,
        string? remoteEndpoint)
    {
        var safeBytes = bytes.Length > MaxFrameBytes
            ? bytes.AsSpan(0, MaxFrameBytes).ToArray()
            : bytes;
        var entry = new TcpFrameLogEntry(
            Guid.NewGuid().ToString("N")[..12],
            profile.Id,
            direction,
            DateTimeOffset.UtcNow,
            bytes.Length,
            DecodeFrameText(profile, safeBytes),
            Convert.ToHexString(safeBytes),
            remoteEndpoint);

        var queue = _frames.GetOrAdd(profile.Id, static _ => new ConcurrentQueue<TcpFrameLogEntry>());
        queue.Enqueue(entry);
        while (queue.Count > MaxFramesPerProfile && queue.TryDequeue(out _))
        {
            // Bounded in-memory receive/send log.
        }
    }

    private static bool TryBuildPayloadBytes(
        TcpCommunicationProfile profile,
        string? payload,
        bool isHex,
        out byte[] bytes,
        out string error)
    {
        var text = payload ?? string.Empty;
        if (isHex ||
            profile.Encoding == TcpCommunicationProfile.EncodingHex ||
            profile.FrameMode == TcpCommunicationProfile.FrameModeHex)
        {
            return TryParseHex(text, out bytes, out error);
        }

        var wireText = text + ResolveLineEnding(profile);
        bytes = ResolveEncoding(profile.Encoding).GetBytes(wireText);
        error = string.Empty;
        return true;
    }

    private static bool TryParseHex(string value, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        var compact = new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0)
        {
            error = string.Empty;
            return true;
        }

        if (compact.Length % 2 != 0)
        {
            error = "HEX 内容必须是偶数位。";
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(compact);
            error = string.Empty;
            return true;
        }
        catch (FormatException)
        {
            error = "HEX 内容包含非法字符。";
            return false;
        }
    }

    private static string DecodeFrameText(TcpCommunicationProfile profile, byte[] bytes)
    {
        if (profile.Encoding == TcpCommunicationProfile.EncodingHex ||
            profile.FrameMode == TcpCommunicationProfile.FrameModeHex)
        {
            return Convert.ToHexString(bytes);
        }

        return ResolveEncoding(profile.Encoding).GetString(bytes);
    }

    private static Encoding ResolveEncoding(string encoding)
    {
        return TcpCommunicationProfile.NormalizeEncoding(encoding) switch
        {
            TcpCommunicationProfile.EncodingAscii => Encoding.ASCII,
            TcpCommunicationProfile.EncodingGbk => ResolveGbkEncoding(),
            _ => Encoding.UTF8
        };
    }

    private static Encoding ResolveGbkEncoding()
    {
        try
        {
            return Encoding.GetEncoding(936);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string ResolveLineEnding(TcpCommunicationProfile profile)
    {
        return profile.FrameMode == TcpCommunicationProfile.FrameModeLine
            ? profile.LineEnding switch
            {
                TcpCommunicationProfile.LineEndingCr => "\r",
                TcpCommunicationProfile.LineEndingLf => "\n",
                TcpCommunicationProfile.LineEndingCrlf => "\r\n",
                _ => string.Empty
            }
            : string.Empty;
    }

    private static int ResolveOperationTimeout(TcpCommunicationProfile profile, int? overrideTimeoutMs)
    {
        if (overrideTimeoutMs is > 0)
        {
            return TcpCommunicationProfile.NormalizeTimeout(overrideTimeoutMs.Value);
        }

        return profile.TimeoutMs;
    }

    private static async Task<byte[]> ReadOneFrameAsync(
        NetworkStream stream,
        TcpCommunicationProfile profile,
        CancellationToken cancellationToken)
    {
        return profile.FrameMode switch
        {
            TcpCommunicationProfile.FrameModeFixedLength when profile.FixedLength > 0 =>
                await ReadExactAsync(stream, profile.FixedLength, cancellationToken),
            TcpCommunicationProfile.FrameModeLine =>
                await ReadLineFrameAsync(stream, ResolveExpectedLineEnding(profile), cancellationToken),
            _ => await ReadRawFrameAsync(stream, endMarker: null, cancellationToken)
        };
    }

    // Raw / 无定界响应的安全收敛读取。
    // - 首个 ReadAsync 使用调用方的整体操作超时（cancellationToken）等待第一段数据；
    // - 收到第一段后，通过 DataAvailable 轮询等待后续分段，直到出现超过 idle gap 的空闲、
    //   达到 maxBytes、连接关闭，或命中可选的 end marker，从而避免"只读一次就判定完整响应"
    //   导致的截断。
    // 注意：这里刻意用 DataAvailable 轮询而非取消挂起的 ReadAsync —— 取消 NetworkStream 上
    // 挂起的 socket 读取在部分平台会让 socket 进入不可用状态，会破坏需要保持的持久连接。
    private static async Task<byte[]> ReadRawFrameAsync(
        NetworkStream stream,
        byte[]? endMarker,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[DefaultReadBufferSize];

        // 首段：阻塞等待，遵循整体超时。对端立即关闭时返回空帧（语义与旧实现一致）。
        var first = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        if (first == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new List<byte>(DefaultReadBufferSize);
        bytes.AddRange(buffer.AsSpan(0, first).ToArray());

        while (bytes.Count < MaxFrameBytes)
        {
            if (HasEndMarker(bytes, endMarker))
            {
                break;
            }

            if (!await WaitForMoreDataAsync(stream, DefaultReadIdleGapMs, cancellationToken))
            {
                // idle gap 内没有更多数据到达：把已缓冲的内容视为一个完整帧。
                break;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                // 对端在发送完毕后关闭连接：已缓冲的数据即为完整帧。
                break;
            }

            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return bytes.ToArray();
    }

    // 在 idle gap 内轮询等待后续分段到达，不取消任何挂起的 socket 读取。
    // 返回 true 表示有数据可读，false 表示在 idle gap 内保持空闲（视为响应收敛）。
    private static async Task<bool> WaitForMoreDataAsync(
        NetworkStream stream,
        int idleGapMs,
        CancellationToken cancellationToken)
    {
        const int pollIntervalMs = 5;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(idleGapMs);
        while (true)
        {
            try
            {
                if (stream.DataAvailable)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                return false;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
        }
    }

    private static bool HasEndMarker(List<byte> bytes, byte[]? endMarker)
    {
        return endMarker is { Length: > 0 } && EndsWith(bytes, endMarker);
    }

    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                return offset == 0 ? Array.Empty<byte>() : buffer.AsSpan(0, offset).ToArray();
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task<byte[]> ReadLineFrameAsync(
        NetworkStream stream,
        byte[] expectedEnding,
        CancellationToken cancellationToken)
    {
        if (expectedEnding.Length == 0)
        {
            return await ReadRawFrameAsync(stream, endMarker: null, cancellationToken);
        }

        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (bytes.Count < MaxFrameBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytes.Add(buffer[0]);
            if (EndsWith(bytes, expectedEnding))
            {
                break;
            }
        }

        return bytes.Count == 0 ? Array.Empty<byte>() : bytes.ToArray();
    }

    private static bool EndsWith(List<byte> bytes, byte[] suffix)
    {
        if (suffix.Length == 0 || bytes.Count < suffix.Length)
        {
            return false;
        }

        for (var i = 0; i < suffix.Length; i++)
        {
            if (bytes[bytes.Count - suffix.Length + i] != suffix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ResolveExpectedLineEnding(TcpCommunicationProfile profile)
    {
        return profile.LineEnding switch
        {
            TcpCommunicationProfile.LineEndingCr => "\r"u8.ToArray(),
            TcpCommunicationProfile.LineEndingLf => "\n"u8.ToArray(),
            TcpCommunicationProfile.LineEndingCrlf => "\r\n"u8.ToArray(),
            _ => Array.Empty<byte>()
        };
    }

    private static IPAddress ResolveListenAddress(string? host)
    {
        var value = string.IsNullOrWhiteSpace(host) ? IPAddress.Loopback.ToString() : host.Trim();
        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        return IPAddress.TryParse(value, out var address) ? address : IPAddress.Loopback;
    }

    private static void ApplyKeepAlive(TcpClient client, bool keepAlive)
    {
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keepAlive);
        }
        catch
        {
            // KeepAlive is a best-effort transport setting.
        }
    }

    private TcpProfileStatus UpdateStatus(
        string profileId,
        string mode,
        Func<TcpProfileStatus, TcpProfileStatus> update)
    {
        return _statuses.AddOrUpdate(
            profileId,
            id => update(CreateStatus(id, mode)),
            (_, current) => update(current));
    }

    private void TouchStatus(
        string profileId,
        string mode,
        Func<TcpProfileStatus, TcpProfileStatus> update)
    {
        UpdateStatus(profileId, mode, update);
    }

    private static TcpProfileStatus CreateStatus(string profileId, string mode)
    {
        return new TcpProfileStatus(
            profileId,
            mode,
            IsConnected: false,
            IsListening: false,
            LocalEndpoint: null,
            RemoteEndpoint: null,
            ConnectedClients: 0,
            LastError: string.Empty,
            LastConnectedAtUtc: null,
            LastReceivedAtUtc: null,
            LastSentAtUtc: null);
    }

    private TcpProfileStatus BuildClientStatus(TcpCommunicationProfile profile, ClientSession session)
    {
        var current = _statuses.GetOrAdd(profile.Id, id => CreateStatus(id, profile.Mode));
        var status = current with
        {
            ProfileId = profile.Id,
            Mode = profile.Mode,
            IsConnected = session.IsConnected,
            IsListening = false,
            ConnectedClients = 0,
            LocalEndpoint = session.LocalEndpoint,
            RemoteEndpoint = session.RemoteEndpoint
        };
        _statuses[profile.Id] = status;
        return status;
    }

    private TcpProfileStatus MarkReleasedStatus(string profileId, string mode)
    {
        return UpdateStatus(profileId, mode, current => current with
        {
            IsConnected = false,
            IsListening = false,
            ConnectedClients = 0,
            LocalEndpoint = null,
            RemoteEndpoint = null
        });
    }

    private TcpProfileStatus BuildServerStatus(TcpCommunicationProfile profile, ServerSession session)
    {
        var current = _statuses.GetOrAdd(profile.Id, id => CreateStatus(id, profile.Mode));
        var status = current with
        {
            ProfileId = profile.Id,
            Mode = profile.Mode,
            IsConnected = session.ConnectedClientCount > 0,
            IsListening = session.IsListening,
            ConnectedClients = session.ConnectedClientCount,
            LocalEndpoint = session.LocalEndpoint,
            RemoteEndpoint = session.GetLatestClient()?.RemoteEndpoint
        };
        _statuses[profile.Id] = status;
        return status;
    }

    private static TcpCommunicationConfig CloneConfig(TcpCommunicationConfig config)
    {
        var json = JsonSerializer.Serialize(config, CloneJsonOptions);
        var clone = JsonSerializer.Deserialize<TcpCommunicationConfig>(json, CloneJsonOptions) ?? new TcpCommunicationConfig();
        clone.Normalize();
        return clone;
    }

    private sealed record ResolvedProfile(
        bool Success,
        string Message,
        TcpCommunicationProfile? Profile,
        IReadOnlyList<TcpCommunicationValidationIssue>? Errors)
    {
        public static ResolvedProfile Ok(TcpCommunicationProfile profile)
        {
            return new ResolvedProfile(true, string.Empty, profile, null);
        }

        public static ResolvedProfile Fail(
            string message,
            IReadOnlyList<TcpCommunicationValidationIssue>? errors = null)
        {
            return new ResolvedProfile(false, message, null, errors);
        }
    }

    private sealed class ClientSession : IAsyncDisposable
    {
        private readonly TcpClient _client;

        public ClientSession(TcpCommunicationProfile profile, TcpClient client)
        {
            Profile = profile;
            _client = client;
            Stream = client.GetStream();
        }

        public TcpCommunicationProfile Profile { get; }

        public NetworkStream Stream { get; }

        public SemaphoreSlim RequestLock { get; } = new(1, 1);

        public bool IsConnected => IsSocketConnected(_client);

        public string? LocalEndpoint => _client.Client.LocalEndPoint?.ToString();

        public string? RemoteEndpoint => _client.Client.RemoteEndPoint?.ToString();

        public ValueTask DisposeAsync()
        {
            RequestLock.Dispose();
            try
            {
                Stream.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            try
            {
                _client.Close();
                _client.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ServerSession : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, ServerClientSession> _clients = new(StringComparer.OrdinalIgnoreCase);

        public ServerSession(TcpCommunicationProfile profile, TcpListener listener)
        {
            Profile = profile;
            Listener = listener;
            CancellationSource = new CancellationTokenSource();
        }

        public TcpCommunicationProfile Profile { get; }

        public TcpListener Listener { get; }

        public CancellationTokenSource CancellationSource { get; }

        public CancellationToken Cancellation => CancellationSource.Token;

        public Task? AcceptLoopTask { get; set; }

        public bool IsListening => !Cancellation.IsCancellationRequested;

        public string? LocalEndpoint => Listener.LocalEndpoint?.ToString();

        public int ConnectedClientCount => _clients.Values.Count(client => client.IsConnected);

        public void AddClient(ServerClientSession client)
        {
            _clients[client.Id] = client;
        }

        public void RemoveClient(string id)
        {
            _clients.TryRemove(id, out _);
        }

        public ServerClientSession? GetLatestClient()
        {
            return _clients.Values
                .Where(client => client.IsConnected)
                .OrderByDescending(client => client.ConnectedAtUtc)
                .FirstOrDefault();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                CancellationSource.Cancel();
                Listener.Stop();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            foreach (var client in _clients.Values)
            {
                await client.DisposeAsync();
            }

            _clients.Clear();

            if (AcceptLoopTask != null)
            {
                try
                {
                    await AcceptLoopTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Ignore accept loop termination errors during cleanup.
                }
            }

            CancellationSource.Dispose();
        }
    }

    private sealed class ServerClientSession : IAsyncDisposable
    {
        private readonly TcpClient _client;

        public ServerClientSession(TcpClient client)
        {
            _client = client;
            Stream = client.GetStream();
            RemoteEndpoint = client.Client.RemoteEndPoint?.ToString();
        }

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset ConnectedAtUtc { get; } = DateTimeOffset.UtcNow;

        public NetworkStream Stream { get; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public string? RemoteEndpoint { get; }

        public bool IsConnected => IsSocketConnected(_client);

        public ValueTask DisposeAsync()
        {
            SendLock.Dispose();
            try
            {
                Stream.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            try
            {
                _client.Close();
                _client.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            return ValueTask.CompletedTask;
        }
    }

    private static bool IsSocketConnected(TcpClient client)
    {
        try
        {
            return client.Connected &&
                   !(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0);
        }
        catch
        {
            return false;
        }
    }
}
