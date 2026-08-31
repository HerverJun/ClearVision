// PlcCommunicationOperatorBase.cs
// 创建失败的执行输出
// 作者：蘅芜君

using System.Collections.Concurrent;
using System.Text.Json;
using ClearVision.PlcComm;
using ClearVision.PlcComm.Interfaces;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

public sealed record PlcConnectionPoolSnapshot(
    int Capacity,
    int PooledCount,
    int ConnectedCount,
    int ActiveLeaseCount,
    int RetiringCount,
    int PendingConnectionCount,
    int ConnectionKeyLockCount,
    int ConnectionKeyLockReferenceCount,
    int OperationLockCount,
    int OperationLockReferenceCount,
    long IdleEvictionCount,
    long CapacityEvictionCount,
    long DisconnectedRemovalCount);

/// <summary>
/// PLC通信算子基类
/// </summary>
public abstract class PlcCommunicationOperatorBase : OperatorBase
{
    private readonly IExecutionResourceProfileResolver _executionResourceProfileResolver;

    // ─── 静态连接池 ───────────────────────────────────────────
    private const int DefaultMaxPooledConnections = 32;
    private static readonly TimeSpan DefaultMaxIdleConnectionAge = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<string, PooledConnectionEntry> _connectionPool = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim _poolLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> _connectionKeyLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> _operationLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<PooledConnectionEntry, byte> _retiringConnections = new();
    private static TimeProvider _poolTimeProvider = TimeProvider.System;
    private static TimeSpan _maxIdleConnectionAge = DefaultMaxIdleConnectionAge;
    private static int _maxPooledConnections = DefaultMaxPooledConnections;
    private static int _pendingConnectionCount;
    private static long _poolGeneration;
    private static long _idleEvictionCount;
    private static long _capacityEvictionCount;
    private static long _disconnectedRemovalCount;

    // ─── 心跳巡检 ─────────────────────────────────────────────
    private static readonly object _heartbeatGate = new();
    private static Task? _heartbeatTask;
    private static CancellationTokenSource? _heartbeatCts;
    private static readonly ConcurrentDictionary<string, bool> _lastKnownState = new(StringComparer.Ordinal);
    private static ILogger? _heartbeatLogger;
    private static bool _heartbeatStarted;
    private static bool _processExitRegistered;

    /// <summary>
    /// 心跳检测间隔（毫秒）
    /// </summary>
    private const int DefaultHeartbeatIntervalMs = 1000;

    /// <summary>
    /// 单次 Ping 超时（毫秒）。超时视为设备忙碌（≈在线）
    /// </summary>
    private const int PingTimeoutMs = 2000;
    private const int MaxHeartbeatConcurrency = 4;
    private static readonly object _configLock = new();
    private static readonly TimeSpan ConfigRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions _configJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static CommunicationConfig _cachedCommunicationConfig = new();
    private static DateTime _cachedCommunicationConfigAtUtc = DateTime.MinValue;

    protected PlcCommunicationOperatorBase(ILogger logger)
        : this(logger, DenyAllExecutionResourceProfileResolver.Instance)
    {
    }

    protected PlcCommunicationOperatorBase(
        ILogger logger,
        IExecutionResourceProfileResolver executionResourceProfileResolver)
        : base(logger)
    {
        _executionResourceProfileResolver = executionResourceProfileResolver ??
            throw new ArgumentNullException(nameof(executionResourceProfileResolver));
        // 首次创建算子时自动启动心跳巡检
        StartHeartbeat(logger);
    }

    protected ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolveExecutionResource(
        string profileId,
        string protocol,
        string address,
        string operation,
        int elementCount = 1) =>
        _executionResourceProfileResolver.ResolvePlc(
            profileId,
            new PlcExecutionResourceRequest(protocol, address, operation, elementCount));

    protected static string? FindForbiddenRawTargetParameter(
        Operator @operator,
        params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        return @operator.Parameters
            .FirstOrDefault(parameter => parameterNames.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }

    // ─── 心跳管理 ─────────────────────────────────────────────

    /// <summary>
    /// 启动后台心跳巡检
    /// </summary>
    public static void StartHeartbeat()
    {
        StartHeartbeat(null);
    }

    private static void StartHeartbeat(ILogger? logger)
    {
        lock (_heartbeatGate)
        {
            if (_heartbeatStarted)
            {
                return;
            }

            if (logger != null)
            {
                _heartbeatLogger = logger;
            }

            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
            _heartbeatStarted = true;

            if (!_processExitRegistered)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => StopHeartbeat();
                _processExitRegistered = true;
            }
        }
    }

    /// <summary>
    /// 停止心跳巡检（应用退出时调用）
    /// </summary>
    public static void StopHeartbeat()
    {
        Task? heartbeatTask;
        CancellationTokenSource? heartbeatCts;

        lock (_heartbeatGate)
        {
            if (!_heartbeatStarted)
            {
                return;
            }

            heartbeatTask = _heartbeatTask;
            heartbeatCts = _heartbeatCts;
            _heartbeatTask = null;
            _heartbeatCts = null;
            _heartbeatStarted = false;
        }

        heartbeatCts?.Cancel();

        try
        {
            heartbeatTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // 忽略取消异常
        }
        finally
        {
            heartbeatCts?.Dispose();
        }
    }

    public static IReadOnlyDictionary<string, bool> GetConnectionStateSnapshot()
    {
        if (_poolLock.Wait(0))
        {
            try
            {
                var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var (key, entry) in _connectionPool)
                {
                    var isAlive = _lastKnownState.TryGetValue(key, out var knownState)
                        ? knownState
                        : entry.Client.IsConnected;
                    snapshot[key] = isAlive;
                }

                return snapshot;
            }
            finally
            {
                _poolLock.Release();
            }
        }

        return _lastKnownState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public static PlcConnectionPoolSnapshot GetConnectionPoolSnapshot()
    {
        _poolLock.Wait();
        try
        {
            var retiring = _retiringConnections.Keys.ToArray();
            return new PlcConnectionPoolSnapshot(
                Capacity: _maxPooledConnections,
                PooledCount: _connectionPool.Count,
                ConnectedCount: _connectionPool.Values.Count(entry => entry.Client.IsConnected),
                ActiveLeaseCount: _connectionPool.Values.Sum(entry => entry.LeaseCount)
                    + retiring.Sum(entry => entry.LeaseCount),
                RetiringCount: retiring.Length,
                PendingConnectionCount: _pendingConnectionCount,
                ConnectionKeyLockCount: _connectionKeyLocks.Count,
                ConnectionKeyLockReferenceCount: _connectionKeyLocks.Values.Sum(entry => entry.ReferenceCount),
                OperationLockCount: _operationLocks.Count,
                OperationLockReferenceCount: _operationLocks.Values.Sum(entry => entry.ReferenceCount),
                IdleEvictionCount: Interlocked.Read(ref _idleEvictionCount),
                CapacityEvictionCount: Interlocked.Read(ref _capacityEvictionCount),
                DisconnectedRemovalCount: Interlocked.Read(ref _disconnectedRemovalCount));
        }
        finally
        {
            _poolLock.Release();
        }
    }

    internal static async Task ConfigureConnectionPoolForTestingAsync(
        int capacity,
        TimeSpan maxIdleConnectionAge,
        TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        if (maxIdleConnectionAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIdleConnectionAge));
        }

        ArgumentNullException.ThrowIfNull(timeProvider);
        await ClearConnectionPoolAsync();
        await _poolLock.WaitAsync();
        try
        {
            _maxPooledConnections = capacity;
            _maxIdleConnectionAge = maxIdleConnectionAge;
            _poolTimeProvider = timeProvider;
            _idleEvictionCount = 0;
            _capacityEvictionCount = 0;
            _disconnectedRemovalCount = 0;
        }
        finally
        {
            _poolLock.Release();
        }
    }

    internal static async Task ResetConnectionPoolPolicyForTestingAsync()
    {
        await ClearConnectionPoolAsync();
        await _poolLock.WaitAsync();
        try
        {
            _maxPooledConnections = DefaultMaxPooledConnections;
            _maxIdleConnectionAge = DefaultMaxIdleConnectionAge;
            _poolTimeProvider = TimeProvider.System;
            _idleEvictionCount = 0;
            _capacityEvictionCount = 0;
            _disconnectedRemovalCount = 0;
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public static void InvalidateGlobalConfigurationCache()
    {
        lock (_configLock)
        {
            _cachedCommunicationConfig = new CommunicationConfig();
            _cachedCommunicationConfigAtUtc = DateTime.MinValue;
        }
    }

    public static async Task ClearConnectionPoolAsync()
    {
        PooledConnectionEntry[] snapshot;
        await _poolLock.WaitAsync();
        try
        {
            _poolGeneration++;
            snapshot = _connectionPool.Values.ToArray();
            _connectionPool.Clear();
            _lastKnownState.Clear();
            foreach (var entry in snapshot)
            {
                _retiringConnections.TryAdd(entry, 0);
                entry.Retire();
            }
        }
        finally
        {
            _poolLock.Release();
        }

        await Task.WhenAll(snapshot.Select(entry => entry.DisposalCompleted));
    }

    public static async Task ResetRuntimeConfigurationAsync()
    {
        InvalidateGlobalConfigurationCache();
        await ClearConnectionPoolAsync();
    }

    /// <summary>
    /// 心跳巡检主循环
    /// </summary>
    private static async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        _heartbeatLogger?.LogInformation("[Heartbeat] 心跳巡检已启动，间隔 {Interval}ms", GetHeartbeatIntervalMs());

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetHeartbeatIntervalMs(), ct);

                var snapshot = await AcquireHeartbeatLeasesAsync(ct);

                if (snapshot.Length == 0)
                    continue;

                await PingSnapshotAsync(snapshot, ct);
            }
            catch (OperationCanceledException)
            {
                break; // 正常关闭
            }
            catch (Exception ex)
            {
                _heartbeatLogger?.LogError(ex, "[Heartbeat] 巡检循环发生意外异常");
            }
        }

        _heartbeatLogger?.LogInformation("[Heartbeat] 心跳巡检已停止");
    }

    private static async Task PingSnapshotAsync(PooledPlcConnectionLease[] snapshot, CancellationToken ct)
    {
        if (snapshot.Length == 1)
        {
            await PingClientAsync(snapshot[0], ct);
            return;
        }

        try
        {
            await Parallel.ForEachAsync(
                snapshot,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxHeartbeatConcurrency,
                    CancellationToken = ct
                },
                async (lease, token) => await PingClientAsync(lease, token));
        }
        finally
        {
            foreach (var lease in snapshot)
            {
                await lease.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 对单个客户端执行 Ping 检测
    /// </summary>
    private static async Task PingClientAsync(PooledPlcConnectionLease lease, CancellationToken ct)
    {
        var key = lease.ConnectionKey;
        var client = lease.Client;
        bool isAlive;

        try
        {
            // 使用短超时，避免阻塞算子的正常读写
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            pingCts.CancelAfter(PingTimeoutMs);

            isAlive = await ExecuteWithConnectionOperationLockCoreAsync(
                key,
                () => client.PingAsync(pingCts.Token),
                pingCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Ping 超时 → 设备可能正忙于算子读写，视为在线
            isAlive = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await lease.DisposeAsync();
            throw;
        }
        catch
        {
            isAlive = false;
        }

        try
        {
            // ─── 状态变化检测（仅在状态改变时记录日志）──────────
            var hadPreviousState = _lastKnownState.TryGetValue(key, out var wasAlive);
            if (!hadPreviousState || wasAlive != isAlive)
            {
                _lastKnownState[key] = isAlive;
                if (hadPreviousState && isAlive)
                {
                    _heartbeatLogger?.LogInformation("[Heartbeat] ✅ 设备恢复在线: {Key}", key);
                }
                else if (!isAlive)
                {
                    _heartbeatLogger?.LogWarning("[Heartbeat] ⚠️ 设备掉线并从池中移除: {Key}", key);
                }
            }

            if (!isAlive)
            {
                await RemoveDisconnectedEntryAsync(key, lease.Entry);
            }
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // ─── 连接管理 ─────────────────────────────────────────────

    /// <summary>
    /// 获取或创建PLC连接
    /// </summary>
    protected internal sealed class PooledPlcConnectionLease : IAsyncDisposable
    {
        private PooledConnectionEntry? _entry;
        private readonly bool _touchLastUsedOnRelease;

        internal PooledPlcConnectionLease(
            PooledConnectionEntry entry,
            bool isNewConnection,
            bool touchLastUsedOnRelease)
        {
            _entry = entry;
            _touchLastUsedOnRelease = touchLastUsedOnRelease;
            Client = entry.Client;
            ConnectionKey = entry.ConnectionKey;
            IsNewConnection = isNewConnection;
        }

        public IPlcClient Client { get; }

        public string ConnectionKey { get; }

        public bool IsNewConnection { get; }

        internal PooledConnectionEntry Entry =>
            _entry ?? throw new ObjectDisposedException(nameof(PooledPlcConnectionLease));

        public async ValueTask DisposeAsync()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry != null)
            {
                await ReleaseConnectionLeaseAsync(entry, _touchLastUsedOnRelease);
            }
        }
    }

    internal sealed class PooledConnectionEntry
    {
        private readonly object _sync = new();
        private readonly TaskCompletionSource _disposalCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DateTimeOffset _lastUsedUtc;
        private int _leaseCount;
        private bool _retired;
        private bool _disposeStarted;

        public PooledConnectionEntry(string connectionKey, IPlcClient client, DateTimeOffset nowUtc)
        {
            ConnectionKey = connectionKey;
            Client = client;
            _lastUsedUtc = nowUtc;
        }

        public string ConnectionKey { get; }

        public IPlcClient Client { get; }

        public Task DisposalCompleted => _disposalCompleted.Task;

        public int LeaseCount
        {
            get
            {
                lock (_sync)
                {
                    return _leaseCount;
                }
            }
        }

        public DateTimeOffset LastUsedUtc
        {
            get
            {
                lock (_sync)
                {
                    return _lastUsedUtc;
                }
            }
        }

        public bool IsRetired
        {
            get
            {
                lock (_sync)
                {
                    return _retired;
                }
            }
        }

        public bool TryAcquireLease(
            DateTimeOffset nowUtc,
            bool isNewConnection,
            bool touchLastUsed,
            out PooledPlcConnectionLease? lease)
        {
            lock (_sync)
            {
                if (_retired)
                {
                    lease = null;
                    return false;
                }

                _leaseCount++;
                if (touchLastUsed)
                {
                    _lastUsedUtc = nowUtc;
                }

                lease = new PooledPlcConnectionLease(this, isNewConnection, touchLastUsed);
                return true;
            }
        }

        public bool IsIdle(DateTimeOffset nowUtc, TimeSpan maxIdleAge)
        {
            lock (_sync)
            {
                return !_retired && _leaseCount == 0 && nowUtc - _lastUsedUtc >= maxIdleAge;
            }
        }

        public bool CanEvictForCapacity()
        {
            lock (_sync)
            {
                return !_retired && _leaseCount == 0;
            }
        }

        public bool Retire()
        {
            var disposeNow = false;
            lock (_sync)
            {
                if (!_retired)
                {
                    _retired = true;
                }

                if (_leaseCount == 0 && !_disposeStarted)
                {
                    _disposeStarted = true;
                    disposeNow = true;
                }
            }

            if (disposeNow)
            {
                _ = DisposeClientAsync();
            }

            return disposeNow;
        }

        public bool ReleaseLease(DateTimeOffset nowUtc, bool touchLastUsed)
        {
            var disposeNow = false;
            lock (_sync)
            {
                if (_leaseCount <= 0)
                {
                    throw new InvalidOperationException("PLC connection lease count underflow.");
                }

                _leaseCount--;
                if (touchLastUsed)
                {
                    _lastUsedUtc = nowUtc;
                }
                if (_retired && _leaseCount == 0 && !_disposeStarted)
                {
                    _disposeStarted = true;
                    disposeNow = true;
                }
            }

            if (disposeNow)
            {
                _ = DisposeClientAsync();
            }

            return disposeNow;
        }

        private async Task DisposeClientAsync()
        {
            try
            {
                try
                {
                    await Client.DisconnectAsync();
                }
                catch
                {
                    // A disconnected industrial endpoint can fail its final close handshake.
                }

                try
                {
                    Client.Dispose();
                }
                catch
                {
                    // Pool retirement is best-effort after the entry has become unreachable.
                }
            }
            finally
            {
                _retiringConnections.TryRemove(this, out _);
                _disposalCompleted.TrySetResult();
            }
        }
    }

    private sealed class RefCountedSemaphore : IDisposable
    {
        private readonly object _sync = new();
        private int _refCount;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount
        {
            get
            {
                lock (_sync)
                {
                    return _refCount;
                }
            }
        }

        public bool TryAddRef()
        {
            lock (_sync)
            {
                if (_retired)
                {
                    return false;
                }

                _refCount++;
                return true;
            }
        }

        public bool ReleaseRefAndRetireIfUnused()
        {
            lock (_sync)
            {
                if (_refCount <= 0)
                {
                    throw new InvalidOperationException("PLC keyed lock reference count underflow.");
                }

                _refCount--;
                if (_refCount != 0)
                {
                    return false;
                }

                _retired = true;
                return true;
            }
        }

        public void Dispose() => Semaphore.Dispose();
    }

    private static RefCountedSemaphore AcquireRefCountedSemaphore(
        ConcurrentDictionary<string, RefCountedSemaphore> dictionary,
        string key)
    {
        while (true)
        {
            var entry = dictionary.GetOrAdd(key, static _ => new RefCountedSemaphore());
            if (!entry.TryAddRef())
            {
                dictionary.TryRemove(new KeyValuePair<string, RefCountedSemaphore>(key, entry));
                continue;
            }

            if (dictionary.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                return entry;
            }

            if (entry.ReleaseRefAndRetireIfUnused())
            {
                entry.Dispose();
            }
        }
    }

    private static void ReleaseRefCountedSemaphore(
        ConcurrentDictionary<string, RefCountedSemaphore> dictionary,
        string key,
        RefCountedSemaphore entry)
    {
        if (!entry.ReleaseRefAndRetireIfUnused())
        {
            return;
        }

        dictionary.TryRemove(new KeyValuePair<string, RefCountedSemaphore>(key, entry));
        entry.Dispose();
    }

    protected async Task<PooledPlcConnectionLease> AcquireConnectionLeaseAsync(
        string connectionKey,
        Func<IPlcClient> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionKey);
        ArgumentNullException.ThrowIfNull(factory);

        var keyLockEntry = AcquireRefCountedSemaphore(_connectionKeyLocks, connectionKey);
        var keyLockAcquired = false;
        var reservationHeld = false;
        var reservationGeneration = 0L;
        IPlcClient? newClient = null;

        try
        {
            await keyLockEntry.Semaphore.WaitAsync(cancellationToken);
            keyLockAcquired = true;

            var immediateDisposals = new List<Task>();
            PooledPlcConnectionLease? existingLease = null;
            await _poolLock.WaitAsync(cancellationToken);
            try
            {
                var nowUtc = _poolTimeProvider.GetUtcNow();
                MaintainPoolUnderLock(nowUtc, immediateDisposals);

                if (_connectionPool.TryGetValue(connectionKey, out var existingEntry) &&
                    existingEntry.Client.IsConnected &&
                    existingEntry.TryAcquireLease(
                        nowUtc,
                        isNewConnection: false,
                        touchLastUsed: true,
                        out existingLease))
                {
                    Logger.LogDebug("[{OperatorType}] 复用现有连接: {Key}", OperatorType, connectionKey);
                }
                else
                {
                    EnsureCapacityForReservationUnderLock(immediateDisposals);
                    if (_connectionPool.Count + _pendingConnectionCount >= _maxPooledConnections)
                    {
                        throw new InvalidOperationException(
                            $"PLC_CONNECTION_POOL_CAPACITY_REACHED: capacity={_maxPooledConnections}.");
                    }

                    _pendingConnectionCount++;
                    reservationHeld = true;
                    reservationGeneration = _poolGeneration;
                }
            }
            finally
            {
                _poolLock.Release();
            }

            if (immediateDisposals.Count > 0)
            {
                await Task.WhenAll(immediateDisposals);
            }

            if (existingLease != null)
            {
                return existingLease;
            }

            Logger.LogInformation("[{OperatorType}] 创建新连接: {Key}", OperatorType, connectionKey);
            newClient = factory();
            bool connected;
            try
            {
                connected = await newClient.ConnectAsync(cancellationToken);
            }
            catch
            {
                await DisposeUnpooledClientAsync(newClient);
                newClient = null;
                throw;
            }

            if (!connected)
            {
                await DisposeUnpooledClientAsync(newClient);
                newClient = null;
                throw new InvalidOperationException($"无法连接到PLC: {connectionKey}");
            }

            PooledPlcConnectionLease? createdLease = null;
            var generationChanged = false;
            await _poolLock.WaitAsync(cancellationToken);
            try
            {
                _pendingConnectionCount--;
                reservationHeld = false;
                generationChanged = reservationGeneration != _poolGeneration;
                if (!generationChanged)
                {
                    var entry = new PooledConnectionEntry(
                        connectionKey,
                        newClient,
                        _poolTimeProvider.GetUtcNow());
                    if (!_connectionPool.TryAdd(connectionKey, entry) ||
                        !entry.TryAcquireLease(
                            _poolTimeProvider.GetUtcNow(),
                            isNewConnection: true,
                            touchLastUsed: true,
                            out createdLease))
                    {
                        throw new InvalidOperationException(
                            $"PLC connection '{connectionKey}' was created through an uncoordinated path.");
                    }

                    _lastKnownState[connectionKey] = true;
                    newClient = null;
                }
            }
            finally
            {
                _poolLock.Release();
            }

            if (generationChanged)
            {
                await DisposeUnpooledClientAsync(newClient!);
                newClient = null;
                throw new InvalidOperationException("PLC_CONNECTION_POOL_RESET_DURING_CONNECT.");
            }

            return createdLease!;
        }
        finally
        {
            if (newClient != null)
            {
                await DisposeUnpooledClientAsync(newClient);
            }

            if (reservationHeld)
            {
                await _poolLock.WaitAsync(CancellationToken.None);
                try
                {
                    _pendingConnectionCount--;
                }
                finally
                {
                    _poolLock.Release();
                }
            }

            if (keyLockAcquired)
            {
                keyLockEntry.Semaphore.Release();
            }

            ReleaseRefCountedSemaphore(_connectionKeyLocks, connectionKey, keyLockEntry);
        }
    }

    internal static async Task RunConnectionPoolMaintenanceAsync()
    {
        var immediateDisposals = new List<Task>();
        await _poolLock.WaitAsync();
        try
        {
            MaintainPoolUnderLock(_poolTimeProvider.GetUtcNow(), immediateDisposals);
        }
        finally
        {
            _poolLock.Release();
        }

        if (immediateDisposals.Count > 0)
        {
            await Task.WhenAll(immediateDisposals);
        }
    }

    private static async Task<PooledPlcConnectionLease[]> AcquireHeartbeatLeasesAsync(CancellationToken ct)
    {
        if (!await _poolLock.WaitAsync(200, ct))
        {
            return [];
        }

        var immediateDisposals = new List<Task>();
        var leases = new List<PooledPlcConnectionLease>();
        try
        {
            var nowUtc = _poolTimeProvider.GetUtcNow();
            MaintainPoolUnderLock(nowUtc, immediateDisposals);
            foreach (var entry in _connectionPool.Values)
            {
                if (entry.TryAcquireLease(
                        nowUtc,
                        isNewConnection: false,
                        touchLastUsed: false,
                        out var lease))
                {
                    leases.Add(lease!);
                }
            }
        }
        finally
        {
            _poolLock.Release();
        }

        if (immediateDisposals.Count > 0)
        {
            await Task.WhenAll(immediateDisposals);
        }

        return leases.ToArray();
    }

    internal static async Task RunHeartbeatProbeOnceForTestingAsync(CancellationToken cancellationToken)
    {
        var snapshot = await AcquireHeartbeatLeasesAsync(cancellationToken);
        if (snapshot.Length > 0)
        {
            await PingSnapshotAsync(snapshot, cancellationToken);
        }
    }

    private static void MaintainPoolUnderLock(DateTimeOffset nowUtc, List<Task> immediateDisposals)
    {
        foreach (var (key, entry) in _connectionPool.ToArray())
        {
            if (!entry.Client.IsConnected)
            {
                RemoveEntryUnderLock(
                    key,
                    entry,
                    ConnectionRetirementReason.Disconnected,
                    immediateDisposals);
            }
            else if (entry.IsIdle(nowUtc, _maxIdleConnectionAge))
            {
                RemoveEntryUnderLock(
                    key,
                    entry,
                    ConnectionRetirementReason.Idle,
                    immediateDisposals);
            }
        }
    }

    private static void EnsureCapacityForReservationUnderLock(List<Task> immediateDisposals)
    {
        while (_connectionPool.Count + _pendingConnectionCount >= _maxPooledConnections)
        {
            var candidate = _connectionPool
                .Where(pair => pair.Value.CanEvictForCapacity())
                .OrderBy(pair => pair.Value.LastUsedUtc)
                .FirstOrDefault();
            if (candidate.Value == null)
            {
                return;
            }

            RemoveEntryUnderLock(
                candidate.Key,
                candidate.Value,
                ConnectionRetirementReason.Capacity,
                immediateDisposals);
        }
    }

    private static bool RemoveEntryUnderLock(
        string connectionKey,
        PooledConnectionEntry entry,
        ConnectionRetirementReason reason,
        List<Task> immediateDisposals)
    {
        if (!_connectionPool.TryGetValue(connectionKey, out var current) ||
            !ReferenceEquals(current, entry) ||
            !_connectionPool.Remove(connectionKey))
        {
            return false;
        }

        _lastKnownState.TryRemove(connectionKey, out _);
        _retiringConnections.TryAdd(entry, 0);
        if (entry.Retire())
        {
            immediateDisposals.Add(entry.DisposalCompleted);
        }

        switch (reason)
        {
            case ConnectionRetirementReason.Idle:
                Interlocked.Increment(ref _idleEvictionCount);
                break;
            case ConnectionRetirementReason.Capacity:
                Interlocked.Increment(ref _capacityEvictionCount);
                break;
            case ConnectionRetirementReason.Disconnected:
                Interlocked.Increment(ref _disconnectedRemovalCount);
                break;
        }

        return true;
    }

    private static async Task RemoveDisconnectedEntryAsync(
        string connectionKey,
        PooledConnectionEntry entry)
    {
        var immediateDisposals = new List<Task>();
        await _poolLock.WaitAsync();
        try
        {
            RemoveEntryUnderLock(
                connectionKey,
                entry,
                ConnectionRetirementReason.Disconnected,
                immediateDisposals);
        }
        finally
        {
            _poolLock.Release();
        }

        if (immediateDisposals.Count > 0)
        {
            await Task.WhenAll(immediateDisposals);
        }
    }

    private static async ValueTask ReleaseConnectionLeaseAsync(
        PooledConnectionEntry entry,
        bool touchLastUsed)
    {
        var disposeStarted = entry.ReleaseLease(_poolTimeProvider.GetUtcNow(), touchLastUsed);
        if (disposeStarted)
        {
            await entry.DisposalCompleted;
            return;
        }

        if (!entry.IsRetired && !entry.Client.IsConnected)
        {
            await RemoveDisconnectedEntryAsync(entry.ConnectionKey, entry);
        }
    }

    private static async Task DisposeUnpooledClientAsync(IPlcClient client)
    {
        try
        {
            await client.DisconnectAsync();
        }
        catch
        {
        }

        try
        {
            client.Dispose();
        }
        catch
        {
        }
    }

    private enum ConnectionRetirementReason
    {
        Idle,
        Capacity,
        Disconnected
    }

    protected Task<T> ExecuteWithConnectionOperationLockAsync<T>(
        string connectionKey,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteWithConnectionOperationLockCoreAsync(connectionKey, operation, cancellationToken);
    }

    private static async Task<T> ExecuteWithConnectionOperationLockCoreAsync<T>(
        string connectionKey,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var operationLockEntry = AcquireRefCountedSemaphore(_operationLocks, connectionKey);
        var lockAcquired = false;

        try
        {
            await operationLockEntry.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;
            return await operation();
        }
        finally
        {
            if (lockAcquired)
            {
                operationLockEntry.Semaphore.Release();
            }

            ReleaseRefCountedSemaphore(_operationLocks, connectionKey, operationLockEntry);
        }
    }

    // ─── 数据转换工具 ─────────────────────────────────────────

    /// <summary>
    /// 根据数据类型获取长度
    /// </summary>
    protected (string ipAddress, int port, string protocol, string connectionSource) ResolveConnectionSettings(
        string? ipAddress,
        int? port,
        string fallbackProtocol = "",
        bool useGlobalFallback = false)
    {
        var global = GetGlobalCommunicationConfig();
        var normalizedProtocol = CommunicationConfig.NormalizeProtocolKey(fallbackProtocol, global.ActiveProtocol) ?? string.Empty;
        var globalProfile = global.GetProfile(normalizedProtocol);
        var normalizedIp = (ipAddress ?? string.Empty).Trim();
        var requestedPort = port ?? 0;
        var hasOperatorIp = !OperatorParameterValueSemantics.IsMissing(normalizedIp);
        var hasOperatorPort = requestedPort > 0;
        var globalIp = (globalProfile.IpAddress ?? string.Empty).Trim();
        var hasGlobalIp = !OperatorParameterValueSemantics.IsMissing(globalIp);
        var hasGlobalPort = globalProfile.Port > 0 && globalProfile.Port <= 65535;

        if (hasOperatorPort && (requestedPort < 1 || requestedPort > 65535))
        {
            throw new InvalidOperationException(BuildConnectionConfigErrorMessage(
                code: "PLC_CONNECTION_CONFIG_INVALID_PORT",
                message: "Operator Port must be within 1..65535.",
                protocol: normalizedProtocol,
                useGlobalFallback: useGlobalFallback,
                hasOperatorIp: hasOperatorIp,
                hasOperatorPort: hasOperatorPort,
                hasGlobalIp: hasGlobalIp,
                hasGlobalPort: hasGlobalPort));
        }

        if (!useGlobalFallback)
        {
            if (!hasOperatorIp || !hasOperatorPort)
            {
                throw new InvalidOperationException(BuildConnectionConfigErrorMessage(
                    code: "PLC_CONNECTION_CONFIG_OPERATOR_REQUIRED",
                    message: "Operator IpAddress and Port are required when UseGlobalFallback is false.",
                    protocol: normalizedProtocol,
                    useGlobalFallback: false,
                    hasOperatorIp: hasOperatorIp,
                    hasOperatorPort: hasOperatorPort,
                    hasGlobalIp: hasGlobalIp,
                    hasGlobalPort: hasGlobalPort));
            }

            return (normalizedIp, requestedPort, normalizedProtocol, "OperatorParameters");
        }

        var resolvedIp = hasOperatorIp ? normalizedIp : globalIp;
        var resolvedPort = hasOperatorPort ? requestedPort : globalProfile.Port;
        var usedGlobalFallback = !hasOperatorIp || !hasOperatorPort;

        if (string.IsNullOrWhiteSpace(resolvedIp))
        {
            throw new InvalidOperationException(BuildConnectionConfigErrorMessage(
                code: "PLC_CONNECTION_CONFIG_MISSING_IP",
                message: "PLC IP is not configured in operator parameters and global settings.",
                protocol: normalizedProtocol,
                useGlobalFallback: true,
                hasOperatorIp: hasOperatorIp,
                hasOperatorPort: hasOperatorPort,
                hasGlobalIp: hasGlobalIp,
                hasGlobalPort: hasGlobalPort));
        }

        if (resolvedPort <= 0 || resolvedPort > 65535)
        {
            throw new InvalidOperationException(BuildConnectionConfigErrorMessage(
                code: "PLC_CONNECTION_CONFIG_MISSING_PORT",
                message: "PLC Port is not configured in operator parameters and global settings.",
                protocol: normalizedProtocol,
                useGlobalFallback: true,
                hasOperatorIp: hasOperatorIp,
                hasOperatorPort: hasOperatorPort,
                hasGlobalIp: hasGlobalIp,
                hasGlobalPort: hasGlobalPort));
        }

        if (usedGlobalFallback)
        {
            Logger.LogInformation(
                "[{OperatorType}] Connection fallback applied. Operator IP='{OperatorIp}', Port='{OperatorPort}', Global IP='{GlobalIp}', Port={GlobalPort}.",
                OperatorType,
                ipAddress,
                port,
                globalProfile.IpAddress,
                globalProfile.Port);
        }

        return (resolvedIp, resolvedPort, normalizedProtocol, usedGlobalFallback ? "GlobalFallback" : "OperatorParameters");
    }

    private static string BuildConnectionConfigErrorMessage(
        string code,
        string message,
        string protocol,
        bool useGlobalFallback,
        bool hasOperatorIp,
        bool hasOperatorPort,
        bool hasGlobalIp,
        bool hasGlobalPort)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Code"] = code,
            ["Message"] = message,
            ["Protocol"] = protocol,
            ["UseGlobalFallback"] = useGlobalFallback,
            ["Details"] = new Dictionary<string, object>
            {
                ["HasOperatorIp"] = hasOperatorIp,
                ["HasOperatorPort"] = hasOperatorPort,
                ["HasGlobalIp"] = hasGlobalIp,
                ["HasGlobalPort"] = hasGlobalPort
            }
        });
    }

    private static int GetHeartbeatIntervalMs()
    {
        var intervalMs = GetGlobalCommunicationConfig().HeartbeatIntervalMs;
        return intervalMs > 0 ? intervalMs : DefaultHeartbeatIntervalMs;
    }

    private static CommunicationConfig GetGlobalCommunicationConfig()
    {
        lock (_configLock)
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - _cachedCommunicationConfigAtUtc < ConfigRefreshInterval)
            {
                _cachedCommunicationConfig.Normalize();
                return _cachedCommunicationConfig;
            }

            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json, _configJsonOptions);
                    config?.Normalize();
                    if (config?.Communication != null)
                    {
                        _cachedCommunicationConfig = config.Communication;
                        _cachedCommunicationConfig.Normalize();
                    }
                }
            }
            catch (Exception ex)
            {
                _heartbeatLogger?.LogDebug(ex, "[PLC Config] Failed to load global config, using cached defaults.");
            }

            _cachedCommunicationConfigAtUtc = nowUtc;
            _cachedCommunicationConfig.Normalize();
            return _cachedCommunicationConfig;
        }
    }

    protected ushort GetReadElementCount(string dataType)
    {
        // 统一长度语义：ReadAsync 的 length 表示“元素个数/点数”
        // 算子单值读取固定读取 1 个元素，避免协议层重复按类型大小扩展导致过读。
        return 1;
    }

    /// <summary>
    /// 将字节数组转换为指定类型的值
    /// </summary>
    protected object ConvertBytesToValue(IPlcClient client, byte[] data, string dataType)
    {
        var transform = client.ByteTransform;

        return dataType.ToUpper() switch
        {
            "BIT" or "BOOL" => data[0] != 0,
            "BYTE" => data[0],
            "WORD" or "USHORT" => transform.ToUInt16(data, 0),
            "INT16" or "SHORT" => transform.ToInt16(data, 0),
            "DWORD" or "UINT" => transform.ToUInt32(data, 0),
            "INT32" or "INT" => transform.ToInt32(data, 0),
            "FLOAT" => transform.ToFloat(data, 0),
            "LWORD" or "ULONG" => transform.ToUInt64(data, 0),
            "INT64" or "LONG" => transform.ToInt64(data, 0),
            "DOUBLE" => transform.ToDouble(data, 0),
            "STRING" => System.Text.Encoding.ASCII.GetString(data).TrimEnd('\0'),
            _ => data
        };
    }

    /// <summary>
    /// 将值转换为字节数组
    /// </summary>
    protected byte[] ConvertValueToBytes(IPlcClient client, object value, string dataType)
    {
        var transform = client.ByteTransform;

        return dataType.ToUpper() switch
        {
            "BIT" or "BOOL" => new byte[] { Convert.ToBoolean(value) ? (byte)1 : (byte)0 },
            "BYTE" => new byte[] { Convert.ToByte(value) },
            "WORD" or "USHORT" => transform.GetBytes(Convert.ToUInt16(value)),
            "INT16" or "SHORT" => transform.GetBytes(Convert.ToInt16(value)),
            "DWORD" or "UINT" => transform.GetBytes(Convert.ToUInt32(value)),
            "INT32" or "INT" => transform.GetBytes(Convert.ToInt32(value)),
            "FLOAT" => transform.GetBytes(Convert.ToSingle(value)),
            "LWORD" or "ULONG" => transform.GetBytes(Convert.ToUInt64(value)),
            "INT64" or "LONG" => transform.GetBytes(Convert.ToInt64(value)),
            "DOUBLE" => transform.GetBytes(Convert.ToDouble(value)),
            "STRING" => System.Text.Encoding.ASCII.GetBytes(Convert.ToString(value) ?? ""),
            _ => throw new NotSupportedException($"不支持的数据类型: {dataType}")
        };
    }

    /// <summary>
    /// 创建成功的执行输出
    /// </summary>
    protected OperatorExecutionOutput CreateSuccessOutput(object value, string dataType)
    {
        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Value"] = value,
            ["DataType"] = dataType,
            ["Status"] = true,
            ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        });
    }

    protected static void AttachConnectionAuditInfo(OperatorExecutionOutput output, string connectionSource)
    {
        if (!output.IsSuccess)
        {
            return;
        }

        output.OutputData ??= new Dictionary<string, object>();
        output.OutputData["ConnectionSource"] = connectionSource;
    }

    /// <summary>
    /// 创建失败的执行输出
    /// </summary>
    protected OperatorExecutionOutput CreateFailureOutput(string errorMessage)
    {
        return OperatorExecutionOutput.Failure(errorMessage);
    }
}
