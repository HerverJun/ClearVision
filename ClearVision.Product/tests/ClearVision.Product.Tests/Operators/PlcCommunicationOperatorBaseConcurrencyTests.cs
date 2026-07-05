using System.Collections;
using System.Diagnostics;
using System.Reflection;
using ClearVision.PlcComm.Common;
using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Interfaces;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Operators;

[Collection(RuntimeConcurrencyCollection.Name)]
public class PlcCommunicationOperatorBaseConcurrencyTests
{
    [Fact]
    public async Task GetOrCreateConnectionAsync_DifferentKeys_ShouldNotBeBlockedBySlowConnectUnderGlobalPoolLock()
    {
        ResetStaticConnectionState();
        var sut = new TestPlcOperator();

        var slowConnectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowClient = new DelayedConnectPlcClient(
            connectDelay: TimeSpan.FromMilliseconds(700),
            onConnectStart: () => slowConnectStarted.TrySetResult());
        var fastClient = new DelayedConnectPlcClient(connectDelay: TimeSpan.FromMilliseconds(80));

        var slowTask = sut.GetOrCreateConnectionPublicAsync("S7:192.168.0.1:102", () => slowClient);
        await slowConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var sw = Stopwatch.StartNew();
        var fastResult = await sut.GetOrCreateConnectionPublicAsync("S7:192.168.0.2:102", () => fastClient)
            .WaitAsync(TimeSpan.FromSeconds(2));
        sw.Stop();

        fastResult.isNewConnection.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(350));

        _ = await slowTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task GetOrCreateConnectionAsync_SameKeyConcurrentCalls_ShouldCreateOnceAndReuse()
    {
        ResetStaticConnectionState();
        var sut = new TestPlcOperator();

        var factoryCalls = 0;
        var connectCalls = 0;
        Func<IPlcClient> factory = () =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new DelayedConnectPlcClient(
                connectDelay: TimeSpan.FromMilliseconds(150),
                onConnectStart: () => Interlocked.Increment(ref connectCalls));
        };

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                await startGate.Task;
                return await sut.GetOrCreateConnectionPublicAsync("MC:192.168.1.10:5002", factory);
            }))
            .ToArray();

        startGate.TrySetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        factoryCalls.Should().Be(1);
        connectCalls.Should().Be(1);
        results.Count(static r => r.isNewConnection).Should().Be(1);

        var firstClient = results[0].client;
        results.Should().OnlyContain(r => ReferenceEquals(r.client, firstClient));
    }

    [Fact]
    public async Task GetOrCreateConnectionAsync_ManyDistinctKeys_ShouldNotRetainKeyLocks()
    {
        ResetStaticConnectionState();
        var sut = new TestPlcOperator();

        try
        {
            for (var i = 0; i < 64; i++)
            {
                var key = $"S7:192.168.10.{i}:102";
                _ = await sut.GetOrCreateConnectionPublicAsync(
                    key,
                    () => new DelayedConnectPlcClient(TimeSpan.FromMilliseconds(1)));
            }

            GetConnectionKeyLockCount().Should().Be(0);
        }
        finally
        {
            ResetStaticConnectionState();
        }
    }

    [Fact]
    public async Task ExecuteWithConnectionOperationLockAsync_SameKey_ShouldSerializeOperations()
    {
        ResetStaticConnectionState();
        var sut = new TestPlcOperator();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeOperations = 0;
        var maxConcurrentOperations = 0;

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                await startGate.Task;
                await sut.ExecuteWithConnectionOperationLockPublicAsync(
                    "S7:192.168.2.10:102",
                    async () =>
                    {
                        var current = Interlocked.Increment(ref activeOperations);
                        UpdateMax(ref maxConcurrentOperations, current);
                        await Task.Delay(30);
                        Interlocked.Decrement(ref activeOperations);
                    });
            }))
            .ToArray();

        startGate.TrySetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        maxConcurrentOperations.Should().Be(1);
        GetOperationLockCount().Should().Be(0);
    }

    [Fact]
    public async Task PingSnapshotAsync_ManySlowConnections_ShouldUseBoundedConcurrency()
    {
        ResetStaticConnectionState();
        var probe = new PingConcurrencyProbe();
        var snapshot = Enumerable.Range(0, 8)
            .Select(index => new KeyValuePair<string, IPlcClient>(
                $"S7:192.168.3.{index}:102",
                new SlowPingPlcClient(TimeSpan.FromMilliseconds(200), probe)))
            .ToArray();

        try
        {
            await InvokePingSnapshotAsync(snapshot, CancellationToken.None);

            probe.MaxActive.Should().BeGreaterThan(1);
            probe.MaxActive.Should().BeLessThanOrEqualTo(GetPrivateStaticInt("MaxHeartbeatConcurrency"));
            probe.PingCount.Should().Be(snapshot.Length);
            GetOperationLockCount().Should().Be(0);
        }
        finally
        {
            ResetStaticConnectionState();
        }
    }

    [Fact]
    public async Task GetConnectionStateSnapshot_WhenPoolLockIsBusy_ShouldReturnLastKnownStateWithoutBlocking()
    {
        ResetStaticConnectionState();
        const string key = "S7:192.168.4.10:102";
        SetLastKnownState(key, true);
        var poolLock = GetPoolLock();

        await poolLock.WaitAsync();
        try
        {
            var sw = Stopwatch.StartNew();
            var snapshot = PlcCommunicationOperatorBase.GetConnectionStateSnapshot();
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(40));
            snapshot.Should().ContainKey(key);
            snapshot[key].Should().BeTrue();
        }
        finally
        {
            poolLock.Release();
            ResetStaticConnectionState();
        }
    }

    [Fact]
    public async Task StartHeartbeat_WhenCalledConcurrently_ShouldCreateSingleHeartbeatTask()
    {
        PlcCommunicationOperatorBase.StopHeartbeat();
        try
        {
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(async () =>
                {
                    await startGate.Task;
                    PlcCommunicationOperatorBase.StartHeartbeat();
                    return GetHeartbeatTask();
                }))
                .ToArray();

            startGate.TrySetResult();
            var observedTasks = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
            var heartbeatTask = GetHeartbeatTask();

            heartbeatTask.Should().NotBeNull();
            observedTasks.Should().OnlyContain(task => ReferenceEquals(task, heartbeatTask));
        }
        finally
        {
            PlcCommunicationOperatorBase.StopHeartbeat();
        }
    }

    private static void ResetStaticConnectionState()
    {
        var poolField = typeof(PlcCommunicationOperatorBase).GetField("_connectionPool", BindingFlags.Static | BindingFlags.NonPublic);
        var stateField = typeof(PlcCommunicationOperatorBase).GetField("_lastKnownState", BindingFlags.Static | BindingFlags.NonPublic);
        var keyLocksField = typeof(PlcCommunicationOperatorBase).GetField("_connectionKeyLocks", BindingFlags.Static | BindingFlags.NonPublic);
        var operationLocksField = typeof(PlcCommunicationOperatorBase).GetField("_operationLocks", BindingFlags.Static | BindingFlags.NonPublic);

        if (poolField?.GetValue(null) is IDictionary pool)
        {
            foreach (DictionaryEntry entry in pool)
            {
                if (entry.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            pool.Clear();
        }

        if (stateField?.GetValue(null) is IDictionary state)
        {
            state.Clear();
        }

        if (keyLocksField?.GetValue(null) is IDictionary keyLocks)
        {
            foreach (DictionaryEntry entry in keyLocks)
            {
                if (entry.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            keyLocks.Clear();
        }

        if (operationLocksField?.GetValue(null) is IDictionary operationLocks)
        {
            foreach (DictionaryEntry entry in operationLocks)
            {
                if (entry.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            operationLocks.Clear();
        }
    }

    private static Task? GetHeartbeatTask()
    {
        var heartbeatTaskField = typeof(PlcCommunicationOperatorBase).GetField("_heartbeatTask", BindingFlags.Static | BindingFlags.NonPublic);
        heartbeatTaskField.Should().NotBeNull();
        return heartbeatTaskField!.GetValue(null) as Task;
    }

    private static SemaphoreSlim GetPoolLock()
    {
        var poolLockField = typeof(PlcCommunicationOperatorBase).GetField("_poolLock", BindingFlags.Static | BindingFlags.NonPublic);
        poolLockField.Should().NotBeNull();
        return (SemaphoreSlim)poolLockField!.GetValue(null)!;
    }

    private static void SetLastKnownState(string key, bool value)
    {
        var stateField = typeof(PlcCommunicationOperatorBase).GetField("_lastKnownState", BindingFlags.Static | BindingFlags.NonPublic);
        stateField.Should().NotBeNull();

        var state = stateField!.GetValue(null)!;
        var indexer = state.GetType().GetProperty("Item");
        indexer.Should().NotBeNull();
        indexer!.SetValue(state, value, new object[] { key });
    }

    private static async Task InvokePingSnapshotAsync(KeyValuePair<string, IPlcClient>[] snapshot, CancellationToken cancellationToken)
    {
        var method = typeof(PlcCommunicationOperatorBase).GetMethod("PingSnapshotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, new object[] { snapshot, cancellationToken })!;
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static int GetPrivateStaticInt(string name)
    {
        var field = typeof(PlcCommunicationOperatorBase).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.IsLiteral
            ? (int)field.GetRawConstantValue()!
            : (int)field.GetValue(null)!;
    }

    private static int GetConnectionKeyLockCount()
    {
        var keyLocksField = typeof(PlcCommunicationOperatorBase).GetField("_connectionKeyLocks", BindingFlags.Static | BindingFlags.NonPublic);
        keyLocksField.Should().NotBeNull();

        var keyLocks = keyLocksField!.GetValue(null) as IDictionary;
        keyLocks.Should().NotBeNull();
        return keyLocks!.Count;
    }

    private static int GetOperationLockCount()
    {
        var operationLocksField = typeof(PlcCommunicationOperatorBase).GetField("_operationLocks", BindingFlags.Static | BindingFlags.NonPublic);
        operationLocksField.Should().NotBeNull();

        var operationLocks = operationLocksField!.GetValue(null) as IDictionary;
        operationLocks.Should().NotBeNull();
        return operationLocks!.Count;
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }

    private sealed class TestPlcOperator : PlcCommunicationOperatorBase
    {
        public TestPlcOperator() : base(NullLogger.Instance)
        {
        }

        public override OperatorType OperatorType => OperatorType.SiemensS7Communication;

        public Task<(IPlcClient client, bool isNewConnection)> GetOrCreateConnectionPublicAsync(
            string connectionKey,
            Func<IPlcClient> factory)
        {
            return GetOrCreateConnectionAsync(connectionKey, factory);
        }

        public Task ExecuteWithConnectionOperationLockPublicAsync(
            string connectionKey,
            Func<Task> operation)
        {
            return ExecuteWithConnectionOperationLockAsync(
                connectionKey,
                async () =>
                {
                    await operation();
                    return true;
                },
                CancellationToken.None);
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class DelayedConnectPlcClient : IPlcClient
    {
        private readonly TimeSpan _connectDelay;
        private readonly Action? _onConnectStart;

        public DelayedConnectPlcClient(TimeSpan connectDelay, Action? onConnectStart = null)
        {
            _connectDelay = connectDelay;
            _onConnectStart = onConnectStart;
        }

        public string IpAddress => "127.0.0.1";
        public int Port => 0;
        public bool IsConnected { get; private set; }
        public int ConnectTimeout { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public ReconnectPolicy ReconnectPolicy { get; set; } = new();
        public IByteTransform ByteTransform { get; } = BigEndianTransform.Instance;

        public event EventHandler<ConnectionEventArgs>? Connected { add { } remove { } }
        public event EventHandler<DisconnectionEventArgs>? Disconnected { add { } remove { } }
        public event EventHandler<PlcErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            _onConnectStart?.Invoke();
            await Task.Delay(_connectDelay, ct);
            IsConnected = true;
            return true;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<OperateResult<byte[]>> ReadAsync(string address, ushort length, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult> WriteAsync(string address, byte[] value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult<T>> ReadAsync<T>(string address, CancellationToken ct = default) where T : struct => throw new NotSupportedException();
        public Task<OperateResult> WriteAsync<T>(string address, T value, CancellationToken ct = default) where T : struct => throw new NotSupportedException();
        public Task<OperateResult<Dictionary<string, byte[]>>> ReadBatchAsync(string[] addresses, ushort[] lengths, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult<string>> ReadStringAsync(string address, ushort length, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult> WriteStringAsync(string address, string value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(IsConnected);

        public void Dispose()
        {
        }
    }

    private sealed class PingConcurrencyProbe
    {
        private int _active;
        private int _maxActive;
        private int _pingCount;

        public int MaxActive => Volatile.Read(ref _maxActive);

        public int PingCount => Volatile.Read(ref _pingCount);

        public int Enter()
        {
            Interlocked.Increment(ref _pingCount);
            var active = Interlocked.Increment(ref _active);
            UpdateMax(ref _maxActive, active);
            return active;
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _active);
        }
    }

    private sealed class SlowPingPlcClient : IPlcClient
    {
        private readonly TimeSpan _delay;
        private readonly PingConcurrencyProbe _probe;

        public SlowPingPlcClient(TimeSpan delay, PingConcurrencyProbe probe)
        {
            _delay = delay;
            _probe = probe;
        }

        public string IpAddress => "127.0.0.1";
        public int Port => 0;
        public bool IsConnected => true;
        public int ConnectTimeout { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public ReconnectPolicy ReconnectPolicy { get; set; } = new();
        public IByteTransform ByteTransform { get; } = BigEndianTransform.Instance;

        public event EventHandler<ConnectionEventArgs>? Connected { add { } remove { } }
        public event EventHandler<DisconnectionEventArgs>? Disconnected { add { } remove { } }
        public event EventHandler<PlcErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OperateResult<byte[]>> ReadAsync(string address, ushort length, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult> WriteAsync(string address, byte[] value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult<T>> ReadAsync<T>(string address, CancellationToken ct = default) where T : struct => throw new NotSupportedException();
        public Task<OperateResult> WriteAsync<T>(string address, T value, CancellationToken ct = default) where T : struct => throw new NotSupportedException();
        public Task<OperateResult<Dictionary<string, byte[]>>> ReadBatchAsync(string[] addresses, ushort[] lengths, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult<string>> ReadStringAsync(string address, ushort length, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperateResult> WriteStringAsync(string address, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(_delay, ct);
                return true;
            }
            finally
            {
                _probe.Exit();
            }
        }

        public void Dispose()
        {
        }
    }
}
