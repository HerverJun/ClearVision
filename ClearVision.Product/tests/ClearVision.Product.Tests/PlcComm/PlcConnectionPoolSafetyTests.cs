using ClearVision.PlcComm.Common;
using ClearVision.PlcComm.Core;
using ClearVision.PlcComm.Interfaces;
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
public sealed class PlcConnectionPoolSafetyTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        PlcCommunicationOperatorBase.StopHeartbeat();
        await PlcCommunicationOperatorBase.ResetConnectionPoolPolicyForTestingAsync();
    }

    public async Task DisposeAsync()
    {
        PlcCommunicationOperatorBase.StopHeartbeat();
        await PlcCommunicationOperatorBase.ResetConnectionPoolPolicyForTestingAsync();
    }

    [Fact]
    public async Task Capacity_ShouldEvictOldestUnleasedEntryAndExposeBoundedCounts()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-31T00:00:00Z"));
        await PlcCommunicationOperatorBase.ConfigureConnectionPoolForTestingAsync(
            capacity: 3,
            maxIdleConnectionAge: TimeSpan.FromHours(1),
            timeProvider: clock);
        var sut = new TestPlcOperator();
        var first = new FakePlcClient("profile-a");
        var second = new FakePlcClient("profile-b");
        var third = new FakePlcClient("profile-c");
        var fourth = new FakePlcClient("profile-d");

        await AcquireAndReleaseAsync(sut, "S7:profile-a", first);
        clock.Advance(TimeSpan.FromMinutes(1));
        await AcquireAndReleaseAsync(sut, "S7:profile-b", second);
        clock.Advance(TimeSpan.FromMinutes(1));
        await AcquireAndReleaseAsync(sut, "S7:profile-c", third);
        clock.Advance(TimeSpan.FromMinutes(1));
        await AcquireAndReleaseAsync(sut, "S7:profile-d", fourth);

        var snapshot = PlcCommunicationOperatorBase.GetConnectionPoolSnapshot();
        snapshot.Capacity.Should().Be(3);
        snapshot.PooledCount.Should().Be(3);
        snapshot.ConnectedCount.Should().Be(3);
        snapshot.ActiveLeaseCount.Should().Be(0);
        snapshot.CapacityEvictionCount.Should().Be(1);
        snapshot.ConnectionKeyLockCount.Should().Be(0);
        snapshot.ConnectionKeyLockReferenceCount.Should().Be(0);
        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(0);
        third.DisposeCount.Should().Be(0);
        fourth.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task Capacity_WhenEveryEntryIsLeased_ShouldFailClosedWithoutClosingLease()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-31T00:00:00Z"));
        await PlcCommunicationOperatorBase.ConfigureConnectionPoolForTestingAsync(
            capacity: 1,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            timeProvider: clock);
        var sut = new TestPlcOperator();
        var protectedClient = new FakePlcClient("profile-protected");
        var replacementClient = new FakePlcClient("profile-replacement");
        var protectedLease = await sut.AcquirePublicAsync(
            "S7:profile-protected",
            () => protectedClient);
        var rejectedFactoryCalls = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.AcquirePublicAsync(
                "S7:profile-rejected",
                () =>
                {
                    Interlocked.Increment(ref rejectedFactoryCalls);
                    return replacementClient;
                }));

        exception.Message.Should().StartWith("PLC_CONNECTION_POOL_CAPACITY_REACHED:");
        rejectedFactoryCalls.Should().Be(0);
        protectedClient.DisposeCount.Should().Be(0);
        PlcCommunicationOperatorBase.GetConnectionPoolSnapshot().ActiveLeaseCount.Should().Be(1);

        await protectedLease.DisposeAsync();
        await AcquireAndReleaseAsync(sut, "S7:profile-replacement", replacementClient);

        protectedClient.DisposeCount.Should().Be(1);
        PlcCommunicationOperatorBase.GetConnectionPoolSnapshot().PooledCount.Should().Be(1);
    }

    [Fact]
    public async Task Maintenance_ShouldRespectActiveLeaseThenRemoveIdleAndDisconnectedEntries()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-31T00:00:00Z"));
        await PlcCommunicationOperatorBase.ConfigureConnectionPoolForTestingAsync(
            capacity: 4,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            timeProvider: clock);
        var sut = new TestPlcOperator();
        var idleClient = new FakePlcClient("profile-idle");
        var activeLease = await sut.AcquirePublicAsync("MC:profile-idle", () => idleClient);

        clock.Advance(TimeSpan.FromMinutes(20));
        await PlcCommunicationOperatorBase.RunConnectionPoolMaintenanceAsync();

        idleClient.DisposeCount.Should().Be(0);
        PlcCommunicationOperatorBase.GetConnectionPoolSnapshot().PooledCount.Should().Be(1);

        await activeLease.DisposeAsync();
        await PlcCommunicationOperatorBase.RunConnectionPoolMaintenanceAsync();
        idleClient.DisposeCount.Should().Be(0, "lease release refreshes the idle deadline");

        clock.Advance(TimeSpan.FromMinutes(4));
        await PlcCommunicationOperatorBase.RunHeartbeatProbeOnceForTestingAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        await PlcCommunicationOperatorBase.RunConnectionPoolMaintenanceAsync();
        idleClient.DisposeCount.Should().Be(1);

        var disconnectedClient = new FakePlcClient("profile-disconnected");
        var disconnectedLease = await sut.AcquirePublicAsync(
            "FINS:profile-disconnected",
            () => disconnectedClient);
        disconnectedClient.MarkDisconnected();

        await PlcCommunicationOperatorBase.RunConnectionPoolMaintenanceAsync();

        var retiring = PlcCommunicationOperatorBase.GetConnectionPoolSnapshot();
        retiring.PooledCount.Should().Be(0);
        retiring.RetiringCount.Should().Be(1);
        retiring.ActiveLeaseCount.Should().Be(1);
        disconnectedClient.DisposeCount.Should().Be(0);

        await disconnectedLease.DisposeAsync();

        var completed = PlcCommunicationOperatorBase.GetConnectionPoolSnapshot();
        completed.RetiringCount.Should().Be(0);
        completed.ActiveLeaseCount.Should().Be(0);
        completed.IdleEvictionCount.Should().Be(1);
        completed.DisconnectedRemovalCount.Should().Be(1);
        disconnectedClient.DisconnectCount.Should().Be(1);
        disconnectedClient.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentSameProfile_ShouldConnectOnceAndReleaseAllKeyedLocks()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-31T00:00:00Z"));
        await PlcCommunicationOperatorBase.ConfigureConnectionPoolForTestingAsync(
            capacity: 4,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            timeProvider: clock);
        var sut = new TestPlcOperator();
        var connectEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakePlcClient(
            "profile-shared",
            async cancellationToken =>
            {
                connectEntered.TrySetResult();
                await releaseConnect.Task.WaitAsync(cancellationToken);
            });
        var factoryCalls = 0;

        var acquisitions = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => sut.AcquirePublicAsync(
                "S7:profile-shared",
                () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return client;
                })))
            .ToArray();

        try
        {
            await connectEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            SpinWait.SpinUntil(
                    () => PlcCommunicationOperatorBase.GetConnectionPoolSnapshot()
                        .ConnectionKeyLockReferenceCount == acquisitions.Length,
                    TimeSpan.FromSeconds(10))
                .Should().BeTrue();
        }
        finally
        {
            releaseConnect.TrySetResult();
        }

        var leases = await Task.WhenAll(acquisitions);
        try
        {
            factoryCalls.Should().Be(1);
            client.ConnectCount.Should().Be(1);
            leases.Should().OnlyContain(lease => ReferenceEquals(lease.Client, client));
            PlcCommunicationOperatorBase.GetConnectionPoolSnapshot().ActiveLeaseCount.Should().Be(16);
        }
        finally
        {
            await Task.WhenAll(leases.Select(lease => lease.DisposeAsync().AsTask()));
        }

        var completed = PlcCommunicationOperatorBase.GetConnectionPoolSnapshot();
        completed.PooledCount.Should().Be(1);
        completed.ActiveLeaseCount.Should().Be(0);
        completed.ConnectionKeyLockCount.Should().Be(0);
        completed.ConnectionKeyLockReferenceCount.Should().Be(0);
    }

    [Fact]
    public async Task PendingConnections_ShouldCountAgainstHardCapacity()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-31T00:00:00Z"));
        await PlcCommunicationOperatorBase.ConfigureConnectionPoolForTestingAsync(
            capacity: 2,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            timeProvider: clock);
        var sut = new TestPlcOperator();
        var bothEntered = new CountdownEvent(2);
        var releaseConnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePlcClient CreatePendingClient(string profile) => new(
            profile,
            async cancellationToken =>
            {
                bothEntered.Signal();
                await releaseConnect.Task.WaitAsync(cancellationToken);
            });
        var firstClient = CreatePendingClient("profile-one");
        var secondClient = CreatePendingClient("profile-two");
        var firstTask = sut.AcquirePublicAsync("S7:profile-one", () => firstClient);
        var secondTask = sut.AcquirePublicAsync("S7:profile-two", () => secondClient);

        var thirdFactoryCalls = 0;
        try
        {
            bothEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            var pending = PlcCommunicationOperatorBase.GetConnectionPoolSnapshot();
            pending.PendingConnectionCount.Should().Be(2);
            pending.PooledCount.Should().Be(0);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sut.AcquirePublicAsync(
                    "S7:profile-three",
                    () =>
                    {
                        Interlocked.Increment(ref thirdFactoryCalls);
                        return new FakePlcClient("profile-three");
                    }));
            exception.Message.Should().StartWith("PLC_CONNECTION_POOL_CAPACITY_REACHED:");
            thirdFactoryCalls.Should().Be(0);
        }
        finally
        {
            releaseConnect.TrySetResult();
        }

        var leases = await Task.WhenAll(firstTask, secondTask);
        await Task.WhenAll(leases.Select(lease => lease.DisposeAsync().AsTask()));

        var completed = PlcCommunicationOperatorBase.GetConnectionPoolSnapshot();
        completed.PooledCount.Should().Be(2);
        completed.PendingConnectionCount.Should().Be(0);
    }

    private static async Task AcquireAndReleaseAsync(
        TestPlcOperator sut,
        string connectionKey,
        FakePlcClient client)
    {
        var lease = await sut.AcquirePublicAsync(connectionKey, () => client);
        await lease.DisposeAsync();
    }

    private sealed class TestPlcOperator : PlcCommunicationOperatorBase
    {
        public TestPlcOperator()
            : base(NullLogger.Instance)
        {
            StopHeartbeat();
        }

        public override OperatorType OperatorType => OperatorType.SiemensS7Communication;

        public Task<PooledPlcConnectionLease> AcquirePublicAsync(
            string connectionKey,
            Func<IPlcClient> factory,
            CancellationToken cancellationToken = default) =>
            AcquireConnectionLeaseAsync(connectionKey, factory, cancellationToken);

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override ValidationResult ValidateParameters(Operator @operator) => ValidationResult.Valid();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount)
        {
            _utcNow += amount;
        }
    }

    private sealed class FakePlcClient : IPlcClient
    {
        private readonly Func<CancellationToken, Task>? _beforeConnectAsync;
        private int _isConnected;
        private int _connectCount;
        private int _disconnectCount;
        private int _disposeCount;

        public FakePlcClient(
            string profileName,
            Func<CancellationToken, Task>? beforeConnectAsync = null)
        {
            IpAddress = profileName;
            _beforeConnectAsync = beforeConnectAsync;
        }

        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int DisconnectCount => Volatile.Read(ref _disconnectCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public string IpAddress { get; }
        public int Port => 0;
        public bool IsConnected => Volatile.Read(ref _isConnected) != 0;
        public int ConnectTimeout { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public ReconnectPolicy ReconnectPolicy { get; set; } = new();
        public IByteTransform ByteTransform => LittleEndianTransform.Instance;
        public event EventHandler<ConnectionEventArgs>? Connected { add { } remove { } }
        public event EventHandler<DisconnectionEventArgs>? Disconnected { add { } remove { } }
        public event EventHandler<PlcErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _connectCount);
            if (_beforeConnectAsync != null)
            {
                await _beforeConnectAsync(ct);
            }

            Volatile.Write(ref _isConnected, 1);
            return true;
        }

        public Task DisconnectAsync()
        {
            Interlocked.Increment(ref _disconnectCount);
            Volatile.Write(ref _isConnected, 0);
            return Task.CompletedTask;
        }

        public void MarkDisconnected() => Volatile.Write(ref _isConnected, 0);

        public Task<OperateResult<byte[]>> ReadAsync(string address, ushort length, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperateResult> WriteAsync(string address, byte[] value, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperateResult<T>> ReadAsync<T>(string address, CancellationToken ct = default) where T : struct =>
            throw new NotSupportedException();

        public Task<OperateResult> WriteAsync<T>(string address, T value, CancellationToken ct = default) where T : struct =>
            throw new NotSupportedException();

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

        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(IsConnected);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            Volatile.Write(ref _isConnected, 0);
        }
    }
}
